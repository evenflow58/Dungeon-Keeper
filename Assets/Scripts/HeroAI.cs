using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The hero's delve driver (DESIGN §A.6): path to stand beside the Heart and attack it; fight minions that
/// block the way (adjacent, or standing on his route) but never chase across the room; step on armed Spike
/// Traps and take their damage (§A.4); at ≤ fleeHealthFraction HP flee to the entrance and escape. With no
/// route he waits where he is and re-checks on the poll — he never teleports. GoblinAI-shaped: one goal,
/// a decision poll, and an attack cooldown whose first hit lands as soon as he's adjacent.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Hero))]
public class HeroAI : MonoBehaviour
{
    public enum Goal { Delve, Fight, Flee, Done }

    [Header("References")]
    [SerializeField] private Hero hero;
    [SerializeField] private Heart heart;
    [SerializeField] private PlacementManager placementManager; // Spike Traps; null = no trap checks
    [SerializeField] private GameManager gameManager;           // Optional: once the game is over he stops

    [Header("Fighter (provisional: DESIGN fixes no numbers)")]
    [SerializeField] private int attackDamage = 2;
    [SerializeField] private float attackIntervalSeconds = 1f;
    [SerializeField] private float decisionIntervalSeconds = 0.25f;

    [Header("Fleeing")]
    [SerializeField] private float fleeHealthFraction = 0.3f;    // DESIGN §A.6: flees at 30% HP
    [SerializeField] private Vector2Int entranceTile;            // Set by the spawner: where he came in

    public Hero Hero { get => hero; set => hero = value; }
    public Heart Heart { get => heart; set => heart = value; }
    public PlacementManager PlacementManager { get => placementManager; set => placementManager = value; }
    public GameManager GameManager { get => gameManager; set => gameManager = value; }
    public int AttackDamage { get => attackDamage; set => attackDamage = value; }
    public float AttackIntervalSeconds { get => attackIntervalSeconds; set => attackIntervalSeconds = value; }
    public float DecisionIntervalSeconds { get => decisionIntervalSeconds; set => decisionIntervalSeconds = value; }
    public float FleeHealthFraction { get => fleeHealthFraction; set => fleeHealthFraction = value; }
    public Vector2Int EntranceTile { get => entranceTile; set => entranceTile = value; }

    public Goal CurrentGoal { get; private set; } = Goal.Delve;

    /// <summary>The minion being fought (Fight goal only).</summary>
    public Health CurrentTarget { get; private set; }

    /// <summary>True once he reached the entrance while fleeing and left the dungeon.</summary>
    public bool HasEscaped { get; private set; }

    private Vector2Int? lastTile;       // For trap step-on: a trap fires when he enters its tile
    private float pollTimer;
    private float attackCooldown;       // ≤ 0: the next attack lands as soon as he's adjacent
    private bool repathNow = true;      // Plan a route on the next Tick instead of waiting for the poll
    private Vector2Int pathedTargetTile;
    private List<Vector2Int> delvePath = new List<Vector2Int>(); // Last route to the Heart (start included)

    private DungeonBoard Board => hero.Board;

    private void Start()
    {
        hero ??= GetComponent<Hero>();
        heart ??= FindAnyObjectByType<Heart>();
        placementManager ??= FindAnyObjectByType<PlacementManager>();
        gameManager ??= FindAnyObjectByType<GameManager>();
    }

    private void Update()
    {
        Tick(Time.deltaTime);
    }

    // Death deactivates the hero's GameObject (this component included), so Update stops before Tick could
    // see IsDead. Mark the AI finished here so its state reads Done. Guarded on IsDead: escaping also
    // deactivates, and Escape() has already set Done.
    private void OnDisable()
    {
        if (hero == null || !hero.IsDead) return;
        CurrentGoal = Goal.Done;
        CurrentTarget = null;
    }

    /// <summary>
    /// Advances the AI by deltaTime. Called from Update; public so tests can step it after Hero.Tick.
    /// </summary>
    public void Tick(float deltaTime)
    {
        if (hero == null || Board == null) return;
        if (hero.IsDead)
        {
            CurrentGoal = Goal.Done;
            CurrentTarget = null;
            return;
        }
        if (CurrentGoal == Goal.Done) return;
        if (gameManager != null && gameManager.State != GameManager.GameState.Playing)
        {
            Finish(); // The game is already over: no more orders
            return;
        }

        CheckTrapStepOn();

        Health health = hero.Health;
        if (CurrentGoal != Goal.Flee && health != null && health.CurrentHealth <= fleeHealthFraction * health.MaxHealth)
            EnterFlee();

        attackCooldown -= deltaTime;
        pollTimer += deltaTime;
        bool poll = pollTimer >= decisionIntervalSeconds;
        if (poll) pollTimer = 0f;

        switch (CurrentGoal)
        {
            case Goal.Delve: TickDelve(poll); break;
            case Goal.Fight: TickFight(poll); break;
            case Goal.Flee: TickFlee(poll); break;
        }
    }

    // ---- traps ----

    private void CheckTrapStepOn()
    {
        Vector2Int tile = hero.CurrentTile;
        bool entered = lastTile.HasValue && lastTile.Value != tile;
        lastTile = tile;
        if (!entered || placementManager == null) return;

        Placeable placed = placementManager.GetAt(tile);
        if (placed == null || !placed.TryGetComponent(out SpikeTrap trap)) return;
        if (trap.TryTrigger(out int damage) && hero.Health != null) hero.Health.TakeDamage(damage);
    }

    // ---- delve ----

    private void TickDelve(bool poll)
    {
        if (heart == null) return; // Nothing to delve toward
        if (heart.IsDestroyed)
        {
            Finish();
            return;
        }

        if (poll || repathNow)
        {
            delvePath = Pathfinder.FindPathToNeighbor(Board, hero.CurrentTile, heart.Tile);
            if (poll && TryStartFight()) return;
        }

        if (!hero.IsMoving && Manhattan(hero.CurrentTile, heart.Tile) == 1)
        {
            repathNow = false;
            if (attackCooldown > 0f) return;
            if (heart.Health != null) heart.Health.TakeDamage(attackDamage); // Unity null check, not ?.
            attackCooldown = attackIntervalSeconds;
            if (heart.IsDestroyed) Finish();
            return;
        }

        if (!(poll || repathNow || !hero.IsMoving)) return;
        repathNow = false;
        // No route (yet): wait in place; digging may open one. Never teleport.
        if (delvePath.Count == 0) return;
        GoTo(delvePath[delvePath.Count - 1]);
    }

    /// <summary>Commits to fighting the nearest living minion that's adjacent or standing on his route.</summary>
    private bool TryStartFight()
    {
        Health blocker = FindBlocker(hero.CurrentTile, delvePath, FindObjectsByType<Health>());
        if (blocker == null) return false;

        CurrentGoal = Goal.Fight;
        CurrentTarget = blocker;
        repathNow = true;
        // Stop delving: finish only the step in progress, then hold or close in from there.
        if (hero.IsMoving) hero.SetDestination(hero.NextTile);
        return true;
    }

    // ---- fight ----

    private void TickFight(bool poll)
    {
        if (!IsTargetValid())
        {
            BackToDelve(); // Dead, deactivated or destroyed
            return;
        }

        Vector2Int targetTile = CurrentTarget.GetComponent<Goblin>().CurrentTile;
        bool adjacent = Manhattan(hero.CurrentTile, targetTile) == 1;
        bool comingForMe = IsTargetEngagingMe();
        if (poll && !adjacent && !comingForMe)
        {
            // Only blockers are worth fighting: one that stepped aside (off the route) and isn't attacking
            // him is left alone.
            if (heart != null) delvePath = Pathfinder.FindPathToNeighbor(Board, hero.CurrentTile, heart.Tile);
            if (!delvePath.Contains(targetTile))
            {
                BackToDelve();
                return;
            }
        }

        // Let the step in progress finish; decide once he's standing on a tile.
        if (hero.IsMoving) return;

        if (!adjacent)
        {
            // A goblin that's attacking him is walking over: hold ground and let it come. Closing in at the
            // same time is what made both sides head for the same tile and never end up side by side.
            if (comingForMe) return;

            // A blocker that isn't fighting back (asleep, eating, standing there): walk up beside it.
            bool replan = repathNow || targetTile != pathedTargetTile;
            if (!replan) return;
            repathNow = false;
            List<Vector2Int> path = Pathfinder.FindPathToNeighbor(Board, hero.CurrentTile, targetTile);
            if (path.Count == 0)
            {
                BackToDelve(); // Can't stand beside it
                return;
            }
            pathedTargetTile = targetTile;
            GoTo(path[path.Count - 1]);
            return;
        }

        repathNow = false;
        if (attackCooldown > 0f) return;
        CurrentTarget.TakeDamage(attackDamage);
        attackCooldown = attackIntervalSeconds;
        if (CurrentTarget.IsDead) BackToDelve();
    }

    /// <summary>True when the target goblin's own AI is fighting this hero (so it will come to him).</summary>
    private bool IsTargetEngagingMe() =>
        CurrentTarget.TryGetComponent(out GoblinAI goblinAI) && goblinAI.enabled && goblinAI.gameObject.activeInHierarchy &&
        goblinAI.CurrentGoal == GoblinAI.Goal.Fight && goblinAI.CurrentTarget == hero.Health;

    private bool IsTargetValid() =>
        CurrentTarget != null &&                                    // Unity null: destroyed
        CurrentTarget.gameObject.activeInHierarchy &&
        !CurrentTarget.IsDead &&
        CurrentTarget.TryGetComponent(out Goblin body) && !body.IsDead;

    private void BackToDelve()
    {
        CurrentGoal = Goal.Delve;
        CurrentTarget = null;
        repathNow = true;
    }

    // ---- flee ----

    private void EnterFlee()
    {
        CurrentGoal = Goal.Flee;
        CurrentTarget = null;
        repathNow = true;
    }

    private void TickFlee(bool poll)
    {
        // He leaves through the door: from its threshold (the dug tile beside it — the door itself isn't
        // walkable, so it can't be pathed onto), or straight away if he's still standing on it.
        if (!hero.IsMoving && Manhattan(hero.CurrentTile, entranceTile) <= 1)
        {
            Escape();
            return;
        }

        if (!(poll || repathNow || !hero.IsMoving)) return;
        repathNow = false;
        List<Vector2Int> path = Pathfinder.FindPathToNeighbor(Board, hero.CurrentTile, entranceTile);
        if (path.Count == 0) return; // Cut off: wait and re-check on the poll
        GoTo(path[path.Count - 1]);
    }

    private void Escape()
    {
        HasEscaped = true;
        CurrentGoal = Goal.Done;
        CurrentTarget = null;
        gameObject.SetActive(false);
    }

    // ---- helpers ----

    /// <summary>Heads for tile unless he's already standing on it or already walking there.</summary>
    private void GoTo(Vector2Int tile)
    {
        IReadOnlyList<Vector2Int> remaining = hero.RemainingPath;
        bool alreadyHeading = remaining.Count > 0 && remaining[remaining.Count - 1] == tile;
        if (alreadyHeading || (!hero.IsMoving && hero.CurrentTile == tile)) return;
        hero.SetDestination(tile);
    }

    private void Finish()
    {
        CurrentGoal = Goal.Done;
        CurrentTarget = null;
        if (hero.IsMoving) hero.SetDestination(hero.NextTile); // Finish the step in progress only
    }

    private static int Manhattan(Vector2Int a, Vector2Int b) => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);

    /// <summary>
    /// The nearest living Monster-team, Goblin-bodied Health that blocks the hero: adjacent to heroTile
    /// (Manhattan ≤ 1) or standing on a tile of route. Other minions are ignored (he doesn't chase).
    /// Ties go to board scan order. Null when nothing blocks.
    /// </summary>
    public static Health FindBlocker(Vector2Int heroTile, IReadOnlyList<Vector2Int> route, IEnumerable<Health> candidates)
    {
        Health best = null;
        Vector2Int bestTile = default;
        int bestDistance = int.MaxValue;

        foreach (Health candidate in candidates)
        {
            if (candidate == null || candidate.IsDead || candidate.Team != HealthTeam.Monster) continue;
            if (!candidate.TryGetComponent(out Goblin body) || body.IsDead) continue;

            Vector2Int tile = body.CurrentTile;
            int distance = Manhattan(heroTile, tile);
            bool blocks = distance <= 1 || Contains(route, tile);
            if (!blocks) continue;

            bool better = distance < bestDistance ||
                          (distance == bestDistance && (tile.x < bestTile.x || (tile.x == bestTile.x && tile.y < bestTile.y)));
            if (!better) continue;

            best = candidate;
            bestTile = tile;
            bestDistance = distance;
        }
        return best;
    }

    private static bool Contains(IReadOnlyList<Vector2Int> route, Vector2Int tile)
    {
        if (route == null) return false;
        for (int i = 0; i < route.Count; i++)
        {
            if (route[i] == tile) return true;
        }
        return false;
    }
}
