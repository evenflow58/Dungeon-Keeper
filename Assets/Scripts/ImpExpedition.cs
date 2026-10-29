using UnityEngine;

/// <summary>
/// Goblin recruitment by imp expedition (#163). The player selects an imp and sends it out through the door (the tile
/// heroes enter by). The order is offered only while the door is dug out, a walkable path leads from the imp to it, and
/// no other expedition is out: one at a time, "out" spanning order to return.
/// - Ordered: the imp drops its job (the job stays in the pool for the other imps) and is committed: it walks to the door
///   taking no jobs on the way (ImpDigger.OnExpedition) and departs when it steps onto the door tile.
/// - Away: its GameObject is deactivated, not destroyed (the same imp comes back). It takes no jobs and, its tile never
///   changing, reveals no fog. The expedition counts expeditionSeconds of game time (Time.deltaTime: it stops while the
///   game is paused and runs double at 2×).
/// - Return: the outcome is rolled once (Roll, a test seam: success when the roll is below successChance). On success a
///   goblin comes back with the imp, on the free tile nearest the door (the #161 free-tile rules), unleashed (every
///   recruit is), named after the goblins already in the game (Goblin4 after Goblin1–3, never a living goblin's name).
///   If no free tile exists the expedition stays out and retries every tick. The imp reappears on the door tile, steps
///   in, and goes back to work. No other outcome exists: the imp never dies and nothing follows it in.
/// Tuning (duration, success chance) is serialized.
/// </summary>
[DisallowMultipleComponent]
public class ImpExpedition : MonoBehaviour
{
    public enum ExpeditionPhase { None, Departing, Away }

    public const string DoorSealedReason = "The door is sealed: dig it out first.";
    public const string NoPathReason = "No path to the door.";
    public const string AlreadyOutReason = "An expedition is already out.";
    public const string NoImpReason = "No imp selected.";

    [Header("References")]
    [SerializeField] private DungeonBoard dungeonBoard;
    [SerializeField] private BoardRenderer boardRenderer;
    [SerializeField] private PlacementManager placementManager;
    [SerializeField] private Heart heart;
    [SerializeField] private FogOfWar fogOfWar;          // Optional: unwired, every tile counts as explored
    [SerializeField] private Goblin templateGoblin;      // A starting goblin: recruits copy its serialized values

    [Header("Expedition")]
    [SerializeField] private float expeditionSeconds = 120f;              // Game seconds away
    [SerializeField, Range(0f, 1f)] private float successChance = 0.75f;  // Chance of coming back with a goblin
    [SerializeField] private string goblinNamePrefix = "Goblin";          // The goblin naming convention: Goblin1, Goblin2, …

    public DungeonBoard Board { get => dungeonBoard; set => dungeonBoard = value; }
    public BoardRenderer Renderer { get => boardRenderer; set => boardRenderer = value; }
    public PlacementManager PlacementManager { get => placementManager; set => placementManager = value; }
    public Heart Heart { get => heart; set => heart = value; }
    public FogOfWar FogOfWar { get => fogOfWar; set => fogOfWar = value; }
    public Goblin TemplateGoblin { get => templateGoblin; set => templateGoblin = value; }
    public float ExpeditionSeconds { get => expeditionSeconds; set => expeditionSeconds = value; }
    public float SuccessChance { get => successChance; set => successChance = value; }

    /// <summary>
    /// The outcome roll (test seam): a value in [0, 1); the expedition succeeds when it's below SuccessChance. Null uses
    /// UnityEngine.Random.value.
    /// </summary>
    public System.Func<float> Roll { get; set; }

    public ExpeditionPhase Phase { get; private set; }

    /// <summary>True from an order until the imp is back (departing counts).</summary>
    public bool IsActive => Phase != ExpeditionPhase.None;

    /// <summary>The imp on the current expedition, or null.</summary>
    public Imp ExpeditionImp { get; private set; }

    /// <summary>Game seconds the current expedition has been away (0 while departing or with none out).</summary>
    public float SecondsAway { get; private set; }

    /// <summary>Game seconds until the imp is due back (the full duration while departing; 0 with none out).</summary>
    public float SecondsRemaining =>
        Phase == ExpeditionPhase.None ? 0f : Mathf.Max(0f, expeditionSeconds - SecondsAway);

    /// <summary>How many expeditions have come back this run.</summary>
    public int ReturnCount { get; private set; }

    /// <summary>How many of those brought a goblin.</summary>
    public int GoblinsRecruited { get; private set; }

    /// <summary>The imp that came back last, or null.</summary>
    public Imp LastReturnedImp { get; private set; }

    /// <summary>The goblin the last expedition brought back, or null when it came back empty-handed (or none has).</summary>
    public Goblin LastRecruit { get; private set; }

    /// <summary>The player-facing line for the last return (the toast), or null before any.</summary>
    public string LastReturnMessage { get; private set; }

    /// <summary>Raised once per return, with the imp and its goblin (null when empty-handed).</summary>
    public event System.Action<Imp, Goblin> Returned;

    private bool? pendingSuccess; // Rolled once when the time is up; kept while waiting for a free tile

    private void Start()
    {
        dungeonBoard ??= FindAnyObjectByType<DungeonBoard>();
        boardRenderer ??= FindAnyObjectByType<BoardRenderer>();
        placementManager ??= FindAnyObjectByType<PlacementManager>();
        heart ??= FindAnyObjectByType<Heart>();
        fogOfWar ??= FindAnyObjectByType<FogOfWar>();
        templateGoblin ??= FindAnyObjectByType<Goblin>(); // At game start: one of the starting goblins
    }

    private void Update()
    {
        Tick(Time.deltaTime); // Game time: 0 while paused, doubled at 2×
    }

    // ---- the order ----

    /// <summary>Whether this imp can be sent now: see UnavailableReason.</summary>
    public bool CanSend(Imp imp) => UnavailableReason(imp) == null;

    /// <summary>
    /// Why the order isn't available for this imp right now, or null when it is: no imp, an expedition already out, the
    /// door still sealed, or no walkable path from the imp to the door.
    /// </summary>
    public string UnavailableReason(Imp imp)
    {
        if (imp == null || !imp.gameObject.activeInHierarchy) return NoImpReason;
        if (IsActive) return AlreadyOutReason;
        if (dungeonBoard == null || !dungeonBoard.IsDoorOpen) return DoorSealedReason;
        if (!HasPathToDoor(imp)) return NoPathReason;
        return null;
    }

    /// <summary>True when a walkable path leads from the imp's tile to a tile beside the door (or it stands on the door).</summary>
    public bool HasPathToDoor(Imp imp)
    {
        if (imp == null || dungeonBoard == null) return false;
        Vector2Int door = dungeonBoard.EntranceTile;
        return imp.CurrentTile == door || Pathfinder.FindPathToNeighbor(dungeonBoard, imp.CurrentTile, door).Count > 0;
    }

    /// <summary>
    /// Sends the imp out: it drops its job and walks to the door, taking no jobs on the way. False, changing nothing, when
    /// the order isn't available (CanSend).
    /// </summary>
    public bool Send(Imp imp)
    {
        if (!CanSend(imp)) return false;
        if (!imp.SetExitDestination(dungeonBoard.EntranceTile)) return false;

        if (imp.TryGetComponent(out ImpDigger digger)) digger.BeginExpedition();
        ExpeditionImp = imp;
        Phase = ExpeditionPhase.Departing;
        SecondsAway = 0f;
        pendingSuccess = null;
        return true;
    }

    // ---- the trip ----

    /// <summary>
    /// Advances the expedition by deltaTime game seconds: departs the imp when it reaches the door, counts the time away,
    /// and brings it back when the time is up. Called from Update; public for tests.
    /// </summary>
    public void Tick(float deltaTime)
    {
        if (Phase == ExpeditionPhase.None) return;
        if (ExpeditionImp == null)
        {
            Clear(); // Unity null: the imp is gone (nothing in this story destroys it)
            return;
        }

        if (Phase == ExpeditionPhase.Departing)
        {
            TickDeparting();
            return;
        }

        SecondsAway += Mathf.Max(0f, deltaTime);
        if (SecondsAway >= expeditionSeconds) TryReturn();
    }

    private void TickDeparting()
    {
        if (ExpeditionImp.IsMoving) return;
        Vector2Int door = dungeonBoard.EntranceTile;
        if (ExpeditionImp.CurrentTile == door)
        {
            Depart();
            return;
        }

        // Stopped short (the board changed under it): walk on, or give up and put the imp back to work.
        if (!ExpeditionImp.SetExitDestination(door)) Release(ExpeditionImp);
    }

    // On the door tile: out of the dungeon. Deactivated (not destroyed): the same imp returns.
    private void Depart()
    {
        Phase = ExpeditionPhase.Away;
        SecondsAway = 0f;
        ExpeditionImp.gameObject.SetActive(false);
    }

    private void TryReturn()
    {
        pendingSuccess ??= RollValue() < successChance;

        Vector2Int door = dungeonBoard.EntranceTile;
        Vector2Int goblinTile = door;
        if (pendingSuccess.Value && !TryFindGoblinTile(out goblinTile)) return; // No room by the door: stay out, retry

        Imp imp = ExpeditionImp;
        Goblin recruit = pendingSuccess.Value ? SpawnGoblin(goblinTile) : null;

        imp.PlaceOnTile(door);
        imp.gameObject.SetActive(true);
        StepInside(imp, door);
        Release(imp);

        ReturnCount++;
        if (recruit != null) GoblinsRecruited++;
        LastReturnedImp = imp;
        LastRecruit = recruit;
        LastReturnMessage = ReturnMessage(imp.name, recruit != null ? recruit.name : null);
        Returned?.Invoke(imp, recruit);
    }

    /// <summary>The return toast (short: it fits the toast width): "Imp3 back with Goblin4!" or "Imp3 back empty-handed."</summary>
    public static string ReturnMessage(string impName, string goblinName) =>
        goblinName != null ? $"{impName} back with {goblinName}!" : $"{impName} back empty-handed.";

    private float RollValue() => Roll != null ? Roll() : Random.value;

    // Off the door tile onto the walkable tile beside it; the imp's work loop takes over from there.
    private void StepInside(Imp imp, Vector2Int door)
    {
        var inside = Pathfinder.FindPathToNeighbor(dungeonBoard, door, door);
        if (inside.Count > 0) imp.SetDestination(inside[inside.Count - 1]);
    }

    // Ends the expedition and hands the imp back to its work loop.
    private void Release(Imp imp)
    {
        if (imp != null && imp.TryGetComponent(out ImpDigger digger)) digger.EndExpedition();
        Clear();
    }

    private void Clear()
    {
        Phase = ExpeditionPhase.None;
        ExpeditionImp = null;
        SecondsAway = 0f;
        pendingSuccess = null;
    }

    // ---- the recruit ----

    /// <summary>Where a returning goblin appears: the free tile nearest the door by walking distance (#161's rules).</summary>
    public bool TryFindGoblinTile(out Vector2Int tile) =>
        HeartPrinting.FindNearestFreeTile(dungeonBoard, dungeonBoard != null ? dungeonBoard.EntranceTile : Vector2Int.zero,
            t => HeartPrinting.IsFreeTile(dungeonBoard, fogOfWar, heart, placementManager, t), out tile);

    /// <summary>
    /// The highest goblin number in the game, read live (dead goblins are deactivated, so inactive ones count too): a name
    /// "Goblin7" is 7, any other goblin 0. A recruit is named one past it.
    /// </summary>
    public int HighestGoblinNumber()
    {
        int highest = 0;
        foreach (Goblin goblin in FindObjectsByType<Goblin>(FindObjectsInactive.Include))
        {
            string n = goblin.name;
            if (n.StartsWith(goblinNamePrefix) && int.TryParse(n.Substring(goblinNamePrefix.Length), out int number))
                highest = Mathf.Max(highest, number);
        }
        return highest;
    }

    // The starting goblins' setup, built in code: Health, Goblin and GoblinAI with the template's serialized values, then
    // every reference set explicitly (the copy would otherwise point at the template's board, health and goblin).
    private Goblin SpawnGoblin(Vector2Int tile)
    {
        var go = new GameObject(goblinNamePrefix + (HighestGoblinNumber() + 1));

        Health health = go.AddComponent<Health>();
        Goblin goblin = go.AddComponent<Goblin>();
        GoblinAI ai = go.AddComponent<GoblinAI>();

        if (templateGoblin != null)
        {
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(templateGoblin), goblin);
            if (templateGoblin.TryGetComponent(out GoblinAI templateAi))
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(templateAi), ai);
            if (templateGoblin.TryGetComponent(out Health templateHealth))
            {
                health.MaxHealth = templateHealth.MaxHealth;
                health.Team = templateHealth.Team;
            }
        }

        goblin.Board = dungeonBoard;
        goblin.Renderer = boardRenderer;
        goblin.Health = health;
        goblin.SpawnTile = tile;  // Its Start places it here
        goblin.PlaceOnTile(tile); // ...and so does this, for anything that reads it before then
        ai.Goblin = goblin;
        ai.PlacementManager = placementManager;
        ai.ClearLeash();          // Every recruit arrives unleashed
        return goblin;
    }
}
