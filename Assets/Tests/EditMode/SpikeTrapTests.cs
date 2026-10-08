using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

public class SpikeTrapTests
{
    private static readonly Vector2Int ImpTile = new Vector2Int(24, 16); // Imp spawn: cavern center
    private const float Dt = 0.02f;
    private const float RearmSeconds = 2f; // Shortened rearm channel for fast tests

    private GameObject managerGo;
    private GameObject impGo;
    private DungeonBoard board;
    private BoardRenderer boardRenderer;
    private PlacementManager manager;
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
        manager = managerGo.AddComponent<PlacementManager>();
        manager.Board = board;
        manager.Renderer = boardRenderer;
        manager.SpikeDamage = 3; // set before placing: traps take the manager's config

        impGo = new GameObject("TestImp");
        imp = impGo.AddComponent<Imp>();
        imp.Board = board;
        imp.Renderer = boardRenderer;
        imp.MoveSpeed = 4f;
        imp.Spawn();

        digger = impGo.AddComponent<ImpDigger>();
        digger.Board = board;
        digger.Imp = imp;
        digger.PlacementManager = manager;
        digger.DigSecondsPerTile = 1f;
        digger.RearmSecondsPerTrap = RearmSeconds;
    }

    [TearDown]
    public void TearDown()
    {
        if (impGo != null) Object.DestroyImmediate(impGo);
        if (managerGo != null) Object.DestroyImmediate(managerGo);
    }

    private SpikeTrap PlaceTrap(int x, int y)
    {
        Assert.IsTrue(manager.TryPlaceBuilt(PlaceableType.SpikeTrap, new Vector2Int(x, y), ImpTile, out Placeable p));
        var trap = p.gameObject.GetComponent<SpikeTrap>();
        Assert.IsNotNull(trap);
        return trap;
    }

    private SpikeTrap PlaceSpentTrap(int x, int y)
    {
        SpikeTrap trap = PlaceTrap(x, y);
        Assert.IsTrue(trap.TryTrigger(out _));
        return trap;
    }

    private void Designate(int x, int y) => board.SetTile(x, y, TileState.Designated);

    /// <summary>Walls off all four neighbors of a tile so nothing can stand next to it.</summary>
    private void Enclose(int x, int y)
    {
        board.SetTile(x + 1, y, TileState.Rock);
        board.SetTile(x - 1, y, TileState.Rock);
        board.SetTile(x, y + 1, TileState.Rock);
        board.SetTile(x, y - 1, TileState.Rock);
    }

    private void Step()
    {
        imp.Advance(Dt);
        digger.Tick(Dt);
    }

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

    private bool IsIdle() => digger.CurrentTarget == null && digger.CurrentTrap == null && !imp.IsMoving;

    private static int Steps(Vector2Int a, Vector2Int b) => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);

    // --- Placement wiring ---

    [Test]
    public void PlacedTrap_StartsArmed_WithManagerDamage_SpikesVisible()
    {
        SpikeTrap trap = PlaceTrap(22, 14);

        Assert.IsTrue(trap.IsArmed);
        Assert.AreEqual(3, trap.Damage);
        Assert.AreEqual(new Vector2Int(22, 14), trap.Tile);
        Assert.IsTrue(trap.SpikesVisible);
    }

    [Test]
    public void ManagerDamageConfig_FlowsToEachNewTrap()
    {
        SpikeTrap first = PlaceTrap(22, 14);
        manager.SpikeDamage = 7;
        SpikeTrap second = PlaceTrap(23, 14);

        Assert.AreEqual(3, first.Damage, "Already-placed traps keep their damage");
        Assert.AreEqual(7, second.Damage);
        Assert.IsTrue(second.TryTrigger(out int dealt));
        Assert.AreEqual(7, dealt);
    }

    [Test]
    public void CotAndPlot_HaveNoSpikeTrapComponent()
    {
        manager.TryPlaceBuilt(PlaceableType.LairCot, new Vector2Int(22, 14), ImpTile, out Placeable cot);
        manager.TryPlaceBuilt(PlaceableType.MushroomPlot, new Vector2Int(23, 14), ImpTile, out Placeable plot);

        Assert.IsFalse(cot.TryGetComponent(out SpikeTrap _));
        Assert.IsFalse(plot.TryGetComponent(out SpikeTrap _));
    }

    // --- Trigger / rearm ---

    [Test]
    public void TryTrigger_Armed_DealsDamage_GoesSpent_HidesSpikes()
    {
        SpikeTrap trap = PlaceTrap(22, 14);

        Assert.IsTrue(trap.TryTrigger(out int dealt));
        Assert.AreEqual(3, dealt);
        Assert.IsFalse(trap.IsArmed);
        Assert.IsFalse(trap.SpikesVisible, "Spikes hide in the same call");
    }

    [Test]
    public void TryTrigger_Spent_DoesNotFireAgain()
    {
        SpikeTrap trap = PlaceSpentTrap(22, 14);

        Assert.IsFalse(trap.TryTrigger(out int dealt));
        Assert.AreEqual(0, dealt);
        Assert.IsFalse(trap.IsArmed);
        Assert.IsFalse(trap.SpikesVisible);
    }

    [Test]
    public void Rearm_Spent_ReturnsToArmed_AndCanFireAgain()
    {
        SpikeTrap trap = PlaceSpentTrap(22, 14);

        trap.Rearm();
        Assert.IsTrue(trap.IsArmed);
        Assert.IsTrue(trap.SpikesVisible);

        Assert.IsTrue(trap.TryTrigger(out int dealt));
        Assert.AreEqual(3, dealt);
    }

    [Test]
    public void Rearm_AlreadyArmed_IsHarmlessNoOp()
    {
        SpikeTrap trap = PlaceTrap(22, 14);
        int childrenBefore = trap.transform.childCount; // The plate model and the spike grid

        trap.Rearm();
        Assert.IsTrue(trap.IsArmed);
        Assert.IsTrue(trap.SpikesVisible);
        Assert.AreEqual(childrenBefore, trap.transform.childCount, "Rearming doesn't add spikes");
    }

    // --- GetSpikeTraps ---

    [Test]
    public void GetSpikeTraps_ScanOrder_ExcludesOtherTypes()
    {
        // Placed out of scan order, with other types mixed in.
        SpikeTrap c = PlaceTrap(25, 13);
        SpikeTrap b = PlaceTrap(22, 17);
        manager.TryPlaceBuilt(PlaceableType.LairCot, new Vector2Int(21, 13), ImpTile, out _);
        SpikeTrap a = PlaceTrap(22, 14);
        manager.TryPlaceBuilt(PlaceableType.MushroomPlot, new Vector2Int(23, 13), ImpTile, out _);
        a.TryTrigger(out _); // Spent traps are included too

        List<SpikeTrap> traps = manager.GetSpikeTraps();
        CollectionAssert.AreEqual(new[] { a, b, c }, traps);
    }

    // --- TrySelectWork ---

    [Test]
    public void TrySelectWork_SpentTrapAlone_SelectedWithAdjacentStandPoint()
    {
        SpikeTrap trap = PlaceSpentTrap(22, 14);

        Assert.IsTrue(ImpDigger.TrySelectWork(board, manager.GetSpikeTraps(), ImpTile,
            out Vector2Int target, out Vector2Int stand, out SpikeTrap selected));
        Assert.AreSame(trap, selected);
        Assert.AreEqual(trap.Tile, target);
        var expected = Pathfinder.FindPathToNeighbor(board, ImpTile, trap.Tile);
        Assert.AreEqual(expected[expected.Count - 1], stand);
        Assert.AreEqual(1, Steps(stand, trap.Tile), "Stand-point is next to the trap, never on it");
        Assert.IsTrue(board.IsWalkable(stand.x, stand.y));
    }

    [Test]
    public void TrySelectWork_ArmedTrap_NotSelected()
    {
        PlaceTrap(22, 14);

        Assert.IsFalse(ImpDigger.TrySelectWork(board, manager.GetSpikeTraps(), ImpTile, out _, out _, out SpikeTrap selected));
        Assert.IsNull(selected);
    }

    [Test]
    public void TrySelectWork_DigOnly_MatchesTrySelectTarget()
    {
        // Same layout as ImpDiggerTests' path-length test: (27,14) wins over the straight-line-nearer (24,19).
        for (int x = 22; x <= 26; x++) board.SetTile(x, 17, TileState.Rock);
        Designate(24, 19);
        Designate(27, 14);

        Assert.IsTrue(ImpDigger.TrySelectWork(board, new List<SpikeTrap>(), ImpTile,
            out Vector2Int target, out Vector2Int stand, out SpikeTrap trap));
        Assert.IsTrue(ImpDigger.TrySelectTarget(board, ImpTile, out Vector2Int digTarget, out Vector2Int digStand));
        Assert.IsNull(trap);
        Assert.AreEqual(new Vector2Int(27, 14), target);
        Assert.AreEqual(digTarget, target);
        Assert.AreEqual(digStand, stand);
    }

    [Test]
    public void TrySelectWork_TrapStrictlyNearer_TrapWins()
    {
        SpikeTrap trap = PlaceSpentTrap(22, 16); // stand-point (23,16): 1 step
        Designate(27, 14);                      // stand-point (26,14): 4 steps

        Assert.IsTrue(ImpDigger.TrySelectWork(board, manager.GetSpikeTraps(), ImpTile,
            out Vector2Int target, out _, out SpikeTrap selected));
        Assert.AreSame(trap, selected);
        Assert.AreEqual(new Vector2Int(22, 16), target);
    }

    [Test]
    public void TrySelectWork_DigStrictlyNearer_DigWins()
    {
        PlaceSpentTrap(21, 13); // nearest stand-point (22,13) or (21,14): 5 steps
        Designate(24, 17);      // directly above the imp: 0 steps

        Assert.IsTrue(ImpDigger.TrySelectWork(board, manager.GetSpikeTraps(), ImpTile,
            out Vector2Int target, out _, out SpikeTrap selected));
        Assert.IsNull(selected);
        Assert.AreEqual(new Vector2Int(24, 17), target);
    }

    [Test]
    public void TrySelectWork_ExactTie_DigWins()
    {
        // Both stand-points are 2 steps from (24,16): (24,14) for the trap, (26,16) for the dig.
        // The trap comes first in board scan order (x=24 < 27), so the dig wins on kind, not scan position.
        PlaceSpentTrap(24, 13);
        Designate(27, 16);
        Assert.AreEqual(
            Pathfinder.FindPathToNeighbor(board, ImpTile, new Vector2Int(24, 13)).Count,
            Pathfinder.FindPathToAdjacent(board, ImpTile, new Vector2Int(27, 16)).Count,
            "Precondition: equal path lengths");

        Assert.IsTrue(ImpDigger.TrySelectWork(board, manager.GetSpikeTraps(), ImpTile,
            out Vector2Int target, out _, out SpikeTrap selected));
        Assert.IsNull(selected);
        Assert.AreEqual(new Vector2Int(27, 16), target);
    }

    [Test]
    public void TrySelectWork_UnreachableSpentTrap_SkippedForReachableDig()
    {
        SpikeTrap trap = PlaceSpentTrap(22, 14);
        Enclose(22, 14);
        Designate(27, 16);

        Assert.IsTrue(ImpDigger.TrySelectWork(board, manager.GetSpikeTraps(), ImpTile,
            out Vector2Int target, out _, out SpikeTrap selected));
        Assert.IsNull(selected);
        Assert.AreEqual(new Vector2Int(27, 16), target);
        Assert.IsFalse(trap.IsArmed, "Unreachable trap stays Spent");
    }

    [Test]
    public void TrySelectWork_UnreachableDig_SkippedForReachableSpentTrap()
    {
        Designate(10, 5); // Enclosed in Rock; earlier in scan order than the trap
        SpikeTrap trap = PlaceSpentTrap(22, 14);

        Assert.IsTrue(ImpDigger.TrySelectWork(board, manager.GetSpikeTraps(), ImpTile,
            out Vector2Int target, out _, out SpikeTrap selected));
        Assert.AreSame(trap, selected);
        Assert.AreEqual(trap.Tile, target);
        Assert.AreEqual(TileState.Designated, board.GetTile(10, 5));
    }

    [Test]
    public void TrySelectWork_OnlyUnreachableSpentTrap_ReturnsFalse()
    {
        SpikeTrap trap = PlaceSpentTrap(22, 14);
        Enclose(22, 14);

        Assert.IsFalse(ImpDigger.TrySelectWork(board, manager.GetSpikeTraps(), ImpTile, out _, out _, out SpikeTrap selected));
        Assert.IsNull(selected);
        Assert.IsFalse(trap.IsArmed);
    }

    // --- Rearm cycle (stepped) ---

    [Test]
    public void RearmCycle_ArmedAgainOnlyAfterTravelPlusFullRearmTime()
    {
        SpikeTrap trap = PlaceTrap(22, 14);
        for (int i = 0; i < 50; i++) Step();
        Assert.IsTrue(IsIdle(), "An Armed trap isn't work");

        Assert.IsTrue(trap.TryTrigger(out _)); // Idle imp must notice via its recheck poll
        StepUntil(() => digger.IsRearming, 5f, "imp to arrive beside the trap and start rearming");
        Assert.AreSame(trap, digger.CurrentTrap);
        Assert.IsNull(digger.CurrentTarget, "One job at a time");
        Assert.AreEqual(digger.StandPoint, imp.CurrentTile);
        Assert.AreEqual(1, Steps(imp.CurrentTile, trap.Tile), "Rearms from beside the trap");

        for (int i = 0; i < 95; i++) Step(); // 1.9s of a 2s channel
        Assert.IsFalse(trap.IsArmed);
        Assert.IsTrue(digger.IsRearming);
        Assert.Less(digger.RearmProgress, 1f);
        Assert.IsFalse(trap.SpikesVisible);

        StepUntil(() => trap.IsArmed, 0.2f, "rearm to complete at 2s");
        Assert.IsTrue(trap.SpikesVisible);
        Assert.IsNull(digger.CurrentTrap);
        Assert.IsFalse(digger.IsRearming);
        Assert.AreEqual(0f, digger.RearmElapsed);

        for (int i = 0; i < 50; i++) Step();
        Assert.IsTrue(IsIdle(), "Nothing left: idles");
    }

    [Test]
    public void RearmCycle_TrapArmedMidChannel_JobDropped()
    {
        SpikeTrap trap = PlaceSpentTrap(22, 14);
        StepUntil(() => digger.IsRearming, 5f, "rearm to start");
        for (int i = 0; i < 25; i++) Step();

        trap.Rearm(); // armed by some other means
        Step();
        Assert.IsNull(digger.CurrentTrap);
        Assert.IsFalse(digger.IsRearming);
        Assert.IsTrue(trap.IsArmed);
    }

    [Test]
    public void RearmCycle_MixedWork_NearerTrapFirst_ThenDig()
    {
        SpikeTrap trap = PlaceSpentTrap(22, 16); // 1 step
        Designate(27, 14);                      // 4 steps

        Step();
        Assert.AreSame(trap, digger.CurrentTrap);
        StepUntil(() => trap.IsArmed, 5f, "trap to be rearmed first");
        Assert.AreEqual(TileState.Designated, board.GetTile(27, 14));
        StepUntil(() => board.GetTile(27, 14) == TileState.Floor, 6f, "imp to move on to the dig");
    }

    [Test]
    public void UnreachableSpentTrap_DoesNotStallDigging()
    {
        SpikeTrap trap = PlaceSpentTrap(22, 14);
        Enclose(22, 14);
        Designate(27, 16);

        StepUntil(() => board.GetTile(27, 16) == TileState.Floor, 6f, "reachable designation to be dug");
        for (int i = 0; i < 50; i++) Step();

        Assert.IsTrue(IsIdle());
        Assert.IsFalse(trap.IsArmed, "Unreachable trap stays Spent and isn't acted on");
    }

    [Test]
    public void ImpWalkingOverTrap_DoesNotTriggerIt()
    {
        SpikeTrap trap = PlaceTrap(25, 16); // On the imp's path east to (26,16)
        Designate(27, 16);

        StepUntil(() => board.GetTile(27, 16) == TileState.Floor, 6f, "dig past the trap");
        Assert.IsTrue(trap.IsArmed);
    }
}
