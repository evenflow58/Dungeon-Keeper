using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A goblin's simplified utility AI (DESIGN §A.5): Hunger below hungerThreshold → eat at the nearest
/// stocked Mushroom Plot; else Energy below energyThreshold → claim the nearest free Lair Cot and sleep
/// on it; else wander. The Goblin component still owns movement and the needs clock; this driver only
/// reads needs and issues orders. It commits to one goal at a time (ImpDigger-shaped): decisions happen
/// when a goal completes or drops, and on the decision poll during wander pauses and sleep — never
/// mid-travel.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Goblin))]
public class GoblinAI : MonoBehaviour
{
    public enum Goal { None, Eat, Sleep, Wander }

    [Header("References")]
    [SerializeField] private Goblin goblin;
    [SerializeField] private PlacementManager placementManager; // Source of plots and cots; null = wander only

    [Header("Thresholds (DESIGN §A.5)")]
    [SerializeField] private float hungerThreshold = 30f; // Seek food when Hunger is below this
    [SerializeField] private float energyThreshold = 25f; // Seek a cot when Energy is below this

    [Header("Restoring (provisional)")]
    [SerializeField] private float hungerPerFood = 50f;       // Hunger restored per food eaten
    [SerializeField] private float sleepSecondsToFull = 10f;  // Sleeping restores Energy 0→100 over this long

    [Header("Pacing")]
    [SerializeField] private float decisionIntervalSeconds = 0.25f; // Need re-check poll while pausing or sleeping
    [SerializeField] private float wanderPauseSeconds = 1.5f;       // Idle pause before each wander leg

    public Goblin Goblin { get => goblin; set => goblin = value; }
    public PlacementManager PlacementManager { get => placementManager; set => placementManager = value; }
    public float HungerThreshold { get => hungerThreshold; set => hungerThreshold = value; }
    public float EnergyThreshold { get => energyThreshold; set => energyThreshold = value; }
    public float HungerPerFood { get => hungerPerFood; set => hungerPerFood = value; }
    public float SleepSecondsToFull { get => sleepSecondsToFull; set => sleepSecondsToFull = value; }
    public float DecisionIntervalSeconds { get => decisionIntervalSeconds; set => decisionIntervalSeconds = value; }
    public float WanderPauseSeconds { get => wanderPauseSeconds; set => wanderPauseSeconds = value; }

    /// <summary>Random source for wander targets; tests replace it with a seeded one.</summary>
    public System.Random Rng { get; set; } = new System.Random();

    public Goal CurrentGoal { get; private set; }

    /// <summary>True only while lying on ClaimedCot (not while walking to it).</summary>
    public bool IsSleeping { get; private set; }

    /// <summary>The cot this goblin holds a claim on: from selection, through travel and sleep, until it wakes.</summary>
    public Placeable ClaimedCot { get; private set; }

    /// <summary>The plot this goblin is walking to eat from (Eat goal only).</summary>
    public MushroomPlot TargetPlot { get; private set; }

    private Vector2Int standPoint;   // Eat: the tile beside TargetPlot
    private bool wanderTraveling;    // Wander: false = pausing, true = walking the leg
    private float wanderPauseTimer;
    private float decisionTimer;

    private DungeonBoard Board => goblin.Board;

    private void Start()
    {
        goblin ??= GetComponent<Goblin>();
        placementManager ??= FindAnyObjectByType<PlacementManager>();
    }

    private void Update()
    {
        Tick(Time.deltaTime);
    }

    // Death deactivates the goblin's GameObject (this component included), so Update stops before Tick
    // could see IsDead. Releasing here frees the cot the moment the goblin dies. Guarded on IsDead so an
    // ordinary disable doesn't drop a live goblin's claim.
    private void OnDisable()
    {
        if (goblin != null && goblin.IsDead) ClearAll();
    }

    /// <summary>
    /// Advances the AI by deltaTime. Called from Update; public so tests can step it after Goblin.Tick.
    /// </summary>
    public void Tick(float deltaTime)
    {
        if (goblin == null || Board == null) return;
        if (goblin.IsDead)
        {
            ClearAll(); // Also covers stepped tests, where OnDisable doesn't fire
            return;
        }

        switch (CurrentGoal)
        {
            case Goal.None: Decide(); break;
            case Goal.Eat: TickEat(); break;
            case Goal.Sleep: TickSleep(deltaTime); break;
            case Goal.Wander: TickWander(deltaTime); break;
        }
    }

    // ---- decisions ----

    // Strict §A.5 priority. Eat and Sleep fall through when they can't be serviced (nothing stocked or free,
    // or unreachable), so a needy goblin keeps wandering and retries on the poll instead of stalling.
    private void Decide()
    {
        if (TryStartEat()) return;
        if (TryStartSleep()) return;
        StartWander();
    }

    private bool TryStartEat()
    {
        if (placementManager == null || goblin.Hunger >= hungerThreshold) return false;
        if (!TrySelectPlot(Board, placementManager.GetMushroomPlots(), goblin.CurrentTile, out MushroomPlot plot, out Vector2Int stand))
            return false;
        if (!goblin.SetDestination(stand)) return false;

        ResetGoalState();
        CurrentGoal = Goal.Eat;
        TargetPlot = plot;
        standPoint = stand;
        return true;
    }

    private bool TryStartSleep()
    {
        if (placementManager == null || goblin.Energy >= energyThreshold) return false;

        List<Placeable> cots = placementManager.GetCots();
        // A failed claim means another goblin just took that cot; it's now IsClaimed, so reselect.
        for (int attempt = 0; attempt < cots.Count; attempt++)
        {
            if (!TrySelectCot(Board, cots, goblin.CurrentTile, out Placeable cot)) return false;
            if (!cot.TryClaim()) continue;

            if (!goblin.SetDestination(cot.Tile))
            {
                cot.Release();
                return false;
            }

            ResetGoalState();
            CurrentGoal = Goal.Sleep;
            ClaimedCot = cot; // Held from selection, so no other goblin can pick it while this one walks over
            return true;
        }
        return false;
    }

    private void StartWander()
    {
        ResetGoalState();
        CurrentGoal = Goal.Wander;
    }

    /// <summary>Goal finished or dropped: decide again on the very next Tick.</summary>
    private void EndGoal()
    {
        ReleaseCot();
        ResetGoalState();
        CurrentGoal = Goal.None;
    }

    private void ResetGoalState()
    {
        TargetPlot = null;
        IsSleeping = false;
        wanderTraveling = false;
        wanderPauseTimer = 0f;
        decisionTimer = 0f;
    }

    private void ReleaseCot()
    {
        if (ClaimedCot != null) ClaimedCot.Release();
        ClaimedCot = null;
        IsSleeping = false;
    }

    private void ClearAll()
    {
        ReleaseCot();
        ResetGoalState();
        CurrentGoal = Goal.None;
    }

    // ---- goal execution ----

    private void TickEat()
    {
        if (TargetPlot == null)
        {
            EndGoal(); // Plot gone
            return;
        }
        if (goblin.IsMoving) return;

        bool ate = goblin.CurrentTile == standPoint && TargetPlot.TryTakeFood();
        if (ate) goblin.Hunger += hungerPerFood; // The setter clamps at 100
        EndGoal(); // Ate (one food per trip), or the last food went to another goblin: re-decide either way
    }

    private void TickSleep(float deltaTime)
    {
        if (ClaimedCot == null)
        {
            EndGoal(); // Cot gone
            return;
        }

        if (!IsSleeping)
        {
            if (goblin.IsMoving) return;
            if (goblin.CurrentTile != ClaimedCot.Tile)
            {
                EndGoal(); // Ended up somewhere else: give the cot back
                return;
            }
            IsSleeping = true;
            decisionTimer = 0f;
        }

        goblin.Energy += Goblin.MaxNeed / sleepSecondsToFull * deltaTime;
        if (goblin.Energy >= Goblin.MaxNeed)
        {
            EndGoal(); // Rested: wake and release
            return;
        }

        // Hunger outranks sleep, but only when there's food to go to; otherwise a starving, tired goblin
        // with no stock would wake and re-sleep every tick.
        decisionTimer += deltaTime;
        if (decisionTimer < decisionIntervalSeconds) return;
        decisionTimer = 0f;
        if (goblin.Hunger < hungerThreshold && HasReachableFood())
        {
            EndGoal();
            Decide(); // Wake straight into the eat trip
        }
    }

    private void TickWander(float deltaTime)
    {
        if (wanderTraveling)
        {
            if (!goblin.IsMoving) EndGoal(); // Leg done
            return;
        }

        // Standing still between legs: poll for a need that has become serviceable.
        wanderPauseTimer += deltaTime;
        decisionTimer += deltaTime;
        if (decisionTimer >= decisionIntervalSeconds)
        {
            decisionTimer = 0f;
            if (TryStartEat() || TryStartSleep()) return;
        }
        if (wanderPauseTimer < wanderPauseSeconds) return;

        Vector2Int target = PickWanderTarget(Board, goblin.CurrentTile, Rng);
        if (target == goblin.CurrentTile || !goblin.SetDestination(target))
        {
            EndGoal(); // Nowhere to go this time: pause again and retry
            return;
        }
        wanderTraveling = true;
    }

    private bool HasReachableFood() =>
        placementManager != null &&
        TrySelectPlot(Board, placementManager.GetMushroomPlots(), goblin.CurrentTile, out _, out _);

    // ---- selection (static for tests) ----

    /// <summary>
    /// Nearest plot with FoodCount &gt; 0, by FindPathToNeighbor path length (the goblin eats from beside
    /// the plot, never standing on it). Ties keep list (scan) order; unreachable plots are skipped.
    /// </summary>
    public static bool TrySelectPlot(DungeonBoard board, IReadOnlyList<MushroomPlot> plots, Vector2Int fromTile,
        out MushroomPlot plot, out Vector2Int standPoint)
    {
        plot = null;
        standPoint = default;
        int bestCost = int.MaxValue;

        foreach (MushroomPlot candidate in plots)
        {
            if (candidate == null || candidate.FoodCount <= 0) continue;

            List<Vector2Int> path = Pathfinder.FindPathToNeighbor(board, fromTile, candidate.Tile);
            if (path.Count == 0 || path.Count >= bestCost) continue; // Unreachable, or not strictly nearer

            bestCost = path.Count;
            plot = candidate;
            standPoint = path[path.Count - 1];
        }
        return plot != null;
    }

    /// <summary>
    /// Nearest unclaimed Lair Cot by FindPath length to the cot tile itself (the goblin sleeps on it).
    /// Ties keep list (scan) order; unreachable cots are skipped. Doesn't claim.
    /// </summary>
    public static bool TrySelectCot(DungeonBoard board, IReadOnlyList<Placeable> cots, Vector2Int fromTile, out Placeable cot)
    {
        cot = null;
        int bestCost = int.MaxValue;

        foreach (Placeable candidate in cots)
        {
            if (candidate == null || candidate.IsClaimed) continue;

            List<Vector2Int> path = Pathfinder.FindPath(board, fromTile, candidate.Tile);
            if (path.Count == 0 || path.Count >= bestCost) continue;

            bestCost = path.Count;
            cot = candidate;
        }
        return cot != null;
    }

    /// <summary>
    /// A random walkable tile reachable from fromTile: up to 32 random picks among all walkable tiles
    /// (collected in scan order), returning the first with a path. Returns fromTile if none pans out.
    /// </summary>
    public static Vector2Int PickWanderTarget(DungeonBoard board, Vector2Int fromTile, System.Random rng)
    {
        var walkable = new List<Vector2Int>();
        for (int x = 0; x < board.Width; x++)
        {
            for (int y = 0; y < board.Height; y++)
            {
                if (board.IsWalkable(x, y)) walkable.Add(new Vector2Int(x, y));
            }
        }
        if (walkable.Count == 0) return fromTile;

        for (int i = 0; i < 32; i++)
        {
            Vector2Int pick = walkable[rng.Next(walkable.Count)];
            if (Pathfinder.FindPath(board, fromTile, pick).Count > 0) return pick;
        }
        return fromTile;
    }
}
