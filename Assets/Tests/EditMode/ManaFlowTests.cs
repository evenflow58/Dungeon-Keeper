using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// #145: the mana simulation. Occupancy (walls, the cap, one type per tile, the vent's own tile closed), the diffusion flow
/// (density weighted by closeness over open tiles, strictly downhill moves with a threshold, flow-around, occupancy
/// updating within a tick), the anti-jam contract (a packed tunnel empties, the vent's mana leaves its tunnel, a closed
/// space comes to rest, the vent saturates every open tile and resumes after a collection), the vent's push through its own
/// mana, accumulators with no pull, the vent (entombed, emitting, saturated, resuming), stores (adopt,
/// accept, forget, clamps), discovery and global totals, accumulator collection, the transfer pass (overflow above the
/// reserve, adopt/skip, the throughput budget with its exact carry, own-tile attachment, independent networks,
/// conservation), and the pipe layer with the imp building it.
/// </summary>
public class ManaFlowTests
{
    private static readonly Vector2Int ImpTile = new Vector2Int(24, 16); // Cavern centre; cavern (21,13)-(26,18)
    private const float Dt = 0.02f;
    private const string Fire = ManaFlow.FireId;
    private const string Water = ManaFlow.WaterId;

    private GameObject managerGo;
    private GameObject impGo;
    private DungeonBoard board;
    private BoardRenderer boardRenderer;
    private PlacementManager manager;
    private PowerGrid grid;
    private ManaFlow mana;
    private Imp imp;
    private ImpDigger digger;

    /// <summary>A store with no building: the registry and the totals over plain data.</summary>
    private sealed class FakePort : IManaPort
    {
        public Vector2Int Tile { get; set; }
        public ManaPortRole Role { get; set; } = ManaPortRole.InputOutput;
        public ManaStore Store { get; set; } = new ManaStore(10);
    }

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
        mana = managerGo.AddComponent<ManaFlow>();
        mana.Board = board;
        mana.HasVent = false; // Tests that want the vent turn it on and place it
        manager = managerGo.AddComponent<PlacementManager>();
        manager.Board = board;
        manager.Renderer = boardRenderer;
        manager.PowerGrid = grid;
        manager.ManaFlow = mana;
    }

    [TearDown]
    public void TearDown()
    {
        if (impGo != null) Object.DestroyImmediate(impGo);
        if (managerGo != null) Object.DestroyImmediate(managerGo);
    }

    // ---- helpers ----

    private static Vector2Int T(int x, int y) => new Vector2Int(x, y);

    private void Open(params Vector2Int[] tiles)
    {
        foreach (Vector2Int t in tiles) board.SetTile(t.x, t.y, TileState.Floor);
    }

    private void OpenRow(int y, int fromX, int toX)
    {
        for (int x = fromX; x <= toX; x++) board.SetTile(x, y, TileState.Floor);
    }

    private void SpawnImp()
    {
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
        digger.ManaFlow = mana;
        digger.DigSecondsPerTile = 1f;
    }

    private void StepImp(float dt = Dt)
    {
        imp.Advance(dt);
        digger.Tick(dt);
    }

    private void BuildPipe(params Vector2Int[] tiles)
    {
        foreach (Vector2Int tile in tiles)
        {
            Assert.AreEqual(1, mana.ApplyPipeRect(null, new RectInt(tile.x, tile.y, 1, 1), ManaFlow.PipeMode.Lay), $"Lay {tile}");
            Assert.IsTrue(mana.CompletePipe(tile));
        }
    }

    private Accumulator BuiltAccumulator(Vector2Int tile)
    {
        Assert.IsTrue(manager.TryPlaceBuilt(PlaceableType.Accumulator, tile, ImpTile, out Placeable p), $"Accumulator at {tile}");
        return p.GetComponent<Accumulator>();
    }

    private ManaContainer BuiltContainer(Vector2Int tile)
    {
        Assert.IsTrue(manager.TryPlaceBuilt(PlaceableType.ManaContainer, tile, ImpTile, out Placeable p), $"Container at {tile}");
        return p.GetComponent<ManaContainer>();
    }

    private static ManaStore StoreWith(int capacity, string type, int amount)
    {
        var store = new ManaStore(capacity);
        if (amount > 0) store.Deposit(type, amount);
        return store;
    }

    // ---- 1. occupancy ----

    [Test]
    public void Occupancy_FloorAndEntranceOpen_RockAndDesignatedWalls_OneUnitPerTile()
    {
        board.SetTile(30, 10, TileState.Designated);
        board.SetTile(31, 10, TileState.Entrance);
        Assert.IsTrue(mana.TryAddUnit(T(22, 14), Fire), "Floor");
        Assert.IsTrue(mana.TryAddUnit(T(31, 10), Fire), "Entrance is open");
        Assert.IsFalse(mana.TryAddUnit(T(5, 5), Fire), "Rock is a wall");
        Assert.IsFalse(mana.TryAddUnit(T(30, 10), Fire), "Designated is a wall");
        Assert.IsFalse(mana.TryAddUnit(T(22, 14), Fire), "maxManaPerTile 1: never two on a tile");
        Assert.IsFalse(mana.TryAddUnit(T(-1, 0), Fire), "Off the board");
        Assert.AreEqual(1, mana.UnitsAt(T(22, 14)));
        Assert.AreEqual(Fire, mana.TypeAt(T(22, 14)));
        Assert.IsNull(mana.TypeAt(T(23, 14)));
        Assert.AreEqual(2, mana.UnitCount);

        Assert.IsTrue(mana.TryTakeUnit(T(22, 14), out string took));
        Assert.AreEqual(Fire, took);
        Assert.AreEqual(0, mana.UnitsAt(T(22, 14)));
        Assert.IsFalse(mana.TryTakeUnit(T(22, 14), out _), "Nothing left");
    }

    [Test]
    public void CanStack_TheCapAndOneTypePerTile()
    {
        Assert.IsTrue(ManaFlow.CanStack(0, null, Fire, 1));
        Assert.IsFalse(ManaFlow.CanStack(1, Fire, Fire, 1), "At the cap");
        Assert.IsTrue(ManaFlow.CanStack(1, Fire, Fire, 2), "A raised cap stacks the same type");
        Assert.IsFalse(ManaFlow.CanStack(1, Fire, Water, 2), "...but never another type");

        mana.MaxManaPerTile = 2;
        Assert.IsTrue(mana.TryAddUnit(T(22, 14), Fire));
        Assert.IsTrue(mana.TryAddUnit(T(22, 14), Fire));
        Assert.IsFalse(mana.TryAddUnit(T(22, 14), Fire), "Cap 2 reached");
        Assert.AreEqual(2, mana.UnitsAt(T(22, 14)));
    }

    // ---- 2. the flow rule ----

    [Test]
    public void Density_WeightsNearerUnits_DividesByTheOpenTiles_LeavesTheMoverOut()
    {
        var units = new Dictionary<Vector2Int, int>();
        Func<Vector2Int, int> at = t => units.TryGetValue(t, out int n) ? n : 0;
        Func<Vector2Int, bool> open = t => true;
        Vector2Int far = T(-99, -99); // No unit stands there: nothing is left out

        Assert.AreEqual(0f, ManaFlow.Density(T(5, 5), far, 2, open, at), "Empty");
        units[T(6, 5)] = 1;
        float near = ManaFlow.Density(T(5, 5), far, 2, open, at);
        units.Clear();
        units[T(7, 5)] = 1;
        float farther = ManaFlow.Density(T(5, 5), far, 2, open, at);
        Assert.Greater(near, farther, "A unit beside counts more than one two tiles off");
        units[T(9, 5)] = 1;
        Assert.AreEqual(farther, ManaFlow.Density(T(5, 5), far, 2, open, at), "Beyond the radius: not counted");

        units.Clear();
        units[T(5, 5)] = 1;
        Assert.AreEqual(0f, ManaFlow.Density(T(5, 5), T(5, 5), 2, open, at), "The mover is left out of its own reading");

        // A packed one-wide tunnel reads fuller than a hall with the same units around it.
        Func<Vector2Int, bool> tunnel = t => t.x == 5;
        units.Clear();
        foreach (Vector2Int t in new[] { T(5, 3), T(5, 4), T(5, 6), T(5, 7) }) units[t] = 1;
        Assert.AreEqual(1f * (2 + 2 + 1 + 1) / (3 + 2 + 2 + 1 + 1), ManaFlow.Density(T(5, 5), far, 2, tunnel, at), 1e-6f);
        Assert.Greater(ManaFlow.Density(T(5, 5), far, 2, tunnel, at), ManaFlow.Density(T(5, 5), far, 2, open, at),
            "Dividing by open tiles: the tunnel is fuller");
    }

    [Test]
    public void ChooseMove_LowestDensityFreeNeighbor_OnlyWhenLowerByTheThreshold_TiesRightFirst()
    {
        var density = new Dictionary<Vector2Int, float>
        {
            [T(5, 5)] = 0.5f, [T(6, 5)] = 0.3f, [T(5, 6)] = 0.2f, [T(4, 5)] = 0.2f, [T(5, 4)] = 0.6f,
        };
        Func<Vector2Int, Vector2Int, float> d = (t, mover) => density.TryGetValue(t, out float v) ? v : 0f;
        Func<Vector2Int, bool> all = t => true;

        Assert.IsTrue(ManaFlow.ChooseMove(T(5, 5), all, d, 0.01f, out Vector2Int to));
        Assert.AreEqual(T(5, 6), to, "Up and left tie at the lowest: up comes first (right, up, left, down)");
        Assert.IsTrue(ManaFlow.ChooseMove(T(5, 5), t => t != T(5, 6) && t != T(4, 5), d, 0.01f, out to));
        Assert.AreEqual(T(6, 5), to, "Never the denser tile below");
        Assert.IsFalse(ManaFlow.ChooseMove(T(5, 5), t => t == T(5, 4), d, 0.01f, out to), "Only uphill is free: rests");
        Assert.AreEqual(T(5, 5), to);

        density[T(6, 5)] = 0.495f;
        Assert.IsFalse(ManaFlow.ChooseMove(T(5, 5), t => t == T(6, 5), d, 0.01f, out _), "A near-tie isn't worth a step");
        Assert.IsTrue(ManaFlow.ChooseMove(T(5, 5), t => t == T(6, 5), d, 0f, out _), "...without the threshold it would be");
        Assert.IsFalse(ManaFlow.ChooseMove(T(5, 5), t => false, d, 0.01f, out _), "Walled in");
    }

    // ---- the anti-jam contract (Oct 22 amendment): the failure modes that killed the first local rule, pinned ----

    // The Main-scene layout: the vent at (24,5) behind a one-wide tunnel (24, 6..12) that opens into the cavern (21..26, 13..18).
    private const int TunnelFloor = 6, TunnelTop = 12, HallFloor = 13;

    private void DigTunnel()
    {
        for (int y = TunnelFloor; y <= TunnelTop; y++) board.SetTile(24, y, TileState.Floor);
    }

    private void PlaceVentBelowTheTunnel()
    {
        mana.HasVent = true;
        mana.VentTile = T(24, TunnelFloor - 1);
        mana.CarveVent();
    }

    private int InHall()
    {
        int n = 0;
        foreach (Vector2Int t in mana.OccupiedTiles()) if (t.y >= HallFloor) n++;
        return n;
    }

    // Steps flow until a tick moves nothing; returns the ticks it took, or -1 if it never rested within maxTicks.
    private int FlowUntilRest(int maxTicks)
    {
        for (int i = 1; i <= maxTicks; i++)
            if (mana.FlowStep() == 0) return i;
        return -1;
    }

    [Test]
    public void Jam1_APackedOneWideTunnel_EmptiesIntoTheHall_AndNeverCyclesAtTheMouth()
    {
        DigTunnel();
        for (int y = TunnelFloor; y <= TunnelTop; y++) Assert.IsTrue(mana.TryAddUnit(T(24, y), Fire));
        int units = mana.UnitCount;

        int restedAfter = FlowUntilRest(200);
        Assert.Greater(restedAfter, 0, "It comes to rest: no unit keeps stepping in and out of the mouth");
        for (int i = 0; i < 50; i++) Assert.AreEqual(0, mana.FlowStep(), "And stays at rest");
        Assert.AreEqual(units, mana.UnitCount, "Conserved");
        Assert.GreaterOrEqual(InHall(), 2, "The front left the tunnel for the hall");
        // With no source behind them, units deeper than the density radius can't feel the empty hall and rest where the
        // crowd thinned out (the vent's push is what drives a tunnel through: Jam2).
    }

    [Test]
    public void Jam2_TheVentsManaLeavesItsTunnel_AndTheMouthNeverJams()
    {
        PlaceVentBelowTheTunnel();
        DigTunnel();
        for (int i = 0; i < 1000; i++) mana.Tick(Dt); // 20 s: ten units, seven of them fit in the tunnel
        Assert.Greater(InHall(), 0, "Mana reached the hall");
        Assert.IsFalse(mana.VentDormant, "Room in the hall: the vent keeps going");
        int at20 = InHall();
        for (int i = 0; i < 1000; i++) mana.Tick(Dt); // 20 s more
        Assert.GreaterOrEqual(InHall() - at20, 8, "Every unit since flowed on into the hall (one per 2 s): nothing stuck at the mouth");
    }

    [Test]
    public void Jam3_AClosedSpace_NoSourceNoSink_ComesToRest()
    {
        // The cavern and its tunnel, no vent and no accumulator: three starting crowds, each spreads and then stops.
        DigTunnel();
        var crowds = new List<Vector2Int[]>
        {
            new[] { T(21, 13), T(22, 13), T(21, 14), T(22, 14), T(23, 13), T(21, 15), T(23, 14), T(22, 15), T(23, 15) },
            new[] { T(24, 8), T(24, 9), T(24, 10), T(24, 11), T(24, 12), T(24, 13), T(23, 13), T(25, 13), T(24, 14) },
        };
        var half = new List<Vector2Int>();
        for (int x = 21; x <= 26; x++)
            for (int y = 13; y <= 15; y++)
                half.Add(T(x, y));
        crowds.Add(half.ToArray()); // Half the hall packed

        foreach (Vector2Int[] crowd in crowds)
        {
            foreach (Vector2Int t in mana.OccupiedTiles()) mana.TryTakeUnit(t, out _);
            foreach (Vector2Int t in crowd) Assert.IsTrue(mana.TryAddUnit(t, Fire), $"{t}");
            int restedAfter = FlowUntilRest(300);
            Assert.Greater(restedAfter, 1, $"A crowd of {crowd.Length} spread, then came to rest");
            for (int i = 0; i < 50; i++) Assert.AreEqual(0, mana.FlowStep(), "At rest it stays at rest: zero moves");
            Assert.AreEqual(crowd.Length, mana.UnitCount, "Conserved");
            foreach (Vector2Int t in mana.OccupiedTiles())
            {
                Assert.AreEqual(1, mana.UnitsAt(t), $"{t}: never two");
                Assert.IsTrue(mana.IsOpen(t), $"{t}: open floor only");
            }
        }
    }

    [Test]
    public void Jam4_Saturation_TheVentFillsEveryOpenTile_GoesDormant_AndResumesAfterACollection()
    {
        PlaceVentBelowTheTunnel();
        DigTunnel();
        // The sealed space the vent opens into: every open tile connected to its mouth.
        var space = new HashSet<Vector2Int> { T(24, TunnelFloor) };
        var queue = new Queue<Vector2Int>(space);
        while (queue.Count > 0)
        {
            Vector2Int t = queue.Dequeue();
            foreach (Vector2Int d in new[] { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down })
                if (mana.IsOpen(t + d) && space.Add(t + d)) queue.Enqueue(t + d);
        }
        int open = space.Count;
        Assert.GreaterOrEqual(open, 43, "The 7-tile tunnel and the 36-tile hall (and whatever else connects)");

        for (int i = 0; i < 10000 && !mana.VentDormant; i++) mana.Tick(Dt);
        Assert.IsTrue(mana.VentDormant, "Dormant by saturation");
        foreach (Vector2Int t in space) Assert.AreEqual(1, mana.UnitsAt(t), $"{t}: every open tile filled");
        Assert.AreEqual(open, mana.UnitCount);

        Assert.IsTrue(mana.TryTakeUnit(T(21, 18), out _), "A collection in the far corner");
        Assert.IsFalse(mana.VentDormant, "A gap anywhere it reaches wakes it");
        for (int i = 0; i < 150; i++) mana.Tick(Dt); // 3 s
        Assert.AreEqual(open, mana.UnitCount, "Refilled");
        Assert.IsTrue(mana.VentDormant, "Full again");
    }

    [Test]
    public void Vent_WithItsMouthFull_PushesItsLineAlong_ToTheNearestFreeTile_StoppedByOtherTypes()
    {
        PlaceVentBelowTheTunnel();
        DigTunnel();
        for (int y = TunnelFloor; y <= 9; y++) Assert.IsTrue(mana.TryAddUnit(T(24, y), Fire));
        Assert.IsTrue(mana.TryFindEmitTile(out Vector2Int tile));
        Assert.AreEqual(T(24, 10), tile, "The line shifts up: the new unit lands where the line ends");

        Assert.IsTrue(mana.TryAddUnit(T(24, 10), Water));
        Assert.IsFalse(mana.TryFindEmitTile(out _), "Water in the way: the push can't pass another type");
        Assert.IsTrue(mana.VentDormant);
    }

    [Test]
    public void Accumulator_HasNoPull_ALoneUnitNearbyStaysPut()
    {
        Accumulator accumulator = BuiltAccumulator(T(21, 13));
        Assert.IsTrue(mana.TryAddUnit(T(23, 14), Fire), "Three tiles off, alone");
        for (int i = 0; i < 500; i++) mana.Tick(Dt); // 10 s
        Assert.AreEqual(1, mana.UnitsAt(T(23, 14)), "Nothing draws it in: a lone unit has nowhere emptier to go");
        Assert.AreEqual(0, accumulator.Buffer.Amount);
    }

    [Test]
    public void Accumulator_DiffusionCarriesTheFrontToIt()
    {
        // A packed bank at the hall's far side, an accumulator in the near corner: nothing pulls, but the bank spreads
        // through the empty hall until its front reaches the accumulator. (With no source, the thinned-out gas then rests;
        // a vent keeps feeding it: the far-corner test below.)
        Accumulator accumulator = BuiltAccumulator(T(21, 13));
        int bank = 0;
        for (int x = 24; x <= 26; x++)
            for (int y = 14; y <= 18; y++)
            {
                Assert.IsTrue(mana.TryAddUnit(T(x, y), Fire));
                bank++;
            }
        for (int i = 0; i < 3000; i++) mana.Tick(Dt); // 60 s
        Assert.Greater(accumulator.Buffer.Amount, 0, "The front reached it");
        Assert.AreEqual(bank, accumulator.Buffer.Amount + mana.UnitCount, "Conserved");
    }

    [Test]
    public void Accumulator_InTheFarCorner_FillsWhenTheFrontArrives_ThenSipsAtTheVentsRate()
    {
        PlaceVentBelowTheTunnel();
        DigTunnel();
        manager.AccumulatorCapacity = 1000;
        Accumulator accumulator = BuiltAccumulator(T(21, 18)); // About 14 tiles' walk from the vent
        for (int i = 0; i < 1500; i++) mana.Tick(Dt); // 30 s: fifteen units, not yet across the hall
        Assert.AreEqual(0, accumulator.Buffer.Amount, "The front hasn't arrived");

        for (int i = 0; i < 9000; i++) mana.Tick(Dt); // 180 s more
        int at210 = accumulator.Buffer.Amount;
        Assert.Greater(at210, 0, "The front arrived and it collected");
        for (int i = 0; i < 3000; i++) mana.Tick(Dt); // 60 s more
        int sipped = accumulator.Buffer.Amount - at210;
        Assert.Greater(sipped, 0, "It keeps collecting");
        Assert.LessOrEqual(sipped, 31, "No faster than the vent emits (one per 2 s): it sips what diffuses in");
    }

    [Test]
    public void Flow_SpreadsFromACrowd_NeverIntoRock_NeverStacks_AndConserves()
    {
        // The cavern (21..26, 13..18): drop a 3x3 block of units in its middle.
        for (int x = 22; x <= 24; x++)
            for (int y = 14; y <= 16; y++)
                Assert.IsTrue(mana.TryAddUnit(T(x, y), Fire));
        for (int i = 0; i < 20; i++) mana.FlowStep();

        Assert.AreEqual(9, mana.UnitCount, "No unit appears or vanishes");
        foreach (Vector2Int t in mana.OccupiedTiles())
        {
            Assert.AreEqual(TileState.Floor, board.GetTile(t.x, t.y), $"{t}: only open tiles");
            Assert.AreEqual(1, mana.UnitsAt(t), $"{t}: never two");
        }
        Assert.Less(Crowding(), 12, "The block spread out (a 3x3 block starts with 12 neighbor pairs)");
    }

    // Adjacent occupied pairs: how crowded the units are.
    private int Crowding()
    {
        int pairs = 0;
        foreach (Vector2Int t in mana.OccupiedTiles())
        {
            if (mana.UnitsAt(t + Vector2Int.right) > 0) pairs++;
            if (mana.UnitsAt(t + Vector2Int.up) > 0) pairs++;
        }
        return pairs;
    }

    [Test]
    public void Flow_AUnitBlockingAOneWideCorridor_CantBePassed_ButWiderSpaceFlowsAround()
    {
        // A one-wide corridor (30..34, 10): a unit at its mouth blocks the rest, which can't stack past it.
        OpenRow(10, 30, 34);
        Assert.IsTrue(mana.TryAddUnit(T(30, 10), Fire));
        Assert.IsTrue(mana.TryAddUnit(T(31, 10), Fire));
        Assert.IsTrue(mana.TryAddUnit(T(32, 10), Fire));
        for (int i = 0; i < 10; i++) mana.FlowStep();
        Assert.AreEqual(3, mana.UnitCount, "In a one-wide corridor nobody passes anybody: they can't stack");
        foreach (Vector2Int t in mana.OccupiedTiles()) Assert.AreEqual(10, t.y, "Never through the rock");

        // Two lanes (y 20 and 21, x 30..34): a unit standing in one lane, the crowd flows past it in the other.
        OpenRow(20, 30, 34);
        OpenRow(21, 30, 34);
        Assert.IsTrue(mana.TryAddUnit(T(31, 20), Fire)); // The blocker
        Assert.IsTrue(mana.TryAddUnit(T(30, 21), Fire));
        Assert.IsTrue(mana.TryAddUnit(T(30, 20), Fire));
        Assert.IsTrue(mana.TryAddUnit(T(31, 21), Fire));
        for (int i = 0; i < 20; i++) mana.FlowStep();
        bool passed = false;
        foreach (Vector2Int t in mana.OccupiedTiles()) if (t.y >= 20 && t.x >= 32) passed = true;
        Assert.IsTrue(passed, "Units got past the blocker through the other lane");
    }

    [Test]
    public void Flow_OccupancyUpdatesAsUnitsMove_WithinATick()
    {
        // A packed one-wide tunnel (30..36, 10) emptying into nothing: in one tick the front unit steps out and the unit
        // behind it can take its tile the same tick, so the run advances more than one tile per tick.
        OpenRow(10, 30, 40);
        for (int x = 30; x <= 34; x++) Assert.IsTrue(mana.TryAddUnit(T(x, 10), Fire));
        mana.FlowStep();
        Assert.AreEqual(5, mana.UnitCount);
        foreach (Vector2Int t in mana.OccupiedTiles()) Assert.AreEqual(1, mana.UnitsAt(t), $"{t}: never two");
        Assert.Greater(mana.OccupiedTiles()[4].x, 34, "The front moved out");
    }

    [Test]
    public void Tick_RunsFlowOnItsSerializedCadence()
    {
        Assert.AreEqual(0.5f, mana.FlowTickSeconds, "Shipped cadence");
        Assert.IsTrue(mana.TryAddUnit(T(22, 14), Fire));
        Assert.IsTrue(mana.TryAddUnit(T(22, 15), Fire)); // Crowded: one of them will step away on a flow tick
        mana.Tick(0.49f);
        Assert.AreEqual(1, mana.UnitsAt(T(22, 14)), "Not yet a flow tick");
        Assert.AreEqual(1, mana.UnitsAt(T(22, 15)));
        mana.Tick(0.02f);
        Assert.IsFalse(mana.UnitsAt(T(22, 14)) == 1 && mana.UnitsAt(T(22, 15)) == 1, "The tick moved one apart");
    }

    // ---- 3. the vent ----

    [Test]
    public void Vent_EntombedIsDormant_DiggingOutANeighborWakesIt_EmittingAtItsInterval()
    {
        // The vent at (24,11), sealed by the rock row y=12 just below the cavern (21..26, 13..18).
        mana.HasVent = true;
        mana.VentTile = T(24, 11);
        mana.CarveVent();
        Assert.AreEqual(TileState.Floor, board.GetTile(24, 11), "The vent's own tile is carved");
        Assert.AreEqual(TileState.Rock, board.GetTile(24, 12), "Its pocket stays sealed");
        Assert.IsFalse(mana.IsOpen(T(24, 11)), "The vent's own tile is never a home for mana");
        Assert.IsTrue(mana.VentDormant, "Entombed");
        Assert.AreEqual(2f, mana.VentEmitSeconds, "Shipped interval");

        for (int i = 0; i < 300; i++) mana.Tick(Dt); // 6 s
        Assert.AreEqual(0, mana.UnitCount, "Entombed: nothing emitted");

        board.SetTile(24, 12, TileState.Floor); // Dug out into the cavern
        Assert.IsFalse(mana.VentDormant);
        mana.Tick(Dt);
        Assert.AreEqual(1, mana.UnitsAt(T(24, 12)), "Held a full interval while dormant: emits the moment space frees");
        Assert.AreEqual(Fire, mana.TypeAt(T(24, 12)));

        for (int i = 0; i < 105; i++) mana.Tick(Dt); // 2.1 s more: the vent emits again (pushing its line along)
        Assert.AreEqual(2, mana.UnitCount, "One unit per 2 s");
        for (int i = 0; i < 400; i++) mana.Tick(Dt); // 8 s more
        Assert.AreEqual(6, mana.UnitCount, "Still one per 2 s while it has room");
        foreach (Vector2Int t in mana.OccupiedTiles())
            Assert.IsTrue(t == T(24, 12) || board.GetTile(t.x, t.y) == TileState.Floor, $"{t}: through open floor only");
    }

    [Test]
    public void Vent_EmitsOntoTheFirstFreeNeighborInScanOrder()
    {
        mana.HasVent = true;
        mana.VentTile = T(22, 15); // In the cavern: all four neighbors open
        Assert.IsTrue(mana.TryFindEmitTile(out Vector2Int tile));
        Assert.AreEqual(T(21, 15), tile, "Scan order: left (x-1) first");
        Assert.IsTrue(mana.TryAddUnit(T(21, 15), Water)); // Another type there: not a home for Fire
        Assert.IsTrue(mana.TryFindEmitTile(out tile));
        Assert.AreEqual(T(22, 14), tile, "Then down (y-1)");
    }

    [Test]
    public void Vent_SaturatedGoesDormant_AndResumesOnItsOwnWhenSpaceFrees()
    {
        // A vent at (10,5) whose only open neighbor (10,6) is a dead end: once that holds a unit, nowhere is free.
        mana.HasVent = true;
        mana.VentTile = T(10, 5);
        mana.CarveVent();
        board.SetTile(10, 6, TileState.Floor);
        int changes = 0;
        mana.VentChanged += () => changes++;

        for (int i = 0; i < 200; i++) mana.Tick(Dt); // 4 s
        Assert.AreEqual(1, mana.UnitsAt(T(10, 6)), "One unit in the dead end: nowhere for it to go (the vent's tile is closed)");
        Assert.AreEqual(1, mana.UnitCount);
        Assert.IsTrue(mana.VentDormant, "Saturated: no free neighbor and nowhere to flow");
        int held = mana.UnitCount;
        for (int i = 0; i < 300; i++) mana.Tick(Dt);
        Assert.AreEqual(held, mana.UnitCount, "Dormant: emits nothing");
        Assert.Greater(changes, 0, "It announced going dormant");

        Assert.IsTrue(mana.TryTakeUnit(T(10, 6), out _), "An accumulator (or anyone) takes the unit");
        Assert.IsFalse(mana.VentDormant, "Space freed");
        mana.Tick(Dt);
        Assert.AreEqual(1, mana.UnitsAt(T(10, 6)), "Resumed unaided");
    }

    // ---- 4. stores, discovery, totals ----

    [Test]
    public void Store_AdoptsOnFirstDeposit_AcceptsOnlyThatType_ForgetsWhenEmptied_ClampsAndReports()
    {
        var store = new ManaStore(5);
        Assert.IsNull(store.Type);
        Assert.IsTrue(store.CanAccept(Water));
        Assert.AreEqual(3, store.Deposit(Fire, 3));
        Assert.AreEqual(Fire, store.Type, "Adopted");
        Assert.IsFalse(store.CanAccept(Water));
        Assert.AreEqual(0, store.Deposit(Water, 1), "Another type is refused");
        Assert.AreEqual(2, store.Deposit(Fire, 10), "Clamped at capacity: reports what went in");
        Assert.IsTrue(store.IsFull);
        Assert.AreEqual(0, store.Deposit(Fire, 1));

        Assert.AreEqual(4, store.Withdraw(4));
        Assert.AreEqual(Fire, store.Type, "Still holding one");
        Assert.AreEqual(1, store.Withdraw(9), "Clamped at the amount");
        Assert.IsNull(store.Type, "Emptied: forgets the type");
        Assert.AreEqual(2, store.Deposit(Water, 2), "...so another type is welcome now");
        Assert.AreEqual(0, store.Deposit(Fire, 0));
        Assert.AreEqual(0, store.Withdraw(-3));
    }

    [Test]
    public void Discovery_FirstHoldAnywhere_IsPermanent_AndTotalsSumAcrossStores()
    {
        var a = new FakePort { Tile = T(1, 1) };
        var b = new FakePort { Tile = T(2, 1) };
        var c = new FakePort { Tile = T(3, 1) };
        mana.Register(a);
        mana.Register(b);
        mana.Register(c);
        mana.Register(a); // Idempotent
        Assert.AreEqual(3, mana.PortCount);
        Assert.IsFalse(mana.IsDiscovered(Fire));
        CollectionAssert.IsEmpty(mana.DiscoveredTypes());

        a.Store.Deposit(Fire, 4);
        b.Store.Deposit(Fire, 3);
        c.Store.Deposit(Water, 2);
        Assert.IsFalse(mana.IsDiscovered(Fire), "Discovery is noted on the clock, not by the store");
        mana.Tick(Dt);
        Assert.IsTrue(mana.IsDiscovered(Fire));
        Assert.IsTrue(mana.IsDiscovered(Water));
        CollectionAssert.AreEqual(new[] { Fire, Water }, mana.DiscoveredTypes(), "Catalog order");
        Assert.AreEqual(7, mana.TotalOf(Fire));
        Assert.AreEqual(2, mana.TotalOf(Water));
        Assert.AreEqual(9, mana.TotalAll());

        a.Store.Withdraw(4);
        b.Store.Withdraw(3);
        mana.Tick(Dt);
        Assert.AreEqual(0, mana.TotalOf(Fire));
        Assert.IsTrue(mana.IsDiscovered(Fire), "Held once: discovered for the session");

        mana.Unregister(c);
        Assert.AreEqual(0, mana.TotalOf(Water), "An unregistered store counts nowhere");
    }

    [Test]
    public void ShippedCatalog_FireAndWater_ByIdNotEnum()
    {
        Assert.AreEqual(2, mana.ManaTypes.Count);
        Assert.AreEqual("Fire Mana", mana.DisplayName(Fire));
        Assert.AreEqual("Water Mana", mana.DisplayName(Water));
        Assert.AreNotEqual(mana.ColorOf(Fire), mana.ColorOf(Water));
        Assert.AreEqual("Storm", mana.DisplayName("Storm"), "A type with no row reads its id");
        Assert.AreEqual(Color.white, mana.ColorOf("Storm"));
    }

    // ---- 5. collection ----

    [Test]
    public void Collect_OwnTileAndAdjacentOnly_ScanOrder_AdoptsAndLeavesMismatches_StopsWhenFull()
    {
        Vector2Int at = T(23, 15);
        var buffer = new ManaStore(3);
        Assert.IsTrue(mana.TryAddUnit(T(24, 16), Fire), "Diagonal");
        Assert.IsTrue(mana.TryAddUnit(T(24, 15), Fire), "Right");
        Assert.IsTrue(mana.TryAddUnit(T(23, 16), Water), "Up");
        Assert.IsTrue(mana.TryAddUnit(T(22, 15), Fire), "Left");
        Assert.IsTrue(mana.TryAddUnit(at, Fire), "Under it");

        Assert.AreEqual(1, Accumulator.Collect(mana, at, buffer, 1));
        Assert.AreEqual(0, mana.UnitsAt(at), "Its own tile first: mana may sit under a building");
        Assert.AreEqual(1, mana.UnitsAt(T(22, 15)));
        buffer.Withdraw(1); // Empty again: the type is forgotten

        Assert.AreEqual(1, Accumulator.Collect(mana, at, buffer, 1));
        Assert.AreEqual(0, mana.UnitsAt(T(22, 15)), "Then scan order: left first");
        Assert.AreEqual(Fire, buffer.Type, "Adopted");

        Assert.AreEqual(1, Accumulator.Collect(mana, at, buffer, 5), "Water above is left: only the right-hand Fire is taken");
        Assert.AreEqual(Water, mana.TypeAt(T(23, 16)), "The mismatch stays put");
        Assert.AreEqual(1, mana.UnitsAt(T(24, 16)), "The diagonal is never reached");
        Assert.AreEqual(2, buffer.Amount);

        Assert.IsTrue(mana.TryAddUnit(T(23, 14), Fire));
        Assert.IsTrue(mana.TryAddUnit(T(22, 15), Fire));
        Assert.AreEqual(1, Accumulator.Collect(mana, at, buffer, 5), "Room for one more");
        Assert.IsTrue(buffer.IsFull);
        Assert.AreEqual(0, Accumulator.Collect(mana, at, buffer, 5), "Full: stops");
    }

    [Test]
    public void LiveAccumulator_CollectsAtItsRate_FillsToCapacity_AndASiteCollectsNothing()
    {
        Assert.AreEqual(20, manager.AccumulatorCapacity);
        Assert.AreEqual(1f, manager.AccumulatorCollectSeconds);
        Assert.AreEqual(1, manager.AccumulatorCollectPerCycle);
        manager.AccumulatorCapacity = 3;
        mana.FlowTickSeconds = 1000f; // Hold units still (they'd wander off) so the counts are exact

        Assert.IsTrue(manager.TryPlace(PlaceableType.Accumulator, T(23, 15), ImpTile, out Placeable site));
        Assert.IsTrue(mana.TryAddUnit(T(22, 15), Fire));
        for (int i = 0; i < 100; i++) mana.Tick(Dt);
        Assert.AreEqual(1, mana.UnitsAt(T(22, 15)), "A site is inert");
        Assert.AreEqual(0, mana.PortCount, "...and not yet a store");

        site.CompleteConstruction();
        Accumulator accumulator = site.GetComponent<Accumulator>();
        Assert.AreEqual(1, mana.PortCount, "Registered on build-complete");
        for (int i = 0; i < 49; i++) mana.Tick(Dt);
        Assert.AreEqual(0, accumulator.Buffer.Amount, "Not before its cycle");
        for (int i = 0; i < 2; i++) mana.Tick(Dt);
        Assert.AreEqual(1, accumulator.Buffer.Amount, "One per second");

        foreach (Vector2Int t in new[] { T(22, 15), T(23, 14), T(23, 16), T(24, 15) }) mana.TryAddUnit(t, Fire);
        for (int i = 0; i < 300; i++) mana.Tick(Dt); // 6 s
        Assert.AreEqual(3, accumulator.Buffer.Amount, "Stops at capacity");
        Assert.AreEqual(2, mana.UnitCount, "The rest wait beside it");
        Assert.AreEqual(3, mana.TotalOf(Fire));
        Assert.IsTrue(mana.IsDiscovered(Fire), "Held: discovered");
        Assert.AreEqual(3, accumulator.RecentCollected(mana.ElapsedSeconds), "All three within the last 10 s");
        for (int i = 0; i < 600; i++) mana.Tick(Dt); // 12 s with nothing to take
        Assert.AreEqual(0, accumulator.RecentCollected(mana.ElapsedSeconds), "Old collections age out of the window");
    }

    // ---- 6. the transfer pass ----

    [Test]
    public void Budget_CarriesTheFraction_SoTheRateIsExactOverTime_AndNeverBanksUnusedUnits()
    {
        float carry = 0f;
        var budgets = new List<int>();
        for (int i = 0; i < 8; i++) budgets.Add(ManaFlow.Budget(carry, 5f, 0.25f, out carry));
        CollectionAssert.AreEqual(new[] { 1, 1, 1, 2, 1, 1, 1, 2 }, budgets, "5/s at 0.25 s: 1.25 per pass");
        int sum = 0;
        carry = 0f;
        for (int i = 0; i < 400; i++) sum += ManaFlow.Budget(carry, 5f, 0.25f, out carry);
        Assert.AreEqual(500, sum, "Exactly 5/s over 100 s");
        Assert.AreEqual(0, ManaFlow.Budget(0f, 0f, 0.25f, out _));
    }

    [Test]
    public void Overflow_OnlyAboveTheReserve_IntoContainersWithRoom_AdoptAndSkip_UnderTheBudget()
    {
        ManaStore acc = StoreWith(20, Fire, 12);
        ManaStore waterTank = StoreWith(100, Water, 4);
        ManaStore empty = new ManaStore(100);
        Assert.AreEqual(3, ManaFlow.Overflow(new[] { acc }, new[] { waterTank, empty }, 5, 3), "Budget 3");
        Assert.AreEqual(4, waterTank.Amount, "Holding Water: skipped for Fire");
        Assert.AreEqual(Fire, empty.Type, "The empty container adopted Fire");
        Assert.AreEqual(3, empty.Amount);
        Assert.AreEqual(9, acc.Amount);

        Assert.AreEqual(4, ManaFlow.Overflow(new[] { acc }, new[] { empty }, 5, 100));
        Assert.AreEqual(5, acc.Amount, "Down to the reserve and no further");
        Assert.AreEqual(0, ManaFlow.Overflow(new[] { acc }, new[] { empty }, 5, 100), "At the reserve: nothing moves");

        ManaStore small = StoreWith(2, Fire, 1);
        ManaStore big = StoreWith(20, Fire, 20);
        Assert.AreEqual(1, ManaFlow.Overflow(new[] { big }, new[] { small }, 5, 100), "Only the room there is");
        Assert.AreEqual(19, big.Amount);
    }

    [Test]
    public void TransferPass_PipesOnly_OwnTileAttachment_ThroughputCapped_AndConserving()
    {
        mana.FlowTickSeconds = 1000f;
        Accumulator acc = BuiltAccumulator(T(22, 14));
        ManaContainer tank = BuiltContainer(T(25, 14));
        ManaContainer beside = BuiltContainer(T(23, 15)); // Touching the accumulator, no pipe under it
        acc.Buffer.Deposit(Fire, 20);

        for (int i = 0; i < 50; i++) mana.Tick(Dt);
        Assert.AreEqual(20, acc.Buffer.Amount, "No pipe: buildings never transfer by touching");
        Assert.AreEqual(0, beside.Store.Amount);

        BuildPipe(T(22, 14), T(23, 14), T(24, 14), T(25, 14)); // Under the accumulator and the tank, not under `beside`
        Assert.AreEqual(-1, mana.NetworkAt(T(23, 15)), "Beside a run is not on it");
        int before = acc.Buffer.Amount + tank.Store.Amount + beside.Store.Amount;

        for (int i = 0; i < 4; i++) mana.TransferPass(0.25f); // 1 s of passes
        Assert.AreEqual(5, tank.Store.Amount, "Throughput 5/s");
        Assert.AreEqual(Fire, tank.Store.Type);
        Assert.AreEqual(0, beside.Store.Amount, "Own-tile only: the container beside the run gets nothing");
        Assert.AreEqual(before, acc.Buffer.Amount + tank.Store.Amount + beside.Store.Amount, "Conserved");

        for (int i = 0; i < 500; i++) mana.Tick(Dt);
        Assert.AreEqual(5, acc.Buffer.Amount, "Drained to its reserve");
        Assert.AreEqual(15, tank.Store.Amount);
        Assert.AreEqual(20, mana.TotalOf(Fire), "Global total unchanged by moving it");
    }

    [Test]
    public void TransferPass_CuttingThePipe_StopsTheFlowWhereTheRunBreaks_AndTwoNetworksAreIndependent()
    {
        mana.FlowTickSeconds = 1000f;
        Accumulator accA = BuiltAccumulator(T(21, 14));
        ManaContainer tankA = BuiltContainer(T(24, 14));
        Accumulator accB = BuiltAccumulator(T(21, 17));
        ManaContainer tankB = BuiltContainer(T(24, 17));
        accA.Buffer.Deposit(Fire, 20);
        accB.Buffer.Deposit(Fire, 20);
        BuildPipe(T(21, 14), T(22, 14), T(23, 14), T(24, 14));
        BuildPipe(T(21, 17), T(22, 17), T(23, 17), T(24, 17));
        Assert.AreEqual(2, mana.NetworkCount);

        for (int i = 0; i < 4; i++) mana.TransferPass(0.25f); // 1 s of passes
        Assert.AreEqual(5, tankA.Store.Amount, "Each network has its own 5/s");
        Assert.AreEqual(5, tankB.Store.Amount);

        Assert.IsTrue(mana.RemovePipe(T(23, 14))); // Cut A's run between the two buildings
        for (int i = 0; i < 8; i++) mana.TransferPass(0.25f); // 2 s of passes
        Assert.AreEqual(5, tankA.Store.Amount, "Cut: A's flow stops where the run breaks");
        Assert.AreEqual(15, tankB.Store.Amount, "B carries on");
    }

    // ---- 7. pipes ----

    [Test]
    public void PipeEligibility_IsExactlyWhatApplyChanges_ExploredFloorOnly_RemoveTakesAny()
    {
        var fog = managerGo.AddComponent<FogOfWar>();
        fog.Board = board;
        fog.RevealAround(T(23, 15));
        board.SetTile(30, 15, TileState.Floor); // Floor, never seen
        var rect = new RectInt(20, 14, 12, 2);  // Rock at x 20 and 27+, floor 21..26, the unseen floor at (30,15)

        var preview = new List<Vector2Int>();
        mana.EligiblePipeTiles(fog, rect, ManaFlow.PipeMode.Lay, preview);
        Assert.AreEqual(12, preview.Count, "Six floor tiles on each of two rows");
        Assert.IsFalse(preview.Contains(T(30, 15)), "Sight-gated");
        Assert.IsFalse(preview.Contains(T(20, 14)), "Rock");
        Assert.AreEqual(preview.Count, mana.ApplyPipeRect(fog, rect, ManaFlow.PipeMode.Lay), "Preview == apply");
        foreach (Vector2Int t in preview) Assert.IsTrue(mana.IsPipePending(t));
        Assert.AreEqual(0, mana.ApplyPipeRect(fog, rect, ManaFlow.PipeMode.Lay), "Once each");

        Assert.IsTrue(mana.CompletePipe(T(22, 14)));
        mana.EligiblePipeTiles(fog, rect, ManaFlow.PipeMode.Remove, preview);
        Assert.AreEqual(12, preview.Count, "Remove takes pending and built alike");
        Assert.AreEqual(12, mana.ApplyPipeRect(fog, rect, ManaFlow.PipeMode.Remove));
        Assert.AreEqual(0, mana.PendingPipeCount + mana.BuiltPipeCount, "Instant, no refund");

        Assert.IsTrue(grid.ApplyRect(null, new RectInt(22, 14, 1, 1), PowerGrid.ConduitMode.Lay) == 1);
        Assert.AreEqual(1, mana.ApplyPipeRect(null, new RectInt(22, 14, 1, 1), ManaFlow.PipeMode.Lay), "Pipe and conduit share a tile");
        Assert.IsTrue(manager.TryPlace(PlaceableType.LairCot, T(22, 14), ImpTile, out _), "Layers never block placement");
    }

    [Test]
    public void PipeProgress_FlipsPendingToBuilt_AtTheSerializedTime_KeepingPartialProgress()
    {
        Assert.AreEqual(0.5f, mana.PipeSecondsPerTile);
        mana.ApplyPipeRect(null, new RectInt(22, 14, 1, 1), ManaFlow.PipeMode.Lay);
        Assert.IsFalse(mana.AddPipeProgress(T(22, 14), 0.2f));
        Assert.AreEqual(0.4f, mana.PipeProgressOf(T(22, 14)), 1e-4f);
        Assert.IsTrue(mana.AddPipeProgress(T(22, 14), 0.3f));
        Assert.IsTrue(mana.IsPipeBuilt(T(22, 14)));
        Assert.IsFalse(mana.AddPipeProgress(T(23, 14), 1f), "Not pending: no-op");
    }

    [Test]
    public void Imp_BuildsPendingPipe_FromBesideIt_AndUnreachablePipeStaysPending()
    {
        SpawnImp();
        mana.ApplyPipeRect(null, new RectInt(22, 14, 2, 1), ManaFlow.PipeMode.Lay);
        board.SetTile(40, 5, TileState.Floor);
        mana.ApplyPipeRect(null, new RectInt(40, 5, 1, 1), ManaFlow.PipeMode.Lay); // Walled in by rock: unreachable

        float elapsed = 0f;
        while (mana.IsPipePending(T(22, 14)) || mana.IsPipePending(T(23, 14)))
        {
            Assert.Less(elapsed, 10f, "Timed out building the run");
            StepImp();
            elapsed += Dt;
            if (digger.IsPiping) Assert.AreNotEqual(digger.CurrentPipe, imp.CurrentTile, "Works from beside the tile, never on it");
        }
        Assert.IsTrue(mana.IsPipeBuilt(T(22, 14)) && mana.IsPipeBuilt(T(23, 14)));
        for (int i = 0; i < 100; i++) StepImp();
        Assert.IsTrue(mana.IsPipePending(T(40, 5)), "Unreachable: stays pending");
    }

    [Test]
    public void Selection_PipeIsConstruction_TiesBreakConduitBeforePipe()
    {
        // A conduit tile and a pipe tile the same path length from the imp: conduit (evaluated first) wins the tie.
        int conduitCost = Pathfinder.FindPathToNeighbor(board, ImpTile, T(22, 16)).Count;
        Assert.AreEqual(conduitCost, Pathfinder.FindPathToNeighbor(board, ImpTile, T(26, 16)).Count, "Staged as a tie");
        Assert.IsTrue(ImpDigger.TrySelectWork(board, null, new[] { T(22, 16) }, new[] { T(26, 16) }, null, ImpTile,
            out Vector2Int target, out _, out _, out bool conduit, out bool pipe, out _));
        Assert.IsTrue(conduit);
        Assert.IsFalse(pipe);
        Assert.AreEqual(T(22, 16), target);

        Assert.IsTrue(ImpDigger.TrySelectWork(board, null, null, new[] { T(26, 16) }, null, ImpTile,
            out target, out _, out _, out conduit, out pipe, out _));
        Assert.IsTrue(pipe, "Alone, the pipe is the job");
        Assert.AreEqual(T(26, 16), target);
    }
}
