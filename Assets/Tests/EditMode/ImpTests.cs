using NUnit.Framework;
using UnityEngine;

public class ImpTests
{
    private GameObject managerGo;
    private GameObject impGo;
    private DungeonBoard board;
    private BoardRenderer boardRenderer;
    private Imp imp;

    [SetUp]
    public void SetUp()
    {
        // DungeonBoard and BoardRenderer on the same object, matching the real scene layout.
        // No Tilemap is wired, so tile centers use the fallback math: BoardOrigin (-24,-16) + (x+0.5, y+0.5).
        managerGo = new GameObject("TestDungeonManager");
        board = managerGo.AddComponent<DungeonBoard>();
        board.InitializeBoard();
        boardRenderer = managerGo.AddComponent<BoardRenderer>();
        boardRenderer.Board = board;

        impGo = new GameObject("TestImp");
        imp = impGo.AddComponent<Imp>();
        // Start() is not called in EditMode tests, so wire and spawn explicitly.
        imp.Board = board;
        imp.Renderer = boardRenderer;
        imp.MoveSpeed = 4f;
        imp.Spawn();
    }

    [TearDown]
    public void TearDown()
    {
        if (impGo != null) Object.DestroyImmediate(impGo);
        if (managerGo != null) Object.DestroyImmediate(managerGo);
    }

    private Vector3 Center(int x, int y) => boardRenderer.GetTileCenterWorldPosition(x, y);

    private void RunUntilIdle(int maxSteps = 1000, float dt = 0.02f)
    {
        for (int i = 0; i < maxSteps && imp.IsMoving; i++) imp.Advance(dt);
        Assert.IsFalse(imp.IsMoving, "Imp never finished its path");
    }

    // --- Spawn tile ---

    [Test]
    public void GetCavernCenterTile_Defaults_Returns24_16()
    {
        // 48x32 board, 6x6 cavern at (21,13)-(26,18).
        Assert.AreEqual(new Vector2Int(24, 16), Imp.GetCavernCenterTile(48, 32, 6));
    }

    [Test]
    public void GetCavernCenterTile_OddCavern_ReturnsExactCenter()
    {
        // 5x5 cavern on 48x32 spans (21,13)-(25,17); exact center is (23,15).
        Assert.AreEqual(new Vector2Int(23, 15), Imp.GetCavernCenterTile(48, 32, 5));
    }

    [Test]
    public void GetCavernCenterTile_IsFloorOnDefaultBoard()
    {
        Vector2Int c = Imp.GetCavernCenterTile(board.Width, board.Height, board.CavernSize);
        Assert.AreEqual(TileState.Floor, board.GetTile(c.x, c.y));
    }

    [Test]
    public void Spawn_PlacesImpOnCavernCenter()
    {
        Assert.AreEqual(new Vector2Int(24, 16), imp.CurrentTile);
        Assert.AreEqual(Center(24, 16), impGo.transform.position);
        Assert.IsFalse(imp.IsMoving);
    }

    // --- Reachable destinations ---

    [Test]
    public void Advance_MovesAtTilesPerSecond()
    {
        Assert.IsTrue(imp.SetDestination(new Vector2Int(26, 16)));
        imp.Advance(0.25f); // 4 tiles/s * 0.25s = exactly one tile
        Assert.AreEqual(new Vector2Int(25, 16), imp.CurrentTile);
        Assert.That(Vector3.Distance(Center(25, 16), impGo.transform.position), Is.LessThan(1e-4f));
    }

    [Test]
    public void SetDestination_Reachable_PathsAroundRockAndStopsOnTarget()
    {
        board.SetTile(25, 16, TileState.Rock); // block the straight line inside the cavern

        Assert.IsTrue(imp.SetDestination(new Vector2Int(26, 16)));
        for (int i = 0; i < 1000 && imp.IsMoving; i++)
        {
            imp.Advance(0.02f);
            Assert.AreNotEqual(new Vector2Int(25, 16), imp.CurrentTile, "Imp walked through Rock");
        }

        Assert.IsFalse(imp.IsMoving);
        Assert.AreEqual(new Vector2Int(26, 16), imp.CurrentTile);
        Assert.AreEqual(Center(26, 16), impGo.transform.position); // stops exactly on the center
    }

    [Test]
    public void CurrentTile_SwitchesAtSegmentMidpoint()
    {
        imp.SetDestination(new Vector2Int(26, 16));
        imp.Advance(0.1f); // 0.4 tiles: still in (24,16)'s cell
        Assert.AreEqual(new Vector2Int(24, 16), imp.CurrentTile);
        imp.Advance(0.05f); // 0.6 tiles: now in (25,16)'s cell
        Assert.AreEqual(new Vector2Int(25, 16), imp.CurrentTile);
    }

    // --- Unreachable destinations ---

    [Test]
    public void SetDestination_RockTile_StaysPut()
    {
        Assert.IsFalse(imp.SetDestination(new Vector2Int(0, 0)));
        imp.Advance(1f);
        Assert.IsFalse(imp.IsMoving);
        Assert.AreEqual(new Vector2Int(24, 16), imp.CurrentTile);
        Assert.AreEqual(Center(24, 16), impGo.transform.position);
    }

    [Test]
    public void SetDestination_EnclosedFloor_StaysPut()
    {
        board.SetTile(5, 5, TileState.Floor); // isolated pocket surrounded by Rock

        Assert.IsFalse(imp.SetDestination(new Vector2Int(5, 5)));
        imp.Advance(1f);
        Assert.IsFalse(imp.IsMoving);
        Assert.AreEqual(Center(24, 16), impGo.transform.position);
    }

    [Test]
    public void SetDestination_UnreachableMidMove_KeepsCurrentOrders()
    {
        imp.SetDestination(new Vector2Int(26, 16));
        imp.Advance(0.1f);

        Assert.IsFalse(imp.SetDestination(new Vector2Int(0, 0)));
        RunUntilIdle();
        Assert.AreEqual(new Vector2Int(26, 16), imp.CurrentTile);
    }

    // --- Repathing ---

    [Test]
    public void SetDestination_MidMove_FinishesCurrentStepThenRepaths()
    {
        imp.SetDestination(new Vector2Int(26, 16));
        imp.Advance(0.1f); // 0.4 tiles toward (25,16)

        Assert.IsTrue(imp.SetDestination(new Vector2Int(24, 18)));
        Assert.AreEqual(new Vector2Int(25, 16), imp.NextTile, "Repath should continue to the waypoint ahead");

        RunUntilIdle();
        Assert.AreEqual(new Vector2Int(24, 18), imp.CurrentTile);
        Assert.AreEqual(Center(24, 18), impGo.transform.position);
    }

    [Test]
    public void SetDestination_OnTileCenter_RepathsFromCurrentTile()
    {
        imp.SetDestination(new Vector2Int(26, 16));
        imp.Advance(0.25f); // exactly on (25,16)'s center

        Assert.IsTrue(imp.SetDestination(new Vector2Int(25, 18)));
        Assert.AreEqual(new Vector2Int(25, 17), imp.NextTile);

        RunUntilIdle();
        Assert.AreEqual(new Vector2Int(25, 18), imp.CurrentTile);
    }

    [Test]
    public void SetDestination_CurrentTile_IsNoOp()
    {
        Assert.IsTrue(imp.SetDestination(new Vector2Int(24, 16)));
        Assert.IsFalse(imp.IsMoving);
        Assert.AreEqual(Center(24, 16), impGo.transform.position);
    }
}
