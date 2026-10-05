using NUnit.Framework;
using UnityEngine;

public class HeroTests
{
    private GameObject managerGo;
    private GameObject heroGo;
    private DungeonBoard board;
    private BoardRenderer boardRenderer;
    private Hero hero;
    private Health health;

    [SetUp]
    public void SetUp()
    {
        // Default board: 48x32 Rock with the 6x6 Floor cavern at (21,13)-(26,18).
        managerGo = new GameObject("TestDungeonManager");
        board = managerGo.AddComponent<DungeonBoard>();
        board.InitializeBoard();
        boardRenderer = managerGo.AddComponent<BoardRenderer>();
        boardRenderer.Board = board;

        heroGo = new GameObject("TestHero");
        health = heroGo.AddComponent<Health>();
        health.MaxHealth = 30;
        health.Team = HealthTeam.Hero;
        hero = heroGo.AddComponent<Hero>();
        // Start() is not called in EditMode tests, so wire and place explicitly.
        hero.Board = board;
        hero.Renderer = boardRenderer;
        hero.Health = health;
        hero.MoveSpeed = 3f;
        hero.PlaceOnTile(new Vector2Int(23, 15));
    }

    [TearDown]
    public void TearDown()
    {
        if (heroGo != null) Object.DestroyImmediate(heroGo);
        if (managerGo != null) Object.DestroyImmediate(managerGo);
    }

    private Vector3 Center(int x, int y) => boardRenderer.GetTileCenterWorldPosition(x, y);

    [Test]
    public void PlaceOnTile_SnapsToTileCenter()
    {
        hero.PlaceOnTile(new Vector2Int(25, 17));

        Assert.AreEqual(new Vector2Int(25, 17), hero.CurrentTile);
        Assert.AreEqual(new Vector2Int(25, 17), hero.NextTile);
        Assert.AreEqual(Center(25, 17), heroGo.transform.position);
        Assert.IsFalse(hero.IsMoving);
    }

    [Test]
    public void SetDestination_WalksPath_CurrentTileFlipsAtMidpoint()
    {
        Assert.IsTrue(hero.SetDestination(new Vector2Int(26, 15)));
        CollectionAssert.AreEqual(new[] { new Vector2Int(24, 15), new Vector2Int(25, 15), new Vector2Int(26, 15) }, hero.RemainingPath);

        hero.Advance(0.4f / 3f); // 0.4 tiles: still in (23,15)'s cell
        Assert.AreEqual(new Vector2Int(23, 15), hero.CurrentTile);
        hero.Advance(0.2f / 3f); // 0.6 tiles: now in (24,15)'s cell
        Assert.AreEqual(new Vector2Int(24, 15), hero.CurrentTile);

        for (int i = 0; i < 100 && hero.IsMoving; i++) hero.Advance(0.02f);
        Assert.AreEqual(new Vector2Int(26, 15), hero.CurrentTile);
        Assert.AreEqual(Center(26, 15), heroGo.transform.position);
    }

    [Test]
    public void SetDestination_Unreachable_KeepsCurrentOrders()
    {
        Assert.IsTrue(hero.SetDestination(new Vector2Int(26, 15)));
        hero.Advance(0.1f);

        Assert.IsFalse(hero.SetDestination(new Vector2Int(0, 0)), "Rock: no path");
        for (int i = 0; i < 100 && hero.IsMoving; i++) hero.Advance(0.02f);
        Assert.AreEqual(new Vector2Int(26, 15), hero.CurrentTile);
    }

    [Test]
    public void PathsOffARockStartTile()
    {
        // He spawns on a Rock entrance tile; the start tile always counts as walkable.
        board.SetTile(24, 19, TileState.Rock);
        hero.PlaceOnTile(new Vector2Int(24, 19));

        Assert.IsTrue(hero.SetDestination(new Vector2Int(24, 17)));
        for (int i = 0; i < 100 && hero.IsMoving; i++) hero.Advance(0.02f);
        Assert.AreEqual(new Vector2Int(24, 17), hero.CurrentTile);
    }

    [Test]
    public void LethalDamage_DiesOnNextTick_Deactivates()
    {
        hero.SetDestination(new Vector2Int(26, 15));
        health.TakeDamage(30);
        Assert.IsFalse(hero.IsDead, "Death is the body's response on its next Tick");

        hero.Tick(0.02f);
        Assert.IsTrue(hero.IsDead);
        Assert.IsFalse(heroGo.activeSelf);
        Assert.IsFalse(hero.IsMoving, "Orders cleared");
        Assert.IsFalse(hero.SetDestination(new Vector2Int(26, 15)));
    }

    [Test]
    public void NoHealth_NeverDies()
    {
        hero.Health = null;
        health.TakeDamage(1000);

        for (int i = 0; i < 10; i++) hero.Tick(0.02f);
        Assert.IsFalse(hero.IsDead);
        Assert.IsTrue(heroGo.activeSelf);
    }
}
