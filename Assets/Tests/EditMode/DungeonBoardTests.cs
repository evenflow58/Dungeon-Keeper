using NUnit.Framework;
using UnityEngine;

public class DungeonBoardTests
{
    private GameObject boardObject;
    private DungeonBoard board;

    [SetUp]
    public void SetUp()
    {
        boardObject = new GameObject("TestDungeonBoard");
        board = boardObject.AddComponent<DungeonBoard>();
        board.InitializeBoard();
    }

    [TearDown]
    public void TearDown()
    {
        if (boardObject != null)
        {
            Object.DestroyImmediate(boardObject);
        }
    }

    [Test]
    public void DungeonBoard_Initializes48x32_AllRock_With6x6FloorCavernCenteredAt21_13()
    {
        Assert.AreEqual(48, board.Width, "Board width should be 48");
        Assert.AreEqual(32, board.Height, "Board height should be 32");

        int floorCount = 0;
        int rockCount = 0;

        for (int x = 0; x < board.Width; x++)
        {
            for (int y = 0; y < board.Height; y++)
            {
                TileState state = board.GetTile(x, y);
                bool inCavern = (x >= 21 && x < 21 + 6) && (y >= 13 && y < 13 + 6);

                if (inCavern)
                {
                    Assert.AreEqual(TileState.Floor, state, $"Tile at ({x},{y}) in cavern should be Floor");
                    floorCount++;
                }
                else
                {
                    Assert.AreEqual(TileState.Rock, state, $"Tile at ({x},{y}) outside cavern should be Rock");
                    rockCount++;
                }
            }
        }

        Assert.AreEqual(36, floorCount, "Cavern should have 6x6 = 36 Floor tiles");
        Assert.AreEqual(48 * 32 - 36, rockCount, "Remaining tiles should all be Rock");

        // Specific corner checks of the 6x6 cavern: (21,13) to (26,18)
        Assert.AreEqual(TileState.Floor, board.GetTile(21, 13), "Cavern min corner (21,13) should be Floor");
        Assert.AreEqual(TileState.Floor, board.GetTile(26, 18), "Cavern max corner (26,18) should be Floor");
        Assert.AreEqual(TileState.Rock, board.GetTile(20, 13), "Tile (20,13) just west of cavern should be Rock");
        Assert.AreEqual(TileState.Rock, board.GetTile(27, 18), "Tile (27,18) just east of cavern should be Rock");
        Assert.AreEqual(TileState.Rock, board.GetTile(21, 12), "Tile (21,12) just south of cavern should be Rock");
        Assert.AreEqual(TileState.Rock, board.GetTile(21, 19), "Tile (21,19) just north of cavern should be Rock");
    }

    [Test]
    public void SetTile_FiresOnTileChanged_OnlyOnActualChange()
    {
        int eventFireCount = 0;
        int lastX = -1;
        int lastY = -1;
        TileState lastState = TileState.Rock;

        board.OnTileChanged += (x, y, state) =>
        {
            eventFireCount++;
            lastX = x;
            lastY = y;
            lastState = state;
        };

        // Tile at (0,0) starts as Rock
        Assert.AreEqual(TileState.Rock, board.GetTile(0, 0));

        // 1. Change to Floor -> should fire
        board.SetTile(0, 0, TileState.Floor);
        Assert.AreEqual(1, eventFireCount, "OnTileChanged should fire once when tile changes from Rock to Floor");
        Assert.AreEqual(0, lastX);
        Assert.AreEqual(0, lastY);
        Assert.AreEqual(TileState.Floor, lastState);
        Assert.AreEqual(TileState.Floor, board.GetTile(0, 0));

        // 2. Set to Floor again -> should NOT fire
        board.SetTile(0, 0, TileState.Floor);
        Assert.AreEqual(1, eventFireCount, "OnTileChanged should NOT fire when setting to the same state");

        // 3. Change to Designated -> should fire
        board.SetTile(0, 0, TileState.Designated);
        Assert.AreEqual(2, eventFireCount, "OnTileChanged should fire when tile changes from Floor to Designated");
        Assert.AreEqual(TileState.Designated, lastState);
        Assert.AreEqual(TileState.Designated, board.GetTile(0, 0));

        // 4. Set to Designated again -> should NOT fire
        board.SetTile(0, 0, TileState.Designated);
        Assert.AreEqual(2, eventFireCount, "OnTileChanged should NOT fire when setting to the same state again");
    }

    [Test]
    public void OutOfBounds_GetTile_ReturnsRock_And_SetTile_IsNoOp()
    {
        int eventFireCount = 0;
        board.OnTileChanged += (x, y, state) =>
        {
            eventFireCount++;
        };

        // Out of bounds coordinates
        int[] testCoordsX = { -1, -50, 48, 100, 0, 47 };
        int[] testCoordsY = { 0, 15, 0, 50, -1, 32 };

        for (int i = 0; i < testCoordsX.Length; i++)
        {
            int x = testCoordsX[i];
            int y = testCoordsY[i];

            if (!board.IsInBounds(x, y))
            {
                // GetTile out of bounds must return Rock
                Assert.AreEqual(TileState.Rock, board.GetTile(x, y), $"GetTile({x},{y}) out of bounds should return Rock");

                // SetTile out of bounds must be a no-op and not fire OnTileChanged
                board.SetTile(x, y, TileState.Floor);
                Assert.AreEqual(0, eventFireCount, $"SetTile({x},{y}) out of bounds should not fire OnTileChanged");
                Assert.AreEqual(TileState.Rock, board.GetTile(x, y), $"GetTile({x},{y}) should still return Rock");
            }
        }
    }

    // --- Terrain predicates ---

    [Test]
    public void IsWalkable_TrueOnlyForInBoundsFloor()
    {
        board.SetTile(0, 0, TileState.Designated);

        Assert.IsTrue(board.IsWalkable(24, 16), "Cavern Floor");
        Assert.IsFalse(board.IsWalkable(5, 5), "Rock");
        Assert.IsFalse(board.IsWalkable(0, 0), "Designated");
        Assert.IsFalse(board.IsWalkable(-1, 16), "Out of bounds (left)");
        Assert.IsFalse(board.IsWalkable(48, 16), "Out of bounds (right)");
        Assert.IsFalse(board.IsWalkable(24, 32), "Out of bounds (top)");
    }

    [Test]
    public void IsDiggable_TrueOnlyForInBoundsRock()
    {
        board.SetTile(0, 0, TileState.Designated);

        Assert.IsTrue(board.IsDiggable(5, 5), "Rock");
        Assert.IsFalse(board.IsDiggable(24, 16), "Floor");
        Assert.IsFalse(board.IsDiggable(0, 0), "Designated");
        Assert.IsFalse(board.IsDiggable(-1, -1), "Out of bounds (GetTile reports Rock there)");
        Assert.IsFalse(board.IsDiggable(48, 32), "Out of bounds (far corner)");
    }
}
