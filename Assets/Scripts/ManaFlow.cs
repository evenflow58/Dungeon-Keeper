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
/// The Fire Vent is a world feature on ventTile (carved to Floor at setup, entombed in rock until dug out): every
/// ventEmitSeconds it puts one unit of its type on a free 4-neighbor (board scan order), or is dormant while none is free,
/// resuming on its own. Flow is gas-like: a tile's pressure is how full its surroundings are (Density: units over open
/// tiles within densityRadius, nearer tiles weighted more), plus the vent's push (ventPressure at its mouth, carried along
/// the passages it reaches and fading by e every ventReach tiles walked — rock stops it), minus the pull of each
/// accumulator that has room (its suction, fading to nothing past its pull radius). Every
/// flowTickSeconds each unit, in scan order and seeing moves already made, steps to its lowest-pressure free 4-neighbor
/// if that is lower by more than moveThreshold (ties: right, up, left, down), else it rests. So mana flows from full to
/// empty: out of the vent's tunnel, through the halls, and toward accumulators, which only pull within their radius —
/// beyond it, the emptier patch they leave draws mana in on its own.
/// Stores register here (the PowerNode pattern): TotalOf sums a type across them and a type is discovered, for the
/// session, the first time any store holds it. Accumulators collect from beside them on their own cycle, driven by this
/// clock. Pipes are conduit's twin (pending / built, imp-built over pipeSecondsPerTile, explored Floor only); a network is
/// a connected run of built pipe, and a building attaches only through pipe on its own tile. The transfer pass runs every
/// transferSeconds per network: accumulators push what's above accumulatorReserve into containers with room, capped by a
/// shared per-network throughput with an exact fractional carry. Pipes never pool and buildings never touch-transfer.
/// </summary>
[DisallowMultipleComponent]
public class ManaFlow : MonoBehaviour
{
    public const string FireId = "Fire";
    public const string WaterId = "Water";

    /// <summary>What a pipe drag does: lay pending pipe, or remove it.</summary>
    public enum PipeMode { Lay, Remove }

    [Header("References")]
    [SerializeField] private DungeonBoard dungeonBoard;

    // The catalog home for mana types: append-only rows. Water has no source yet (#150); the row makes it vocabulary.
    [Header("Mana Types")]
    [SerializeField] private List<ManaTypeDefinition> manaTypes = new List<ManaTypeDefinition>
    {
        new ManaTypeDefinition(FireId, "Fire Mana", new Color(1.00f, 0.42f, 0.12f, 1f)),
        new ManaTypeDefinition(WaterId, "Water Mana", new Color(0.25f, 0.60f, 1.00f, 1f)),
    };

    [Header("Occupancy and Flow")]
    [SerializeField] private int maxManaPerTile = 1;
    [SerializeField] private float flowTickSeconds = 0.5f;
    [SerializeField] private int densityRadius = 2;          // How far around a tile its density looks (Manhattan)
    [SerializeField] private float moveThreshold = 0.01f;    // A step must lower the pressure by more than this: no twitching on near-ties

    [Header("Fire Vent")]
    [SerializeField] private bool hasVent = true;
    [SerializeField] private Vector2Int ventTile = new Vector2Int(24, 5); // Deep in the rock south of the starter cavern
    [SerializeField] private string ventManaType = FireId;
    [SerializeField] private float ventEmitSeconds = 2f;
    [SerializeField] private float ventPressure = 1.5f;      // The vent's push at its mouth (density runs 0–1)
    [SerializeField] private float ventReach = 8f;           // The push fades by e every this many tiles walked through open floor

    [Header("Pipes")]
    [SerializeField] private float pipeSecondsPerTile = 0.5f;      // Imp work to build one pending pipe tile
    [SerializeField] private float pipeThroughputPerSecond = 5f;   // Per network, shared across every type on it
    [SerializeField] private float transferSeconds = 0.25f;        // The transfer pass's cadence
    [SerializeField] private int accumulatorReserve = 5;           // An accumulator keeps this much; only the rest overflows

    public DungeonBoard Board { get => dungeonBoard; set => dungeonBoard = value; }
    public int MaxManaPerTile { get => maxManaPerTile; set => maxManaPerTile = Mathf.Max(1, value); }
    public float FlowTickSeconds { get => flowTickSeconds; set => flowTickSeconds = value; }
    public int DensityRadius { get => densityRadius; set => densityRadius = Mathf.Max(0, value); }
    public float MoveThreshold { get => moveThreshold; set => moveThreshold = value; }
    public float VentPressure { get => ventPressure; set => ventPressure = value; }
    public float VentReach { get => ventReach; set => ventReach = value; }
    public bool HasVent { get => hasVent; set => hasVent = value; }
    public Vector2Int VentTile { get => ventTile; set => ventTile = value; }
    public string VentManaType { get => ventManaType; set => ventManaType = value; }
    public float VentEmitSeconds { get => ventEmitSeconds; set => ventEmitSeconds = value; }
    public float PipeSecondsPerTile { get => pipeSecondsPerTile; set => pipeSecondsPerTile = value; }
    public float PipeThroughputPerSecond { get => pipeThroughputPerSecond; set => pipeThroughputPerSecond = value; }
    public float TransferSeconds { get => transferSeconds; set => transferSeconds = value; }
    public int AccumulatorReserve { get => accumulatorReserve; set => accumulatorReserve = Mathf.Max(0, value); }
    public IReadOnlyList<ManaTypeDefinition> ManaTypes => manaTypes;

    /// <summary>Game seconds this simulation has run (the clock accumulators time their recent collection by).</summary>
    public float ElapsedSeconds { get; private set; }

    /// <summary>Raised when a tile's mana changes (a unit arrives or leaves), so the board view restyles it.</summary>
    public event Action<Vector2Int> ManaChanged;

    /// <summary>Raised when a tile's pipe changes (laid, built or removed).</summary>
    public event Action<Vector2Int> PipeChanged;

    /// <summary>Raised when the vent's state may have changed (it emitted, went dormant, or woke).</summary>
    public event Action VentChanged;

    private struct Cell
    {
        public string type;
        public int count;
    }

    private readonly Dictionary<Vector2Int, Cell> cells = new Dictionary<Vector2Int, Cell>();
    private readonly List<IManaPort> ports = new List<IManaPort>();
    private readonly HashSet<string> discovered = new HashSet<string>();
    private readonly HashSet<Vector2Int> pendingPipe = new HashSet<Vector2Int>();
    private readonly HashSet<Vector2Int> builtPipe = new HashSet<Vector2Int>();
    private readonly Dictionary<Vector2Int, float> pipeProgress = new Dictionary<Vector2Int, float>();
    private Dictionary<Vector2Int, int> networks = new Dictionary<Vector2Int, int>();
    private readonly Dictionary<int, float> networkCarry = new Dictionary<int, float>();
    private bool networksDirty;
    private float ventTimer;
    private float flowTimer;
    private float transferTimer;
    private bool lastVentDormant = true;

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
        CarveVent();
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

    // ---- the vent ----

    /// <summary>
    /// Carves the vent's tile to Floor (its pocket stays sealed: the tiles around it are untouched, so it starts entombed
    /// and dormant). Start runs it; idempotent; a no-op with no vent or no board.
    /// </summary>
    public void CarveVent()
    {
        if (!hasVent || dungeonBoard == null || !dungeonBoard.IsInBounds(ventTile.x, ventTile.y)) return;
        if (dungeonBoard.GetTile(ventTile.x, ventTile.y) != TileState.Floor) dungeonBoard.SetTile(ventTile.x, ventTile.y, TileState.Floor);
    }

    /// <summary>Whether this tile holds the vent.</summary>
    public bool IsVentTile(Vector2Int tile) => hasVent && tile == ventTile;

    /// <summary>True while the vent can't emit: no 4-neighbor can take a unit of its type (entombed or saturated).</summary>
    public bool VentDormant => !hasVent || !TryFindEmitTile(out _);

    /// <summary>The free neighbor the vent emits onto next: the first of its 4-neighbors, in board scan order, that can accept.</summary>
    public bool TryFindEmitTile(out Vector2Int tile)
    {
        tile = default;
        if (!hasVent || dungeonBoard == null) return false;
        foreach (Vector2Int candidate in ScanOrderedNeighbors(ventTile))
        {
            if (!CanAccept(candidate, ventManaType)) continue;
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
    /// Open to mana: in-bounds Floor or Entrance. Rock and Designated are walls; placeables never block. The vent's own tile
    /// is never open: the vent stands there and emits beside it.
    /// </summary>
    public bool IsOpen(Vector2Int tile) =>
        dungeonBoard != null && dungeonBoard.IsInBounds(tile.x, tile.y) && !IsVentTile(tile)
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
    /// Advances the simulation by deltaTime (game time: pause freezes it): the vent, then flow, then collection, then the
    /// transfer pass, each on its own serialized cadence; then discovery. Update calls it; public so tests can step it.
    /// </summary>
    public void Tick(float deltaTime)
    {
        if (deltaTime <= 0f) return;
        ElapsedSeconds += deltaTime;

        TickVent(deltaTime);

        flowTimer += deltaTime;
        if (flowTickSeconds > 0f)
        {
            while (flowTimer >= flowTickSeconds)
            {
                flowTimer -= flowTickSeconds;
                FlowStep();
            }
        }

        foreach (Accumulator accumulator in Accumulators()) accumulator.TickCollect(deltaTime, this);

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

    // The vent emits once per interval while it has a free neighbor; dormant, it holds a full interval and emits the moment
    // space frees.
    private void TickVent(float deltaTime)
    {
        if (!hasVent || dungeonBoard == null) return;
        ventTimer += deltaTime;
        if (ventEmitSeconds > 0f)
        {
            while (ventTimer >= ventEmitSeconds)
            {
                if (!TryFindEmitTile(out Vector2Int tile))
                {
                    ventTimer = ventEmitSeconds; // Dormant: ready the moment a neighbor frees
                    break;
                }
                ventTimer -= ventEmitSeconds;
                TryAddUnit(tile, ventManaType);
            }
        }
        bool dormant = VentDormant;
        if (dormant == lastVentDormant) return;
        lastVentDormant = dormant;
        VentChanged?.Invoke();
    }

    /// <summary>
    /// One flow tick: each unit, in board scan order, may step once (ChooseMove over PressureAt). Occupancy updates as units
    /// move, so a tile vacated earlier this tick can be entered and pressures see the moves already made; a unit that moved
    /// doesn't move again this tick. The vent's push and the accumulators' pull are gathered once per tick.
    /// </summary>
    public void FlowStep()
    {
        if (dungeonBoard == null || cells.Count == 0) return;
        ventField = VentDistanceField(); // Rebuilt each tick: cheap at this size, and it follows every dig
        pulls.Clear();
        foreach (Accumulator a in Accumulators())
            if (a.IsPulling) pulls.Add((a.Tile, a.PullRadius, a.Suction));
        try
        {
            var moved = new HashSet<Vector2Int>(); // Tiles that received a unit this tick: their newcomer rests
            foreach (Vector2Int from in ScanOrdered(cells.Keys))
            {
                int units = UnitsAt(from);
                if (units == 0) continue;
                if (moved.Contains(from)) units--; // The newcomer stays put; the tile's earlier units may still move
                for (int i = 0; i < units; i++)
                {
                    string type = TypeAt(from);
                    if (type == null) break;
                    if (!ChooseMove(from, t => CanAccept(t, type), PressureAt, moveThreshold, out Vector2Int to)) break;
                    TryTakeUnit(from, out _);
                    TryAddUnit(to, type);
                    moved.Add(to);
                }
            }
        }
        finally
        {
            ventField = null; // The tick's snapshot: a read outside a tick computes fresh
            pulls.Clear();
        }
    }

    private readonly List<(Vector2Int tile, int radius, float strength)> pulls = new List<(Vector2Int, int, float)>();
    private int[,] ventField;

    /// <summary>
    /// The pressure a unit leaving mover would feel at tile: the local density (Density, the mover not counted), plus the
    /// vent's push (VentPush: carried along the passages it reaches, fading with the walk), minus each pulling accumulator's
    /// suction (strongest at it, gone past its pull radius). Pass a tile no unit stands on as mover to read a tile's own
    /// pressure. Outside a flow tick the vent's walking distances are read fresh.
    /// </summary>
    public float PressureAt(Vector2Int tile, Vector2Int mover)
    {
        float p = Density(tile, mover, densityRadius, IsOpen, UnitsAt);
        p += VentPush(DistanceIn(ventField ?? VentDistanceField(), tile), ventPressure, ventReach);
        foreach (var pull in pulls) p -= Falloff(tile, pull.tile, pull.radius, pull.strength);
        return p;
    }

    /// <summary>
    /// A source's push at a walking distance (a static seam): strength at its mouth, fading by e every reach tiles walked,
    /// never quite 0 along a passage it reaches; 0 where it can't reach (distance −1). Because every step away lowers it, a
    /// pushed unit never ties with the tile ahead of it in a tunnel, so the push carries all the way down.
    /// </summary>
    public static float VentPush(int walkDistance, float strength, float reach) =>
        walkDistance < 0 || reach <= 0f ? 0f : strength * Mathf.Exp(-(walkDistance - 1) / reach);

    /// <summary>
    /// Walking distance from the vent through open tiles (the vent's tile is 0), int.MaxValue where it can't reach; null
    /// with no vent. The vent's push follows these passages: rock stops it.
    /// </summary>
    public int[,] VentDistanceField()
    {
        if (!hasVent || dungeonBoard == null || !dungeonBoard.IsInBounds(ventTile.x, ventTile.y)) return null;
        var field = new int[dungeonBoard.Width, dungeonBoard.Height];
        for (int x = 0; x < dungeonBoard.Width; x++)
            for (int y = 0; y < dungeonBoard.Height; y++)
                field[x, y] = int.MaxValue;
        var queue = new Queue<Vector2Int>();
        field[ventTile.x, ventTile.y] = 0;
        queue.Enqueue(ventTile);
        while (queue.Count > 0)
        {
            Vector2Int tile = queue.Dequeue();
            foreach (Vector2Int d in Neighbors)
            {
                Vector2Int n = tile + d;
                if (!dungeonBoard.IsInBounds(n.x, n.y) || field[n.x, n.y] != int.MaxValue) continue;
                if (!IsOpenState(dungeonBoard.GetTile(n.x, n.y))) continue;
                field[n.x, n.y] = field[tile.x, tile.y] + 1;
                queue.Enqueue(n);
            }
        }
        return field;
    }

    /// <summary>A tile's distance in a field, or -1 for none (no field, off the board, or unreachable from the vent).</summary>
    public static int DistanceIn(int[,] field, Vector2Int tile)
    {
        if (field == null || tile.x < 0 || tile.y < 0 || tile.x >= field.GetLength(0) || tile.y >= field.GetLength(1)) return -1;
        int d = field[tile.x, tile.y];
        return d == int.MaxValue ? -1 : d;
    }

    /// <summary>
    /// How full the area around a tile is (a static seam): the units within Manhattan radius over the open tiles there, each
    /// weighted by closeness (radius + 1 − distance), the mover's own unit left out. 0 with nothing open. Weighting by
    /// closeness is what lets two units side by side feel each other and drift apart, and dividing by the open tiles is what
    /// makes a packed tunnel read fuller than a half-empty hall (a plain neighbor count says the opposite: a tunnel tile has
    /// fewer neighbors, so it always looked emptier and mana sloshed back into it).
    /// </summary>
    public static float Density(Vector2Int tile, Vector2Int mover, int radius, Func<Vector2Int, bool> isOpen, Func<Vector2Int, int> unitsAt)
    {
        float units = 0f, open = 0f;
        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                int d = Mathf.Abs(dx) + Mathf.Abs(dy);
                if (d > radius) continue;
                var t = new Vector2Int(tile.x + dx, tile.y + dy);
                if (t != tile && !isOpen(t)) continue; // The tile itself always counts: it's where the unit would stand
                float w = radius + 1 - d;
                open += w;
                int n = unitsAt(t);
                if (t == mover) n = Mathf.Max(0, n - 1);
                units += w * n;
            }
        }
        return open > 0f ? units / open : 0f;
    }

    /// <summary>A source's (or sink's) strength at a tile: full at its own tile, falling off linearly to 0 past radius (Manhattan).</summary>
    public static float Falloff(Vector2Int tile, Vector2Int source, int radius, float strength)
    {
        int d = Mathf.Abs(tile.x - source.x) + Mathf.Abs(tile.y - source.y);
        return d > radius ? 0f : strength * (radius + 1 - d) / (radius + 1);
    }

    /// <summary>
    /// The movement rule, gas-like (a static seam): a unit steps to the free neighbor canEnter allows with the lowest
    /// pressure, if that is lower than its own tile's by more than threshold; ties go right, up, left, down. Otherwise it
    /// rests — so mana flows from full to empty, away from the vent and toward a pulling accumulator, and settles once the
    /// pressure is even. pressure(tile, mover) is read with the mover's own unit left out. False when it stays.
    /// </summary>
    public static bool ChooseMove(Vector2Int from, Func<Vector2Int, bool> canEnter, Func<Vector2Int, Vector2Int, float> pressure,
        float threshold, out Vector2Int to)
    {
        to = from;
        float best = pressure(from, from) - threshold;
        bool found = false;
        foreach (Vector2Int d in Neighbors)
        {
            Vector2Int candidate = from + d;
            if (!canEnter(candidate)) continue;
            float p = pressure(candidate, from);
            if (p >= best) continue;
            best = p;
            to = candidate;
            found = true;
        }
        return found;
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

    /// <summary>The global sum of a type across every registered store (information is global; the resource is physical).</summary>
    public int TotalOf(string type)
    {
        int total = 0;
        foreach (IManaPort p in Ports())
            if (p.Store != null && p.Store.Type == type) total += p.Store.Amount;
        return total;
    }

    /// <summary>Every type across every store: the TopBar ✦ readout.</summary>
    public int TotalAll()
    {
        int total = 0;
        foreach (IManaPort p in Ports()) total += p.Store?.Amount ?? 0;
        return total;
    }

    /// <summary>A type is discovered the first time any store holds it; permanent for the session.</summary>
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
    /// One transfer pass over seconds of time (#145): per network, the attached ports (own-tile only) are gathered in scan
    /// order and the overflow phase runs: outputs (accumulators) push what's above the reserve into inputs (containers)
    /// with room, under the network's throughput budget. Returns the units moved across all networks.
    /// </summary>
    public int TransferPass(float seconds)
    {
        Dictionary<Vector2Int, int> net = Networks();
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
            if (!sourcesBy.TryGetValue(id, out List<ManaStore> sources) || !sinksBy.TryGetValue(id, out List<ManaStore> sinks)) continue;
            movedTotal += Overflow(sources, sinks, accumulatorReserve, budget);
        }
        return movedTotal;
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
