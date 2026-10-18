using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The build rail and the tool model (#123): PlacementController's three modes (Neutral / Dig / Armed) and what they do
/// to DigDesignator, the presentation table, the rail's entries and digits, the flyout's open/close/arm state machine
/// with its Esc partition, and the tiles' cheapest-row cost line. Board presses themselves
/// (a drag that designates nothing in Neutral) are Play checks: EditMode runs no Update and no input.
/// </summary>
public class BuildRailTests
{
    private GameObject managerGo;
    private DungeonBoard board;
    private PlacementManager manager;
    private Stockpile stockpile;
    private DigDesignator dig;
    private PlacementController controller;
    private BuildRail rail;

    [SetUp]
    public void SetUp()
    {
        // Components on one object, matching DungeonManager. Awake/Start don't run in EditMode, so seams are wired here.
        managerGo = new GameObject("TestDungeonManager");
        board = managerGo.AddComponent<DungeonBoard>();
        board.InitializeBoard();
        var boardRenderer = managerGo.AddComponent<BoardRenderer>();
        boardRenderer.Board = board;
        manager = managerGo.AddComponent<PlacementManager>();
        manager.Board = board;
        manager.Renderer = boardRenderer;
        stockpile = managerGo.AddComponent<Stockpile>(); // 10 stone
        manager.Stockpile = stockpile;

        dig = managerGo.AddComponent<DigDesignator>();
        controller = managerGo.AddComponent<PlacementController>();
        controller.DigDesignator = dig;
        controller.PlacementManager = manager;

        rail = managerGo.AddComponent<BuildRail>();
        rail.PlacementController = controller;
        rail.PlacementManager = manager;
    }

    [TearDown]
    public void TearDown()
    {
        if (rail != null) rail.DestroyChrome();
        if (managerGo != null) Object.DestroyImmediate(managerGo);
    }

    private void SetStock(int stone, int glimmerstone = 0) => stockpile.StartingAmounts = new List<MaterialAmount>
    {
        new MaterialAmount(MaterialType.Stone, stone),
        new MaterialAmount(MaterialType.Glimmerstone, glimmerstone),
    };

    private void AssertMode(PlacementController.ToolMode mode, string because = "")
    {
        Assert.AreEqual(mode, controller.Mode, because);
        Assert.AreEqual(mode == PlacementController.ToolMode.Dig, controller.IsDigging, because);
        Assert.AreEqual(mode == PlacementController.ToolMode.Armed, controller.IsPlacing, because);
        Assert.AreEqual(mode == PlacementController.ToolMode.Dig, dig.enabled, "DigDesignator is enabled in Dig and at no other time. " + because);
    }

    // Stages a live dig drag: input can't be driven in EditMode, so the designator's private drag flag is set directly.
    private void StageDigDrag(bool dragging)
    {
        typeof(DigDesignator).GetField("isDragging", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(dig, dragging);
        Assert.AreEqual(dragging, dig.IsDragging, "The IsDragging seam reads the drag flag");
    }

    // --- The tool model ---

    [Test]
    public void AFreshController_IsNeutral_AndApplyingTheModeDisablesDig_WhateverTheSceneCheckbox()
    {
        Assert.IsTrue(dig.enabled, "As in the scene: the checkbox is on");
        Assert.AreEqual(PlacementController.ToolMode.Neutral, controller.Mode);
        Assert.IsNull(controller.SelectedType);

        controller.ApplyMode(); // What Start does
        AssertMode(PlacementController.ToolMode.Neutral);
    }

    [Test]
    public void SelectDig_EntersDig_AndSelectingATypeFromDig_Arms()
    {
        controller.ApplyMode();

        controller.SelectDig();
        AssertMode(PlacementController.ToolMode.Dig);
        Assert.IsNull(controller.SelectedType, "Nothing armed");

        controller.Select(PlaceableType.LairCot);
        AssertMode(PlacementController.ToolMode.Armed);
        Assert.AreEqual(PlaceableType.LairCot, controller.SelectedType);

        controller.SelectDig();
        AssertMode(PlacementController.ToolMode.Dig, "Dig from Armed disarms");
        Assert.IsNull(controller.SelectedType);
    }

    [Test]
    public void ClearSelection_FromArmedAndFromDig_IsNeutral_DigDisabledBothTimes()
    {
        controller.Select(PlaceableType.SpikeTrap);
        controller.ClearSelection();
        AssertMode(PlacementController.ToolMode.Neutral, "From Armed");

        controller.SelectDig();
        controller.ClearSelection();
        AssertMode(PlacementController.ToolMode.Neutral, "From Dig");
    }

    [Test]
    public void EscInDig_ExitsToNeutral_OnlyWhenNoDigDragIsLive()
    {
        controller.SelectDig();

        StageDigDrag(true);
        Assert.IsFalse(controller.TryExitDigOnAbort(), "The press aborts the drag (DigDesignator's own), not the tool");
        AssertMode(PlacementController.ToolMode.Dig);

        StageDigDrag(false);
        Assert.IsTrue(controller.TryExitDigOnAbort());
        AssertMode(PlacementController.ToolMode.Neutral);

        Assert.IsFalse(controller.TryExitDigOnAbort(), "Nothing to exit from Neutral");
        controller.Select(PlaceableType.LairCot);
        Assert.IsFalse(controller.TryExitDigOnAbort(), "Nor from Armed: that Esc is the placement path's");
        AssertMode(PlacementController.ToolMode.Armed);
    }

    [Test]
    public void ModeChanged_FiresOnEveryModeAndMaterialChange()
    {
        int fired = 0;
        controller.ModeChanged += () => fired++;

        controller.SelectDig();
        Assert.AreEqual(1, fired);
        controller.Select(PlaceableType.LairCot);
        Assert.AreEqual(2, fired);
        controller.CycleMaterial();
        Assert.AreEqual(3, fired);
        controller.ClearSelection();
        Assert.AreEqual(4, fired);
    }

    // --- Presentation ---

    [Test]
    public void EveryPlaceableType_HasACategoryNameAndDescription()
    {
        foreach (PlaceableType type in System.Enum.GetValues(typeof(PlaceableType)))
        {
            Assert.IsTrue(BuildPresentation.TryGet(type, out BuildPresentation.Entry entry), $"{type}");
            Assert.AreEqual(type, entry.type);
            Assert.IsNotEmpty(entry.displayName, $"{type}");
            Assert.IsNotEmpty(entry.description, $"{type}");
            Assert.IsTrue(BuildPresentation.TryGetCategory(type, out BuildCategory category));
            Assert.AreEqual(entry.category, category);
            CollectionAssert.Contains(BuildPresentation.TypesInCategory(category), type);
        }

        Assert.AreEqual("Lair Cot", BuildPresentation.DisplayName(PlaceableType.LairCot));
        Assert.AreEqual("Mushroom Plot", BuildPresentation.DisplayName(PlaceableType.MushroomPlot));
        Assert.AreEqual("Spike Trap", BuildPresentation.DisplayName(PlaceableType.SpikeTrap));
        Assert.AreEqual("A bed of piled material. Goblins sleep here to restore energy.", BuildPresentation.Description(PlaceableType.LairCot));
        Assert.AreEqual("Grows food over time, up to 5. Goblins eat from a stocked plot.", BuildPresentation.Description(PlaceableType.MushroomPlot));
        Assert.AreEqual("A one-shot trap. Hurts the first hero to step on it; an imp rearms it.", BuildPresentation.Description(PlaceableType.SpikeTrap));
    }

    [Test]
    public void Categories_AreInOrder_WithTodaysThreeTypes_AndMagicEmpty()
    {
        // #142 migration: Power is seated between Defense and Magic (it holds the Imp Wheel; PowerGridTests pins it).
        CollectionAssert.AreEqual(
            new[] { BuildCategory.Living, BuildCategory.Farming, BuildCategory.Defense, BuildCategory.Power, BuildCategory.Magic },
            BuildPresentation.CategoryOrder);
        CollectionAssert.AreEqual(new[] { PlaceableType.LairCot }, BuildPresentation.TypesInCategory(BuildCategory.Living));
        CollectionAssert.AreEqual(new[] { PlaceableType.MushroomPlot }, BuildPresentation.TypesInCategory(BuildCategory.Farming));
        CollectionAssert.AreEqual(new[] { PlaceableType.SpikeTrap }, BuildPresentation.TypesInCategory(BuildCategory.Defense));
        CollectionAssert.IsEmpty(BuildPresentation.TypesInCategory(BuildCategory.Magic));
        CollectionAssert.AreEqual(
            new[] { BuildCategory.Living, BuildCategory.Farming, BuildCategory.Defense, BuildCategory.Power },
            BuildPresentation.VisibleCategories(), "An empty category is not shown");
        Assert.AreEqual("FARM", BuildPresentation.RailLabel(BuildCategory.Farming));
        Assert.AreEqual("Farming", BuildPresentation.CategoryName(BuildCategory.Farming));
    }

    // --- The rail's entries and digits ---

    [Test]
    public void VisibleEntries_AreDigThenTheNonEmptyCategories_ThenStock()
    {
        // #126 migration: STOCK joins the rail after the categories (it was 4 entries). #142 migration: Power joins
        // after Defense (5 → 6 entries).
        List<BuildRail.RailEntry> entries = BuildRail.VisibleEntries();
        Assert.AreEqual(6, entries.Count, "DIG, Living, Farming, Defense, Power, STOCK: no Magic");
        Assert.IsTrue(entries[0].isDig);
        Assert.IsFalse(entries[1].isDig);
        Assert.AreEqual(BuildCategory.Living, entries[1].category);
        Assert.AreEqual(BuildCategory.Farming, entries[2].category);
        Assert.AreEqual(BuildCategory.Defense, entries[3].category);
        Assert.AreEqual(BuildCategory.Power, entries[4].category);
        Assert.IsTrue(entries[5].isStock);
        Assert.IsFalse(entries[5].isDig);
        for (int i = 0; i < 5; i++) Assert.IsFalse(entries[i].isStock, $"Entry {i}");
    }

    [Test]
    public void Digits_ActivateTheVisibleEntriesInOrder_AndPastTheEndIsANoOp()
    {
        Assert.IsTrue(rail.ActivateEntry(0), "Digit 1: DIG");
        AssertMode(PlacementController.ToolMode.Dig);
        Assert.IsNull(rail.OpenCategory);

        Assert.IsTrue(rail.ActivateEntry(1), "Digit 2: Living");
        Assert.AreEqual(BuildCategory.Living, rail.OpenCategory);
        Assert.IsTrue(rail.ActivateEntry(3), "Digit 4: Defense");
        Assert.AreEqual(BuildCategory.Defense, rail.OpenCategory);
        Assert.IsTrue(rail.ActivateEntry(3), "Again: toggles it closed");
        Assert.IsNull(rail.OpenCategory);

        rail.ActivateEntry(2);
        controller.Select(PlaceableType.MushroomPlot);
        // #126 migration: index 4 (digit 5) is now STOCK, so "past the end" starts at 5. STOCK's digit is StockPanelTests'.
        // #142 migration: Power takes index 4 and STOCK moves to 5, so "past the end" starts at 6.
        foreach (int beyond in new[] { 6, 7, -1 })
        {
            Assert.IsFalse(rail.ActivateEntry(beyond), $"Index {beyond}");
            Assert.AreEqual(BuildCategory.Farming, rail.OpenCategory, "Nothing changed");
            AssertMode(PlacementController.ToolMode.Armed);
        }
    }

    // --- The flyout state machine ---

    [Test]
    public void OpeningACategoryFromDig_PutsDigDown_AndOpensTheFlyout()
    {
        rail.ActivateDig();
        AssertMode(PlacementController.ToolMode.Dig);

        rail.ToggleCategory(BuildCategory.Farming);
        AssertMode(PlacementController.ToolMode.Neutral);
        Assert.AreEqual(BuildCategory.Farming, rail.OpenCategory);
    }

    [Test]
    public void AnEmptyCategory_HasNoFlyout()
    {
        rail.OpenFlyout(BuildCategory.Magic);
        Assert.IsNull(rail.OpenCategory);
        Assert.IsFalse(rail.IsFlyoutOpen);
    }

    [Test]
    public void ATile_ArmsItsType_AndTogglesItOff_TheFlyoutStaysOpen()
    {
        rail.ToggleCategory(BuildCategory.Living);
        rail.SelectTile(PlaceableType.LairCot);
        AssertMode(PlacementController.ToolMode.Armed);
        Assert.AreEqual(PlaceableType.LairCot, controller.SelectedType);
        Assert.AreEqual(BuildCategory.Living, rail.OpenCategory);

        rail.SelectTile(PlaceableType.LairCot);
        AssertMode(PlacementController.ToolMode.Neutral, "The controller's toggle stands");
        Assert.AreEqual(BuildCategory.Living, rail.OpenCategory);
    }

    [Test]
    public void ClosingTheFlyout_DisarmsATypeFromThatCategory_ByToggle_ByDig_AndBySwitchingCategory()
    {
        rail.ToggleCategory(BuildCategory.Living);
        rail.SelectTile(PlaceableType.LairCot);
        rail.ToggleCategory(BuildCategory.Living);
        Assert.IsNull(rail.OpenCategory);
        AssertMode(PlacementController.ToolMode.Neutral, "Toggle");

        rail.ToggleCategory(BuildCategory.Living);
        rail.SelectTile(PlaceableType.LairCot);
        rail.ActivateDig();
        Assert.IsNull(rail.OpenCategory);
        AssertMode(PlacementController.ToolMode.Dig, "DIG closes the flyout and takes the mode");

        rail.ToggleCategory(BuildCategory.Living);
        rail.SelectTile(PlaceableType.LairCot);
        rail.ToggleCategory(BuildCategory.Defense);
        Assert.AreEqual(BuildCategory.Defense, rail.OpenCategory);
        AssertMode(PlacementController.ToolMode.Neutral, "What's open never disagrees with what's armed");
    }

    [Test]
    public void ClosingAFlyout_LeavesATypeFromAnotherCategoryArmed()
    {
        controller.Select(PlaceableType.SpikeTrap); // Armed outside the rail
        rail.ToggleCategory(BuildCategory.Living);
        rail.CloseFlyout();
        Assert.AreEqual(PlaceableType.SpikeTrap, controller.SelectedType);
    }

    [Test]
    public void Esc_UnwindsOneLevelPerPress_ArmedThenFlyout()
    {
        rail.ToggleCategory(BuildCategory.Living);
        rail.SelectTile(PlaceableType.LairCot);

        Assert.IsFalse(rail.HandleAbort(), "Armed: the press is the controller's");
        Assert.AreEqual(BuildCategory.Living, rail.OpenCategory);

        controller.ClearSelection(); // The controller's half of that press
        Assert.AreEqual(BuildCategory.Living, rail.OpenCategory, "Disarmed, flyout still open");

        Assert.IsTrue(rail.HandleAbort(), "Neutral: the next press closes the flyout");
        Assert.IsNull(rail.OpenCategory);
        Assert.IsFalse(rail.HandleAbort(), "Nothing left to unwind");
    }

    [Test]
    public void Esc_OnTheFrameTheControllerSpentIt_DoesNotAlsoCloseTheFlyout()
    {
        // The controller may update before the rail: by then the mode is already Neutral, on the same press.
        controller.SelectDig();
        Assert.IsTrue(controller.TryExitDigOnAbort());
        Assert.IsTrue(controller.ExitedToolThisFrame);

        rail.OpenFlyout(BuildCategory.Living);
        AssertMode(PlacementController.ToolMode.Neutral);
        Assert.IsFalse(rail.HandleAbort(), "One press, one level");
        Assert.AreEqual(BuildCategory.Living, rail.OpenCategory);
    }

    // --- The tiles' cost line ---

    [Test]
    public void TheCheapestRow_IsStone_AtThreeFourTwo()
    {
        foreach (var (type, amount) in new[] { (PlaceableType.LairCot, 3), (PlaceableType.MushroomPlot, 4), (PlaceableType.SpikeTrap, 2) })
        {
            Assert.IsTrue(BuildRail.TryGetCheapestCost(manager, type, out MaterialAmount cost), $"{type}");
            Assert.AreEqual(MaterialType.Stone, cost.type, $"{type}: every material costs the same today, so the first row wins the tie");
            Assert.AreEqual(amount, cost.amount, $"{type}");
        }
        Assert.AreEqual("from 3 Stone", rail.TileCostLine(PlaceableType.LairCot));
        Assert.AreEqual("from 4 Stone", rail.TileCostLine(PlaceableType.MushroomPlot));
        Assert.AreEqual("from 2 Stone", rail.TileCostLine(PlaceableType.SpikeTrap));
    }

    [Test]
    public void TheCheapestRow_IsTheLeastAmount_NotTheFirstRow_AndAMissIsFalse()
    {
        manager.Costs = new List<PlaceableCost>
        {
            new PlaceableCost(PlaceableType.LairCot, MaterialType.Stone, 5),
            new PlaceableCost(PlaceableType.LairCot, MaterialType.Glimmerstone, 2),
            new PlaceableCost(PlaceableType.LairCot, MaterialType.Deepstone, 2),
        };
        stockpile.Discover(MaterialType.Glimmerstone, MaterialType.Deepstone); // #126 migration: only discovered rows compete
        Assert.IsTrue(BuildRail.TryGetCheapestCost(manager, PlaceableType.LairCot, out MaterialAmount cost));
        Assert.AreEqual(MaterialType.Glimmerstone, cost.type, "Least amount; the earlier of the two tied rows");
        Assert.AreEqual(2, cost.amount);

        Assert.IsFalse(BuildRail.TryGetCheapestCost(manager, PlaceableType.SpikeTrap, out _), "No rows");
        Assert.IsFalse(BuildRail.TryGetCheapestCost(null, PlaceableType.LairCot, out _));
        Assert.AreEqual("", rail.TileCostLine(PlaceableType.SpikeTrap));
    }

    [Test]
    public void TheCostLine_ReddensWhenThatRowWouldNotFundNow()
    {
        SetStock(2);
        string red = ColorUtility.ToHtmlStringRGB(rail.UnaffordableCostColor);
        Assert.AreEqual($"from <color=#{red}>3</color> Stone", rail.TileCostLine(PlaceableType.LairCot));
        Assert.AreEqual("from 2 Stone", rail.TileCostLine(PlaceableType.SpikeTrap), "2 covers the trap");
    }

    // --- The painted chrome ---

    // Transform.Find, not GameObject.Find: the latter can return an inactive object when given a full path.
    private static bool IsShown(string path)
    {
        GameObject canvas = GameObject.Find("BuildRailCanvas");
        if (canvas == null) return false;
        Transform t = canvas.transform.Find(path);
        return t != null && t.gameObject.activeInHierarchy;
    }

    private static Text FindText(string path)
    {
        GameObject go = GameObject.Find(path);
        Assert.IsNotNull(go, path);
        return go.GetComponent<Text>();
    }

    private static Color ColorOf(string path)
    {
        GameObject go = GameObject.Find(path);
        Assert.IsNotNull(go, path);
        return go.GetComponent<Image>().color;
    }

    [Test]
    public void TheBuiltChrome_PaintsTheModeTheOpenFlyoutAndTheArmedTile()
    {
        rail.Build();
        Assert.IsFalse(IsShown("Flyout"), "Collapsed: the flyout is hidden");
        foreach (string entry in new[] { "DigEntry", "LivingEntry", "FarmingEntry", "DefenseEntry" })
            Assert.AreEqual(rail.ButtonNormalColor, ColorOf("BuildRailCanvas/Rail/" + entry), entry);
        Assert.IsFalse(IsShown("Rail/MagicEntry"));
        Assert.AreEqual("DIG", FindText("BuildRailCanvas/Rail/DigEntry/Label").text);
        Assert.AreEqual("DEFENSE", FindText("BuildRailCanvas/Rail/DefenseEntry/Label").text);

        rail.ActivateDig();
        Assert.AreEqual(rail.ButtonSelectedColor, ColorOf("BuildRailCanvas/Rail/DigEntry"));

        rail.ToggleCategory(BuildCategory.Living);
        Assert.AreEqual(rail.ButtonNormalColor, ColorOf("BuildRailCanvas/Rail/DigEntry"), "Dig was put down");
        Assert.AreEqual(rail.ButtonSelectedColor, ColorOf("BuildRailCanvas/Rail/LivingEntry"));
        Assert.IsTrue(IsShown("Flyout/LairCotTile"));
        Assert.AreEqual("Living", FindText("BuildRailCanvas/Flyout/Title").text);
        Assert.AreEqual("Lair Cot", FindText("BuildRailCanvas/Flyout/LairCotTile/Name").text);
        Assert.AreEqual("from 3 Stone", FindText("BuildRailCanvas/Flyout/LairCotTile/Cost").text);
        Assert.AreEqual(rail.ButtonNormalColor, ColorOf("BuildRailCanvas/Flyout/LairCotTile"));
        Assert.IsFalse(IsShown("Flyout/MushroomPlotTile"), "Another category's tile is hidden");

        rail.SelectTile(PlaceableType.LairCot);
        Assert.AreEqual(rail.ButtonSelectedColor, ColorOf("BuildRailCanvas/Flyout/LairCotTile"));
        Assert.IsNull(GameObject.Find("BuildRailCanvas").transform.Find("Flyout/MaterialRow"), "The material choice is the detail card's (#125)");

        controller.ClearSelection(); // Not through the rail: a repaint is what the mode event (or the timer) triggers
        rail.Repaint();
        Assert.AreEqual(rail.ButtonNormalColor, ColorOf("BuildRailCanvas/Flyout/LairCotTile"));

        rail.DestroyChrome();
        Assert.IsNull(GameObject.Find("BuildRailCanvas"));
        Assert.DoesNotThrow(() => rail.Repaint(), "With the chrome gone a repaint is a no-op");
    }
}
