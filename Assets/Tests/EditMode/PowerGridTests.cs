using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// #142: the power grid. Grid math over plain data (components, connection by adjacency, supply/demand, the equality
/// edge), conduit designation (eligibility == apply, sight gating, the wire time, unreachable runs stay pending), the imp's
/// Operate job (bottom priority, preemptible, wheel supply follows tending), the powered trap rearm and its ownership rules,
/// the placed-building card's lines and the unpowered marker rule.
/// </summary>
public class PowerGridTests
{
    private static readonly Vector2Int ImpTile = new Vector2Int(24, 16); // Imp spawn: cavern centre; cavern (21,13)-(26,18)
    private static readonly Vector2Int WheelTile = new Vector2Int(22, 14);
    private static readonly Vector2Int LinkTile = new Vector2Int(23, 14); // Conduit between the wheel and the trap
    private static readonly Vector2Int TrapTile = new Vector2Int(24, 14);
    private const float Dt = 0.02f;

    private GameObject managerGo;
    private GameObject impGo;
    private DungeonBoard board;
    private BoardRenderer boardRenderer;
    private PlacementManager manager;
    private PowerGrid grid;
    private Imp imp;
    private ImpDigger digger;

    [SetUp]
    public void SetUp()
    {
        managerGo = new GameObject("TestDungeonManager");
        board = managerGo.AddComponent<DungeonBoard>();
        board.InitializeBoard();
        boardRenderer = managerGo.AddComponent<BoardRenderer>();
        boardRenderer.Board = board;
        grid = managerGo.AddComponent<PowerGrid>();
        grid.Board = board;
        manager = managerGo.AddComponent<PlacementManager>();
        manager.Board = board;
        manager.Renderer = boardRenderer;
        manager.PowerGrid = grid;

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
        digger.PowerGrid = grid;
        digger.DigSecondsPerTile = 1f;
        digger.RearmSecondsPerTrap = 8f;
    }

    [TearDown]
    public void TearDown()
    {
        if (impGo != null) Object.DestroyImmediate(impGo);
        if (managerGo != null) Object.DestroyImmediate(managerGo);
    }

    // ---- helpers ----

    private void UseUntendedProducer() => manager.Power = new List<PlaceablePower>
    {
        new PlaceablePower(PlaceableType.ImpWheel, PowerRole.Producer, 40), // Produces whenever built: isolates the trap rules
        new PlaceablePower(PlaceableType.SpikeTrap, PowerRole.Consumer, 10, poweredRearmSeconds: 4f),
    };

    private PowerNode PlaceBuiltNode(PlaceableType type, Vector2Int tile)
    {
        Assert.IsTrue(manager.TryPlaceBuilt(type, tile, ImpTile, out Placeable p), $"{type} at {tile}");
        return p.GetComponent<PowerNode>();
    }

    private void BuildConduit(params Vector2Int[] tiles)
    {
        foreach (Vector2Int tile in tiles)
        {
            Assert.AreEqual(1, grid.ApplyRect(null, new RectInt(tile.x, tile.y, 1, 1), PowerGrid.ConduitMode.Lay), $"Lay {tile}");
            Assert.IsTrue(grid.CompleteConduit(tile));
        }
    }

    private SpikeTrap SpentPoweredTrap()
    {
        UseUntendedProducer();
        PlaceBuiltNode(PlaceableType.ImpWheel, WheelTile);
        SpikeTrap trap = PlaceBuiltNode(PlaceableType.SpikeTrap, TrapTile).GetComponent<SpikeTrap>();
        BuildConduit(LinkTile);
        Assert.IsTrue(trap.TryTrigger(out _));
        Assert.IsTrue(trap.IsPowered, "Wheel 40 W covers the trap's 10 W");
        return trap;
    }

    private void Step(float dt = Dt)
    {
        imp.Advance(dt);
        digger.Tick(dt);
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

    private static Dictionary<Vector2Int, int> Comps(params Vector2Int[] tiles) => PowerGrid.Components(tiles);

    private static Vector2Int T(int x, int y) => new Vector2Int(x, y);

    // ---- 1. Grid math ----

    [Test]
    public void Components_SeparateRunsAreSeparateGrids_ABridgeMergesThem_ARemovalSplitsThem()
    {
        var left = new[] { T(1, 1), T(2, 1) };
        var right = new[] { T(4, 1), T(5, 1) };
        var tiles = new List<Vector2Int>(left);
        tiles.AddRange(right);

        Dictionary<Vector2Int, int> split = PowerGrid.Components(tiles);
        Assert.AreEqual(0, split[T(1, 1)], "Ids follow scan order of each component's first tile");
        Assert.AreEqual(0, split[T(2, 1)]);
        Assert.AreEqual(1, split[T(4, 1)]);
        Assert.AreEqual(1, split[T(5, 1)]);

        tiles.Add(T(3, 1));
        Dictionary<Vector2Int, int> merged = PowerGrid.Components(tiles);
        Assert.AreEqual(merged[T(1, 1)], merged[T(5, 1)], "One run once bridged");

        Dictionary<Vector2Int, int> diagonal = Comps(T(1, 1), T(2, 2));
        Assert.AreNotEqual(diagonal[T(1, 1)], diagonal[T(2, 2)], "4-adjacency: diagonals don't join");
    }

    [Test]
    public void GridAt_OwnTileOrA4Neighbor_ConnectsANode_ADiagonalDoesNot()
    {
        Dictionary<Vector2Int, int> comps = Comps(T(5, 5), T(6, 5), T(9, 9));
        Assert.AreEqual(0, PowerGrid.GridAt(comps, T(5, 5)), "On conduit");
        Assert.AreEqual(0, PowerGrid.GridAt(comps, T(5, 6)), "Beside it (up)");
        Assert.AreEqual(0, PowerGrid.GridAt(comps, T(7, 5)), "Beside it (right)");
        Assert.AreEqual(-1, PowerGrid.GridAt(comps, T(7, 6)), "Diagonal only");
        Assert.AreEqual(1, PowerGrid.GridAt(comps, T(9, 10)));
        Assert.AreEqual(-1, PowerGrid.GridAt(comps, T(20, 20)));
    }

    [Test]
    public void Sum_CountsActiveProducersAndAllConsumers_OnThatGridOnly()
    {
        Dictionary<Vector2Int, int> comps = Comps(T(5, 5), T(6, 5), T(20, 20));
        var nodes = new[]
        {
            new PowerGrid.NodeState(T(4, 5), PowerRole.Producer, 40, true),
            new PowerGrid.NodeState(T(7, 5), PowerRole.Producer, 25, false), // Untended: no supply
            new PowerGrid.NodeState(T(5, 6), PowerRole.Consumer, 10, true),
            new PowerGrid.NodeState(T(6, 4), PowerRole.Consumer, 15, true),
            new PowerGrid.NodeState(T(20, 21), PowerRole.Consumer, 99, true), // Another grid
            new PowerGrid.NodeState(T(30, 30), PowerRole.Producer, 99, true), // No grid
        };
        PowerGrid.Sum(nodes, comps, 0, out int supply, out int demand);
        Assert.AreEqual(40, supply);
        Assert.AreEqual(25, demand);

        PowerGrid.Sum(nodes, comps, -1, out supply, out demand);
        Assert.AreEqual(0, supply + demand, "No grid, no totals");
    }

    [Test]
    public void IsPowered_EqualityIsPowered_ShortIsNot_UnconnectedNever()
    {
        Assert.IsTrue(PowerGrid.IsPowered(true, 40, 40), "Equality is powered");
        Assert.IsTrue(PowerGrid.IsPowered(true, 40, 10));
        Assert.IsFalse(PowerGrid.IsPowered(true, 39, 40));
        Assert.IsFalse(PowerGrid.IsPowered(false, 100, 0), "Unconnected is never powered");
        Assert.IsTrue(PowerGrid.IsPowered(true, 0, 0), "An empty grid covers nothing-demanded");
    }

    [Test]
    public void LiveGrid_EqualityEdge_TwoTrapsOn20W_ThirdTrapOverdraws()
    {
        manager.Power = new List<PlaceablePower>
        {
            new PlaceablePower(PlaceableType.ImpWheel, PowerRole.Producer, 20),
            new PlaceablePower(PlaceableType.SpikeTrap, PowerRole.Consumer, 10, poweredRearmSeconds: 4f),
        };
        PowerNode wheel = PlaceBuiltNode(PlaceableType.ImpWheel, T(21, 14));
        PowerNode a = PlaceBuiltNode(PlaceableType.SpikeTrap, T(23, 14));
        PowerNode b = PlaceBuiltNode(PlaceableType.SpikeTrap, T(23, 16));
        BuildConduit(T(22, 14), T(22, 15), T(22, 16));

        Assert.IsTrue(a.IsPowered && b.IsPowered, "20 supplied, 20 demanded: powered");
        Assert.AreEqual(20, wheel.Totals.Supply);
        Assert.AreEqual(20, wheel.Totals.Demand);

        PowerNode c = PlaceBuiltNode(PlaceableType.SpikeTrap, T(23, 15));
        Assert.AreEqual(30, c.Totals.Demand);
        Assert.IsFalse(a.IsPowered || b.IsPowered || c.IsPowered, "Over-drawn: every consumer on the grid is unpowered");
    }

    [Test]
    public void LiveGrid_RemovingTheLink_Unconnects_RestoringReconnects()
    {
        SpikeTrap trap = SpentPoweredTrap();
        PowerNode node = trap.GetComponent<PowerNode>();

        Assert.IsTrue(grid.RemoveConduit(LinkTile));
        Assert.IsFalse(node.IsConnected, "Its only conduit is gone");
        Assert.IsFalse(trap.IsPowered);

        BuildConduit(LinkTile);
        Assert.IsTrue(trap.IsPowered, "Restored");
    }

    [Test]
    public void LiveGrid_CutBetweenWheelAndTrap_LeavesTheTrapConnectedButUnpowered_WithTheMarker()
    {
        UseUntendedProducer();
        PlaceBuiltNode(PlaceableType.ImpWheel, T(21, 14));
        PowerNode trap = PlaceBuiltNode(PlaceableType.SpikeTrap, T(25, 14));
        BuildConduit(T(22, 14), T(23, 14), T(24, 14));
        Assert.IsTrue(trap.IsPowered);
        Assert.IsFalse(trap.ShowsMarker);

        grid.RemoveConduit(T(23, 14)); // Two pieces: the wheel's (22) and the trap's (24)

        Assert.IsTrue(trap.IsConnected, "Still on conduit");
        Assert.AreEqual(0, trap.Totals.Supply, "...but the producer is on the other piece");
        Assert.IsFalse(trap.IsPowered);
        Assert.IsTrue(trap.ShowsMarker, "Connected consumer, unpowered: marker");
        trap.RefreshMarker();
        Assert.IsTrue(trap.Marker.activeSelf);

        BuildConduit(T(23, 14));
        trap.RefreshMarker();
        Assert.IsFalse(trap.Marker.activeSelf, "Repowered: marker gone");
    }

    // ---- 2. Conduit designation ----

    [Test]
    public void Lay_TakesSeenFloorOnly_OnceEach()
    {
        var fog = managerGo.AddComponent<FogOfWar>();
        fog.Board = board;
        fog.RevealAround(ImpTile); // x 21-27, y 13-19
        board.SetTile(24, 19, TileState.Floor); // Seen floor outside the cavern
        board.SetTile(30, 16, TileState.Floor); // Unseen floor

        Assert.IsTrue(grid.IsEligible(fog, T(22, 14), PowerGrid.ConduitMode.Lay), "Seen floor");
        Assert.IsTrue(grid.IsEligible(fog, T(24, 19), PowerGrid.ConduitMode.Lay));
        Assert.IsFalse(grid.IsEligible(fog, T(27, 15), PowerGrid.ConduitMode.Lay), "Rock");
        Assert.IsFalse(grid.IsEligible(fog, T(30, 16), PowerGrid.ConduitMode.Lay), "Floor under fog");
        Assert.IsFalse(grid.IsEligible(fog, T(24, 31), PowerGrid.ConduitMode.Lay), "The entrance isn't Floor");
        Assert.IsTrue(grid.IsEligible(null, T(30, 16), PowerGrid.ConduitMode.Lay), "No fog: all floor counts as seen");

        Assert.AreEqual(1, grid.ApplyRect(fog, new RectInt(22, 14, 1, 1), PowerGrid.ConduitMode.Lay));
        Assert.IsTrue(grid.IsPending(T(22, 14)));
        Assert.AreEqual(0, grid.ApplyRect(fog, new RectInt(22, 14, 1, 1), PowerGrid.ConduitMode.Lay), "Already conduit: deduped");
        Assert.AreEqual(1, grid.PendingCount);
    }

    [Test]
    public void EligibleTiles_IsExactlyWhatApplyChanges_InBothModes()
    {
        var fog = managerGo.AddComponent<FogOfWar>();
        fog.Board = board;
        fog.RevealAround(ImpTile);
        BuildConduit(T(22, 15));
        grid.ApplyRect(null, new RectInt(23, 15, 1, 1), PowerGrid.ConduitMode.Lay);

        RectInt[] rects = { new RectInt(19, 12, 10, 8), new RectInt(21, 15, 4, 1), new RectInt(0, 0, 3, 3) };
        foreach (PowerGrid.ConduitMode mode in new[] { PowerGrid.ConduitMode.Lay, PowerGrid.ConduitMode.Remove })
        {
            foreach (RectInt rect in rects)
            {
                var expected = new List<Vector2Int>();
                grid.EligibleTiles(fog, rect, mode, expected);

                int changed = grid.ApplyRect(fog, rect, mode);

                Assert.AreEqual(expected.Count, changed, $"{mode} {rect}");
                foreach (Vector2Int t in expected)
                    Assert.AreEqual(mode == PowerGrid.ConduitMode.Lay, grid.HasConduit(t), $"{mode} {t}");
                if (mode == PowerGrid.ConduitMode.Lay) grid.ApplyRect(fog, rect, PowerGrid.ConduitMode.Remove); // Reset
            }
        }
    }

    [Test]
    public void Remove_ClearsPendingAndBuiltInstantly_AndDirtiesTheGrid()
    {
        SpikeTrap trap = SpentPoweredTrap();
        grid.ApplyRect(null, new RectInt(25, 14, 1, 1), PowerGrid.ConduitMode.Lay);
        Assert.IsTrue(grid.IsPending(T(25, 14)));

        Assert.AreEqual(2, grid.ApplyRect(null, new RectInt(23, 14, 3, 1), PowerGrid.ConduitMode.Remove), "Pending and built, no refund");
        Assert.IsFalse(grid.HasConduit(T(25, 14)));
        Assert.IsFalse(grid.HasConduit(LinkTile));
        Assert.IsFalse(trap.IsPowered, "The topology recomputed: no grid");
    }

    [Test]
    public void WireProgress_FlipsPendingToBuilt_AtTheSerializedTime_AndKeepsPartialProgress()
    {
        Assert.AreEqual(0.5f, grid.WireSecondsPerTile, 1e-6f, "First-pass tunable");
        grid.ApplyRect(null, new RectInt(22, 14, 1, 1), PowerGrid.ConduitMode.Lay);

        Assert.IsFalse(grid.AddWireProgress(T(22, 14), 0.3f));
        Assert.AreEqual(0.6f, grid.ProgressOf(T(22, 14)), 1e-4f, "Kept on the tile");
        Assert.IsTrue(grid.IsPending(T(22, 14)));
        Assert.IsTrue(grid.AddWireProgress(T(22, 14), 0.2f), "0.5 s total: built");
        Assert.IsTrue(grid.IsBuilt(T(22, 14)));
        Assert.IsFalse(grid.AddWireProgress(T(22, 14), 1f), "Built tiles take no more work");
    }

    [Test]
    public void Imp_WiresPendingConduit_BesideIt_OverTheWireTime()
    {
        grid.ApplyRect(null, new RectInt(22, 16, 2, 1), PowerGrid.ConduitMode.Lay);

        StepUntil(() => grid.PendingCount == 0, 10f, "both tiles wired");
        Assert.IsTrue(grid.IsBuilt(T(22, 16)) && grid.IsBuilt(T(23, 16)));
        Assert.IsNull(digger.CurrentConduit);
        Assert.IsFalse(digger.IsWiring);
    }

    [Test]
    public void UnreachablePendingConduit_StaysPending_WhileTheImpWorksElsewhere()
    {
        // A floor pocket walled off from the cavern: conduit ordered there (no fog) can't be reached.
        board.SetTile(10, 10, TileState.Floor);
        grid.ApplyRect(null, new RectInt(10, 10, 1, 1), PowerGrid.ConduitMode.Lay);
        board.SetTile(27, 16, TileState.Designated);

        StepUntil(() => board.GetTile(27, 16) == TileState.Floor, 10f, "the reachable dig");
        for (int i = 0; i < 100; i++) Step();

        Assert.IsTrue(grid.IsPending(T(10, 10)), "Still a pending task, skipped");
        Assert.IsNull(digger.CurrentConduit);
    }

    [Test]
    public void Selection_ConduitIsConstruction_TiesBreakDigThenConstructionThenRearm()
    {
        // Equal path costs: a conduit tile and a spent trap mirrored about the imp, and a dig the same distance away.
        grid.ApplyRect(null, new RectInt(22, 16, 1, 1), PowerGrid.ConduitMode.Lay);
        Assert.IsTrue(manager.TryPlaceBuilt(PlaceableType.SpikeTrap, T(26, 16), ImpTile, out Placeable trapPlaceable));
        SpikeTrap trap = trapPlaceable.GetComponent<SpikeTrap>();
        trap.TryTrigger(out _);
        int conduitCost = Pathfinder.FindPathToNeighbor(board, ImpTile, T(22, 16)).Count;
        Assert.AreEqual(conduitCost, Pathfinder.FindPathToNeighbor(board, ImpTile, T(26, 16)).Count, "Staged as a tie");

        Assert.IsTrue(ImpDigger.TrySelectWork(board, null, grid.PendingTiles(), manager.GetBuiltSpikeTraps(), ImpTile,
            out Vector2Int target, out _, out _, out bool conduit, out SpikeTrap pickedTrap));
        Assert.IsTrue(conduit, "Construction beats rearm on a tie");
        Assert.AreEqual(T(22, 16), target);
        Assert.IsNull(pickedTrap);

        board.SetTile(24, 18, TileState.Designated); // Dig stand-point (24,17): the same cost as the conduit's
        Assert.AreEqual(conduitCost, Pathfinder.FindPathToAdjacent(board, ImpTile, T(24, 18)).Count, "Staged as a tie");
        Assert.IsTrue(ImpDigger.TrySelectWork(board, null, grid.PendingTiles(), manager.GetBuiltSpikeTraps(), ImpTile,
            out target, out _, out _, out conduit, out _));
        Assert.IsFalse(conduit, "Dig beats construction on a tie");
        Assert.AreEqual(T(24, 18), target);
    }

    // ---- 3. Operate ----

    [Test]
    public void Operate_OnlyWithNoRealWork_TheImpTendsTheWheel_AndSupplyFollowsTending()
    {
        PowerNode wheel = PlaceBuiltNode(PlaceableType.ImpWheel, WheelTile);
        PowerNode trap = PlaceBuiltNode(PlaceableType.SpikeTrap, TrapTile);
        BuildConduit(LinkTile);
        Assert.AreEqual(0, wheel.Totals.Supply, "Untended: 0 W");
        Assert.IsFalse(trap.IsPowered);

        StepUntil(() => digger.IsOperating, 5f, "the imp to reach the wheel");
        Assert.AreSame(wheel, digger.CurrentWheel);
        Assert.IsTrue(wheel.IsTended);
        Assert.AreEqual(40, wheel.Totals.Supply, "Tended: 40 W");
        Assert.IsTrue(trap.IsPowered);
        Assert.AreEqual(1, Mathf.Abs(imp.CurrentTile.x - WheelTile.x) + Mathf.Abs(imp.CurrentTile.y - WheelTile.y), "Beside the wheel");
    }

    [Test]
    public void Operate_IsPreempted_TheMomentADigAppears_AndTheGridBrownsOut()
    {
        PowerNode wheel = PlaceBuiltNode(PlaceableType.ImpWheel, WheelTile);
        PowerNode trap = PlaceBuiltNode(PlaceableType.SpikeTrap, TrapTile);
        BuildConduit(LinkTile);
        StepUntil(() => digger.IsOperating, 5f, "operating");

        board.SetTile(27, 16, TileState.Designated);
        float waited = StepUntil(() => digger.CurrentWheel == null, 1f, "the imp to leave the wheel");

        Assert.LessOrEqual(waited, digger.IdleRecheckSeconds + Dt, "Within one selection pass");
        Assert.AreEqual(T(27, 16), digger.CurrentTarget, "Off to dig");
        Assert.IsFalse(wheel.IsTended);
        Assert.AreEqual(0, wheel.Totals.Supply, "Output drops to 0");
        Assert.IsFalse(trap.IsPowered, "Brownout");

        StepUntil(() => board.GetTile(27, 16) == TileState.Floor, 10f, "the dig");
        StepUntil(() => digger.IsOperating, 10f, "back to the wheel once the queue is empty");
        Assert.IsTrue(trap.IsPowered, "Repowered");
    }

    [Test]
    public void Operate_IsPreemptedByConstruction_Too()
    {
        PlaceBuiltNode(PlaceableType.ImpWheel, WheelTile);
        StepUntil(() => digger.IsOperating, 5f, "operating");

        grid.ApplyRect(null, new RectInt(25, 17, 1, 1), PowerGrid.ConduitMode.Lay);
        StepUntil(() => digger.CurrentWheel == null, 1f, "the imp to leave for the conduit");
        Assert.AreEqual(T(25, 17), digger.CurrentConduit);
    }

    [Test]
    public void TrySelectOperate_SkipsSitesAndWheelsAnotherImpTends()
    {
        PowerNode wheel = PlaceBuiltNode(PlaceableType.ImpWheel, WheelTile);
        Assert.IsTrue(manager.TryPlace(PlaceableType.ImpWheel, T(26, 17), ImpTile, out _), "A site");

        Assert.IsTrue(ImpDigger.TrySelectOperate(board, manager.GetBuiltWheels(), digger, ImpTile, out PowerNode picked, out _));
        Assert.AreSame(wheel, picked, "The built one; sites aren't wheels yet");

        var otherGo = new GameObject("OtherImp");
        try
        {
            otherGo.AddComponent<Imp>();
            ImpDigger other = otherGo.AddComponent<ImpDigger>();
            wheel.SetOperator(other);
            Assert.IsFalse(ImpDigger.TrySelectOperate(board, manager.GetBuiltWheels(), digger, ImpTile, out _, out _),
                "Claimed by another imp");
        }
        finally
        {
            Object.DestroyImmediate(otherGo);
        }
    }

    // ---- 4. Powered rearm ----

    [Test]
    public void PoweredTrap_RearmsItself_AtThePoweredSeconds()
    {
        SpikeTrap trap = SpentPoweredTrap();
        Assert.AreEqual(4f, trap.PoweredRearmSeconds, 1e-6f, "From the power table");

        for (int i = 0; i < 195; i++) trap.Tick(Dt); // 3.9 s
        Assert.IsFalse(trap.IsArmed);
        Assert.AreEqual(0.975f, trap.PoweredRearmProgress, 1e-3f);
        for (int i = 0; i < 5; i++) trap.Tick(Dt);   // 4.0 s
        Assert.IsTrue(trap.IsArmed, "Re-armed itself in 4 s");
        Assert.AreEqual(0f, trap.PoweredRearmProgress, "Progress resets on rearm");
    }

    [Test]
    public void Brownout_PausesAndKeepsProgress_AndHandsTheTrapToTheImp_PowerBackResumes()
    {
        SpikeTrap trap = SpentPoweredTrap();
        for (int i = 0; i < 100; i++) trap.Tick(Dt); // 2 s: half way
        Assert.AreEqual(0.5f, trap.PoweredRearmProgress, 1e-3f);
        Assert.IsTrue(trap.SelfRearmOwns);
        Assert.IsFalse(ImpDigger.TrySelectWork(board, null, manager.GetBuiltSpikeTraps(), ImpTile, out _, out _, out _, out _),
            "Powered: not an imp candidate");

        grid.RemoveConduit(LinkTile);
        for (int i = 0; i < 250; i++) trap.Tick(Dt);
        Assert.AreEqual(0.5f, trap.PoweredRearmProgress, 1e-3f, "Paused, kept");
        Assert.IsFalse(trap.IsArmed);
        Assert.IsFalse(trap.SelfRearmOwns);
        Assert.IsTrue(ImpDigger.TrySelectWork(board, null, manager.GetBuiltSpikeTraps(), ImpTile, out _, out _, out _,
            out SpikeTrap candidate), "Back in the imp's pool");
        Assert.AreSame(trap, candidate);

        BuildConduit(LinkTile);
        for (int i = 0; i < 99; i++) trap.Tick(Dt);
        Assert.IsFalse(trap.IsArmed);
        trap.Tick(Dt);
        Assert.IsTrue(trap.IsArmed, "Resumed from half: 2 more seconds");
    }

    [Test]
    public void ImpAlreadyRearming_WhenPowerArrives_Finishes_WhileSelfRearmStaysPaused()
    {
        UseUntendedProducer();
        PlaceBuiltNode(PlaceableType.ImpWheel, WheelTile);
        SpikeTrap trap = PlaceBuiltNode(PlaceableType.SpikeTrap, TrapTile).GetComponent<SpikeTrap>();
        trap.TryTrigger(out _);
        Assert.IsFalse(trap.IsPowered, "No conduit yet");

        StepUntil(() => digger.IsRearming, 5f, "the imp to start rearming");
        Assert.IsTrue(trap.ImpRearming);
        BuildConduit(LinkTile); // Power arrives mid-channel
        Assert.IsTrue(trap.IsPowered);

        float channel = digger.RearmElapsed;
        while (!trap.IsArmed)
        {
            Step();
            trap.Tick(Dt);
            Assert.AreEqual(0f, trap.PoweredRearmProgress, "Self-rearm paused while the imp channels");
            channel += Dt;
            Assert.Less(channel, digger.RearmSecondsPerTrap + 0.1f, "The imp finishes on its own clock");
        }
        Assert.AreEqual(digger.RearmSecondsPerTrap, channel, 2 * Dt, "Armed by the imp at 8 s, not by power");
        Assert.IsFalse(trap.ImpRearming);
    }

    [Test]
    public void PowerArrivingWhileTheImpWalksToTheTrap_HandsItToTheSelfRearm()
    {
        UseUntendedProducer();
        PlaceBuiltNode(PlaceableType.ImpWheel, WheelTile);
        SpikeTrap trap = PlaceBuiltNode(PlaceableType.SpikeTrap, TrapTile).GetComponent<SpikeTrap>();
        trap.TryTrigger(out _);
        Step();
        Assert.AreSame(trap, digger.CurrentTrap, "Dispatched");
        Assert.IsFalse(digger.IsRearming, "Still walking");

        BuildConduit(LinkTile);
        StepUntil(() => digger.CurrentTrap == null, 2f, "the imp to drop the trap");
        Assert.IsFalse(digger.IsRearming);
        for (int i = 0; i < 200; i++) trap.Tick(Dt);
        Assert.IsTrue(trap.IsArmed, "Self-rearmed");
    }

    [Test]
    public void UnpoweredTrap_IsUnchanged_TheImpRearmsIt()
    {
        SpikeTrap trap = PlaceBuiltNode(PlaceableType.SpikeTrap, TrapTile).GetComponent<SpikeTrap>();
        trap.TryTrigger(out _);
        for (int i = 0; i < 500; i++) trap.Tick(Dt);
        Assert.IsFalse(trap.IsArmed, "No grid: no self-rearm");
        Assert.AreEqual(0f, trap.PoweredRearmProgress);
        Assert.IsFalse(trap.GetComponent<PowerNode>().ShowsMarker, "Unconnected: no marker (as before #142)");

        StepUntil(() => trap.IsArmed, 15f, "the imp's rearm");
    }

    // ---- 5. Card lines and the marker ----

    [Test]
    public void PowerLines_ForWheelAndTrapStates()
    {
        CollectionAssert.AreEqual(new[] { "Producer · 40 W", "Producing", "Grid: 40 produced · 10 demanded" },
            DetailCard.PowerLines(PowerRole.Producer, 40, true, true, true, true, 40, 10));
        CollectionAssert.AreEqual(new[] { "Producer · 40 W", "Untended", "Grid: 0 produced · 10 demanded" },
            DetailCard.PowerLines(PowerRole.Producer, 40, true, false, true, false, 0, 10));
        CollectionAssert.AreEqual(new[] { "Producer · 40 W", "Not connected" },
            DetailCard.PowerLines(PowerRole.Producer, 40, true, true, false, false, 0, 0));
        // #143 migration: "drawn whenever built" left the consumer line once a node can be switched off.
        CollectionAssert.AreEqual(new[] { "Consumer · 10 W", "Powered", "Grid: 40 produced · 10 demanded" },
            DetailCard.PowerLines(PowerRole.Consumer, 10, false, false, true, true, 40, 10));
        CollectionAssert.AreEqual(new[] { "Consumer · 10 W", "Unpowered — grid over-drawn", "Grid: 0 produced · 10 demanded" },
            DetailCard.PowerLines(PowerRole.Consumer, 10, false, false, true, false, 0, 10));
        CollectionAssert.AreEqual(new[] { "Consumer · 10 W", "Not connected" },
            DetailCard.PowerLines(PowerRole.Consumer, 10, false, false, false, false, 0, 0));
        CollectionAssert.IsEmpty(DetailCard.PowerLines(PowerRole.None, 0, false, false, true, true, 0, 0));
    }

    [Test]
    public void PlacedLines_ReadTheLiveGrid()
    {
        PowerNode wheel = PlaceBuiltNode(PlaceableType.ImpWheel, WheelTile);
        PowerNode trap = PlaceBuiltNode(PlaceableType.SpikeTrap, TrapTile);
        BuildConduit(LinkTile);

        CollectionAssert.AreEqual(new[] { "Producer · 40 W", "Untended", "Grid: 0 produced · 10 demanded" },
            DetailCard.PlacedLines(wheel.GetComponent<Placeable>()));
        // #143 migration: the consumer line is just its draw now.
        CollectionAssert.AreEqual(new[] { "Armed", "Consumer · 10 W", "Unpowered — grid over-drawn", "Grid: 0 produced · 10 demanded" },
            DetailCard.PlacedLines(trap.GetComponent<Placeable>()));

        StepUntil(() => digger.IsOperating, 5f, "tending");
        CollectionAssert.AreEqual(new[] { "Producer · 40 W", "Producing", "Grid: 40 produced · 10 demanded" },
            DetailCard.PlacedLines(wheel.GetComponent<Placeable>()));
        Assert.AreEqual("Powered", DetailCard.PlacedLines(trap.GetComponent<Placeable>())[2]);

        Assert.AreEqual("Spent · rearming itself 45%", DetailCard.TrapLine(false, 0.45f));
        Assert.AreEqual("Spent", DetailCard.TrapLine(false, 0f));
    }

    [Test]
    public void PlacedLines_NonNodes_ShowTheirBasics()
    {
        Assert.IsTrue(manager.TryPlaceBuilt(PlaceableType.LairCot, T(21, 18), ImpTile, out Placeable cot));
        Assert.IsTrue(manager.TryPlaceBuilt(PlaceableType.MushroomPlot, T(22, 18), ImpTile, out Placeable plot));
        CollectionAssert.AreEqual(new[] { "Free" }, DetailCard.PlacedLines(cot));
        Assert.IsTrue(cot.TryClaim());
        CollectionAssert.AreEqual(new[] { "Claimed" }, DetailCard.PlacedLines(cot));
        CollectionAssert.AreEqual(new[] { "Food: 0 / 5" }, DetailCard.PlacedLines(plot));
        Assert.IsNull(cot.GetComponent<PowerNode>(), "No power row, no node");
    }

    [Test]
    public void MarkerRule_BuiltConnectedUnpoweredConsumersOnly()
    {
        Assert.IsTrue(PowerNode.ShowsUnpoweredMarker(true, PowerRole.Consumer, true, false));
        Assert.IsFalse(PowerNode.ShowsUnpoweredMarker(true, PowerRole.Consumer, true, true), "Powered");
        Assert.IsFalse(PowerNode.ShowsUnpoweredMarker(true, PowerRole.Consumer, false, false), "Not on a grid");
        Assert.IsFalse(PowerNode.ShowsUnpoweredMarker(false, PowerRole.Consumer, true, false), "A site");
        Assert.IsFalse(PowerNode.ShowsUnpoweredMarker(true, PowerRole.Producer, true, false), "Producers never");
    }

    [Test]
    public void ASite_IsNotANode_UntilBuilt()
    {
        Assert.IsTrue(manager.TryPlace(PlaceableType.SpikeTrap, TrapTile, ImpTile, out Placeable site));
        PowerNode node = site.GetComponent<PowerNode>();
        Assert.IsNotNull(node, "Attached at placement");
        BuildConduit(LinkTile);
        Assert.AreEqual(0, grid.NodeCount);
        Assert.IsFalse(node.IsConnected);
        Assert.IsFalse(node.ShowsMarker);

        site.CompleteConstruction();
        Assert.AreEqual(1, grid.NodeCount, "Registered on build-complete");
        Assert.IsTrue(node.IsConnected);
    }

    // ---- building over conduit (Oct 18 amendment): conduit is not occupancy ----

    [Test]
    public void Placement_OverBuiltOrPendingConduit_IsValid_ExactlyLikeBareFloor()
    {
        Vector2Int built = T(22, 16), pending = T(23, 16), bare = T(24, 17);
        BuildConduit(built);
        grid.ApplyRect(null, new RectInt(pending.x, pending.y, 1, 1), PowerGrid.ConduitMode.Lay);
        Assert.IsTrue(grid.IsPending(pending));

        foreach (Vector2Int tile in new[] { built, pending })
        {
            Assert.AreEqual(manager.CanPlace(bare, ImpTile), manager.CanPlace(tile, ImpTile), $"{tile} reads as bare Floor");
            Assert.IsTrue(manager.CanPlace(tile, ImpTile), $"{tile}");
            Assert.IsTrue(manager.AnyPlaceable(new RectInt(tile.x, tile.y, 1, 1), ImpTile), $"The preview at {tile} is valid");
        }

        // Every type, the rect path the drag uses: one cot on the built tile, one wheel on the pending one.
        Assert.AreEqual(1, manager.ApplyRect(PlaceableType.LairCot, new RectInt(built.x, built.y, 1, 1), ImpTile));
        Assert.AreEqual(1, manager.ApplyRect(PlaceableType.ImpWheel, new RectInt(pending.x, pending.y, 1, 1), ImpTile));
        foreach (PlaceableType type in new[] { PlaceableType.MushroomPlot, PlaceableType.SpikeTrap })
        {
            Vector2Int tile = type == PlaceableType.MushroomPlot ? T(25, 16) : T(26, 16);
            BuildConduit(tile);
            Assert.IsTrue(manager.TryPlace(type, tile, ImpTile, out _), $"{type} over built conduit");
        }

        Assert.IsTrue(grid.IsBuilt(built), "Placing leaves the conduit as it was");
        Assert.IsTrue(grid.IsPending(pending), "Placing leaves a pending order pending");
    }

    [Test]
    public void ANodeBuiltOverConduit_ConnectsThroughItsOwnTile()
    {
        UseUntendedProducer();
        BuildConduit(WheelTile, LinkTile, TrapTile); // The run passes under both buildings
        PowerNode wheel = PlaceBuiltNode(PlaceableType.ImpWheel, WheelTile);
        PowerNode trap = PlaceBuiltNode(PlaceableType.SpikeTrap, TrapTile);

        Assert.IsTrue(wheel.IsConnected && trap.IsConnected);
        Assert.AreEqual(grid.GridAt(WheelTile), grid.GridAt(TrapTile), "One grid under both");
        Assert.IsTrue(trap.IsPowered);
    }

    [Test]
    public void PendingConduitUnderASite_StaysBuildable_TheImpWiresItFromBeside_AndTheBuildingConnects()
    {
        UseUntendedProducer();
        grid.ApplyRect(null, new RectInt(22, 16, 2, 1), PowerGrid.ConduitMode.Lay);
        Assert.IsTrue(manager.TryPlace(PlaceableType.ImpWheel, T(22, 16), ImpTile, out Placeable wheelSite), "A site on pending conduit");

        StepUntil(() => grid.PendingCount == 0, 10f, "the run, including the tile under the site, wired");
        Assert.IsTrue(grid.IsBuilt(T(22, 16)), "The site never blocked the tile");

        wheelSite.CompleteConstruction();
        Assert.IsTrue(wheelSite.GetComponent<PowerNode>().IsConnected, "Built over the wired run: on its grid");
    }

    // ---- placed selection (Neutral only) ----

    [Test]
    public void PlacedSelection_NeutralClickSelectsBuilt_EmptyClears_ArmingClears()
    {
        var controllerGo = new GameObject("TestController");
        try
        {
            var controller = controllerGo.AddComponent<PlacementController>();
            controller.PlacementManager = manager;
            PowerNode wheel = PlaceBuiltNode(PlaceableType.ImpWheel, WheelTile);
            Assert.IsTrue(manager.TryPlace(PlaceableType.SpikeTrap, TrapTile, ImpTile, out Placeable site));
            int changes = 0;
            controller.ModeChanged += () => changes++;

            controller.ClickBoardTile(WheelTile);
            Assert.AreSame(wheel.GetComponent<Placeable>(), controller.SelectedPlaceable);
            Assert.AreEqual(1, changes);

            controller.ClickBoardTile(TrapTile);
            Assert.IsNull(controller.SelectedPlaceable, "A site isn't selectable: the click clears");
            controller.ClickBoardTile(WheelTile);
            controller.ClickBoardTile(T(25, 17));
            Assert.IsNull(controller.SelectedPlaceable, "Empty floor clears");

            controller.ClickBoardTile(WheelTile);
            controller.SelectWire();
            Assert.IsNull(controller.SelectedPlaceable, "Leaving Neutral clears");
            controller.ClickBoardTile(WheelTile);
            Assert.IsNull(controller.SelectedPlaceable, "Board clicks keep their tool meanings outside Neutral");
            Assert.IsFalse(controller.SelectPlaced(wheel.GetComponent<Placeable>()));
            Assert.AreEqual(PlacementController.ToolMode.Wire, controller.Mode);
            controller.SelectWire();
            Assert.AreEqual(PlacementController.ToolMode.Neutral, controller.Mode, "The verb toggles like a type tile");
        }
        finally
        {
            Object.DestroyImmediate(controllerGo);
        }
    }

    // ---- presentation and data ----

    [Test]
    public void PowerCategory_SitsBetweenDefenseAndMagic_WithTheVerbAndTheWheel()
    {
        CollectionAssert.AreEqual(
            new[] { BuildCategory.Living, BuildCategory.Farming, BuildCategory.Defense, BuildCategory.Power, BuildCategory.Magic },
            BuildPresentation.CategoryOrder);
        CollectionAssert.AreEqual(new[] { BuildVerb.LayConduit }, BuildPresentation.VerbsInCategory(BuildCategory.Power));
        // #143 migration: the furnace and the lamp join the wheel, in that order.
        CollectionAssert.AreEqual(new[] { PlaceableType.ImpWheel, PlaceableType.FurnaceGenerator, PlaceableType.GrowLamp },
            BuildPresentation.TypesInCategory(BuildCategory.Power));
        Assert.AreEqual("Imp Wheel", BuildPresentation.DisplayName(PlaceableType.ImpWheel));
        Assert.AreEqual("POWER", BuildPresentation.RailLabel(BuildCategory.Power));
        // Six, not seven: Magic is still empty and hidden. Rail7 is bound anyway, for the entry Magic will add.
        Assert.AreEqual(6, BuildRail.VisibleEntries().Count, "DIG + Living, Farming, Defense, Power + STOCK");
        CollectionAssert.AreEqual(new[] { "Output 40 W", "Needs an imp" }, new[]
        {
            DetailCard.StatLine(BuildStat.PowerOutput, manager, PlaceableType.ImpWheel, MaterialType.Stone),
            DetailCard.StatLine(BuildStat.NeedsOperator, manager, PlaceableType.ImpWheel, MaterialType.Stone),
        });
    }

    [Test]
    public void TheWheel_CostsFourOfEachMaterial_BuildsInSixSeconds_AndThePowerTableShipsBothRows()
    {
        foreach (MaterialType material in new[] { MaterialType.Stone, MaterialType.Glimmerstone, MaterialType.Deepstone })
        {
            Assert.IsTrue(manager.TryGetCost(PlaceableType.ImpWheel, material, out MaterialAmount cost), $"{material}");
            Assert.AreEqual(4, cost.amount);
        }
        Assert.AreEqual(6f, manager.BuildSecondsFor(PlaceableType.ImpWheel), 1e-6f);

        Assert.IsTrue(manager.TryGetPower(PlaceableType.ImpWheel, out PlaceablePower wheel));
        Assert.AreEqual(PowerRole.Producer, wheel.role);
        Assert.AreEqual(40, wheel.watts);
        Assert.IsTrue(wheel.needsOperator);
        Assert.IsTrue(manager.TryGetPower(PlaceableType.SpikeTrap, out PlaceablePower trap));
        Assert.AreEqual(PowerRole.Consumer, trap.role);
        Assert.AreEqual(10, trap.watts);
        Assert.AreEqual(4f, trap.poweredRearmSeconds, 1e-6f);
        Assert.IsFalse(manager.TryGetPower(PlaceableType.LairCot, out _), "No row: no role");
    }

    [Test]
    public void Rail7_ExistsByName_DefaultsToSeven_WithAStableId()
    {
        Assert.AreEqual("Rail7", InputConfig.Rail7);
        using (var a = InputConfig.CreateActions())
        using (var b = InputConfig.CreateActions())
        {
            CollectionAssert.AreEqual(new[] { "<Keyboard>/7" }, InputConfig.EffectivePaths(a.Rail7));
            Assert.AreEqual(a.Rail7.bindings[0].id, b.Rail7.bindings[0].id);
            Assert.AreNotEqual(a.Rail7.bindings[0].id, a.Rail6.bindings[0].id);
        }
    }

    [Test]
    public void ConduitViews_BuiltInlay_PendingGhost_FoggedHidden()
    {
        var fog = managerGo.AddComponent<FogOfWar>();
        fog.Board = board;
        fog.RevealAround(ImpTile);
        boardRenderer.Fog = fog;
        boardRenderer.Grid = grid;
        boardRenderer.RenderFullBoard();

        BuildConduit(T(22, 15));
        grid.ApplyRect(null, new RectInt(23, 15, 1, 1), PowerGrid.ConduitMode.Lay);
        board.SetTile(30, 16, TileState.Floor);
        grid.ApplyRect(null, new RectInt(30, 16, 1, 1), PowerGrid.ConduitMode.Lay); // Staged under fog (no fog check)
        foreach (Vector2Int t in new[] { T(22, 15), T(23, 15), T(30, 16) }) boardRenderer.RefreshConduit(t); // EditMode: no OnEnable hookup

        Assert.AreEqual("Conduit", boardRenderer.GetConduitView(22, 15)?.name);
        Assert.AreEqual("ConduitPending", boardRenderer.GetConduitView(23, 15)?.name);
        Assert.IsNull(boardRenderer.GetConduitView(30, 16), "Under fog: not drawn");
        Assert.IsNull(boardRenderer.GetConduitView(24, 15), "No conduit");
        Assert.IsNotNull(boardRenderer.GetConduitView(22, 15).transform.Find("Arm10"), "Joined toward its neighbor");
    }
}
