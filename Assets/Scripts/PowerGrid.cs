using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>What a placeable does on a power grid (#142): nothing, makes watts, or draws them.</summary>
public enum PowerRole
{
    None,
    Producer,
    Consumer
}

/// <summary>
/// The power grid service (#142), on the DungeonManager. It owns the conduit layer: per-tile data on Floor tiles, each
/// tile pending (ordered, waiting for an imp) or built. Conduit isn't a TileState and isn't a placeable, and buildings may
/// stand on it. Pending tiles are a task list: the imp builds them as construction-family work (ImpDigger), each over
/// wireSecondsPerTile with progress kept on the tile, and an unreachable one simply stays pending.
/// Grids: a grid is a connected component of BUILT conduit (4-neighbor). A registered power node (a built placeable with
/// a power role) connects when its own tile or a 4-neighbor has built conduit; a node touching two grids joins the first
/// it finds (own tile, then right, up, left, down: Pathfinder's order). The topology is cached and recomputed only when
/// built conduit changes (a dirty flag, never a per-frame flood fill). Supply and demand are live on query: supply is the
/// active producers' watts (a wheel only while an imp tends it), demand every built consumer's (v1: a built consumer
/// draws whenever built). A node is powered when it's connected and its grid's supply covers its demand (equality is
/// powered). Toggles (#143): a disabled node is inert and left out of the math entirely — it supplies nothing, draws
/// nothing and is never powered; the topology doesn't change. The math lives in static helpers over plain data so tests
/// drive it without a scene.
/// </summary>
[DisallowMultipleComponent]
public class PowerGrid : MonoBehaviour
{
    /// <summary>What a wire-mode drag does to conduit: lay pending conduit, or remove it.</summary>
    public enum ConduitMode { Lay, Remove }

    [Header("References")]
    [SerializeField] private DungeonBoard dungeonBoard;

    [Header("Conduit")]
    [SerializeField] private float wireSecondsPerTile = 0.5f; // Imp work to build one pending conduit tile

    public DungeonBoard Board { get => dungeonBoard; set => dungeonBoard = value; }
    public float WireSecondsPerTile { get => wireSecondsPerTile; set => wireSecondsPerTile = value; }

    /// <summary>Raised when a tile's conduit changes (laid, built or removed), so the board view restyles it.</summary>
    public event Action<Vector2Int> ConduitChanged;

    private readonly HashSet<Vector2Int> pending = new HashSet<Vector2Int>();
    private readonly HashSet<Vector2Int> built = new HashSet<Vector2Int>();
    private readonly Dictionary<Vector2Int, float> progress = new Dictionary<Vector2Int, float>();
    private readonly List<PowerNode> nodes = new List<PowerNode>();
    private Dictionary<Vector2Int, int> components = new Dictionary<Vector2Int, int>();
    private bool topologyDirty;

    // Progress is a sum of deltaTime / seconds steps; absorb float rounding so a tile lands on its full duration.
    private const float CompleteTolerance = 1e-4f;

    private void Awake()
    {
        dungeonBoard ??= GetComponent<DungeonBoard>();
    }

    // ---- conduit layer ----

    public bool IsPending(Vector2Int tile) => pending.Contains(tile);
    public bool IsBuilt(Vector2Int tile) => built.Contains(tile);
    public bool HasConduit(Vector2Int tile) => pending.Contains(tile) || built.Contains(tile);
    public int PendingCount => pending.Count;
    public int BuiltCount => built.Count;

    /// <summary>Pending conduit tiles in board scan order (x, then y): the imp's conduit task list, deterministic.</summary>
    public List<Vector2Int> PendingTiles() => ScanOrdered(pending);

    /// <summary>Built conduit tiles in board scan order.</summary>
    public List<Vector2Int> BuiltTiles() => ScanOrdered(built);

    /// <summary>A pending tile's build progress, 0–1 (1 for built, 0 for no conduit).</summary>
    public float ProgressOf(Vector2Int tile) =>
        built.Contains(tile) ? 1f : progress.TryGetValue(tile, out float p) ? p : 0f;

    /// <summary>
    /// Whether a wire drag in this mode changes the tile: Lay takes seen Floor with no conduit yet (sight-gated like dig
    /// designation, #131; the Entrance isn't Floor); Remove takes any conduit, pending or built.
    /// </summary>
    public bool IsEligible(FogOfWar fog, Vector2Int tile, ConduitMode mode)
    {
        if (dungeonBoard == null || !dungeonBoard.IsInBounds(tile.x, tile.y)) return false;
        return mode == ConduitMode.Lay
            ? dungeonBoard.GetTile(tile.x, tile.y) == TileState.Floor && FogOfWar.IsSeen(fog, tile) && !HasConduit(tile)
            : HasConduit(tile);
    }

    /// <summary>
    /// The tiles in rect a release would change, in x-then-y order, into results (cleared first). The one eligibility
    /// seam: ApplyRect changes exactly these and the wire preview tints exactly these.
    /// </summary>
    public void EligibleTiles(FogOfWar fog, RectInt rect, ConduitMode mode, List<Vector2Int> results)
    {
        results.Clear();
        for (int x = rect.x; x < rect.xMax; x++)
        {
            for (int y = rect.y; y < rect.yMax; y++)
            {
                var tile = new Vector2Int(x, y);
                if (IsEligible(fog, tile, mode)) results.Add(tile);
            }
        }
    }

    /// <summary>
    /// Lays pending conduit on (or removes conduit from) the eligible tiles in rect; returns how many changed. Removal is
    /// instant for pending and built alike, with no refund (conduit costs imp time only).
    /// </summary>
    public int ApplyRect(FogOfWar fog, RectInt rect, ConduitMode mode)
    {
        var tiles = new List<Vector2Int>();
        EligibleTiles(fog, rect, mode, tiles);
        foreach (Vector2Int tile in tiles)
        {
            if (mode == ConduitMode.Remove)
            {
                RemoveConduit(tile);
                continue;
            }
            pending.Add(tile);
            ConduitChanged?.Invoke(tile);
        }
        return tiles.Count;
    }

    /// <summary>
    /// Imp work on a pending tile: adds deltaTime / wireSecondsPerTile to its progress and builds it on reaching full.
    /// True when this call built it. No-op (false) for a tile that isn't pending.
    /// </summary>
    public bool AddWireProgress(Vector2Int tile, float deltaTime)
    {
        if (!pending.Contains(tile)) return false;
        float p = ProgressOf(tile) + (wireSecondsPerTile > 0f ? deltaTime / wireSecondsPerTile : 1f);
        if (p < 1f - CompleteTolerance)
        {
            progress[tile] = p;
            return false;
        }
        CompleteConduit(tile);
        return true;
    }

    /// <summary>Pending → built (the imp's completion; tests and staging call it directly). False if it wasn't pending.</summary>
    public bool CompleteConduit(Vector2Int tile)
    {
        if (!pending.Remove(tile)) return false;
        progress.Remove(tile);
        built.Add(tile);
        topologyDirty = true;
        ConduitChanged?.Invoke(tile);
        return true;
    }

    /// <summary>Removes the tile's conduit, pending or built (progress goes with it). False if it had none.</summary>
    public bool RemoveConduit(Vector2Int tile)
    {
        bool wasPending = pending.Remove(tile);
        bool wasBuilt = built.Remove(tile);
        progress.Remove(tile);
        if (!wasPending && !wasBuilt) return false;
        if (wasBuilt) topologyDirty = true;
        ConduitChanged?.Invoke(tile);
        return true;
    }

    // ---- nodes ----

    /// <summary>A built power node joins the service (PowerNode calls this on build-complete). Idempotent.</summary>
    public void Register(PowerNode node)
    {
        if (node == null || nodes.Contains(node)) return;
        nodes.Add(node);
    }

    /// <summary>A node leaves the service (destroyed). Idempotent.</summary>
    public void Unregister(PowerNode node) => nodes.Remove(node);

    public int NodeCount
    {
        get
        {
            nodes.RemoveAll(n => n == null);
            return nodes.Count;
        }
    }

    /// <summary>The grid id a node standing on this tile connects to, or -1 when it touches no built conduit.</summary>
    public int GridAt(Vector2Int nodeTile) => GridAt(Topology(), nodeTile);

    /// <summary>The live totals of a node's grid: whether it's connected, and that grid's supply and demand.</summary>
    public GridTotals TotalsFor(PowerNode node)
    {
        if (node == null || !node.IsBuilt) return new GridTotals(false, 0, 0);
        Dictionary<Vector2Int, int> topology = Topology();
        int grid = GridAt(topology, node.Tile);
        if (grid < 0) return new GridTotals(false, 0, 0);

        nodes.RemoveAll(n => n == null);
        var states = new List<NodeState>(nodes.Count);
        foreach (PowerNode n in nodes) states.Add(n.State);
        Sum(states, topology, grid, out int supply, out int demand);
        return new GridTotals(true, supply, demand);
    }

    public bool IsConnected(PowerNode node) => TotalsFor(node).Connected;

    /// <summary>
    /// The seam (#142): powered ⟺ connected and the grid's supply ≥ its demand (equality is powered). #143: a disabled
    /// node is never powered.
    /// </summary>
    public bool IsPowered(PowerNode node)
    {
        if (node == null) return false;
        GridTotals totals = TotalsFor(node);
        return IsNodePowered(node.Enabled, totals.Connected, totals.Supply, totals.Demand);
    }

    private Dictionary<Vector2Int, int> Topology()
    {
        if (!topologyDirty) return components;
        components = Components(built);
        topologyDirty = false;
        return components;
    }

    // ---- the math (static, over plain data) ----

    /// <summary>
    /// One node as the grid math sees it: where it is, what it does, its watts, whether it's active now, and (#143) whether
    /// the player has it enabled.
    /// </summary>
    public readonly struct NodeState
    {
        public readonly Vector2Int Tile;
        public readonly PowerRole Role;
        public readonly int Watts;
        public readonly bool Active;  // A producer counts only while active (a wheel: tended; a furnace: burning)
        public readonly bool Enabled; // #143: a disabled node counts for nothing, producer or consumer

        public NodeState(Vector2Int tile, PowerRole role, int watts, bool active, bool enabled = true)
        {
            Tile = tile;
            Role = role;
            Watts = watts;
            Active = active;
            Enabled = enabled;
        }
    }

    /// <summary>A node's grid: connected or not, and the grid's live supply and demand (0 when unconnected).</summary>
    public readonly struct GridTotals
    {
        public readonly bool Connected;
        public readonly int Supply;
        public readonly int Demand;

        public GridTotals(bool connected, int supply, int demand)
        {
            Connected = connected;
            Supply = supply;
            Demand = demand;
        }
    }

    private static readonly Vector2Int[] Neighbors =
    {
        new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(-1, 0), new Vector2Int(0, -1),
    };

    /// <summary>
    /// Labels each built conduit tile with its grid: the connected components under 4-adjacency, numbered 0, 1, … in the
    /// scan order of each component's first tile (x, then y), so the ids are deterministic.
    /// </summary>
    public static Dictionary<Vector2Int, int> Components(IEnumerable<Vector2Int> builtTiles)
    {
        var labels = new Dictionary<Vector2Int, int>();
        var set = new HashSet<Vector2Int>(builtTiles);
        var queue = new Queue<Vector2Int>();
        int next = 0;
        foreach (Vector2Int seed in ScanOrdered(set))
        {
            if (labels.ContainsKey(seed)) continue;
            labels[seed] = next;
            queue.Enqueue(seed);
            while (queue.Count > 0)
            {
                Vector2Int tile = queue.Dequeue();
                foreach (Vector2Int d in Neighbors)
                {
                    Vector2Int n = tile + d;
                    if (!set.Contains(n) || labels.ContainsKey(n)) continue;
                    labels[n] = next;
                    queue.Enqueue(n);
                }
            }
            next++;
        }
        return labels;
    }

    /// <summary>
    /// The grid a node on nodeTile connects to: its own tile's, else the first 4-neighbor's with built conduit (right, up,
    /// left, down). -1 when none.
    /// </summary>
    public static int GridAt(IReadOnlyDictionary<Vector2Int, int> components, Vector2Int nodeTile)
    {
        if (components.TryGetValue(nodeTile, out int own)) return own;
        foreach (Vector2Int d in Neighbors)
            if (components.TryGetValue(nodeTile + d, out int grid)) return grid;
        return -1;
    }

    /// <summary>
    /// One grid's live totals: supply = its active producers' watts, demand = its consumers' watts. Disabled nodes (#143)
    /// are skipped entirely.
    /// </summary>
    public static void Sum(IEnumerable<NodeState> nodeStates, IReadOnlyDictionary<Vector2Int, int> components, int grid,
        out int supply, out int demand)
    {
        supply = 0;
        demand = 0;
        if (grid < 0) return;
        foreach (NodeState node in nodeStates)
        {
            if (!node.Enabled || GridAt(components, node.Tile) != grid) continue;
            if (node.Role == PowerRole.Producer && node.Active) supply += node.Watts;
            else if (node.Role == PowerRole.Consumer) demand += node.Watts;
        }
    }

    /// <summary>The powered rule: connected, and supply covers demand (equality is powered).</summary>
    public static bool IsPowered(bool connected, int supply, int demand) => connected && supply >= demand;

    /// <summary>A node's powered rule (#143): enabled, and the powered rule for its grid. A disabled node is never powered.</summary>
    public static bool IsNodePowered(bool enabled, bool connected, int supply, int demand) =>
        enabled && IsPowered(connected, supply, demand);

    private static List<Vector2Int> ScanOrdered(IEnumerable<Vector2Int> tiles)
    {
        var list = new List<Vector2Int>(tiles);
        list.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        return list;
    }
}
