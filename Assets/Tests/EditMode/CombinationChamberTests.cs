using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// #146: the process table and its pilot host, the Combination Chamber. Table resolution by host; the new made types;
/// cycle accounting (exactly the row's inputs consumed at the start, exactly its outputs produced at the end, every unit
/// accounted for in between); the draw order (containers before buffers, buffers never below the reserve); both stall
/// reasons and the hands-off resume; a partial deposit; process selection; the full chain to Lightning; no conversion
/// in pipes; and the chamber's shipped data and presentation.
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

    // ---- the process table ----

    [Test]
    public void ProcessTable_TheThreeChamberRows_ResolveExactly_FilteredByHost()
    {
        List<ManaProcessRow> rows = mana.RowsFor(PlaceableType.CombinationChamber);
        Assert.AreEqual(3, rows.Count);
        void AssertRow(ManaProcessRow row, (string, int)[] inputs, (string, int)[] outputs, string name)
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
            Assert.AreEqual(name, row.Name, "Named from its data");
        }
        AssertRow(rows[0], new[] { (Fire, 2), (Water, 2) }, new[] { (Air, 3) }, "2 Fire + 2 Water → 3 Air");
        AssertRow(rows[1], new[] { (Air, 2), (Water, 2) }, new[] { (Storm, 3) }, "2 Air + 2 Water → 3 Storm");
        AssertRow(rows[2], new[] { (Storm, 3) }, new[] { (Lightning, 2) }, "3 Storm → 2 Lightning");
        Assert.AreEqual(2, rows[0].InputOf(Fire));
        Assert.AreEqual(0, rows[0].InputOf(Storm));

        Assert.IsEmpty(mana.RowsFor(PlaceableType.Accumulator), "A host with no rows has none");
        Assert.IsEmpty(mana.RowsFor(PlaceableType.LairCot));

        // Rows key by host: a later host's rows never leak into the chamber's.
        mana.Processes.Add(new ManaProcessRow(PlaceableType.FurnaceGenerator, new[] { new ManaAmount(Fire, 1) }, new ManaAmount[0], 5f));
        Assert.AreEqual(3, mana.RowsFor(PlaceableType.CombinationChamber).Count);
        Assert.AreEqual(1, mana.RowsFor(PlaceableType.FurnaceGenerator).Count);
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
    public void Cycle_ConsumesExactlyTheRowsInputs_ProducesExactlyItsOutputs_At10s_EveryUnitAccountedFor()
    {
        Tank(T(21, 15), Fire, 10);
        Tank(T(22, 15), Water, 10);
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
            Assert.AreEqual(20 - 4 * started, mana.TotalOf(Fire) + mana.TotalOf(Water), $"{t:0.00} s: inputs leave only at a cycle's start");
            Assert.AreEqual(mana.TotalOf(Fire), mana.TotalOf(Water), "Two of each");
            Assert.AreEqual(3 * processor.CyclesCompleted, mana.TotalOf(Air), $"{t:0.00} s: Air appears only at a cycle's end");
            if (startedAt < 0f && processor.IsRunning) startedAt = t;
            if (processor.CyclesCompleted == 1) completedAt = t;
        }
        Assert.Greater(startedAt, 0f, "It drew a full set and started");
        Assert.Less(startedAt, 1.5f, "Four units at 5/s: staged within a second or so");
        Assert.AreEqual(10f, completedAt - startedAt, 2 * Dt, "The row's cycle: 10 s");
        Assert.AreEqual(8, mana.TotalOf(Fire), "Exactly 2 Fire consumed");
        Assert.AreEqual(8, mana.TotalOf(Water), "Exactly 2 Water consumed");
        Assert.AreEqual(3, mana.TotalOf(Air), "Exactly 3 Air produced");
        Assert.IsTrue(mana.IsDiscovered(Air), "Held anywhere: discovered the moment the cycle completed");

        Run(0.5f);
        Assert.AreEqual(3, airTank.Store.Amount, "Deposited into the empty container, which adopted Air");
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
        ManaStore fireBuffer = StoreWith(20, Fire, 7);
        ManaStore waterBuffer = StoreWith(20, Water, 9);
        ManaStore emptyTank = new ManaStore(100);

        // Budget 1: the container's Fire goes first.
        Assert.AreEqual(1, ManaFlow.Draw(new[] { processor }, new[] { emptyTank, fireTank }, new[] { fireBuffer, waterBuffer }, 5, 1));
        Assert.AreEqual(0, fireTank.Amount, "From the container first");
        Assert.AreEqual(7, fireBuffer.Amount);
        Assert.AreEqual(1, processor.StagedOf(Fire));

        Assert.AreEqual(3, ManaFlow.Draw(new[] { processor }, new[] { emptyTank, fireTank }, new[] { fireBuffer, waterBuffer }, 5, 100));
        Assert.AreEqual(6, fireBuffer.Amount, "Then the buffer, for the rest");
        Assert.AreEqual(7, waterBuffer.Amount);
        Assert.IsTrue(processor.IsRunning, "A full set staged: consumed, running");
        Assert.AreEqual(0, emptyTank.Amount, "An empty container supplies nothing");

        var second = new ManaProcessor(row);
        ManaStore atReserve = StoreWith(20, Fire, 5);
        Assert.AreEqual(2, ManaFlow.Draw(new[] { second }, new ManaStore[0], new[] { atReserve, waterBuffer }, 5, 100));
        Assert.AreEqual(5, atReserve.Amount, "At the reserve: never drawn below it");
        Assert.AreEqual(0, second.StagedOf(Fire));
        Assert.AreEqual(2, second.StagedOf(Water), "Partial staging is held");
        Assert.AreEqual(5, waterBuffer.Amount);
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
        Assert.AreEqual(2, processor.StagedOf(Fire), "Its Fire is staged and held, committed to the chamber");
        Assert.AreEqual(8, fireTank.Store.Amount);
        Assert.AreEqual(10, mana.TotalOf(Fire), "Staged Fire still counts");
        Assert.IsFalse(processor.IsRunning);

        // The fix is elsewhere: an accumulator with Water joins the network. Nothing touches the chamber.
        PipedAccumulator(T(23, 14), Water, 9);
        Run(0.5f);
        Assert.IsTrue(processor.IsRunning, "Supplied: the cycle started on its own");
        Assert.AreEqual(ManaProcessorStall.None, processor.Stall);
        RunUntil(() => airTank.Store.Amount == 4, 12f);
        Assert.AreEqual(1, processor.CyclesCompleted);
    }

    [Test]
    public void NoRoomForOutput_HoldsTheProduct_StartsNoNewCycle_APartialDepositConserves_AndRoomResumesIt()
    {
        ManaContainer fireTank = Tank(T(21, 15), Fire, 20);
        ManaContainer waterTank = Tank(T(22, 15), Water, 20);
        CombinationChamber chamber = Chamber(T(23, 15));
        ManaProcessor processor = chamber.Processor;

        RunUntil(() => processor.CyclesCompleted == 1, 15f);
        Run(0.5f);
        Assert.AreEqual(3, processor.HeldOf(Air), "Nowhere to put it: held");
        Assert.AreEqual(ManaProcessorStall.NoRoomForOutput, processor.Stall);
        Assert.AreEqual("No room for output", processor.StallText);
        Assert.AreEqual(3, mana.TotalOf(Air), "Held product counts in the totals");
        Assert.IsTrue(mana.IsDiscovered(Air));

        Run(25f);
        Assert.AreEqual(1, processor.CyclesCompleted, "No new cycle while holding");
        Assert.AreEqual(0, processor.StagedTotal, "...and nothing drawn for one");
        Assert.AreEqual(18, fireTank.Store.Amount);
        Assert.AreEqual(18, waterTank.Store.Amount);

        // A one-unit container: a partial deposit, the rest still held, still stalled, every unit accounted for.
        manager.ManaContainerCapacity = 1;
        ManaContainer small = Tank(T(24, 15));
        Run(0.5f);
        Assert.AreEqual(1, small.Store.Amount);
        Assert.AreEqual(2, processor.HeldOf(Air));
        Assert.AreEqual(3, mana.TotalOf(Air), "Conserved across the chamber and the store");
        Assert.AreEqual(ManaProcessorStall.NoRoomForOutput, processor.Stall, "Still no room for the rest");

        // Room: a big container. The rest goes in and the next cycle begins, hands off.
        manager.ManaContainerCapacity = 100;
        ManaContainer big = Tank(T(25, 15));
        RunUntil(() => !processor.IsHolding, 2f);
        Assert.AreEqual(2, big.Store.Amount);
        Assert.AreEqual(ManaProcessorStall.None, processor.Stall);
        RunUntil(() => processor.IsRunning, 3f); // Four units to draw at the network's 5/s
        Assert.AreEqual(1, processor.CyclesCompleted, "The next cycle began on its own");
        Assert.AreEqual(16, fireTank.Store.Amount);
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
        Assert.AreEqual("2 Fire + 2 Water → 3 Air", processor.Row.Name);

        Assert.IsTrue(chamber.SelectProcess(1), "Idle and empty: switches");
        Assert.AreEqual("2 Air + 2 Water → 3 Storm", processor.Row.Name);
        Assert.IsFalse(chamber.SelectProcess(7), "No such row");
        Assert.AreEqual(1, chamber.SelectedIndex);

        Assert.AreEqual(2, processor.Stage(Water, 2), "Partly staged");
        Assert.IsFalse(chamber.SelectProcess(0), "Staged units would be stranded: refused");
        Assert.IsFalse(processor.CanSwitch);
        Assert.AreEqual(2, processor.Stage(Air, 5), "Only its demand");
        Assert.IsTrue(processor.IsRunning);
        Assert.IsFalse(chamber.SelectProcess(0), "Running: refused");
        processor.Tick(10f);
        Assert.AreEqual(3, processor.HeldOf(Storm));
        Assert.IsFalse(chamber.SelectProcess(0), "Holding product: refused");
        Assert.AreEqual(3, processor.TakeHeld(Storm, 3));
        Assert.IsTrue(chamber.SelectProcess(2), "Empty again: switches");
        Assert.AreEqual("3 Storm → 2 Lightning", processor.Row.Name);
    }

    // ---- the chain, and no conversion in transit ----

    [Test]
    public void EndToEnd_FireAndWaterToAir_AirAndWaterToStorm_StormToLightning_LandsInAContainer()
    {
        Tank(T(21, 15), Fire, 20);
        Tank(T(22, 15), Water, 20);
        CombinationChamber chamber = Chamber(T(23, 15));
        Tank(T(24, 15));
        Tank(T(25, 15));
        ManaProcessor processor = chamber.Processor;

        void RunOneCycleThenSwitch(int completed, int nextRow)
        {
            RunUntil(() => processor.CyclesCompleted == completed && !processor.IsHolding && processor.IsEmpty, 20f);
            Assert.IsTrue(chamber.SelectProcess(nextRow), $"Idle and empty after cycle {completed}: switch to row {nextRow}");
        }

        RunOneCycleThenSwitch(1, 1);
        Assert.AreEqual(3, mana.TotalOf(Air));
        RunOneCycleThenSwitch(2, 2);
        Assert.AreEqual(1, mana.TotalOf(Air), "Two Air went into the Storm");
        Assert.AreEqual(3, mana.TotalOf(Storm));
        RunUntil(() => processor.CyclesCompleted == 3 && !processor.IsHolding, 20f);

        Assert.AreEqual(0, mana.TotalOf(Storm), "Three Storm went into the Lightning");
        Assert.AreEqual(2, mana.TotalOf(Lightning));
        Assert.IsTrue(mana.IsDiscovered(Lightning));
        bool inAContainer = false;
        foreach (ManaContainer tank in manager.GetManaContainers())
            if (tank.Store.Type == Lightning && tank.Store.Amount == 2) inAContainer = true;
        Assert.IsTrue(inAContainer, "Lightning landed in a container");
        Assert.AreEqual(20 - 2, mana.TotalOf(Fire), "Fire: only the first row's 2");
        Assert.AreEqual(20 - 4, mana.TotalOf(Water), "Water: 2 for Air, 2 for Storm");
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
        Run(1f);
        List<string> lines = DetailCard.ChamberLines(chamber.Processor, true, mana.PortsOnNetworkOf(chamber.Tile));
        CollectionAssert.AreEqual(new[]
        {
            "Process: 2 Fire + 2 Water → 3 Air",
            "Stalled: Missing Water",
            "Staged: Fire 2/2 · Water 0/2",
            "On a pipe · 2 stores · Fire 8 · 1 empty",
            "Process locked until it's idle and empty",
        }, lines);

        chamber.Processor.Stage(Water, 2);
        chamber.Processor.Tick(4f);
        lines = DetailCard.ChamberLines(chamber.Processor, false, null);
        Assert.AreEqual("Running 40% · 4.0/10 s", lines[1]);
        Assert.AreEqual("Not on a pipe · lay pipe onto its tile to connect it", lines[2]);
        chamber.Processor.Tick(6f);
        lines = DetailCard.ChamberLines(chamber.Processor, false, null);
        Assert.AreEqual("Holding: Air 3", lines[2]);
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
        chamber.Processor.Stage(Fire, 2);
        chamber.Processor.Stage(Water, 2);
        Assert.AreEqual(mana.ColorOf(Air), boardRenderer.ProcessorOverlayColor(chamber.Processor), "Running: its product's color");
        Assert.IsNull(TileHover.PipeLine(mana, T(23, 15)), "The building is what the tile is");
    }
}
