using NUnit.Framework;
using UnityEngine;

public class DigDesignatorTests
{
    // All tests target the three static pure-logic methods: NormalizeRect,
    // ClampToBoard, and ApplyRect. No MonoBehaviour lifecycle is needed.

    private GameObject boardGo;
    private DungeonBoard board;

    [SetUp]
    public void SetUp()
    {
        boardGo = new GameObject("TestBoard");
        board = boardGo.AddComponent<DungeonBoard>();
        board.InitializeBoard(); // 48x32, cavern Floor at (21-26, 13-18)
    }

    [TearDown]
    public void TearDown()
    {
        if (boardGo != null) Object.DestroyImmediate(boardGo);
    }

    // ---- NormalizeRect: all four drag directions produce the same RectInt ----

    [Test]
    public void NormalizeRect_AllFourDragDirections_ProduceSameRect()
    {
        var a = new Vector2Int(5, 5);
        var b = new Vector2Int(7, 8);
        var expected = new RectInt(5, 5, 3, 4);

        Assert.AreEqual(expected, DigDesignator.NormalizeRect(a, b),
            "right-up drag");
        Assert.AreEqual(expected, DigDesignator.NormalizeRect(b, a),
            "left-down drag");
        Assert.AreEqual(expected, DigDesignator.NormalizeRect(new Vector2Int(7, 5), new Vector2Int(5, 8)),
            "left-up drag");
        Assert.AreEqual(expected, DigDesignator.NormalizeRect(new Vector2Int(5, 8), new Vector2Int(7, 5)),
            "right-down drag");
    }

    // ---- NormalizeRect: 1x1 when anchor == current ----

    [Test]
    public void NormalizeRect_AnchorEqualsCurrentTile_ReturnsOneByOne()
    {
        var rect = DigDesignator.NormalizeRect(new Vector2Int(10, 10), new Vector2Int(10, 10));
        Assert.AreEqual(new RectInt(10, 10, 1, 1), rect);
    }

    // ---- ClampToBoard ----

    [Test]
    public void ClampToBoard_NegativeCoords_ClampsToZero()
    {
        var result = DigDesignator.ClampToBoard(new Vector2Int(-5, -3), 48, 32);
        Assert.AreEqual(new Vector2Int(0, 0), result);
    }

    [Test]
    public void ClampToBoard_OvershootCoords_ClampsToLastTile()
    {
        var result = DigDesignator.ClampToBoard(new Vector2Int(50, 40), 48, 32);
        Assert.AreEqual(new Vector2Int(47, 31), result);
    }

    // ---- ApplyRect: Designate ----

    [Test]
    public void ApplyRect_Designate_ChangesOnlyRock_LeavesFloorUnchanged()
    {
        // Cavern Floor: x in [21-26], y in [13-18].
        // Rect (20,12,3,3) = x in [20,21,22], y in [12,13,14].
        // Rock tiles: (20,12),(21,12),(22,12),(20,13),(20,14) → 5 tiles.
        // Floor tiles: (21,13),(22,13),(21,14),(22,14) → 4 tiles, unchanged.
        var rect = new RectInt(20, 12, 3, 3);
        int changed = DigDesignator.ApplyRect(board, rect, DigDesignator.DesignateMode.Designate);

        Assert.AreEqual(5, changed, "5 Rock tiles in rect should become Designated");
        Assert.AreEqual(TileState.Designated, board.GetTile(20, 12));
        Assert.AreEqual(TileState.Floor,      board.GetTile(21, 13), "Floor tile must not change");
        Assert.AreEqual(TileState.Floor,      board.GetTile(22, 14), "Floor tile must not change");
    }

    [Test]
    public void ApplyRect_Designate_AlreadyDesignated_NotCountedAgain()
    {
        board.SetTile(5, 5, TileState.Designated);
        int changed = DigDesignator.ApplyRect(board, new RectInt(5, 5, 1, 1), DigDesignator.DesignateMode.Designate);
        Assert.AreEqual(0, changed, "Already-Designated tile must not be counted again");
    }

    [Test]
    public void ApplyRect_Designate_OneByOneOnRock_ChangesExactlyOneTile()
    {
        int changed = DigDesignator.ApplyRect(board, new RectInt(0, 0, 1, 1), DigDesignator.DesignateMode.Designate);
        Assert.AreEqual(1, changed);
        Assert.AreEqual(TileState.Designated, board.GetTile(0, 0));
    }

    // ---- ApplyRect: Clear ----

    [Test]
    public void ApplyRect_Clear_ChangesOnlyDesignated_LeavesRockUnchanged()
    {
        // Designate a 3x3 patch at (5,5) — all Rock, so 9 tiles become Designated.
        DigDesignator.ApplyRect(board, new RectInt(5, 5, 3, 3), DigDesignator.DesignateMode.Designate);

        // Clear a larger 5x5 area that encloses the patch plus Rock tiles.
        int changed = DigDesignator.ApplyRect(board, new RectInt(4, 4, 5, 5), DigDesignator.DesignateMode.Clear);

        Assert.AreEqual(9, changed, "Only the 9 Designated tiles should be cleared");
        Assert.AreEqual(TileState.Rock, board.GetTile(5, 5),  "Cleared tile becomes Rock");
        Assert.AreEqual(TileState.Rock, board.GetTile(4, 4),  "Rock tile outside patch must stay Rock");
    }
}
