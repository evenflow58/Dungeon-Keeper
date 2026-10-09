using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Choosing the material at order time (#109): the type x material cost rows, orders stamped with the chosen material
/// (kept after construction), the bar's material selector, the material-tinted order marker, and the whole-catalog
/// TopBar readout. Only Stone exists until B3, so multi-material behavior is covered at the seams (staged rows and
/// formatter inputs), not end-to-end — no fake material is invented to force it.
/// </summary>
public class MaterialChoiceTests
{
    private static readonly Vector2Int PlaceFrom = new Vector2Int(24, 16);

    private GameObject managerGo;
    private GameObject controllerGo;
    private DungeonBoard board;
    private PlacementManager manager;
    private Stockpile stockpile;

    [SetUp]
    public void SetUp()
    {
        managerGo = new GameObject("TestDungeonManager");
        board = managerGo.AddComponent<DungeonBoard>();
        board.InitializeBoard();
        var boardRenderer = managerGo.AddComponent<BoardRenderer>();
        boardRenderer.Board = board;
        manager = managerGo.AddComponent<PlacementManager>();
        manager.Board = board;
        manager.Renderer = boardRenderer;
        stockpile = managerGo.AddComponent<Stockpile>(); // 10 stone; Stone's catalog color
        manager.Stockpile = stockpile;
    }

    [TearDown]
    public void TearDown()
    {
        if (controllerGo != null) Object.DestroyImmediate(controllerGo);
        if (managerGo != null) Object.DestroyImmediate(managerGo);
    }

    private void SetStock(int stone) => stockpile.StartingAmounts = new List<MaterialAmount> { new MaterialAmount(MaterialType.Stone, stone) };

    // --- The cost rows ---

    [Test]
    public void ShippedRows_EveryTypeInStone_AtTheCurrentCosts()
    {
        Assert.AreEqual(3, manager.Costs.Count);
        foreach (var (type, amount) in new[] { (PlaceableType.LairCot, 3), (PlaceableType.MushroomPlot, 4), (PlaceableType.SpikeTrap, 2) })
        {
            Assert.IsTrue(manager.TryGetCost(type, MaterialType.Stone, out MaterialAmount cost), $"{type}");
            Assert.AreEqual(MaterialType.Stone, cost.type);
            Assert.AreEqual(amount, cost.amount);
            CollectionAssert.AreEqual(new[] { MaterialType.Stone }, manager.MaterialsFor(type), $"{type}: orderable in exactly its row materials");
            Assert.IsTrue(manager.TryGetDefaultMaterial(type, out MaterialType def));
            Assert.AreEqual(MaterialType.Stone, def);
        }
    }

    [Test]
    public void AnUnorderablePair_ReportsNotOrderable_WithoutThrowing_AndCantBePlaced()
    {
        // Staged by removing the cot's Stone row (no second material exists to leave unlisted).
        manager.Costs = new List<PlaceableCost> { new PlaceableCost(PlaceableType.MushroomPlot, MaterialType.Stone, 4) };
        Assert.DoesNotThrow(() => manager.TryGetCost(PlaceableType.LairCot, MaterialType.Stone, out _));
        Assert.IsFalse(manager.TryGetCost(PlaceableType.LairCot, MaterialType.Stone, out MaterialAmount none));
        Assert.IsNull(none);
        CollectionAssert.IsEmpty(manager.MaterialsFor(PlaceableType.LairCot));
        Assert.IsFalse(manager.TryGetDefaultMaterial(PlaceableType.LairCot, out _));
        Assert.IsFalse(manager.WouldFundNow(PlaceableType.LairCot, MaterialType.Stone));
        Assert.IsFalse(manager.TryPlace(PlaceableType.LairCot, MaterialType.Stone, new Vector2Int(22, 14), PlaceFrom, out _), "No row, no order");
        Assert.IsFalse(manager.TryPlace(PlaceableType.LairCot, new Vector2Int(22, 14), PlaceFrom, out _));
        Assert.IsFalse(manager.IsOccupied(new Vector2Int(22, 14)));
        Assert.IsTrue(manager.TryPlace(PlaceableType.MushroomPlot, MaterialType.Stone, new Vector2Int(22, 14), PlaceFrom, out _), "Other rows still order");
    }

    // --- The order records its material ---

    [Test]
    public void PlacingInAChosenMaterial_StampsThatRowsRequirement_AndTheMaterialSurvivesConstruction()
    {
        // A non-default cost row, staged (the material is still Stone: it's the only one).
        manager.Costs = new List<PlaceableCost> { new PlaceableCost(PlaceableType.LairCot, MaterialType.Stone, 7) };
        Assert.IsTrue(manager.TryPlace(PlaceableType.LairCot, MaterialType.Stone, new Vector2Int(22, 14), PlaceFrom, out Placeable cot));
        Assert.AreEqual(MaterialType.Stone, cot.OrderedMaterial);
        Assert.AreEqual(7, cot.RequiredAmount, "The chosen row's amount");
        Assert.AreEqual(3, stockpile.Count(MaterialType.Stone), "Funded from that material: 10 - 7");

        cot.CompleteConstruction();
        Assert.AreEqual(MaterialType.Stone, cot.OrderedMaterial, "Still made of it once built");
    }

    [Test]
    public void ThePlainPlacement_UsesTheTypesDefaultMaterial_Stone()
    {
        Assert.IsTrue(manager.TryPlace(PlaceableType.SpikeTrap, new Vector2Int(22, 14), PlaceFrom, out Placeable trap));
        Assert.AreEqual(MaterialType.Stone, trap.OrderedMaterial);
        Assert.AreEqual(2, trap.RequiredAmount);
    }

    // --- The selector ---

    private PlacementController Controller()
    {
        controllerGo = new GameObject("TestPlacementController");
        var c = controllerGo.AddComponent<PlacementController>();
        c.PlacementManager = manager;
        return c;
    }

    [Test]
    public void Selector_ListsExactlyTheSelectedTypesRowMaterials_DefaultStone()
    {
        PlacementController c = Controller();
        Assert.AreEqual(MaterialType.Stone, c.SelectedMaterial, "Default");
        CollectionAssert.IsEmpty(c.AvailableMaterials, "No type selected");

        c.Select(PlaceableType.MushroomPlot);
        CollectionAssert.AreEqual(manager.MaterialsFor(PlaceableType.MushroomPlot), c.AvailableMaterials);
        Assert.AreEqual(MaterialType.Stone, c.SelectedMaterial);

        c.CycleMaterial(); // One material: cycling stays put
        Assert.AreEqual(MaterialType.Stone, c.SelectedMaterial);
        Assert.IsTrue(c.SelectMaterial(MaterialType.Stone));
    }

    [Test]
    public void Selector_ATypeWithoutTheCurrentMaterial_RefusesIt()
    {
        manager.Costs = new List<PlaceableCost> { new PlaceableCost(PlaceableType.MushroomPlot, MaterialType.Stone, 4) };
        PlacementController c = Controller();
        c.Select(PlaceableType.LairCot); // No rows at all
        CollectionAssert.IsEmpty(c.AvailableMaterials);
        Assert.IsFalse(c.SelectMaterial(MaterialType.Stone), "Not orderable in Stone");
    }

    // --- The marker wears the material ---

    [Test]
    public void MarkerTint_IsTheMaterialsCatalogColor_CyanFallback()
    {
        Color cyan = new Color(0.45f, 0.80f, 1.00f, 1f);
        Assert.IsTrue(stockpile.TryGetColor(MaterialType.Stone, out Color stone));
        Assert.AreEqual(stone, Placeable.MarkerTintFor(stockpile, MaterialType.Stone, cyan), "The catalog color");
        Assert.AreEqual(cyan, Placeable.MarkerTintFor(null, MaterialType.Stone, cyan), "No stockpile: fallback");
        stockpile.Definitions = new List<MaterialDefinition>();
        Assert.AreEqual(cyan, Placeable.MarkerTintFor(stockpile, MaterialType.Stone, cyan), "No definition: fallback");
    }

    [Test]
    public void AWaitingOrder_GhostWearsItsMaterial_TheOutlineStaysBlueprint()
    {
        SetStock(0);
        Assert.IsTrue(manager.TryPlace(PlaceableType.LairCot, MaterialType.Stone, new Vector2Int(22, 14), PlaceFrom, out Placeable cot));
        Assert.IsFalse(cot.IsFunded);
        stockpile.TryGetColor(MaterialType.Stone, out Color stone);
        Assert.AreEqual(stone, cot.OrderGhostTint, "The ghost body is tinted by Stone's catalog color");

        // A ghost part's color is its own pulled 60% toward the tint; the outline is the blueprint fallback.
        Transform frame = cot.OrderMarker.transform.Find("Frame");
        Color ghost = frame.GetComponent<MeshRenderer>().sharedMaterial.GetColor("_BaseColor");
        Color frameWood = cot.Model.transform.Find("Frame").GetComponent<MeshRenderer>().sharedMaterial.GetColor("_BaseColor");
        Color expected = Color.Lerp(frameWood, stone, 0.6f);
        Assert.AreEqual(expected.r, ghost.r, 1e-3f);
        Assert.AreEqual(expected.g, ghost.g, 1e-3f);
        Assert.AreEqual(expected.b, ghost.b, 1e-3f);
        Color outline = cot.OrderMarker.transform.Find("Outline").GetChild(0).GetComponent<MeshRenderer>().sharedMaterial.GetColor("_BaseColor");
        Color cyan = cot.OrderMarkerFallbackTint;
        Assert.AreEqual(cyan.r, outline.r, 1e-3f, "The outline stays blueprint cyan");
        Assert.AreEqual(cyan.b, outline.b, 1e-3f);
    }

    // --- The TopBar reads the catalog ---

    [Test]
    public void MaterialsReadout_OneMaterialAsBefore_TwoInCatalogOrder()
    {
        Assert.AreEqual("Stone: 10", TopBar.MaterialsReadout(new List<(string, int)> { ("Stone", 10) }));
        Assert.AreEqual("Stone: 10 · Deepstone: 3", TopBar.MaterialsReadout(new List<(string, int)> { ("Stone", 10), ("Deepstone", 3) }),
            "Staged second segment (a name and a count, no fake material)");
        Assert.AreEqual("Stone: 0", TopBar.MaterialsReadout(new List<(string, int)>()), "Nothing: degrades as before");
        Assert.AreEqual("Stone: 0", TopBar.MaterialsReadout(null));
    }
}
