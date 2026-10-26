using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>One mana type (#145): a data row — id, display name, color. Types are referenced by id everywhere, never an enum.</summary>
[Serializable]
public class ManaTypeDefinition
{
    public string id;
    public string displayName;
    public Color color;

    public ManaTypeDefinition(string id, string displayName, Color color)
    {
        this.id = id;
        this.displayName = displayName;
        this.color = color;
    }
}

/// <summary>
/// A mana point of interest (#145, rows since #150): a world feature on a tile that emits one unit of its type every
/// emitSeconds. A data row — the Fire Vent and the Spring are two rows of the one mechanism. The timer and the last
/// dormant reading are runtime state, never serialized.
/// </summary>
[Serializable]
public class ManaPointOfInterest
{
    public string displayName;
    public Vector2Int tile;
    public string manaType;
    public float emitSeconds;

    [NonSerialized] public float emitTimer;
    [NonSerialized] public bool wasEmitting; // Starts false: a fresh point reads dormant until it first can emit

    public ManaPointOfInterest(string displayName, Vector2Int tile, string manaType, float emitSeconds)
    {
        this.displayName = displayName;
        this.tile = tile;
        this.manaType = manaType;
        this.emitSeconds = emitSeconds;
    }
}

/// <summary>
/// A single-type mana store (#145): the accumulator's buffer and the container's bulk store. Empty, it holds no type; the
/// first deposit adopts its type; it then accepts only that type; withdrawing to zero forgets the type. Deposits clamp at
/// capacity and withdrawals at the amount, and both report what actually moved. Plain data, so tests drive it directly.
/// </summary>
public class ManaStore
{
    public string Type { get; private set; }
    public int Amount { get; private set; }
    public int Capacity { get; private set; }

    public ManaStore(int capacity) { Capacity = Mathf.Max(0, capacity); }

    public bool IsEmpty => Amount == 0;
    public bool IsFull => Amount >= Capacity;
    public int Room => Mathf.Max(0, Capacity - Amount);

    /// <summary>Whether a unit of this type could go in now: room, and empty or already holding this type.</summary>
    public bool CanAccept(string type) => !string.IsNullOrEmpty(type) && Room > 0 && (Type == null || Type == type);

    /// <summary>Deposits up to amount of type; returns how many went in (0 for a mismatched type or no room).</summary>
    public int Deposit(string type, int amount)
    {
        if (amount <= 0 || !CanAccept(type)) return 0;
        int moved = Mathf.Min(amount, Room);
        Type = type;
        Amount += moved;
        return moved;
    }

    /// <summary>Withdraws up to amount; returns how many came out. Emptying it forgets the type.</summary>
    public int Withdraw(int amount)
    {
        int moved = Mathf.Clamp(amount, 0, Amount);
        Amount -= moved;
        if (Amount == 0) Type = null;
        return moved;
    }
}

/// <summary>How a store meets a pipe (#145): accumulators only give, containers take and give, sinks (later) only take.</summary>
public enum ManaPortRole
{
    Output,
    InputOutput,
    Input
}

/// <summary>A building that holds mana and can attach to a pipe by its own tile (#145).</summary>
public interface IManaPort
{
    Vector2Int Tile { get; }
    ManaPortRole Role { get; }
    ManaStore Store { get; }
}

/// <summary>
/// The mana simulation (#145), a service on the DungeonManager. Mana is discrete: units on tiles, grid data, never
/// GameObjects. A tile holds at most maxManaPerTile units of one type; Floor and Entrance are open, Rock and Designated
/// are walls (placeables and creatures never block). Fog never gates the simulation.
/// Points of interest are data rows (#150: the Fire Vent and the Spring): each sits on its tile (carved to Floor at
/// setup, entombed in rock until dug out) and every emitSeconds emits one unit of its type onto the first 4-neighbor
/// (board scan order) that has room, dormant while none has (entombed, or saturated), resuming on its own the moment one
/// does. The flow is type-uniform: every type obeys the same rules. Flow is pressure, like a gas:
/// a tile's pressure is the units on it. Every flowTickSeconds each tile holding mana, in a shuffled order and seeing
/// moves already made, sends one unit to a neighbor that holds fewer units and has room, picked at random from those
/// (seeded by flowSeed, so a run replays exactly). So mana always flows from high pressure to low; a closed space evens
/// out until every tile is within one unit of the rest — the remainder keeps juggling, the counts never change — and
/// with a point emitting, every connected tile ends at the cap and the flow stops. Nothing pulls: an accumulator takes
/// the units beside it and the flow refills the dip.
/// Stores register here (the PowerNode pattern): TotalOf sums a type across them and a type is discovered, for the
/// session, the first time any store holds it. Accumulators collect from beside them on their own cycle, driven by this
/// clock. Pipes are conduit's twin (pending / built, imp-built over pipeSecondsPerTile, explored Floor only); a network is
/// a connected run of built pipe, and a building attaches only through pipe on its own tile. The transfer pass runs every
/// transferSeconds per network, under a shared per-network throughput with an exact fractional carry: (#146) process hosts
/// draw their demand first (from containers, then accumulator buffers above accumulatorReserve), then deposit held
/// product into containers, then accumulators push what's above the reserve into containers with room. Pipes never pool,
/// and buildings never touch-transfer.
/// The process table (#146) holds the rows hosts run (inputs and outputs by type, cycle seconds), keyed by host type.
/// Types change in exactly two places: inside a running host cycle (the controlled route, the inputs × the row's host
/// multiplier) and where a row's inputs collide on the map (the wild route, CombineStep, after every flow tick). Never in
/// pipes, never in stores.
/// </summary>
[DisallowMultipleComponent]
public class ManaFlow : MonoBehaviour
{
    public const string FireId = "Fire";
    public const string WaterId = "Water";
    public const string AirId = "Air";             // #146: made, not found — the chamber's products
    public const string StormId = "Storm";
    public const string LightningId = "Lightning";
    public const string FireVentName = "Fire Vent";
    public const string SpringName = "Spring";

    /// <summary>What a pipe drag does: lay pending pipe, or remove it.</summary>
    public enum PipeMode { Lay, Remove }

    [Header("References")]
    [SerializeField] private DungeonBoard dungeonBoard;

    // The catalog home for mana types: append-only rows, referenced by id.
    [Header("Mana Types")]
    [SerializeField] private List<ManaTypeDefinition> manaTypes = new List<ManaTypeDefinition>
    {
        new ManaTypeDefinition(FireId, "Fire Mana", new Color(1.00f, 0.42f, 0.12f, 1f)),
        new ManaTypeDefinition(WaterId, "Water Mana", new Color(0.25f, 0.60f, 1.00f, 1f)),
        new ManaTypeDefinition(AirId, "Air Mana", new Color(0.82f, 0.94f, 0.96f, 1f)),             // #146: pale
        new ManaTypeDefinition(StormId, "Storm Mana", new Color(0.52f, 0.48f, 0.66f, 1f)),         // Grey-violet
        new ManaTypeDefinition(LightningId, "Lightning Mana", new Color(1.00f, 0.92f, 0.25f, 1f)), // Bright yellow
    };

    // The process table (#146): inputs + cycle → outputs, keyed by host. The chamber is the pilot host; later hosts add rows.
    // Inputs are the map amounts (3 + 3 → 1 where fronts collide); the chamber takes hostInputMultiplier (×2) of them.
    // The single-input Storm row doesn't combine on the map: with nothing to collide with, Storm would decay on its own.
    [Header("Processes")]
    [SerializeField] private List<ManaProcessRow> processes = new List<ManaProcessRow>
    {
        new ManaProcessRow(PlaceableType.CombinationChamber,
            new[] { new ManaAmount(FireId, 3), new ManaAmount(WaterId, 3) }, new[] { new ManaAmount(AirId, 1) }, 10f),
        new ManaProcessRow(PlaceableType.CombinationChamber,
            new[] { new ManaAmount(AirId, 3), new ManaAmount(WaterId, 3) }, new[] { new ManaAmount(StormId, 1) }, 10f),
        new ManaProcessRow(PlaceableType.CombinationChamber,
            new[] { new ManaAmount(StormId, 3) }, new[] { new ManaAmount(LightningId, 1) }, 10f, combinesOnMap: false),
    };

    [Header("Occupancy and Flow")]
    [SerializeField] private int maxManaPerTile = 5;         // A tile's pressure tops out here
    [SerializeField] private float flowTickSeconds = 0.5f;
    [SerializeField] private int flowSeed = 145;             // Seeds the flow's random picks: the same seed replays the same run

    // The world's mana sources (#150): one row per point, each in its own sealed rock pocket.
    [Header("Points of Interest")]
    [SerializeField] private List<ManaPointOfInterest> pointsOfInterest = new List<ManaPointOfInterest>
    {
        new ManaPointOfInterest(FireVentName, new Vector2Int(24, 5), FireId, 2f), // Deep in the rock south of the starter cavern
        new ManaPointOfInterest(SpringName, new Vector2Int(20, 27), WaterId, 2f), // Shallow, west of the entrance column
    };

    [Header("Pipes")]
    [SerializeField] private float pipeSecondsPerTile = 0.5f;      // Imp work to build one pending pipe tile
    [SerializeField] private float pipeThroughputPerSecond = 5f;   // Per network, shared across every type on it
    [SerializeField] private float transferSeconds = 0.25f;        // The transfer pass's cadence
    [SerializeField] private int accumulatorReserve = 5;           // An accumulator keeps this much; only the rest overflows

    public DungeonBoard Board { get => dungeonBoard; set => dungeonBoard = value; }
    public int MaxManaPerTile { get => maxManaPerTile; set => maxManaPerTile = Mathf.Max(1, value); }
    public float FlowTickSeconds { get => flowTickSeconds; set => flowTickSeconds = value; }

    /// <summary>The flow's seed. Setting it restarts the random sequence from that seed.</summary>
    public int FlowSeed
    {
        get => flowSeed;
        set
        {
            flowSeed = value;
            flowRandom = null;
        }
    }
    public IReadOnlyList<ManaPointOfInterest> PointsOfInterest => pointsOfInterest;
    public float PipeSecondsPerTile { get => pipeSecondsPerTile; set => pipeSecondsPerTile = value; }
    public float PipeThroughputPerSecond { get => pipeThroughputPerSecond; set => pipeThroughputPerSecond = value; }
    public float TransferSeconds { get => transferSeconds; set => transferSeconds = value; }
    public int AccumulatorReserve { get => accumulatorReserve; set => accumulatorReserve = Mathf.Max(0, value); }
    public IReadOnlyList<ManaTypeDefinition> ManaTypes => manaTypes;

    /// <summary>The process rows, in table order (replaceable in code, e.g. by tests).</summary>
    public List<ManaProcessRow> Processes { get => processes; set => processes = value ?? new List<ManaProcessRow>(); }

    /// <summary>Game seconds this simulation has run (the clock accumulators time their recent collection by).</summary>
    public float ElapsedSeconds { get; private set; }

    /// <summary>Raised when a tile's mana changes (a unit arrives or leaves), so the board view restyles it.</summary>
    public event Action<Vector2Int> ManaChanged;

    /// <summary>Raised when a tile's pipe changes (laid, built or removed).</summary>
    public event Action<Vector2Int> PipeChanged;

    /// <summary>Raised with a point of interest's tile when its state changed (went dormant, or woke).</summary>
    public event Action<Vector2Int> PointOfInterestChanged;

    private struct Cell
    {
        public string type;
        public int count;
    }

    private readonly Dictionary<Vector2Int, Cell> cells = new Dictionary<Vector2Int, Cell>();
    private readonly List<IManaPort> ports = new List<IManaPort>();
    private readonly List<IManaProcessorHost> processors = new List<IManaProcessorHost>(); // #146
    private readonly HashSet<string> discovered = new HashSet<string>();
    private readonly HashSet<Vector2Int> pendingPipe = new HashSet<Vector2Int>();
    private readonly HashSet<Vector2Int> builtPipe = new HashSet<Vector2Int>();
    private readonly Dictionary<Vector2Int, float> pipeProgress = new Dictionary<Vector2Int, float>();
    private Dictionary<Vector2Int, int> networks = new Dictionary<Vector2Int, int>();
    private readonly Dictionary<int, float> networkCarry = new Dictionary<int, float>();
    private bool networksDirty;
    private float flowTimer;
    private float transferTimer;
    private System.Random flowRandom; // Made from flowSeed on the first flow tick

    private const float CompleteTolerance = 1e-4f; // Pipe progress sums deltaTime steps: absorb float rounding

    // Pathfinder's direction order: right, up, left, down.
    private static readonly Vector2Int[] Neighbors =
    {
        new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(-1, 0), new Vector2Int(0, -1),
    };

    private void Awake()
    {
        dungeonBoard ??= GetComponent<DungeonBoard>();
    }

    private void Start()
    {
        dungeonBoard ??= FindAnyObjectByType<DungeonBoard>();
        CarvePointsOfInterest();
    }

    private void Update()
    {
        Tick(Time.deltaTime);
    }

    // ---- the catalog ----

    public bool TryGetType(string id, out ManaTypeDefinition definition)
    {
        if (manaTypes != null)
        {
            foreach (ManaTypeDefinition d in manaTypes)
            {
                if (d == null || d.id != id) continue;
                definition = d;
                return true;
            }
        }
        definition = null;
        return false;
    }

    /// <summary>A type's display name ("Fire Mana"); the id itself for a type with no row.</summary>
    public string DisplayName(string id) => TryGetType(id, out ManaTypeDefinition d) ? d.displayName : id ?? "";

    /// <summary>A type's color; white for a type with no row.</summary>
    public Color ColorOf(string id) => TryGetType(id, out ManaTypeDefinition d) ? d.color : Color.white;

    // ---- points of interest ----

    /// <summary>Removes every point of interest (tests and staging build their own).</summary>
    public void ClearPointsOfInterest() => pointsOfInterest.Clear();

    /// <summary>Adds a point of interest row and returns it. Its tile isn't carved until CarvePointsOfInterest.</summary>
    public ManaPointOfInterest AddPointOfInterest(string displayName, Vector2Int tile, string manaType, float emitSeconds)
    {
        var point = new ManaPointOfInterest(displayName, tile, manaType, emitSeconds);
        pointsOfInterest.Add(point);
        return point;
    }

    /// <summary>
    /// Carves each point's own tile to Floor (its pocket stays sealed: the tiles around it are untouched, so it starts
    /// entombed and dormant). Start runs it; idempotent; a no-op with no board.
    /// </summary>
    public void CarvePointsOfInterest()
    {
        if (dungeonBoard == null) return;
        foreach (ManaPointOfInterest point in pointsOfInterest)
        {
            if (point == null || !dungeonBoard.IsInBounds(point.tile.x, point.tile.y)) continue;
            if (dungeonBoard.GetTile(point.tile.x, point.tile.y) != TileState.Floor)
                dungeonBoard.SetTile(point.tile.x, point.tile.y, TileState.Floor);
        }
    }

    /// <summary>The point of interest on this tile, or null.</summary>
    public ManaPointOfInterest PointOfInterestAt(Vector2Int tile)
    {
        foreach (ManaPointOfInterest point in pointsOfInterest)
            if (point != null && point.tile == tile) return point;
        return null;
    }

    /// <summary>Whether a point of interest stands on this tile.</summary>
    public bool IsPointOfInterestTile(Vector2Int tile) => PointOfInterestAt(tile) != null;

    /// <summary>True while the point can't emit: no 4-neighbor has room for a unit of its type (entombed or saturated).</summary>
    public bool IsDormant(ManaPointOfInterest point) => !TryFindEmitTile(point, out _);

    /// <summary>Where the point emits next: the first of its 4-neighbors, in board scan order, with room for its type.</summary>
    public bool TryFindEmitTile(ManaPointOfInterest point, out Vector2Int tile)
    {
        tile = default;
        if (point == null || dungeonBoard == null) return false;
        foreach (Vector2Int candidate in ScanOrderedNeighbors(point.tile))
        {
            if (!CanAccept(candidate, point.manaType)) continue;
            tile = candidate;
            return true;
        }
        return false;
    }

    // The 4-neighbors in board scan order (x, then y): left, down, up, right.
    private static IEnumerable<Vector2Int> ScanOrderedNeighbors(Vector2Int tile)
    {
        yield return tile + new Vector2Int(-1, 0);
        yield return tile + new Vector2Int(0, -1);
        yield return tile + new Vector2Int(0, 1);
        yield return tile + new Vector2Int(1, 0);
    }

    // ---- occupancy ----

    /// <summary>
    /// Open to mana: in-bounds Floor or Entrance. Rock and Designated are walls; placeables never block. A point of
    /// interest's own tile is never open: the point stands there and emits beside it.
    /// </summary>
    public bool IsOpen(Vector2Int tile) =>
        dungeonBoard != null && dungeonBoard.IsInBounds(tile.x, tile.y) && !IsPointOfInterestTile(tile)
        && IsOpenState(dungeonBoard.GetTile(tile.x, tile.y));

    public static bool IsOpenState(TileState state) => state == TileState.Floor || state == TileState.Entrance;

    /// <summary>Units on a tile (0 for none).</summary>
    public int UnitsAt(Vector2Int tile) => cells.TryGetValue(tile, out Cell c) ? c.count : 0;

    /// <summary>The type on a tile, or null when it holds none.</summary>
    public string TypeAt(Vector2Int tile) => cells.TryGetValue(tile, out Cell c) ? c.type : null;

    /// <summary>Whether a unit of this type can go onto the tile: open, and under the cap with no other type there.</summary>
    public bool CanAccept(Vector2Int tile, string type) =>
        IsOpen(tile) && CanStack(UnitsAt(tile), TypeAt(tile), type, maxManaPerTile);

    /// <summary>The per-tile cap rule: under maxPerTile, and empty or holding the same type (one type per tile).</summary>
    public static bool CanStack(int units, string typeThere, string type, int maxPerTile) =>
        units < maxPerTile && (units == 0 || typeThere == type);

    /// <summary>Every tile holding mana, in board scan order.</summary>
    public List<Vector2Int> OccupiedTiles() => ScanOrdered(cells.Keys);

    /// <summary>Units on the board, all types.</summary>
    public int UnitCount
    {
        get
        {
            int n = 0;
            foreach (Cell c in cells.Values) n += c.count;
            return n;
        }
    }

    /// <summary>Puts a unit on a tile if it can go there (emission, staging, tests). True when it went down.</summary>
    public bool TryAddUnit(Vector2Int tile, string type)
    {
        if (!CanAccept(tile, type)) return false;
        cells[tile] = new Cell { type = type, count = UnitsAt(tile) + 1 };
        ManaChanged?.Invoke(tile);
        return true;
    }

    /// <summary>Takes one unit off a tile (collection). False when it holds none.</summary>
    public bool TryTakeUnit(Vector2Int tile, out string type)
    {
        type = null;
        if (!cells.TryGetValue(tile, out Cell c) || c.count <= 0) return false;
        type = c.type;
        if (c.count == 1) cells.Remove(tile);
        else cells[tile] = new Cell { type = c.type, count = c.count - 1 };
        ManaChanged?.Invoke(tile);
        return true;
    }

    // ---- the clock ----

    /// <summary>
    /// Advances the simulation by deltaTime (game time: pause freezes it): the points of interest in row order, then flow
    /// (each flow tick followed by a map combination pass, #146), then collection, then (#146)
    /// process cycles, then the transfer pass, each on its own serialized cadence; then discovery. Update calls it; public so tests can step it.
    /// </summary>
    public void Tick(float deltaTime)
    {
        if (deltaTime <= 0f) return;
        ElapsedSeconds += deltaTime;

        foreach (ManaPointOfInterest point in pointsOfInterest) TickPointOfInterest(point, deltaTime);

        flowTimer += deltaTime;
        if (flowTickSeconds > 0f)
        {
            while (flowTimer >= flowTickSeconds)
            {
                flowTimer -= flowTickSeconds;
                FlowStep();
                CombineStep(); // #146: fronts that met this tick react before the next
            }
        }

        foreach (Accumulator accumulator in Accumulators()) accumulator.TickCollect(deltaTime, this);
        foreach (IManaProcessorHost host in Processors()) host.Processor?.Tick(deltaTime); // #146: cycles run on game time

        transferTimer += deltaTime;
        if (transferSeconds > 0f)
        {
            while (transferTimer >= transferSeconds)
            {
                transferTimer -= transferSeconds;
                TransferPass(transferSeconds);
            }
        }

        RefreshDiscovery();
    }

    // A point emits once per interval while a neighbor has room; dormant, it holds a full interval and emits the moment
    // space frees.
    private void TickPointOfInterest(ManaPointOfInterest point, float deltaTime)
    {
        if (point == null || dungeonBoard == null) return;
        point.emitTimer += deltaTime;
        if (point.emitSeconds > 0f)
        {
            while (point.emitTimer >= point.emitSeconds)
            {
                if (!TryFindEmitTile(point, out Vector2Int tile))
                {
                    point.emitTimer = point.emitSeconds; // Dormant: ready the moment a neighbor frees
                    break;
                }
                point.emitTimer -= point.emitSeconds;
                TryAddUnit(tile, point.manaType);
            }
        }
        bool emitting = !IsDormant(point);
        if (emitting == point.wasEmitting) return;
        point.wasEmitting = emitting;
        PointOfInterestChanged?.Invoke(point.tile);
    }

    /// <summary>
    /// One flow tick: each tile holding mana at the start of the tick, in a shuffled order, sends one unit to a neighbor
    /// with fewer units and room (ChooseMove, the pick random). Occupancy updates as units move, so later tiles see the
    /// moves already made. Returns how many units moved.
    /// </summary>
    public int FlowStep()
    {
        if (dungeonBoard == null || cells.Count == 0) return 0;
        flowRandom ??= new System.Random(flowSeed);
        List<Vector2Int> order = ScanOrdered(cells.Keys); // Scan order, then shuffled: the same seed gives the same order
        for (int i = order.Count - 1; i > 0; i--)
        {
            int j = flowRandom.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }
        int movedCount = 0;
        foreach (Vector2Int from in order)
        {
            string type = TypeAt(from);
            if (type == null) continue;
            if (!ChooseMove(from, UnitsAt, t => CanAccept(t, type), flowRandom.Next, out Vector2Int to)) continue;
            TryTakeUnit(from, out _);
            TryAddUnit(to, type);
            movedCount++;
        }
        return movedCount;
    }

    /// <summary>
    /// The movement rule, pressure (a static seam): the 4-neighbors (right, up, left, down) that canEnter allows and that
    /// hold fewer units than from are the candidates; pick(n) chooses one of n (random in play, fixed in tests). False,
    /// staying put, when none has fewer: mana only ever flows from high pressure to low.
    /// </summary>
    public static bool ChooseMove(Vector2Int from, Func<Vector2Int, int> unitsAt, Func<Vector2Int, bool> canEnter,
        Func<int, int> pick, out Vector2Int to)
    {
        to = from;
        int here = unitsAt(from);
        var lower = new List<Vector2Int>(4);
        foreach (Vector2Int d in Neighbors)
        {
            Vector2Int candidate = from + d;
            if (unitsAt(candidate) < here && canEnter(candidate)) lower.Add(candidate);
        }
        if (lower.Count == 0) return false;
        to = lower[Mathf.Clamp(pick(lower.Count), 0, lower.Count - 1)];
        return true;
    }

    // ---- map combination (#146, the wild route) ----

    /// <summary>
    /// One combination pass over the board (the clock runs it after every flow tick). Tiles in board scan order; for each,
    /// the process rows that combine on the map, in table order; the first that fires wins. A row fires centred on a tile
    /// that holds one of its inputs when the plus — the centre and its 4-neighbours not yet used this tick — holds at least
    /// the row's map amount of every input. So the other input always touches the centre: a collision, not a mix at a
    /// distance; and the scattered 1–2 unit pockets where fronts interleave still count. Exactly the row's amounts leave
    /// the plus (the centre first, then right, up, left, down) and exactly its outputs appear: on the centre, then its open
    /// neighbours, then the open neighbours of the tiles drawn from, each taking what the cap and the one-type rule allow.
    /// When the product can't all fit, nothing changes — it waits for room. Every tile that changed is used for the rest of
    /// the tick. Deterministic: no random picks. Returns how many reactions fired.
    /// </summary>
    public int CombineStep()
    {
        if (dungeonBoard == null || cells.Count == 0 || processes == null) return 0;
        var rows = new List<ManaProcessRow>();
        foreach (ManaProcessRow row in processes)
            if (row != null && row.CanCombineOnMap) rows.Add(row);
        if (rows.Count == 0) return 0;

        var used = new HashSet<Vector2Int>();
        int reactions = 0;
        foreach (Vector2Int tile in ScanOrdered(cells.Keys))
        {
            if (used.Contains(tile) || TypeAt(tile) == null) continue;
            foreach (ManaProcessRow row in rows)
            {
                if (!TryCombineAt(row, tile, used)) continue;
                reactions++;
                break;
            }
        }
        return reactions;
    }

    private bool TryCombineAt(ManaProcessRow row, Vector2Int tile, HashSet<Vector2Int> used)
    {
        List<string> types = row.InputTypes();
        if (!types.Contains(TypeAt(tile))) return false; // The centre holds an input: whatever else counts touches it

        // The plus: the centre, then its 4-neighbours not yet used this tick. Each input is taken from it in that order.
        var plus = new List<Vector2Int> { tile };
        foreach (Vector2Int d in Neighbors)
            if (!used.Contains(tile + d)) plus.Add(tile + d);
        var after = new Dictionary<Vector2Int, Cell>(); // What the board would hold after; committed only if all fits
        foreach (string type in types)
        {
            int need = row.MapInputOf(type);
            foreach (Vector2Int p in plus)
            {
                if (need <= 0) break;
                Cell c = after.TryGetValue(p, out Cell a) ? a : cells.TryGetValue(p, out Cell b) ? b : default;
                if (c.count <= 0 || c.type != type) continue;
                int take = Mathf.Min(need, c.count);
                after[p] = c.count > take ? new Cell { type = type, count = c.count - take } : default;
                need -= take;
            }
            if (need > 0) return false; // Below the row's amount: nothing converts
        }

        // The product goes to the centre, then its open neighbours, then the open neighbours of the tiles it drew from.
        var places = new List<Vector2Int> { tile };
        foreach (Vector2Int d in Neighbors)
            if (IsOpen(tile + d)) places.Add(tile + d);
        foreach (Vector2Int r in new List<Vector2Int>(after.Keys))
            foreach (Vector2Int d in Neighbors)
                if (!places.Contains(r + d) && IsOpen(r + d)) places.Add(r + d);

        foreach (ManaAmount output in row.outputs)
        {
            int toPlace = output.amount;
            foreach (Vector2Int p in places)
            {
                if (toPlace <= 0) break;
                Cell c = after.TryGetValue(p, out Cell a) ? a : cells.TryGetValue(p, out Cell b) ? b : default;
                if (c.count > 0 && c.type != output.type) continue;
                int put = Mathf.Min(toPlace, maxManaPerTile - c.count);
                if (put <= 0) continue;
                after[p] = new Cell { type = output.type, count = c.count + put };
                toPlace -= put;
            }
            if (toPlace > 0) return false; // No room for the product: nothing converts
        }

        foreach (KeyValuePair<Vector2Int, Cell> e in after)
        {
            if (e.Value.count > 0) cells[e.Key] = e.Value;
            else cells.Remove(e.Key);
            used.Add(e.Key);
            ManaChanged?.Invoke(e.Key);
        }
        return true;
    }

    // ---- stores, totals, discovery ----

    /// <summary>A built store joins the service (Accumulator / ManaContainer call this on build-complete). Idempotent.</summary>
    public void Register(IManaPort port)
    {
        if (port == null || ports.Contains(port)) return;
        ports.Add(port);
        RefreshDiscovery();
    }

    /// <summary>A store leaves the service (destroyed). Idempotent.</summary>
    public void Unregister(IManaPort port) => ports.Remove(port);

    public int PortCount
    {
        get
        {
            PrunePorts();
            return ports.Count;
        }
    }

    /// <summary>The registered stores, pruned of destroyed ones, in board scan order.</summary>
    public List<IManaPort> Ports()
    {
        PrunePorts();
        var list = new List<IManaPort>(ports);
        list.Sort((a, b) => CompareScan(a.Tile, b.Tile));
        return list;
    }

    /// <summary>The registered store standing on this tile, or null.</summary>
    public IManaPort PortAt(Vector2Int tile)
    {
        foreach (IManaPort p in Ports())
            if (p.Tile == tile) return p;
        return null;
    }

    private List<Accumulator> Accumulators()
    {
        var list = new List<Accumulator>();
        foreach (IManaPort p in Ports())
            if (p is Accumulator a) list.Add(a);
        return list;
    }

    // A destroyed MonoBehaviour port reads null through Unity's check.
    private void PrunePorts() => ports.RemoveAll(p => p == null || (p is Object o && o == null));

    /// <summary>
    /// The global sum of a type across every registered store and (#146) every process host's staged inputs and held
    /// product (information is global; the resource is physical).
    /// </summary>
    public int TotalOf(string type)
    {
        int total = 0;
        foreach (IManaPort p in Ports())
            if (p.Store != null && p.Store.Type == type) total += p.Store.Amount;
        foreach (IManaProcessorHost h in Processors())
            if (h.Processor != null) total += h.Processor.StagedOf(type) + h.Processor.HeldOf(type);
        return total;
    }

    /// <summary>Every type across every store and process host: the TopBar ✦ readout.</summary>
    public int TotalAll()
    {
        int total = 0;
        foreach (IManaPort p in Ports()) total += p.Store?.Amount ?? 0;
        foreach (IManaProcessorHost h in Processors())
            if (h.Processor != null) total += h.Processor.StagedTotal + h.Processor.HeldTotal;
        return total;
    }

    /// <summary>A type is discovered the first time any store (or, #146, process host) holds it; permanent for the session.</summary>
    public bool IsDiscovered(string type) => type != null && discovered.Contains(type);

    /// <summary>The discovered types in catalog order (a discovered type with no row comes after, in id order).</summary>
    public List<string> DiscoveredTypes()
    {
        var list = new List<string>();
        if (manaTypes != null)
            foreach (ManaTypeDefinition d in manaTypes)
                if (d != null && discovered.Contains(d.id) && !list.Contains(d.id)) list.Add(d.id);
        var extra = new List<string>();
        foreach (string id in discovered) if (!list.Contains(id)) extra.Add(id);
        extra.Sort(StringComparer.Ordinal);
        list.AddRange(extra);
        return list;
    }

    /// <summary>Marks every type a store holds now as discovered. The clock calls it; public so staging can.</summary>
    public void RefreshDiscovery()
    {
        foreach (IManaPort p in Ports())
            if (p.Store != null && p.Store.Amount > 0 && p.Store.Type != null) discovered.Add(p.Store.Type);
        foreach (IManaProcessorHost h in Processors())
            if (h.Processor != null)
                foreach (KeyValuePair<string, int> e in h.Processor.Holdings()) discovered.Add(e.Key);
    }

    // ---- process hosts and the process table (#146) ----

    /// <summary>The process rows for a host type, in table order (none for a host with no rows).</summary>
    public List<ManaProcessRow> RowsFor(PlaceableType host)
    {
        var rows = new List<ManaProcessRow>();
        if (processes != null)
            foreach (ManaProcessRow row in processes)
                if (row != null && row.host == host) rows.Add(row);
        return rows;
    }

    /// <summary>A built process host joins the service (the chamber calls this on build-complete). Idempotent.</summary>
    public void RegisterProcessor(IManaProcessorHost host)
    {
        if (host == null || processors.Contains(host)) return;
        processors.Add(host);
    }

    /// <summary>A process host leaves the service (destroyed). Idempotent.</summary>
    public void UnregisterProcessor(IManaProcessorHost host) => processors.Remove(host);

    /// <summary>The registered process hosts, pruned of destroyed ones, in board scan order.</summary>
    public List<IManaProcessorHost> Processors()
    {
        processors.RemoveAll(h => h == null || (h is Object o && o == null));
        var list = new List<IManaProcessorHost>(processors);
        list.Sort((a, b) => CompareScan(a.Tile, b.Tile));
        return list;
    }

    /// <summary>The registered process host standing on this tile, or null.</summary>
    public IManaProcessorHost ProcessorAt(Vector2Int tile)
    {
        foreach (IManaProcessorHost h in Processors())
            if (h.Tile == tile) return h;
        return null;
    }

    /// <summary>The stores on the same pipe network as a tile (its own-tile attachment), in scan order; none off a network.</summary>
    public List<IManaPort> PortsOnNetworkOf(Vector2Int tile)
    {
        var list = new List<IManaPort>();
        int id = NetworkAt(tile);
        if (id < 0) return list;
        foreach (IManaPort p in Ports())
            if (p.Tile != tile && NetworkAt(p.Tile) == id) list.Add(p);
        return list;
    }

    // ---- the pipe layer (conduit's twin) ----

    public bool IsPipePending(Vector2Int tile) => pendingPipe.Contains(tile);
    public bool IsPipeBuilt(Vector2Int tile) => builtPipe.Contains(tile);
    public bool HasPipe(Vector2Int tile) => pendingPipe.Contains(tile) || builtPipe.Contains(tile);
    public int PendingPipeCount => pendingPipe.Count;
    public int BuiltPipeCount => builtPipe.Count;

    /// <summary>Pending pipe tiles in board scan order: the imp's pipe task list, deterministic.</summary>
    public List<Vector2Int> PendingPipeTiles() => ScanOrdered(pendingPipe);

    public List<Vector2Int> BuiltPipeTiles() => ScanOrdered(builtPipe);

    /// <summary>A pending tile's build progress, 0–1 (1 for built, 0 for no pipe).</summary>
    public float PipeProgressOf(Vector2Int tile) =>
        builtPipe.Contains(tile) ? 1f : pipeProgress.TryGetValue(tile, out float p) ? p : 0f;

    /// <summary>
    /// Whether a pipe drag in this mode changes the tile: Lay takes seen Floor with no pipe yet (sight-gated, #131); Remove
    /// takes any pipe. Pipe and conduit are independent layers, and neither is occupancy.
    /// </summary>
    public bool IsPipeEligible(FogOfWar fog, Vector2Int tile, PipeMode mode)
    {
        if (dungeonBoard == null || !dungeonBoard.IsInBounds(tile.x, tile.y)) return false;
        return mode == PipeMode.Lay
            ? dungeonBoard.GetTile(tile.x, tile.y) == TileState.Floor && FogOfWar.IsSeen(fog, tile) && !HasPipe(tile)
            : HasPipe(tile);
    }

    /// <summary>The tiles in rect a release would change, in x-then-y order, into results (cleared first): the one seam.</summary>
    public void EligiblePipeTiles(FogOfWar fog, RectInt rect, PipeMode mode, List<Vector2Int> results)
    {
        results.Clear();
        for (int x = rect.x; x < rect.xMax; x++)
            for (int y = rect.y; y < rect.yMax; y++)
            {
                var tile = new Vector2Int(x, y);
                if (IsPipeEligible(fog, tile, mode)) results.Add(tile);
            }
    }

    /// <summary>Lays pending pipe on (or removes pipe from) the eligible tiles in rect; returns how many changed. No refund.</summary>
    public int ApplyPipeRect(FogOfWar fog, RectInt rect, PipeMode mode)
    {
        var tiles = new List<Vector2Int>();
        EligiblePipeTiles(fog, rect, mode, tiles);
        foreach (Vector2Int tile in tiles)
        {
            if (mode == PipeMode.Remove)
            {
                RemovePipe(tile);
                continue;
            }
            pendingPipe.Add(tile);
            PipeChanged?.Invoke(tile);
        }
        return tiles.Count;
    }

    /// <summary>Imp work on a pending tile; builds it on reaching full. True when this call built it.</summary>
    public bool AddPipeProgress(Vector2Int tile, float deltaTime)
    {
        if (!pendingPipe.Contains(tile)) return false;
        float p = PipeProgressOf(tile) + (pipeSecondsPerTile > 0f ? deltaTime / pipeSecondsPerTile : 1f);
        if (p < 1f - CompleteTolerance)
        {
            pipeProgress[tile] = p;
            return false;
        }
        CompletePipe(tile);
        return true;
    }

    /// <summary>Pending → built (the imp's completion; tests and staging call it directly). False if it wasn't pending.</summary>
    public bool CompletePipe(Vector2Int tile)
    {
        if (!pendingPipe.Remove(tile)) return false;
        pipeProgress.Remove(tile);
        builtPipe.Add(tile);
        networksDirty = true;
        PipeChanged?.Invoke(tile);
        return true;
    }

    /// <summary>Removes the tile's pipe, pending or built. False if it had none.</summary>
    public bool RemovePipe(Vector2Int tile)
    {
        bool wasPending = pendingPipe.Remove(tile);
        bool wasBuilt = builtPipe.Remove(tile);
        pipeProgress.Remove(tile);
        if (!wasPending && !wasBuilt) return false;
        if (wasBuilt) networksDirty = true;
        PipeChanged?.Invoke(tile);
        return true;
    }

    /// <summary>The pipe network a building on this tile attaches to: its own tile's built pipe only. -1 for none.</summary>
    public int NetworkAt(Vector2Int tile) => Networks().TryGetValue(tile, out int id) ? id : -1;

    public int NetworkCount
    {
        get
        {
            Dictionary<Vector2Int, int> n = Networks();
            return PowerGrid.CountGrids(n);
        }
    }

    // Connected components of built pipe (PowerGrid's labelling), recomputed only when built pipe changes.
    private Dictionary<Vector2Int, int> Networks()
    {
        if (!networksDirty) return networks;
        networks = PowerGrid.Components(builtPipe);
        networksDirty = false;
        networkCarry.Clear(); // Ids may have moved: no network inherits another's carry
        return networks;
    }

    // ---- the transfer pass ----

    /// <summary>
    /// One transfer pass over seconds of time (#145): per network, the attached ports and process hosts (own-tile only) are
    /// gathered in scan order and, under the network's one throughput budget: (#146) the draw phase fills each host's
    /// demand from containers, then accumulator buffers above the reserve; the deposit phase moves held product into
    /// containers that accept it; then the overflow phase: outputs (accumulators) push what's above the reserve into
    /// inputs (containers) with room. Each host is then told what's stalling it, if anything (an input nothing on its
    /// network can supply; product no connected store can take). Returns the units moved across all networks.
    /// </summary>
    public int TransferPass(float seconds)
    {
        Dictionary<Vector2Int, int> net = Networks();
        var hostsBy = new Dictionary<int, List<ManaProcessor>>();
        foreach (IManaProcessorHost host in Processors())
        {
            if (host.Processor == null) continue;
            if (!net.TryGetValue(host.Tile, out int id))
            {
                // Not on a pipe: nothing can supply it and nothing can take its product.
                host.Processor.SetStallVerdicts(FirstShortInput(host.Processor, null, null), true);
                continue;
            }
            if (!hostsBy.TryGetValue(id, out List<ManaProcessor> h)) hostsBy[id] = h = new List<ManaProcessor>();
            h.Add(host.Processor);
        }
        if (net.Count == 0) return 0;

        var sourcesBy = new Dictionary<int, List<ManaStore>>();
        var sinksBy = new Dictionary<int, List<ManaStore>>();
        foreach (IManaPort port in Ports())
        {
            if (port.Store == null || !net.TryGetValue(port.Tile, out int id)) continue; // Not on a pipe: holds what it has
            if (port.Role == ManaPortRole.Output)
            {
                if (!sourcesBy.TryGetValue(id, out List<ManaStore> s)) sourcesBy[id] = s = new List<ManaStore>();
                s.Add(port.Store);
            }
            else
            {
                if (!sinksBy.TryGetValue(id, out List<ManaStore> k)) sinksBy[id] = k = new List<ManaStore>();
                k.Add(port.Store);
            }
        }

        int movedTotal = 0;
        int count = PowerGrid.CountGrids(net);
        for (int id = 0; id < count; id++)
        {
            float carry = networkCarry.TryGetValue(id, out float c) ? c : 0f;
            int budget = Budget(carry, pipeThroughputPerSecond, seconds, out float nextCarry);
            networkCarry[id] = nextCarry;
            sourcesBy.TryGetValue(id, out List<ManaStore> sources);
            sinksBy.TryGetValue(id, out List<ManaStore> sinks);
            if (hostsBy.TryGetValue(id, out List<ManaProcessor> hosts))
            {
                int drawn = Draw(hosts, sinks, sources, accumulatorReserve, budget);
                int deposited = Deposit(hosts, sinks, budget - drawn);
                budget -= drawn + deposited;
                movedTotal += drawn + deposited;
                foreach (ManaProcessor host in hosts)
                    host.SetStallVerdicts(FirstShortInput(host, sinks, sources), !CanTakeAnyHeld(host, sinks));
            }
            if (sources == null || sinks == null) continue;
            movedTotal += Overflow(sources, sinks, accumulatorReserve, budget);
        }
        return movedTotal;
    }

    /// <summary>
    /// The draw phase (#146, a static seam): each host in order, each input in row order, takes its demand from containers
    /// (in order) holding that type, then from accumulator buffers holding it above reserve — never below — until budget
    /// units have moved. Returns how many moved. Conserves mana: what leaves a store is staged in the host.
    /// </summary>
    public static int Draw(IList<ManaProcessor> hosts, IList<ManaStore> containers, IList<ManaStore> buffers, int reserve, int budget)
    {
        int moved = 0;
        if (hosts == null) return 0;
        foreach (ManaProcessor host in hosts)
        {
            if (host?.Row == null) continue;
            foreach (ManaAmount input in host.Row.inputs)
            {
                if (input == null) continue;
                moved += DrawFrom(host, input.type, containers, 0, budget - moved);
                moved += DrawFrom(host, input.type, buffers, reserve, budget - moved);
                if (moved >= budget) return moved;
            }
        }
        return moved;
    }

    private static int DrawFrom(ManaProcessor host, string type, IList<ManaStore> stores, int keep, int budget)
    {
        int moved = 0;
        if (stores == null) return 0;
        foreach (ManaStore store in stores)
        {
            int want = Mathf.Min(host.Demand(type), budget - moved);
            if (want <= 0) break;
            if (store == null || store.Type != type) continue;
            int spare = store.Amount - Mathf.Max(0, keep);
            if (spare <= 0) continue;
            int taken = store.Withdraw(Mathf.Min(want, spare));
            int staged = host.Stage(type, taken);
            if (staged < taken) store.Deposit(type, taken - staged); // Never lose a unit
            moved += staged;
        }
        return moved;
    }

    /// <summary>
    /// The deposit phase (#146, a static seam): each host's held product, type by type, goes into containers (in order)
    /// that accept it — adopting the type if empty — until budget units have moved. Returns how many moved; the rest stays held.
    /// </summary>
    public static int Deposit(IList<ManaProcessor> hosts, IList<ManaStore> containers, int budget)
    {
        int moved = 0;
        if (hosts == null || containers == null) return 0;
        foreach (ManaProcessor host in hosts)
        {
            if (host == null) continue;
            foreach (string type in host.HeldTypes())
            {
                foreach (ManaStore sink in containers)
                {
                    int amount = Mathf.Min(host.HeldOf(type), budget - moved);
                    if (amount <= 0) break;
                    if (sink == null || !sink.CanAccept(type)) continue;
                    int put = sink.Deposit(type, Mathf.Min(amount, sink.Room));
                    host.TakeHeld(type, put);
                    moved += put;
                }
                if (moved >= budget) return moved;
            }
        }
        return moved;
    }

    // The first input (row order) a host is still short of that nothing it reaches can supply: no container holding it,
    // no buffer holding it above the reserve. Null when it's running, holding product, or every shortfall has a supply.
    private string FirstShortInput(ManaProcessor host, IList<ManaStore> containers, IList<ManaStore> buffers)
    {
        if (host?.Row == null || host.IsRunning || host.IsHolding) return null;
        foreach (ManaAmount input in host.Row.inputs)
        {
            if (input == null || host.Demand(input.type) <= 0) continue;
            if (!HasSupply(input.type, containers, 0) && !HasSupply(input.type, buffers, accumulatorReserve)) return input.type;
        }
        return null;
    }

    private static bool HasSupply(string type, IList<ManaStore> stores, int keep)
    {
        if (stores == null) return false;
        foreach (ManaStore store in stores)
            if (store != null && store.Type == type && store.Amount > keep) return true;
        return false;
    }

    // Whether any connected container could take some of a host's held product now.
    private static bool CanTakeAnyHeld(ManaProcessor host, IList<ManaStore> containers)
    {
        if (containers == null) return false;
        foreach (string type in host.HeldTypes())
            foreach (ManaStore sink in containers)
                if (sink != null && sink.CanAccept(type)) return true;
        return false;
    }

    /// <summary>
    /// The throughput budget for one pass: carry plus rate × seconds, whole units only; the fraction carries to the next
    /// pass (so the rate is exact over time) and unused whole units are not banked.
    /// </summary>
    public static int Budget(float carry, float ratePerSecond, float seconds, out float nextCarry)
    {
        float available = Mathf.Max(0f, carry) + Mathf.Max(0f, ratePerSecond) * Mathf.Max(0f, seconds);
        int whole = Mathf.FloorToInt(available + 1e-5f);
        nextCarry = Mathf.Max(0f, available - whole);
        return whole;
    }

    /// <summary>
    /// The overflow phase (a static seam): sources in order push what's above reserve into sinks in order, each sink taking
    /// only a type it can accept (adopt rules), until budget units have moved. Returns how many moved. Conserves mana.
    /// </summary>
    public static int Overflow(IList<ManaStore> sources, IList<ManaStore> sinks, int reserve, int budget)
    {
        int moved = 0;
        if (sources == null || sinks == null) return 0;
        foreach (ManaStore source in sources)
        {
            if (source == null) continue;
            foreach (ManaStore sink in sinks)
            {
                if (moved >= budget) return moved;
                int spare = source.Amount - Mathf.Max(0, reserve);
                if (spare <= 0 || source.Type == null) break;
                if (sink == null || sink == source || !sink.CanAccept(source.Type)) continue;
                int amount = Mathf.Min(spare, Mathf.Min(sink.Room, budget - moved));
                string type = source.Type;
                int taken = source.Withdraw(amount);
                int put = sink.Deposit(type, taken);
                if (put < taken) source.Deposit(type, taken - put); // Never lose a unit
                moved += put;
            }
        }
        return moved;
    }

    // ---- helpers ----

    private static List<Vector2Int> ScanOrdered(IEnumerable<Vector2Int> tiles)
    {
        var list = new List<Vector2Int>(tiles);
        list.Sort(CompareScan);
        return list;
    }

    private static int CompareScan(Vector2Int a, Vector2Int b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y);
}
