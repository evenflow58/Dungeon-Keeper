using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// #143: node enable toggles (out of the grid math entirely; a disabled wheel isn't tended, a disabled trap goes back to
/// the imp's pool with no marker), the Furnace Generator (paid burn intervals bought with one unit of global fuel, a
/// seamless rebuy, out of fuel and back, paused while disabled, exact spend counts), the Grow-Lamp (Chebyshev coverage,
/// the best single multiplier, live with power and toggles, plots really grow faster), and the cards and shipped data.
/// </summary>
public class FurnaceLampToggleTests
{
    private static readonly Vector2Int ImpTile = new Vector2Int(24, 16); // Imp spawn: cavern centre; cavern (21,13)-(26,18)
    private static readonly Vector2Int WheelTile = new Vector2Int(22, 14);
    private static readonly Vector2Int LinkTile = new Vector2Int(23, 14);
    private static readonly Vector2Int TrapTile = new Vector2Int(24, 14);
    private const float Dt = 0.02f;

    private GameObject managerGo;
    private GameObject impGo;
    private DungeonBoard board;
    private PlacementManager manager;
    private PowerGrid grid;
    private Imp imp;
    private ImpDigger digger;

    /// <summary>A fuel store with a plain counter, so burn timing is tested apart from where fuel lives.</summary>
    private sealed class CountingStore : IFuelStore
    {
        public int Units;
        public int Spent;
        public string LastId;

        public int FuelCount(string fuelId) => Units;

        public bool TrySpendFuel(string fuelId)
        {
            LastId = fuelId;
            if (Units <= 0) return false;
            Units--;
            Spent++;
            return true;
        }
    }

    [SetUp]
    public void SetUp()
    {
        managerGo = new GameObject("TestDungeonManager");
        board = managerGo.AddComponent<DungeonBoard>();
        board.InitializeBoard();
        var boardRenderer = managerGo.AddComponent<BoardRenderer>();
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

    // The wheel produces whenever built (no imp needed), so toggle rules are seen apart from tending.
    private void UseUntendedWheel()
    {
        var rows = new List<PlaceablePower>(manager.Power);
        rows[rows.FindIndex(r => r.type == PlaceableType.ImpWheel)] = new PlaceablePower(PlaceableType.ImpWheel, PowerRole.Producer, 40);
        manager.Power = rows;
    }

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

    // A node connects only through conduit on its own tile: the run passes under both ends.
    private void WireLink() => BuildConduit(WheelTile, LinkTile, TrapTile);

    // A furnace site switched to a counting store, then built (its first purchase happens at build-complete).
    private FurnaceGenerator BuiltFurnace(Vector2Int tile, CountingStore store, float retrySeconds = 1f)
    {
        Assert.IsTrue(manager.TryPlace(PlaceableType.FurnaceGenerator, tile, ImpTile, out Placeable site));
        var furnace = site.GetComponent<FurnaceGenerator>();
        furnace.Initialize(store, PlacementManager.FoodFuelId, 12f, retrySeconds);
        site.CompleteConstruction();
        return furnace;
    }

    private static void TickFor(FurnaceGenerator furnace, float seconds, Action<float> eachStep = null)
    {
        int steps = Mathf.RoundToInt(seconds / Dt);
        for (int i = 0; i < steps; i++)
        {
            furnace.Tick(Dt);
            eachStep?.Invoke((i + 1) * Dt);
        }
    }

    private MushroomPlot BuiltPlot(Vector2Int tile, int food)
    {
        Assert.IsTrue(manager.TryPlaceBuilt(PlaceableType.MushroomPlot, tile, ImpTile, out Placeable p), $"Plot at {tile}");
        var plot = p.GetComponent<MushroomPlot>();
        plot.Manager = null; // Stage the food at ×1, whatever lamps are about
        plot.Tick(food * plot.GrowthSecondsPerFood);
        plot.Manager = manager;
        Assert.AreEqual(food, plot.FoodCount);
        return plot;
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

    private static Vector2Int T(int x, int y) => new Vector2Int(x, y);

    // ---- 1. Toggles ----

    [Test]
    public void Sum_LeavesDisabledNodesOut_ProducerAndConsumer()
    {
        var comps = PowerGrid.Components(new[] { T(4, 5), T(5, 5), T(6, 5), T(7, 5), T(5, 6), T(6, 6) });
        var states = new[]
        {
            new PowerGrid.NodeState(T(4, 5), PowerRole.Producer, 40, true),
            new PowerGrid.NodeState(T(7, 5), PowerRole.Producer, 100, true, enabled: false), // Off: no supply
            new PowerGrid.NodeState(T(5, 6), PowerRole.Consumer, 10, true),
            new PowerGrid.NodeState(T(6, 6), PowerRole.Consumer, 20, true, enabled: false),  // Off: no demand
        };
        PowerGrid.Sum(states, comps, 0, out int supply, out int demand);
        Assert.AreEqual(40, supply);
        Assert.AreEqual(10, demand);

        Assert.IsTrue(PowerGrid.IsNodePowered(true, true, 40, 10));
        Assert.IsFalse(PowerGrid.IsNodePowered(false, true, 40, 10), "A disabled node is never powered");
        Assert.IsFalse(PowerNode.ShowsUnpoweredMarker(true, PowerRole.Consumer, false, true, false), "Off by choice: no marker");
        Assert.IsTrue(PowerNode.ShowsUnpoweredMarker(true, PowerRole.Consumer, true, true, false));
    }

    [Test]
    public void DisablingTheProducer_DropsSupplyAtOnce_TheConsumerBrownsOut_ReenablingRestores()
    {
        UseUntendedWheel();
        PowerNode wheel = PlaceBuiltNode(PlaceableType.ImpWheel, WheelTile);
        PowerNode trap = PlaceBuiltNode(PlaceableType.SpikeTrap, TrapTile);
        WireLink();
        Assert.IsTrue(wheel.Enabled, "Default enabled");
        Assert.IsTrue(trap.IsPowered);

        int changed = 0;
        wheel.EnabledChanged += () => changed++;
        wheel.SetEnabled(false);
        Assert.AreEqual(1, changed);
        Assert.AreEqual(0, trap.Totals.Supply, "Immediately: supply is a live query");
        Assert.AreEqual(10, trap.Totals.Demand);
        Assert.IsFalse(trap.IsPowered);
        Assert.IsTrue(trap.ShowsMarker, "The consumer reacts: browned out");
        Assert.IsTrue(wheel.IsConnected, "Topology is unchanged");

        wheel.SetEnabled(true);
        Assert.AreEqual(40, trap.Totals.Supply);
        Assert.IsTrue(trap.IsPowered);
        Assert.IsFalse(trap.ShowsMarker);
    }

    [Test]
    public void DisablingAConsumer_DropsItsDemand_ItIsNeverPowered_AndShowsNoMarker()
    {
        UseUntendedWheel();
        PowerNode wheel = PlaceBuiltNode(PlaceableType.ImpWheel, WheelTile);
        PowerNode trap = PlaceBuiltNode(PlaceableType.SpikeTrap, TrapTile);
        PowerNode lamp = PlaceBuiltNode(PlaceableType.GrowLamp, T(23, 15)); // A spur off the link at (23,14)
        WireLink();
        BuildConduit(T(23, 15));
        Assert.IsTrue(lamp.IsConnected);
        Assert.AreEqual(30, lamp.Totals.Demand, "Trap 10 + lamp 20");
        Assert.IsTrue(lamp.IsPowered);

        lamp.SetEnabled(false);
        Assert.AreEqual(10, trap.Totals.Demand, "The lamp's 20 W left the grid");
        Assert.IsFalse(lamp.IsPowered, "A disabled node is never powered");
        Assert.IsFalse(lamp.ShowsMarker);

        wheel.SetEnabled(false); // Now the grid can't cover anyone: the enabled trap shows the marker, the disabled lamp doesn't
        Assert.IsTrue(trap.ShowsMarker);
        Assert.IsFalse(lamp.ShowsMarker, "Off by choice isn't a fault");

        wheel.SetEnabled(true);
        lamp.SetEnabled(true);
        Assert.AreEqual(30, lamp.Totals.Demand, "Re-enabled: back on the books");
        Assert.IsTrue(lamp.IsPowered);
    }

    [Test]
    public void ADisabledTrap_NeverSelfRearms_ItSitsInTheImpPool_AndTheImpRearmsIt()
    {
        UseUntendedWheel();
        PlaceBuiltNode(PlaceableType.ImpWheel, WheelTile);
        PowerNode node = PlaceBuiltNode(PlaceableType.SpikeTrap, TrapTile);
        WireLink();
        var trap = node.GetComponent<SpikeTrap>();
        Assert.IsTrue(trap.TryTrigger(out _));
        Assert.IsTrue(trap.SelfRearmOwns, "Powered and enabled: the grid would rearm it");

        node.SetEnabled(false);
        Assert.IsFalse(trap.IsPowered);
        Assert.IsFalse(trap.SelfRearmOwns, "Off: back in the imp's rearm pool");
        Assert.IsFalse(node.ShowsMarker);

        StepUntil(() => trap.IsArmed, 20f, "the imp rearms the disabled trap");
        Assert.AreEqual(0f, trap.PoweredRearmProgress, "The grid never touched it");
    }

    [Test]
    public void ADisabledWheel_IsNeverTended_AndATendedWheelSwitchedOffIsLeft()
    {
        PowerNode wheel = PlaceBuiltNode(PlaceableType.ImpWheel, WheelTile); // The shipped row: needs an imp
        StepUntil(() => digger.IsOperating, 5f, "tending");

        wheel.SetEnabled(false);
        StepUntil(() => !digger.IsOperating && digger.CurrentWheel == null, 1f, "the imp leaves a switched-off wheel");
        Assert.IsFalse(wheel.IsActive);
        Assert.IsFalse(ImpDigger.TrySelectOperate(board, manager.GetBuiltWheels(), digger, imp.CurrentTile, out _, out _),
            "Not an Operate candidate");
        for (int i = 0; i < 100; i++) Step();
        Assert.IsNull(digger.CurrentWheel);

        wheel.SetEnabled(true);
        StepUntil(() => digger.IsOperating, 5f, "back to the wheel");
    }

    // ---- 2. Furnace ----

    [Test]
    public void Furnace_BuysItsFirstIntervalOnBuildComplete_StockPermitting()
    {
        var store = new CountingStore { Units = 3 };
        FurnaceGenerator furnace = BuiltFurnace(T(22, 16), store);
        Assert.AreEqual(1, store.Spent);
        Assert.AreEqual(PlacementManager.FoodFuelId, store.LastId);
        Assert.IsTrue(furnace.IsBurning);
        Assert.AreEqual(12f, furnace.PaidSecondsRemaining, 1e-5f);
        Assert.IsTrue(furnace.GetComponent<PowerNode>().IsActive, "Supplies with no imp");

        var empty = new CountingStore();
        FurnaceGenerator cold = BuiltFurnace(T(25, 16), empty);
        Assert.IsFalse(cold.IsBurning, "No stock: built, but out");
        Assert.IsFalse(cold.GetComponent<PowerNode>().IsActive);
    }

    [Test]
    public void Furnace_RebuysSeamlessly_AtTheIntervalEnd()
    {
        var store = new CountingStore { Units = 5 };
        FurnaceGenerator furnace = BuiltFurnace(T(22, 16), store);
        TickFor(furnace, 11.9f, _ => Assert.IsTrue(furnace.IsBurning));
        Assert.AreEqual(1, store.Spent, "Still inside the first interval");
        TickFor(furnace, 0.2f, _ => Assert.IsTrue(furnace.IsBurning, "No gap at the boundary"));
        Assert.AreEqual(2, store.Spent);
        Assert.AreEqual(11.9f, furnace.PaidSecondsRemaining, 0.05f, "The new interval started where the old one ended");
    }

    [Test]
    public void Furnace_GoesOutWhenTheStockRunsDry_AndResumesOnItsOwnWhenStockReturns()
    {
        var store = new CountingStore { Units = 1 };
        FurnaceGenerator furnace = BuiltFurnace(WheelTile, store, retrySeconds: 1f);
        PowerNode trap = PlaceBuiltNode(PlaceableType.SpikeTrap, TrapTile);
        WireLink();
        Assert.AreEqual(100, trap.Totals.Supply);

        TickFor(furnace, 12.02f);
        Assert.IsFalse(furnace.IsBurning, "The interval lapsed and nothing was left to buy");
        Assert.AreEqual(0, trap.Totals.Supply, "Grid browns out");
        Assert.IsFalse(trap.IsPowered);
        Assert.IsTrue(trap.ShowsMarker);

        TickFor(furnace, 5f);
        Assert.IsFalse(furnace.IsBurning);
        store.Units = 2; // Restocked
        TickFor(furnace, 1.02f);
        Assert.IsTrue(furnace.IsBurning, "Retried within the cadence");
        Assert.AreEqual(2, store.Spent);
        Assert.IsTrue(trap.IsPowered);
    }

    [Test]
    public void Furnace_Disabled_SpendsNothing_AndItsPaidTimeWaits()
    {
        var store = new CountingStore { Units = 10 };
        FurnaceGenerator furnace = BuiltFurnace(T(22, 16), store);
        PowerNode node = furnace.GetComponent<PowerNode>();
        TickFor(furnace, 5f);
        float left = furnace.PaidSecondsRemaining;
        Assert.AreEqual(7f, left, 0.05f);

        node.SetEnabled(false);
        Assert.IsFalse(furnace.IsBurning);
        Assert.IsFalse(node.IsActive, "Supplies nothing while off");
        TickFor(furnace, 100f);
        Assert.AreEqual(left, furnace.PaidSecondsRemaining, 1e-6f, "Paid time doesn't drain while disabled");
        Assert.AreEqual(1, store.Spent, "Nothing bought while disabled");

        node.SetEnabled(true);
        Assert.IsTrue(furnace.IsBurning, "Resumes the remaining paid time");
        TickFor(furnace, 6.9f);
        Assert.AreEqual(1, store.Spent);
        TickFor(furnace, 0.2f);
        Assert.AreEqual(2, store.Spent);
    }

    [Test]
    public void Furnace_SpendCounts_OverALongRun()
    {
        var store = new CountingStore { Units = 100 };
        FurnaceGenerator furnace = BuiltFurnace(T(22, 16), store);
        TickFor(furnace, 59.9f, _ => Assert.IsTrue(furnace.IsBurning));
        Assert.AreEqual(5, store.Spent, "60 s of burn is 5 mushrooms: bought at 0, 12, 24, 36 and 48 s");
        TickFor(furnace, 0.2f);
        Assert.AreEqual(6, store.Spent, "...and the sixth buys the interval starting at 60 s");
        Assert.AreEqual(6, furnace.UnitsBurned);
    }

    [Test]
    public void Furnace_BurnsTheFoodInPlots_FullestFirst_AndPowersTheGridWithNoImp()
    {
        manager.MushroomCapacity = 5;
        MushroomPlot small = BuiltPlot(T(21, 18), 2);
        MushroomPlot big = BuiltPlot(T(22, 18), 4);
        Assert.AreEqual(6, manager.FuelCount(PlacementManager.FoodFuelId));

        Assert.IsTrue(manager.TryPlaceBuilt(PlaceableType.FurnaceGenerator, WheelTile, ImpTile, out Placeable placed));
        var furnace = placed.GetComponent<FurnaceGenerator>();
        Assert.AreSame(manager, furnace.Store, "The manager is the game's fuel store");
        Assert.IsTrue(furnace.IsBurning);
        Assert.AreEqual(3, big.FoodCount, "Taken from the fullest plot");
        Assert.AreEqual(2, small.FoodCount);
        Assert.AreEqual(5, manager.TotalFood, "The top bar's Food drops with it");

        PowerNode trap = PlaceBuiltNode(PlaceableType.SpikeTrap, TrapTile);
        WireLink();
        Assert.AreEqual(100, trap.Totals.Supply);
        Assert.IsTrue(trap.IsPowered);
        Assert.IsFalse(digger.IsOperating, "No tending: the furnace isn't an Operate job");
        Assert.IsFalse(ImpDigger.TrySelectOperate(board, manager.GetBuiltWheels(), digger, imp.CurrentTile, out _, out _));

        Assert.AreEqual(5f * 12f / 60f + 12f / 60f, furnace.MinutesRemaining, 1e-4f, "5 in store + the paid 12 s");
    }

    [Test]
    public void FuelStore_MaterialIdsReadTheStockpile_UnknownIdsHoldNothing()
    {
        var stockGo = new GameObject("TestStockpile");
        try
        {
            var stockpile = stockGo.AddComponent<Stockpile>();
            manager.Stockpile = stockpile;
            int before = stockpile.Count(MaterialType.Stone);
            stockpile.Add(MaterialType.Stone, 2);
            Assert.AreEqual(before + 2, manager.FuelCount("Stone"));
            Assert.IsTrue(manager.TrySpendFuel("stone"), "Case-insensitive");
            Assert.AreEqual(before + 1, stockpile.Count(MaterialType.Stone));
            Assert.AreEqual(0, manager.FuelCount("charcoal"), "Not a fuel the store knows (yet)");
            Assert.IsFalse(manager.TrySpendFuel("charcoal"));
            Assert.IsFalse(manager.TrySpendFuel(PlacementManager.FoodFuelId), "No plots, no mushrooms");
        }
        finally
        {
            Object.DestroyImmediate(stockGo);
        }
    }

    [Test]
    public void BurnMinutes_StockPlusPaidTime()
    {
        Assert.AreEqual(1f, FurnaceGenerator.BurnMinutes(5, 12f, 0f), 1e-6f);
        Assert.AreEqual(1.1f, FurnaceGenerator.BurnMinutes(5, 12f, 6f), 1e-6f);
        Assert.AreEqual(0f, FurnaceGenerator.BurnMinutes(0, 12f, 0f));
    }

    // ---- 3. Grow-Lamp coverage ----

    [Test]
    public void Coverage_ChebyshevRadius_DiagonalsIn()
    {
        Vector2Int lamp = T(10, 10);
        Assert.IsTrue(GrowLamp.Covers(lamp, T(10, 10), 2), "Its own tile");
        Assert.IsTrue(GrowLamp.Covers(lamp, T(12, 12), 2), "The far diagonal corner");
        Assert.IsTrue(GrowLamp.Covers(lamp, T(8, 11), 2));
        Assert.IsFalse(GrowLamp.Covers(lamp, T(13, 10), 2));
        Assert.IsFalse(GrowLamp.Covers(lamp, T(12, 13), 2));
        Assert.AreEqual(2, GrowLamp.Chebyshev(T(0, 0), T(2, 1)));
    }

    [Test]
    public void Multiplier_BestSingleLitLamp_NoStacking_UnlitOrOutsideIsX1()
    {
        Vector2Int plot = T(10, 10);
        var twoLit = new[] { new GrowLamp.LampState(T(9, 9), true), new GrowLamp.LampState(T(11, 11), true) };
        Assert.AreEqual(2f, GrowLamp.MultiplierAt(twoLit, plot, 2, 2f), 1e-6f, "Two lamps give ×2, not ×4");
        Assert.AreEqual(1f, GrowLamp.MultiplierAt(new[] { new GrowLamp.LampState(T(10, 11), false) }, plot, 2, 2f), "Unlit: ×1");
        Assert.AreEqual(1f, GrowLamp.MultiplierAt(new[] { new GrowLamp.LampState(T(13, 10), true) }, plot, 2, 2f), "Out of reach: ×1");
        Assert.AreEqual(1f, GrowLamp.MultiplierAt(new GrowLamp.LampState[0], plot, 2, 2f));
        Assert.AreEqual(1f, GrowLamp.MultiplierAt(twoLit, plot, 2, 0.5f), "A lamp never slows growth");
        Assert.AreEqual(2, GrowLamp.CountCovered(T(10, 10), new[] { T(9, 9), T(12, 12), T(13, 13) }, 2));
    }

    [Test]
    public void LiveCoverage_PoweredLampDoublesNearbyPlots_UnpoweredOrDisabledIsX1_TwoLampsStillX2()
    {
        UseUntendedWheel();
        PowerNode wheel = PlaceBuiltNode(PlaceableType.ImpWheel, WheelTile);
        PowerNode lamp = PlaceBuiltNode(PlaceableType.GrowLamp, TrapTile);
        WireLink();
        MushroomPlot near = BuiltPlot(T(25, 16), 0); // Chebyshev 2 from (24,14)
        MushroomPlot far = BuiltPlot(T(21, 18), 0);
        Assert.IsTrue(lamp.IsPowered);

        Assert.AreEqual(2f, near.GrowthMultiplier, 1e-6f);
        Assert.AreEqual(1f, far.GrowthMultiplier, 1e-6f);

        lamp.SetEnabled(false);
        Assert.AreEqual(1f, near.GrowthMultiplier, "A disabled lamp: ×1");
        lamp.SetEnabled(true);
        wheel.SetEnabled(false);
        Assert.IsFalse(lamp.IsPowered);
        Assert.AreEqual(1f, near.GrowthMultiplier, "An unpowered lamp: plots grow normally");
        wheel.SetEnabled(true);

        PowerNode second = PlaceBuiltNode(PlaceableType.GrowLamp, T(23, 15)); // A spur off (23,14)
        BuildConduit(T(23, 15));
        Assert.IsTrue(second.IsPowered, "40 W covers both lamps' 40 W");
        Assert.AreEqual(2f, near.GrowthMultiplier, 1e-6f, "Two lamps on one plot: still ×2");

        Assert.AreEqual(1, manager.PlotsCoveredFrom(TrapTile), "The near plot only");
    }

    [Test]
    public void ACoveredPlot_AccumulatesFoodTwiceAsFast()
    {
        UseUntendedWheel();
        PowerNode wheel = PlaceBuiltNode(PlaceableType.ImpWheel, WheelTile);
        PlaceBuiltNode(PlaceableType.GrowLamp, TrapTile);
        WireLink();
        MushroomPlot near = BuiltPlot(T(25, 15), 0);
        MushroomPlot far = BuiltPlot(T(21, 18), 0);
        float perFood = near.GrowthSecondsPerFood;

        int steps = Mathf.RoundToInt(perFood / Dt) + 1; // One step past the interval, clear of float rounding
        for (int i = 0; i < steps; i++)
        {
            near.Tick(Dt);
            far.Tick(Dt);
        }
        Assert.AreEqual(2, near.FoodCount, "×2 under the lit lamp");
        Assert.AreEqual(1, far.FoodCount);

        wheel.SetEnabled(false); // The lamp goes dark: back to ×1
        for (int i = 0; i < steps; i++) near.Tick(Dt);
        Assert.AreEqual(3, near.FoodCount);
    }

    // ---- 4. Cards and data ----

    [Test]
    public void FurnaceLines_InEveryState()
    {
        CollectionAssert.AreEqual(new[]
        {
            "Producer · 100 W", "Burning", "Fuel: 7 mushrooms · burns 1 per 12 s", "About 1.6 min of fuel left", "Grid: 100 produced · 30 demanded",
        }, DetailCard.FurnaceLines(100, true, true, true, 7, "mushrooms", 12f, 1.6f, 100, 30));
        CollectionAssert.AreEqual(new[]
        {
            "Producer · 100 W", "Out of fuel", "Fuel: 0 mushrooms · burns 1 per 12 s", "No fuel left", "Grid: 0 produced · 30 demanded",
        }, DetailCard.FurnaceLines(100, true, true, false, 0, "mushrooms", 12f, 0f, 0, 30));
        Assert.AreEqual("Fuel: 1 mushroom · burns 1 per 12 s", DetailCard.FurnaceLines(100, true, true, true, 1, "mushrooms", 12f, 0.4f, 100, 0)[2]);
        Assert.AreEqual("Disabled", DetailCard.FurnaceStateLine(false, true, false));
        Assert.AreEqual("Disabled", DetailCard.FurnaceStateLine(false, false, true), "Off wins over everything");
        Assert.AreEqual("Not connected", DetailCard.FurnaceStateLine(true, false, false));
        Assert.AreEqual("Burning · not connected", DetailCard.FurnaceStateLine(true, false, true));
        Assert.AreEqual(4, DetailCard.FurnaceLines(100, true, false, false, 0, "mushrooms", 12f, 0f, 0, 0).Count, "No grid line unconnected");
    }

    [Test]
    public void LampLines_InEveryState_AndTheDisabledStateForOtherNodes()
    {
        CollectionAssert.AreEqual(new[] { "Consumer · 20 W", "Powered", "Covers 3 plots", "Grid: 40 produced · 20 demanded" },
            DetailCard.LampLines(20, true, true, true, 3, 40, 20));
        CollectionAssert.AreEqual(new[] { "Consumer · 20 W", "Unpowered — grid over-drawn", "Covers 1 plot", "Grid: 0 produced · 20 demanded" },
            DetailCard.LampLines(20, true, true, false, 1, 0, 20));
        CollectionAssert.AreEqual(new[] { "Consumer · 20 W", "Not connected", "Covers 0 plots" },
            DetailCard.LampLines(20, true, false, false, 0, 0, 0));
        Assert.AreEqual("Disabled", DetailCard.LampLines(20, false, true, false, 2, 40, 0)[1]);

        Assert.AreEqual("Disabled", DetailCard.PowerStateLine(PowerRole.Producer, true, true, true, true, enabled: false));
        Assert.AreEqual("Disabled", DetailCard.PowerStateLine(PowerRole.Consumer, false, false, false, false, enabled: false));
    }

    [Test]
    public void PlacedLines_ReadTheLiveFurnaceAndLamp()
    {
        manager.MushroomCapacity = 5;
        BuiltPlot(T(25, 16), 5);
        Assert.IsTrue(manager.TryPlaceBuilt(PlaceableType.FurnaceGenerator, WheelTile, ImpTile, out Placeable furnace));
        PowerNode lamp = PlaceBuiltNode(PlaceableType.GrowLamp, TrapTile);
        WireLink();

        CollectionAssert.AreEqual(new[]
        {
            "Producer · 100 W", "Burning", "Fuel: 4 mushrooms · burns 1 per 12 s", "About 1 min of fuel left", "Grid: 100 produced · 20 demanded",
        }, DetailCard.PlacedLines(furnace, manager));
        CollectionAssert.AreEqual(new[] { "Consumer · 20 W", "Powered", "Covers 1 plot", "Grid: 100 produced · 20 demanded" },
            DetailCard.PlacedLines(lamp.GetComponent<Placeable>(), manager));

        furnace.GetComponent<PowerNode>().SetEnabled(false);
        Assert.AreEqual("Disabled", DetailCard.PlacedLines(furnace, manager)[1]);
        Assert.AreEqual("Unpowered — grid over-drawn", DetailCard.PlacedLines(lamp.GetComponent<Placeable>(), manager)[1]);
    }

    [Test]
    public void TheCardToggle_ShowsOnPowerNodesOnly_AndFlipsTheShownNode()
    {
        var uiGo = new GameObject("TestCardAndController");
        try
        {
            var controller = uiGo.AddComponent<PlacementController>();
            controller.PlacementManager = manager;
            var card = uiGo.AddComponent<DetailCard>();
            card.PlacementController = controller;
            card.PlacementManager = manager;

            PowerNode lamp = PlaceBuiltNode(PlaceableType.GrowLamp, TrapTile);
            Assert.IsTrue(manager.TryPlaceBuilt(PlaceableType.LairCot, T(21, 18), ImpTile, out Placeable cot));

            Assert.IsFalse(card.HasToggle, "Nothing selected");
            controller.ClickBoardTile(TrapTile);
            Assert.IsTrue(card.HasToggle);
            Assert.AreEqual("Disable", card.ToggleLabel);
            Assert.IsTrue(card.ToggleShownNode());
            Assert.IsFalse(lamp.Enabled);
            Assert.AreEqual("Enable", card.ToggleLabel);
            Assert.IsTrue(card.ToggleShownNode());
            Assert.IsTrue(lamp.Enabled);

            controller.ClickBoardTile(cot.Tile);
            Assert.IsFalse(card.HasToggle, "A cot isn't a power node");
            Assert.IsFalse(card.ToggleShownNode());
            Assert.AreEqual("", card.ToggleLabel);

            controller.Select(PlaceableType.GrowLamp);
            Assert.IsFalse(card.HasToggle, "Armed: buildable mode, no toggle");
        }
        finally
        {
            Object.DestroyImmediate(uiGo);
        }
    }

    [Test]
    public void ShippedData_CostsBuildTimesPowerRowsAndPresentation()
    {
        foreach (MaterialType material in new[] { MaterialType.Stone, MaterialType.Glimmerstone, MaterialType.Deepstone })
        {
            Assert.IsTrue(manager.TryGetCost(PlaceableType.FurnaceGenerator, material, out MaterialAmount furnaceCost), $"{material}");
            Assert.AreEqual(6, furnaceCost.amount);
            Assert.IsTrue(manager.TryGetCost(PlaceableType.GrowLamp, material, out MaterialAmount lampCost), $"{material}");
            Assert.AreEqual(4, lampCost.amount);
        }
        Assert.AreEqual(18, manager.Costs.Count);
        Assert.AreEqual(8f, manager.BuildSecondsFor(PlaceableType.FurnaceGenerator), 1e-6f);
        Assert.AreEqual(5f, manager.BuildSecondsFor(PlaceableType.GrowLamp), 1e-6f);

        Assert.IsTrue(manager.TryGetPower(PlaceableType.FurnaceGenerator, out PlaceablePower furnace));
        Assert.AreEqual(PowerRole.Producer, furnace.role);
        Assert.AreEqual(100, furnace.watts);
        Assert.IsFalse(furnace.needsOperator);
        Assert.AreEqual("mushrooms", furnace.fuelId);
        Assert.AreEqual(12f, furnace.fuelSecondsPerUnit, 1e-6f);
        Assert.IsTrue(manager.TryGetPower(PlaceableType.GrowLamp, out PlaceablePower lamp));
        Assert.AreEqual(PowerRole.Consumer, lamp.role);
        Assert.AreEqual(20, lamp.watts);
        Assert.AreEqual("", lamp.fuelId, "Unfueled");
        Assert.IsTrue(manager.TryGetPower(PlaceableType.ImpWheel, out PlaceablePower wheel));
        Assert.AreEqual("", wheel.fuelId, "The wheel stays unfueled");
        Assert.AreEqual(2, manager.LampRadius);
        Assert.AreEqual(2f, manager.LampGrowthMultiplier, 1e-6f);

        Assert.AreEqual("Furnace Generator", BuildPresentation.DisplayName(PlaceableType.FurnaceGenerator));
        Assert.AreEqual("Grow-Lamp", BuildPresentation.DisplayName(PlaceableType.GrowLamp));
        Assert.IsTrue(BuildPresentation.TryGetCategory(PlaceableType.FurnaceGenerator, out BuildCategory fc) && fc == BuildCategory.Power);
        Assert.IsTrue(BuildPresentation.TryGetCategory(PlaceableType.GrowLamp, out BuildCategory lc) && lc == BuildCategory.Power);
        CollectionAssert.AreEqual(new[] { "Output 100 W", "Burns 1 mushroom per 12 s" }, new[]
        {
            DetailCard.StatLine(BuildStat.PowerOutput, manager, PlaceableType.FurnaceGenerator, MaterialType.Stone),
            DetailCard.StatLine(BuildStat.FuelUse, manager, PlaceableType.FurnaceGenerator, MaterialType.Stone),
        });
        CollectionAssert.AreEqual(new[] { BuildStat.PowerOutput, BuildStat.FuelUse }, BuildPresentation.Stats(PlaceableType.FurnaceGenerator));
        CollectionAssert.AreEqual(new[] { "Draw 20 W", "Plots within 2 tiles grow ×2" }, new[]
        {
            DetailCard.StatLine(BuildStat.PowerDraw, manager, PlaceableType.GrowLamp, MaterialType.Stone),
            DetailCard.StatLine(BuildStat.LampCoverage, manager, PlaceableType.GrowLamp, MaterialType.Stone),
        });
        Assert.IsNull(DetailCard.StatLine(BuildStat.NeedsOperator, manager, PlaceableType.FurnaceGenerator, MaterialType.Stone));
        Assert.IsNull(DetailCard.StatLine(BuildStat.PowerDraw, manager, PlaceableType.FurnaceGenerator, MaterialType.Stone),
            "A producer has no draw line");
    }

    [Test]
    public void ASiteIsInert_TheFurnaceBurnsNothingUntilBuilt()
    {
        var store = new CountingStore { Units = 5 };
        Assert.IsTrue(manager.TryPlace(PlaceableType.FurnaceGenerator, T(22, 16), ImpTile, out Placeable site));
        var furnace = site.GetComponent<FurnaceGenerator>();
        furnace.Initialize(store, PlacementManager.FoodFuelId, 12f, 1f);
        TickFor(furnace, 30f);
        Assert.AreEqual(0, store.Spent);
        Assert.IsFalse(furnace.IsBurning);
        Assert.IsFalse(site.TryGetComponent(out GrowLamp _), "A furnace isn't a lamp");
        Assert.AreSame(furnace, furnace.GetComponent<PowerNode>().SupplyGate, "Its node is gated on the burn");
    }
}
