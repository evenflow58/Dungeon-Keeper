using NUnit.Framework;
using UnityEngine;

public class TileHoverTests
{
    private GameObject managerGo;
    private GameObject tileHoverGo;
    private DungeonBoard board;
    private BoardRenderer boardRenderer;
    private TileHover tileHover;

    [SetUp]
    public void SetUp()
    {
        // DungeonBoard and BoardRenderer on the same object, matching the real scene layout.
        // No Tilemap is wired, so WorldToBoardCoords uses the fallback math path.
        managerGo = new GameObject("TestDungeonManager");
        board = managerGo.AddComponent<DungeonBoard>();
        board.InitializeBoard();
        boardRenderer = managerGo.AddComponent<BoardRenderer>();
        // Awake already called InitializeReferences; board found via GetComponent.

        tileHoverGo = new GameObject("TestTileHover");
        tileHover = tileHoverGo.AddComponent<TileHover>();
        // Start() is not called in EditMode tests, so highlight/canvas are never created.
        tileHover.Board = board;
        tileHover.Renderer = boardRenderer;
    }

    [TearDown]
    public void TearDown()
    {
        if (tileHoverGo != null) Object.DestroyImmediate(tileHoverGo);
        if (managerGo != null) Object.DestroyImmediate(managerGo);
    }

    // --- WorldToBoardCoords fallback math ---

    [Test]
    public void WorldToBoardCoords_Fallback_BoardCorner_ReturnsZeroZero()
    {
        // BoardOrigin = (-24, -16, 0). Tile (0,0) center is at (-23.5, -15.5).
        Vector2Int result = boardRenderer.WorldToBoardCoords(new Vector3(-23.5f, -15.5f, 0f));
        Assert.AreEqual(new Vector2Int(0, 0), result);
    }

    [Test]
    public void WorldToBoardCoords_Fallback_BoardCenter_Returns24_16()
    {
        // Tile (24,16) center is at BoardOrigin + (24.5, 16.5) = (0.5, 0.5).
        Vector2Int result = boardRenderer.WorldToBoardCoords(new Vector3(0.5f, 0.5f, 0f));
        Assert.AreEqual(new Vector2Int(24, 16), result);
    }

    // --- Label formatting ---

    [Test]
    public void FormatTileLabel_AllThreeStates_FormatsCorrectly()
    {
        Assert.AreEqual("Rock (0, 0)", TileHover.FormatTileLabel(TileState.Rock, 0, 0));
        Assert.AreEqual("Floor (24, 16)", TileHover.FormatTileLabel(TileState.Floor, 24, 16));
        Assert.AreEqual("Designated (5, 5)", TileHover.FormatTileLabel(TileState.Designated, 5, 5));
    }

    // --- Hover resolution ---

    [Test]
    public void TryGetHoverState_CavernTile_ReturnsFloor()
    {
        // Tile (24,16) is inside the 6x6 cavern at (21,13)-(26,18). Center at (0.5, 0.5).
        bool hit = tileHover.TryGetHoverState(new Vector3(0.5f, 0.5f, 0f), out TileState state, out Vector2Int coords);
        Assert.IsTrue(hit);
        Assert.AreEqual(TileState.Floor, state);
        Assert.AreEqual(new Vector2Int(24, 16), coords);
    }

    [Test]
    public void TryGetHoverState_OutsideCavern_ReturnsRock()
    {
        // Tile (0,0) is outside the cavern. Center at (-23.5, -15.5).
        bool hit = tileHover.TryGetHoverState(new Vector3(-23.5f, -15.5f, 0f), out TileState state, out Vector2Int coords);
        Assert.IsTrue(hit);
        Assert.AreEqual(TileState.Rock, state);
        Assert.AreEqual(new Vector2Int(0, 0), coords);
    }

    [Test]
    public void TryGetHoverState_OutOfBounds_ReturnsFalse()
    {
        // World (-25, -17) -> board coords (-1, -1) -> out of bounds.
        bool hit = tileHover.TryGetHoverState(new Vector3(-25f, -17f, 0f), out _, out _);
        Assert.IsFalse(hit);
    }
}
