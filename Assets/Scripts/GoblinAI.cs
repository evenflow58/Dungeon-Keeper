using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A goblin's simplified utility AI (DESIGN §A.5). Strict priority: an opposing-team Health within
/// aggroRange tiles → fight it; else Hunger below hungerThreshold → eat at the nearest stocked Mushroom
/// Plot; else Energy below energyThreshold → claim the nearest free Lair Cot and sleep on it; else wander.
/// The Goblin component still owns movement and the needs clock; this driver only reads state and issues
/// orders. It commits to one goal at a time (ImpDigger-shaped): needs are re-decided when a goal completes
/// or drops, and on the decision poll during wander pauses and sleep — never mid-travel. Combat is the
/// exception: a combat scan also runs on the poll during any other goal, and preempts it.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Goblin))]
public class GoblinAI : MonoBehaviour
{
    public enum Goal { None, Eat, Sleep, Wander, Fight }

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

    [Header("Combat (provisional until the hero's stats, Epic #5)")]
    [SerializeField] private int aggroRange = 4;                // Tiles, Manhattan (DESIGN §A.5)
    [SerializeField] private float attackIntervalSeconds = 1f;  // Doubled while weakened (half attack rate)
    [SerializeField] private int attackDamage = 1;

    public Goblin Goblin { get => goblin; set => goblin = value; }
    public PlacementManager PlacementManager { get => placementManager; set => placementManager = value; }
    public float HungerThreshold { get => hungerThreshold; set => hungerThreshold = value; }
    public float EnergyThreshold { get => energyThreshold; set => energyThreshold = value; }
    public float HungerPerFood { get => hungerPerFood; set => hungerPerFood = value; }
    public float SleepSecondsToFull { get => sleepSecondsToFull; set => sleepSecondsToFull = value; }
    public float DecisionIntervalSeconds { get => decisionIntervalSeconds; set => decisionIntervalSeconds = value; }
    public float WanderPauseSeconds { get => wanderPauseSeconds; set => wanderPauseSeconds = value; }
    public int AggroRange { get => aggroRange; set => aggroRange = value; }
    public float AttackIntervalSeconds { get => attackIntervalSeconds; set => attackIntervalSeconds = value; }
    public int AttackDamage { get => attackDamage; set => attackDamage = value; }

    /// <summary>Random source for wander targets; tests replace it with a seeded one.</summary>
    public System.Random Rng { get; set; } = new System.Random();

    public Goal CurrentGoal { get; private set; }

    /// <summary>True only while lying on ClaimedCot (not while walking to it).</summary>
    public bool IsSleeping { get; private set; }

    /// <summary>The cot this goblin holds a claim on: from selection, through travel and sleep, until it wakes.</summary>
    public Placeable ClaimedCot { get; private set; }

    /// <summary>The plot this goblin is walking to eat from (Eat goal only).</summary>
    public MushroomPlot TargetPlot { get; private set; }

    /// <summary>The hostile being fought (Fight goal only).</summary>
    public Health CurrentTarget { get; private set; }

    /// <summary>The attack interval right now: attackIntervalSeconds, doubled while the goblin is weakened.</summary>
    public float EffectiveAttackInterval =>
        goblin != null && goblin.IsWeakened ? attackIntervalSeconds * 2f : attackIntervalSeconds;

    private Vector2Int standPoint;   // Eat: the tile beside TargetPlot
    private bool wanderTraveling;    // Wander: false = pausing, true = walking the leg
    private float wanderPauseTimer;
    private float decisionTimer;
    private float combatScanTimer;     // Combat poll during non-Fight goals; also Fight's range/re-path poll
    private float attackCooldown;      // Fight: ≤ 0 means the next attack lands as soon as the goblin is adjacent
    private Goblin targetBody;         // Fight: CurrentTarget's body, for its tile
    private Vector2Int pathedTargetTile;

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

        // Combat outranks every need: scan on the poll mid-goal (including asleep). Decide() scans too.
        if (CurrentGoal != Goal.Fight && CurrentGoal != Goal.None)
        {
            combatScanTimer += deltaTime;
            if (combatScanTimer >= decisionIntervalSeconds)
            {
                combatScanTimer = 0f;
                if (TryStartFight()) return;
            }
        }

        switch (CurrentGoal)
        {
            case Goal.None: Decide(); break;
            case Goal.Eat: TickEat(); break;
            case Goal.Sleep: TickSleep(deltaTime); break;
            case Goal.Wander: TickWander(deltaTime); break;
            case Goal.Fight: TickFight(deltaTime); break;
        }
    }

    // ---- decisions ----

    // Strict §A.5 priority, with combat first. Fight, Eat and Sleep fall through when they can't be serviced
    // (no hostile in range or reachable, nothing stocked or free), so the goblin keeps going and retries on
    // the poll instead of stalling.
    private void Decide()
    {
        if (TryStartFight()) return;
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

    /// <summary>
    /// Commits to fighting the nearest reachable hostile in range, preempting any current goal: a held cot
    /// claim is released and sleep ends.
    /// </summary>
    private bool TryStartFight()
    {
        Health target = TrySelectTarget(Board, goblin.CurrentTile, MyTeam, ScanCandidates(), aggroRange);
        if (target == null || !target.TryGetComponent(out Goblin body)) return false;

        ReleaseCot();
        ResetGoalState();
        CurrentGoal = Goal.Fight;
        CurrentTarget = target;
        targetBody = body;
        attackCooldown = 0f; // First attack lands as soon as the goblin is adjacent
        combatScanTimer = 0f;
        if (RepathToTarget()) return true;

        EndGoal(); // Not Disengage(): that re-decides, which would re-enter here
        return false;
    }

    private HealthTeam MyTeam => goblin.Health != null ? goblin.Health.Team : HealthTeam.Monster;

    // Active Health components with a body, sorted by body tile in board scan order so ties are deterministic.
    private static List<Health> ScanCandidates()
    {
        var candidates = new List<Health>();
        foreach (Health h in FindObjectsByType<Health>())
        {
            if (h.TryGetComponent(out Goblin _)) candidates.Add(h);
        }
        candidates.Sort((a, b) =>
        {
            Vector2Int ta = a.GetComponent<Goblin>().CurrentTile, tb = b.GetComponent<Goblin>().CurrentTile;
            return ta.x != tb.x ? ta.x.CompareTo(tb.x) : ta.y.CompareTo(tb.y);
        });
        return candidates;
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
        CurrentTarget = null;
        targetBody = null;
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

    private void TickFight(float deltaTime)
    {
        if (!IsTargetValid())
        {
            Disengage(); // Target dead, deactivated or destroyed: never hit a corpse
            return;
        }

        Vector2Int targetTile = targetBody.CurrentTile;
        attackCooldown -= deltaTime;

        combatScanTimer += deltaTime;
        bool poll = combatScanTimer >= decisionIntervalSeconds;
        if (poll)
        {
            combatScanTimer = 0f;
            if (Manhattan(goblin.CurrentTile, targetTile) > aggroRange)
            {
                Disengage(); // Left range
                return;
            }
        }

        if (goblin.IsMoving)
        {
            // The target moved since this path was planned: re-path on the poll.
            if (poll && targetTile != pathedTargetTile && !RepathToTarget()) Disengage();
            return;
        }

        if (Manhattan(goblin.CurrentTile, targetTile) != 1)
        {
            if (!RepathToTarget()) Disengage(); // Can no longer stand beside it
            return;
        }

        if (attackCooldown > 0f) return;
        CurrentTarget.TakeDamage(attackDamage);
        attackCooldown = EffectiveAttackInterval; // Read at the moment of each attack
        if (CurrentTarget.IsDead) Disengage();
    }

    /// <summary>Paths to a tile beside the target (never onto it). False when that's no longer possible.</summary>
    private bool RepathToTarget()
    {
        Vector2Int targetTile = targetBody.CurrentTile;
        List<Vector2Int> path = Pathfinder.FindPathToNeighbor(Board, goblin.CurrentTile, targetTile);
        if (path.Count == 0 || !goblin.SetDestination(path[path.Count - 1])) return false;
        pathedTargetTile = targetTile;
        return true;
    }

    private bool IsTargetValid() =>
        CurrentTarget != null && targetBody != null &&            // Unity null: destroyed
        CurrentTarget.gameObject.activeInHierarchy &&
        !CurrentTarget.IsDead && !targetBody.IsDead;

    /// <summary>Drops the fight and lets needs take over in the same tick.</summary>
    private void Disengage()
    {
        // Stop walking the old approach path (it leads toward the target): finish the step in progress only.
        if (goblin.IsMoving) goblin.SetDestination(goblin.NextTile);
        EndGoal();
        Decide();
    }

    private static int Manhattan(Vector2Int a, Vector2Int b) => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);

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
    /// The nearest opposing-team, living Health within aggroRange (Manhattan) that can be stood beside.
    /// A candidate's tile is its Goblin body's CurrentTile (candidates without one are skipped). Ties keep
    /// candidate order. Unreachable hostiles are skipped — noted, not acted on. Null when there's none.
    /// </summary>
    public static Health TrySelectTarget(DungeonBoard board, Vector2Int fromTile, HealthTeam myTeam,
        IEnumerable<Health> candidates, int aggroRange)
    {
        Health best = null;
        int bestDistance = int.MaxValue;

        foreach (Health candidate in candidates)
        {
            if (candidate == null || candidate.IsDead || candidate.Team == myTeam) continue;
            if (!candidate.TryGetComponent(out Goblin body) || body.IsDead) continue;

            int distance = Manhattan(fromTile, body.CurrentTile);
            if (distance > aggroRange || distance >= bestDistance) continue; // Out of range, or not strictly nearer
            if (Pathfinder.FindPathToNeighbor(board, fromTile, body.CurrentTile).Count == 0) continue;

            best = candidate;
            bestDistance = distance;
        }
        return best;
    }

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
