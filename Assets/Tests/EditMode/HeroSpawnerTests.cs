using NUnit.Framework;
using UnityEngine;

public class HeroSpawnerTests
{
    private static readonly Vector2Int PlaceFrom = new Vector2Int(24, 16);
    private static readonly Vector2Int HeartTile = new Vector2Int(24, 14);
    private const float Dt = 0.02f;

    private GameObject managerGo;
    private DungeonBoard board;
    private BoardRenderer boardRenderer;
    private PlacementManager manager;
    private GameObject heartGo;
    private Heart heart;
    private GameObject spawnerGo;
    private HeroSpawner spawner;

    [SetUp]
    public void SetUp()
    {
        // Default board: Rock, the 6x6 Floor cavern at (21,13)-(26,18), the door at (24,31). Sealed: no route yet.
        managerGo = new GameObject("TestDungeonManager");
        board = managerGo.AddComponent<DungeonBoard>();
        board.InitializeBoard();
        boardRenderer = managerGo.AddComponent<BoardRenderer>();
        boardRenderer.Board = board;
        manager = managerGo.AddComponent<PlacementManager>();
        manager.Board = board;
        manager.Renderer = boardRenderer;

        heartGo = new GameObject("TestHeart");
        heart = heartGo.AddComponent<Heart>(); // Health 100 / Monster via Reset
        heart.Board = board;
        heart.Renderer = boardRenderer;
        heart.Health = heartGo.GetComponent<Health>();
        heart.PlaceOnTile(HeartTile);

        spawnerGo = new GameObject("TestHeroSpawner");
        spawner = spawnerGo.AddComponent<HeroSpawner>();
        spawner.PlacementManager = manager;
        spawner.Board = board;
        spawner.Renderer = boardRenderer;
        spawner.Heart = heart;
    }

    [TearDown]
    public void TearDown()
    {
        if (spawner != null && spawner.ActiveHero != null) Object.DestroyImmediate(spawner.ActiveHero.gameObject);
        if (spawnerGo != null) Object.DestroyImmediate(spawnerGo);
        if (heartGo != null) Object.DestroyImmediate(heartGo);
        if (managerGo != null) Object.DestroyImmediate(managerGo);
    }

    private void Place(PlaceableType type, int x, int y) =>
        Assert.IsTrue(manager.TryPlaceBuilt(type, new Vector2Int(x, y), PlaceFrom, out _));

    private void PlaceBuildingTrigger()
    {
        Place(PlaceableType.LairCot, 21, 13);
        Place(PlaceableType.LairCot, 22, 13);
        Place(PlaceableType.LairCot, 23, 13);
        Place(PlaceableType.MushroomPlot, 21, 18);
        Place(PlaceableType.MushroomPlot, 22, 18);
    }

    /// <summary>The route: the x=24 column from the cavern's top edge up to just below the door.</summary>
    private void DigColumnToDoor()
    {
        for (int y = 19; y <= 30; y++) board.SetTile(24, y, TileState.Floor);
    }

    private void TickFor(int ticks)
    {
        for (int i = 0; i < ticks; i++) spawner.Tick(Dt);
    }

    [Test]
    public void Defaults_MatchDesign()
    {
        Assert.AreEqual(3, spawner.RequiredCots);
        Assert.AreEqual(2, spawner.RequiredPlots);
        Assert.AreEqual(300f, spawner.FirstHeroSeconds);
        Assert.AreEqual(new Vector2Int(24, 31), spawner.EntranceTile);
    }

    // --- Triggers (route dug, so only the trigger decides) ---

    [Test]
    public void TwoCotsTwoPlots_NoSpawn_ThirdCot_SpawnsOnNextTick()
    {
        DigColumnToDoor();
        Place(PlaceableType.LairCot, 21, 13);
        Place(PlaceableType.LairCot, 22, 13);
        Place(PlaceableType.MushroomPlot, 21, 18);
        Place(PlaceableType.MushroomPlot, 22, 18);

        TickFor(100);
        Assert.AreEqual(0, spawner.HeroesSpawned);
        Assert.IsNull(spawner.ActiveHero);

        Place(PlaceableType.LairCot, 23, 13);
        spawner.Tick(Dt);

        Assert.AreEqual(1, spawner.HeroesSpawned);
        Hero hero = spawner.ActiveHero;
        Assert.IsNotNull(hero);
        Assert.AreEqual(new Vector2Int(24, 31), hero.CurrentTile);
        Health health = hero.GetComponent<Health>();
        Assert.AreEqual(30, health.MaxHealth);
        Assert.AreEqual(30, health.CurrentHealth);
        Assert.AreEqual(HealthTeam.Hero, health.Team);
        Assert.AreSame(health, hero.Health);
        Assert.AreEqual(3f, hero.MoveSpeed);

        HeroAI ai = hero.GetComponent<HeroAI>();
        Assert.IsNotNull(ai);
        Assert.AreSame(hero, ai.Hero);
        Assert.AreEqual(2, ai.AttackDamage);
        Assert.AreEqual(1f, ai.AttackIntervalSeconds);
        Assert.AreEqual(new Vector2Int(24, 31), ai.EntranceTile);
        Assert.AreEqual(0.3f, ai.FleeHealthFraction);
        Assert.AreEqual(0.25f, ai.DecisionIntervalSeconds);

        TickFor(100);
        Assert.AreEqual(1, spawner.HeroesSpawned, "Only the first hero (waves are #53)");
        Assert.AreEqual(1, Object.FindObjectsByType<Hero>().Length);
    }

    [Test]
    public void SpawnerStats_FlowToTheSpawnedHero()
    {
        // A custom door at (0,16): moved on the board (the door's source of truth) and on the spawner, which
        // must agree. The route is a row dug from beside the door into the cavern.
        board.EntranceTile = new Vector2Int(0, 16);
        board.InitializeBoard();
        for (int x = 1; x <= 20; x++) board.SetTile(x, 16, TileState.Floor);

        spawner.HeroMaxHealth = 45;
        spawner.HeroMoveSpeed = 4.5f;
        spawner.HeroAttackDamage = 5;
        spawner.HeroAttackIntervalSeconds = 0.5f;
        spawner.HeroFleeHealthFraction = 0.5f;
        spawner.HeroDecisionIntervalSeconds = 0.1f;
        spawner.EntranceTile = new Vector2Int(0, 16);

        spawner.Tick(300f);
        Hero hero = spawner.ActiveHero;
        HeroAI ai = hero.GetComponent<HeroAI>();

        Assert.AreEqual(45, hero.Health.MaxHealth);
        Assert.AreEqual(45, hero.Health.CurrentHealth);
        Assert.AreEqual(4.5f, hero.MoveSpeed);
        Assert.AreEqual(5, ai.AttackDamage);
        Assert.AreEqual(0.5f, ai.AttackIntervalSeconds);
        Assert.AreEqual(0.5f, ai.FleeHealthFraction);
        Assert.AreEqual(0.1f, ai.DecisionIntervalSeconds);
        Assert.AreEqual(new Vector2Int(0, 16), hero.CurrentTile);
        Assert.AreEqual(new Vector2Int(0, 16), ai.EntranceTile);
    }

    [Test]
    public void ThreeCotsOnePlot_NoSpawn_BothConditionsNeeded()
    {
        Place(PlaceableType.LairCot, 21, 13);
        Place(PlaceableType.LairCot, 22, 13);
        Place(PlaceableType.LairCot, 23, 13);
        Place(PlaceableType.MushroomPlot, 21, 18);

        TickFor(100);
        Assert.AreEqual(0, spawner.HeroesSpawned);
        Assert.IsFalse(spawner.BuildingsTriggerMet());
    }

    [Test]
    public void NoBuildings_SpawnsWhenTimeReachesFiveMinutes()
    {
        DigColumnToDoor();
        spawner.Tick(299f);
        Assert.AreEqual(0, spawner.HeroesSpawned);
        Assert.AreEqual(299f, spawner.ElapsedSeconds, 1e-3f);

        spawner.Tick(1f);
        Assert.AreEqual(1, spawner.HeroesSpawned);
        Assert.AreEqual(new Vector2Int(24, 31), spawner.ActiveHero.CurrentTile);
    }

    [Test]
    public void NoManagerWired_TimeTriggerStillWorks()
    {
        DigColumnToDoor();
        spawner.PlacementManager = null;
        Assert.DoesNotThrow(() => spawner.Tick(150f));
        Assert.AreEqual(0, spawner.HeroesSpawned);
        spawner.Tick(150f);
        Assert.AreEqual(1, spawner.HeroesSpawned);
    }

    // --- The route gate (#58) ---

    [Test]
    public void BuildTrigger_SealedDungeon_NoSpawn()
    {
        PlaceBuildingTrigger();
        Assert.IsTrue(spawner.BuildingsTriggerMet());
        Assert.IsFalse(spawner.RouteExists());

        TickFor(500);
        Assert.AreEqual(0, spawner.HeroesSpawned);
        Assert.IsNull(spawner.ActiveHero);
        Assert.AreEqual(0, Object.FindObjectsByType<Hero>().Length);
    }

    [Test]
    public void TimeTrigger_SealedDungeon_NoSpawn_ClockKeepsCounting()
    {
        spawner.Tick(300f);
        spawner.Tick(1000f);

        Assert.AreEqual(0, spawner.HeroesSpawned);
        Assert.AreEqual(1300f, spawner.ElapsedSeconds, 1e-2f);
    }

    [Test]
    public void PartialTunnel_IsNotARoute_NoSpawn()
    {
        for (int y = 24; y <= 30; y++) board.SetTile(24, y, TileState.Floor); // dug down from the door, stops at (24,24)
        Assert.IsFalse(spawner.RouteExists());
        spawner.Tick(300f);
        Assert.AreEqual(0, spawner.HeroesSpawned);

        for (int y = 24; y <= 30; y++) board.SetTile(24, y, TileState.Rock);
        for (int y = 19; y <= 25; y++) board.SetTile(24, y, TileState.Floor); // dug up from the cavern, stops short of the door
        Assert.IsFalse(spawner.RouteExists());
        spawner.Tick(Dt);
        Assert.AreEqual(0, spawner.HeroesSpawned);
    }

    [Test]
    public void TriggerFirst_RouteLater_SpawnsOnTheNextTick_AtTheDoor()
    {
        spawner.Tick(300f);
        PlaceBuildingTrigger();
        TickFor(50);
        Assert.AreEqual(0, spawner.HeroesSpawned, "Both triggers fired, still sealed");

        DigColumnToDoor();
        Assert.IsTrue(spawner.RouteExists());
        spawner.Tick(Dt);

        Assert.AreEqual(1, spawner.HeroesSpawned);
        Assert.AreEqual(board.EntranceTile, spawner.ActiveHero.CurrentTile);
        Assert.AreEqual(TileState.Entrance, board.GetTile(board.EntranceTile.x, board.EntranceTile.y));
    }

    [Test]
    public void RouteExists_StartsAtTheBoardsDoor()
    {
        board.EntranceTile = new Vector2Int(0, 16);
        board.InitializeBoard();

        DigColumnToDoor(); // a route from the old door spot (24,31) only
        Assert.IsFalse(spawner.RouteExists(), "The old door tile isn't the door any more");

        for (int x = 1; x <= 20; x++) board.SetTile(x, 16, TileState.Floor); // from the new door into the cavern
        Assert.IsTrue(spawner.RouteExists());
    }

    [Test]
    public void NoHeart_NeverSpawns_NoException()
    {
        DigColumnToDoor();
        spawner.Heart = null;

        Assert.IsFalse(spawner.RouteExists());
        Assert.DoesNotThrow(() => spawner.Tick(300f));
        Assert.AreEqual(0, spawner.HeroesSpawned);
    }

    // --- Outcomes ---

    [Test]
    public void Outcome_Killed()
    {
        DigColumnToDoor();
        spawner.Tick(300f);
        Hero hero = spawner.ActiveHero;
        Assert.AreEqual(HeroSpawner.HeroOutcome.None, spawner.Outcome);

        hero.Health.TakeDamage(1000);
        hero.Tick(Dt);
        spawner.Tick(Dt);

        Assert.AreEqual(HeroSpawner.HeroOutcome.Killed, spawner.Outcome);
        TickFor(50);
        Assert.AreEqual(HeroSpawner.HeroOutcome.Killed, spawner.Outcome, "Reported once, stays");
        Assert.AreEqual(1, spawner.HeroesSpawned);
    }

    [Test]
    public void Outcome_Escaped()
    {
        DigColumnToDoor();
        spawner.Tick(300f);
        Hero hero = spawner.ActiveHero;
        HeroAI ai = hero.GetComponent<HeroAI>();

        hero.Health.TakeDamage(25); // 5/30: below the flee fraction before his AI ever steps
        hero.Tick(Dt);
        ai.Tick(Dt);                // he's standing on the door: escapes at once
        Assert.IsTrue(ai.HasEscaped);
        Assert.IsFalse(hero.gameObject.activeSelf);

        spawner.Tick(Dt);
        Assert.AreEqual(HeroSpawner.HeroOutcome.Escaped, spawner.Outcome);
    }
}
