using NUnit.Framework;
using UnityEngine;

public class EndScreenTests
{
    private static readonly Vector2Int HeartTile = new Vector2Int(24, 14);
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
    private EndScreen screen;

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
        spawner.NextHeroDelaySeconds = 0.1f;
        game = directorGo.AddComponent<GameManager>();
        game.Heart = heart;
        game.HeroSpawner = spawner;
        spawner.GameManager = game;

        screen = directorGo.AddComponent<EndScreen>();
        screen.GameManager = game;

        for (int y = 19; y <= 30; y++) board.SetTile(24, y, TileState.Floor); // route to the door
    }

    [TearDown]
    public void TearDown()
    {
        foreach (Hero h in Object.FindObjectsByType<Hero>(FindObjectsInactive.Include))
            if (h.name.StartsWith("Hero ")) Object.DestroyImmediate(h.gameObject);
        if (directorGo != null) Object.DestroyImmediate(directorGo);
        if (heartGo != null) Object.DestroyImmediate(heartGo);
        if (managerGo != null) Object.DestroyImmediate(managerGo);
    }

    private void KillActiveHero()
    {
        Hero hero = spawner.ActiveHero;
        hero.Health.TakeDamage(10000);
        hero.Tick(Dt);
        spawner.Tick(Dt);
    }

    [Test]
    public void FreshRun_NotShowing()
    {
        screen.Refresh();
        Assert.IsFalse(screen.IsShowing);
        Assert.AreEqual("", screen.ShownText);
    }

    [Test]
    public void HeartDestroyed_ShowsTheGameOverLine()
    {
        heart.Health.TakeDamage(100);
        game.Tick(Dt);
        screen.Refresh();

        Assert.AreEqual(GameManager.GameState.GameOver, game.State);
        Assert.IsTrue(screen.IsShowing);
        Assert.AreEqual(GameManager.GameOverLine, screen.ShownText);
        Assert.AreEqual("The dark goes quiet.", screen.ShownText);
    }

    [Test]
    public void AllHeroesDealtWith_ShowsTheVictoryLine()
    {
        spawner.Tick(300f);
        KillActiveHero();
        spawner.Tick(0.5f);
        KillActiveHero();
        spawner.Tick(0.5f);
        KillActiveHero();
        game.Tick(Dt);
        screen.Refresh();

        Assert.AreEqual(GameManager.GameState.Victory, game.State);
        Assert.IsTrue(screen.IsShowing);
        Assert.AreEqual(GameManager.VictoryLine, screen.ShownText);
    }

    [Test]
    public void MidRun_OneHeroOfThree_NotShowing()
    {
        spawner.Tick(300f);
        KillActiveHero();
        game.Tick(Dt);
        screen.Refresh();

        Assert.AreEqual(GameManager.GameState.Playing, game.State);
        Assert.IsFalse(screen.IsShowing);
        Assert.AreEqual("", screen.ShownText);
    }

    [Test]
    public void Idempotent_InAnEndState()
    {
        heart.Health.TakeDamage(100);
        game.Tick(Dt);
        for (int i = 0; i < 5; i++) screen.Refresh();

        Assert.IsTrue(screen.IsShowing);
        Assert.AreEqual(GameManager.GameOverLine, screen.ShownText);
    }

    [Test]
    public void Unwired_NeverShows_NoException()
    {
        screen.GameManager = null;
        Assert.DoesNotThrow(() => screen.Refresh());
        Assert.IsFalse(screen.IsShowing);
        Assert.AreEqual("", screen.ShownText);
    }

    [Test]
    public void LineFor_MapsEachState()
    {
        Assert.AreEqual("", EndScreen.LineFor(null));
        Assert.AreEqual("", EndScreen.LineFor(game), "Playing");
        heart.Health.TakeDamage(100);
        game.Tick(Dt);
        Assert.AreEqual(GameManager.GameOverLine, EndScreen.LineFor(game));
    }

    [Test]
    public void Defaults_AndLabel()
    {
        Assert.AreEqual(0.25f, screen.PollIntervalSeconds);
        Assert.AreEqual("Play again", EndScreen.PlayAgainLabel);
    }
}
