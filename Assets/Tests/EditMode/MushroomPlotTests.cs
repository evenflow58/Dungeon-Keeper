using NUnit.Framework;
using UnityEngine;

public class MushroomPlotTests
{
    private static readonly Vector2Int ImpTile = new Vector2Int(24, 16); // Imp spawn: cavern center
    private const float Interval = 2f;                                    // Shortened growth interval for fast tests

    private GameObject managerGo;
    private DungeonBoard board;
    private BoardRenderer boardRenderer;
    private PlacementManager manager;

    [SetUp]
    public void SetUp()
    {
        // Default board: 48x32 Rock with the 6x6 Floor cavern at (21,13)-(26,18).
        managerGo = new GameObject("TestDungeonManager");
        board = managerGo.AddComponent<DungeonBoard>();
        board.InitializeBoard();
        boardRenderer = managerGo.AddComponent<BoardRenderer>();
        boardRenderer.Board = board;
        manager = managerGo.AddComponent<PlacementManager>();
        manager.Board = board;
        manager.Renderer = boardRenderer;
        manager.MushroomGrowthSecondsPerFood = Interval; // set before placing: plots take the manager's config
        manager.MushroomCapacity = 5;
    }

    [TearDown]
    public void TearDown()
    {
        if (managerGo != null) Object.DestroyImmediate(managerGo);
    }

    private MushroomPlot PlacePlot(int x, int y)
    {
        Assert.IsTrue(manager.TryPlace(PlaceableType.MushroomPlot, new Vector2Int(x, y), ImpTile, out Placeable p));
        var plot = p.gameObject.GetComponent<MushroomPlot>();
        Assert.IsNotNull(plot);
        return plot;
    }

    private static int PipObjectCount(MushroomPlot plot)
    {
        int n = 0;
        foreach (Transform child in plot.transform)
            if (child.name.StartsWith("FoodPip")) n++;
        return n;
    }

    // --- Placement wiring ---

    [Test]
    public void PlacedPlot_StartsEmpty_WithManagerConfig()
    {
        MushroomPlot plot = PlacePlot(22, 14);

        Assert.AreEqual(0, plot.FoodCount);
        Assert.AreEqual(0f, plot.GrowthElapsed);
        Assert.AreEqual(Interval, plot.GrowthSecondsPerFood);
        Assert.AreEqual(5, plot.Capacity);
        Assert.AreEqual(5, PipObjectCount(plot), "One pip per unit of capacity");
        Assert.AreEqual(0, plot.VisiblePipCount);
    }

    [Test]
    public void CotAndTrap_HaveNoMushroomPlotComponent()
    {
        manager.TryPlace(PlaceableType.LairCot, new Vector2Int(22, 14), ImpTile, out Placeable cot);
        manager.TryPlace(PlaceableType.SpikeTrap, new Vector2Int(23, 14), ImpTile, out Placeable trap);

        Assert.IsFalse(cot.TryGetComponent(out MushroomPlot _));
        Assert.IsFalse(trap.TryGetComponent(out MushroomPlot _));
    }

    // --- Growth ---

    [Test]
    public void Tick_FoodAppearsAtIntervalBoundary_NotBefore()
    {
        MushroomPlot plot = PlacePlot(22, 14);

        plot.Tick(Interval - 0.01f);
        Assert.AreEqual(0, plot.FoodCount);
        Assert.AreEqual(0, plot.VisiblePipCount);

        plot.Tick(0.02f);
        Assert.AreEqual(1, plot.FoodCount);
        Assert.AreEqual(1, plot.VisiblePipCount, "Pip appears in the same call");
    }

    [Test]
    public void Tick_LargeStep_CarriesRemainder()
    {
        MushroomPlot plot = PlacePlot(22, 14);

        plot.Tick(2.5f * Interval);

        Assert.AreEqual(2, plot.FoodCount);
        Assert.AreEqual(0.5f * Interval, plot.GrowthElapsed, 1e-4f);
        Assert.AreEqual(2, plot.VisiblePipCount);
    }

    [Test]
    public void Tick_StopsAtCapacity_WithElapsedZero()
    {
        MushroomPlot plot = PlacePlot(22, 14);

        for (int i = 0; i < 100; i++) plot.Tick(0.5f); // 50s = 25 intervals, far past the cap of 5

        Assert.AreEqual(5, plot.FoodCount);
        Assert.AreEqual(0f, plot.GrowthElapsed);
        Assert.AreEqual(5, plot.VisiblePipCount);
    }

    [Test]
    public void Tick_SingleStepPastCap_DiscardsLeftover()
    {
        MushroomPlot plot = PlacePlot(22, 14);

        plot.Tick(7.5f * Interval);

        Assert.AreEqual(5, plot.FoodCount);
        Assert.AreEqual(0f, plot.GrowthElapsed);
    }

    [Test]
    public void AtCap_NoBanking_NextFoodNeedsFullInterval()
    {
        MushroomPlot plot = PlacePlot(22, 14);
        plot.Tick(5f * Interval);
        Assert.AreEqual(5, plot.FoodCount);

        for (int i = 0; i < 3; i++) plot.Tick(Interval); // three intervals spent full
        Assert.AreEqual(0f, plot.GrowthElapsed);

        Assert.IsTrue(plot.TryTakeFood());
        Assert.AreEqual(4, plot.FoodCount);

        plot.Tick(Interval - 0.1f);
        Assert.AreEqual(4, plot.FoodCount, "Time spent at the cap must not count toward the next food");

        plot.Tick(0.2f);
        Assert.AreEqual(5, plot.FoodCount);
    }

    // --- Taking food ---

    [Test]
    public void TryTakeFood_Empty_ReturnsFalse()
    {
        MushroomPlot plot = PlacePlot(22, 14);

        Assert.IsFalse(plot.TryTakeFood());
        Assert.AreEqual(0, plot.FoodCount);
    }

    [Test]
    public void TryTakeFood_Stocked_DecrementsAndHidesPip()
    {
        MushroomPlot plot = PlacePlot(22, 14);
        plot.Tick(3f * Interval);
        Assert.AreEqual(3, plot.VisiblePipCount);

        Assert.IsTrue(plot.TryTakeFood());
        Assert.AreEqual(2, plot.FoodCount);
        Assert.AreEqual(2, plot.VisiblePipCount, "Pip hides in the same call");
    }

    [Test]
    public void TryTakeFood_BelowCap_PreservesPartialProgress()
    {
        MushroomPlot plot = PlacePlot(22, 14);
        plot.Tick(Interval);              // 1 food
        plot.Tick(0.6f * Interval);       // 60% toward the next

        Assert.IsTrue(plot.TryTakeFood());
        Assert.AreEqual(0, plot.FoodCount);
        Assert.AreEqual(0.6f * Interval, plot.GrowthElapsed, 1e-4f, "A take doesn't reset progress");

        plot.Tick(0.4f * Interval - 0.01f);
        Assert.AreEqual(0, plot.FoodCount);
        plot.Tick(0.02f);                 // completes the remaining 40%
        Assert.AreEqual(1, plot.FoodCount);
    }

    // --- Pips ---

    [Test]
    public void RefreshPips_ReconcilesToCapacityChange()
    {
        MushroomPlot plot = PlacePlot(22, 14);
        plot.Tick(2f * Interval);

        plot.Capacity = 3;
        plot.RefreshPips();
        Assert.AreEqual(3, PipObjectCount(plot));
        Assert.AreEqual(2, plot.VisiblePipCount);

        plot.Capacity = 6;
        plot.RefreshPips();
        Assert.AreEqual(6, PipObjectCount(plot));
        Assert.AreEqual(2, plot.VisiblePipCount);
    }

    // --- TotalFood ---

    [Test]
    public void TotalFood_SumsAllPlots_IgnoresOtherTypes()
    {
        Assert.AreEqual(0, manager.TotalFood);

        MushroomPlot a = PlacePlot(22, 14);
        MushroomPlot b = PlacePlot(23, 14);
        manager.TryPlace(PlaceableType.LairCot, new Vector2Int(25, 14), ImpTile, out _);
        manager.TryPlace(PlaceableType.SpikeTrap, new Vector2Int(26, 14), ImpTile, out _);

        a.Tick(3f * Interval);
        b.Tick(1f * Interval);
        Assert.AreEqual(4, manager.TotalFood);

        a.TryTakeFood();
        Assert.AreEqual(3, manager.TotalFood);
    }
}
