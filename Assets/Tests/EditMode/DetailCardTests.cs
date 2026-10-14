using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The detail card (#125): visible exactly while a buildable is armed; the armed type's name, category, description and
/// stat lines (read from PlacementManager, the cot's rest rate from the #110 property table for the selected material);
/// and the material chips, a view over the controller's AvailableMaterials / SelectedMaterial / SelectMaterial with
/// WouldFundNow's redness. The drag that places an order is a Play check: EditMode runs no Update and no input, so the
/// placement here goes through the same PlacementManager.ApplyRect call the controller's drag release makes.
/// </summary>
public class DetailCardTests
{
    private static readonly Vector2Int PlaceFrom = new Vector2Int(24, 16);

    private GameObject managerGo;
    private PlacementManager manager;
    private Stockpile stockpile;
    private PlacementController controller;
    private BuildRail rail;
    private DetailCard card;

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
        stockpile = managerGo.AddComponent<Stockpile>(); // 10 stone
        manager.Stockpile = stockpile;
        // #126 migration: chips list discovered materials only. These pins are about the chips over a type's materials,
        // so every material is discovered here (counts unchanged); the gating itself is StockPanelTests'.
        stockpile.DiscoverAll();

        controller = managerGo.AddComponent<PlacementController>();
        controller.DigDesignator = managerGo.AddComponent<DigDesignator>();
        controller.PlacementManager = manager;

        rail = managerGo.AddComponent<BuildRail>();
        rail.PlacementController = controller;
        rail.PlacementManager = manager;

        card = managerGo.AddComponent<DetailCard>();
        card.PlacementController = controller;
        card.PlacementManager = manager;
    }

    [TearDown]
    public void TearDown()
    {
        if (card != null) card.DestroyChrome();
        if (rail != null) rail.DestroyChrome();
        if (managerGo != null) Object.DestroyImmediate(managerGo);
    }

    private void SetStock(int stone, int glimmerstone = 0, int deepstone = 0)
    {
        stockpile.StartingAmounts = new List<MaterialAmount>
        {
            new MaterialAmount(MaterialType.Stone, stone),
            new MaterialAmount(MaterialType.Glimmerstone, glimmerstone),
            new MaterialAmount(MaterialType.Deepstone, deepstone),
        };
        stockpile.DiscoverAll(); // Re-seeding is a fresh stockpile: discovery is staged again (#126 migration)
    }

    private DetailCard.Chip ChipFor(MaterialType material)
    {
        foreach (DetailCard.Chip chip in card.Chips())
            if (chip.material == material) return chip;
        Assert.Fail($"No {material} chip");
        return default;
    }

    private MaterialType SelectedChip()
    {
        MaterialType? selected = null;
        foreach (DetailCard.Chip chip in card.Chips())
        {
            if (!chip.selected) continue;
            Assert.IsNull(selected, "Exactly one chip is selected");
            selected = chip.material;
        }
        Assert.IsNotNull(selected, "A chip is selected");
        return selected.Value;
    }

    private string StatStartingWith(string prefix)
    {
        foreach (string line in card.StatLines())
            if (line.StartsWith(prefix)) return line;
        return null;
    }

    // --- Visibility ---

    [Test]
    public void TheCard_ShowsExactlyWhileABuildableIsArmed()
    {
        Assert.IsFalse(card.IsVisible, "Neutral");
        Assert.AreEqual("", card.Title);
        CollectionAssert.IsEmpty(card.StatLines());
        CollectionAssert.IsEmpty(card.Chips());

        controller.SelectDig();
        Assert.IsFalse(card.IsVisible, "Dig");

        rail.ToggleCategory(BuildCategory.Living); // Puts dig down: Neutral with the flyout open
        Assert.IsFalse(card.IsVisible, "Flyout open, nothing armed");

        rail.SelectTile(PlaceableType.LairCot);
        Assert.IsTrue(card.IsVisible, "Armed");

        controller.ClearSelection(); // The controller's half of an Esc / right-click unwind
        Assert.IsFalse(card.IsVisible, "After the unwind");
        Assert.AreEqual(BuildCategory.Living, rail.OpenCategory, "The flyout is its own level");
    }

    [Test]
    public void TheCard_HidesWhenTheArmedTypesFlyoutCloses_AndOnTheSwitchToDig()
    {
        rail.ToggleCategory(BuildCategory.Living);
        rail.SelectTile(PlaceableType.LairCot);
        rail.ToggleCategory(BuildCategory.Living); // Re-opening the open category closes it
        Assert.IsFalse(card.IsVisible, "Category toggled closed");

        rail.ToggleCategory(BuildCategory.Living);
        rail.SelectTile(PlaceableType.LairCot);
        rail.ToggleCategory(BuildCategory.Defense);
        Assert.IsFalse(card.IsVisible, "Another category opened");

        rail.SelectTile(PlaceableType.SpikeTrap);
        Assert.IsTrue(card.IsVisible);
        rail.ActivateDig();
        Assert.IsFalse(card.IsVisible, "Dig");
    }

    // --- Header, description, stats ---

    [Test]
    public void TheHeaderAndDescription_ComeFromThePresentationTable()
    {
        foreach (PlaceableType type in System.Enum.GetValues(typeof(PlaceableType)))
        {
            controller.Select(type);
            Assert.AreEqual(BuildPresentation.DisplayName(type), card.Title, $"{type}");
            Assert.AreEqual(BuildPresentation.Description(type), card.Description, $"{type}");
            Assert.IsTrue(BuildPresentation.TryGetCategory(type, out BuildCategory category));
            Assert.AreEqual(BuildPresentation.CategoryName(category), card.CategoryTag, $"{type}");
        }

        controller.Select(PlaceableType.LairCot);
        Assert.AreEqual("Lair Cot", card.Title);
        Assert.AreEqual("Living", card.CategoryTag);
        Assert.AreEqual("A bed of piled material. Goblins sleep here to restore energy.", card.Description);
    }

    [Test]
    public void TheStatsEachTypeLists_AreDeclaredInThePresentationTable()
    {
        CollectionAssert.AreEqual(new[] { BuildStat.RestRate }, BuildPresentation.Stats(PlaceableType.LairCot));
        CollectionAssert.AreEqual(new[] { BuildStat.FoodGrowth, BuildStat.FoodCapacity }, BuildPresentation.Stats(PlaceableType.MushroomPlot));
        CollectionAssert.AreEqual(new[] { BuildStat.TrapDamage }, BuildPresentation.Stats(PlaceableType.SpikeTrap));
    }

    [Test]
    public void TheCotsRestRate_FollowsTheSelectedChip_ReadFromThePropertyTable()
    {
        controller.Select(PlaceableType.LairCot);
        CollectionAssert.AreEqual(new[] { "Build time: 4s", "Rest rate: ×1.0" }, card.StatLines(), "Stone");

        Assert.IsTrue(card.SelectChip(MaterialType.Glimmerstone));
        Assert.AreEqual("Rest rate: ×1.25", StatStartingWith("Rest rate"));
        Assert.IsTrue(card.SelectChip(MaterialType.Deepstone));
        Assert.AreEqual("Rest rate: ×1.5", StatStartingWith("Rest rate"));

        // The line is the table's value, not a string per chip: a staged row changes it.
        Assert.IsTrue(manager.TryGetProperties(PlaceableType.LairCot, MaterialType.Deepstone, out PlaceableProperty row));
        float shipped = row.restRateMultiplier;
        row.restRateMultiplier = 2.75f;
        Assert.AreEqual("Rest rate: ×2.75", StatStartingWith("Rest rate"));
        row.restRateMultiplier = shipped;

        // No row is the baseline rate, as the goblin's sleep reads it.
        manager.Properties = new List<PlaceableProperty>();
        Assert.AreEqual("Rest rate: ×1.0", StatStartingWith("Rest rate"));
    }

    [Test]
    public void ThePlotAndTheTrap_ListTheirOwnStats_FromTheManagersValues_AndNoRestLine()
    {
        controller.Select(PlaceableType.MushroomPlot);
        CollectionAssert.AreEqual(new[] { "Build time: 6s", "Grows 1 food every 30s", "Holds up to 5 food" }, card.StatLines());

        controller.Select(PlaceableType.SpikeTrap);
        CollectionAssert.AreEqual(new[] { "Build time: 5s", "Damage: 1" }, card.StatLines());
        Assert.IsNull(StatStartingWith("Rest rate"));

        manager.SpikeTrapBuildSeconds = 7.5f;
        manager.SpikeDamage = 3;
        CollectionAssert.AreEqual(new[] { "Build time: 7.5s", "Damage: 3" }, card.StatLines(), "The manager's values, read live");

        manager.MushroomGrowthSecondsPerFood = 12f;
        manager.MushroomCapacity = 8;
        controller.Select(PlaceableType.MushroomPlot);
        CollectionAssert.AreEqual(new[] { "Build time: 6s", "Grows 1 food every 12s", "Holds up to 8 food" }, card.StatLines());

        Assert.IsNull(DetailCard.StatLine(BuildStat.TrapDamage, null, PlaceableType.SpikeTrap, MaterialType.Stone), "No manager, no line");
    }

    // --- The chips ---

    [Test]
    public void TheChips_AreTheArmedTypesAvailableMaterials_WithCostAndOwnedCount()
    {
        SetStock(7, 2, 31);
        controller.Select(PlaceableType.MushroomPlot);

        List<DetailCard.Chip> chips = card.Chips();
        List<MaterialType> available = controller.AvailableMaterials;
        Assert.AreEqual(3, available.Count, "Every row material today");
        Assert.AreEqual(available.Count, chips.Count);
        for (int i = 0; i < chips.Count; i++)
        {
            Assert.AreEqual(available[i], chips[i].material, "In AvailableMaterials' order");
            Assert.AreEqual(stockpile.DisplayName(available[i]), chips[i].name);
            Assert.IsTrue(manager.TryGetCost(PlaceableType.MushroomPlot, available[i], out MaterialAmount cost));
            Assert.AreEqual(cost.amount, chips[i].cost);
            Assert.AreEqual(stockpile.Count(available[i]), chips[i].owned);
        }
        Assert.AreEqual(7, ChipFor(MaterialType.Stone).owned);
        Assert.AreEqual(2, ChipFor(MaterialType.Glimmerstone).owned);
        Assert.AreEqual(31, ChipFor(MaterialType.Deepstone).owned);
        Assert.AreEqual(4, ChipFor(MaterialType.Stone).cost);

        stockpile.Add(MaterialType.Glimmerstone, 5);
        Assert.AreEqual(7, ChipFor(MaterialType.Glimmerstone).owned, "Live");

        // The set is the seam's, whatever it holds: a type with one row has one chip.
        manager.Costs = new List<PlaceableCost> { new PlaceableCost(PlaceableType.SpikeTrap, MaterialType.Glimmerstone, 6) };
        controller.Select(PlaceableType.SpikeTrap);
        chips = card.Chips();
        Assert.AreEqual(1, chips.Count);
        Assert.AreEqual(MaterialType.Glimmerstone, chips[0].material);
        Assert.AreEqual(6, chips[0].cost);
    }

    [Test]
    public void AChip_SelectsItsMaterial_AndTheNextPlacementIsStampedWithIt()
    {
        SetStock(10, 10);
        controller.Select(PlaceableType.LairCot);
        Assert.AreEqual(MaterialType.Stone, SelectedChip());

        Assert.IsTrue(card.SelectChip(MaterialType.Glimmerstone));
        Assert.AreEqual(MaterialType.Glimmerstone, controller.SelectedMaterial);
        Assert.AreEqual(MaterialType.Glimmerstone, SelectedChip());

        // The controller's drag release: ApplyRect with the armed type and the selected material.
        var tile = new Vector2Int(22, 14);
        Assert.AreEqual(1, manager.ApplyRect(controller.SelectedType.Value, controller.SelectedMaterial, new RectInt(tile.x, tile.y, 1, 1), PlaceFrom));
        Placeable cot = manager.GetAt(tile);
        Assert.AreEqual(PlaceableType.LairCot, cot.Type);
        Assert.AreEqual(MaterialType.Glimmerstone, cot.OrderedMaterial);
        Assert.AreEqual(7, stockpile.Count(MaterialType.Glimmerstone), "Paid in glimmerstone");
        Assert.AreEqual(10, stockpile.Count(MaterialType.Stone));

        Assert.IsTrue(card.IsVisible, "Still armed after placing");
        Assert.AreEqual(MaterialType.Glimmerstone, SelectedChip(), "The selection persists across placements");
    }

    [Test]
    public void AChipTheTypeHasNoRowFor_IsRefused_AndWithNothingArmedTheCardSelectsNothing()
    {
        Assert.IsFalse(card.SelectChip(MaterialType.Glimmerstone), "Nothing armed");
        Assert.AreEqual(MaterialType.Stone, controller.SelectedMaterial);

        manager.Costs = new List<PlaceableCost> { new PlaceableCost(PlaceableType.SpikeTrap, MaterialType.Stone, 2) };
        controller.Select(PlaceableType.SpikeTrap);
        Assert.IsFalse(card.SelectChip(MaterialType.Deepstone));
        Assert.AreEqual(MaterialType.Stone, controller.SelectedMaterial);
    }

    [Test]
    public void SwitchingTheArmedType_SelectsTheFallbackMaterial_NotAStaleOneTheNewTypeLacks()
    {
        // The trap has no Deepstone row; the cot and the plot do.
        manager.Costs = new List<PlaceableCost>
        {
            new PlaceableCost(PlaceableType.SpikeTrap, MaterialType.Stone, 2),
            new PlaceableCost(PlaceableType.SpikeTrap, MaterialType.Glimmerstone, 2),
            new PlaceableCost(PlaceableType.LairCot, MaterialType.Stone, 3),
            new PlaceableCost(PlaceableType.LairCot, MaterialType.Deepstone, 3),
            new PlaceableCost(PlaceableType.MushroomPlot, MaterialType.Stone, 4),
            new PlaceableCost(PlaceableType.MushroomPlot, MaterialType.Deepstone, 4),
        };

        controller.Select(PlaceableType.LairCot);
        Assert.IsTrue(card.SelectChip(MaterialType.Deepstone));

        controller.Select(PlaceableType.MushroomPlot);
        Assert.AreEqual(MaterialType.Deepstone, SelectedChip(), "A type that has the material keeps it");

        controller.Select(PlaceableType.SpikeTrap);
        Assert.AreEqual(2, card.Chips().Count);
        Assert.AreEqual(MaterialType.Stone, SelectedChip(), "The trap's first row");
        Assert.AreEqual(controller.MaterialFor(PlaceableType.SpikeTrap), SelectedChip());
        Assert.AreEqual(controller.SelectedMaterial, SelectedChip());
    }

    [Test]
    public void AChipsCost_IsRedExactlyWhenAnOrderInThatMaterialWouldWait()
    {
        string red = ColorUtility.ToHtmlStringRGB(card.UnaffordableCostColor);
        SetStock(10, 2);
        controller.Select(PlaceableType.LairCot);

        foreach (DetailCard.Chip chip in card.Chips())
            Assert.AreEqual(manager.WouldFundNow(PlaceableType.LairCot, chip.material), chip.affordable, $"{chip.material}");

        Assert.IsTrue(ChipFor(MaterialType.Stone).affordable);
        Assert.AreEqual("Cost 3 · Own 10", card.ChipCostLine(ChipFor(MaterialType.Stone)));
        Assert.IsFalse(ChipFor(MaterialType.Glimmerstone).affordable, "2 owned, 3 needed");
        Assert.AreEqual($"Cost <color=#{red}>3</color> · Own 2", card.ChipCostLine(ChipFor(MaterialType.Glimmerstone)));
        Assert.AreEqual($"Cost <color=#{red}>3</color> · Own 0", card.ChipCostLine(ChipFor(MaterialType.Deepstone)));

        stockpile.Add(MaterialType.Glimmerstone, 1);
        Assert.AreEqual("Cost 3 · Own 3", card.ChipCostLine(ChipFor(MaterialType.Glimmerstone)), "Follows the stockpile");

        // WouldFundNow's other half: an order already waiting in a material holds its line, whatever the stock.
        Assert.IsTrue(manager.TryPlace(PlaceableType.LairCot, MaterialType.Deepstone, new Vector2Int(22, 14), PlaceFrom, out _));
        stockpile.Add(MaterialType.Deepstone, 2);
        Assert.IsFalse(ChipFor(MaterialType.Deepstone).affordable, "2 owned, and one order waiting");
        Assert.IsTrue(ChipFor(MaterialType.Stone).affordable, "Other materials' lines are their own");
    }

    // --- The painted chrome ---

    private static Transform Find(string path)
    {
        GameObject canvas = GameObject.Find("DetailCardCanvas");
        return canvas != null ? canvas.transform.Find(path) : null;
    }

    // Transform.Find, not GameObject.Find: the latter can return an inactive object when given a full path.
    private static bool IsShown(string path)
    {
        Transform t = Find(path);
        return t != null && t.gameObject.activeInHierarchy;
    }

    private static string TextOf(string path)
    {
        Transform t = Find(path);
        Assert.IsNotNull(t, path);
        return t.GetComponent<Text>().text;
    }

    private static Color ColorOf(string path)
    {
        Transform t = Find(path);
        Assert.IsNotNull(t, path);
        return t.GetComponent<Image>().color;
    }

    private static void Click(string path)
    {
        Transform t = Find(path);
        Assert.IsNotNull(t, path);
        t.GetComponent<Button>().onClick.Invoke();
    }

    [Test]
    public void TheBuiltCard_PaintsTheArmedType_AndAChipClickMovesTheSelection()
    {
        card.Build();
        Assert.IsFalse(IsShown("Card"), "Nothing armed: hidden");

        controller.Select(PlaceableType.LairCot); // Not through the card: the mode event (or the timer) repaints
        card.Repaint();
        Assert.IsTrue(IsShown("Card"));
        Assert.AreEqual("Lair Cot", TextOf("Card/Title"));
        Assert.AreEqual("Living", TextOf("Card/Tag"));
        Assert.AreEqual("A bed of piled material. Goblins sleep here to restore energy.", TextOf("Card/Description"));
        Assert.AreEqual("Build time: 4s\nRest rate: ×1.0", TextOf("Card/Stats"));
        Assert.AreEqual(DetailCard.HintText, TextOf("Card/Hint"));

        Assert.AreEqual(3, Find("Card/Chips").childCount);
        Assert.AreEqual("Stone", TextOf("Card/Chips/StoneChip/Name"));
        Assert.AreEqual("Cost 3 · Own 10", TextOf("Card/Chips/StoneChip/Cost"));
        Assert.AreEqual(card.ChipSelectedColor, ColorOf("Card/Chips/StoneChip"));
        Assert.AreEqual(card.ChipColor, ColorOf("Card/Chips/GlimmerstoneChip"));
        Assert.IsTrue(stockpile.TryGetColor(MaterialType.Glimmerstone, out Color glimmer));
        Assert.AreEqual(glimmer, ColorOf("Card/Chips/GlimmerstoneChip/Swatch"), "The material's catalog color");

        Click("Card/Chips/GlimmerstoneChip");
        Assert.AreEqual(MaterialType.Glimmerstone, controller.SelectedMaterial);
        Assert.AreEqual(card.ChipColor, ColorOf("Card/Chips/StoneChip"));
        Assert.AreEqual(card.ChipSelectedColor, ColorOf("Card/Chips/GlimmerstoneChip"));
        Assert.AreEqual("Build time: 4s\nRest rate: ×1.25", TextOf("Card/Stats"), "The stats follow the chip");

        controller.Select(PlaceableType.SpikeTrap);
        card.Repaint();
        Assert.AreEqual("Spike Trap", TextOf("Card/Title"));
        Assert.AreEqual("Defense", TextOf("Card/Tag"));
        Assert.AreEqual("Build time: 5s\nDamage: 1", TextOf("Card/Stats"));
        Assert.AreEqual("Cost 2 · Own 10", TextOf("Card/Chips/StoneChip/Cost"));

        controller.ClearSelection();
        card.Repaint();
        Assert.IsFalse(IsShown("Card"));

        card.DestroyChrome();
        Assert.IsNull(GameObject.Find("DetailCardCanvas"));
        Assert.DoesNotThrow(() => card.Repaint(), "With the chrome gone a repaint is a no-op");
    }

    [Test]
    public void TheChipObjects_FollowTheChipCount_WhenTheArmedTypeChanges()
    {
        manager.Costs = new List<PlaceableCost>
        {
            new PlaceableCost(PlaceableType.LairCot, MaterialType.Stone, 3),
            new PlaceableCost(PlaceableType.LairCot, MaterialType.Glimmerstone, 3),
            new PlaceableCost(PlaceableType.LairCot, MaterialType.Deepstone, 3),
            new PlaceableCost(PlaceableType.SpikeTrap, MaterialType.Deepstone, 2),
        };
        card.Build();

        controller.Select(PlaceableType.LairCot);
        card.Repaint();
        Assert.AreEqual(3, Find("Card/Chips").childCount);

        controller.Select(PlaceableType.SpikeTrap);
        card.Repaint();
        Assert.AreEqual(1, Find("Card/Chips").childCount);
        Assert.AreEqual("Deepstone", TextOf("Card/Chips/DeepstoneChip/Name"));
        Assert.AreEqual(card.ChipSelectedColor, ColorOf("Card/Chips/DeepstoneChip"), "The trap's only material is the selected one");

        controller.Select(PlaceableType.MushroomPlot); // No rows: no chips, and the card still paints
        Assert.DoesNotThrow(() => card.Repaint());
        Assert.AreEqual(0, Find("Card/Chips").childCount);
        Assert.AreEqual("Mushroom Plot", TextOf("Card/Title"));
    }

    [Test]
    public void AnEmptiedOrMissingChipArray_IsRebuilt_NoException()
    {
        // The #83 condition: under no-domain-reload a recompile can leave the array empty (or null) while the chrome
        // stands. Staged by reflection; the next repaint must neither index it nor leave the chips stale.
        FieldInfo field = typeof(DetailCard).GetField("chipViews", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field);
        card.Build();
        controller.Select(PlaceableType.LairCot);
        card.Repaint();

        foreach (bool empty in new[] { true, false })
        {
            field.SetValue(card, empty ? System.Array.CreateInstance(field.FieldType.GetElementType(), 0) : null);
            Assert.DoesNotThrow(() => card.Repaint(), empty ? "Empty array" : "Null array");
            Assert.AreEqual(3, Find("Card/Chips").childCount, "Rebuilt, not doubled");
            Click("Card/Chips/DeepstoneChip");
            Assert.AreEqual(MaterialType.Deepstone, controller.SelectedMaterial, "And the rebuilt chips work");
            Assert.IsTrue(card.SelectChip(MaterialType.Stone));
        }
    }
}
