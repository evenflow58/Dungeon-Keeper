using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Build costs and place-and-wait (#104): orders record their cost, fund strictly in placement order from the
/// stockpile (deducted at funding), and only funded sites become build jobs. With no stockpile wired, orders fund at
/// once — #91/#92's staging is unchanged.
/// </summary>
public class BuildCostTests
{
    private static readonly Vector2Int ImpTile = new Vector2Int(24, 16);
    private const float Dt = 0.02f;

    private GameObject managerGo;
    private GameObject impGo;
    private DungeonBoard board;
    private BoardRenderer boardRenderer;
    private PlacementManager manager;
    private Stockpile stockpile;
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
        manager = managerGo.AddComponent<PlacementManager>();
        manager.Board = board;
        manager.Renderer = boardRenderer;
        stockpile = managerGo.AddComponent<Stockpile>(); // 10 stone
        manager.Stockpile = stockpile;                   // Wired explicitly: orders cost stone

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
        digger.Stockpile = stockpile;
        digger.DigSecondsPerTile = 1f;
        manager.LairCotBuildSeconds = 1f;
        manager.MushroomPlotBuildSeconds = 1f;
        manager.SpikeTrapBuildSeconds = 1f;
    }

    [TearDown]
    public void TearDown()
    {
        if (impGo != null) Object.DestroyImmediate(impGo);
        if (managerGo != null) Object.DestroyImmediate(managerGo);
    }

    private int Stone => stockpile.Count(MaterialType.Stone);

    private void SetStock(int stone) => stockpile.StartingAmounts = new List<MaterialAmount> { new MaterialAmount(MaterialType.Stone, stone) };

    private Placeable Order(PlaceableType type, int x, int y)
    {
        Assert.IsTrue(manager.TryPlace(type, new Vector2Int(x, y), ImpTile, out Placeable p), "Placing is never blocked by cost");
        return p;
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

    private static void AssertWaiting(Placeable p, string because)
    {
        Assert.IsFalse(p.IsFunded, because + ": unfunded");
        Assert.IsNotNull(p.OrderMarker, because + ": order marker");
        Assert.IsTrue(p.OrderMarker.activeInHierarchy, because + ": marker shown");
        Assert.IsFalse(p.MaterialsPile.activeSelf, because + ": no pile (the materials haven't arrived)");
        Assert.IsFalse(p.Model.activeSelf, because + ": no finished model");
    }

    private static void AssertFunded(Placeable p, string because)
    {
        Assert.IsTrue(p.IsFunded, because + ": funded");
        Assert.IsNull(p.OrderMarker, because + ": marker gone");
        Assert.IsTrue(p.MaterialsPile.activeInHierarchy, because + ": the pile arrived");
    }

    // --- The cost table and the order record ---

    [Test]
    public void CostFor_AnswersTheTable_StoneForEveryType()
    {
        foreach (var (type, amount) in new[] { (PlaceableType.LairCot, 3), (PlaceableType.MushroomPlot, 4), (PlaceableType.SpikeTrap, 2) })
        {
            MaterialAmount cost = manager.CostFor(type);
            Assert.AreEqual(MaterialType.Stone, cost.type, $"{type}");
            Assert.AreEqual(amount, cost.amount, $"{type}");
        }
    }

    [Test]
    public void APlacedOrder_RecordsItsRequirement_AndItsPlaceInLine()
    {
        Placeable cot = Order(PlaceableType.LairCot, 21, 13);
        Placeable plot = Order(PlaceableType.MushroomPlot, 22, 13);
        Assert.AreEqual(MaterialType.Stone, cot.RequiredMaterial);
        Assert.AreEqual(3, cot.RequiredAmount);
        Assert.AreEqual(4, plot.RequiredAmount);
        Assert.Less(cot.OrderIndex, plot.OrderIndex, "Placement order");
    }

    // --- Funding ---

    [Test]
    public void Affordable_FundsAtPlacement_DeductsOnce_PileShown_ABuildJob()
    {
        Placeable cot = Order(PlaceableType.LairCot, 22, 14);
        AssertFunded(cot, "Affordable");
        Assert.AreEqual(7, Stone, "10 - 3, at funding");
        CollectionAssert.Contains(manager.GetConstructionSites(), cot);
        CollectionAssert.IsEmpty(manager.GetAwaitingFunding());

        manager.RunFundingPass();
        manager.RunFundingPass();
        Assert.AreEqual(7, Stone, "Charged exactly once");
        Assert.IsTrue(manager.WouldFundNow(PlaceableType.LairCot));
    }

    [Test]
    public void Unaffordable_Waits_NoDeduction_MarkerShown_NotABuildJob_AndTheImpWorksAroundIt()
    {
        SetStock(2);
        Placeable plot = Order(PlaceableType.MushroomPlot, 22, 14);
        AssertWaiting(plot, "Short by 2");
        Assert.AreEqual(2, Stone, "Nothing deducted");
        CollectionAssert.DoesNotContain(manager.GetConstructionSites(), plot);
        CollectionAssert.AreEqual(new[] { plot }, manager.GetAwaitingFunding());
        Assert.IsFalse(manager.WouldFundNow(PlaceableType.MushroomPlot));
        Assert.IsFalse(ImpDigger.TrySelectWork(board, new List<Placeable> { plot }, null, ImpTile, out _, out _, out Placeable none, out _),
            "Selection never sees an unfunded site, even handed one directly");
        Assert.IsNull(none);

        // Other (funded) work proceeds; the imp never goes to the order.
        digger.StoneYieldPerTile = 0; // Keep the order unaffordable through the dig
        board.SetTile(27, 16, TileState.Designated);
        bool everTargeted = false;
        StepUntil(() => { everTargeted |= digger.CurrentSite == plot; return board.GetTile(27, 16) == TileState.Floor; }, 10f, "the dig");
        for (int i = 0; i < 100; i++) { Step(); everTargeted |= digger.CurrentSite == plot; }
        Assert.IsFalse(everTargeted, "The imp never travels to or builds an unfunded site");
        Assert.AreEqual(0f, plot.BuildProgress);
        AssertWaiting(plot, "Still waiting");
    }

    [Test]
    public void StrictFifo_AnEarlierOrderIsNeverJumped()
    {
        SetStock(3);
        Placeable plot = Order(PlaceableType.MushroomPlot, 21, 13); // 4: can't fund
        Placeable trap = Order(PlaceableType.SpikeTrap, 22, 13);     // 2: could, but it's behind the plot
        AssertWaiting(plot, "Plot");
        AssertWaiting(trap, "Trap: no jumping the line");
        Assert.AreEqual(3, Stone);
        Assert.IsFalse(manager.WouldFundNow(PlaceableType.SpikeTrap), "A new order would queue behind them");

        stockpile.Add(MaterialType.Stone, 1);
        Assert.AreEqual(1, manager.RunFundingPass());
        AssertFunded(plot, "Plot, at 4");
        AssertWaiting(trap, "Trap still waits");
        Assert.AreEqual(0, Stone);

        stockpile.Add(MaterialType.Stone, 2);
        Assert.AreEqual(1, manager.RunFundingPass());
        AssertFunded(trap, "Trap, at 2");
        Assert.AreEqual(0, Stone);
        CollectionAssert.IsEmpty(manager.GetAwaitingFunding());
    }

    [Test]
    public void EarningFromADig_FundsTheWaitingOrder_ThenTheImpBuildsIt()
    {
        SetStock(3);
        Placeable plot = Order(PlaceableType.MushroomPlot, 22, 14);
        AssertWaiting(plot, "Short by 1");

        board.SetTile(27, 16, TileState.Designated); // The dig that earns the difference (1 stone, #103)
        StepUntil(() => board.GetTile(27, 16) == TileState.Floor, 10f, "the dig");
        Assert.AreEqual(4, Stone);
        manager.RunFundingPass(); // What PlacementManager.Update does every frame while an order waits
        AssertFunded(plot, "Funded by the dig's yield, no placement action");
        Assert.AreEqual(0, Stone);

        StepUntil(() => plot.IsBuilt, 10f, "the imp to build the now-funded plot");
    }

    [Test]
    public void AnUnfundedOrderCompletedByStaging_LeavesTheLineUncharged()
    {
        SetStock(0);
        Placeable cot = Order(PlaceableType.LairCot, 22, 14);
        cot.CompleteConstruction(); // Staging / tests
        Assert.IsNull(cot.OrderMarker, "Marker cleaned up");
        stockpile.Add(MaterialType.Stone, 5);
        Assert.AreEqual(0, manager.RunFundingPass());
        Assert.AreEqual(5, Stone, "Never charged for a site that's already built");
    }

    // --- Degradation ---

    [Test]
    public void UnwiredStockpile_OrdersFundAtOnce_ExactlyAsBefore()
    {
        manager.Stockpile = null;
        Placeable plot = Order(PlaceableType.MushroomPlot, 22, 14);
        AssertFunded(plot, "Unwired: no waiting");
        CollectionAssert.Contains(manager.GetConstructionSites(), plot);
        Assert.IsTrue(manager.WouldFundNow(PlaceableType.MushroomPlot));
        Assert.AreEqual(10, Stone, "Nothing charged anywhere");
        StepUntil(() => plot.IsBuilt, 10f, "the imp to build it");
    }

    // --- The marker ---

    [Test]
    public void OrderMarker_IsATranslucentGhost_OnAnOutline_NoShadows()
    {
        SetStock(0);
        Placeable cot = Order(PlaceableType.LairCot, 22, 14);
        Transform marker = cot.OrderMarker.transform;
        Transform outline = marker.Find("Outline");
        Assert.IsNotNull(outline, "A footprint outline");
        Assert.AreEqual(4, outline.childCount, "Four sides");
        Assert.AreEqual(cot.Model.transform.childCount + 1, marker.childCount, "A ghost of every finished part, plus the outline");
        foreach (MeshRenderer r in marker.GetComponentsInChildren<MeshRenderer>())
        {
            Assert.AreEqual((int)UnityEngine.Rendering.RenderQueue.Transparent, r.sharedMaterial.renderQueue, r.name + ": translucent");
            Assert.Less(r.sharedMaterial.GetColor("_BaseColor").a, 1f, r.name);
            Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off, r.shadowCastingMode, r.name + ": no shadow");
        }
    }

    // --- The build bar ---

    [Test]
    public void CostLabel_ShowsTheCost_RedWhenItWouldWait()
    {
        Assert.AreEqual("Lair Cot · 3", PlacementController.CostLabel("Lair Cot", 3, true, Color.red));
        string red = PlacementController.CostLabel("Mushroom Plot", 4, false, Color.red);
        Assert.AreEqual("Mushroom Plot · <color=#FF0000>4</color>", red);
    }

    [Test]
    public void WouldFundNow_TracksTheStockAndTheLine()
    {
        SetStock(3);
        Assert.IsTrue(manager.WouldFundNow(PlaceableType.LairCot), "3 covers 3");
        Assert.IsFalse(manager.WouldFundNow(PlaceableType.MushroomPlot), "3 doesn't cover 4");
        stockpile.Add(MaterialType.Stone, 1);
        Assert.IsTrue(manager.WouldFundNow(PlaceableType.MushroomPlot));
    }
}
