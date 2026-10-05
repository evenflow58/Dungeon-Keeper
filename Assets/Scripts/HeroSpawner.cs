using UnityEngine;

/// <summary>
/// Spawns the first hero (DESIGN §A.6): once the player has placed requiredCots Lair Cots AND requiredPlots
/// Mushroom Plots, or after firstHeroSeconds, a Fighter enters at the entrance tile on the board edge —
/// but only if a route exists from the door to beside the Heart (#58). No route, no hero; a trigger that has
/// already fired spawns him on the first later poll where the route exists. Then it watches for his outcome
/// (killed or escaped) and goes quiet — waves and escalation are #53. Multiple simultaneous heroes are out of
/// the slice (§A.9).
/// </summary>
[DisallowMultipleComponent]
public class HeroSpawner : MonoBehaviour
{
    public enum HeroOutcome { None, Killed, Escaped }

    [Header("References")]
    [SerializeField] private PlacementManager placementManager;
    [SerializeField] private DungeonBoard dungeonBoard;
    [SerializeField] private BoardRenderer boardRenderer;
    [SerializeField] private Heart heart; // The route gate's destination; no Heart = never spawn

    [Header("Trigger (DESIGN §A.6)")]
    [SerializeField] private int requiredCots = 3;
    [SerializeField] private int requiredPlots = 2;
    [SerializeField] private float firstHeroSeconds = 300f;           // 5 minutes
    [SerializeField] private Vector2Int entranceTile = new Vector2Int(24, 31); // The door (DungeonBoard.EntranceTile): must agree

    [Header("Fighter (provisional: DESIGN fixes no numbers)")]
    [SerializeField] private int heroMaxHealth = 30;
    [SerializeField] private float heroMoveSpeed = 3f;
    [SerializeField] private int heroAttackDamage = 2;
    [SerializeField] private float heroAttackIntervalSeconds = 1f;
    [SerializeField] private float heroFleeHealthFraction = 0.3f;     // DESIGN §A.6: flees at 30% HP
    [SerializeField] private float heroDecisionIntervalSeconds = 0.25f; // How often he re-plans and looks for blockers

    public PlacementManager PlacementManager { get => placementManager; set => placementManager = value; }
    public DungeonBoard Board { get => dungeonBoard; set => dungeonBoard = value; }
    public BoardRenderer Renderer { get => boardRenderer; set => boardRenderer = value; }
    public Heart Heart { get => heart; set => heart = value; }
    public int RequiredCots { get => requiredCots; set => requiredCots = value; }
    public int RequiredPlots { get => requiredPlots; set => requiredPlots = value; }
    public float FirstHeroSeconds { get => firstHeroSeconds; set => firstHeroSeconds = value; }
    public Vector2Int EntranceTile { get => entranceTile; set => entranceTile = value; }
    public int HeroMaxHealth { get => heroMaxHealth; set => heroMaxHealth = value; }
    public float HeroMoveSpeed { get => heroMoveSpeed; set => heroMoveSpeed = value; }
    public int HeroAttackDamage { get => heroAttackDamage; set => heroAttackDamage = value; }
    public float HeroAttackIntervalSeconds { get => heroAttackIntervalSeconds; set => heroAttackIntervalSeconds = value; }
    public float HeroFleeHealthFraction { get => heroFleeHealthFraction; set => heroFleeHealthFraction = value; }
    public float HeroDecisionIntervalSeconds { get => heroDecisionIntervalSeconds; set => heroDecisionIntervalSeconds = value; }

    public HeroOutcome Outcome { get; private set; }
    public int HeroesSpawned { get; private set; }
    public Hero ActiveHero { get; private set; }

    /// <summary>Game seconds counted toward the time trigger (stops once the hero has spawned).</summary>
    public float ElapsedSeconds { get; private set; }

    private void Start()
    {
        placementManager ??= FindAnyObjectByType<PlacementManager>();
        dungeonBoard ??= FindAnyObjectByType<DungeonBoard>();
        boardRenderer ??= FindAnyObjectByType<BoardRenderer>();
        heart ??= FindAnyObjectByType<Heart>();
    }

    private void Update()
    {
        Tick(Time.deltaTime);
    }

    /// <summary>Checks the trigger, or the spawned hero's outcome. Called from Update; public for tests.</summary>
    public void Tick(float deltaTime)
    {
        if (HeroesSpawned > 0)
        {
            PollOutcome();
            return;
        }

        ElapsedSeconds += deltaTime;
        // Both triggers are persistent (time only grows; buildings aren't removed in the slice), so a trigger that
        // fired while the dungeon was sealed spawns him on the first poll after the route opens. The trigger is
        // checked first so the path query only runs when it's hot.
        if ((BuildingsTriggerMet() || ElapsedSeconds >= firstHeroSeconds) && RouteExists()) Spawn();
    }

    /// <summary>
    /// True when a hero could walk from the door to beside the Heart right now: the same FindPathToNeighbor
    /// query HeroAI delves with, starting at the board's door (DungeonBoard.EntranceTile, the door's source of
    /// truth). A tunnel that stops short of the Heart isn't a route.
    /// </summary>
    public bool RouteExists() =>
        dungeonBoard != null && heart != null &&
        Pathfinder.FindPathToNeighbor(dungeonBoard, dungeonBoard.EntranceTile, heart.Tile).Count > 0;

    /// <summary>True when requiredCots Lair Cots and requiredPlots Mushroom Plots are placed (both).</summary>
    public bool BuildingsTriggerMet() =>
        placementManager != null &&
        placementManager.GetCots().Count >= requiredCots &&
        placementManager.GetMushroomPlots().Count >= requiredPlots;

    private void PollOutcome()
    {
        if (Outcome != HeroOutcome.None || ActiveHero == null) return;
        if (ActiveHero.IsDead) Outcome = HeroOutcome.Killed;
        else if (ActiveHero.TryGetComponent(out HeroAI ai) && ai.HasEscaped) Outcome = HeroOutcome.Escaped;
    }

    private void Spawn()
    {
        var go = new GameObject("Hero");

        // Configure Health before anything reads it: CurrentHealth initializes lazily to MaxHealth.
        var health = go.AddComponent<Health>();
        health.MaxHealth = heroMaxHealth;
        health.Team = HealthTeam.Hero;

        var hero = go.AddComponent<Hero>();
        hero.Board = dungeonBoard;
        hero.Renderer = boardRenderer;
        hero.Health = health;
        hero.MoveSpeed = heroMoveSpeed;

        var ai = go.AddComponent<HeroAI>();
        ai.Hero = hero;
        ai.PlacementManager = placementManager;
        ai.AttackDamage = heroAttackDamage;
        ai.AttackIntervalSeconds = heroAttackIntervalSeconds;
        ai.FleeHealthFraction = heroFleeHealthFraction;
        ai.DecisionIntervalSeconds = heroDecisionIntervalSeconds;
        ai.EntranceTile = entranceTile;
        // Heart and GameManager resolve through HeroAI's own Start fallbacks.

        hero.PlaceOnTile(entranceTile);
        HeroesSpawned = 1;
        ActiveHero = hero;
    }
}
