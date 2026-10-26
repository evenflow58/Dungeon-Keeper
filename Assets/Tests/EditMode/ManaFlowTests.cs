using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// #145, #150: the mana simulation. Occupancy (walls, the cap of 5, one type per tile, the vent's own tile closed), the pressure
/// flow (one unit per tile per tick to a random neighbor holding fewer, never uphill or sideways, never mixing types,
/// seeded so a run replays exactly), the anti-jam contract (a packed tunnel empties, the vent's mouth never jams, a closed
/// space evens out to within one unit and stays even, the vent fills every open tile to the cap and resumes after a
/// collection), accumulators with no pull, the vent (entombed, emitting, saturated, resuming), stores (adopt,
/// accept, forget, clamps), discovery and global totals, accumulator collection, the transfer pass (overflow above the
/// reserve, adopt/skip, the throughput budget with its exact carry, own-tile attachment, independent networks,
/// conservation), the pipe layer with the imp building it, and (#150) points of interest as data rows: the Spring, both
/// types emitting and going dormant identically, two types sharing one space, and stores meeting a second type.
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
    private ManaPointOfInterest vent;
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
        mana.ClearPointsOfInterest(); // Tests that want a point of interest add and place it
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
    public void Occupancy_FloorAndEntranceOpen_RockAndDesignatedWalls_UpToTheCapPerTile()
    {
        board.SetTile(30, 10, TileState.Designated);
        board.SetTile(31, 10, TileState.Entrance);
        Assert.IsTrue(mana.TryAddUnit(T(22, 14), Fire), "Floor");
        Assert.IsTrue(mana.TryAddUnit(T(31, 10), Fire), "Entrance is open");
        Assert.IsFalse(mana.TryAddUnit(T(5, 5), Fire), "Rock is a wall");
        Assert.IsFalse(mana.TryAddUnit(T(30, 10), Fire), "Designated is a wall");
        for (int i = 0; i < 4; i++) Assert.IsTrue(mana.TryAddUnit(T(22, 14), Fire), "Stacks up to the cap");
        Assert.IsFalse(mana.TryAddUnit(T(22, 14), Fire), "maxManaPerTile 5: never a sixth");
        Assert.IsFalse(mana.TryAddUnit(T(-1, 0), Fire), "Off the board");
        Assert.AreEqual(5, mana.UnitsAt(T(22, 14)));
        Assert.AreEqual(Fire, mana.TypeAt(T(22, 14)));
        Assert.IsNull(mana.TypeAt(T(23, 14)));
        Assert.AreEqual(6, mana.UnitCount);

        Assert.IsTrue(mana.TryTakeUnit(T(22, 14), out string took));
        Assert.AreEqual(Fire, took);
        Assert.AreEqual(4, mana.UnitsAt(T(22, 14)));
        while (mana.TryTakeUnit(T(22, 14), out _)) { }
        Assert.IsNull(mana.TypeAt(T(22, 14)), "Emptied: no type");
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
    public void ChooseMove_OnlyToNeighborsWithFewerUnitsAndRoom_ThePickChoosesAmongThem()
    {
        var units = new Dictionary<Vector2Int, int>
        {
            [T(5, 5)] = 3, [T(6, 5)] = 3, [T(5, 6)] = 1, [T(4, 5)] = 2, [T(5, 4)] = 4,
        };
        Func<Vector2Int, int> at = t => units.TryGetValue(t, out int n) ? n : 0;
        Func<Vector2Int, bool> open = t => units.ContainsKey(t);

        // Candidates in order right, up, left, down: right holds as many (no), up 1 (yes), left 2 (yes), down 4 (no).
        Assert.IsTrue(ManaFlow.ChooseMove(T(5, 5), at, open, n => 0, out Vector2Int to));
        Assert.AreEqual(T(5, 6), to, "Pick 0 of the two: up");
        Assert.IsTrue(ManaFlow.ChooseMove(T(5, 5), at, open, n => { Assert.AreEqual(2, n, "Two candidates"); return 1; }, out to));
        Assert.AreEqual(T(4, 5), to, "Pick 1: left");
        Assert.IsTrue(ManaFlow.ChooseMove(T(5, 5), at, t => open(t) && t != T(5, 6), n => 0, out to));
        Assert.AreEqual(T(4, 5), to, "A neighbor without room (or of another type) is never a candidate");

        units[T(5, 5)] = 1;
        Assert.IsFalse(ManaFlow.ChooseMove(T(5, 5), at, open, n => 0, out to), "Nobody has fewer: it stays (never uphill, never sideways)");
        Assert.AreEqual(T(5, 5), to);
        Assert.IsFalse(ManaFlow.ChooseMove(T(5, 5), at, t => false, n => 0, out _), "Walled in");
    }

    [Test]
    public void Flow_SpreadsAPile_NeverIntoRock_NeverPastTheCap_AndConserves()
    {
        Assert.AreEqual(5, mana.MaxManaPerTile, "Shipped cap");
        foreach (Vector2Int t in new[] { T(23, 15), T(22, 15), T(24, 15), T(23, 14), T(23, 16) })
            for (int i = 0; i < 5; i++) Assert.IsTrue(mana.TryAddUnit(t, Fire));
        Assert.IsFalse(mana.TryAddUnit(T(23, 15), Fire), "Capped at 5");

        for (int i = 0; i < 60; i++) mana.FlowStep();
        Assert.AreEqual(25, mana.UnitCount, "No unit appears or vanishes");
        int most = 0;
        foreach (Vector2Int t in mana.OccupiedTiles())
        {
            Assert.AreEqual(TileState.Floor, board.GetTile(t.x, t.y), $"{t}: only open tiles");
            most = Mathf.Max(most, mana.UnitsAt(t));
        }
        Assert.LessOrEqual(most, 2, "25 units over the 36-tile hall: the pile flattened out");
    }

    [Test]
    public void Flow_NeverMixesTypes_AnotherTypeBlocksTheWay()
    {
        // A closed two-tile pocket (30..31, 10): Fire can't enter the Water tile, Water can't enter the Fire tile.
        OpenRow(10, 30, 31);
        for (int i = 0; i < 5; i++) Assert.IsTrue(mana.TryAddUnit(T(30, 10), Fire));
        Assert.IsTrue(mana.TryAddUnit(T(31, 10), Water));
        for (int i = 0; i < 20; i++) Assert.AreEqual(0, mana.FlowStep(), "Nothing can move");
        Assert.AreEqual(5, mana.UnitsAt(T(30, 10)));
        Assert.AreEqual(Water, mana.TypeAt(T(31, 10)));
    }

    [Test]
    public void Flow_EachTileSendsOneUnitPerTick_DownTheGradient()
    {
        OpenRow(10, 30, 40);
        for (int i = 0; i < 5; i++) Assert.IsTrue(mana.TryAddUnit(T(30, 10), Fire));
        Assert.AreEqual(1, mana.FlowStep(), "One tile held mana: one unit moved");
        Assert.AreEqual(4, mana.UnitsAt(T(30, 10)));
        Assert.AreEqual(1, mana.UnitsAt(T(31, 10)), "Down the gradient, the only lower neighbor");
        Assert.AreEqual(2, mana.FlowStep(), "Two tiles now: each sends one");
        Assert.AreEqual(5, mana.UnitCount);
    }

    [Test]
    public void Flow_TheSameSeedReplaysExactly_AnotherSeedLandsTheSameCounts()
    {
        string Run(int seed)
        {
            foreach (Vector2Int t in mana.OccupiedTiles()) while (mana.TryTakeUnit(t, out _)) { }
            mana.FlowSeed = seed;
            for (int x = 21; x <= 23; x++)
                for (int i = 0; i < 5; i++) Assert.IsTrue(mana.TryAddUnit(T(x, 13), Fire));
            for (int i = 0; i < 200; i++) mana.FlowStep();
            var sb = new System.Text.StringBuilder();
            foreach (Vector2Int t in mana.OccupiedTiles()) sb.Append($"{t}:{mana.UnitsAt(t)} ");
            return sb.ToString();
        }

        string first = Run(7);
        Assert.AreEqual(first, Run(7), "Same seed, same run, tile for tile");
        Run(8);
        Assert.AreEqual(15, mana.UnitCount);
        Assert.AreEqual(15, mana.OccupiedTiles().Count, "15 units over 36 tiles: each seed ends with 15 tiles at 1, wherever they are");
    }

    [Test]
    public void Tick_RunsFlowOnItsSerializedCadence()
    {
        Assert.AreEqual(0.5f, mana.FlowTickSeconds, "Shipped cadence");
        for (int i = 0; i < 3; i++) Assert.IsTrue(mana.TryAddUnit(T(22, 14), Fire));
        mana.Tick(0.49f);
        Assert.AreEqual(3, mana.UnitsAt(T(22, 14)), "Not yet a flow tick");
        mana.Tick(0.02f);
        Assert.AreEqual(2, mana.UnitsAt(T(22, 14)), "The tick sent one unit to a neighbor");
    }

    // ---- the anti-jam contract (Oct 22 amendment, pressure model): the failure modes that killed the first local rule ----

    // The Main-scene layout: the vent at (24,5) behind a one-wide tunnel (24, 6..12) that opens into the cavern (21..26, 13..18).
    private const int TunnelFloor = 6, TunnelTop = 12, HallFloor = 13;

    private void DigTunnel()
    {
        for (int y = TunnelFloor; y <= TunnelTop; y++) board.SetTile(24, y, TileState.Floor);
    }

    private void PlaceVentBelowTheTunnel() => PlaceVent(T(24, TunnelFloor - 1));

    private void PlaceVent(Vector2Int tile)
    {
        vent = mana.AddPointOfInterest(ManaFlow.FireVentName, tile, Fire, 2f);
        mana.CarvePointsOfInterest();
    }

    private int InHall()
    {
        int n = 0;
        foreach (Vector2Int t in mana.OccupiedTiles()) if (t.y >= HallFloor) n += mana.UnitsAt(t);
        return n;
    }

    // Every open tile connected to the tunnel's floor: the tunnel and the hall (and whatever else connects).
    private HashSet<Vector2Int> Space()
    {
        var space = new HashSet<Vector2Int> { T(24, TunnelFloor) };
        var queue = new Queue<Vector2Int>(space);
        while (queue.Count > 0)
        {
            Vector2Int t = queue.Dequeue();
            foreach (Vector2Int d in new[] { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down })
                if (mana.IsOpen(t + d) && space.Add(t + d)) queue.Enqueue(t + d);
        }
        return space;
    }

    // Evened out: every tile of the space holds k or k + 1 units (k = units / tiles, rounded down), and exactly
    // units − k·tiles of them hold k + 1. Which tiles hold the extra is the only thing left to chance.
    private void AssertEvenedOut(HashSet<Vector2Int> space, int units, string because)
    {
        int k = units / space.Count;
        int high = 0;
        foreach (Vector2Int t in space)
        {
            int n = mana.UnitsAt(t);
            Assert.IsTrue(n == k || n == k + 1, $"{t} holds {n}, not {k} or {k + 1}: {because}");
            if (n == k + 1) high++;
        }
        Assert.AreEqual(units - k * space.Count, high, because);
    }

    [Test]
    public void Jam1_APackedOneWideTunnel_EmptiesIntoTheHall_AndTheMouthNeverJams()
    {
        DigTunnel();
        HashSet<Vector2Int> space = Space();
        for (int y = TunnelFloor; y <= TunnelTop; y++)
            for (int i = 0; i < 5; i++) Assert.IsTrue(mana.TryAddUnit(T(24, y), Fire));
        int units = mana.UnitCount;
        Assert.AreEqual(35, units, "The tunnel packed to the cap");

        for (int i = 0; i < 400; i++) mana.FlowStep(); // 200 s
        AssertEvenedOut(space, units, "35 units over the 43 tiles: one each on 35 of them");
        Assert.Greater(InHall(), 25, "Most of it left the tunnel for the hall");
        for (int i = 0; i < 50; i++)
        {
            mana.FlowStep();
            AssertEvenedOut(space, units, "And it stays even: the remainder juggles, the counts don't change");
        }
    }

    [Test]
    public void Jam2_TheVentsManaLeavesItsTunnel_AndTheMouthNeverJams()
    {
        PlaceVentBelowTheTunnel();
        DigTunnel();
        for (int i = 0; i < 3000; i++) // 60 s: thirty units, far short of the 215 the space holds
        {
            mana.Tick(Dt);
            Assert.IsFalse(mana.IsDormant(vent), $"Room everywhere: the vent's mouth never jams ({i * Dt:0.0} s)");
        }
        Assert.Greater(InHall(), 0, "Mana reached the hall");
        Assert.That(mana.UnitCount, Is.InRange(29, 30), "One unit per 2 s, every one of them emitted (the 30th lands on the 60 s mark)");
    }

    [Test]
    public void Jam3_AClosedSpace_NoSourceNoSink_EvensOut_AndStaysEven()
    {
        DigTunnel();
        HashSet<Vector2Int> space = Space();
        var crowds = new List<(Vector2Int[] tiles, int each)>
        {
            (new[] { T(21, 13), T(22, 13), T(21, 14), T(22, 14), T(23, 13), T(21, 15), T(23, 14), T(22, 15), T(23, 15) }, 5),
            (new[] { T(24, 8), T(24, 9), T(24, 10), T(24, 11), T(24, 12) }, 4),
        };
        var half = new List<Vector2Int>();
        for (int x = 21; x <= 26; x++)
            for (int y = 13; y <= 18; y++)
                half.Add(T(x, y));
        crowds.Add((half.ToArray(), 2)); // 72 units: the hall at 2, the tunnel empty
        foreach (Vector2Int t in space) if (t.y < HallFloor) half.Add(t);
        crowds.Add((half.ToArray(), 2)); // 86 units = 2 per tile exactly: evened out means nothing left to move

        foreach ((Vector2Int[] tiles, int each) in crowds)
        {
            foreach (Vector2Int t in mana.OccupiedTiles()) while (mana.TryTakeUnit(t, out _)) { }
            foreach (Vector2Int t in tiles)
                for (int i = 0; i < each; i++) Assert.IsTrue(mana.TryAddUnit(t, Fire), $"{t}");
            int units = tiles.Length * each;

            for (int i = 0; i < 600; i++) mana.FlowStep(); // 300 s
            AssertEvenedOut(space, units, $"A crowd of {units} evened out");
            for (int i = 0; i < 50; i++)
            {
                int moved = mana.FlowStep();
                AssertEvenedOut(space, units, $"A crowd of {units} stays even");
                if (units % space.Count == 0) Assert.AreEqual(0, moved, "Every tile equal: at rest");
            }
            Assert.AreEqual(units, mana.UnitCount, "Conserved");
        }
    }

    [Test]
    public void Jam4_Saturation_TheVentFillsEveryOpenTileToTheCap_GoesDormant_AndResumesAfterACollection()
    {
        PlaceVentBelowTheTunnel();
        DigTunnel();
        HashSet<Vector2Int> space = Space();
        int full = space.Count * mana.MaxManaPerTile;
        Assert.AreEqual(215, full, "43 tiles at 5");

        const float step = 0.1f;
        // Up to 4000 s; it needs 430 s at the least. (The vent reads dormant for a moment whenever its mouth is at the cap,
        // until the next flow tick passes a unit on, so wait for the space itself to fill.)
        for (int i = 0; i < 40000 && mana.UnitCount < full; i++) mana.Tick(step);
        for (int i = 0; i < 50; i++) mana.Tick(step);
        Assert.IsTrue(mana.IsDormant(vent), "Dormant by saturation");
        foreach (Vector2Int t in space) Assert.AreEqual(mana.MaxManaPerTile, mana.UnitsAt(t), $"{t}: every open tile at the cap");
        Assert.AreEqual(full, mana.UnitCount);
        Assert.AreEqual(0, mana.FlowStep(), "Full: nothing moves");

        Assert.IsTrue(mana.TryTakeUnit(T(21, 18), out _), "A collection in the far corner");
        for (int i = 0; i < 20000 && mana.UnitCount < full; i++) mana.Tick(step);
        Assert.AreEqual(full, mana.UnitCount, "The gap flowed back to the vent and it topped the space up");
        Assert.IsTrue(mana.IsDormant(vent), "Full again");
    }

    [Test]
    public void Accumulator_FlowCarriesManaToIt_AndRefillsTheDipItLeaves()
    {
        // A packed bank at the hall's far side, an accumulator in the near corner: nothing pulls, but the bank spreads
        // through the hall to it, and every unit it takes leaves a low spot the flow refills.
        Accumulator accumulator = BuiltAccumulator(T(21, 13));
        int bank = 0;
        for (int x = 24; x <= 26; x++)
            for (int y = 14; y <= 18; y++)
                for (int i = 0; i < 5; i++)
                {
                    Assert.IsTrue(mana.TryAddUnit(T(x, y), Fire));
                    bank++;
                }
        for (int i = 0; i < 3000; i++) mana.Tick(Dt); // 60 s
        Assert.AreEqual(20, accumulator.Buffer.Amount, "It kept collecting until full");
        Assert.AreEqual(bank, accumulator.Buffer.Amount + mana.UnitCount, "Conserved");
    }

    [Test]
    public void Accumulator_InTheFarCorner_FillsWhenTheFrontArrives_ThenSipsAtTheVentsRate()
    {
        PlaceVentBelowTheTunnel();
        DigTunnel();
        manager.AccumulatorCapacity = 1000;
        Accumulator accumulator = BuiltAccumulator(T(21, 18)); // About 14 tiles' walk from the vent
        for (int i = 0; i < 500; i++) mana.Tick(Dt); // 10 s: five units, still in the tunnel
        Assert.AreEqual(0, accumulator.Buffer.Amount, "The front hasn't arrived");

        for (int i = 0; i < 10000; i++) mana.Tick(Dt); // 200 s more
        int at210 = accumulator.Buffer.Amount;
        Assert.Greater(at210, 0, "The front arrived and it collected");
        for (int i = 0; i < 3000; i++) mana.Tick(Dt); // 60 s more
        int sipped = accumulator.Buffer.Amount - at210;
        Assert.Greater(sipped, 0, "It keeps collecting");
        Assert.LessOrEqual(sipped, 31, "No faster than the vent emits (one per 2 s) once the hall's stock is gone");
    }

    // ---- 3. the vent ----

    [Test]
    public void Vent_EntombedIsDormant_DiggingOutANeighborWakesIt_EmittingAtItsInterval()
    {
        // The vent at (24,11), sealed by the rock row y=12 just below the cavern (21..26, 13..18).
        PlaceVent(T(24, 11));
        Assert.AreEqual(TileState.Floor, board.GetTile(24, 11), "The vent's own tile is carved");
        Assert.AreEqual(TileState.Rock, board.GetTile(24, 12), "Its pocket stays sealed");
        Assert.IsFalse(mana.IsOpen(T(24, 11)), "The vent's own tile is never a home for mana");
        Assert.IsTrue(mana.IsDormant(vent), "Entombed");

        for (int i = 0; i < 300; i++) mana.Tick(Dt); // 6 s
        Assert.AreEqual(0, mana.UnitCount, "Entombed: nothing emitted");

        board.SetTile(24, 12, TileState.Floor); // Dug out into the cavern
        Assert.IsFalse(mana.IsDormant(vent));
        mana.Tick(Dt);
        Assert.AreEqual(1, mana.UnitsAt(T(24, 12)), "Held a full interval while dormant: emits the moment space frees");
        Assert.AreEqual(Fire, mana.TypeAt(T(24, 12)));

        for (int i = 0; i < 105; i++) mana.Tick(Dt); // 2.1 s more: the vent emits again
        Assert.AreEqual(2, mana.UnitCount, "One unit per 2 s");
        for (int i = 0; i < 400; i++) mana.Tick(Dt); // 8 s more
        Assert.AreEqual(6, mana.UnitCount, "Still one per 2 s while it has room");
        foreach (Vector2Int t in mana.OccupiedTiles())
            Assert.IsTrue(t == T(24, 12) || board.GetTile(t.x, t.y) == TileState.Floor, $"{t}: through open floor only");
    }

    [Test]
    public void Vent_EmitsOntoTheFirstFreeNeighborInScanOrder()
    {
        vent = mana.AddPointOfInterest(ManaFlow.FireVentName, T(22, 15), Fire, 2f); // In the cavern: all four neighbors open
        Assert.IsTrue(mana.TryFindEmitTile(vent, out Vector2Int tile));
        Assert.AreEqual(T(21, 15), tile, "Scan order: left (x-1) first");
        Assert.IsTrue(mana.TryAddUnit(T(21, 15), Water)); // Another type there: not a home for Fire
        Assert.IsTrue(mana.TryFindEmitTile(vent, out tile));
        Assert.AreEqual(T(22, 14), tile, "Then down (y-1)");
    }

    [Test]
    public void Vent_SaturatedGoesDormant_AndResumesOnItsOwnWhenSpaceFrees()
    {
        // A vent at (10,5) whose only open neighbor (10,6) is a dead end: once that holds 5 units, nowhere has room.
        PlaceVent(T(10, 5));
        board.SetTile(10, 6, TileState.Floor);
        int changes = 0;
        mana.PointOfInterestChanged += t =>
        {
            Assert.AreEqual(T(10, 5), t, "Announced with its tile");
            changes++;
        };

        for (int i = 0; i < 600; i++) mana.Tick(Dt); // 12 s
        Assert.AreEqual(5, mana.UnitsAt(T(10, 6)), "The dead end at the cap: nowhere for it to go (the vent's tile is closed)");
        Assert.AreEqual(5, mana.UnitCount);
        Assert.IsTrue(mana.IsDormant(vent), "Saturated: no free neighbor and nowhere to flow");
        int held = mana.UnitCount;
        for (int i = 0; i < 300; i++) mana.Tick(Dt);
        Assert.AreEqual(held, mana.UnitCount, "Dormant: emits nothing");
        Assert.Greater(changes, 0, "It announced going dormant");

        Assert.IsTrue(mana.TryTakeUnit(T(10, 6), out _), "An accumulator (or anyone) takes a unit");
        Assert.IsFalse(mana.IsDormant(vent), "Space freed");
        mana.Tick(Dt);
        Assert.AreEqual(5, mana.UnitsAt(T(10, 6)), "Resumed unaided");
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
    public void ShippedCatalog_FireWaterAndTheMadeTypes_ByIdNotEnum()
    {
        // #146 migration: Air and Storm join the catalog (2 → 4), so the no-row example is now "Void".
        Assert.AreEqual(4, mana.ManaTypes.Count);
        Assert.AreEqual("Fire Mana", mana.DisplayName(Fire));
        Assert.AreEqual("Water Mana", mana.DisplayName(Water));
        Assert.AreNotEqual(mana.ColorOf(Fire), mana.ColorOf(Water));
        Assert.AreEqual("Void", mana.DisplayName("Void"), "A type with no row reads its id");
        Assert.AreEqual(Color.white, mana.ColorOf("Void"));
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

    // ---- 8. points of interest as rows, the Spring, and a second type (#150) ----

    [Test]
    public void ShippedPointsOfInterest_TheFireVentAndTheSpring_EachInItsOwnSealedPocket()
    {
        var go = new GameObject("FreshMana");
        try
        {
            var fresh = go.AddComponent<ManaFlow>();
            fresh.Board = board;
            Assert.AreEqual(2, fresh.PointsOfInterest.Count);
            ManaPointOfInterest fireVent = fresh.PointsOfInterest[0];
            Assert.AreEqual(ManaFlow.FireVentName, fireVent.displayName);
            Assert.AreEqual(T(24, 5), fireVent.tile, "Deep in the rock south of the cavern");
            Assert.AreEqual(Fire, fireVent.manaType);
            Assert.AreEqual(2f, fireVent.emitSeconds, "Shipped interval");
            ManaPointOfInterest spring = fresh.PointsOfInterest[1];
            Assert.AreEqual("Spring", spring.displayName);
            Assert.AreEqual(T(20, 27), spring.tile, "Shallow: west of the entrance column (24,31)");
            Assert.AreEqual(Water, spring.manaType);
            Assert.AreEqual(2f, spring.emitSeconds);

            fresh.CarvePointsOfInterest();
            foreach (ManaPointOfInterest point in fresh.PointsOfInterest)
            {
                Assert.AreEqual(TileState.Floor, board.GetTile(point.tile.x, point.tile.y), $"{point.displayName}: its own tile carved");
                Assert.IsFalse(fresh.IsOpen(point.tile), $"{point.displayName}: its own tile is never a home for mana");
                foreach (Vector2Int d in new[] { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down })
                    Assert.AreEqual(TileState.Rock, board.GetTile(point.tile.x + d.x, point.tile.y + d.y), $"{point.displayName}: sealed");
                Assert.IsTrue(fresh.IsDormant(point), $"{point.displayName}: entombed at start");
                Assert.AreSame(point, fresh.PointOfInterestAt(point.tile));
            }
            Assert.IsNull(fresh.PointOfInterestAt(T(24, 16)), "Not every tile is a point");
        }
        finally { Object.DestroyImmediate(go); }
    }

    // The Spring's pocket for the parity test: four tiles dug beside it, only (20,28) touching it.
    private static readonly Vector2Int[] SpringPocket = { new Vector2Int(20, 28), new Vector2Int(19, 28), new Vector2Int(21, 28), new Vector2Int(20, 29) };

    [TestCase(ManaFlow.WaterId)]
    [TestCase(ManaFlow.FireId)]
    public void PointOfInterest_AnyType_SealedDormant_DugOutEmits_SaturatesItsPocket_ResumesAfterACollection(string type)
    {
        // The same scenario for either type, with the same exact counts: the row's type is the only thing that differs.
        ManaPointOfInterest spring = mana.AddPointOfInterest(ManaFlow.SpringName, T(20, 27), type, 2f);
        mana.CarvePointsOfInterest();
        int changes = 0;
        mana.PointOfInterestChanged += t => changes++;
        Assert.IsTrue(mana.IsDormant(spring), "Sealed: dormant");
        for (int i = 0; i < 300; i++) mana.Tick(Dt); // 6 s
        Assert.AreEqual(0, mana.UnitCount, "Sealed: nothing emitted");

        Open(SpringPocket);
        Assert.IsFalse(mana.IsDormant(spring), "Dug out");
        mana.Tick(Dt);
        Assert.AreEqual(1, mana.UnitsAt(T(20, 28)), "Emits the moment it has room");
        Assert.AreEqual(type, mana.TypeAt(T(20, 28)), "Its own type");
        for (int i = 0; i < 500; i++) mana.Tick(Dt); // 10 s more
        Assert.AreEqual(6, mana.UnitCount, "One unit per 2 s");

        int full = SpringPocket.Length * mana.MaxManaPerTile;
        for (int i = 0; i < 4000 && mana.UnitCount < full; i++) mana.Tick(0.1f);
        for (int i = 0; i < 50; i++) mana.Tick(0.1f);
        Assert.AreEqual(full, mana.UnitCount, "The pocket filled");
        foreach (Vector2Int t in SpringPocket)
        {
            Assert.AreEqual(mana.MaxManaPerTile, mana.UnitsAt(t), $"{t} at the cap");
            Assert.AreEqual(type, mana.TypeAt(t));
        }
        Assert.IsTrue(mana.IsDormant(spring), "Dormant by saturation");
        Assert.Greater(changes, 0, "It announced its state changes");

        Assert.IsTrue(mana.TryTakeUnit(T(20, 29), out string took), "A collection at the pocket's far end");
        Assert.AreEqual(type, took);
        for (int i = 0; i < 2000 && mana.UnitCount < full; i++) mana.Tick(0.1f);
        Assert.AreEqual(full, mana.UnitCount, "The gap flowed back and it topped the pocket up");
        Assert.IsTrue(mana.IsDormant(spring), "Full again");
    }

    // Units of a type on the board.
    private int UnitsOf(string type)
    {
        int n = 0;
        foreach (Vector2Int t in mana.OccupiedTiles()) if (mana.TypeAt(t) == type) n += mana.UnitsAt(t);
        return n;
    }

    [Test]
    public void TwoTypes_OneHall_NoSources_EachTypeConservedEveryTick_AndNeverShareATile()
    {
        // A Fire crowd in the hall's west columns and a Water crowd in its east columns, nothing emitting or collecting.
        for (int y = 13; y <= 18; y++)
        {
            for (int i = 0; i < 5; i++) Assert.IsTrue(mana.TryAddUnit(T(21, y), Fire));
            for (int i = 0; i < 5; i++) Assert.IsTrue(mana.TryAddUnit(T(26, y), Water));
        }
        int fire = UnitsOf(Fire), water = UnitsOf(Water);
        Assert.AreEqual(30, fire);
        Assert.AreEqual(30, water);

        bool met = false;
        for (int step = 0; step < 400; step++) // 200 s
        {
            mana.FlowStep();
            Assert.AreEqual(fire, UnitsOf(Fire), $"Tick {step}: Fire conserved");
            Assert.AreEqual(water, UnitsOf(Water), $"Tick {step}: Water conserved");
            foreach (Vector2Int t in mana.OccupiedTiles())
            {
                Assert.LessOrEqual(mana.UnitsAt(t), mana.MaxManaPerTile, $"{t}: never past the cap");
                foreach (Vector2Int d in new[] { Vector2Int.right, Vector2Int.up })
                {
                    string other = mana.TypeAt(t + d);
                    if (other != null && other != mana.TypeAt(t)) met = true;
                }
            }
        }
        Assert.IsTrue(met, "The fronts met: Fire and Water side by side, each on its own tiles");
        Assert.Less(mana.UnitsAt(T(21, 15)), 5, "Fire spread out of its column");
        Assert.Less(mana.UnitsAt(T(26, 15)), 5, "Water spread out of its column");
    }

    [Test]
    public void BothPointsLive_InOneSpace_BothTypesGrowByTheirEmissions_DormantOnlyWhenTheMouthIsTaken()
    {
        PlaceVentBelowTheTunnel();
        DigTunnel();
        // A Spring at the hall's north wall, its mouth (24,18) in the hall: both points feed the one connected space.
        ManaPointOfInterest spring = mana.AddPointOfInterest(ManaFlow.SpringName, T(24, 19), Water, 2f);
        mana.CarvePointsOfInterest();

        int lastFire = 0, lastWater = 0;
        for (int i = 0; i < 3000; i++) // 60 s: about 30 of each, far short of the 215 the space holds
        {
            mana.Tick(Dt);
            // #146: where the fronts meet they combine on the map (3 + 3 → 1). Count what each point has emitted: what's
            // on the board plus what reactions consumed (a1 Air reactions, a2 Storm reactions; Storm = a2, Air = a1 − 3·a2).
            int a2 = UnitsOf(ManaFlow.StormId);
            int a1 = UnitsOf(ManaFlow.AirId) + 3 * a2;
            int f = UnitsOf(Fire) + 3 * a1, w = UnitsOf(Water) + 3 * a1 + 3 * a2;
            Assert.GreaterOrEqual(f, lastFire, "Emitted Fire only grows: nothing collects");
            Assert.GreaterOrEqual(w, lastWater, "Emitted Water only grows");
            Assert.AreEqual(mana.UnitCount, UnitsOf(Fire) + UnitsOf(Water) + UnitsOf(ManaFlow.AirId) + UnitsOf(ManaFlow.StormId),
                "Every unit is Fire, Water, or what they made");
            lastFire = f;
            lastWater = w;
            // A one-mouth point reads dormant while its mouth is at the cap or holds the other type: the shipped rule,
            // never anything else.
            Assert.AreEqual(!mana.CanAccept(T(24, TunnelFloor), Fire), mana.IsDormant(vent), $"The vent ({i * Dt:0.0} s)");
            Assert.AreEqual(!mana.CanAccept(T(24, 18), Water), mana.IsDormant(spring), $"The Spring ({i * Dt:0.0} s)");
        }
        Assert.Greater(lastFire, 20, "Fire kept coming: about one per 2 s");
        Assert.Greater(lastWater, 20, "Water kept coming: about one per 2 s");
        int fireInHall = 0;
        foreach (Vector2Int t in mana.OccupiedTiles()) if (t.y >= HallFloor && mana.TypeAt(t) == Fire) fireInHall += mana.UnitsAt(t);
        Assert.Greater(fireInHall, 0, "Fire reached the hall, where the Water is");
    }

    [Test]
    public void Accumulator_HoldingFire_IgnoresWaterBesideIt_OnceEmptiedItAdoptsWater_AndViceVersa()
    {
        mana.FlowTickSeconds = 1000f; // Hold units still so the counts are exact
        Accumulator accumulator = BuiltAccumulator(T(23, 15));
        Assert.AreEqual(3, accumulator.Buffer.Deposit(Fire, 3));
        for (int i = 0; i < 2; i++) Assert.IsTrue(mana.TryAddUnit(T(22, 15), Water));

        for (int i = 0; i < 300; i++) mana.Tick(Dt); // 6 s: six collection cycles
        Assert.AreEqual(3, accumulator.Buffer.Amount, "Holding Fire: collected zero Water");
        Assert.AreEqual(Fire, accumulator.Buffer.Type);
        Assert.AreEqual(2, mana.UnitsAt(T(22, 15)), "The Water stays put");

        Assert.AreEqual(3, accumulator.Buffer.Withdraw(3), "Emptied (spent, or piped away)");
        Assert.IsNull(accumulator.Buffer.Type, "Forgets Fire");
        int ticks = 0;
        while (accumulator.Buffer.IsEmpty && ticks < 100) { mana.Tick(Dt); ticks++; }
        Assert.LessOrEqual(ticks, 51, "Within its next cycle");
        Assert.AreEqual(Water, accumulator.Buffer.Type, "Adopted Water");
        Assert.AreEqual(1, accumulator.Buffer.Amount);

        Assert.IsTrue(mana.TryAddUnit(T(24, 15), Fire));
        for (int i = 0; i < 100; i++) mana.Tick(Dt); // 2 s
        Assert.AreEqual(2, accumulator.Buffer.Amount, "The second Water came in");
        Assert.AreEqual(Water, accumulator.Buffer.Type);
        Assert.AreEqual(1, mana.UnitsAt(T(24, 15)), "...and now the Fire beside it is the one ignored");
    }

    [Test]
    public void Container_HoldingFire_RefusesPipedWater_OnceEmptiedItAdoptsWater()
    {
        mana.FlowTickSeconds = 1000f;
        Accumulator accumulator = BuiltAccumulator(T(22, 14));
        ManaContainer tank = BuiltContainer(T(23, 14));
        BuildPipe(T(22, 14), T(23, 14));
        accumulator.Buffer.Deposit(Water, 15);
        tank.Store.Deposit(Fire, 4);

        for (int i = 0; i < 100; i++) mana.Tick(Dt); // 2 s: the pass would move 10 if it could
        Assert.AreEqual(4, tank.Store.Amount, "Holding Fire: no Water piped in");
        Assert.AreEqual(Fire, tank.Store.Type);
        Assert.AreEqual(15, accumulator.Buffer.Amount, "The Water waits in the accumulator");

        tank.Store.Withdraw(4);
        for (int i = 0; i < 150; i++) mana.Tick(Dt); // 3 s: room for the whole 10 at 5/s
        Assert.AreEqual(Water, tank.Store.Type, "Emptied: it adopted Water");
        Assert.AreEqual(10, tank.Store.Amount, "Everything above the reserve");
        Assert.AreEqual(5, accumulator.Buffer.Amount);
        Assert.AreEqual(15, mana.TotalOf(Water), "Conserved across the two stores");
        Assert.IsTrue(mana.IsDiscovered(Fire) && mana.IsDiscovered(Water), "Both types discovered in one session");
    }
}
