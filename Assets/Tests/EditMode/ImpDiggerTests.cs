using System;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

public class ImpDiggerTests
{
    private const float Dt = 0.02f;

    private GameObject managerGo;
    private GameObject impGo;
    private DungeonBoard board;
    private BoardRenderer boardRenderer;
    private Imp imp;
    private ImpDigger digger;

    [SetUp]
    public void SetUp()
    {
        // Default board: 48x32 Rock with the 6x6 Floor cavern at (21,13)-(26,18). Imp spawns at (24,16).
        managerGo = new GameObject("TestDungeonManager");
        board = managerGo.AddComponent<DungeonBoard>();
        board.InitializeBoard();
        boardRenderer = managerGo.AddComponent<BoardRenderer>();
        boardRenderer.Board = board;

        impGo = new GameObject("TestImp");
        imp = impGo.AddComponent<Imp>();
        imp.Board = board;
        imp.Renderer = boardRenderer;
        imp.MoveSpeed = 4f;
        imp.Spawn();

        digger = impGo.AddComponent<ImpDigger>();
        digger.Board = board;
        digger.Imp = imp;
        digger.DigSecondsPerTile = 2.5f;
    }

    [TearDown]
    public void TearDown()
    {
        if (impGo != null) Object.DestroyImmediate(impGo);
        if (managerGo != null) Object.DestroyImmediate(managerGo);
    }

    private void Designate(int x, int y) => board.SetTile(x, y, TileState.Designated);

    private void Step()
    {
        imp.Advance(Dt);
        digger.Tick(Dt);
    }

    /// <summary>Steps until the condition holds; fails if it doesn't within maxSeconds. Returns seconds stepped.</summary>
    private float StepUntil(Func<bool> condition, float maxSeconds, string because)
    {
        float elapsed = 0f;
        while (!condition())
        {
            Assert.Less(elapsed, maxSeconds, "Timed out waiting: " + because);
            Step();
            elapsed += Dt;
        }
        return elapsed;
    }

    private bool IsIdle() => digger.CurrentTarget == null && !imp.IsMoving;

    // --- Target selection ---

    [Test]
    public void TrySelectTarget_NearestByPathLength_BeatsNearerStraightLine()
    {
        // Wall off the cavern's top row so (24,18) is only reachable around the west end.
        for (int x = 22; x <= 26; x++) board.SetTile(x, 17, TileState.Rock);
        Designate(24, 19); // 3 tiles away in a straight line, but its only stand-point (24,18) is 8 steps away
        Designate(27, 14); // ~3.6 tiles away in a straight line; stand-point (26,14) is 4 steps away

        Assert.IsTrue(ImpDigger.TrySelectTarget(board, new Vector2Int(24, 16), out Vector2Int target, out Vector2Int stand));
        Assert.AreEqual(new Vector2Int(27, 14), target);
        Assert.AreEqual(new Vector2Int(26, 14), stand);
    }

    [Test]
    public void TrySelectTarget_EnclosedDesignation_SkippedForReachableOne()
    {
        Designate(5, 5);   // No Floor neighbor anywhere near it; earlier in scan order than the reachable tile
        Designate(27, 16);

        Assert.IsTrue(ImpDigger.TrySelectTarget(board, new Vector2Int(24, 16), out Vector2Int target, out _));
        Assert.AreEqual(new Vector2Int(27, 16), target);
        Assert.AreEqual(TileState.Designated, board.GetTile(5, 5));
    }

    [Test]
    public void TrySelectTarget_OnlyEnclosedDesignation_ReturnsFalseAndLeavesItDesignated()
    {
        Designate(5, 5);

        Assert.IsFalse(ImpDigger.TrySelectTarget(board, new Vector2Int(24, 16), out _, out _));
        Assert.AreEqual(TileState.Designated, board.GetTile(5, 5));
    }

    [Test]
    public void TrySelectTarget_NoDesignations_ReturnsFalse()
    {
        Assert.IsFalse(ImpDigger.TrySelectTarget(board, new Vector2Int(24, 16), out _, out _));
    }

    [Test]
    public void TrySelectTarget_StandPoint_IsFindPathToAdjacentEndpoint()
    {
        // A designation inside the cavern has four Floor neighbors; Pathfinder picks among them.
        Designate(23, 14);
        var from = new Vector2Int(24, 16);

        Assert.IsTrue(ImpDigger.TrySelectTarget(board, from, out Vector2Int target, out Vector2Int stand));
        var expected = Pathfinder.FindPathToAdjacent(board, from, new Vector2Int(23, 14));
        Assert.AreEqual(new Vector2Int(23, 14), target);
        Assert.AreEqual(expected[expected.Count - 1], stand);
        Assert.IsTrue(board.IsWalkable(stand.x, stand.y));
        Assert.AreEqual(1, Mathf.Abs(stand.x - 23) + Mathf.Abs(stand.y - 14), "Stand-point must be a 4-neighbor");
    }

    [Test]
    public void TrySelectTarget_Tie_ResolvesToScanOrderFirst()
    {
        // Both stand-points are 2 steps from (24,16): (26,16) for (27,16), (24,18) for (24,19).
        // Designated in reverse scan order to show designation order doesn't matter.
        Designate(27, 16);
        Designate(24, 19);

        Assert.IsTrue(ImpDigger.TrySelectTarget(board, new Vector2Int(24, 16), out Vector2Int target, out _));
        Assert.AreEqual(new Vector2Int(24, 19), target, "x-ascending scan reaches (24,19) first");
    }

    // --- Dig cycle ---

    [Test]
    public void DigCycle_ConvertsOnlyAfterFullDigTime_ThenMovesToNextDesignation()
    {
        Designate(27, 16); // stand-point (26,16), 2 steps
        Designate(27, 15); // further: stand-point 3 steps away from the start

        StepUntil(() => digger.IsDigging, 5f, "imp to arrive and start digging");
        Assert.AreEqual(new Vector2Int(27, 16), digger.CurrentTarget);
        Assert.AreEqual(new Vector2Int(26, 16), imp.CurrentTile);

        // Just short of 2.5s of digging: not converted yet.
        for (int i = 0; i < 120; i++) Step(); // 2.4s
        Assert.AreEqual(TileState.Designated, board.GetTile(27, 16));
        Assert.IsTrue(digger.IsDigging);
        Assert.Less(digger.DigProgress, 1f);

        StepUntil(() => board.GetTile(27, 16) == TileState.Floor, 0.2f, "first dig to complete at 2.5s");

        StepUntil(() => digger.IsDigging, 5f, "imp to start on the second designation");
        Assert.AreEqual(new Vector2Int(27, 15), digger.CurrentTarget);
        StepUntil(() => board.GetTile(27, 15) == TileState.Floor, 2.6f, "second dig to complete");

        // Nothing left: the digger idles and stays idle.
        for (int i = 0; i < 50; i++) Step();
        Assert.IsTrue(IsIdle());
        Assert.IsFalse(digger.IsDigging);
    }

    [Test]
    public void DigCycle_StandPointIsCurrentTile_DigsWithoutMoving()
    {
        Designate(24, 17); // inside the cavern, directly above the imp

        Step();
        Assert.AreEqual(new Vector2Int(24, 17), digger.CurrentTarget);
        StepUntil(() => board.GetTile(24, 17) == TileState.Floor, 2.7f, "dig in place to complete");
        Assert.AreEqual(new Vector2Int(24, 16), imp.CurrentTile);
    }

    [Test]
    public void DigCycle_EnclosedDesignation_DoesNotStall()
    {
        Designate(5, 5);
        Designate(27, 16);

        StepUntil(() => board.GetTile(27, 16) == TileState.Floor, 6f, "reachable tile to be dug");
        for (int i = 0; i < 50; i++) Step();

        Assert.IsTrue(IsIdle());
        Assert.AreEqual(TileState.Designated, board.GetTile(5, 5), "Unreachable designation stays on the task list");
    }

    [Test]
    public void MidDigClear_AbandonsWithoutConverting_AndRetargets()
    {
        Designate(27, 16);
        Designate(27, 14);

        StepUntil(() => digger.IsDigging, 5f, "dig to start");
        Assert.AreEqual(new Vector2Int(27, 16), digger.CurrentTarget);
        for (int i = 0; i < 50; i++) Step(); // 1s into the dig

        board.SetTile(27, 16, TileState.Rock); // what DigDesignator's Clear does
        Step();
        Assert.AreNotEqual(new Vector2Int(27, 16), digger.CurrentTarget);

        StepUntil(() => board.GetTile(27, 14) == TileState.Floor, 6f, "imp to retarget and dig the other designation");
        Assert.AreEqual(TileState.Rock, board.GetTile(27, 16), "Abandoned tile is never converted");
    }

    [Test]
    public void MidDigClear_NoOtherWork_Idles()
    {
        Designate(27, 16);

        StepUntil(() => digger.IsDigging, 5f, "dig to start");
        for (int i = 0; i < 50; i++) Step();
        board.SetTile(27, 16, TileState.Rock);

        for (int i = 0; i < 200; i++) Step(); // well past when the dig would have finished
        Assert.IsTrue(IsIdle());
        Assert.AreEqual(TileState.Rock, board.GetTile(27, 16));
        Assert.AreEqual(0f, digger.DigElapsed);
    }

    [Test]
    public void DesignationAddedWhileDigging_IsPickedUp()
    {
        Designate(27, 16);

        StepUntil(() => digger.IsDigging, 5f, "dig to start");
        Designate(27, 14);

        StepUntil(() => board.GetTile(27, 14) == TileState.Floor, 10f, "added designation to be dug");
        Assert.AreEqual(TileState.Floor, board.GetTile(27, 16));
    }

    [Test]
    public void IdleImp_PicksUpNewDesignation()
    {
        for (int i = 0; i < 50; i++) Step();
        Assert.IsTrue(IsIdle());

        Designate(27, 16);
        StepUntil(() => board.GetTile(27, 16) == TileState.Floor, 6f, "idle imp to notice and dig the new designation");
    }
}
