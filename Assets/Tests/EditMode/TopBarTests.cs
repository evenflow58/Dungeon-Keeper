using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class TopBarTests
{
    private static readonly Vector2Int HeartTile = new Vector2Int(24, 14);
    private static readonly Vector2Int PlaceFrom = new Vector2Int(24, 16);
    private const float Dt = 0.02f;

    private GameObject managerGo;
    private DungeonBoard board;
    private BoardRenderer boardRenderer;
    private PlacementManager manager;
    private GameObject heartGo;
    private Heart heart;
    private GameObject directorGo;
    private HeroSpawner spawner;
    private GameManager game;
    private TopBar bar;
    private Stockpile stockpile;
    private readonly List<GameObject> goblinGos = new List<GameObject>();
    private readonly List<Goblin> goblins = new List<Goblin>();
    private int baselineGoblins; // Living goblins already loaded outside this test (e.g. the open Main scene's three)

    [SetUp]
    public void SetUp()
    {
        baselineGoblins = CountLiving();

        managerGo = new GameObject("TestDungeonManager");
        board = managerGo.AddComponent<DungeonBoard>();
        board.InitializeBoard();
        boardRenderer = managerGo.AddComponent<BoardRenderer>();
        boardRenderer.Board = board;
        manager = managerGo.AddComponent<PlacementManager>();
        manager.Board = board;
        manager.Renderer = boardRenderer;
        manager.MushroomGrowthSecondsPerFood = 1f; // set before placing: 1 food per stepped second

        heartGo = new GameObject("TestHeart");
        heart = heartGo.AddComponent<Heart>();
        heart.Board = board;
        heart.Renderer = boardRenderer;
        heart.Health = heartGo.GetComponent<Health>();
        heart.PlaceOnTile(HeartTile);

        directorGo = new GameObject("TestDirector");
        spawner = directorGo.AddComponent<HeroSpawner>();
        spawner.PlacementManager = manager;
        spawner.Board = board;
        spawner.Renderer = boardRenderer;
        spawner.Heart = heart;
        game = directorGo.AddComponent<GameManager>();
        game.Heart = heart;
        game.HeroSpawner = spawner;
        spawner.GameManager = game;

        bar = directorGo.AddComponent<TopBar>();
        bar.PlacementManager = manager;
        bar.HeroSpawner = spawner;
        bar.GameManager = game;
        stockpile = managerGo.AddComponent<Stockpile>(); // On the DungeonManager, as in the scene
        bar.Stockpile = stockpile;

        foreach (Vector2Int t in new[] { new Vector2Int(23, 15), new Vector2Int(25, 15), new Vector2Int(24, 17) })
            AddGoblin(t);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (Hero h in Object.FindObjectsByType<Hero>(FindObjectsInactive.Include))
            if (h.name.StartsWith("Hero ")) Object.DestroyImmediate(h.gameObject); // spawned by this test's director
        foreach (GameObject go in goblinGos) if (go != null) Object.DestroyImmediate(go);
        goblinGos.Clear();
        goblins.Clear();
        if (directorGo != null) Object.DestroyImmediate(directorGo);
        if (heartGo != null) Object.DestroyImmediate(heartGo);
        if (managerGo != null) Object.DestroyImmediate(managerGo);
    }

    private static int CountLiving()
    {
        int n = 0;
        foreach (Goblin g in Object.FindObjectsByType<Goblin>()) if (!g.IsDead) n++;
        return n;
    }

    private void AddGoblin(Vector2Int tile)
    {
        var go = new GameObject("TestGoblin" + goblinGos.Count);
        goblinGos.Add(go);
        var goblin = go.AddComponent<Goblin>();
        goblin.Board = board;
        goblin.Renderer = boardRenderer;
        goblin.LogStats = false;
        goblin.PlaceOnTile(tile);
        var health = go.AddComponent<Health>();
        health.MaxHealth = 20;
        goblin.Health = health;
        goblins.Add(goblin);
    }

    private void DigColumnToDoor()
    {
        for (int y = 19; y <= 30; y++) board.SetTile(24, y, TileState.Floor);
    }

    private void Place(PlaceableType type, int x, int y) =>
        Assert.IsTrue(manager.TryPlaceBuilt(type, new Vector2Int(x, y), PlaceFrom, out _));

    private void KillActiveHero()
    {
        Hero hero = spawner.ActiveHero;
        hero.Health.TakeDamage(10000);
        hero.Tick(Dt);
        spawner.Tick(Dt);
    }

    // --- Readouts ---

    [Test]
    public void InitialRefresh_ThreeGoblins_NoFood_FiveMinutes()
    {
        bar.Refresh();

        Assert.AreEqual($"Goblins: {baselineGoblins + 3}", bar.GoblinText);
        Assert.AreEqual("Food: 0", bar.FoodText);
        Assert.AreEqual("Next hero: 5:00", bar.NextHeroText);
    }

    [Test]
    public void GoblinDeath_DropsTheCount()
    {
        Goblin victim = goblins[1];
        victim.Health.TakeDamage(1000);
        victim.Tick(Dt); // dies and deactivates

        bar.Refresh();
        Assert.IsTrue(victim.IsDead);
        Assert.AreEqual($"Goblins: {baselineGoblins + 2}", bar.GoblinText);
    }

    [Test]
    public void Food_ReflectsStockedPlots()
    {
        Assert.IsTrue(manager.TryPlaceBuilt(PlaceableType.MushroomPlot, new Vector2Int(21, 13), PlaceFrom, out Placeable p1));
        p1.GetComponent<MushroomPlot>().Tick(1f);
        bar.Refresh();
        Assert.AreEqual("Food: 1", bar.FoodText);

        Assert.IsTrue(manager.TryPlaceBuilt(PlaceableType.MushroomPlot, new Vector2Int(22, 13), PlaceFrom, out Placeable p2));
        p2.GetComponent<MushroomPlot>().Tick(2f);
        bar.Refresh();
        Assert.AreEqual("Food: 3", bar.FoodText, "Sums across plots");

        p1.GetComponent<MushroomPlot>().TryTakeFood(); // a goblin eats
        bar.Refresh();
        Assert.AreEqual("Food: 2", bar.FoodText);
    }

    // --- Next-hero phases ---

    [Test]
    public void BeforeHeroOne_CountsDownTheTimeTrigger()
    {
        spawner.Tick(61f); // sealed: the route gate holds him even if the timer were up
        bar.Refresh();
        Assert.AreEqual("Next hero: 3:59", bar.NextHeroText);
    }

    [Test]
    public void TriggerMetButSealed_SaysNeedsARoute()
    {
        Place(PlaceableType.LairCot, 21, 13);
        Place(PlaceableType.LairCot, 22, 13);
        Place(PlaceableType.LairCot, 23, 13);
        Place(PlaceableType.MushroomPlot, 21, 18);
        Place(PlaceableType.MushroomPlot, 22, 18);
        spawner.Tick(Dt);

        bar.Refresh();
        Assert.AreEqual(0, spawner.HeroesSpawned);
        Assert.AreEqual("Next hero: needs a route", bar.NextHeroText);
    }

    [Test]
    public void TimerUpButSealed_SaysNeedsARoute()
    {
        spawner.Tick(300f);
        bar.Refresh();
        Assert.AreEqual("Next hero: needs a route", bar.NextHeroText);
    }

    [Test]
    public void HeroActive_SaysHeroInTheDungeon()
    {
        DigColumnToDoor();
        spawner.Tick(300f);
        bar.Refresh();

        Assert.AreEqual(1, spawner.HeroesSpawned);
        Assert.AreEqual("Hero in the dungeon", bar.NextHeroText);
    }

    [Test]
    public void BetweenHeroes_ShowsTheLiveCountdown()
    {
        spawner.NextHeroDelaySeconds = 90f;
        DigColumnToDoor();
        spawner.Tick(300f);
        KillActiveHero();

        bar.Refresh();
        Assert.AreEqual("Next hero: 1:30", bar.NextHeroText);

        spawner.Tick(30f);
        bar.Refresh();
        Assert.AreEqual("Next hero: 1:00", bar.NextHeroText);
    }

    [Test]
    public void AfterTheFinalHero_Dash()
    {
        spawner.NextHeroDelaySeconds = 0.1f;
        DigColumnToDoor();
        spawner.Tick(300f);
        for (int i = 0; i < 3; i++)
        {
            KillActiveHero();
            spawner.Tick(0.5f); // the next one arrives (no-op after the third)
        }
        Assert.AreEqual(3, spawner.HeroesResolved);

        bar.Refresh();
        Assert.AreEqual($"Next hero: {TopBar.Dash}", bar.NextHeroText);
    }

    [Test]
    public void GameOver_Dash()
    {
        heart.Health.TakeDamage(100);
        game.Tick(Dt);

        bar.Refresh();
        Assert.AreEqual(GameManager.GameState.GameOver, game.State);
        Assert.AreEqual($"Next hero: {TopBar.Dash}", bar.NextHeroText);
    }

    [Test]
    public void Unwired_DegradesToZerosAndDash_NoExceptions()
    {
        bar.PlacementManager = null;
        bar.HeroSpawner = null;
        bar.GameManager = null;
        bar.Stockpile = null;

        Assert.DoesNotThrow(() => bar.Refresh());
        Assert.AreEqual("Food: 0", bar.FoodText);
        Assert.AreEqual("Stone: 0", bar.MaterialText);
        Assert.AreEqual($"Next hero: {TopBar.Dash}", bar.NextHeroText);
    }

    [Test]
    public void Materials_ShowTheStockpilesStone_FromTheFirstRefresh()
    {
        bar.Refresh();
        Assert.AreEqual("Stone: 10 · Glimmerstone: 0", bar.MaterialText, "A fresh game: 10 stone, no glimmerstone (#114), in catalog order");

        stockpile.Add(MaterialType.Stone, 5);
        bar.Refresh();
        Assert.AreEqual("Stone: 15 · Glimmerstone: 0", bar.MaterialText, "Polled on refresh");

        Assert.IsTrue(stockpile.TrySpend(MaterialType.Stone, 15));
        bar.Refresh();
        Assert.AreEqual("Stone: 0 · Glimmerstone: 0", bar.MaterialText);

        stockpile.Add(MaterialType.Glimmerstone, 4);
        bar.Refresh();
        Assert.AreEqual("Stone: 0 · Glimmerstone: 4", bar.MaterialText, "Both materials, each its own count");
    }

    [Test]
    public void MaterialReadout_FormatsNameAndCount()
    {
        Assert.AreEqual("Stone: 10", TopBar.MaterialReadout("Stone", 10));
        Assert.AreEqual("Stone: 0", TopBar.MaterialReadout("Stone", 0));
        Assert.AreEqual("Stone: 1234", TopBar.MaterialReadout("Stone", 1234));
    }

    [Test]
    public void FormatCountdown_MinutesAndSeconds_RoundedUp()
    {
        Assert.AreEqual("5:00", TopBar.FormatCountdown(300f));
        Assert.AreEqual("1:30", TopBar.FormatCountdown(90f));
        Assert.AreEqual("0:07", TopBar.FormatCountdown(6.2f));
        Assert.AreEqual("1:00", TopBar.FormatCountdown(59.01f));
        Assert.AreEqual("0:00", TopBar.FormatCountdown(0f));
        Assert.AreEqual("0:00", TopBar.FormatCountdown(-4f), "Never negative");
    }

    [Test]
    public void Defaults_FontSizeAndPoll()
    {
        var fresh = new GameObject("FreshBar").AddComponent<TopBar>();
        try
        {
            Assert.AreEqual(20, fresh.FontSize);
            Assert.AreEqual(0.25f, fresh.PollIntervalSeconds);
        }
        finally
        {
            Object.DestroyImmediate(fresh.gameObject);
        }
    }
}
