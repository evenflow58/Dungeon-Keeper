using UnityEngine;

/// <summary>
/// The hero director (DESIGN §A.6): three escalating heroes, one at a time (§A.9).
/// - The clock (#163): nothing happens until the door is dug out. The door starts undug; the moment its tile becomes the
///   Entrance (DungeonBoard.OnTileChanged, an event, not a poll) the hero clock starts. Before that no hero arrives,
///   however long the game runs. A board whose door is already open when the director first looks starts it at once.
/// - Hero 1: firstHeroSeconds after the door was dug out, a Fighter enters at the door. Timer only: the cots-and-plots
///   trigger is removed for now (Evan, #163), so building never brings him sooner.
/// - When a hero is resolved (killed or escaped), the next is scheduled nextHeroDelaySeconds later. An escaped
///   hero reports back: the next comes after delay × escapedDelayMultiplier and carries the escape stat bonus.
/// - Each wave adds heroHealthPerWave / heroDamagePerWave to the base stats. Later heroes don't re-check the
///   first-hero timer; it started the sequence.
/// - Every spawn is gated on a route from the door to beside the Heart (#58).
/// - After totalHeroes resolutions it's done; GameManager reads HeroesResolved for Victory. Once the game is
///   over (GameManager not Playing) it goes quiet: no spawns, no countdown.
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
    [SerializeField] private GameManager gameManager; // Optional: once the game is over, the director goes quiet

    [Header("Trigger (DESIGN §A.6: timer only)")]
    [SerializeField] private float firstHeroSeconds = 300f;           // 5 minutes
    [SerializeField] private Vector2Int entranceTile = new Vector2Int(24, 31); // The door (DungeonBoard.EntranceTile): must agree

    [Header("Fighter (provisional: DESIGN fixes no numbers)")]
    [SerializeField] private int heroMaxHealth = 30;
    [SerializeField] private float heroMoveSpeed = 3f;
    [SerializeField] private int heroAttackDamage = 2;
    [SerializeField] private float heroAttackIntervalSeconds = 1f;
    [SerializeField] private float heroFleeHealthFraction = 0.3f;     // DESIGN §A.6: flees at 30% HP
    [SerializeField] private float heroDecisionIntervalSeconds = 0.25f; // How often he re-plans and looks for blockers

    [Header("Escalation (provisional: DESIGN fixes the shape, not the numbers)")]
    [SerializeField] private int totalHeroes = 3;                  // DESIGN §A.6: three heroes
    [SerializeField] private float nextHeroDelaySeconds = 120f;    // After a hero is resolved
    [SerializeField] private float escapedDelayMultiplier = 0.5f;  // An escaped hero reports back: the next comes sooner…
    [SerializeField] private int heroHealthPerWave = 15;           // Added per wave after the first
    [SerializeField] private int heroDamagePerWave = 1;
    [SerializeField] private int escapedHealthBonus = 10;          // …and tougher (next hero only; never stacks)
    [SerializeField] private int escapedDamageBonus = 1;

    public PlacementManager PlacementManager { get => placementManager; set => placementManager = value; }
    public DungeonBoard Board { get => dungeonBoard; set => dungeonBoard = value; }
    public BoardRenderer Renderer { get => boardRenderer; set => boardRenderer = value; }
    public Heart Heart { get => heart; set => heart = value; }
    public GameManager GameManager { get => gameManager; set => gameManager = value; }
    public float FirstHeroSeconds { get => firstHeroSeconds; set => firstHeroSeconds = value; }
    public Vector2Int EntranceTile { get => entranceTile; set => entranceTile = value; }
    public int HeroMaxHealth { get => heroMaxHealth; set => heroMaxHealth = value; }
    public float HeroMoveSpeed { get => heroMoveSpeed; set => heroMoveSpeed = value; }
    public int HeroAttackDamage { get => heroAttackDamage; set => heroAttackDamage = value; }
    public float HeroAttackIntervalSeconds { get => heroAttackIntervalSeconds; set => heroAttackIntervalSeconds = value; }
    public float HeroFleeHealthFraction { get => heroFleeHealthFraction; set => heroFleeHealthFraction = value; }
    public float HeroDecisionIntervalSeconds { get => heroDecisionIntervalSeconds; set => heroDecisionIntervalSeconds = value; }
    public int TotalHeroes { get => totalHeroes; set => totalHeroes = value; }
    public float NextHeroDelaySeconds { get => nextHeroDelaySeconds; set => nextHeroDelaySeconds = value; }
    public float EscapedDelayMultiplier { get => escapedDelayMultiplier; set => escapedDelayMultiplier = value; }
    public int HeroHealthPerWave { get => heroHealthPerWave; set => heroHealthPerWave = value; }
    public int HeroDamagePerWave { get => heroDamagePerWave; set => heroDamagePerWave = value; }
    public int EscapedHealthBonus { get => escapedHealthBonus; set => escapedHealthBonus = value; }
    public int EscapedDamageBonus { get => escapedDamageBonus; set => escapedDamageBonus = value; }

    /// <summary>The most recent hero's outcome (None until the first hero is resolved).</summary>
    public HeroOutcome Outcome { get; private set; }

    /// <summary>Heroes spawned so far; also the current wave number (1-based) once the first has arrived.</summary>
    public int HeroesSpawned { get; private set; }

    /// <summary>Heroes killed or escaped so far. GameManager declares Victory when this reaches TotalHeroes.</summary>
    public int HeroesResolved { get; private set; }

    /// <summary>The most recently spawned hero (still referenced after he's resolved).</summary>
    public Hero ActiveHero { get; private set; }

    /// <summary>True while counting down to the next hero (for Epic #6's next-hero timer).</summary>
    public bool NextHeroScheduled { get; private set; }

    /// <summary>The live countdown to the next hero while NextHeroScheduled; 0 otherwise.</summary>
    public float SecondsUntilNextHero { get; private set; }

    /// <summary>True when the next scheduled hero will carry the escape bonus (the previous one escaped).</summary>
    public bool NextHeroHasEscapeBonus { get; private set; }

    /// <summary>
    /// Game seconds counted toward the time trigger since the door was dug out (#163); stops once the first hero has
    /// spawned.
    /// </summary>
    public float ElapsedSeconds { get; private set; }

    /// <summary>
    /// #163: true once the door has been dug out. The first hero's triggers only run from then on. Reading it starts
    /// watching the board (WatchDoor), so a reader never sees a stale value.
    /// </summary>
    public bool ClockStarted
    {
        get
        {
            WatchDoor();
            return clockStarted;
        }
    }

    private bool clockStarted;

    // #163: the board whose OnTileChanged we listen to in this domain (a reload drops C# event subscriptions, #137).
    [System.NonSerialized] private DungeonBoard watchedBoard;

    private void OnEnable() => WatchDoor();

    private void OnDisable() => UnwatchDoor();

    /// <summary>
    /// #163: listens for the door being dug out on the current board (idempotent; follows a changed Board). A door that
    /// is already open starts the clock at once. Start, OnEnable and Tick call it, so tests that wire a board and tick
    /// get it too.
    /// </summary>
    public void WatchDoor()
    {
        if (dungeonBoard == watchedBoard) return;
        UnwatchDoor();
        if (dungeonBoard == null) return;
        dungeonBoard.OnTileChanged += OnBoardTileChanged;
        watchedBoard = dungeonBoard;
        if (dungeonBoard.IsDoorOpen) clockStarted = true;
    }

    private void UnwatchDoor()
    {
        if (watchedBoard != null) watchedBoard.OnTileChanged -= OnBoardTileChanged;
        watchedBoard = null;
    }

    private void OnBoardTileChanged(int x, int y, TileState state)
    {
        if (state == TileState.Entrance && watchedBoard != null && new Vector2Int(x, y) == watchedBoard.EntranceTile)
            clockStarted = true; // The door is dug out: the setup time is over
    }

    private void Start()
    {
        placementManager ??= FindAnyObjectByType<PlacementManager>();
        dungeonBoard ??= FindAnyObjectByType<DungeonBoard>();
        boardRenderer ??= FindAnyObjectByType<BoardRenderer>();
        heart ??= FindAnyObjectByType<Heart>();
        gameManager ??= FindAnyObjectByType<GameManager>();
        WatchDoor();
    }

    private void Update()
    {
        Tick(Time.deltaTime);
    }

    /// <summary>
    /// Runs the director: the first-hero trigger, the active hero's resolution, or the countdown to the next.
    /// Called from Update; public for tests.
    /// </summary>
    public void Tick(float deltaTime)
    {
        if (gameManager != null && gameManager.State != GameManager.GameState.Playing) return; // Game over: quiet
        if (HeroesResolved >= totalHeroes) return;                                                // Done

        if (HeroesSpawned == 0)
        {
            if (!ClockStarted) return; // #163: the door is still undug: no hero, no countdown
            ElapsedSeconds += deltaTime;
            // The timer only grows, so if it ran out while the dungeon was sealed he spawns on the first poll after
            // the route opens. The timer is checked first so the path query only runs when it's due.
            if (FirstHeroDue && RouteExists()) Spawn();
            return;
        }

        if (!NextHeroScheduled)
        {
            PollOutcome(); // A hero is active
            return;
        }

        SecondsUntilNextHero = Mathf.Max(0f, SecondsUntilNextHero - deltaTime);
        if (SecondsUntilNextHero <= 0f && RouteExists()) Spawn(); // Same #58 gate for every hero
    }

    /// <summary>
    /// True when a hero could walk from the door to beside the Heart right now: the same FindPathToNeighbor
    /// query HeroAI delves with, starting at the board's door (DungeonBoard.EntranceTile, the door's source of
    /// truth). A tunnel that stops short of the Heart isn't a route.
    /// </summary>
    public bool RouteExists() =>
        dungeonBoard != null && heart != null &&
        Pathfinder.FindPathToNeighbor(dungeonBoard, dungeonBoard.EntranceTile, heart.Tile, Mover.Hero).Count > 0; // #165: hero rules

    /// <summary>True once firstHeroSeconds have passed since the door was dug out (hero 1's only trigger).</summary>
    public bool FirstHeroDue => ElapsedSeconds >= firstHeroSeconds;

    private void PollOutcome()
    {
        if (ActiveHero == null) return;
        if (ActiveHero.IsDead) Resolve(HeroOutcome.Killed);
        else if (ActiveHero.TryGetComponent(out HeroAI ai) && ai.HasEscaped) Resolve(HeroOutcome.Escaped);
    }

    /// <summary>Once per hero: count him, record his outcome, and schedule the next unless this was the last.</summary>
    private void Resolve(HeroOutcome outcome)
    {
        HeroesResolved++;
        Outcome = outcome;
        if (HeroesResolved >= totalHeroes)
        {
            NextHeroScheduled = false;
            SecondsUntilNextHero = 0f;
            NextHeroHasEscapeBonus = false;
            return;
        }

        bool escaped = outcome == HeroOutcome.Escaped;
        NextHeroScheduled = true;
        SecondsUntilNextHero = escaped ? nextHeroDelaySeconds * escapedDelayMultiplier : nextHeroDelaySeconds;
        NextHeroHasEscapeBonus = escaped; // Re-triggered by each escape, never stacked
    }

    /// <summary>Wave N's max HP: base + (N-1) × per-wave, plus the escape bonus if the previous hero escaped.</summary>
    public int MaxHealthForWave(int wave, bool escapeBonus) =>
        heroMaxHealth + (wave - 1) * heroHealthPerWave + (escapeBonus ? escapedHealthBonus : 0);

    /// <summary>Wave N's attack damage: base + (N-1) × per-wave, plus the escape bonus if the previous hero escaped.</summary>
    public int AttackDamageForWave(int wave, bool escapeBonus) =>
        heroAttackDamage + (wave - 1) * heroDamagePerWave + (escapeBonus ? escapedDamageBonus : 0);

    private void Spawn()
    {
        int wave = HeroesSpawned + 1;
        bool escapeBonus = NextHeroHasEscapeBonus;

        var go = new GameObject("Hero " + wave);

        // Configure Health before anything reads it: CurrentHealth initializes lazily to MaxHealth.
        // Durability and threat escalate per wave; tempo (speed, attack interval, flee point) stays at base.
        var health = go.AddComponent<Health>();
        health.MaxHealth = MaxHealthForWave(wave, escapeBonus);
        health.Team = HealthTeam.Hero;

        var hero = go.AddComponent<Hero>();
        hero.Board = dungeonBoard;
        hero.Renderer = boardRenderer;
        hero.Health = health;
        hero.MoveSpeed = heroMoveSpeed;

        var ai = go.AddComponent<HeroAI>();
        ai.Hero = hero;
        ai.PlacementManager = placementManager;
        ai.AttackDamage = AttackDamageForWave(wave, escapeBonus);
        ai.AttackIntervalSeconds = heroAttackIntervalSeconds;
        ai.FleeHealthFraction = heroFleeHealthFraction;
        ai.DecisionIntervalSeconds = heroDecisionIntervalSeconds;
        ai.EntranceTile = entranceTile;
        // Heart and GameManager resolve through HeroAI's own Start fallbacks.

        hero.PlaceOnTile(entranceTile);
        HeroesSpawned = wave;
        ActiveHero = hero;
        NextHeroScheduled = false;
        SecondsUntilNextHero = 0f;
        NextHeroHasEscapeBonus = false;
    }
}
