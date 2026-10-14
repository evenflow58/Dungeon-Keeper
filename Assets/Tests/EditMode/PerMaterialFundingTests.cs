using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Per-material funding queues (#120): each material keeps its own first-come line. Within a material the line is as
/// strict as #104's (a later, cheaper order never jumps an earlier one); across materials there is no line — a waiting
/// order blocks only orders that need the same material.
/// </summary>
public class PerMaterialFundingTests
{
    private static readonly Vector2Int ImpTile = new Vector2Int(24, 16);
    private const MaterialType Stone = MaterialType.Stone;
    private const MaterialType Glimmer = MaterialType.Glimmerstone;
    private const MaterialType Deep = MaterialType.Deepstone;

    private GameObject managerGo;
    private DungeonBoard board;
    private BoardRenderer boardRenderer;
    private PlacementManager manager;
    private Stockpile stockpile;
    private int nextTile;

    [SetUp]
    public void SetUp()
    {
        // Default board: 48x32 Rock with the 6x6 Floor cavern at (21,13)-(26,18). Shipped costs: cot 3, plot 4, trap 2
        // in every material.
        managerGo = new GameObject("TestDungeonManager");
        board = managerGo.AddComponent<DungeonBoard>();
        board.InitializeBoard();
        boardRenderer = managerGo.AddComponent<BoardRenderer>();
        boardRenderer.Board = board;
        manager = managerGo.AddComponent<PlacementManager>();
        manager.Board = board;
        manager.Renderer = boardRenderer;
        stockpile = managerGo.AddComponent<Stockpile>();
        manager.Stockpile = stockpile;
        nextTile = 0;
    }

    [TearDown]
    public void TearDown()
    {
        if (managerGo != null) Object.DestroyImmediate(managerGo);
    }

    private void SetStock(int stone, int glimmer = 0, int deep = 0) => stockpile.StartingAmounts = new List<MaterialAmount>
    {
        new MaterialAmount(Stone, stone), new MaterialAmount(Glimmer, glimmer), new MaterialAmount(Deep, deep),
    };

    private void AssertStock(int stone, int glimmer, int deep, string because)
    {
        Assert.AreEqual(stone, stockpile.Count(Stone), because + ": stone");
        Assert.AreEqual(glimmer, stockpile.Count(Glimmer), because + ": glimmerstone");
        Assert.AreEqual(deep, stockpile.Count(Deep), because + ": deepstone");
    }

    /// <summary>Places an order on the next free cavern tile (row by row from (21,13)), away from the imp's tile.</summary>
    private Placeable Order(PlaceableType type, MaterialType material)
    {
        var tile = new Vector2Int(21 + nextTile % 6, 13 + nextTile / 6);
        nextTile++;
        Assert.IsTrue(manager.TryPlace(type, material, tile, ImpTile, out Placeable p), "Placing is never blocked by cost");
        Assert.AreEqual(material, p.OrderedMaterial);
        return p;
    }

    private void AssertWaiting(Placeable p, string because)
    {
        Assert.IsFalse(p.IsFunded, because + ": unfunded");
        Assert.IsNotNull(p.OrderMarker, because + ": order marker");
        Assert.IsTrue(p.OrderMarker.activeInHierarchy, because + ": marker shown");
        Assert.IsFalse(p.MaterialsPile.activeSelf, because + ": no pile");
        CollectionAssert.Contains(manager.GetAwaitingFunding(), p, because + ": in the waiting line");
        CollectionAssert.DoesNotContain(manager.GetConstructionSites(), p, because + ": not a build job");
    }

    private void AssertFunded(Placeable p, string because)
    {
        Assert.IsTrue(p.IsFunded, because + ": funded");
        Assert.IsNull(p.OrderMarker, because + ": marker gone");
        Assert.IsTrue(p.MaterialsPile.activeInHierarchy, because + ": the pile arrived");
        CollectionAssert.DoesNotContain(manager.GetAwaitingFunding(), p, because + ": left the waiting line");
        CollectionAssert.Contains(manager.GetConstructionSites(), p, because + ": a build job");
    }

    // --- Across materials: no line ---

    [Test]
    public void AWaitingGlimmerstoneOrder_DoesNotBlockAStoneOrderTheStockCovers()
    {
        SetStock(stone: 17); // PR #115's play moment: 17 stone in hand, no glimmerstone
        Placeable glimmerCot = Order(PlaceableType.LairCot, Glimmer); // 3 glimmerstone: can't fund
        AssertWaiting(glimmerCot, "Glimmerstone cot");

        Placeable stoneTrap = Order(PlaceableType.SpikeTrap, Stone); // 2 stone: placed later, funds at once
        AssertFunded(stoneTrap, "Stone trap, behind a glimmerstone order in placement order");
        AssertStock(15, 0, 0, "Charged 2 stone, exactly once");
        AssertWaiting(glimmerCot, "Glimmerstone cot still waits");
        Assert.Less(glimmerCot.OrderIndex, stoneTrap.OrderIndex, "The waiting order was placed first");

        Assert.AreEqual(0, manager.RunFundingPass(), "Nothing more to fund");
        AssertStock(15, 0, 0, "A further pass charges nothing");
    }

    [Test]
    public void ThreeLines_StockForOneMaterial_FundsOnlyThatLinesHead()
    {
        SetStock(stone: 0);
        Placeable stoneCot = Order(PlaceableType.LairCot, Stone);
        Placeable glimmerCot = Order(PlaceableType.LairCot, Glimmer);
        Placeable deepCot = Order(PlaceableType.LairCot, Deep);
        AssertWaiting(stoneCot, "Stone");
        AssertWaiting(glimmerCot, "Glimmerstone");
        AssertWaiting(deepCot, "Deepstone");

        stockpile.Add(Glimmer, 3); // The middle line, so neither placement-order neighbour is what lets it through
        Assert.AreEqual(1, manager.RunFundingPass());
        AssertFunded(glimmerCot, "Glimmerstone");
        AssertWaiting(stoneCot, "Stone untouched");
        AssertWaiting(deepCot, "Deepstone untouched");
        AssertStock(0, 0, 0, "Only glimmerstone was charged");

        stockpile.Add(Deep, 3);
        Assert.AreEqual(1, manager.RunFundingPass());
        AssertFunded(deepCot, "Deepstone");
        AssertWaiting(stoneCot, "Stone still waits");

        stockpile.Add(Stone, 3);
        Assert.AreEqual(1, manager.RunFundingPass());
        AssertFunded(stoneCot, "Stone");
        AssertStock(0, 0, 0, "Every order charged exactly its own cost");
        CollectionAssert.IsEmpty(manager.GetAwaitingFunding());
    }

    [Test]
    public void OnePass_FundsEveryLinesHeadItCanCover()
    {
        SetStock(stone: 0);
        Placeable stoneCot = Order(PlaceableType.LairCot, Stone);
        Placeable glimmerPlot = Order(PlaceableType.MushroomPlot, Glimmer); // 4
        Placeable deepCot = Order(PlaceableType.LairCot, Deep);

        stockpile.Add(Stone, 3);
        stockpile.Add(Glimmer, 3); // One short
        stockpile.Add(Deep, 3);
        Assert.AreEqual(2, manager.RunFundingPass(), "Stone and deepstone; glimmerstone is short");
        AssertFunded(stoneCot, "Stone");
        AssertFunded(deepCot, "Deepstone, placed after the waiting glimmerstone order");
        AssertWaiting(glimmerPlot, "Glimmerstone");
        AssertStock(0, 3, 0, "The short line was not charged (all or nothing)");
    }

    // --- Within a material: as strict as ever ---

    [Test]
    public void WithinAMaterial_AnEarlierOrderIsNeverJumped()
    {
        SetStock(stone: 3);
        Placeable plot = Order(PlaceableType.MushroomPlot, Stone); // 4: can't fund
        Placeable trap = Order(PlaceableType.SpikeTrap, Stone);    // 2: could, but it's behind the plot
        AssertWaiting(plot, "Plot");
        AssertWaiting(trap, "Trap: no jumping its own material's line");
        AssertStock(3, 0, 0, "Nothing charged");

        stockpile.Add(Stone, 1);
        Assert.AreEqual(1, manager.RunFundingPass());
        AssertFunded(plot, "Plot, at 4");
        AssertWaiting(trap, "Trap still waits");
        AssertStock(0, 0, 0, "Plot paid");

        stockpile.Add(Stone, 2);
        Assert.AreEqual(1, manager.RunFundingPass());
        AssertFunded(trap, "Trap, at 2");
        AssertStock(0, 0, 0, "Trap paid");
        CollectionAssert.IsEmpty(manager.GetAwaitingFunding());
    }

    [Test]
    public void WithinAMaterial_StrictnessHoldsInEveryLine_WithOtherLinesInterleaved()
    {
        SetStock(stone: 3, glimmer: 3, deep: 3);
        Placeable stonePlot = Order(PlaceableType.MushroomPlot, Stone);     // 4: short
        Placeable glimmerPlot = Order(PlaceableType.MushroomPlot, Glimmer); // 4: short
        Placeable stoneTrap = Order(PlaceableType.SpikeTrap, Stone);        // 2: behind the stone plot
        Placeable deepTrap = Order(PlaceableType.SpikeTrap, Deep);          // 2: its line is empty, funds
        Placeable glimmerTrap = Order(PlaceableType.SpikeTrap, Glimmer);    // 2: behind the glimmerstone plot

        AssertWaiting(stonePlot, "Stone plot");
        AssertWaiting(stoneTrap, "Stone trap");
        AssertWaiting(glimmerPlot, "Glimmerstone plot");
        AssertWaiting(glimmerTrap, "Glimmerstone trap");
        AssertFunded(deepTrap, "Deepstone trap");
        AssertStock(3, 3, 1, "Only deepstone charged");

        stockpile.Add(Glimmer, 1);
        Assert.AreEqual(1, manager.RunFundingPass(), "The glimmerstone head only: 4 of 4 leaves nothing for the trap");
        AssertFunded(glimmerPlot, "Glimmerstone plot");
        AssertWaiting(glimmerTrap, "Glimmerstone trap");
        AssertWaiting(stonePlot, "Stone plot");
        AssertWaiting(stoneTrap, "Stone trap");
        AssertStock(3, 0, 1, "Glimmerstone plot paid");
    }

    [Test]
    public void AFundedHead_LetsTheSameMaterialsNextOrderFund_InTheSamePass()
    {
        SetStock(stone: 0);
        Placeable cot = Order(PlaceableType.LairCot, Stone);    // 3
        Placeable trap = Order(PlaceableType.SpikeTrap, Stone); // 2
        Placeable plot = Order(PlaceableType.MushroomPlot, Stone); // 4

        stockpile.Add(Stone, 6);
        Assert.AreEqual(2, manager.RunFundingPass(), "Cot then trap; the plot is short");
        AssertFunded(cot, "Cot");
        AssertFunded(trap, "Trap");
        AssertWaiting(plot, "Plot");
        AssertStock(1, 0, 0, "6 - 3 - 2");
    }

    // --- Earning ---

    [Test]
    public void EarningAMaterial_FundsItsOwnWaitingHead_AndNeverAnotherLines()
    {
        SetStock(stone: 0);
        Placeable glimmerCot = Order(PlaceableType.LairCot, Glimmer);
        Placeable stoneCot = Order(PlaceableType.LairCot, Stone);

        stockpile.Add(Stone, 2);
        Assert.AreEqual(0, manager.RunFundingPass(), "2 stone covers neither");
        stockpile.Add(Stone, 1);
        Assert.AreEqual(1, manager.RunFundingPass(), "No placement action: the pass alone");
        AssertFunded(stoneCot, "Stone cot, though the glimmerstone order was placed first");
        AssertWaiting(glimmerCot, "Stone can't pay for glimmerstone");
        AssertStock(0, 0, 0, "Stone charged; glimmerstone never was");

        stockpile.Add(Stone, 50);
        Assert.AreEqual(0, manager.RunFundingPass(), "No amount of stone funds a glimmerstone head");
        AssertWaiting(glimmerCot, "Still waiting");
        AssertStock(50, 0, 0, "Nothing charged");

        stockpile.Add(Glimmer, 3);
        Assert.AreEqual(1, manager.RunFundingPass());
        AssertFunded(glimmerCot, "On the pass after its own material covers it");
        AssertStock(50, 0, 0, "Glimmerstone charged, stone untouched");
    }

    [Test]
    public void AnOrder_FundsExactlyOnce()
    {
        SetStock(stone: 9, glimmer: 9);
        Placeable stoneCot = Order(PlaceableType.LairCot, Stone);
        Placeable glimmerCot = Order(PlaceableType.LairCot, Glimmer);
        AssertStock(6, 6, 0, "Each charged at placement");
        for (int i = 0; i < 5; i++) Assert.AreEqual(0, manager.RunFundingPass());
        AssertStock(6, 6, 0, "Never charged again");
        AssertFunded(stoneCot, "Stone cot");
        AssertFunded(glimmerCot, "Glimmerstone cot");
    }

    // --- WouldFundNow (the red-cost rule on the rail's tiles and the card's chips) ---

    [Test]
    public void WouldFundNow_AnotherMaterialsWaitingLine_DoesNotRedden()
    {
        SetStock(stone: 10);
        Placeable glimmerCot = Order(PlaceableType.LairCot, Glimmer);
        AssertWaiting(glimmerCot, "Glimmerstone cot");

        Assert.IsTrue(manager.WouldFundNow(PlaceableType.LairCot, Stone), "Stone covers it; the glimmerstone line is not its line");
        Assert.IsTrue(manager.WouldFundNow(PlaceableType.MushroomPlot, Stone));
        Assert.IsFalse(manager.WouldFundNow(PlaceableType.SpikeTrap, Glimmer), "Its own line is waiting");
        Assert.IsFalse(manager.WouldFundNow(PlaceableType.LairCot, Deep), "No line, but no deepstone");
    }

    [Test]
    public void WouldFundNow_TheSameMaterialsWaitingLine_StillReddens()
    {
        SetStock(stone: 3);
        Placeable plot = Order(PlaceableType.MushroomPlot, Stone); // 4: waits, 3 stone left in hand
        AssertWaiting(plot, "Stone plot");
        Assert.IsFalse(manager.WouldFundNow(PlaceableType.SpikeTrap, Stone), "3 covers a trap's 2, but it would queue behind the plot");

        stockpile.Add(Stone, 1);
        manager.RunFundingPass();
        AssertFunded(plot, "Plot");
        Assert.IsFalse(manager.WouldFundNow(PlaceableType.SpikeTrap, Stone), "The line is clear, but the stock is 0 now");
        stockpile.Add(Stone, 2);
        Assert.IsTrue(manager.WouldFundNow(PlaceableType.SpikeTrap, Stone), "Clear line, covered");
    }

    [Test]
    public void WouldFundNow_AgreesWithWhatPlacingDoes()
    {
        SetStock(stone: 5, glimmer: 2);
        Order(PlaceableType.LairCot, Glimmer); // Waits: 2 of 3

        foreach (var (type, material) in new[] { (PlaceableType.SpikeTrap, Stone), (PlaceableType.SpikeTrap, Glimmer), (PlaceableType.LairCot, Stone), (PlaceableType.LairCot, Deep) })
        {
            bool predicted = manager.WouldFundNow(type, material);
            Placeable placed = Order(type, material);
            Assert.AreEqual(predicted, placed.IsFunded, $"{type} in {material}");
        }
    }
}
