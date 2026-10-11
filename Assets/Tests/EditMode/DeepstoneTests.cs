using System;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Deepstone (#111): the third material — catalogued, held in the stockpile (none to start), dug from the deep band
/// far from the entrance through #103's yield seam, orderable in everything, and the best rest material (#110's table).
/// </summary>
public class DeepstoneTests
{
    private const float Dt = 0.02f;
    private static readonly Color Slate = new Color(0.30f, 0.36f, 0.50f, 1f);

    private GameObject managerGo;
    private GameObject impGo;
    private DungeonBoard board;
    private BoardRenderer boardRenderer;
    private Stockpile stockpile;
    private Imp imp;
    private ImpDigger digger;

    [SetUp]
    public void SetUp()
    {
        // Default board: 48x32, the 6x6 Floor cavern at (21,13)-(26,18), the door at (24,31) on the south edge.
        // Depth = 31 - y, so the default deep band (depth >= 20) is rows y <= 11; the cavern is depth 13-18.
        managerGo = new GameObject("TestDungeonManager");
        board = managerGo.AddComponent<DungeonBoard>();
        board.InitializeBoard();
        boardRenderer = managerGo.AddComponent<BoardRenderer>();
        boardRenderer.Board = board;
        stockpile = managerGo.AddComponent<Stockpile>();

        impGo = new GameObject("TestImp");
        imp = impGo.AddComponent<Imp>();
        imp.Board = board;
        imp.Renderer = boardRenderer;
        imp.MoveSpeed = 4f;
        imp.Spawn();
        digger = impGo.AddComponent<ImpDigger>();
        digger.Board = board;
        digger.Imp = imp;
        digger.Stockpile = stockpile;
        digger.DigSecondsPerTile = 1f;
    }

    [TearDown]
    public void TearDown()
    {
        if (impGo != null) Object.DestroyImmediate(impGo);
        if (managerGo != null) Object.DestroyImmediate(managerGo);
    }

    private int Count(MaterialType m) => stockpile.Count(m);

    private MaterialType YieldAt(int x, int y)
    {
        Assert.IsTrue(digger.TryGetTileYield(new Vector2Int(x, y), out MaterialType material, out int amount));
        Assert.AreEqual(1, amount, "The per-tile amount is unchanged");
        return material;
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

    // --- The enum and the catalog ---

    [Test]
    public void TheEnumGrewByAppendage_DeepstoneIsTwo()
    {
        Assert.AreEqual(0, (int)MaterialType.Stone, "Scenes store Stone as 0: never reorder");
        Assert.AreEqual(1, (int)MaterialType.Glimmerstone);
        Assert.AreEqual(2, (int)MaterialType.Deepstone);
    }

    [Test]
    public void TheCatalog_DeepstoneResolves_NamedAndSlate_DarkerThanStone_NotLavender()
    {
        Assert.IsTrue(stockpile.TryGetDefinition(MaterialType.Deepstone, out MaterialDefinition deep));
        Assert.AreEqual("Deepstone", deep.displayName);
        Assert.AreEqual("Deepstone", stockpile.DisplayName(MaterialType.Deepstone));
        Assert.AreEqual(Slate, deep.color);

        Assert.IsTrue(stockpile.TryGetColor(MaterialType.Stone, out Color stone));
        Assert.IsTrue(stockpile.TryGetColor(MaterialType.Glimmerstone, out Color glimmer));
        Assert.AreNotEqual(stone, deep.color);
        Assert.AreNotEqual(glimmer, deep.color);
        Assert.Less(deep.color.grayscale, stone.grayscale - 0.2f, "Clearly darker than stone's gray");
        Assert.Less(deep.color.grayscale, glimmer.grayscale - 0.2f, "And than glimmerstone's lavender");
        Assert.AreEqual(3, stockpile.Definitions.Count, "Stone, Glimmerstone, Deepstone — catalog order");
        Assert.AreEqual(MaterialType.Deepstone, stockpile.Definitions[2].type);
    }

    [Test]
    public void AFreshStockpile_HoldsNoDeepstone_AndItsSeamsRoundTrip()
    {
        Assert.AreEqual(0, Count(MaterialType.Deepstone), "Earned, never given");
        Assert.AreEqual(10, Count(MaterialType.Stone), "Stone's start is unchanged");
        Assert.IsFalse(stockpile.TrySpend(MaterialType.Deepstone, 1));
        stockpile.Add(MaterialType.Deepstone, 3);
        Assert.AreEqual(3, Count(MaterialType.Deepstone));
        Assert.IsTrue(stockpile.TrySpend(MaterialType.Deepstone, 2));
        Assert.AreEqual(1, Count(MaterialType.Deepstone));
        Assert.AreEqual(10, Count(MaterialType.Stone), "Materials don't share a count");
        Assert.AreEqual(0, Count(MaterialType.Glimmerstone));
    }

    // --- The deep band ---

    [Test]
    public void TheYield_DeepstoneFromTheThreshold_StoneAboveIt_ExactEdge()
    {
        Assert.AreEqual(20, digger.DeepstoneDepthThreshold, "Serialized default");
        Assert.AreEqual(19, ImpDigger.DepthOf(board, new Vector2Int(24, 12)));
        Assert.AreEqual(20, ImpDigger.DepthOf(board, new Vector2Int(24, 11)));
        Assert.AreEqual(MaterialType.Stone, YieldAt(24, 12), "Depth 19: the last mid row");
        Assert.AreEqual(MaterialType.Deepstone, YieldAt(24, 11), "Depth 20: the first deep row");
        Assert.AreEqual(MaterialType.Deepstone, YieldAt(10, 0), "The far edge");
    }

    [Test]
    public void TheOtherBands_AreUnchanged_Glimmerstone_Stone_AndTheCavernRows()
    {
        Assert.AreEqual(MaterialType.Glimmerstone, YieldAt(24, 31), "Depth 0");
        Assert.AreEqual(MaterialType.Glimmerstone, YieldAt(23, 26), "Depth 5: the last shallow row");
        Assert.AreEqual(MaterialType.Stone, YieldAt(23, 25), "Depth 6");
        Assert.AreEqual(MaterialType.Stone, YieldAt(27, 16), "Mid-board, beside the cavern");
        Assert.AreEqual(MaterialType.Stone, YieldAt(20, 13), "The cavern's deepest row (depth 18)");
        Assert.AreEqual(MaterialType.Stone, YieldAt(20, 18), "The cavern's shallowest row (depth 13)");
    }

    [Test]
    public void TheThreshold_IsTheSerializedValue()
    {
        digger.DeepstoneDepthThreshold = 25;
        Assert.AreEqual(MaterialType.Stone, YieldAt(24, 11), "Depth 20 is mid-board now");
        Assert.AreEqual(MaterialType.Stone, YieldAt(24, 7), "Depth 24");
        Assert.AreEqual(MaterialType.Deepstone, YieldAt(24, 6), "Depth 25: the moved edge");

        digger.DeepstoneDepthThreshold = 19;
        Assert.AreEqual(MaterialType.Deepstone, YieldAt(24, 12), "Depth 19");
        Assert.AreEqual(MaterialType.Stone, YieldAt(27, 13), "Depth 18");
    }

    [Test]
    public void MaterialAtDepth_IsTheThreeBandRule_DeepFirst()
    {
        Assert.AreEqual(MaterialType.Glimmerstone, ImpDigger.MaterialAtDepth(0, 6, 20));
        Assert.AreEqual(MaterialType.Glimmerstone, ImpDigger.MaterialAtDepth(5, 6, 20));
        Assert.AreEqual(MaterialType.Stone, ImpDigger.MaterialAtDepth(6, 6, 20));
        Assert.AreEqual(MaterialType.Stone, ImpDigger.MaterialAtDepth(19, 6, 20));
        Assert.AreEqual(MaterialType.Deepstone, ImpDigger.MaterialAtDepth(20, 6, 20));
        Assert.AreEqual(MaterialType.Deepstone, ImpDigger.MaterialAtDepth(31, 6, 20));
        Assert.AreEqual(MaterialType.Stone, ImpDigger.MaterialAtDepth(-1, 6, 20), "Off the board: no band");
    }

    [Test]
    public void OverlappingBands_AnswerDeepstone_TheDeepCheckWins()
    {
        Assert.AreEqual(MaterialType.Deepstone, ImpDigger.MaterialAtDepth(4, 6, 3), "In both bands: deep wins");
        Assert.AreEqual(MaterialType.Glimmerstone, ImpDigger.MaterialAtDepth(2, 6, 3), "Shallow band only");

        digger.GlimmerstoneBandRows = 6;
        digger.DeepstoneDepthThreshold = 3;
        Assert.AreEqual(MaterialType.Deepstone, YieldAt(24, 27), "Depth 4, through the seam");
        Assert.AreEqual(MaterialType.Glimmerstone, YieldAt(24, 29), "Depth 2");
    }

    // --- Digging it ---

    [Test]
    public void ACompletedDeepDig_PaysDeepstone_NotStone()
    {
        board.SetTile(24, 12, TileState.Floor); // Staging: opens the row below the cavern (not an imp dig, so it pays nothing)
        Assert.AreEqual(10, Count(MaterialType.Stone));
        board.SetTile(24, 11, TileState.Designated); // Depth 20: the first deep row

        StepUntil(() => board.GetTile(24, 11) == TileState.Floor, 15f, "the deep dig");
        Assert.AreEqual(1, Count(MaterialType.Deepstone), "+1 deepstone");
        Assert.AreEqual(10, Count(MaterialType.Stone), "Stone untouched");
        Assert.AreEqual(0, Count(MaterialType.Glimmerstone));
    }

    [Test]
    public void TheEdgeRows_DugInTurn_PayStoneThenDeepstone()
    {
        board.SetTile(24, 12, TileState.Designated); // Depth 19
        StepUntil(() => board.GetTile(24, 12) == TileState.Floor, 15f, "the row-12 dig");
        Assert.AreEqual(11, Count(MaterialType.Stone));
        Assert.AreEqual(0, Count(MaterialType.Deepstone));

        board.SetTile(24, 11, TileState.Designated); // Depth 20
        StepUntil(() => board.GetTile(24, 11) == TileState.Floor, 15f, "the row-11 dig");
        Assert.AreEqual(11, Count(MaterialType.Stone));
        Assert.AreEqual(1, Count(MaterialType.Deepstone));
    }

    [Test]
    public void AnAbandonedDeepDig_PaysNothing()
    {
        board.SetTile(24, 12, TileState.Floor);
        board.SetTile(24, 11, TileState.Designated);
        StepUntil(() => digger.IsDigging, 15f, "the imp to start the deep dig");
        for (int i = 0; i < 25; i++) Step(); // Halfway
        board.SetTile(24, 11, TileState.Rock); // Designation cleared mid-dig
        for (int i = 0; i < 100; i++) Step();
        Assert.AreEqual(TileState.Rock, board.GetTile(24, 11));
        Assert.AreEqual(0, Count(MaterialType.Deepstone));
        Assert.AreEqual(10, Count(MaterialType.Stone));
    }

    // --- Ordering in it ---

    private PlacementManager WiredPlacement()
    {
        var manager = managerGo.AddComponent<PlacementManager>();
        manager.Board = board;
        manager.Renderer = boardRenderer;
        manager.Stockpile = stockpile;
        manager.LairCotBuildSeconds = 1f;
        digger.PlacementManager = manager; // The imp builds funded sites
        return manager;
    }

    [Test]
    public void TheCostTable_HasADeepstoneRowForEveryType_AtStonesAmounts()
    {
        PlacementManager manager = WiredPlacement();
        foreach (var (type, amount) in new[] { (PlaceableType.LairCot, 3), (PlaceableType.MushroomPlot, 4), (PlaceableType.SpikeTrap, 2) })
        {
            Assert.IsTrue(manager.TryGetCost(type, MaterialType.Deepstone, out MaterialAmount cost), $"{type} is orderable in Deepstone");
            Assert.AreEqual(MaterialType.Deepstone, cost.type);
            Assert.AreEqual(amount, cost.amount);
            Assert.IsTrue(manager.TryGetCost(type, MaterialType.Stone, out MaterialAmount stone));
            Assert.AreEqual(stone.amount, cost.amount, $"{type}: same amount as Stone");
            CollectionAssert.AreEqual(new[] { MaterialType.Stone, MaterialType.Glimmerstone, MaterialType.Deepstone },
                manager.MaterialsFor(type), $"{type}: table order");
            Assert.IsTrue(manager.TryGetDefaultMaterial(type, out MaterialType def));
            Assert.AreEqual(MaterialType.Stone, def, "Stone stays the default");
        }
    }

    [Test]
    public void TheSelector_ListsDeepstone_ForEveryType()
    {
        PlacementManager manager = WiredPlacement();
        var go = new GameObject("TestPlacementController");
        try
        {
            var c = go.AddComponent<PlacementController>();
            c.PlacementManager = manager;
            foreach (PlaceableType type in new[] { PlaceableType.LairCot, PlaceableType.MushroomPlot, PlaceableType.SpikeTrap })
            {
                c.Select(type);
                CollectionAssert.AreEqual(new[] { MaterialType.Stone, MaterialType.Glimmerstone, MaterialType.Deepstone },
                    c.AvailableMaterials, $"{type}");
                Assert.IsTrue(c.SelectMaterial(MaterialType.Deepstone), $"{type}");
                Assert.AreEqual(MaterialType.Deepstone, c.SelectedMaterial);
                c.ClearSelection();
            }
        }
        finally { Object.DestroyImmediate(go); }
    }

    [Test]
    public void ThePropertyTable_HasTheDeepstoneCot_TheBestRest_AndNoOtherDeepstoneRows()
    {
        PlacementManager manager = WiredPlacement();
        Assert.IsTrue(manager.TryGetProperties(PlaceableType.LairCot, MaterialType.Deepstone, out PlaceableProperty deep));
        Assert.AreEqual(1.5f, deep.restRateMultiplier);
        Assert.AreEqual(1f, deep.restQuality);
        Assert.IsFalse(deep.restsMagicalCreatures);

        Assert.IsTrue(manager.TryGetProperties(PlaceableType.LairCot, MaterialType.Stone, out PlaceableProperty stone));
        Assert.IsTrue(manager.TryGetProperties(PlaceableType.LairCot, MaterialType.Glimmerstone, out PlaceableProperty glimmer));
        Assert.Greater(deep.restRateMultiplier, glimmer.restRateMultiplier, "Rests better than glimmerstone");
        Assert.Greater(glimmer.restRateMultiplier, stone.restRateMultiplier);

        Assert.IsFalse(manager.TryGetProperties(PlaceableType.MushroomPlot, MaterialType.Deepstone, out _), "No rest axis: no row");
        Assert.IsFalse(manager.TryGetProperties(PlaceableType.SpikeTrap, MaterialType.Deepstone, out _));
    }

    [Test]
    public void ADeepstoneOrder_StampsDeepstone_FundsFromItsStock_OthersUntouched_AndIsBuiltInIt()
    {
        PlacementManager manager = WiredPlacement();
        stockpile.Add(MaterialType.Deepstone, 5);
        stockpile.Add(MaterialType.Glimmerstone, 4);
        Assert.IsTrue(manager.TryPlace(PlaceableType.LairCot, MaterialType.Deepstone, new Vector2Int(22, 14), new Vector2Int(24, 16), out Placeable cot));
        Assert.AreEqual(MaterialType.Deepstone, cot.OrderedMaterial);
        Assert.AreEqual(3, cot.RequiredAmount);
        Assert.IsTrue(cot.IsFunded, "5 deepstone covers 3");
        Assert.AreEqual(2, Count(MaterialType.Deepstone), "Paid from deepstone: 5 - 3");
        Assert.AreEqual(10, Count(MaterialType.Stone), "Stone untouched");
        Assert.AreEqual(4, Count(MaterialType.Glimmerstone), "Glimmerstone untouched");

        StepUntil(() => cot.IsBuilt, 10f, "the imp to build the deepstone cot");
        Assert.AreEqual(MaterialType.Deepstone, cot.OrderedMaterial, "Made of deepstone once built");
        Assert.IsTrue(manager.TryGetProperties(cot.Type, cot.OrderedMaterial, out PlaceableProperty row));
        Assert.AreEqual(1.5f, row.restRateMultiplier, "The built cot reads the deepstone rest rate");
    }

    [Test]
    public void ADeepstoneOrder_WaitsOnDeepstone_NotStone_BehindASlateGhost()
    {
        PlacementManager manager = WiredPlacement();
        Assert.IsFalse(manager.WouldFundNow(PlaceableType.LairCot, MaterialType.Deepstone), "0 deepstone, though stone is 10");
        Assert.IsTrue(manager.TryPlace(PlaceableType.LairCot, MaterialType.Deepstone, new Vector2Int(22, 14), new Vector2Int(24, 16), out Placeable cot));
        Assert.IsFalse(cot.IsFunded, "Stone can't pay for a deepstone order");
        Assert.AreEqual(10, Count(MaterialType.Stone));
        Assert.AreEqual(Slate, cot.OrderGhostTint, "The ghost wears deepstone's slate");

        stockpile.Add(MaterialType.Deepstone, 3); // E.g. three deep digs
        Assert.AreEqual(1, manager.RunFundingPass());
        Assert.IsTrue(cot.IsFunded);
        Assert.AreEqual(0, Count(MaterialType.Deepstone));
        Assert.AreEqual(10, Count(MaterialType.Stone));
    }
}
