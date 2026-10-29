using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// #165: walls, doors and room detection. Walls: placed like any placeable (an order per tile, per material), imp-built,
/// then on the board: unwalkable for everyone and closed to mana, over conduit and pipe that keep working. Doors: minions
/// path through, heroes never (paths and the route gate); shut they block mana, open (a minion transiting) mana leaks
/// across, then they shut again. Removal is instant and undoes all of it. Rooms: enclosed bare-Floor regions, delimited by
/// walls, doors, rock and designations, typed by built contents with the decided priority; open floor touching the
/// Entrance is no room; the starter cavern is a room at start. The tooltip and the Room view read the same map.
/// </summary>
public class WallsDoorsRoomsTests
{
    // Default board: Rock with the 6x6 Floor cavern at (21,13)-(26,18); the dungeon door at (24,31).
    private static readonly Vector2Int ImpTile = new Vector2Int(24, 16);
    private static readonly Vector2Int HeartTile = new Vector2Int(25, 17);
    private const float Dt = 0.02f;
    private const string Fire = ManaFlow.FireId;

    private GameObject managerGo;
    private GameObject impGo;
    private GameObject heartGo;
    private DungeonBoard board;
    private BoardRenderer boardRenderer;
    private PlacementManager manager;
    private PowerGrid grid;
    private ManaFlow mana;
    private RoomMap rooms;

    [SetUp]
    public void SetUp()
    {
        managerGo = new GameObject("TestDungeonManager");
        board = managerGo.AddComponent<DungeonBoard>();
        board.InitializeBoard();
        boardRenderer = managerGo.AddComponent<BoardRenderer>();
        boardRenderer.Board = board;
        grid = managerGo.AddComponent<PowerGrid>();
        grid.Board = board;
        mana = managerGo.AddComponent<ManaFlow>();
        mana.Board = board;
        mana.ClearPointsOfInterest();
        manager = managerGo.AddComponent<PlacementManager>();
        manager.Board = board;
        manager.Renderer = boardRenderer;
        manager.PowerGrid = grid;
        manager.ManaFlow = mana;
        rooms = managerGo.AddComponent<RoomMap>();
        rooms.Board = board;
        rooms.PlacementManager = manager;
    }

    [TearDown]
    public void TearDown()
    {
        if (impGo != null) Object.DestroyImmediate(impGo);
        if (heartGo != null) Object.DestroyImmediate(heartGo);
        foreach (Hero hero in Object.FindObjectsByType<Hero>(FindObjectsInactive.Include)) Object.DestroyImmediate(hero.gameObject);
        if (managerGo != null) Object.DestroyImmediate(managerGo);
    }

    // ---- helpers ----

    private static Vector2Int T(int x, int y) => new Vector2Int(x, y);

    private Placeable Build(PlaceableType type, Vector2Int tile)
    {
        Assert.IsTrue(manager.TryPlaceBuilt(type, tile, ImpTile, out Placeable p), $"{type} at {tile}");
        return p;
    }

    /// <summary>A wall down the cavern at x = 23 (y 13..18), splitting it in two; doorY (if any) gets a door instead.</summary>
    private void WallColumn(int? doorY = null)
    {
        for (int y = 13; y <= 18; y++)
            Build(y == doorY ? PlaceableType.Door : PlaceableType.Wall, T(23, y));
    }

    private Imp SpawnImp(Vector2Int tile)
    {
        impGo = new GameObject("TestImp");
        Imp imp = impGo.AddComponent<Imp>();
        imp.Board = board;
        imp.Renderer = boardRenderer;
        imp.MoveSpeed = 4f;
        imp.PlaceOnTile(tile);
        return imp;
    }

    private Heart SpawnHeart()
    {
        heartGo = new GameObject("TestHeart");
        Heart heart = heartGo.AddComponent<Heart>();
        heart.Board = board;
        heart.Renderer = boardRenderer;
        heart.Health = heartGo.GetComponent<Health>();
        heart.PlaceOnTile(HeartTile);
        return heart;
    }

    private HeroSpawner AddSpawner(Heart heart)
    {
        HeroSpawner spawner = managerGo.AddComponent<HeroSpawner>();
        spawner.PlacementManager = manager;
        spawner.Board = board;
        spawner.Renderer = boardRenderer;
        spawner.Heart = heart;
        return spawner;
    }

    // The tunnel from the cavern up to the dungeon door (x = 24, y 19..30), and the door dug out.
    private void DigTunnelAndOpenDoor()
    {
        for (int y = 19; y <= 30; y++) board.SetTile(24, y, TileState.Floor);
        board.OpenDoor();
    }

    private int UnitsWhere(System.Func<Vector2Int, bool> where)
    {
        int n = 0;
        foreach (Vector2Int t in mana.OccupiedTiles()) if (where(t)) n += mana.UnitsAt(t);
        return n;
    }

    private void FillLeftWithFire()
    {
        for (int x = 21; x <= 22; x++)
            for (int y = 13; y <= 18; y++)
                for (int i = 0; i < 4; i++) Assert.IsTrue(mana.TryAddUnit(T(x, y), Fire));
    }

    // ---- 1. Wall: placed, priced, imp-built ----

    [Test]
    public void Costs_WallOnePerTile_DoorTwo_InEveryMaterial_RowsGrowBySix()
    {
        foreach (MaterialType material in new[] { MaterialType.Stone, MaterialType.Glimmerstone, MaterialType.Deepstone })
        {
            Assert.IsTrue(manager.TryGetCost(PlaceableType.Wall, material, out MaterialAmount wall), $"Wall in {material}");
            Assert.AreEqual(1, wall.amount);
            Assert.AreEqual(material, wall.type);
            Assert.IsTrue(manager.TryGetCost(PlaceableType.Door, material, out MaterialAmount door), $"Door in {material}");
            Assert.AreEqual(2, door.amount);
        }
        Assert.AreEqual(36, manager.Costs.Count, "30 rows before + 3 per new type");
        Assert.AreEqual(2f, manager.BuildSecondsFor(PlaceableType.Wall));
        Assert.AreEqual(5f, manager.BuildSecondsFor(PlaceableType.Door));
        Assert.IsTrue(BuildPresentation.TryGetCategory(PlaceableType.Wall, out BuildCategory wallCategory));
        Assert.AreEqual(BuildCategory.Living, wallCategory);
        Assert.IsTrue(BuildPresentation.TryGetCategory(PlaceableType.Door, out BuildCategory doorCategory));
        Assert.AreEqual(BuildCategory.Living, doorCategory);
    }

    [Test]
    public void WallLine_DragPlacesOneOrderPerTile_ChargedPerTile_InTheChosenMaterial()
    {
        var stockpile = managerGo.AddComponent<Stockpile>();
        manager.Stockpile = stockpile;
        stockpile.Add(MaterialType.Glimmerstone, 10);

        int placed = manager.ApplyRect(PlaceableType.Wall, MaterialType.Glimmerstone, new RectInt(21, 15, 4, 1), ImpTile);
        Assert.AreEqual(4, placed, "A one-tile-wide drag over (21..24, 15): one order per tile");
        Assert.AreEqual(10 - placed, stockpile.Count(MaterialType.Glimmerstone), "1 per tile");
        foreach (Placeable p in new[] { manager.GetAt(T(21, 15)), manager.GetAt(T(22, 15)) })
        {
            Assert.AreEqual(PlaceableType.Wall, p.Type);
            Assert.AreEqual(MaterialType.Glimmerstone, p.OrderedMaterial);
            Assert.IsFalse(p.IsBuilt, "An order, for the imps to build");
        }
    }

    [Test]
    public void WallSite_IsNothingOnTheBoard_UntilAnImpBuildsIt_InTwoSeconds()
    {
        Imp imp = SpawnImp(ImpTile);
        var digger = impGo.AddComponent<ImpDigger>();
        digger.Board = board;
        digger.Imp = imp;
        digger.PlacementManager = manager;

        Assert.IsTrue(manager.TryPlace(PlaceableType.Wall, T(22, 16), ImpTile, out Placeable wall));
        Assert.AreEqual(TileFeature.None, board.GetFeature(22, 16), "A site doesn't block");
        Assert.IsTrue(board.IsWalkable(22, 16));

        float t = 0f;
        float workStarted = -1f;
        while (!wall.IsBuilt && t < 20f)
        {
            imp.Advance(Dt);
            digger.Tick(Dt);
            if (digger.IsBuilding && workStarted < 0f) workStarted = t;
            t += Dt;
        }
        Assert.IsTrue(wall.IsBuilt, "The imp built it");
        Assert.GreaterOrEqual(t - workStarted, 2f - 2 * Dt, "Two seconds of work");
        Assert.AreEqual(TileFeature.Wall, board.GetFeature(22, 16));
        Assert.IsFalse(board.IsWalkable(22, 16), "Unwalkable once built");
        Assert.IsFalse(board.IsWalkable(22, 16, Mover.Hero));
    }

    // ---- 2. Walls block everyone; 4. doors block heroes only ----

    [Test]
    public void Wall_MinionsAndHeroesPathAroundIt_ASolidWallLeavesNoPath()
    {
        Build(PlaceableType.Wall, T(23, 15));
        List<Vector2Int> minion = Pathfinder.FindPath(board, T(21, 15), T(25, 15));
        List<Vector2Int> hero = Pathfinder.FindPath(board, T(21, 15), T(25, 15), Mover.Hero);
        Assert.Greater(minion.Count, 0);
        Assert.Greater(hero.Count, 0);
        CollectionAssert.DoesNotContain(minion, T(23, 15), "Around, not through");
        CollectionAssert.DoesNotContain(hero, T(23, 15));
        Assert.AreEqual(7, minion.Count, "One step around: 5 straight + 2");

        for (int y = 13; y <= 18; y++) if (y != 15) Build(PlaceableType.Wall, T(23, y));
        Assert.AreEqual(0, Pathfinder.FindPath(board, T(21, 15), T(25, 15)).Count, "Sealed for minions");
        Assert.AreEqual(0, Pathfinder.FindPath(board, T(21, 15), T(25, 15), Mover.Hero).Count, "...and heroes");
        Assert.AreEqual(0, Pathfinder.FindPathToNeighbor(board, T(21, 15), T(25, 15)).Count);
    }

    [Test]
    public void Door_MinionsPathThroughIt_HeroesDoNot()
    {
        WallColumn(doorY: 15);
        List<Vector2Int> minion = Pathfinder.FindPath(board, T(21, 15), T(25, 15));
        Assert.AreEqual(5, minion.Count, "Straight through the door");
        CollectionAssert.Contains(minion, T(23, 15));
        Assert.IsTrue(board.IsWalkable(23, 15), "Passable for a minion");
        Assert.IsFalse(board.IsWalkable(23, 15, Mover.Hero), "Never for a hero");
        Assert.AreEqual(0, Pathfinder.FindPath(board, T(21, 15), T(25, 15), Mover.Hero).Count, "No hero path");
        Assert.AreEqual(0, Pathfinder.FindPathToNeighbor(board, T(21, 15), T(25, 15), Mover.Hero).Count);
    }

    [Test]
    public void Imp_WalksThroughTheDoor_TileByTile()
    {
        WallColumn(doorY: 15);
        Imp imp = SpawnImp(T(21, 15));
        Assert.IsTrue(imp.SetDestination(T(25, 15)));
        var visited = new List<Vector2Int>();
        for (int i = 0; i < 200 && (imp.IsMoving || visited.Count == 0); i++)
        {
            imp.Advance(Dt);
            if (visited.Count == 0 || visited[visited.Count - 1] != imp.CurrentTile) visited.Add(imp.CurrentTile);
        }
        Assert.AreEqual(T(25, 15), imp.CurrentTile);
        CollectionAssert.Contains(visited, T(23, 15), "Through the doorway");
    }

    [Test]
    public void RouteGate_WalledOrDooredTunnel_NoRoute_NoHero_RemovalReopensIt()
    {
        DigTunnelAndOpenDoor();
        HeroSpawner spawner = AddSpawner(SpawnHeart());
        Assert.IsTrue(spawner.RouteExists(), "Open tunnel: a route");

        Build(PlaceableType.Wall, T(24, 22));
        Assert.IsFalse(spawner.RouteExists(), "Walled off");
        Assert.AreEqual("Next hero: needs a route", TopBarAfter(spawner, 300f));
        Assert.AreEqual(0, spawner.HeroesSpawned, "No hero into a sealed dungeon");

        Assert.IsTrue(manager.TryRemovePartition(T(24, 22)));
        Build(PlaceableType.Door, T(24, 22));
        Assert.IsFalse(spawner.RouteExists(), "A door bars heroes just the same");
        spawner.Tick(Dt);
        Assert.AreEqual(0, spawner.HeroesSpawned);
        Assert.IsTrue(Pathfinder.FindPath(board, ImpTile, T(24, 25)).Count > 0, "...while minions still walk the tunnel");

        Assert.IsTrue(manager.TryRemovePartition(T(24, 22)));
        Assert.IsTrue(spawner.RouteExists(), "Removed: the route is back at once");
        spawner.Tick(Dt);
        Assert.AreEqual(1, spawner.HeroesSpawned, "He comes in on the next poll");
    }

    [Test]
    public void RouteGate_ALaterHero_DooredOut_WaitsAndTheTopBarSaysNeedsARoute()
    {
        DigTunnelAndOpenDoor();
        HeroSpawner spawner = AddSpawner(SpawnHeart());
        spawner.Tick(300f);
        Assert.AreEqual(1, spawner.HeroesSpawned);
        Hero first = spawner.ActiveHero;
        first.Health.TakeDamage(10000);
        first.Tick(Dt);
        spawner.Tick(0f);
        Assert.IsTrue(spawner.NextHeroScheduled);
        Assert.AreEqual("Next hero: 2:00", TopBar.NextHeroReadout(spawner, null), "Counting down while the way is open");

        Build(PlaceableType.Door, T(24, 22));
        spawner.Tick(spawner.NextHeroDelaySeconds + 1f);
        Assert.AreEqual(1, spawner.HeroesSpawned, "Time's up, but no route: he waits");
        Assert.AreEqual("Next hero: needs a route", TopBar.NextHeroReadout(spawner, null));

        Assert.IsTrue(manager.TryRemovePartition(T(24, 22)));
        spawner.Tick(Dt);
        Assert.AreEqual(2, spawner.HeroesSpawned, "Door gone: he comes in");
    }

    private static string TopBarAfter(HeroSpawner spawner, float seconds)
    {
        spawner.Tick(seconds);
        return TopBar.NextHeroReadout(spawner, null);
    }

    [Test]
    public void Hero_InTheDungeon_PathsAroundAWall_AndNeverThroughADoor()
    {
        Build(PlaceableType.Wall, T(23, 15));
        Build(PlaceableType.Door, T(23, 16));
        var heroGo = new GameObject("TestHero");
        Hero hero = heroGo.AddComponent<Hero>();
        hero.Board = board;
        hero.Renderer = boardRenderer;
        hero.PlaceOnTile(T(21, 15));
        Assert.IsTrue(hero.SetDestination(T(25, 15)));
        var visited = new HashSet<Vector2Int>();
        for (int i = 0; i < 400 && hero.IsMoving; i++)
        {
            hero.Tick(Dt);
            visited.Add(hero.CurrentTile);
        }
        Assert.AreEqual(T(25, 15), hero.CurrentTile);
        Assert.IsFalse(visited.Contains(T(23, 15)), "Not through the wall");
        Assert.IsFalse(visited.Contains(T(23, 16)), "Not through the door");
        Assert.IsFalse(hero.SetDestination(T(23, 16)), "A door is never a hero's destination");
    }

    // ---- 3. Mana: walls closed, doors closed while shut, open while open ----

    [Test]
    public void Wall_BlocksMana_NothingEverCrosses()
    {
        WallColumn();
        FillLeftWithFire();
        int total = mana.UnitCount;
        for (int i = 0; i < 300; i++) mana.FlowStep();

        Assert.AreEqual(0, UnitsWhere(t => t.x >= 23), "Nothing on or past the wall");
        Assert.AreEqual(total, mana.UnitCount, "Conserved");
        Assert.IsFalse(mana.IsOpen(T(23, 15)), "A wall tile is closed");
        Assert.IsFalse(mana.CanAccept(T(23, 15), Fire));
    }

    [Test]
    public void ShutDoor_BlocksMana_OpenDoor_Leaks_ThenStopsWhenShut()
    {
        WallColumn(doorY: 15);
        FillLeftWithFire();
        int total = mana.UnitCount;
        for (int i = 0; i < 200; i++) mana.FlowStep();
        Assert.AreEqual(0, UnitsWhere(t => t.x >= 23), "Shut: a wall");
        Assert.IsFalse(mana.IsOpen(T(23, 15)));

        board.SetInnerDoorOpen(23, 15, true); // A minion is passing: the seam
        Assert.IsTrue(mana.IsOpen(T(23, 15)), "Open: the normal rules apply across it");
        int ticks = 0;
        while (UnitsWhere(t => t.x >= 24) == 0 && ticks < 50)
        {
            mana.FlowStep();
            ticks++;
        }
        Assert.Greater(UnitsWhere(t => t.x >= 24), 0, "Mana sneaked through");

        board.SetInnerDoorOpen(23, 15, false);
        for (int i = 0; i < 50; i++) mana.FlowStep(); // What sat in the doorway drains out, either way
        Assert.AreEqual(0, mana.UnitsAt(T(23, 15)), "The doorway empties");
        int leaked = UnitsWhere(t => t.x >= 24);
        for (int i = 0; i < 300; i++) mana.FlowStep();
        Assert.AreEqual(leaked, UnitsWhere(t => t.x >= 24), "Shut again: no more crosses");
        Assert.AreEqual(total, mana.UnitCount, "Conserved");
    }

    [Test]
    public void Door_OpensWhileAMinionTransits_LingersThenShuts_ARealImpOpensIt()
    {
        WallColumn(doorY: 15);
        Partition door = manager.GetAt(T(23, 15)).GetComponent<Partition>();
        bool inDoorway = false;
        door.OccupiedProbe = t => inDoorway && t == T(23, 15);
        door.LingerSeconds = 0.5f;

        door.Tick(Dt);
        Assert.IsFalse(door.IsOpen, "Nobody there: shut");
        inDoorway = true;
        door.Tick(Dt);
        Assert.IsTrue(door.IsOpen, "A minion in the doorway: open");
        Assert.IsTrue(board.IsInnerDoorOpen(23, 15));
        inDoorway = false;
        door.Tick(0.3f);
        Assert.IsTrue(door.IsOpen, "Lingers after the last leaves");
        door.Tick(0.3f);
        Assert.IsFalse(door.IsOpen, "...then shuts");

        // The live probe: an imp walking through the doorway opens it.
        door.OccupiedProbe = Partition.MinionInDoorway;
        Imp imp = SpawnImp(T(21, 15));
        Assert.IsTrue(imp.SetDestination(T(25, 15)));
        bool sawOpen = false;
        for (int i = 0; i < 200 && imp.IsMoving; i++)
        {
            imp.Advance(Dt);
            door.Tick(Dt);
            sawOpen |= door.IsOpen;
        }
        Assert.IsTrue(sawOpen, "The imp's transit opened it");
        for (int i = 0; i < 60; i++) door.Tick(Dt);
        Assert.IsFalse(door.IsOpen, "Shut again once it's through");
    }

    [Test]
    public void Door_HeroNeverOpensIt()
    {
        WallColumn(doorY: 15);
        Partition door = manager.GetAt(T(23, 15)).GetComponent<Partition>();
        door.OccupiedProbe = Partition.MinionInDoorway;
        var heroGo = new GameObject("TestHero");
        Hero hero = heroGo.AddComponent<Hero>();
        hero.Board = board;
        hero.Renderer = boardRenderer;
        hero.PlaceOnTile(T(22, 15)); // Right against it
        for (int i = 0; i < 20; i++) door.Tick(Dt);
        Assert.IsFalse(door.IsOpen, "Only minions open doors");
    }

    // ---- conduit and pipe under walls and doors ----

    [Test]
    public void WallAndDoor_OverConduitAndPipe_TheGridAndNetworkStayWhole()
    {
        for (int x = 21; x <= 25; x++)
        {
            Assert.AreEqual(1, grid.ApplyRect(null, new RectInt(x, 15, 1, 1), PowerGrid.ConduitMode.Lay));
            Assert.IsTrue(grid.CompleteConduit(T(x, 15)));
            Assert.AreEqual(1, mana.ApplyPipeRect(null, new RectInt(x, 15, 1, 1), ManaFlow.PipeMode.Lay));
            Assert.IsTrue(mana.CompletePipe(T(x, 15)));
        }
        int gridBefore = grid.GridAt(T(21, 15));
        Assert.AreEqual(gridBefore, grid.GridAt(T(25, 15)));

        Build(PlaceableType.Wall, T(23, 15)); // Layers never affect placement validity
        Build(PlaceableType.Door, T(24, 15));
        Assert.AreEqual(grid.GridAt(T(21, 15)), grid.GridAt(T(25, 15)), "One grid still, through the wall and the door");
        Assert.IsTrue(grid.IsBuilt(T(23, 15)));
        Assert.AreEqual(mana.NetworkAt(T(21, 15)), mana.NetworkAt(T(25, 15)), "One pipe network still");
        Assert.IsTrue(mana.IsPipeBuilt(T(24, 15)));
    }

    // ---- 8. removal ----

    [Test]
    public void Removal_IsInstant_AnyState_NoRefund_AndRestoresPathingManaAndRooms()
    {
        var stockpile = managerGo.AddComponent<Stockpile>();
        manager.Stockpile = stockpile;
        stockpile.Add(MaterialType.Stone, 20);
        WallColumn();
        int afterWalls = stockpile.Count(MaterialType.Stone);
        Assert.AreEqual(2, rooms.Rooms.Count, "Split in two");
        Assert.AreEqual(0, Pathfinder.FindPath(board, T(21, 15), T(25, 15)).Count);

        Assert.IsTrue(manager.TryRemovePartition(T(23, 15)));
        Assert.AreEqual(afterWalls, stockpile.Count(MaterialType.Stone), "No refund");
        Assert.IsNull(manager.GetAt(T(23, 15)));
        Assert.AreEqual(TileFeature.None, board.GetFeature(23, 15));
        Assert.Greater(Pathfinder.FindPath(board, T(21, 15), T(25, 15)).Count, 0, "Pathing restored");
        Assert.IsTrue(mana.IsOpen(T(23, 15)), "Mana openness restored");
        Assert.AreEqual(1, rooms.Rooms.Count, "The rooms merge at once");

        Assert.IsTrue(manager.TryPlace(PlaceableType.Wall, T(22, 13), ImpTile, out Placeable order));
        Assert.IsTrue(manager.TryRemovePartition(T(22, 13)), "An order or site comes out too");
        Assert.IsFalse(manager.TryRemovePartition(T(22, 13)), "Nothing left there");
        Placeable cot = Build(PlaceableType.LairCot, T(21, 13));
        Assert.IsFalse(manager.TryRemovePartition(T(21, 13)), "Other placeables can't be removed");
        Assert.AreSame(cot, manager.GetAt(T(21, 13)));
    }

    [Test]
    public void RemovalDrag_WithAWallArmed_RemovesTheRectsWallsAndDoors()
    {
        WallColumn(doorY: 15);
        var controllerGo = new GameObject("TestController");
        try
        {
            var controller = controllerGo.AddComponent<PlacementController>();
            controller.Board = board;
            controller.PlacementManager = manager;
            Assert.AreEqual(0, controller.RemovePartitions(new RectInt(23, 13, 1, 6)), "Nothing armed: no removal");
            controller.Select(PlaceableType.Wall);
            Assert.AreEqual(6, controller.RemovePartitions(new RectInt(23, 13, 1, 6)), "Five walls and the door");
            for (int y = 13; y <= 18; y++) Assert.AreEqual(TileFeature.None, board.GetFeature(23, y));
        }
        finally { Object.DestroyImmediate(controllerGo); }
    }

    // ---- 6. room detection and typing ----

    [Test]
    public void StarterCavern_IsOneRoom_AtStart()
    {
        Assert.AreEqual(1, rooms.Rooms.Count);
        Room cavern = rooms.Rooms[0];
        Assert.AreEqual(36, cavern.Tiles.Count);
        Assert.AreEqual(RoomType.Room, cavern.Type);
        Assert.AreSame(cavern, rooms.RoomAt(ImpTile));
        Assert.IsNull(rooms.RoomAt(T(20, 15)), "Rock is in no room");
    }

    [Test]
    public void UnenclosedFloor_TouchingTheEntrance_IsNoRoom_ADoorAcrossTheTunnelEnclosesIt()
    {
        DigTunnelAndOpenDoor();
        Assert.AreEqual(0, rooms.Rooms.Count, "Open to the outside: no room of any type");
        Assert.IsNull(rooms.RoomAt(ImpTile));
        Assert.IsNull(rooms.RoomAt(board.EntranceTile), "The Entrance is in no room");

        Build(PlaceableType.Door, T(24, 25));
        Assert.AreEqual(1, rooms.Rooms.Count, "The cavern and the inner tunnel are enclosed; the outer stretch is open");
        Assert.IsNotNull(rooms.RoomAt(ImpTile));
        Assert.IsNull(rooms.RoomAt(T(24, 28)), "Beyond the door, still open to the outside");
        Assert.IsNull(rooms.RoomAt(T(24, 25)), "A door tile belongs to no room");
    }

    [Test]
    public void DoorJoinedSpaces_AreTwoRooms_AndWallAndDoorTilesBelongToNone()
    {
        WallColumn(doorY: 15);
        Assert.AreEqual(2, rooms.Rooms.Count);
        Room left = rooms.RoomAt(T(21, 15));
        Room right = rooms.RoomAt(T(25, 15));
        Assert.IsNotNull(left);
        Assert.IsNotNull(right);
        Assert.AreNotSame(left, right);
        Assert.AreEqual(12, left.Tiles.Count);
        Assert.AreEqual(18, right.Tiles.Count);
        Assert.IsNull(rooms.RoomAt(T(23, 15)), "The door");
        Assert.IsNull(rooms.RoomAt(T(23, 14)), "A wall");
    }

    [Test]
    public void DesignatedBoundary_Encloses_ADugGapMergesRooms_ABuiltWallSplitsOne()
    {
        // A side chamber at (18..19, 15) reached through (20, 15): rock until dug.
        board.SetTile(18, 15, TileState.Floor);
        board.SetTile(19, 15, TileState.Floor);
        board.SetTile(20, 15, TileState.Designated);
        Assert.AreEqual(2, rooms.Rooms.Count, "A designation still encloses: the chamber and the cavern");
        Assert.AreNotSame(rooms.RoomAt(T(19, 15)), rooms.RoomAt(T(21, 15)));

        board.SetTile(20, 15, TileState.Floor); // Dug: the gap opens
        Assert.AreEqual(1, rooms.Rooms.Count, "One room now");
        Assert.AreSame(rooms.RoomAt(T(19, 15)), rooms.RoomAt(T(25, 15)));

        Build(PlaceableType.Wall, T(20, 15)); // Walled again
        Assert.AreEqual(2, rooms.Rooms.Count, "A built wall splits it");
        Assert.IsNull(rooms.RoomAt(T(20, 15)));
    }

    [Test]
    public void AWallSiteDoesntSplit_UntilItsBuilt()
    {
        for (int y = 13; y <= 18; y++) Assert.IsTrue(manager.TryPlace(PlaceableType.Wall, T(23, y), ImpTile, out _));
        Assert.AreEqual(1, rooms.Rooms.Count, "Sites aren't walls yet");
        foreach (Placeable p in new List<Placeable> { manager.GetAt(T(23, 13)), manager.GetAt(T(23, 14)), manager.GetAt(T(23, 15)),
                     manager.GetAt(T(23, 16)), manager.GetAt(T(23, 17)), manager.GetAt(T(23, 18)) })
            p.CompleteConstruction();
        Assert.AreEqual(2, rooms.Rooms.Count, "Built: split");
    }

    [Test]
    public void Typing_ByBuiltContents_WithTheDecidedPriority()
    {
        WallColumn(doorY: 15);
        Assert.AreEqual(RoomType.Room, rooms.RoomAt(T(21, 15)).Type, "Empty: a generic room");

        Assert.IsTrue(manager.TryPlace(PlaceableType.LairCot, T(21, 13), ImpTile, out Placeable cotSite));
        Assert.AreEqual(RoomType.Room, rooms.RoomAt(T(21, 15)).Type, "A cot site isn't furniture yet");
        cotSite.CompleteConstruction();
        Assert.AreEqual(RoomType.Bedroom, rooms.RoomAt(T(21, 15)).Type, "A built cot: a bedroom");

        Build(PlaceableType.MushroomPlot, T(25, 13));
        Assert.AreEqual(RoomType.Farm, rooms.RoomAt(T(25, 15)).Type, "Plots: a farm");
        Build(PlaceableType.MushroomPlot, T(22, 13));
        Assert.AreEqual(RoomType.Bedroom, rooms.RoomAt(T(21, 15)).Type, "Cot and plot: Bedroom outranks Farm");

        Build(PlaceableType.SpikeTrap, T(26, 18));
        Assert.AreEqual(RoomType.Farm, rooms.RoomAt(T(25, 15)).Type, "Untyped contents don't change it");
        Assert.AreEqual("Farm", rooms.RoomAt(T(25, 15)).DisplayName);
    }

    [Test]
    public void TypingRules_PriorityOrderIsDecided_AndAnticipatesTheLaterFurniture()
    {
        CollectionAssert.AreEqual(
            new[] { RoomType.Infirmary, RoomType.DiningHall, RoomType.Bedroom, RoomType.CommonRoom, RoomType.Farm },
            System.Array.ConvertAll(RoomTyping.Priority, r => r.type));
        Assert.AreEqual(RoomType.Bedroom, RoomTyping.TypeOf(new HashSet<PlaceableType> { PlaceableType.MushroomPlot, PlaceableType.LairCot }));
        Assert.AreEqual(RoomType.Farm, RoomTyping.TypeOf(new HashSet<PlaceableType> { PlaceableType.MushroomPlot, PlaceableType.Warren }));
        Assert.AreEqual(RoomType.Room, RoomTyping.TypeOf(new HashSet<PlaceableType> { PlaceableType.Warren }));
        Assert.AreEqual(RoomType.Room, RoomTyping.TypeOf(new HashSet<PlaceableType>()));
        Assert.AreEqual("Dining hall", RoomTyping.DisplayName(RoomType.DiningHall));
        Assert.AreEqual("Common room", RoomTyping.DisplayName(RoomType.CommonRoom));
    }

    [Test]
    public void OneTileCloset_IsARoom()
    {
        board.SetTile(10, 10, TileState.Floor);
        Room closet = rooms.RoomAt(T(10, 10));
        Assert.IsNotNull(closet);
        Assert.AreEqual(1, closet.Tiles.Count);
    }

    // ---- 7. presentation: tooltip lines and the Room view ----

    [Test]
    public void Tooltip_NamesTheRoomType_AndTheWallOrDoor()
    {
        WallColumn(doorY: 15);
        Build(PlaceableType.LairCot, T(21, 13));
        Assert.AreEqual("Bedroom", TileHover.RoomLine(rooms, T(21, 15)));
        Assert.AreEqual("Room", TileHover.RoomLine(rooms, T(25, 15)));
        Assert.IsNull(TileHover.RoomLine(rooms, T(23, 14)), "A wall is in no room");
        Assert.AreEqual("Wall", TileHover.PartitionLine(board, T(23, 14)));
        Assert.AreEqual("Door", TileHover.PartitionLine(board, T(23, 15)));
        board.SetInnerDoorOpen(23, 15, true);
        Assert.AreEqual("Door (open)", TileHover.PartitionLine(board, T(23, 15)));
        Assert.IsNull(TileHover.PartitionLine(board, T(21, 15)));

        string label = TileHover.TooltipLabel(null, TileIdentity.Of(board, null, T(21, 15)), T(21, 15), null,
            TileHover.RoomLine(rooms, T(21, 15)), TileHover.PartitionLine(board, T(21, 15)));
        Assert.AreEqual("Floor (21, 15)\nBedroom", label);
    }

    [Test]
    public void RoomView_TintsEachRoomByType_DimsEverythingElse_NormalUntouched()
    {
        boardRenderer.Rooms = rooms;
        WallColumn(doorY: 15);
        Build(PlaceableType.LairCot, T(21, 13));
        Color normalFloor = boardRenderer.DisplayColorAt(21, 15);
        Color normalRock = boardRenderer.DisplayColorAt(10, 10);

        boardRenderer.SetViewMode(ViewMode.Room);
        Assert.AreEqual(ViewMode.Room, boardRenderer.Mode);
        Color bedroom = boardRenderer.DisplayColorAt(21, 15);
        Color generic = boardRenderer.DisplayColorAt(25, 15);
        Assert.AreNotEqual(normalFloor, bedroom, "A room tile takes its type's tint");
        Assert.AreNotEqual(bedroom, generic, "Different types, different tints");
        Assert.AreEqual(PowerOverlay.Dim(normalRock, boardRenderer.OverlayDimFactor), boardRenderer.DisplayColorAt(10, 10), "Rock dims");
        Assert.AreEqual(PowerOverlay.Dim(normalFloor, boardRenderer.OverlayDimFactor), boardRenderer.DisplayColorAt(23, 15),
            "The door's floor is in no room: dimmed");

        boardRenderer.SetViewMode(ViewMode.Normal);
        Assert.AreEqual(normalFloor, boardRenderer.DisplayColorAt(21, 15), "Normal restored exactly");
        Assert.IsFalse(PowerOverlay.SeesThrough(ViewMode.Room, false, false), "Furniture stays solid in the Room view");
    }

    [Test]
    public void RoomViewKey_IsR_AndRebindable()
    {
        Assert.AreEqual("ToggleRoomView", InputConfig.ToggleRoomView);
        using (InputConfig.Actions a = InputConfig.CreateActions())
        {
            Assert.AreSame(a.Map[InputConfig.ToggleRoomView], a.ToggleRoomView);
            CollectionAssert.AreEqual(new[] { "<Keyboard>/r" }, InputConfig.EffectivePaths(a.ToggleRoomView));
        }
    }

    // ---- nothing changes without walls or doors ----

    [Test]
    public void NoPartitions_TodaysRules_Exactly()
    {
        for (int x = 21; x <= 26; x++)
            for (int y = 13; y <= 18; y++)
            {
                Assert.AreEqual(board.GetTile(x, y) == TileState.Floor, board.IsWalkable(x, y, Mover.Hero), "Heroes walk Floor");
                Assert.AreEqual(board.IsWalkable(x, y), board.IsWalkable(x, y, Mover.Hero), "Minions and heroes agree");
                Assert.IsFalse(board.BlocksMana(x, y));
            }
        Assert.AreEqual(Pathfinder.FindPath(board, T(21, 13), T(26, 18)), Pathfinder.FindPath(board, T(21, 13), T(26, 18), Mover.Hero));
    }
}
