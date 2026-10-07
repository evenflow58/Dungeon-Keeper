using NUnit.Framework;
using UnityEngine;

public class GroundShadowTests
{
    private GameObject root;
    private GameObject managerGo;

    [TearDown]
    public void TearDown()
    {
        if (root != null) Object.DestroyImmediate(root);
        if (managerGo != null) Object.DestroyImmediate(managerGo);
    }

    [Test]
    public void Create_LiesFlatOnTheGround_UnderTheRoot()
    {
        root = new GameObject("Owner");
        root.transform.position = new Vector3(3.5f, 0f, -2.5f);

        SpriteRenderer disc = GroundShadow.Create(root.transform, new GroundShadowStyle(0.7f, 0.4f), 0.01f);

        Assert.AreSame(root.transform, disc.transform.parent);
        Assert.AreEqual(GroundShadow.ObjectName, disc.gameObject.name);
        Assert.AreEqual(new Vector3(3.5f, 0.01f, -2.5f), disc.transform.position, "At the ground point, just above it");
        Assert.Less(Vector3.Distance(Vector3.up, -disc.transform.forward), 1e-5f, "Sprite faces up: lying on XZ");
        Assert.AreEqual(new Vector3(0.7f, 0.7f, 1f), disc.transform.localScale, "Diameter in tiles");
        Assert.AreEqual(new Color(0f, 0f, 0f, 0.4f), disc.color);
        Assert.IsNotNull(disc.sprite);
    }

    [Test]
    public void Create_SharesOneDiscSprite()
    {
        root = new GameObject("Owner");
        SpriteRenderer a = GroundShadow.Create(root.transform, new GroundShadowStyle(0.5f, 0.3f), 0.01f);
        SpriteRenderer b = GroundShadow.Create(root.transform, new GroundShadowStyle(0.9f, 0.5f), 0.01f);
        Assert.AreSame(a.sprite, b.sprite);
    }

    [Test]
    public void OverlayLiftOf_FallsBackWithoutARenderer()
    {
        Assert.AreEqual(BoardRenderer.DefaultOverlayLift, BoardRenderer.OverlayLiftOf(null));
    }

    [Test]
    public void PlacedCot_StandsOnADisc()
    {
        managerGo = new GameObject("TestDungeonManager");
        var board = managerGo.AddComponent<DungeonBoard>();
        board.InitializeBoard();
        var boardRenderer = managerGo.AddComponent<BoardRenderer>();
        boardRenderer.Board = board;
        var manager = managerGo.AddComponent<PlacementManager>();
        manager.Board = board;
        manager.Renderer = boardRenderer;

        Assert.IsTrue(manager.TryPlace(PlaceableType.LairCot, new Vector2Int(22, 14), new Vector2Int(24, 16), out Placeable cot));

        Transform disc = cot.transform.Find(GroundShadow.ObjectName);
        Assert.IsNotNull(disc, "The cot has a ground shadow");
        Assert.AreEqual(boardRenderer.OverlayLift, disc.position.y, 1e-6f);
        Assert.AreEqual(cot.transform.position.x, disc.position.x, 1e-6f);
        Assert.AreEqual(cot.transform.position.z, disc.position.z, 1e-6f);
    }
}
