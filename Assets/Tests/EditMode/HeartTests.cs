using NUnit.Framework;
using UnityEngine;

public class HeartTests
{
    private GameObject managerGo;
    private GameObject heartGo;
    private DungeonBoard board;
    private BoardRenderer boardRenderer;
    private Heart heart;
    private Health health;

    [SetUp]
    public void SetUp()
    {
        // DungeonBoard and BoardRenderer on one object, as in GoblinTests (BoardRenderer's ground-plane tile math).
        managerGo = new GameObject("TestDungeonManager");
        board = managerGo.AddComponent<DungeonBoard>();
        board.InitializeBoard();
        boardRenderer = managerGo.AddComponent<BoardRenderer>();
        boardRenderer.Board = board;

        heartGo = new GameObject("TestHeart");
        heart = heartGo.AddComponent<Heart>(); // RequireComponent adds Health first; Reset configures it
        health = heartGo.GetComponent<Health>();
        // Start() doesn't run in EditMode tests: wire explicitly.
        heart.Board = board;
        heart.Renderer = boardRenderer;
        heart.Health = health;
    }

    [TearDown]
    public void TearDown()
    {
        if (heartGo != null) Object.DestroyImmediate(heartGo);
        if (managerGo != null) Object.DestroyImmediate(managerGo);
    }

    [Test]
    public void Defaults_Tile_HundredHp_MonsterTeam_NotDestroyed()
    {
        Assert.AreEqual(new Vector2Int(24, 14), heart.Tile);
        Assert.IsNotNull(health, "Heart requires a Health");
        Assert.AreEqual(100, health.MaxHealth);
        Assert.AreEqual(100, health.CurrentHealth);
        Assert.AreEqual(HealthTeam.Monster, health.Team);
        Assert.IsFalse(heart.IsDestroyed);
    }

    [Test]
    public void DefaultTile_IsCavernFloor_NotASpawnTile()
    {
        Vector2Int t = heart.Tile;
        Assert.AreEqual(TileState.Floor, board.GetTile(t.x, t.y));
        CollectionAssert.DoesNotContain(
            new[] { new Vector2Int(24, 16), new Vector2Int(23, 15), new Vector2Int(25, 15), new Vector2Int(24, 17) }, t,
            "Imp and goblin spawn tiles are taken");
    }

    [Test]
    public void NinetyNineDamage_NotDestroyed_OneMore_Destroyed()
    {
        health.TakeDamage(99);
        Assert.AreEqual(1, health.CurrentHealth);
        Assert.IsFalse(heart.IsDestroyed);

        health.TakeDamage(1);
        Assert.IsTrue(health.IsDead);
        Assert.IsTrue(heart.IsDestroyed);
    }

    [Test]
    public void ExactlyHundredDamage_Destroyed_GameObjectStaysActive()
    {
        health.TakeDamage(100);
        Assert.IsTrue(heart.IsDestroyed);
        Assert.IsTrue(heartGo.activeSelf, "Health never deactivates: the defeat screen needs the Heart");
    }

    [Test]
    public void PlaceOnTile_SnapsToTileCenter()
    {
        heart.PlaceOnTile(new Vector2Int(24, 14));
        Assert.AreEqual(boardRenderer.GetTileCenterWorldPosition(24, 14), heartGo.transform.position);
        Assert.AreEqual(new Vector2Int(24, 14), heart.Tile);

        heart.PlaceOnTile(new Vector2Int(22, 17));
        Assert.AreEqual(boardRenderer.GetTileCenterWorldPosition(22, 17), heartGo.transform.position);
        Assert.AreEqual(new Vector2Int(22, 17), heart.Tile);
    }

    [Test]
    public void TileStaysWalkable()
    {
        heart.PlaceOnTile(heart.Tile);
        Assert.IsTrue(board.IsWalkable(heart.Tile.x, heart.Tile.y), "The Heart doesn't block its tile");
    }

    [Test]
    public void MissingHealth_NotDestroyed()
    {
        heart.Health = null;
        Assert.IsFalse(heart.IsDestroyed);
    }
}
