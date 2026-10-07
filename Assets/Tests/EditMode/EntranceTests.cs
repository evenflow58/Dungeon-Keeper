using System;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

public class EntranceTests
{
    private static readonly Vector2Int Door = new Vector2Int(24, 31);
    private static readonly Vector2Int HeartTile = new Vector2Int(24, 14);
    private const float Dt = 0.02f;

    private GameObject managerGo;
    private DungeonBoard board;
    private BoardRenderer boardRenderer;
    private PlacementManager manager;
    private GameObject heartGo;
    private Heart heart;
    private GameObject heroGo;
    private Hero hero;
    private HeroAI ai;

    [SetUp]
    public void SetUp()
    {
        // Default board: Rock, the 6x6 Floor cavern at (21,13)-(26,18), and the door at (24,31).
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
        if (heroGo != null) Object.DestroyImmediate(heroGo);
        if (heartGo != null) Object.DestroyImmediate(heartGo);
        if (managerGo != null) Object.DestroyImmediate(managerGo);
    }

    /// <summary>A Heart at (24,14) and a hero standing on the door, as the spawner leaves him.</summary>
    private void AddHeartAndHeroAtDoor()
    {
        heartGo = new GameObject("TestHeart");
        heart = heartGo.AddComponent<Heart>();
        heart.Board = board;
        heart.Renderer = boardRenderer;
        heart.Health = heartGo.GetComponent<Health>();
        heart.PlaceOnTile(HeartTile);

        heroGo = new GameObject("TestHero");
        var health = heroGo.AddComponent<Health>();
        health.MaxHealth = 30;
        health.Team = HealthTeam.Hero;
        hero = heroGo.AddComponent<Hero>();
        hero.Board = board;
        hero.Renderer = boardRenderer;
        hero.Health = health;
        ai = heroGo.AddComponent<HeroAI>();
        ai.Hero = hero;
        ai.Heart = heart;
        ai.PlacementManager = manager;
        ai.EntranceTile = board.EntranceTile;
        hero.PlaceOnTile(board.EntranceTile);
    }

    private void DigColumnToDoor()
    {
        for (int y = 19; y < Door.y; y++) board.SetTile(24, y, TileState.Floor); // (24,19)..(24,30)
    }

    private void Step()
    {
        hero.Tick(Dt);
        ai.Tick(Dt);
    }

    private void StepFor(float seconds, Action afterEachStep = null)
    {
        int steps = Mathf.RoundToInt(seconds / Dt);
        for (int i = 0; i < steps; i++)
        {
            Step();
            afterEachStep?.Invoke();
        }
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

    private static int Manhattan(Vector2Int a, Vector2Int b) => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);

    // --- Door identity ---

    [Test]
    public void DefaultBoard_DoorAtTopEdge_CavernUnchanged()
    {
        Assert.AreEqual(Door, board.EntranceTile);
        Assert.AreEqual(TileState.Entrance, board.GetTile(Door.x, Door.y));
        Assert.IsTrue(board.IsEntrance(Door));
        Assert.IsFalse(board.IsEntrance(new Vector2Int(24, 30)));

        int entrances = 0, floor = 0;
        for (int x = 0; x < board.Width; x++)
        {
            for (int y = 0; y < board.Height; y++)
            {
                TileState t = board.GetTile(x, y);
                if (t == TileState.Entrance) entrances++;
                if (t != TileState.Floor) continue;
                floor++;
                Assert.IsTrue(x >= 21 && x <= 26 && y >= 13 && y <= 18, $"Floor outside the cavern at ({x},{y})");
            }
        }
        Assert.AreEqual(1, entrances, "Exactly one door");
        Assert.AreEqual(36, floor, "The 6x6 cavern is unchanged");
    }

    [Test]
    public void EntranceTileSeam_MovesTheDoorOnRegeneration()
    {
        board.EntranceTile = new Vector2Int(0, 16);
        board.InitializeBoard();

        Assert.AreEqual(TileState.Entrance, board.GetTile(0, 16));
        Assert.AreEqual(TileState.Rock, board.GetTile(Door.x, Door.y));
    }

    [Test]
    public void SpawnerEntrance_AgreesWithTheBoardsDoor()
    {
        var go = new GameObject("TestSpawner");
        try
        {
            var spawner = go.AddComponent<HeroSpawner>();
            Assert.AreEqual(board.EntranceTile, spawner.EntranceTile);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void Renderer_BuildsADoorOnTheEntranceTileOnly()
    {
        boardRenderer.RenderFullBoard();

        GameObject door = boardRenderer.GetDoorView(Door.x, Door.y);
        Assert.IsNotNull(door, "A door stands on the entrance tile");
        Assert.IsTrue(door.activeSelf);
        Assert.AreEqual(4, door.transform.childCount, "Two posts, a lintel, a panel");
        Assert.AreEqual(boardRenderer.GetTileCenterWorldPosition(Door.x, Door.y), door.transform.position);
        Assert.IsNull(boardRenderer.GetDoorView(Door.x - 1, Door.y), "No door on the rock beside it");
        Assert.AreEqual(boardRenderer.ShapeFor(TileState.Floor).Scale,
            boardRenderer.GetTileView(Door.x, Door.y).transform.localScale, "The door stands on a floor slab");
    }

    // --- Door rules: everyone but the hero treats it like the board edge ---

    [Test]
    public void Door_NotWalkable_NotDiggable_NotPlaceable()
    {
        DigColumnToDoor(); // even with a tunnel right up to it
        Assert.IsFalse(board.IsWalkable(Door.x, Door.y));
        Assert.IsFalse(board.IsDiggable(Door.x, Door.y));
        Assert.IsFalse(manager.CanPlace(Door, new Vector2Int(24, 16)));
        Assert.IsFalse(manager.TryPlace(PlaceableType.LairCot, Door, new Vector2Int(24, 16), out _));
        Assert.IsTrue(manager.CanPlace(new Vector2Int(24, 30), new Vector2Int(24, 16)), "The tile below it is fine");
    }

    [Test]
    public void Door_CannotBeDesignated()
    {
        int changed = DigDesignator.ApplyRect(board, new RectInt(23, 30, 3, 2), DigDesignator.DesignateMode.Designate);

        Assert.AreEqual(TileState.Entrance, board.GetTile(Door.x, Door.y));
        Assert.AreEqual(5, changed, "Only the 5 Rock tiles around it");
    }

    [Test]
    public void Door_IsNotAShortcut_ForMinionPathing()
    {
        // Floor on both sides of the door: walkers can't route across it.
        board.SetTile(23, 31, TileState.Floor);
        board.SetTile(25, 31, TileState.Floor);
        Assert.AreEqual(0, Pathfinder.FindPath(board, new Vector2Int(23, 31), new Vector2Int(25, 31)).Count);

        // A goblin or the imp heading "to" the door gets no path (not a walkable goal).
        DigColumnToDoor();
        Assert.AreEqual(0, Pathfinder.FindPath(board, new Vector2Int(24, 16), Door).Count);
    }

    [Test]
    public void ImpDiggingUpToTheDoor_StandsBelowIt_NeverOnIt()
    {
        for (int y = 19; y < 30; y++) board.SetTile(24, y, TileState.Floor); // tunnel to (24,29)
        board.SetTile(24, 30, TileState.Designated);                         // last tile, just below the door

        Assert.IsTrue(ImpDigger.TrySelectTarget(board, new Vector2Int(24, 16), out Vector2Int target, out Vector2Int stand));
        Assert.AreEqual(new Vector2Int(24, 30), target);
        Assert.AreEqual(new Vector2Int(24, 29), stand, "Digs from below, not from the door");
    }

    [Test]
    public void GoblinWanderTargets_NeverTheDoor()
    {
        DigColumnToDoor();
        var rng = new System.Random(7);
        for (int i = 0; i < 200; i++)
            Assert.AreNotEqual(Door, GoblinAI.PickWanderTarget(board, new Vector2Int(24, 16), rng));
    }

    // --- The hero and the door ---

    [Test]
    public void SealedIn_HeroWaitsOnTheDoor()
    {
        AddHeartAndHeroAtDoor();
        Vector3 start = heroGo.transform.position;

        Assert.DoesNotThrow(() => StepFor(3f, () =>
        {
            Assert.AreEqual(Door, hero.CurrentTile);
            Assert.IsFalse(hero.IsMoving);
        }));
        Assert.AreEqual(TileState.Entrance, board.GetTile(hero.CurrentTile.x, hero.CurrentTile.y), "Standing on the door");
        Assert.AreEqual(start, heroGo.transform.position);
        Assert.AreEqual(HeroAI.Goal.Delve, ai.CurrentGoal);
        Assert.AreEqual(100, heart.Health.CurrentHealth);
    }

    [Test]
    public void TunnelReachesTheDoor_HeroEntersAndDelves()
    {
        AddHeartAndHeroAtDoor();
        StepFor(1f);
        Assert.AreEqual(Door, hero.CurrentTile, "Still waiting while sealed");

        DigColumnToDoor();
        StepUntil(() => heart.Health.CurrentHealth < 100, 10f, "hero to come down the tunnel and strike");
        Assert.AreNotEqual(Door, hero.CurrentTile, "Left the door");
        Assert.AreEqual(1, Manhattan(hero.CurrentTile, HeartTile), "Beside the Heart");
    }

    [Test]
    public void Fleeing_LeavesThroughTheDoor()
    {
        AddHeartAndHeroAtDoor();
        DigColumnToDoor();
        StepUntil(() => hero.CurrentTile.y <= 26, 3f, "hero to be well into the tunnel");

        hero.Health.TakeDamage(21); // 9/30
        StepUntil(() => ai.HasEscaped, 5f, "hero to flee back to the door");
        Assert.AreEqual(new Vector2Int(24, 30), hero.CurrentTile, "Despawns on the door's threshold");
        Assert.IsFalse(heroGo.activeSelf);
    }

    [Test]
    public void SealedIn_BelowFleeThreshold_LeavesStraightAway()
    {
        AddHeartAndHeroAtDoor();
        hero.Health.TakeDamage(25); // 5/30 while still on the door
        Step();

        Assert.IsTrue(ai.HasEscaped);
        Assert.AreEqual(Door, hero.CurrentTile);
    }
}
