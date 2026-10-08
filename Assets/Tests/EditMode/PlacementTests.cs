using NUnit.Framework;
using UnityEngine;

public class PlacementTests
{
    private static readonly Vector2Int ImpTile = new Vector2Int(24, 16); // Imp spawn: cavern center

    private GameObject managerGo;
    private DungeonBoard board;
    private BoardRenderer boardRenderer;
    private PlacementManager manager;

    [SetUp]
    public void SetUp()
    {
        // Default board: 48x32 Rock with the 6x6 Floor cavern at (21,13)-(26,18).
        // Components on one object, matching DungeonManager. Awake/Start don't run in EditMode,
        // so seams are wired explicitly.
        managerGo = new GameObject("TestDungeonManager");
        board = managerGo.AddComponent<DungeonBoard>();
        board.InitializeBoard();
        boardRenderer = managerGo.AddComponent<BoardRenderer>();
        boardRenderer.Board = board;
        manager = managerGo.AddComponent<PlacementManager>();
        manager.Board = board;
        manager.Renderer = boardRenderer;
    }

    [TearDown]
    public void TearDown()
    {
        // Placed objects live under a holder parented to the manager, so this cleans them up too.
        if (managerGo != null) Object.DestroyImmediate(managerGo);
    }

    // --- CanPlace ---

    [Test]
    public void CanPlace_ReachableCavernFloor_True()
    {
        Assert.IsTrue(manager.CanPlace(new Vector2Int(22, 14), ImpTile));
        Assert.IsTrue(manager.CanPlace(ImpTile, ImpTile), "The imp's own tile is reachable");
    }

    [Test]
    public void CanPlace_Rock_False()
    {
        Assert.IsFalse(manager.CanPlace(new Vector2Int(10, 10), ImpTile));
        Assert.IsFalse(manager.CanPlace(new Vector2Int(27, 16), ImpTile), "Rock right next to the cavern");
    }

    [Test]
    public void CanPlace_Designated_False()
    {
        board.SetTile(27, 16, TileState.Designated);
        Assert.IsFalse(manager.CanPlace(new Vector2Int(27, 16), ImpTile));
    }

    [Test]
    public void CanPlace_OutOfBounds_False()
    {
        Assert.IsFalse(manager.CanPlace(new Vector2Int(-1, 16), ImpTile));
        Assert.IsFalse(manager.CanPlace(new Vector2Int(48, 16), ImpTile));
        Assert.IsFalse(manager.CanPlace(new Vector2Int(24, 32), ImpTile));
    }

    [Test]
    public void CanPlace_UnreachableFloorPocket_False()
    {
        board.SetTile(5, 5, TileState.Floor); // isolated: no connection to the cavern
        Assert.IsTrue(board.IsWalkable(5, 5));
        Assert.IsFalse(manager.CanPlace(new Vector2Int(5, 5), ImpTile));
    }

    [Test]
    public void CanPlace_Occupied_False()
    {
        var tile = new Vector2Int(22, 14);
        Assert.IsTrue(manager.TryPlace(PlaceableType.SpikeTrap, tile, ImpTile, out _));
        Assert.IsFalse(manager.CanPlace(tile, ImpTile));
    }

    // --- TryPlace / registry ---

    [Test]
    public void TryPlace_RegistersPlaceableWithTypeAndTile()
    {
        var tile = new Vector2Int(22, 14);
        Assert.IsTrue(manager.TryPlace(PlaceableType.LairCot, tile, ImpTile, out Placeable cot));

        Assert.IsNotNull(cot);
        Assert.AreEqual(PlaceableType.LairCot, cot.Type);
        Assert.AreEqual(tile, cot.Tile);
        Assert.AreSame(cot, manager.GetAt(tile));
        Assert.IsTrue(manager.IsOccupied(tile));
        Assert.AreEqual(1, manager.Count);
        Assert.AreEqual(1, manager.CountOfType(PlaceableType.LairCot));
        Assert.AreEqual(0, manager.CountOfType(PlaceableType.MushroomPlot));
        Assert.AreEqual(0, manager.CountOfType(PlaceableType.SpikeTrap));
        Assert.AreEqual(boardRenderer.GetTileCenterWorldPosition(22, 14), cot.transform.position);
    }

    [Test]
    public void TryPlace_SameTileTwice_SecondFails()
    {
        var tile = new Vector2Int(22, 14);
        Assert.IsTrue(manager.TryPlace(PlaceableType.LairCot, tile, ImpTile, out Placeable first));
        Assert.IsFalse(manager.TryPlace(PlaceableType.MushroomPlot, tile, ImpTile, out Placeable second));

        Assert.IsNull(second);
        Assert.AreEqual(1, manager.Count);
        Assert.AreSame(first, manager.GetAt(tile));
    }

    [Test]
    public void TryPlace_InvalidTile_PlacesNothing()
    {
        Assert.IsFalse(manager.TryPlace(PlaceableType.LairCot, new Vector2Int(10, 10), ImpTile, out Placeable p));
        Assert.IsNull(p);
        Assert.AreEqual(0, manager.Count);
        Assert.IsNull(manager.GetAt(new Vector2Int(10, 10)));
    }

    [Test]
    public void TryPlace_LeavesTileFloorAndWalkable()
    {
        var tile = new Vector2Int(25, 16);
        manager.TryPlace(PlaceableType.SpikeTrap, tile, ImpTile, out _);

        Assert.AreEqual(TileState.Floor, board.GetTile(tile.x, tile.y));
        Assert.IsTrue(board.IsWalkable(tile.x, tile.y));
        // Paths still run straight through the occupied tile.
        var path = Pathfinder.FindPath(board, ImpTile, new Vector2Int(26, 16));
        Assert.AreEqual(3, path.Count);
        Assert.AreEqual(tile, path[1]);
    }

    // --- ApplyRect ---

    [Test]
    public void ApplyRect_StraddlingCavernEdge_PlacesOnlyFloorTiles()
    {
        // x 25-27, y 15-16: x 25-26 are cavern Floor (4 tiles), x 27 is Rock (2 tiles).
        int placed = manager.ApplyRect(PlaceableType.MushroomPlot, new RectInt(25, 15, 3, 2), ImpTile);

        Assert.AreEqual(4, placed);
        Assert.AreEqual(4, manager.Count);
        Assert.AreEqual(4, manager.CountOfType(PlaceableType.MushroomPlot));
        Assert.IsTrue(manager.IsOccupied(new Vector2Int(25, 15)));
        Assert.IsTrue(manager.IsOccupied(new Vector2Int(26, 16)));
        Assert.IsFalse(manager.IsOccupied(new Vector2Int(27, 15)));
        Assert.IsFalse(manager.IsOccupied(new Vector2Int(27, 16)));
    }

    [Test]
    public void ApplyRect_AllRock_ReturnsZero()
    {
        Assert.AreEqual(0, manager.ApplyRect(PlaceableType.LairCot, new RectInt(0, 0, 3, 3), ImpTile));
        Assert.AreEqual(0, manager.Count);
    }

    [Test]
    public void ApplyRect_SkipsOccupiedTiles()
    {
        manager.TryPlace(PlaceableType.LairCot, new Vector2Int(25, 15), ImpTile, out _);

        int placed = manager.ApplyRect(PlaceableType.SpikeTrap, new RectInt(25, 15, 2, 2), ImpTile);

        Assert.AreEqual(3, placed);
        Assert.AreEqual(4, manager.Count);
        Assert.AreEqual(PlaceableType.LairCot, manager.GetAt(new Vector2Int(25, 15)).Type, "Existing placeable untouched");
    }

    [Test]
    public void AnyPlaceable_MatchesWhetherRectHasAValidTile()
    {
        Assert.IsTrue(manager.AnyPlaceable(new RectInt(25, 15, 3, 2), ImpTile));
        Assert.IsFalse(manager.AnyPlaceable(new RectInt(0, 0, 3, 3), ImpTile));
    }

    // --- Lair Cot claim ---

    [Test]
    public void LairCot_ClaimRelease()
    {
        manager.TryPlaceBuilt(PlaceableType.LairCot, new Vector2Int(22, 14), ImpTile, out Placeable cot); // Only a built cot is claimable (#91)

        Assert.IsFalse(cot.IsClaimed);
        Assert.IsTrue(cot.TryClaim());
        Assert.IsTrue(cot.IsClaimed);
        Assert.IsFalse(cot.TryClaim(), "Already claimed");

        cot.Release();
        Assert.IsFalse(cot.IsClaimed);
        Assert.IsTrue(cot.TryClaim(), "Claimable again after release");
    }

    [Test]
    public void TryClaim_NonCotTypes_Fails()
    {
        manager.TryPlace(PlaceableType.MushroomPlot, new Vector2Int(22, 14), ImpTile, out Placeable plot);
        manager.TryPlace(PlaceableType.SpikeTrap, new Vector2Int(23, 14), ImpTile, out Placeable trap);

        Assert.IsFalse(plot.TryClaim());
        Assert.IsFalse(trap.TryClaim());
        Assert.IsFalse(plot.IsClaimed);
        Assert.IsFalse(trap.IsClaimed);
    }

    // --- Mode arbitration ---

    [Test]
    public void Select_DisablesDigDesignator_ClearSelectionRestoresIt()
    {
        var dig = managerGo.AddComponent<DigDesignator>();
        var controller = managerGo.AddComponent<PlacementController>();
        controller.DigDesignator = dig;

        Assert.IsFalse(controller.IsPlacing);
        Assert.IsTrue(dig.enabled);

        controller.Select(PlaceableType.LairCot);
        Assert.AreEqual(PlaceableType.LairCot, controller.SelectedType);
        Assert.IsTrue(controller.IsPlacing);
        Assert.IsFalse(dig.enabled);

        controller.ClearSelection();
        Assert.IsNull(controller.SelectedType);
        Assert.IsFalse(controller.IsPlacing);
        Assert.IsTrue(dig.enabled);
    }

    [Test]
    public void Select_SameTypeTogglesOff_DifferentTypeSwitches()
    {
        var dig = managerGo.AddComponent<DigDesignator>();
        var controller = managerGo.AddComponent<PlacementController>();
        controller.DigDesignator = dig;

        controller.Select(PlaceableType.MushroomPlot);
        controller.Select(PlaceableType.SpikeTrap);
        Assert.AreEqual(PlaceableType.SpikeTrap, controller.SelectedType, "Selecting another type switches");
        Assert.IsFalse(dig.enabled);

        controller.Select(PlaceableType.SpikeTrap);
        Assert.IsNull(controller.SelectedType, "Re-selecting the same type exits placement mode");
        Assert.IsTrue(dig.enabled);
    }
}
