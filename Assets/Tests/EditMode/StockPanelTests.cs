using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The STOCK materials view and the discovery rule (#126): the panel's discovered-only rows under family headers and its
/// filter; the rule enforced at the seams (AvailableMaterials, the fallback material, the rail's "from N", the top-bar
/// kinds); the one-open-thing coordination with the rail and the mode; the Esc chain; WaitingOrders; the detail card's
/// material mode; and the typing guard's testable half. Typing into the field and the wheel are Play checks: EditMode
/// runs no Update, no input, and an InputField only takes focus in its own LateUpdate.
/// </summary>
public class StockPanelTests
{
    private static readonly Vector2Int PlaceFrom = new Vector2Int(24, 16);

    private GameObject managerGo;
    private PlacementManager manager;
    private Stockpile stockpile;
    private PlacementController controller;
    private BuildRail rail;
    private DetailCard card;
    private StockPanel panel;
    private GameObject eventSystemGo;

    [SetUp]
    public void SetUp()
    {
        // Components on one object, matching DungeonManager. Awake/Start don't run in EditMode, so seams are wired here.
        managerGo = new GameObject("TestDungeonManager");
        var board = managerGo.AddComponent<DungeonBoard>();
        board.InitializeBoard();
        var boardRenderer = managerGo.AddComponent<BoardRenderer>();
        boardRenderer.Board = board;
        manager = managerGo.AddComponent<PlacementManager>();
        manager.Board = board;
        manager.Renderer = boardRenderer;
        stockpile = managerGo.AddComponent<Stockpile>(); // The shipped config: 10 stone, so only stone is discovered
        manager.Stockpile = stockpile;

        controller = managerGo.AddComponent<PlacementController>();
        controller.DigDesignator = managerGo.AddComponent<DigDesignator>();
        controller.PlacementManager = manager;

        rail = managerGo.AddComponent<BuildRail>();
        rail.PlacementController = controller;
        rail.PlacementManager = manager;

        card = managerGo.AddComponent<DetailCard>();
        card.PlacementController = controller;
        card.PlacementManager = manager;

        panel = managerGo.AddComponent<StockPanel>();
        panel.PlacementController = controller;
        panel.PlacementManager = manager;
        panel.Stockpile = stockpile;
        panel.DetailCard = card;
        panel.BuildRail = rail;
        rail.StockPanel = panel;
    }

    [TearDown]
    public void TearDown()
    {
        if (panel != null) panel.DestroyChrome();
        if (card != null) card.DestroyChrome();
        if (rail != null) rail.DestroyChrome();
        if (managerGo != null) Object.DestroyImmediate(managerGo);
        if (eventSystemGo != null) Object.DestroyImmediate(eventSystemGo);
    }

    private static List<string> Describe(List<StockPanel.Row> rows)
    {
        var lines = new List<string>();
        foreach (StockPanel.Row row in rows) lines.Add(row.isHeader ? $"[{row.name}]" : $"{row.name} {row.count}");
        return lines;
    }

    private void SetStock(int stone, int glimmerstone = 0, int deepstone = 0) => stockpile.StartingAmounts = new List<MaterialAmount>
    {
        new MaterialAmount(MaterialType.Stone, stone),
        new MaterialAmount(MaterialType.Glimmerstone, glimmerstone),
        new MaterialAmount(MaterialType.Deepstone, deepstone),
    };

    // --- The panel's rows ---

    [Test]
    public void AFreshGame_ListsStoneOnly_UnderTheStonesHeader()
    {
        CollectionAssert.AreEqual(new[] { "[Stones]", "Stone 10" }, Describe(StockPanel.Rows(stockpile, "")));
        Assert.IsFalse(StockPanel.Rows(stockpile, "").Exists(r => !r.isHeader && r.material != MaterialType.Stone),
            "No row, and no placeholder row, for an undiscovered material");
    }

    [Test]
    public void TheRows_AreTheDiscoveredSet_InCatalogOrder_WhateverTheDiscoveryOrder()
    {
        stockpile.Add(MaterialType.Deepstone, 2);
        CollectionAssert.AreEqual(new[] { "[Stones]", "Stone 10", "Deepstone 2" }, Describe(StockPanel.Rows(stockpile, "")));

        stockpile.Add(MaterialType.Glimmerstone, 3);
        CollectionAssert.AreEqual(new[] { "[Stones]", "Stone 10", "Glimmerstone 3", "Deepstone 2" }, Describe(StockPanel.Rows(stockpile, "")));

        // The rows equal the discovered set exactly.
        int materialRows = StockPanel.Rows(stockpile, "").FindAll(r => !r.isHeader).Count;
        Assert.AreEqual(stockpile.DiscoveredKinds, materialRows);
    }

    [Test]
    public void AMaterialSpentToZero_KeepsItsRow_AtZero()
    {
        stockpile.Add(MaterialType.Glimmerstone, 3);
        Assert.IsTrue(stockpile.TrySpend(MaterialType.Glimmerstone, 3));
        CollectionAssert.AreEqual(new[] { "[Stones]", "Stone 10", "Glimmerstone 0" }, Describe(StockPanel.Rows(stockpile, "")));
    }

    [Test]
    public void TheFilter_NarrowsByNameIgnoringCase_AndAFamilyWithNoVisibleRowHasNoHeader()
    {
        CollectionAssert.IsEmpty(StockPanel.Rows(stockpile, "deep"), "Before discovery: nothing, not even the header");

        stockpile.Add(MaterialType.Deepstone, 1);
        stockpile.Add(MaterialType.Glimmerstone, 1);
        CollectionAssert.AreEqual(new[] { "[Stones]", "Deepstone 1" }, Describe(StockPanel.Rows(stockpile, "DEEP")));
        CollectionAssert.AreEqual(new[] { "[Stones]", "Stone 10", "Glimmerstone 1", "Deepstone 1" }, Describe(StockPanel.Rows(stockpile, "stone")),
            "A substring, not a prefix");
        CollectionAssert.AreEqual(new[] { "[Stones]", "Glimmerstone 1" }, Describe(StockPanel.Rows(stockpile, " glim ")), "Trimmed");
        CollectionAssert.IsEmpty(StockPanel.Rows(stockpile, "zzz"));
        Assert.AreEqual(4, StockPanel.Rows(stockpile, null).Count, "No filter: everything discovered");
        CollectionAssert.IsEmpty(StockPanel.Rows(null, ""), "No stockpile: no rows");
    }

    [Test]
    public void AMaterialWithNoPresentationEntry_FallsUnderOther_RatherThanVanishing()
    {
        var unmapped = (MaterialType)99; // Staged: no enum value lacks an entry today
        Assert.AreEqual(MaterialFamily.Other, BuildPresentation.FamilyOf(unmapped));
        Assert.AreEqual(MaterialFamily.Stones, BuildPresentation.FamilyOf(MaterialType.Glimmerstone));
        CollectionAssert.AreEqual(new[] { MaterialFamily.Stones, MaterialFamily.Other }, BuildPresentation.FamilyOrder);

        stockpile.Definitions.Add(new MaterialDefinition(unmapped, "Oddite", Color.green));
        stockpile.Add(unmapped, 4);
        CollectionAssert.AreEqual(new[] { "[Stones]", "Stone 10", "[Other]", "Oddite 4" }, Describe(StockPanel.Rows(stockpile, "")));
    }

    [Test]
    public void ThePresentationCopy_ForEachMaterial()
    {
        Assert.AreEqual("The dungeon's backbone material, dug from the mid depths.", BuildPresentation.MaterialDescription(MaterialType.Stone));
        Assert.AreEqual("Dug · mid depths", BuildPresentation.MaterialSource(MaterialType.Stone));
        Assert.AreEqual("Shallow stone from near the entrance, faintly magical.", BuildPresentation.MaterialDescription(MaterialType.Glimmerstone));
        Assert.AreEqual("Dug · entrance shallows", BuildPresentation.MaterialSource(MaterialType.Glimmerstone));
        Assert.AreEqual("Hard stone from far underground.", BuildPresentation.MaterialDescription(MaterialType.Deepstone));
        Assert.AreEqual("Dug · deep rows", BuildPresentation.MaterialSource(MaterialType.Deepstone));
        Assert.AreEqual("Stones", BuildPresentation.FamilyName(MaterialFamily.Stones));
    }

    // --- The discovery rule at the seams ---

    [Test]
    public void AvailableMaterials_WithAStockpile_AreTheDiscoveredRowsOnly_Unwired_EveryRow()
    {
        controller.Select(PlaceableType.LairCot);
        CollectionAssert.AreEqual(new[] { MaterialType.Stone }, controller.AvailableMaterials, "A fresh game: stone only");
        Assert.IsFalse(controller.SelectMaterial(MaterialType.Deepstone), "An undiscovered material can't be selected");

        stockpile.Add(MaterialType.Deepstone, 1);
        CollectionAssert.AreEqual(new[] { MaterialType.Stone, MaterialType.Deepstone }, controller.AvailableMaterials, "Table order");

        manager.Stockpile = null; // Unwired: every row, as before #126 (and as WouldFundNow degrades)
        CollectionAssert.AreEqual(manager.MaterialsFor(PlaceableType.LairCot), controller.AvailableMaterials);
        Assert.AreEqual(3, controller.AvailableMaterials.Count);
    }

    [Test]
    public void TheCardsChips_ListDiscoveredMaterialsOnly_AndGainOneOnDiscovery()
    {
        controller.Select(PlaceableType.LairCot);
        List<DetailCard.Chip> chips = card.Chips();
        Assert.AreEqual(1, chips.Count, "Stone only");
        Assert.AreEqual(MaterialType.Stone, chips[0].material);

        stockpile.Add(MaterialType.Glimmerstone, 3);
        chips = card.Chips();
        Assert.AreEqual(2, chips.Count);
        Assert.AreEqual(MaterialType.Glimmerstone, chips[1].material);
        Assert.AreEqual(3, chips[1].owned);

        Assert.IsTrue(stockpile.TrySpend(MaterialType.Glimmerstone, 3));
        Assert.AreEqual(2, card.Chips().Count, "Spent to zero, the chip stays");
        Assert.IsTrue(card.SelectChip(MaterialType.Glimmerstone), "And it stays orderable");
        Assert.IsTrue(manager.TryPlace(PlaceableType.LairCot, controller.SelectedMaterial, new Vector2Int(22, 14), PlaceFrom, out Placeable cot));
        Assert.IsFalse(cot.IsFunded, "Place-and-wait");
        Assert.AreEqual(MaterialType.Glimmerstone, cot.OrderedMaterial);
    }

    [Test]
    public void TheFallbackMaterial_IsTheFirstDiscoveredRow_InTableOrder()
    {
        // Glimmerstone first in the table, but undiscovered: the fallback skips it.
        manager.Costs = new List<PlaceableCost>
        {
            new PlaceableCost(PlaceableType.LairCot, MaterialType.Glimmerstone, 3),
            new PlaceableCost(PlaceableType.LairCot, MaterialType.Deepstone, 3),
            new PlaceableCost(PlaceableType.LairCot, MaterialType.Stone, 3),
            new PlaceableCost(PlaceableType.SpikeTrap, MaterialType.Deepstone, 2),
        };
        Assert.IsTrue(controller.SelectMaterial(MaterialType.Glimmerstone), "Nothing armed: any material is accepted");
        controller.Select(PlaceableType.LairCot);
        Assert.AreEqual(MaterialType.Stone, controller.SelectedMaterial, "The current material is undiscovered: first discovered row");
        Assert.AreEqual(MaterialType.Stone, controller.MaterialFor(PlaceableType.LairCot));

        stockpile.Add(MaterialType.Deepstone, 1);
        controller.ClearSelection();
        Assert.IsTrue(controller.SelectMaterial(MaterialType.Glimmerstone));
        controller.Select(PlaceableType.LairCot);
        Assert.AreEqual(MaterialType.Deepstone, controller.SelectedMaterial, "Deepstone now precedes stone among the discovered rows");
    }

    [Test]
    public void TheSafetyNet_ATypeWithNoDiscoveredRow_FallsBackToItsFirstRow_AndStaysOrderable()
    {
        manager.Costs = new List<PlaceableCost>
        {
            new PlaceableCost(PlaceableType.SpikeTrap, MaterialType.Deepstone, 2),
            new PlaceableCost(PlaceableType.SpikeTrap, MaterialType.Glimmerstone, 2),
        };
        controller.Select(PlaceableType.SpikeTrap);
        CollectionAssert.IsEmpty(controller.AvailableMaterials, "No chips: nothing discovered");
        Assert.AreEqual(MaterialType.Deepstone, controller.SelectedMaterial, "Its first row");
        Assert.AreEqual(1, manager.ApplyRect(PlaceableType.SpikeTrap, controller.SelectedMaterial, new RectInt(22, 14, 1, 1), PlaceFrom),
            "Never unorderable");
        Assert.IsTrue(BuildRail.TryGetCheapestCost(manager, PlaceableType.SpikeTrap, out MaterialAmount cost), "The tile still has a cost line");
        Assert.AreEqual(MaterialType.Deepstone, cost.type);
    }

    [Test]
    public void TheShippedConfig_GivesEveryTypeADiscoveredRow_FromTheStart()
    {
        // The invariant the safety net backs up: stone starts held and every type has a stone row.
        foreach (PlaceableType type in System.Enum.GetValues(typeof(PlaceableType)))
        {
            Assert.IsNotEmpty(controller.DiscoveredMaterialsFor(type), $"{type}");
            controller.Select(type);
            Assert.AreEqual(MaterialType.Stone, controller.SelectedMaterial, $"{type}");
        }
    }

    [Test]
    public void TheTilesFromLine_IgnoresACheaperUndiscoveredRow()
    {
        manager.Costs = new List<PlaceableCost>
        {
            new PlaceableCost(PlaceableType.LairCot, MaterialType.Stone, 5),
            new PlaceableCost(PlaceableType.LairCot, MaterialType.Glimmerstone, 2),
        };
        Assert.AreEqual("from 5 Stone", rail.TileCostLine(PlaceableType.LairCot), "Glimmerstone is cheaper but undiscovered");
        stockpile.Add(MaterialType.Glimmerstone, 9);
        Assert.AreEqual("from 2 Glimmerstone", rail.TileCostLine(PlaceableType.LairCot));
    }

    [Test]
    public void TheTopBar_CountsDiscoveredKinds_AndTheTotalOfEveryCount()
    {
        Assert.AreEqual("Materials: 1 kind · 10 total", TopBar.MaterialsSummary(TopBar.CatalogCounts(stockpile)));
        stockpile.Add(MaterialType.Glimmerstone, 3);
        Assert.AreEqual("Materials: 2 kinds · 13 total", TopBar.MaterialsSummary(TopBar.CatalogCounts(stockpile)), "2 of 3 kinds held");
        CollectionAssert.IsEmpty(TopBar.CatalogCounts(null), "Unwired: no kinds");
    }

    // --- WaitingOrders ---

    [Test]
    public void WaitingOrders_CountsOnlyUnfundedOrdersOfThatMaterial()
    {
        SetStock(10, 0, 0);
        stockpile.Discover(MaterialType.Glimmerstone, MaterialType.Deepstone);
        Assert.IsTrue(manager.TryPlace(PlaceableType.LairCot, MaterialType.Stone, new Vector2Int(22, 14), PlaceFrom, out Placeable funded));
        Assert.IsTrue(funded.IsFunded);
        Assert.IsTrue(manager.TryPlace(PlaceableType.LairCot, MaterialType.Glimmerstone, new Vector2Int(22, 15), PlaceFrom, out _));
        Assert.IsTrue(manager.TryPlace(PlaceableType.SpikeTrap, MaterialType.Glimmerstone, new Vector2Int(22, 16), PlaceFrom, out _));
        Assert.IsTrue(manager.TryPlace(PlaceableType.SpikeTrap, MaterialType.Deepstone, new Vector2Int(22, 17), PlaceFrom, out _));

        Assert.AreEqual(0, manager.WaitingOrders(MaterialType.Stone), "Funded orders aren't waiting");
        Assert.AreEqual(2, manager.WaitingOrders(MaterialType.Glimmerstone));
        Assert.AreEqual(1, manager.WaitingOrders(MaterialType.Deepstone), "Other materials' orders are their own");

        stockpile.Add(MaterialType.Glimmerstone, 3);
        manager.RunFundingPass();
        Assert.AreEqual(1, manager.WaitingOrders(MaterialType.Glimmerstone), "The cot funded; the trap still waits");
    }

    // --- The detail card's material mode ---

    [Test]
    public void TheGlimmerstoneCard_HighlightsItsProperties_ListsItsUses_AndCountsWaitingOrders()
    {
        SetStock(10, 0, 0);
        stockpile.Discover(MaterialType.Glimmerstone);
        Assert.IsTrue(manager.TryPlace(PlaceableType.LairCot, MaterialType.Glimmerstone, new Vector2Int(22, 14), PlaceFrom, out _));

        panel.Open();
        Assert.IsTrue(panel.SelectRow(MaterialType.Glimmerstone));
        Assert.IsTrue(card.IsVisible);
        Assert.IsTrue(card.IsMaterialMode);
        Assert.AreEqual("Glimmerstone", card.Title);
        Assert.AreEqual("Stones", card.CategoryTag);
        Assert.AreEqual("0 on hand", card.OnHandLine);
        Assert.AreEqual("Shallow stone from near the entrance, faintly magical.", card.Description);
        Assert.AreEqual("Source: Dug · entrance shallows", card.SourceLine);
        CollectionAssert.AreEqual(new[] { "Lair Cot rest ×1.25", "Lair Cot rests magical creatures" }, card.MaterialHighlights());
        // #142 migration: the Imp Wheel has a Glimmerstone row too.
        CollectionAssert.AreEqual(new[] { "Lair Cot · 3", "Mushroom Plot · 4", "Spike Trap · 2", "Imp Wheel · 4" }, card.UseChipLabels());
        Assert.AreEqual("Waiting orders: 1", card.WaitingLine);

        stockpile.Add(MaterialType.Glimmerstone, 7);
        manager.RunFundingPass();
        Assert.AreEqual("4 on hand", card.OnHandLine, "Live: 7 landed, 3 funded the cot");
        Assert.AreEqual("Waiting orders: 0", card.WaitingLine);
    }

    [Test]
    public void TheStoneCard_HasNoHighlights_TheDeepstoneCard_ItsRestRate()
    {
        CollectionAssert.IsEmpty(DetailCard.MaterialHighlights(manager, MaterialType.Stone), "All-default rows: nothing to highlight");
        CollectionAssert.AreEqual(new[] { "Lair Cot rest ×1.5" }, DetailCard.MaterialHighlights(manager, MaterialType.Deepstone));
        CollectionAssert.IsEmpty(DetailCard.MaterialHighlights(null, MaterialType.Deepstone));

        // restQuality has no consumer: a row differing only there highlights nothing.
        manager.Properties = new List<PlaceableProperty> { new PlaceableProperty(PlaceableType.SpikeTrap, MaterialType.Stone, 1f, 3f, false) };
        CollectionAssert.IsEmpty(DetailCard.MaterialHighlights(manager, MaterialType.Stone));
    }

    [Test]
    public void ArmingATypeTakesTheCardBack_AndClosingThePanelLeavesMaterialMode()
    {
        panel.Open();
        panel.SelectRow(MaterialType.Stone);
        Assert.IsTrue(card.IsMaterialMode);

        controller.Select(PlaceableType.SpikeTrap); // From anywhere: the mode event closes the panel
        Assert.IsFalse(panel.IsOpen);
        Assert.IsFalse(card.IsMaterialMode);
        Assert.IsNull(card.ShownMaterial);
        Assert.AreEqual("Spike Trap", card.Title);

        controller.ClearSelection();
        panel.Open();
        panel.SelectRow(MaterialType.Stone);
        panel.Close();
        Assert.IsFalse(card.IsVisible);
        Assert.IsNull(panel.SelectedMaterial);
    }

    [Test]
    public void SelectingARow_TogglesIt_AndAnUndiscoveredOrClosedRowIsRefused()
    {
        Assert.IsFalse(panel.SelectRow(MaterialType.Stone), "Closed");
        panel.Open();
        Assert.IsFalse(panel.SelectRow(MaterialType.Deepstone), "Undiscovered: there is no row");
        Assert.IsNull(card.ShownMaterial);

        Assert.IsTrue(panel.SelectRow(MaterialType.Stone));
        Assert.AreEqual(MaterialType.Stone, panel.SelectedMaterial);
        Assert.AreEqual(MaterialType.Stone, card.ShownMaterial);
        Assert.IsTrue(panel.SelectRow(MaterialType.Stone), "The selected row again");
        Assert.IsNull(panel.SelectedMaterial, "Deselected");
        Assert.IsFalse(card.IsVisible, "The card hides");
    }

    // --- One open thing ---

    [Test]
    public void OpeningStock_PutsTheToolDown_AndClosesTheFlyout()
    {
        rail.ToggleCategory(BuildCategory.Living);
        rail.SelectTile(PlaceableType.LairCot);
        rail.ToggleStock();
        Assert.IsTrue(panel.IsOpen);
        Assert.IsNull(rail.OpenCategory);
        Assert.AreEqual(PlacementController.ToolMode.Neutral, controller.Mode, "Disarmed");

        panel.Close();
        controller.SelectDig();
        panel.Open();
        Assert.AreEqual(PlacementController.ToolMode.Neutral, controller.Mode, "Dig put down");
        Assert.IsTrue(rail.IsStockOpen);
    }

    [Test]
    public void ACategory_DigOrArming_ClosesStock()
    {
        rail.ToggleStock();
        rail.ToggleCategory(BuildCategory.Farming);
        Assert.IsFalse(panel.IsOpen, "A category");
        Assert.AreEqual(BuildCategory.Farming, rail.OpenCategory);

        rail.ToggleStock();
        Assert.IsNull(rail.OpenCategory, "STOCK closes the flyout");
        rail.ActivateDig();
        Assert.IsFalse(panel.IsOpen, "DIG");

        rail.ToggleStock();
        Assert.IsTrue(panel.IsOpen);
        controller.Select(PlaceableType.MushroomPlot);
        Assert.IsFalse(panel.IsOpen, "Arming");
    }

    [Test]
    public void DigitSix_IsTheStockEntry_AndTogglesThePanel()
    {
        // #142 migration: the Power category takes digit 5, so STOCK moves to digit 6 (index 5).
        Assert.IsTrue(rail.ActivateEntry(5));
        Assert.IsTrue(panel.IsOpen);
        Assert.IsTrue(rail.ActivateEntry(5));
        Assert.IsFalse(panel.IsOpen);

        rail.StockPanel = null;
        Assert.DoesNotThrow(() => rail.ToggleStock(), "No panel wired: no effect");
    }

    // --- Esc ---

    [Test]
    public void Esc_UnwindsOneLevelPerPress_TheSelectedRowThenThePanel()
    {
        panel.Open();
        panel.SelectRow(MaterialType.Stone);

        Assert.IsTrue(panel.HandleAbort());
        Assert.IsTrue(panel.IsOpen, "The first press clears the card");
        Assert.IsNull(panel.SelectedMaterial);
        Assert.IsFalse(card.IsVisible);

        Assert.IsTrue(panel.HandleAbort());
        Assert.IsFalse(panel.IsOpen, "The next closes the panel");
        Assert.IsFalse(panel.HandleAbort(), "Nothing left to unwind");
        Assert.IsFalse(rail.HandleAbort(), "Nor for the rail: no flyout is open");
    }

    [Test]
    public void Esc_OnTheFrameTheControllerSpentIt_IsNotThePanels()
    {
        controller.SelectDig();
        Assert.IsTrue(controller.TryExitDigOnAbort());
        panel.Open();
        Assert.IsFalse(panel.HandleAbort(), "One press, one level");
        Assert.IsTrue(panel.IsOpen);
    }

    // --- The painted panel ---

    private static Transform Find(string path)
    {
        GameObject canvas = GameObject.Find("StockPanelCanvas");
        return canvas != null ? canvas.transform.Find(path) : null;
    }

    private static bool IsShown(string path)
    {
        Transform t = Find(path);
        return t != null && t.gameObject.activeInHierarchy;
    }

    private static Text TextAt(string path)
    {
        Transform t = Find(path);
        Assert.IsNotNull(t, path);
        return t.GetComponent<Text>();
    }

    [Test]
    public void TheBuiltPanel_PaintsTheDiscoveredRows_GreysZero_AndARowClickShowsTheCard()
    {
        const string content = "Panel/List/Content/";
        panel.Build();
        card.Build();
        Assert.IsFalse(IsShown("Panel"), "Closed: hidden");

        panel.Open();
        Assert.IsTrue(IsShown("Panel"));
        Assert.AreEqual(StockPanel.TitleText, TextAt("Panel/Title").text);
        Assert.IsNotNull(Find("Panel/Filter").GetComponent<InputField>());
        Assert.IsNotNull(Find("Panel/List").GetComponent<ScrollRect>());
        Assert.AreEqual(2, Find(content.TrimEnd('/')).childCount, "The Stones header and Stone");
        Assert.AreEqual("STONES", TextAt(content + "StonesHeader/Name").text);
        Assert.AreEqual("Stone", TextAt(content + "StoneRow/Name").text);
        Assert.AreEqual("10", TextAt(content + "StoneRow/Count").text);
        Assert.IsNull(Find(content + "GlimmerstoneRow"), "Undiscovered: no row object at all");

        stockpile.Add(MaterialType.Glimmerstone, 2);
        Assert.IsTrue(stockpile.TrySpend(MaterialType.Glimmerstone, 2));
        panel.Repaint(); // The refresh rhythm's call
        Assert.AreEqual(3, Find(content.TrimEnd('/')).childCount, "Rebuilt on discovery");
        Assert.AreEqual("0", TextAt(content + "GlimmerstoneRow/Count").text);
        Assert.AreEqual(panel.ZeroCountColor, TextAt(content + "GlimmerstoneRow/Count").color, "Zero is greyed");
        Assert.AreEqual(Color.white, TextAt(content + "GlimmerstoneRow/Name").color, "The name stays white");
        Assert.AreEqual(Color.white, TextAt(content + "StoneRow/Count").color);

        Find(content + "GlimmerstoneRow").GetComponent<Button>().onClick.Invoke();
        Assert.AreEqual(MaterialType.Glimmerstone, panel.SelectedMaterial);
        Assert.AreEqual(panel.RowSelectedColor, Find(content + "GlimmerstoneRow").GetComponent<Image>().color);
        Assert.AreEqual(panel.RowColor, Find(content + "StoneRow").GetComponent<Image>().color);

        GameObject cardCanvas = GameObject.Find("DetailCardCanvas");
        Assert.IsTrue(cardCanvas.transform.Find("Card").gameObject.activeInHierarchy, "The card shows the material");
        Assert.AreEqual("Glimmerstone", cardCanvas.transform.Find("Card/Title").GetComponent<Text>().text);
        Assert.AreEqual("0 on hand", cardCanvas.transform.Find("Card/OnHand").GetComponent<Text>().text);
        Assert.AreEqual("Source: Dug · entrance shallows\nLair Cot rest ×1.25\nLair Cot rests magical creatures",
            cardCanvas.transform.Find("Card/Stats").GetComponent<Text>().text);
        Assert.AreEqual("Lair Cot · 3", cardCanvas.transform.Find("Card/Chips/Use0/Name").GetComponent<Text>().text);
        Assert.IsFalse(cardCanvas.transform.Find("Card/Chips/Use0").GetComponent<Button>().interactable, "Display only");
        Assert.AreEqual("Waiting orders: 0", cardCanvas.transform.Find("Card/Waiting").GetComponent<Text>().text);
        Assert.IsFalse(cardCanvas.transform.Find("Card/Hint").gameObject.activeSelf, "The placement hint is buildable mode's");

        panel.SelectRow(MaterialType.Stone);
        Assert.AreEqual("Source: Dug · mid depths", cardCanvas.transform.Find("Card/Stats").GetComponent<Text>().text, "No highlight lines for stone");

        panel.SetFilter("glim");
        Assert.AreEqual("glim", Find("Panel/Filter").GetComponent<InputField>().text, "The field mirrors a code-set filter");
        Assert.AreEqual(2, Find(content.TrimEnd('/')).childCount, "Header and Glimmerstone");

        panel.Close();
        Assert.IsFalse(IsShown("Panel"));
        Assert.AreEqual("", panel.Filter, "A fresh filter on the next open");

        panel.DestroyChrome();
        Assert.IsNull(GameObject.Find("StockPanelCanvas"));
        Assert.DoesNotThrow(() => panel.Repaint(), "With the chrome gone a repaint is a no-op");
    }

    [Test]
    public void TheList_ScrollsPastItsMaximumHeight()
    {
        stockpile.DiscoverAll();
        panel.Build();
        panel.Open();
        Assert.IsFalse(panel.IsListOverflowing, "Four rows fit");

        panel.MaxListHeight = 40f; // Staged: there aren't enough materials to overflow the shipped height
        panel.Repaint();
        Assert.IsTrue(panel.IsListOverflowing);
        Assert.IsTrue(Find("Panel/List").GetComponent<ScrollRect>().vertical);
    }

    [Test]
    public void TheRailsStockEntry_IsTintedWhileThePanelIsOpen()
    {
        rail.Build();
        GameObject entry = GameObject.Find("BuildRailCanvas/Rail/StockEntry");
        Assert.IsNotNull(entry);
        Assert.AreEqual("STOCK", entry.transform.Find("Label").GetComponent<Text>().text);
        Assert.AreEqual(rail.ButtonNormalColor, entry.GetComponent<Image>().color);
        rail.ToggleStock();
        Assert.AreEqual(rail.ButtonSelectedColor, entry.GetComponent<Image>().color);
        rail.ToggleStock();
        Assert.AreEqual(rail.ButtonNormalColor, entry.GetComponent<Image>().color);
    }

    // --- The typing guard (its testable half; the focused field itself is a Play check) ---

    [Test]
    public void TheTypingRule_FocusedNow_OrAsOfTheFrameBefore()
    {
        Assert.IsTrue(TextInputFocus.IsTypingAt(true, int.MinValue, 100));
        Assert.IsTrue(TextInputFocus.IsTypingAt(false, 99, 100), "Focused at the end of last frame: this frame's Esc is the field's");
        Assert.IsTrue(TextInputFocus.IsTypingAt(false, 100, 100));
        Assert.IsFalse(TextInputFocus.IsTypingAt(false, 98, 100), "The press after the defocus reaches the game");
        Assert.IsFalse(TextInputFocus.IsTypingAt(false, int.MinValue, 100), "Never focused");
    }

    [Test]
    public void IsFocused_NeedsAFocusedInputField_AsTheSelectedObject()
    {
        Assert.IsFalse(TextInputFocus.IsFocused(null), "No event system");

        eventSystemGo = new GameObject("TestEventSystem");
        var eventSystem = eventSystemGo.AddComponent<EventSystem>();
        Assert.IsFalse(TextInputFocus.IsFocused(eventSystem), "Nothing selected");

        var button = new GameObject("TestButton");
        button.transform.SetParent(eventSystemGo.transform);
        eventSystem.SetSelectedGameObject(button);
        Assert.IsFalse(TextInputFocus.IsFocused(eventSystem), "A selected object that isn't a field");

        panel.Build();
        panel.Open();
        GameObject field = Find("Panel/Filter").gameObject;
        eventSystem.SetSelectedGameObject(field);
        Assert.AreEqual(field, eventSystem.currentSelectedGameObject);
        Assert.IsFalse(TextInputFocus.IsFocused(eventSystem),
            "Selected but not yet focused: a field takes focus in its own LateUpdate, which EditMode never runs (Play checks the focused case)");
    }
}
