using NUnit.Framework;
using UnityEngine;

public class HeroSpawnerTests
{
    private static readonly Vector2Int PlaceFrom = new Vector2Int(24, 16);
    private const float Dt = 0.02f;

    private GameObject managerGo;
    private DungeonBoard board;
    private BoardRenderer boardRenderer;
    private PlacementManager manager;
    private GameObject spawnerGo;
    private HeroSpawner spawner;

    [SetUp]
    public void SetUp()
    {
        managerGo = new GameObject("TestDungeonManager");
        board = managerGo.AddComponent<DungeonBoard>();
        board.InitializeBoard();
        boardRenderer = managerGo.AddComponent<BoardRenderer>();
        boardRenderer.Board = board;
        manager = managerGo.AddComponent<PlacementManager>();
        manager.Board = board;
        manager.Renderer = boardRenderer;

        spawnerGo = new GameObject("TestHeroSpawner");
        spawner = spawnerGo.AddComponent<HeroSpawner>();
        spawner.PlacementManager = manager;
        spawner.Board = board;
        spawner.Renderer = boardRenderer;
    }

    [TearDown]
    public void TearDown()
    {
        if (spawner != null && spawner.ActiveHero != null) Object.DestroyImmediate(spawner.ActiveHero.gameObject);
        if (spawnerGo != null) Object.DestroyImmediate(spawnerGo);
        if (managerGo != null) Object.DestroyImmediate(managerGo);
    }

    private void Place(PlaceableType type, int x, int y) =>
        Assert.IsTrue(manager.TryPlace(type, new Vector2Int(x, y), PlaceFrom, out _));

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

    [Test]
    public void TwoCotsTwoPlots_NoSpawn_ThirdCot_SpawnsOnNextTick()
    {
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
        Assert.AreEqual(1, spawner.HeroesSpawned, "Only the first hero (waves are the next story)");
        Assert.AreEqual(1, Object.FindObjectsByType<Hero>().Length);
    }

    [Test]
    public void SpawnerStats_FlowToTheSpawnedHero()
    {
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
        spawner.PlacementManager = null;
        Assert.DoesNotThrow(() => spawner.Tick(150f));
        Assert.AreEqual(0, spawner.HeroesSpawned);
        spawner.Tick(150f);
        Assert.AreEqual(1, spawner.HeroesSpawned);
    }

    [Test]
    public void Outcome_Killed()
    {
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
        spawner.Tick(300f);
        Hero hero = spawner.ActiveHero;
        HeroAI ai = hero.GetComponent<HeroAI>();

        hero.Health.TakeDamage(25); // 5/30: below the flee fraction before his AI ever steps
        hero.Tick(Dt);
        ai.Tick(Dt);                // he's standing on the entrance: escapes at once
        Assert.IsTrue(ai.HasEscaped);
        Assert.IsFalse(hero.gameObject.activeSelf);

        spawner.Tick(Dt);
        Assert.AreEqual(HeroSpawner.HeroOutcome.Escaped, spawner.Outcome);
    }
}
