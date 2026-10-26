using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// #146: the process table, its pilot host the Combination Chamber, and combination on the map. Table resolution by host;
/// the new made types; the chamber's cycle accounting at the host ratio (6 + 6 → 1: exactly the row's inputs × the host
/// multiplier consumed at the start, exactly its outputs produced at the end, every unit accounted for in between); the
/// multiplier as tuning; the draw order (containers before buffers, buffers never below the reserve); both stall reasons
/// and the hands-off resume; a partial deposit; process selection; the full chain to Lightning; no conversion in pipes;
/// the map collision (3 + 3 → 1, exact, nothing below the row's amounts, the product placed or the reaction waits); and
/// the chamber's shipped data and presentation.
/// </summary>
public class CombinationChamberTests
{
    private static readonly Vector2Int ImpTile = new Vector2Int(24, 16); // Cavern centre; cavern (21,13)-(26,18)
    private const float Dt = 0.02f;
    private const string Fire = ManaFlow.FireId;
    private const string Water = ManaFlow.WaterId;
    private const string Air = ManaFlow.AirId;
    private const string Storm = ManaFlow.StormId;
    private const string Lightning = ManaFlow.LightningId;

    private GameObject managerGo;
    private DungeonBoard board;
    private BoardRenderer boardRenderer;
    private PlacementManager manager;
    private ManaFlow mana;

    [SetUp]
    public void SetUp()
    {
        managerGo = new GameObject("TestDungeonManager");
        board = managerGo.AddComponent<DungeonBoard>();
        board.InitializeBoard();
        boardRenderer = managerGo.AddComponent<BoardRenderer>();
        boardRenderer.Board = board;
        mana = managerGo.AddComponent<ManaFlow>();
        mana.Board = board;
        mana.ClearPointsOfInterest();
        manager = managerGo.AddComponent<PlacementManager>();
        manager.Board = board;
        manager.Renderer = boardRenderer;
        manager.ManaFlow = mana;
        boardRenderer.Mana = mana;
    }

    [TearDown]
    public void TearDown()
    {
        if (managerGo != null) Object.DestroyImmediate(managerGo);
    }

    // ---- helpers ----

    private static Vector2Int T(int x, int y) => new Vector2Int(x, y);

    private void BuildPipe(params Vector2Int[] tiles)
    {
        foreach (Vector2Int tile in tiles)
        {
            Assert.AreEqual(1, mana.ApplyPipeRect(null, new RectInt(tile.x, tile.y, 1, 1), ManaFlow.PipeMode.Lay), $"Lay {tile}");
            Assert.IsTrue(mana.CompletePipe(tile));
        }
    }

    private ManaContainer Tank(Vector2Int tile, string type = null, int amount = 0)
    {
        Assert.IsTrue(manager.TryPlaceBuilt(PlaceableType.ManaContainer, tile, ImpTile, out Placeable p), $"Container at {tile}");
        var tank = p.GetComponent<ManaContainer>();
        if (amount > 0) Assert.AreEqual(amount, tank.Store.Deposit(type, amount));
        BuildPipe(tile);
        return tank;
    }

    private Accumulator PipedAccumulator(Vector2Int tile, string type, int amount)
    {
        Assert.IsTrue(manager.TryPlaceBuilt(PlaceableType.Accumulator, tile, ImpTile, out Placeable p), $"Accumulator at {tile}");
        var accumulator = p.GetComponent<Accumulator>();
        if (amount > 0) Assert.AreEqual(amount, accumulator.Buffer.Deposit(type, amount));
        BuildPipe(tile);
        return accumulator;
    }

    private CombinationChamber Chamber(Vector2Int tile)
    {
        Assert.IsTrue(manager.TryPlaceBuilt(PlaceableType.CombinationChamber, tile, ImpTile, out Placeable p), $"Chamber at {tile}");
        BuildPipe(tile);
        return p.GetComponent<CombinationChamber>();
    }

    private void Run(float seconds)
    {
        int steps = Mathf.RoundToInt(seconds / Dt);
        for (int i = 0; i < steps; i++) mana.Tick(Dt);
    }

    // Ticks until the condition holds (or the limit runs out); returns the game seconds it took.
    private float RunUntil(System.Func<bool> done, float limitSeconds)
    {
        float t = 0f;
        while (!done() && t < limitSeconds)
        {
            mana.Tick(Dt);
            t += Dt;
        }
        Assert.IsTrue(done(), $"Not reached within {limitSeconds} s");
        return t;
    }

    // Puts n units of a type on a tile (the tile must be open).
    private void Put(Vector2Int tile, string type, int n)
    {
        for (int i = 0; i < n; i++) Assert.IsTrue(mana.TryAddUnit(tile, type), $"{type} #{i + 1} onto {tile}");
    }

    // Carves exactly these tiles to Floor (outside the cavern, everything around them stays rock).
    private void Carve(params Vector2Int[] tiles)
    {
        foreach (Vector2Int t in tiles) board.SetTile(t.x, t.y, TileState.Floor);
    }

    // Units of a type on the board.
    private int OnBoard(string type)
    {
        int n = 0;
        foreach (Vector2Int tile in mana.OccupiedTiles())
            if (mana.TypeAt(tile) == type) n += mana.UnitsAt(tile);
        return n;
    }

    // ---- the process table ----

    [Test]
    public void ProcessTable_TheThreeChamberRows_ResolveExactly_FilteredByHost()
    {
        List<ManaProcessRow> rows = mana.RowsFor(PlaceableType.CombinationChamber);
        Assert.AreEqual(3, rows.Count);
        void AssertRow(ManaProcessRow row, (string, int)[] inputs, (string, int)[] outputs, string name, string mapName, bool onMap)
        {
            Assert.AreEqual(PlaceableType.CombinationChamber, row.host);
            Assert.AreEqual(inputs.Length, row.inputs.Count);
            for (int i = 0; i < inputs.Length; i++)
            {
                Assert.AreEqual(inputs[i].Item1, row.inputs[i].type);
                Assert.AreEqual(inputs[i].Item2, row.inputs[i].amount);
            }
            Assert.AreEqual(outputs.Length, row.outputs.Count);
            for (int i = 0; i < outputs.Length; i++)
            {
                Assert.AreEqual(outputs[i].Item1, row.outputs[i].type);
                Assert.AreEqual(outputs[i].Item2, row.outputs[i].amount);
            }
            Assert.AreEqual(10f, row.cycleSeconds);
            Assert.AreEqual(2, row.hostInputMultiplier, "The chamber takes double");
            Assert.AreEqual(name, row.Name, "Named from its data, as the chamber runs it");
            Assert.AreEqual(mapName, row.MapName, "...and as it happens on the map");
            Assert.AreEqual(onMap, row.CanCombineOnMap);
        }
        AssertRow(rows[0], new[] { (Fire, 3), (Water, 3) }, new[] { (Air, 1) }, "6 Fire + 6 Water → 1 Air", "3 Fire + 3 Water → 1 Air", true);
        AssertRow(rows[1], new[] { (Air, 3), (Water, 3) }, new[] { (Storm, 1) }, "6 Air + 6 Water → 1 Storm", "3 Air + 3 Water → 1 Storm", true);
        AssertRow(rows[2], new[] { (Storm, 3) }, new[] { (Lightning, 1) }, "6 Storm → 1 Lightning", "3 Storm → 1 Lightning", false);
        Assert.AreEqual(3, rows[0].MapInputOf(Fire));
        Assert.AreEqual(6, rows[0].HostInputOf(Fire));
        Assert.AreEqual(0, rows[0].HostInputOf(Storm));

        Assert.IsEmpty(mana.RowsFor(PlaceableType.Accumulator), "A host with no rows has none");
        Assert.IsEmpty(mana.RowsFor(PlaceableType.LairCot));

        // Rows key by host: a later host's rows never leak into the chamber's.
        mana.Processes.Add(new ManaProcessRow(PlaceableType.FurnaceGenerator, new[] { new ManaAmount(Fire, 1) }, new ManaAmount[0], 5f));
        Assert.AreEqual(3, mana.RowsFor(PlaceableType.CombinationChamber).Count);
        Assert.AreEqual(1, mana.RowsFor(PlaceableType.FurnaceGenerator).Count);
    }

    [Test]
    public void HostMultiplier_IsTuning_TheChambersDemandAndNameFollowIt_NeverBelowTheMap()
    {
        ManaProcessRow row = mana.RowsFor(PlaceableType.CombinationChamber)[0];
        row.hostInputMultiplier = 3;
        Assert.AreEqual("9 Fire + 9 Water → 1 Air", row.Name);
        var processor = new ManaProcessor(row);
        Assert.AreEqual(9, processor.Demand(Fire));
        Assert.AreEqual(9, processor.Stage(Fire, 20), "Stages only the tuned demand");
        Assert.AreEqual(8, processor.Stage(Water, 8));
        Assert.IsFalse(processor.IsRunning, "Short one Water at ×3");
        Assert.AreEqual(1, processor.Stage(Water, 1));
        Assert.IsTrue(processor.IsRunning);

        row.hostInputMultiplier = 0;
        Assert.AreEqual(1, row.HostMultiplier, "A host never gets the reaction cheaper than the map");
        Assert.AreEqual("3 Fire + 3 Water → 1 Air", row.Name);
    }

    [Test]
    public void ShippedTypes_AirStormAndLightning_AreDataRows()
    {
        foreach ((string id, string name) in new[] { (Air, "Air Mana"), (Storm, "Storm Mana"), (Lightning, "Lightning Mana") })
        {
            Assert.IsTrue(mana.TryGetType(id, out ManaTypeDefinition row), id);
            Assert.AreEqual(name, row.displayName);
            Assert.AreNotEqual(Color.white, row.color, $"{id} has its own color");
        }
        var colors = new HashSet<Color>();
        foreach (ManaTypeDefinition d in mana.ManaTypes) colors.Add(d.color);
        Assert.AreEqual(5, colors.Count, "Five types, five colors");
    }

    // ---- the cycle ----

    [Test]
    public void Cycle_ConsumesExactly6And6_ProducesExactly1Air_At10s_EveryUnitAccountedFor()
    {
        Tank(T(21, 15), Fire, 20);
        Tank(T(22, 15), Water, 20);
        CombinationChamber chamber = Chamber(T(23, 15));
        ManaContainer airTank = Tank(T(24, 15));
        ManaProcessor processor = chamber.Processor;
        Assert.AreEqual(0, chamber.SelectedIndex, "A new chamber runs the table's first chamber row");

        float startedAt = -1f, completedAt = -1f, t = 0f;
        for (int i = 0; i < 1000 && processor.CyclesCompleted < 1; i++) // Up to 20 s
        {
            mana.Tick(Dt);
            t += Dt;
            int started = processor.CyclesCompleted + (processor.IsRunning ? 1 : 0);
            Assert.AreEqual(40 - 12 * started, mana.TotalOf(Fire) + mana.TotalOf(Water), $"{t:0.00} s: inputs leave only at a cycle's start");
            Assert.AreEqual(processor.CyclesCompleted, mana.TotalOf(Air), $"{t:0.00} s: Air appears only at a cycle's end");
            if (startedAt < 0f && processor.IsRunning) startedAt = t;
            if (processor.CyclesCompleted == 1) completedAt = t;
        }
        Assert.Greater(startedAt, 0f, "It drew a full set and started");
        Assert.Less(startedAt, 3.5f, "Twelve units at 5/s: staged within about 2.4 s");
        Assert.AreEqual(10f, completedAt - startedAt, 2 * Dt, "The row's cycle: 10 s");
        Assert.AreEqual(14, mana.TotalOf(Fire), "Exactly 6 Fire consumed");
        Assert.AreEqual(14, mana.TotalOf(Water), "Exactly 6 Water consumed");
        Assert.AreEqual(1, mana.TotalOf(Air), "Exactly 1 Air produced");
        Assert.IsTrue(mana.IsDiscovered(Air), "Held anywhere: discovered the moment the cycle completed");

        Run(0.5f);
        Assert.AreEqual(1, airTank.Store.Amount, "Deposited into the empty container, which adopted Air");
        Assert.AreEqual(Air, airTank.Store.Type);
        Assert.IsFalse(processor.IsHolding);
        Assert.AreEqual(ManaProcessorStall.None, processor.Stall);
    }

    [Test]
    public void Draw_ContainersFirst_ThenBuffers_AndABufferNeverDropsBelowItsReserve()
    {
        ManaProcessRow row = mana.RowsFor(PlaceableType.CombinationChamber)[0];
        var processor = new ManaProcessor(row);
        ManaStore fireTank = StoreWith(100, Fire, 1);
        ManaStore fireBuffer = StoreWith(20, Fire, 11);
        ManaStore waterBuffer = StoreWith(20, Water, 20);
        ManaStore emptyTank = new ManaStore(100);

        // Budget 1: the container's Fire goes first.
        Assert.AreEqual(1, ManaFlow.Draw(new[] { processor }, new[] { emptyTank, fireTank }, new[] { fireBuffer, waterBuffer }, 5, 1));
        Assert.AreEqual(0, fireTank.Amount, "From the container first");
        Assert.AreEqual(11, fireBuffer.Amount);
        Assert.AreEqual(1, processor.StagedOf(Fire));

        Assert.AreEqual(11, ManaFlow.Draw(new[] { processor }, new[] { emptyTank, fireTank }, new[] { fireBuffer, waterBuffer }, 5, 100));
        Assert.AreEqual(6, fireBuffer.Amount, "Then the buffer, for the other 5");
        Assert.AreEqual(14, waterBuffer.Amount, "6 Water");
        Assert.IsTrue(processor.IsRunning, "A full set staged: consumed, running");
        Assert.AreEqual(0, emptyTank.Amount, "An empty container supplies nothing");

        var second = new ManaProcessor(row);
        ManaStore atReserve = StoreWith(20, Fire, 5);
        Assert.AreEqual(6, ManaFlow.Draw(new[] { second }, new ManaStore[0], new[] { atReserve, waterBuffer }, 5, 100));
        Assert.AreEqual(5, atReserve.Amount, "At the reserve: never drawn below it");
        Assert.AreEqual(0, second.StagedOf(Fire));
        Assert.AreEqual(6, second.StagedOf(Water), "Partial staging is held");
        Assert.AreEqual(8, waterBuffer.Amount);
        Assert.IsFalse(second.IsRunning);
    }

    private static ManaStore StoreWith(int capacity, string type, int amount)
    {
        var store = new ManaStore(capacity);
        if (amount > 0) store.Deposit(type, amount);
        return store;
    }

    // ---- stalls and resume ----

    [Test]
    public void MissingInput_NamesIt_PartialStagingIsHeld_AndSupplyingItResumesHandsOff()
    {
        ManaContainer fireTank = Tank(T(21, 15), Fire, 10);
        CombinationChamber chamber = Chamber(T(23, 15));
        ManaContainer airTank = Tank(T(24, 15), Air, 1); // Holding Air already: it can't adopt Water or Fire
        BuildPipe(T(22, 15));
        ManaProcessor processor = chamber.Processor;

        Run(3f);
        Assert.AreEqual(ManaProcessorStall.MissingInput, processor.Stall);
        Assert.AreEqual("Missing Water", processor.StallText);
        Assert.AreEqual("Stalled: Missing Water", DetailCard.ChamberStateLine(processor));
        Assert.AreEqual(6, processor.StagedOf(Fire), "Its Fire is staged and held, committed to the chamber");
        Assert.AreEqual(4, fireTank.Store.Amount);
        Assert.AreEqual(10, mana.TotalOf(Fire), "Staged Fire still counts");
        Assert.IsFalse(processor.IsRunning);

        // The fix is elsewhere: an accumulator with Water joins the network. Nothing touches the chamber.
        PipedAccumulator(T(23, 14), Water, 15);
        Run(2f);
        Assert.IsTrue(processor.IsRunning, "Supplied: the cycle started on its own");
        Assert.AreEqual(ManaProcessorStall.None, processor.Stall);
        RunUntil(() => airTank.Store.Amount == 2, 12f);
        Assert.AreEqual(1, processor.CyclesCompleted);
    }

    [Test]
    public void NoRoomForOutput_HoldsTheProduct_StartsNoNewCycle_AndRoomResumesIt()
    {
        ManaContainer fireTank = Tank(T(21, 15), Fire, 20);
        ManaContainer waterTank = Tank(T(22, 15), Water, 20);
        CombinationChamber chamber = Chamber(T(23, 15));
        ManaProcessor processor = chamber.Processor;

        RunUntil(() => processor.CyclesCompleted == 1, 15f);
        Run(0.5f);
        Assert.AreEqual(1, processor.HeldOf(Air), "Nowhere to put it: held");
        Assert.AreEqual(ManaProcessorStall.NoRoomForOutput, processor.Stall);
        Assert.AreEqual("No room for output", processor.StallText);
        Assert.AreEqual(1, mana.TotalOf(Air), "Held product counts in the totals");
        Assert.IsTrue(mana.IsDiscovered(Air));

        Run(25f);
        Assert.AreEqual(1, processor.CyclesCompleted, "No new cycle while holding");
        Assert.AreEqual(0, processor.StagedTotal, "...and nothing drawn for one");
        Assert.AreEqual(14, fireTank.Store.Amount);
        Assert.AreEqual(14, waterTank.Store.Amount);

        // Room: an empty container joins. The product goes in and the next cycle begins, hands off.
        ManaContainer tank = Tank(T(24, 15));
        RunUntil(() => !processor.IsHolding, 2f);
        Assert.AreEqual(1, tank.Store.Amount);
        Assert.AreEqual(ManaProcessorStall.None, processor.Stall);
        RunUntil(() => processor.IsRunning, 4f); // Twelve units to draw at the network's 5/s
        Assert.AreEqual(1, processor.CyclesCompleted, "The next cycle began on its own");
        Assert.AreEqual(8, fireTank.Store.Amount);
    }

    [Test]
    public void PartialDeposit_LeavesTheRestHeld_StillStalled_EveryUnitConserved()
    {
        // The shipped rows make one unit; a row with a bigger product exercises the partial deposit.
        mana.Processes = new List<ManaProcessRow>
        {
            new ManaProcessRow(PlaceableType.CombinationChamber,
                new[] { new ManaAmount(Fire, 1), new ManaAmount(Water, 1) }, new[] { new ManaAmount(Air, 3) }, 10f, 2, false),
        };
        Tank(T(21, 15), Fire, 20);
        Tank(T(22, 15), Water, 20);
        CombinationChamber chamber = Chamber(T(23, 15));
        ManaProcessor processor = chamber.Processor;
        RunUntil(() => processor.CyclesCompleted == 1, 15f);
        Run(0.5f);
        Assert.AreEqual(3, processor.HeldOf(Air));

        manager.ManaContainerCapacity = 1;
        ManaContainer small = Tank(T(24, 15));
        Run(0.5f);
        Assert.AreEqual(1, small.Store.Amount);
        Assert.AreEqual(2, processor.HeldOf(Air));
        Assert.AreEqual(3, mana.TotalOf(Air), "Conserved across the chamber and the store");
        Assert.AreEqual(ManaProcessorStall.NoRoomForOutput, processor.Stall, "Still no room for the rest");

        manager.ManaContainerCapacity = 100;
        ManaContainer big = Tank(T(25, 15));
        RunUntil(() => !processor.IsHolding, 2f);
        Assert.AreEqual(2, big.Store.Amount);
        Assert.AreEqual(ManaProcessorStall.None, processor.Stall);
    }

    [Test]
    public void OffThePipe_NothingFeedsIt_AndItSaysWhatsMissing()
    {
        Tank(T(21, 15), Fire, 10);
        Assert.IsTrue(manager.TryPlaceBuilt(PlaceableType.CombinationChamber, T(23, 15), ImpTile, out Placeable p));
        ManaProcessor processor = p.GetComponent<CombinationChamber>().Processor;
        Run(2f);
        Assert.AreEqual(0, processor.StagedTotal, "No pipe on its tile: nothing reaches it");
        Assert.AreEqual("Missing Fire", processor.StallText);
    }

    // ---- selection ----

    [Test]
    public void Selection_DefaultsToTheFirstRow_SwitchesOnlyWhileIdleAndEmpty()
    {
        CombinationChamber chamber = Chamber(T(23, 15));
        ManaProcessor processor = chamber.Processor;
        Assert.AreEqual(0, chamber.SelectedIndex);
        Assert.AreEqual("6 Fire + 6 Water → 1 Air", processor.Row.Name);

        Assert.IsTrue(chamber.SelectProcess(1), "Idle and empty: switches");
        Assert.AreEqual("6 Air + 6 Water → 1 Storm", processor.Row.Name);
        Assert.IsFalse(chamber.SelectProcess(7), "No such row");
        Assert.AreEqual(1, chamber.SelectedIndex);

        Assert.AreEqual(6, processor.Stage(Water, 6), "Partly staged");
        Assert.IsFalse(chamber.SelectProcess(0), "Staged units would be stranded: refused");
        Assert.IsFalse(processor.CanSwitch);
        Assert.AreEqual(6, processor.Stage(Air, 10), "Only its demand");
        Assert.IsTrue(processor.IsRunning);
        Assert.IsFalse(chamber.SelectProcess(0), "Running: refused");
        processor.Tick(10f);
        Assert.AreEqual(1, processor.HeldOf(Storm));
        Assert.IsFalse(chamber.SelectProcess(0), "Holding product: refused");
        Assert.AreEqual(1, processor.TakeHeld(Storm, 1));
        Assert.IsTrue(chamber.SelectProcess(2), "Empty again: switches");
        Assert.AreEqual("6 Storm → 1 Lightning", processor.Row.Name);
    }

    [Test]
    public void QueuedSwitch_MidCycle_FinishesAndDepositsTheCycle_DrawsNothingNew_ThenSwitches()
    {
        ManaContainer fireTank = Tank(T(21, 15), Fire, 20);
        ManaContainer waterTank = Tank(T(22, 15), Water, 20);
        CombinationChamber chamber = Chamber(T(23, 15));
        ManaContainer airTank = Tank(T(24, 15), Air, 10);
        ManaProcessor processor = chamber.Processor;
        RunUntil(() => processor.IsRunning, 4f);

        Assert.IsFalse(chamber.SelectProcess(1), "Running: an immediate switch is refused");
        Assert.IsTrue(chamber.RequestProcess(1), "...but the switch can wait");
        Assert.AreEqual(1, chamber.PendingIndex);
        Assert.AreEqual(0, chamber.SelectedIndex, "Still running the first row");

        RunUntil(() => processor.CyclesCompleted == 1 && !processor.IsHolding, 15f);
        Assert.AreEqual(11, airTank.Store.Amount, "The cycle it had started came out in full");
        Assert.AreEqual(1, chamber.SelectedIndex, "Empty: switched");
        Assert.AreEqual(-1, chamber.PendingIndex);
        Assert.AreEqual(14, fireTank.Store.Amount, "No new Fire drawn for the old row");

        RunUntil(() => processor.IsRunning, 4f);
        Assert.AreEqual(5, airTank.Store.Amount, "The new row drew its 6 Air");
        Assert.AreEqual(8, waterTank.Store.Amount);
        Assert.AreEqual(14, fireTank.Store.Amount, "...and no Fire: it isn't one of the new row's inputs");
    }

    [Test]
    public void QueuedSwitch_WhileStalledWithABegunSet_FinishesTheSetFirst()
    {
        Tank(T(21, 15), Fire, 10);
        CombinationChamber chamber = Chamber(T(23, 15));
        Tank(T(24, 15), Air, 1);
        BuildPipe(T(22, 15));
        Run(3f);
        Assert.AreEqual("Missing Water", chamber.Processor.StallText);
        Assert.IsTrue(chamber.RequestProcess(2));
        Run(5f);
        Assert.AreEqual(0, chamber.SelectedIndex, "Its staged Fire is committed: it waits to finish that set");
        Assert.AreEqual(6, chamber.Processor.StagedOf(Fire), "Nothing is stranded or thrown away");
        Assert.AreEqual(10, mana.TotalOf(Fire));
    }

    // ---- the chain, and no conversion in transit ----

    [Test]
    public void EndToEnd_FireAndWaterToAir_AirAndWaterToStorm_StormToLightning_LandsInAContainer()
    {
        // Staged in code: each row makes one unit, so the Air and Storm containers start one cycle short of a set.
        Tank(T(21, 15), Fire, 20);
        Tank(T(22, 15), Water, 20);
        CombinationChamber chamber = Chamber(T(23, 15));
        Tank(T(24, 15), Storm, 5);
        Tank(T(25, 15), Air, 5);
        Tank(T(26, 15));
        ManaProcessor processor = chamber.Processor;

        void RunOneCycleThenSwitch(int completed, int nextRow)
        {
            RunUntil(() => processor.CyclesCompleted == completed && !processor.IsHolding && processor.IsEmpty, 20f);
            Assert.IsTrue(chamber.SelectProcess(nextRow), $"Idle and empty after cycle {completed}: switch to row {nextRow}");
        }

        RunOneCycleThenSwitch(1, 1);
        Assert.AreEqual(6, mana.TotalOf(Air), "One Air made");
        RunOneCycleThenSwitch(2, 2);
        Assert.AreEqual(0, mana.TotalOf(Air), "Six Air went into the Storm");
        Assert.AreEqual(6, mana.TotalOf(Storm), "One Storm made");
        RunUntil(() => processor.CyclesCompleted == 3 && !processor.IsHolding, 20f);

        Assert.AreEqual(0, mana.TotalOf(Storm), "Six Storm went into the Lightning");
        Assert.AreEqual(1, mana.TotalOf(Lightning));
        Assert.IsTrue(mana.IsDiscovered(Lightning));
        bool inAContainer = false;
        foreach (ManaContainer tank in manager.GetManaContainers())
            if (tank.Store.Type == Lightning && tank.Store.Amount == 1) inAContainer = true;
        Assert.IsTrue(inAContainer, "Lightning landed in a container");
        Assert.AreEqual(20 - 6, mana.TotalOf(Fire), "Fire: only the first row's 6");
        Assert.AreEqual(20 - 12, mana.TotalOf(Water), "Water: 6 for Air, 6 for Storm");
        CollectionAssert.AreEqual(new[] { Fire, Water, Air, Storm, Lightning }, mana.DiscoveredTypes(), "Catalog order");
    }

    [Test]
    public void NoConversionInTransit_PipesMoveFireAndWater_PerTypeTotalsNeverChange()
    {
        PipedAccumulator(T(21, 15), Fire, 20);
        PipedAccumulator(T(22, 15), Water, 20);
        Tank(T(23, 15));
        Tank(T(24, 15), Water, 3);
        Tank(T(25, 15));
        for (int i = 0; i < 1500; i++) // 30 s
        {
            mana.Tick(Dt);
            Assert.AreEqual(20, mana.TotalOf(Fire), "Fire never changes in a pipe");
            Assert.AreEqual(23, mana.TotalOf(Water), "Nor Water");
            Assert.AreEqual(43, mana.TotalAll(), "And nothing else appears");
        }
    }

    // ---- combination on the map (the wild route) ----

    [Test]
    public void MapCollision_3FireBeside3Water_Becomes1Air_TheInputsConsumedExactly()
    {
        Carve(T(10, 6), T(11, 6));
        Put(T(10, 6), Fire, 3);
        Put(T(11, 6), Water, 3);

        Assert.AreEqual(1, mana.CombineStep(), "One reaction");
        Assert.AreEqual(0, OnBoard(Fire), "Exactly 3 Fire consumed");
        Assert.AreEqual(0, OnBoard(Water), "Exactly 3 Water consumed");
        Assert.AreEqual(1, OnBoard(Air), "Exactly 1 Air produced");
        Assert.AreEqual(Air, mana.TypeAt(T(10, 6)), "On the first reacting tile, freed by the reaction");
        Assert.AreEqual(1, mana.UnitCount);
        Assert.AreEqual(0, mana.CombineStep(), "Nothing left to react");
    }

    [Test]
    public void MapCollision_BelowTheRowsAmounts_OrNotSideBySide_NothingConverts()
    {
        Carve(T(10, 6), T(11, 6), T(10, 8), T(11, 8), T(10, 10), T(11, 11));
        Put(T(10, 6), Fire, 2);  // One short of 3
        Put(T(11, 6), Water, 5);
        Put(T(10, 8), Fire, 5);
        Put(T(11, 8), Water, 2); // One short of 3
        Put(T(10, 10), Fire, 5);
        Put(T(11, 11), Water, 5); // Diagonal: not a 4-neighbour
        for (int i = 0; i < 5; i++) Assert.AreEqual(0, mana.CombineStep());
        Assert.AreEqual(12, OnBoard(Fire));
        Assert.AreEqual(12, OnBoard(Water));
        Assert.AreEqual(0, OnBoard(Air));
    }

    [Test]
    public void MapCollision_RemaindersStay_TheProductTakesAnOpenNeighbour()
    {
        Carve(T(10, 6), T(11, 6), T(10, 7));
        Put(T(10, 6), Fire, 5);
        Put(T(11, 6), Water, 4);

        Assert.AreEqual(1, mana.CombineStep());
        Assert.AreEqual(2, mana.UnitsAt(T(10, 6)), "5 − 3 Fire stay");
        Assert.AreEqual(Fire, mana.TypeAt(T(10, 6)));
        Assert.AreEqual(1, mana.UnitsAt(T(11, 6)), "4 − 3 Water stay");
        Assert.AreEqual(Water, mana.TypeAt(T(11, 6)));
        Assert.AreEqual(Air, mana.TypeAt(T(10, 7)), "Neither reacting tile is free: the open neighbour takes the Air");
        Assert.AreEqual(1, mana.UnitsAt(T(10, 7)));
    }

    [Test]
    public void MapCollision_NoRoomForTheProduct_Waits_ThenReactsWhenRoomOpens()
    {
        Carve(T(10, 6), T(11, 6));
        Put(T(10, 6), Fire, 5);
        Put(T(11, 6), Water, 5);
        Assert.AreEqual(0, mana.CombineStep(), "2 Fire and 2 Water would remain, and every neighbour is rock: it waits");
        Assert.AreEqual(5, mana.UnitsAt(T(10, 6)));
        Assert.AreEqual(5, mana.UnitsAt(T(11, 6)));

        Carve(T(11, 7));
        Assert.AreEqual(1, mana.CombineStep(), "Room opened: it reacts");
        Assert.AreEqual(Air, mana.TypeAt(T(11, 7)));
        Assert.AreEqual(2, OnBoard(Fire));
        Assert.AreEqual(2, OnBoard(Water));
    }

    [Test]
    public void MapCollision_AirMeetingWater_MakesStorm()
    {
        Carve(T(10, 6), T(11, 6));
        Put(T(10, 6), Air, 3);
        Put(T(11, 6), Water, 3);
        Assert.AreEqual(1, mana.CombineStep());
        Assert.AreEqual(0, OnBoard(Air));
        Assert.AreEqual(0, OnBoard(Water));
        Assert.AreEqual(1, OnBoard(Storm), "3 Air + 3 Water → 1 Storm");
    }

    [Test]
    public void MapCollision_ATileReactsAtMostOncePerTick()
    {
        // A 1 + 1 row, so one pair of tiles holds enough for two reactions.
        mana.Processes = new List<ManaProcessRow>
        {
            new ManaProcessRow(PlaceableType.CombinationChamber,
                new[] { new ManaAmount(Fire, 1), new ManaAmount(Water, 1) }, new[] { new ManaAmount(Air, 1) }, 10f),
        };
        Carve(T(10, 6), T(11, 6), T(10, 7));
        Put(T(10, 6), Fire, 2);
        Put(T(11, 6), Water, 2);

        Assert.AreEqual(1, mana.CombineStep(), "The pair reacts once, then it's done for the tick");
        Assert.AreEqual(1, OnBoard(Fire));
        Assert.AreEqual(1, OnBoard(Water));
        Assert.AreEqual(Air, mana.TypeAt(T(10, 7)), "Neither reacting tile freed: the product went beside them");
        Assert.AreEqual(1, mana.CombineStep(), "Next tick, again");
        Assert.AreEqual(0, OnBoard(Fire));
        Assert.AreEqual(0, OnBoard(Water));
        Assert.AreEqual(2, OnBoard(Air));
    }

    [Test]
    public void MapCollision_SingleInputStormRow_IsOffTheMap_AndTheFlagTurnsARowOff()
    {
        Carve(T(10, 6), T(11, 6), T(13, 6), T(14, 6));
        Put(T(10, 6), Storm, 5);
        Assert.AreEqual(0, mana.CombineStep(), "Storm on its own never decays into Lightning");
        Assert.AreEqual(5, OnBoard(Storm));

        Put(T(13, 6), Fire, 3);
        Put(T(14, 6), Water, 3);
        mana.RowsFor(PlaceableType.CombinationChamber)[0].combinesOnMap = false;
        Assert.AreEqual(0, mana.CombineStep(), "Flagged off: the chamber's route only");
        Assert.AreEqual(3, OnBoard(Fire));
    }

    [Test]
    public void MapCollision_OnTheClock_FrontsMeetAndReact_EveryUnitAccountedForAtEveryTick()
    {
        // A closed corridor, Fire packed at the west end and Water at the east: the flow spreads them and they meet.
        var corridor = new List<Vector2Int>();
        for (int x = 8; x <= 17; x++) corridor.Add(T(x, 6));
        Carve(corridor.ToArray());
        foreach (int x in new[] { 8, 9, 10 }) Put(T(x, 6), Fire, 5);
        foreach (int x in new[] { 15, 16, 17 }) Put(T(x, 6), Water, 5);
        const int f0 = 15, w0 = 15;

        for (int i = 0; i < 3000; i++) // 60 s
        {
            mana.Tick(Dt);
            // Row 1 fires a1 times, row 2 a2 times: Storm = a2, Air = a1 − 3·a2.
            int a2 = OnBoard(Storm);
            int a1 = OnBoard(Air) + 3 * a2;
            Assert.AreEqual(f0 - 3 * a1, OnBoard(Fire), $"tick {i}: Fire leaves only 3 at a time, by reaction");
            Assert.AreEqual(w0 - 3 * a1 - 3 * a2, OnBoard(Water), $"tick {i}: so does Water");
            Assert.AreEqual(0, OnBoard(Lightning), "Storm never decays on the map");
        }
        Assert.Greater(OnBoard(Air) + OnBoard(Storm), 0, "The fronts met and reacted");
    }

    // ---- shipped data and presentation ----

    [Test]
    public void ShippedData_CostBuildTime_NoPowerRow_MagicCategory_StatLine()
    {
        foreach (MaterialType material in new[] { MaterialType.Stone, MaterialType.Glimmerstone, MaterialType.Deepstone })
        {
            Assert.IsTrue(manager.TryGetCost(PlaceableType.CombinationChamber, material, out MaterialAmount cost), $"{material}");
            Assert.AreEqual(8, cost.amount);
        }
        Assert.AreEqual(10f, manager.BuildSecondsFor(PlaceableType.CombinationChamber));
        Assert.IsFalse(manager.TryGetPower(PlaceableType.CombinationChamber, out _), "Unattended and unpowered in v1");
        Assert.IsTrue(BuildPresentation.TryGetCategory(PlaceableType.CombinationChamber, out BuildCategory category));
        Assert.AreEqual(BuildCategory.Magic, category);
        Assert.AreEqual("Combination Chamber", BuildPresentation.DisplayName(PlaceableType.CombinationChamber));
        Assert.AreEqual("Runs one of 3 processes · 10 s cycles",
            DetailCard.StatLine(BuildStat.ManaProcesses, manager, PlaceableType.CombinationChamber, MaterialType.Stone));

        Assert.IsTrue(manager.TryPlace(PlaceableType.CombinationChamber, T(23, 15), ImpTile, out Placeable site));
        Assert.Zero(mana.Processors().Count, "A site isn't a host yet");
        site.CompleteConstruction();
        Assert.AreEqual(1, mana.Processors().Count, "Registered on build-complete");
        Assert.AreSame(site.GetComponent<CombinationChamber>(), mana.ProcessorAt(T(23, 15)));
    }

    [Test]
    public void CardLines_ProcessStateStagedHeldAndNetwork()
    {
        Tank(T(21, 15), Fire, 10);
        Tank(T(22, 15));
        CombinationChamber chamber = Chamber(T(23, 15));
        Run(3f);
        List<string> lines = DetailCard.ChamberLines(chamber.Processor, true, mana.PortsOnNetworkOf(chamber.Tile));
        CollectionAssert.AreEqual(new[]
        {
            "Process: 6 Fire + 6 Water → 1 Air",
            "Stalled: Missing Water",
            "Staged: Fire 6/6 · Water 0/6",
            "On a pipe · 2 stores · Fire 4 · 1 empty",
        }, lines);
        Assert.IsTrue(chamber.RequestProcess(2), "Partly staged: the switch waits");
        lines = DetailCard.ChamberLines(chamber.Processor, true, mana.PortsOnNetworkOf(chamber.Tile));
        Assert.AreEqual("Switching to 6 Storm → 1 Lightning after this cycle", lines[lines.Count - 1]);
        Assert.IsTrue(chamber.RequestProcess(0), "Asking for the running row cancels it");
        Assert.IsNull(chamber.Processor.PendingRow);

        chamber.Processor.Stage(Water, 6);
        chamber.Processor.Tick(4f);
        lines = DetailCard.ChamberLines(chamber.Processor, false, null);
        Assert.AreEqual("Running 40% · 4.0/10 s", lines[1]);
        Assert.AreEqual("Not on a pipe · lay pipe onto its tile to connect it", lines[2]);
        chamber.Processor.Tick(6f);
        lines = DetailCard.ChamberLines(chamber.Processor, false, null);
        Assert.AreEqual("Holding: Air 1", lines[2]);
    }

    [Test]
    public void Presentation_SolidInMagic_OverlayReadsTheProduct_NoPipeLineOnItsTile()
    {
        Assert.IsTrue(PowerOverlay.SeesThrough(ViewMode.Magic, false, false));
        CombinationChamber chamber = Chamber(T(23, 15));
        boardRenderer.SetViewMode(ViewMode.Magic);
        manager.RefreshSeeThrough();
        Assert.IsFalse(chamber.GetComponent<Placeable>().IsSeeThrough, "A mana building: solid in the Magic view");

        Assert.AreEqual(boardRenderer.StoreOverlayColor(null), boardRenderer.ProcessorOverlayColor(chamber.Processor), "Idle: neutral");
        chamber.Processor.Stage(Fire, 6);
        chamber.Processor.Stage(Water, 6);
        Assert.AreEqual(mana.ColorOf(Air), boardRenderer.ProcessorOverlayColor(chamber.Processor), "Running: its product's color");
        Assert.IsNull(TileHover.PipeLine(mana, T(23, 15)), "The building is what the tile is");
    }
}
