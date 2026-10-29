using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// An imp's stats block (#161). Every value is a multiplier, 1.0 = the standard imp. v1 candidates all carry the
/// defaults and nothing reads them yet: stat variation (and what each multiplier scales) is a follow-up.
/// </summary>
[System.Serializable]
public struct ImpStats
{
    public float DigSpeed;
    public float BuildSpeed;
    public float MoveSpeed;

    public static ImpStats Default => new ImpStats { DigSpeed = 1f, BuildSpeed = 1f, MoveSpeed = 1f };
}

/// <summary>One imp the Heart offers this cycle (#161): a name in the imp convention (Imp2, Imp3, …) and its stats.</summary>
public sealed class ImpCandidate
{
    public string Name { get; }
    public int Number { get; }
    public ImpStats Stats { get; }

    public ImpCandidate(string name, int number, ImpStats stats)
    {
        Name = name;
        Number = number;
        Stats = stats;
    }
}

/// <summary>
/// Dungeon Heart printing (#161): the Heart offers candidate imps on a fixed cycle and the player picks one to birth, or
/// none. Nothing spawns on its own, ever: the only way an imp appears is Pick.
/// Cycle: game time (Time.deltaTime, so it stops while paused and doubles at 2×) on fixed boundaries from game start.
/// Cycle k's list is available from k × cycleSeconds; the first is there at game start. Each boundary replaces the list
/// with candidatesPerCycle fresh candidates, whatever happened to the last one. At most one pick per cycle, and picking
/// doesn't move the boundaries. Declining (or ignoring the list) spawns nothing and waits for the next boundary.
/// Names continue the imps on the board: a list is numbered from one past the highest imp number present (the starter
/// "Imp" counts as Imp1), so an unpicked list's numbers come round again and a picked imp's number is never reused.
/// Pick spawns the imp at the starter imp's spawn tile (the cavern center, Imp.GetCavernCenterTile), or, when that tile is
/// blocked or occupied, at the walkable, explored, free Floor tile nearest the Heart by walking distance. The new imp gets
/// the starter's wiring: an Imp and ImpDigger with the starter's serialized values, the board, renderer, placement
/// manager, stockpile, power grid and mana flow, and it reveals fog. Picking costs nothing.
/// </summary>
[DisallowMultipleComponent]
public class HeartPrinting : MonoBehaviour
{
    public enum CycleState { Open, Picked, Declined }

    [Header("References")]
    [SerializeField] private DungeonBoard dungeonBoard;
    [SerializeField] private BoardRenderer boardRenderer;
    [SerializeField] private Heart heart;
    [SerializeField] private Imp templateImp;                  // The starter: printed imps copy its serialized values
    [SerializeField] private PlacementManager placementManager;
    [SerializeField] private Stockpile stockpile;
    [SerializeField] private PowerGrid powerGrid;
    [SerializeField] private ManaFlow manaFlow;
    [SerializeField] private FogOfWar fogOfWar;                // Optional: unwired, every tile counts as explored

    [Header("Cycle")]
    [SerializeField] private float cycleSeconds = 300f;        // Game seconds between candidate lists
    [SerializeField] private int candidatesPerCycle = 3;

    [Header("Candidates")]
    [SerializeField] private string namePrefix = "Imp";        // The imp naming convention: Imp1, Imp2, …
    [SerializeField] private ImpStats candidateStats = new ImpStats { DigSpeed = 1f, BuildSpeed = 1f, MoveSpeed = 1f };

    public DungeonBoard Board { get => dungeonBoard; set => dungeonBoard = value; }
    public BoardRenderer Renderer { get => boardRenderer; set => boardRenderer = value; }
    public Heart Heart { get => heart; set => heart = value; }
    public Imp TemplateImp { get => templateImp; set => templateImp = value; }
    public PlacementManager PlacementManager { get => placementManager; set => placementManager = value; }
    public Stockpile Stockpile { get => stockpile; set => stockpile = value; }
    public PowerGrid PowerGrid { get => powerGrid; set => powerGrid = value; }
    public ManaFlow ManaFlow { get => manaFlow; set => manaFlow = value; }
    public FogOfWar FogOfWar { get => fogOfWar; set => fogOfWar = value; }
    public float CycleSeconds { get => cycleSeconds; set => cycleSeconds = value; }
    public int CandidatesPerCycle { get => candidatesPerCycle; set => candidatesPerCycle = value; }
    public ImpStats CandidateStats { get => candidateStats; set => candidateStats = value; }

    /// <summary>Game seconds since the cycle clock started (game start).</summary>
    public float Elapsed { get; private set; }

    /// <summary>The current cycle's index: 0 from game start, then one more per boundary. -1 before the first Tick.</summary>
    public int Cycle { get; private set; } = -1;

    /// <summary>Open while the current list can be picked from; Picked or Declined once it's been used.</summary>
    public CycleState State { get; private set; }

    /// <summary>The current list. Empty once this cycle's pick or decline is made (and before the first Tick).</summary>
    public IReadOnlyList<ImpCandidate> Candidates => candidates;

    /// <summary>The name of the imp picked this cycle, or null.</summary>
    public string PickedName { get; private set; }

    /// <summary>How many imps the Heart has printed this run.</summary>
    public int ImpsPrinted { get; private set; }

    /// <summary>Game seconds until the next boundary replaces the list.</summary>
    public float SecondsToNextCycle => cycleSeconds > 0f ? Mathf.Max(0f, (Cycle + 1) * cycleSeconds - Elapsed) : 0f;

    /// <summary>Raised once when a pick spawns an imp, with the new imp.</summary>
    public event System.Action<Imp> ImpPrinted;

    private readonly List<ImpCandidate> candidates = new List<ImpCandidate>();

    private void Start()
    {
        dungeonBoard ??= FindAnyObjectByType<DungeonBoard>();
        boardRenderer ??= FindAnyObjectByType<BoardRenderer>();
        heart ??= FindAnyObjectByType<Heart>();
        templateImp ??= FindAnyObjectByType<Imp>(); // Before any pick: the scene's only imp is the starter
        placementManager ??= FindAnyObjectByType<PlacementManager>();
        stockpile ??= FindAnyObjectByType<Stockpile>();
        powerGrid ??= FindAnyObjectByType<PowerGrid>();
        manaFlow ??= FindAnyObjectByType<ManaFlow>();
        fogOfWar ??= FindAnyObjectByType<FogOfWar>();

        Tick(0f); // The first list is available at game start
    }

    private void Update()
    {
        Tick(Time.deltaTime); // Game time: 0 while paused, doubled at 2×
    }

    /// <summary>
    /// Advances the cycle clock by deltaTime game seconds and starts a new cycle when a boundary has passed (several at
    /// once still make one new list, for the latest cycle). Never spawns anything. Called from Update; public for tests.
    /// </summary>
    public void Tick(float deltaTime)
    {
        Elapsed += Mathf.Max(0f, deltaTime);
        int cycle = cycleSeconds > 0f ? Mathf.FloorToInt(Elapsed / cycleSeconds) : 0;
        if (cycle != Cycle) BeginCycle(cycle);
    }

    private void BeginCycle(int cycle)
    {
        Cycle = cycle;
        State = CycleState.Open;
        PickedName = null;
        candidates.Clear();
        int first = HighestImpNumber() + 1;
        for (int i = 0; i < candidatesPerCycle; i++)
            candidates.Add(new ImpCandidate(namePrefix + (first + i), first + i, candidateStats));
    }

    /// <summary>
    /// The highest imp number in the game, read live: a name "Imp7" is 7, and any other imp (the starter, "Imp") is 1.
    /// 0 with no imps. #163: inactive imps count too: an imp away on an expedition is deactivated, not destroyed, and
    /// comes back, so its number must never be offered again while it's gone.
    /// </summary>
    public int HighestImpNumber()
    {
        int highest = 0;
        foreach (Imp imp in FindObjectsByType<Imp>(FindObjectsInactive.Include)) highest = Mathf.Max(highest, NumberOf(imp.name));
        return highest;
    }

    private int NumberOf(string impName) =>
        impName.StartsWith(namePrefix) && int.TryParse(impName.Substring(namePrefix.Length), out int n) && n > 0 ? n : 1;

    /// <summary>
    /// Picks a candidate from the current list: spawns it as a working imp and closes this cycle (no second pick until
    /// the next boundary). Free. Returns the new imp, or null (and nothing changes) when the cycle isn't open, the
    /// candidate isn't on the current list, the Heart is destroyed, or no spawn tile is free.
    /// </summary>
    public Imp Pick(ImpCandidate candidate)
    {
        if (State != CycleState.Open || candidate == null || !candidates.Contains(candidate)) return null;
        if (heart != null && heart.IsDestroyed) return null;
        if (dungeonBoard == null || !TryFindSpawnTile(out Vector2Int tile)) return null;

        Imp imp = SpawnImp(candidate, tile);
        State = CycleState.Picked;
        PickedName = candidate.Name;
        candidates.Clear();
        ImpsPrinted++;
        ImpPrinted?.Invoke(imp);
        return imp;
    }

    /// <summary>Picks the candidate at this index of the current list (see Pick). Null when out of range.</summary>
    public Imp Pick(int index) => index >= 0 && index < candidates.Count ? Pick(candidates[index]) : null;

    /// <summary>
    /// Declines this cycle's list ("wait for next cycle"): nothing spawns, the list is gone, and the next boundary brings
    /// a new one. False when the cycle isn't open.
    /// </summary>
    public bool DeclineAll()
    {
        if (State != CycleState.Open) return false;
        State = CycleState.Declined;
        candidates.Clear();
        return true;
    }

    // ---- spawn ----

    /// <summary>The preferred spawn tile: the starter imp's own spawn, the cavern center.</summary>
    public Vector2Int PreferredSpawnTile =>
        dungeonBoard != null ? Imp.GetCavernCenterTile(dungeonBoard.Width, dungeonBoard.Height, dungeonBoard.CavernSize)
            : Vector2Int.zero;

    /// <summary>
    /// Where a pick would spawn now: the preferred tile when it's free, else the free tile nearest the Heart by walking
    /// distance (breadth-first over walkable tiles, neighbours in +x, −x, +y, −y order). False when none is free.
    /// </summary>
    public bool TryFindSpawnTile(out Vector2Int tile)
    {
        tile = PreferredSpawnTile;
        if (dungeonBoard == null) return false;
        if (IsFreeSpawnTile(tile)) return true;

        return FindNearestFreeTile(dungeonBoard, heart != null ? heart.Tile : tile, IsFreeSpawnTile, out tile);
    }

    private static readonly Vector2Int[] Steps = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };

    /// <summary>
    /// The tile nearest origin by walking distance for which isFree holds: breadth-first from origin (origin itself first,
    /// whether or not it's walkable, then over walkable tiles, neighbours in +x, −x, +y, −y order). False when none is
    /// reachable. Shared by Heart printing (#161) and the expedition's returning goblin (#163, from the door).
    /// </summary>
    public static bool FindNearestFreeTile(DungeonBoard board, Vector2Int origin, System.Func<Vector2Int, bool> isFree,
        out Vector2Int tile)
    {
        tile = origin;
        if (board == null || isFree == null) return false;
        var seen = new HashSet<Vector2Int> { origin };
        var queue = new Queue<Vector2Int>();
        queue.Enqueue(origin);
        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            if (isFree(current))
            {
                tile = current;
                return true;
            }
            foreach (Vector2Int step in Steps)
            {
                Vector2Int next = current + step;
                if (!board.IsWalkable(next.x, next.y) || !seen.Add(next)) continue;
                queue.Enqueue(next);
            }
        }
        return false;
    }

    /// <summary>
    /// A tile a printed imp may appear on: walkable Floor, explored, not the Heart's tile, with no placeable (site or
    /// built) on it and no living imp, goblin or hero standing on it.
    /// </summary>
    public bool IsFreeSpawnTile(Vector2Int tile) => IsFreeTile(dungeonBoard, fogOfWar, heart, placementManager, tile);

    /// <summary>
    /// The free-tile rules (#161, reused by #163): walkable Floor, explored (an unwired fog counts as explored), not the
    /// Heart's tile, no placeable (site or built) on it, and no living (active) imp, goblin or hero standing on it.
    /// </summary>
    public static bool IsFreeTile(DungeonBoard board, FogOfWar fog, Heart heart, PlacementManager placementManager,
        Vector2Int tile)
    {
        if (board == null || !board.IsWalkable(tile.x, tile.y)) return false;
        if (!FogOfWar.IsSeen(fog, tile)) return false;
        if (heart != null && heart.Tile == tile) return false;
        if (placementManager != null && placementManager.GetAt(tile) != null) return false;
        foreach (Imp imp in FindObjectsByType<Imp>())
            if (imp.CurrentTile == tile) return false;
        foreach (Goblin goblin in FindObjectsByType<Goblin>())
            if (!goblin.IsDead && goblin.CurrentTile == tile) return false;
        foreach (Hero hero in FindObjectsByType<Hero>())
            if (!hero.IsDead && hero.CurrentTile == tile) return false;
        return true;
    }

    // The standard imp setup, as on the scene's starter: Imp + ImpDigger with the starter's serialized values, then every
    // reference set explicitly (the copy would otherwise point the digger at the starter's Imp).
    private Imp SpawnImp(ImpCandidate candidate, Vector2Int tile)
    {
        var go = new GameObject(candidate.Name);
        Imp imp = go.AddComponent<Imp>();
        ImpDigger digger = go.AddComponent<ImpDigger>();

        if (templateImp != null)
        {
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(templateImp), imp);
            if (templateImp.TryGetComponent(out ImpDigger templateDigger))
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(templateDigger), digger);
        }

        imp.Board = dungeonBoard;
        imp.Renderer = boardRenderer;
        imp.Stats = candidate.Stats;
        digger.Board = dungeonBoard;
        digger.Imp = imp;
        digger.PlacementManager = placementManager;
        digger.Stockpile = stockpile;
        digger.PowerGrid = powerGrid;
        digger.ManaFlow = manaFlow;

        imp.PlaceOnTile(tile); // Before its Start, so it stays here instead of spawning on the cavern center
        fogOfWar?.AddRevealer(imp);
        return imp;
    }
}
