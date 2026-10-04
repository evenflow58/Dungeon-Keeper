using UnityEngine;

/// <summary>
/// Spawns the first hero (DESIGN §A.6): once the player has placed requiredCots Lair Cots AND requiredPlots
/// Mushroom Plots, or after firstHeroSeconds, a Fighter enters at the entrance tile on the board edge.
/// Then it watches for his outcome (killed or escaped) and goes quiet — waves and escalation are the next
/// story's job. Multiple simultaneous heroes are out of the slice (§A.9).
/// </summary>
[DisallowMultipleComponent]
public class HeroSpawner : MonoBehaviour
{
    public enum HeroOutcome { None, Killed, Escaped }

    [Header("References")]
    [SerializeField] private PlacementManager placementManager;
    [SerializeField] private DungeonBoard dungeonBoard;
    [SerializeField] private BoardRenderer boardRenderer;

    [Header("Trigger (DESIGN §A.6)")]
    [SerializeField] private int requiredCots = 3;
    [SerializeField] private int requiredPlots = 2;
    [SerializeField] private float firstHeroSeconds = 300f;           // 5 minutes
    [SerializeField] private Vector2Int entranceTile = new Vector2Int(24, 31); // Top edge, above the cavern column

    [Header("Fighter (provisional: DESIGN fixes no numbers)")]
    [SerializeField] private int heroMaxHealth = 30;
    [SerializeField] private float heroMoveSpeed = 3f;
    [SerializeField] private int heroAttackDamage = 2;
    [SerializeField] private float heroAttackIntervalSeconds = 1f;

    public PlacementManager PlacementManager { get => placementManager; set => placementManager = value; }
    public DungeonBoard Board { get => dungeonBoard; set => dungeonBoard = value; }
    public BoardRenderer Renderer { get => boardRenderer; set => boardRenderer = value; }
    public int RequiredCots { get => requiredCots; set => requiredCots = value; }
    public int RequiredPlots { get => requiredPlots; set => requiredPlots = value; }
    public float FirstHeroSeconds { get => firstHeroSeconds; set => firstHeroSeconds = value; }
    public Vector2Int EntranceTile { get => entranceTile; set => entranceTile = value; }
    public int HeroMaxHealth { get => heroMaxHealth; set => heroMaxHealth = value; }
    public float HeroMoveSpeed { get => heroMoveSpeed; set => heroMoveSpeed = value; }
    public int HeroAttackDamage { get => heroAttackDamage; set => heroAttackDamage = value; }
    public float HeroAttackIntervalSeconds { get => heroAttackIntervalSeconds; set => heroAttackIntervalSeconds = value; }

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
        if (BuildingsTriggerMet() || ElapsedSeconds >= firstHeroSeconds) Spawn();
    }

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
        ai.EntranceTile = entranceTile;
        // Heart and GameManager resolve through HeroAI's own Start fallbacks.

        hero.PlaceOnTile(entranceTile);
        HeroesSpawned = 1;
        ActiveHero = hero;
    }
}
