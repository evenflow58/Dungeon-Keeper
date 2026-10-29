using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// #163: the undug door (pre-explored, dug through the normal flow), the hero clock gated on it, and goblin recruitment by
/// imp expedition: the order (path required, one at a time), the walk out, the trip (no jobs, no fog, game time), the
/// return (with a goblin or empty-handed, via the roll seam), the recruit (unleashed, named, placed, assignable), the
/// return toast, the imp panel, and the #161 naming interaction.
/// </summary>
public class ImpExpeditionTests
{
    private static readonly Vector2Int Door = new Vector2Int(24, 31);
    private static readonly Vector2Int Threshold = new Vector2Int(24, 30); // The Floor tile just inside the door
    private static readonly Vector2Int HeartTile = new Vector2Int(24, 14);
    private static readonly Vector2Int CavernCenter = new Vector2Int(24, 16);
    private const float Dt = 0.02f;
    private const float Frozen = float.PositiveInfinity;

    private GameObject managerGo;
    private DungeonBoard board;
    private BoardRenderer boardRenderer;
    private PlacementManager manager;
    private Stockpile stockpile;
    private FogOfWar fog;
    private GameObject heartGo;
    private Heart heart;
    private Imp starter;
    private ImpDigger starterDigger;
    private GameObject expeditionGo;
    private ImpExpedition expedition;
    private readonly List<GameObject> spawned = new List<GameObject>();
    private readonly List<Goblin> goblins = new List<Goblin>();

    [SetUp]
    public void SetUp()
    {
        // The game's default board: 48x32 Rock, the 6x6 Floor cavern at (21,13)-(26,18), the door (24,31) undug.
        managerGo = new GameObject("TestDungeonManager");
        board = managerGo.AddComponent<DungeonBoard>();
        board.InitializeBoard();
        boardRenderer = managerGo.AddComponent<BoardRenderer>();
        boardRenderer.Board = board;
        manager = managerGo.AddComponent<PlacementManager>();
        manager.Board = board;
        manager.Renderer = boardRenderer;
        stockpile = managerGo.AddComponent<Stockpile>();
        fog = managerGo.AddComponent<FogOfWar>();
        fog.Board = board;

        heartGo = new GameObject("TestHeart");
        heart = heartGo.AddComponent<Heart>();
        heart.Board = board;
        heart.Renderer = boardRenderer;
        heart.Health = heartGo.GetComponent<Health>();
        heart.PlaceOnTile(HeartTile);

        starter = AddImp("Imp", CavernCenter);
        starterDigger = starter.GetComponent<ImpDigger>();
        fog.Imp = starter;

        // The three starting goblins, along the cavern's south wall.
        AddGoblin("Goblin1", new Vector2Int(21, 13));
        AddGoblin("Goblin2", new Vector2Int(22, 13));
        AddGoblin("Goblin3", new Vector2Int(23, 13));

        expeditionGo = new GameObject("TestExpedition");
        expedition = expeditionGo.AddComponent<ImpExpedition>();
        expedition.Board = board;
        expedition.Renderer = boardRenderer;
        expedition.PlacementManager = manager;
        expedition.Heart = heart;
        expedition.FogOfWar = fog;
        expedition.TemplateGoblin = goblins[0];
    }

    [TearDown]
    public void TearDown()
    {
        foreach (Imp imp in Object.FindObjectsByType<Imp>(FindObjectsInactive.Include)) Object.DestroyImmediate(imp.gameObject);
        foreach (Goblin g in Object.FindObjectsByType<Goblin>(FindObjectsInactive.Include)) Object.DestroyImmediate(g.gameObject);
        foreach (Hero h in Object.FindObjectsByType<Hero>(FindObjectsInactive.Include)) Object.DestroyImmediate(h.gameObject);
        foreach (Canvas c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include)) Object.DestroyImmediate(c.gameObject);
        foreach (GameObject go in spawned) if (go != null) Object.DestroyImmediate(go);
        spawned.Clear();
        goblins.Clear();
        if (expeditionGo != null) Object.DestroyImmediate(expeditionGo);
        if (heartGo != null) Object.DestroyImmediate(heartGo);
        if (managerGo != null) Object.DestroyImmediate(managerGo);
    }

    // ---- staging ----

    private Imp AddImp(string name, Vector2Int tile)
    {
        var go = new GameObject(name);
        spawned.Add(go);
        Imp imp = go.AddComponent<Imp>();
        imp.Board = board;
        imp.Renderer = boardRenderer;
        imp.PlaceOnTile(tile);
        ImpDigger digger = go.AddComponent<ImpDigger>();
        digger.Board = board;
        digger.Imp = imp;
        digger.PlacementManager = manager;
        digger.Stockpile = stockpile;
        return imp;
    }

    private Goblin AddGoblin(string name, Vector2Int tile)
    {
        var go = new GameObject(name);
        spawned.Add(go);
        Health health = go.AddComponent<Health>();
        health.MaxHealth = 20;
        health.Team = HealthTeam.Monster;
        Goblin goblin = go.AddComponent<Goblin>();
        goblin.Board = board;
        goblin.Renderer = boardRenderer;
        goblin.Health = health;
        goblin.LogStats = false;
        goblin.HungerSecondsToEmpty = Frozen;
        goblin.EnergySecondsToEmpty = Frozen;
        goblin.StarvationSecondsToDie = Frozen;
        goblin.PlaceOnTile(tile);
        GoblinAI ai = go.AddComponent<GoblinAI>();
        ai.Goblin = goblin;
        ai.PlacementManager = manager;
        goblins.Add(goblin);
        return goblin;
    }

    /// <summary>A straight tunnel from the cavern's north edge up to the tile below the door: (24,19)..(24,30).</summary>
    private void DigTunnel()
    {
        for (int y = 19; y <= Threshold.y; y++) board.SetTile(24, y, TileState.Floor);
    }

    private void OpenDoorWithTunnel()
    {
        DigTunnel();
        board.SetTile(Door.x, Door.y, board.DugStateOf(Door)); // What the dig converts the door to
    }

    /// <summary>One frame: every active imp moves and works, the fog follows the starter, then the expedition ticks.</summary>
    private void Step(float dt = Dt)
    {
        foreach (Imp imp in Object.FindObjectsByType<Imp>())
        {
            imp.Advance(dt);
            imp.GetComponent<ImpDigger>()?.Tick(dt);
        }
        fog.RevealFromImp();
        expedition.Tick(dt);
    }

    private float StepUntil(System.Func<bool> condition, float maxSeconds, string because, float dt = Dt)
    {
        float elapsed = 0f;
        while (!condition())
        {
            Assert.Less(elapsed, maxSeconds, "Timed out waiting: " + because);
            Step(dt);
            elapsed += dt;
        }
        return elapsed;
    }

    /// <summary>Sends the starter out over the open door and steps until it has departed.</summary>
    private void SendStarterAndDepart()
    {
        OpenDoorWithTunnel();
        Assert.IsTrue(expedition.Send(starter), "The order is available");
        StepUntil(() => expedition.Phase == ImpExpedition.ExpeditionPhase.Away, 10f, "the imp to reach the door");
    }

    private void FinishExpedition() =>
        StepUntil(() => !expedition.IsActive, expedition.ExpeditionSeconds + 5f, "the imp to come back", 0.5f);

    private HeroSpawner AddSpawner()
    {
        var go = new GameObject("TestSpawner");
        spawned.Add(go);
        var spawner = go.AddComponent<HeroSpawner>();
        spawner.Board = board;
        spawner.Renderer = boardRenderer;
        spawner.PlacementManager = manager;
        spawner.Heart = heart;
        return spawner;
    }

    // ---- 1. The door starts undug ----

    [Test]
    public void TheDoor_StartsUndugRock_PreExplored_AndDesignatable()
    {
        Assert.AreEqual(TileState.Rock, board.GetTile(Door.x, Door.y), "Undug");
        Assert.IsFalse(board.IsDoorOpen);
        Assert.IsTrue(board.DoorStartsUndug, "Serialized default");
        Assert.IsTrue(board.IsDiggable(Door.x, Door.y), "Rock like any other");
        Assert.IsTrue(fog.IsExplored(Door), "Pre-explored at game start");
        Assert.IsFalse(fog.IsExplored(Threshold), "Only the door's own tile");
        Assert.AreEqual(1, fog.ExploredCount);

        int changed = DigDesignator.ApplyRect(board, fog, new RectInt(23, 30, 3, 2), DigDesignator.DesignateMode.Designate);
        Assert.AreEqual(1, changed, "Only the door: the rock around it is still fogged");
        Assert.AreEqual(TileState.Designated, board.GetTile(Door.x, Door.y));
    }

    [Test]
    public void ImpDigsTheDoorOut_ThroughTheNormalDigFlow_ItBecomesTheEntrance()
    {
        DigTunnel();
        DigDesignator.ApplyRect(board, fog, new RectInt(Door.x, Door.y, 1, 1), DigDesignator.DesignateMode.Designate);

        StepUntil(() => board.IsDoorOpen, 20f, "the imp to dig the door out");

        Assert.AreEqual(TileState.Entrance, board.GetTile(Door.x, Door.y));
        Assert.IsFalse(board.IsWalkable(Door.x, Door.y), "The entrance keeps its rules: not walkable");
        Assert.IsFalse(board.IsDiggable(Door.x, Door.y), "...nor diggable");
        Assert.AreEqual(Threshold, starter.CurrentTile, "Dug from just inside");
        Assert.AreEqual(TileState.Floor, board.DugStateOf(new Vector2Int(24, 20)), "Every other tile digs to Floor");
    }

    // ---- 2. The hero clock ----

    [Test]
    public void DoorUndug_NoHeroEver_AcrossALongRun_EvenWithEveryTriggerMetAndARouteToTheDoor()
    {
        DigTunnel(); // A tunnel right up to the door
        HeroSpawner spawner = AddSpawner();
        spawner.RequiredCots = 0;  // The buildings trigger is met from the start…
        spawner.RequiredPlots = 0;
        Assert.IsTrue(spawner.RouteExists(), "…and a hero could walk in from the door tile: only the gate stops him");

        for (int i = 0; i < 7200; i++) spawner.Tick(1f); // Two hours

        Assert.AreEqual(0, spawner.HeroesSpawned);
        Assert.IsFalse(spawner.ClockStarted);
        Assert.AreEqual(0f, spawner.ElapsedSeconds, "The clock never ran");
        Assert.AreEqual(TopBar.DoorSealedText, TopBar.NextHeroReadout(spawner, null));
    }

    [Test]
    public void DiggingTheDoor_StartsTheClock_FirstHeroOnSchedule_LaterWavesUnchanged()
    {
        DigTunnel();
        HeroSpawner spawner = AddSpawner();
        spawner.RequiredCots = 99; // Time trigger only
        for (int i = 0; i < 600; i++) spawner.Tick(1f); // Ten minutes of setup time
        Assert.IsFalse(spawner.ClockStarted);

        board.SetTile(Door.x, Door.y, board.DugStateOf(Door)); // The dig's conversion
        Assert.IsTrue(spawner.ClockStarted, "Started by the tile-change event, before any further Tick");
        Assert.AreEqual("Next hero: 5:00", TopBar.NextHeroReadout(spawner, null));

        for (int i = 0; i < 299; i++) spawner.Tick(1f);
        Assert.AreEqual(0, spawner.HeroesSpawned, "Not before firstHeroSeconds after the door");
        Assert.AreEqual(299f, spawner.ElapsedSeconds, 1e-3f);
        spawner.Tick(1f);
        Assert.AreEqual(1, spawner.HeroesSpawned, "300 s after the door was dug out");
        Assert.AreEqual(Door, spawner.ActiveHero.CurrentTile, "At the door");

        Hero first = spawner.ActiveHero;
        first.Health.TakeDamage(10000);
        first.Tick(Dt);    // The body dies
        spawner.Tick(0f);  // The director sees it
        Assert.IsTrue(spawner.NextHeroScheduled, "Wave 2 runs on its normal schedule");
        Assert.AreEqual(spawner.NextHeroDelaySeconds, spawner.SecondsUntilNextHero, 1e-3f);
        for (int i = 0; i < 119; i++) spawner.Tick(1f);
        Assert.AreEqual(1, spawner.HeroesSpawned);
        spawner.Tick(1f);
        Assert.AreEqual(2, spawner.HeroesSpawned, "120 s after the first was resolved, as before");
        Assert.AreEqual(spawner.MaxHealthForWave(2, false), spawner.ActiveHero.Health.MaxHealth, "Escalation unchanged");
    }

    // ---- 3. The order ----

    [Test]
    public void TheOrder_IsUnavailable_WhileTheDoorIsSealed_OrWithNoPath_AndRefusedThen()
    {
        Assert.AreEqual(ImpExpedition.DoorSealedReason, expedition.UnavailableReason(starter));
        Assert.IsFalse(expedition.Send(starter));

        board.OpenDoor(); // Open, but nothing connects the cavern to it
        Assert.AreEqual(ImpExpedition.NoPathReason, expedition.UnavailableReason(starter));
        Assert.IsFalse(expedition.CanSend(starter));
        Assert.IsFalse(expedition.Send(starter), "Refused, never a silent no-op");
        Assert.IsFalse(expedition.IsActive);
        Assert.IsFalse(starterDigger.OnExpedition, "Nothing changed");

        DigTunnel();
        Assert.IsNull(expedition.UnavailableReason(starter));
        Assert.IsTrue(expedition.CanSend(starter));
        Assert.AreEqual(ImpExpedition.NoImpReason, expedition.UnavailableReason(null));
    }

    [Test]
    public void ASentImp_WalksThePathToTheDoor_AndDepartsOnIt_Deactivated_NotDestroyed()
    {
        OpenDoorWithTunnel();
        Assert.IsTrue(expedition.Send(starter));
        Assert.AreEqual(ImpExpedition.ExpeditionPhase.Departing, expedition.Phase);
        Assert.AreSame(starter, expedition.ExpeditionImp);

        var visited = new List<Vector2Int> { starter.CurrentTile };
        StepUntil(() =>
        {
            if (visited[visited.Count - 1] != starter.CurrentTile) visited.Add(starter.CurrentTile);
            return expedition.Phase == ImpExpedition.ExpeditionPhase.Away;
        }, 10f, "the imp to walk out");

        CollectionAssert.AreEqual(
            Enumerable.Range(16, 16).Select(y => new Vector2Int(24, y)).ToList(), visited,
            "Up the tunnel tile by tile, (24,16) to the door (24,31): it walks, no teleport");
        Assert.IsFalse(starter.gameObject.activeSelf, "Out of the dungeon");
        Assert.IsTrue(starter != null, "Not destroyed: the same imp comes back");
        Assert.AreEqual(Door, starter.CurrentTile);
    }

    [Test]
    public void AnOrderedImp_DropsItsJob_TheJobStaysInThePool_AndItTakesNoJobsUntilBack()
    {
        var job = new Vector2Int(27, 16);
        board.SetTile(job.x, job.y, TileState.Designated);
        starterDigger.Tick(Dt);
        Assert.AreEqual(job, starterDigger.CurrentTarget, "Working before the order");

        OpenDoorWithTunnel();
        Assert.IsTrue(expedition.Send(starter));
        Assert.IsTrue(starterDigger.OnExpedition);
        Assert.IsNull(starterDigger.CurrentTarget, "Dropped");
        Assert.AreEqual(TileState.Designated, board.GetTile(job.x, job.y), "The job itself is untouched");

        // Walking out with the job still waiting: it never takes it (nor anything else).
        StepUntil(() =>
        {
            Assert.IsNull(starterDigger.CurrentTarget, "No job en route");
            Assert.IsFalse(starterDigger.IsDigging);
            return expedition.Phase == ImpExpedition.ExpeditionPhase.Away;
        }, 10f, "the imp to depart");

        // Away: even ticked directly, it selects nothing.
        for (int i = 0; i < 100; i++) starterDigger.Tick(1f);
        Assert.IsNull(starterDigger.CurrentTarget, "No jobs while away");

        // The job is still in the pool for any other imp.
        Imp other = AddImp("Imp2", new Vector2Int(22, 16));
        other.GetComponent<ImpDigger>().Tick(Dt);
        Assert.AreEqual(job, other.GetComponent<ImpDigger>().CurrentTarget, "Another imp takes it");
    }

    [Test]
    public void OneAtATime_ASecondOrder_IsRefused_FromOrderToReturn()
    {
        Imp other = AddImp("Imp2", new Vector2Int(22, 16));
        OpenDoorWithTunnel();
        Assert.IsTrue(expedition.Send(starter));

        Assert.AreEqual(ImpExpedition.AlreadyOutReason, expedition.UnavailableReason(other), "Departing counts as out");
        Assert.IsFalse(expedition.Send(other));
        Assert.IsFalse(expedition.Send(starter), "Nor the same imp twice");

        StepUntil(() => expedition.Phase == ImpExpedition.ExpeditionPhase.Away, 10f, "departure");
        Assert.IsFalse(expedition.Send(other), "Away counts as out");
        Assert.IsFalse(other.GetComponent<ImpDigger>().OnExpedition);

        expedition.Roll = () => 0.99f;
        FinishExpedition();
        Assert.IsTrue(expedition.CanSend(other), "Back: the next expedition can go");
    }

    // ---- 4. The trip ----

    [Test]
    public void ADepartedImp_RevealsNoFog_AndResumesRevealingOnReturn()
    {
        fog.RevealFromImp();
        SendStarterAndDepart();
        fog.RevealFromImp(); // The last reveal on the way out, from the door tile
        int seen = fog.ExploredCount;

        for (int i = 0; i < 200; i++)
        {
            expedition.Tick(0.5f);
            fog.RevealFromImp();
        }
        Assert.AreEqual(ImpExpedition.ExpeditionPhase.Away, expedition.Phase, "Still out after 100 s");
        Assert.AreEqual(seen, fog.ExploredCount, "Nothing revealed while away");

        expedition.Roll = () => 0.99f;
        FinishExpedition();
        Assert.IsTrue(starter.gameObject.activeSelf);
        starter.PlaceOnTile(new Vector2Int(40, 5)); // Back and moving: somewhere never seen
        fog.RevealFromImp();
        Assert.Greater(fog.ExploredCount, seen, "Revealing again once back");
        Assert.IsTrue(fog.IsExplored(new Vector2Int(40, 5)));
    }

    [Test]
    public void TheExpeditionClock_IsGameTime_PausedFramesDontAdvanceIt_DoubleSpeedCountsDouble()
    {
        Assert.AreEqual(120f, expedition.ExpeditionSeconds, "Serialized default");
        SendStarterAndDepart();
        Assert.AreEqual(0f, expedition.SecondsAway);
        Assert.AreEqual(120f, expedition.SecondsRemaining, 1e-3f);

        for (int i = 0; i < 10000; i++) expedition.Tick(0f); // Paused: Time.deltaTime is 0 at timeScale 0
        Assert.AreEqual(0f, expedition.SecondsAway, "Pause stops it");
        Assert.AreEqual(ImpExpedition.ExpeditionPhase.Away, expedition.Phase);

        expedition.Tick(0.5f * GameSpeed.Fast); // One half-second frame at 2×
        Assert.AreEqual(1f, expedition.SecondsAway, 1e-4f, "2× counts double");

        expedition.Roll = () => 0.99f;
        expedition.Tick(118.98f);
        Assert.AreEqual(ImpExpedition.ExpeditionPhase.Away, expedition.Phase, "Just short of 120 s");
        expedition.Tick(0.02f);
        Assert.IsFalse(expedition.IsActive, "Back at 120 s");
    }

    // ---- 5. The return ----

    [Test]
    public void Success_TheImpReturnsThroughTheDoor_WithAnUnleashedGoblin_OnTheFreeTileNearestTheDoor()
    {
        SendStarterAndDepart();
        expedition.Roll = () => 0f;
        int goblinsBefore = Object.FindObjectsByType<Goblin>().Length;
        FinishExpedition();

        Assert.AreEqual(1, expedition.ReturnCount);
        Assert.AreEqual(1, expedition.GoblinsRecruited);
        Assert.IsTrue(starter.gameObject.activeSelf, "The same imp, back");
        Assert.AreSame(starter, expedition.LastReturnedImp);
        Assert.AreEqual(Door, starter.CurrentTile, "Back in through the door");
        Assert.IsFalse(starterDigger.OnExpedition);

        Goblin recruit = expedition.LastRecruit;
        Assert.IsNotNull(recruit);
        Assert.AreEqual(goblinsBefore + 1, Object.FindObjectsByType<Goblin>().Length);
        Assert.AreEqual("Goblin4", recruit.name, "Continues Goblin1-3");
        Assert.AreEqual(Threshold, recruit.CurrentTile, "The free tile nearest the door");
        Assert.AreEqual(Threshold, recruit.SpawnTile, "Its Start places it there too");
        Assert.AreEqual("Imp back with Goblin4!", expedition.LastReturnMessage);

        GoblinAI ai = recruit.GetComponent<GoblinAI>();
        Assert.IsNotNull(ai);
        Assert.IsFalse(ai.IsLeashed, "Every recruit arrives unleashed");
        Assert.AreSame(recruit, ai.Goblin);
        Assert.AreSame(manager, ai.PlacementManager);
        Assert.AreSame(board, recruit.Board);
        Assert.AreSame(recruit.GetComponent<Health>(), recruit.Health);
        Assert.AreEqual(20, recruit.Health.MaxHealth, "The starting goblins' values");
        Assert.AreEqual(HealthTeam.Monster, recruit.Health.Team);
        CollectionAssert.Contains(DetailCard.Unassigned(Object.FindObjectsByType<GoblinAI>()), ai, "In the Unassigned list");

        // The imp steps in off the door, then works.
        Step();
        Assert.IsTrue(starter.IsMoving || starter.CurrentTile == Threshold, "Walking in");
    }

    [Test]
    public void TheRecruit_ShowsInAWarrensCardAsUnassigned_AndCanBeAssignedThere()
    {
        SendStarterAndDepart();
        expedition.Roll = () => 0f;
        FinishExpedition();
        GoblinAI recruit = expedition.LastRecruit.GetComponent<GoblinAI>();

        var uiGo = new GameObject("TestCardAndController");
        spawned.Add(uiGo);
        var controller = uiGo.AddComponent<PlacementController>();
        controller.PlacementManager = manager;
        var card = uiGo.AddComponent<DetailCard>();
        card.PlacementController = controller;
        card.PlacementManager = manager;
        Assert.IsTrue(manager.TryPlaceBuilt(PlaceableType.Warren, new Vector2Int(22, 17), HeartTile, out Placeable warren));

        controller.ClickBoardTile(warren.Tile);
        DetailCard.AssignmentRow row = card.ShownAssignmentRows.Single(r => r.goblin == recruit);
        Assert.AreEqual(DetailCard.AssignAction.Assign, row.action, "Listed under Unassigned with Assign");
        Assert.AreEqual("Goblin4", row.text);

        Assert.IsTrue(card.ClickAssignmentRow(row));
        Assert.IsTrue(recruit.IsLeashed, "Assigned through the existing card");
    }

    [Test]
    public void TheRecruitsName_NeverCollides_InactiveGoblinsCountToo()
    {
        Goblin dead = AddGoblin("Goblin5", new Vector2Int(26, 13));
        dead.gameObject.SetActive(false); // As a dead goblin is
        Assert.AreEqual(5, expedition.HighestGoblinNumber());

        SendStarterAndDepart();
        expedition.Roll = () => 0f;
        FinishExpedition();

        Assert.AreEqual("Goblin6", expedition.LastRecruit.name);
    }

    [Test]
    public void TheRecruit_SkipsAnOccupiedTile_ForTheNextNearestFreeOne()
    {
        SendStarterAndDepart();
        AddGoblin("Goblin9", Threshold); // A living goblin stands just inside the door
        expedition.Roll = () => 0f;
        FinishExpedition();

        Assert.AreEqual("Goblin10", expedition.LastRecruit.name);
        Assert.AreEqual(new Vector2Int(24, 29), expedition.LastRecruit.CurrentTile, "One further in");
    }

    [Test]
    public void EmptyHanded_TheImpReturnsAlone()
    {
        SendStarterAndDepart();
        expedition.Roll = () => 0.99f;
        int goblinsBefore = Object.FindObjectsByType<Goblin>(FindObjectsInactive.Include).Length;
        FinishExpedition();

        Assert.AreEqual(1, expedition.ReturnCount);
        Assert.AreEqual(0, expedition.GoblinsRecruited);
        Assert.IsNull(expedition.LastRecruit);
        Assert.IsTrue(starter.gameObject.activeSelf, "The imp is back: it never dies out there");
        Assert.AreEqual(goblinsBefore, Object.FindObjectsByType<Goblin>(FindObjectsInactive.Include).Length, "No goblin");
        Assert.AreEqual(0, Object.FindObjectsByType<Hero>().Length, "Nothing follows it in");
        Assert.AreEqual("Imp back empty-handed.", expedition.LastReturnMessage);
    }

    [Test]
    public void TheRoll_SucceedsBelowTheSuccessChance_SeventyFivePercentByDefault()
    {
        Assert.AreEqual(0.75f, expedition.SuccessChance, "Serialized default");

        SendStarterAndDepart();
        expedition.Roll = () => 0.7499f;
        FinishExpedition();
        Assert.IsNotNull(expedition.LastRecruit, "Below 0.75: a goblin");

        SendStarterAndDepart();
        expedition.Roll = () => 0.75f;
        FinishExpedition();
        Assert.IsNull(expedition.LastRecruit, "At 0.75: empty-handed");

        // The unseeded default roll: over many trips both outcomes occur.
        expedition.Roll = null;
        expedition.ExpeditionSeconds = 0f;
        int with = 0, without = 0;
        for (int i = 0; i < 60; i++)
        {
            Assert.IsTrue(expedition.Send(starter), "Trip " + i);
            StepUntil(() => !expedition.IsActive, 30f, "trip " + i);
            if (expedition.LastRecruit != null)
            {
                with++;
                Object.DestroyImmediate(expedition.LastRecruit.gameObject); // Keep the door clear
            }
            else without++;
        }
        Assert.Greater(with, 0, "Some trips bring a goblin");
        Assert.Greater(without, 0, "Some come back empty-handed");
    }

    [Test]
    public void NoFreeTileByTheDoor_TheExpeditionStaysOut_AndReturnsOnceOneFrees()
    {
        SendStarterAndDepart();
        for (int y = 19; y < Threshold.y; y++) board.SetTile(24, y, TileState.Rock); // Only the threshold left
        Goblin blocker = AddGoblin("Goblin7", Threshold);
        expedition.Roll = () => 0f;

        expedition.Tick(expedition.ExpeditionSeconds + 10f);
        for (int i = 0; i < 10; i++) expedition.Tick(1f);
        Assert.AreEqual(ImpExpedition.ExpeditionPhase.Away, expedition.Phase, "No room: still out, retrying");
        Assert.IsFalse(starter.gameObject.activeSelf);

        blocker.PlaceOnTile(new Vector2Int(22, 15));
        expedition.Tick(Dt);
        Assert.IsFalse(expedition.IsActive, "Back on the next tick");
        Assert.AreEqual(Threshold, expedition.LastRecruit.CurrentTile);
    }

    [Test]
    public void AReturnedImp_ResumesJobs()
    {
        SendStarterAndDepart();
        expedition.Roll = () => 0.99f;
        FinishExpedition();

        var job = new Vector2Int(25, 25);
        board.SetTile(job.x, job.y, TileState.Designated);
        starterDigger.Tick(Dt);
        Assert.AreEqual(job, starterDigger.CurrentTarget, "Working again");
        StepUntil(() => board.GetTile(job.x, job.y) == TileState.Floor, 20f, "the dig");
    }

    // ---- 6. The #161 interaction ----

    [Test]
    public void AHeartListWhileAnImpIsAway_NeverReusesTheDepartedImpsNumber()
    {
        var printerGo = new GameObject("TestPrinter");
        spawned.Add(printerGo);
        var printing = printerGo.AddComponent<HeartPrinting>();
        printing.Board = board;
        printing.Renderer = boardRenderer;
        printing.Heart = heart;
        printing.TemplateImp = starter;
        printing.PlacementManager = manager;
        printing.Stockpile = stockpile;
        printing.Tick(0f);
        Imp imp2 = printing.Pick(0);
        Assert.AreEqual("Imp2", imp2.name);

        OpenDoorWithTunnel();
        Assert.IsTrue(expedition.Send(imp2));
        StepUntil(() => expedition.Phase == ImpExpedition.ExpeditionPhase.Away, 15f, "Imp2 to depart");
        Assert.IsFalse(imp2.gameObject.activeSelf);

        printing.Tick(printing.CycleSeconds); // A new list while Imp2 is away
        Assert.AreEqual(2, printing.HighestImpNumber(), "The away imp still counts");
        CollectionAssert.AreEqual(new[] { "Imp3", "Imp4", "Imp5" }, printing.Candidates.Select(c => c.Name).ToArray());

        Imp imp3 = printing.Pick(0);
        expedition.Roll = () => 0.99f;
        FinishExpedition();
        Assert.IsTrue(imp2.gameObject.activeSelf);
        Assert.AreEqual(1, Object.FindObjectsByType<Imp>().Count(i => i.name == "Imp2"), "No clash on its return");
        Assert.AreEqual("Imp3", imp3.name);
    }

    // ---- 7. Chrome: the panel, the toast, the top bar ----

    [Test]
    public void ImpPanel_OpensOnAClickOnAnImp_OrderDisabledWithAReason_EnabledWhenAvailable_AndSends()
    {
        var uiGo = new GameObject("TestPanel");
        spawned.Add(uiGo);
        var controller = uiGo.AddComponent<PlacementController>();
        controller.PlacementManager = manager;
        var panel = uiGo.AddComponent<ImpPanel>();
        panel.ImpExpedition = expedition;
        panel.PlacementController = controller;
        panel.Build();

        controller.ClickBoardTile(new Vector2Int(21, 18)); // No imp there
        Assert.IsFalse(panel.IsOpen);

        controller.ClickBoardTile(starter.CurrentTile);
        Assert.IsTrue(panel.IsOpen);
        Assert.AreSame(starter, panel.SelectedImp);
        Assert.IsTrue(panel.PanelObject.activeSelf);
        Assert.IsFalse(panel.SendButton.interactable, "Door sealed: disabled");
        Assert.AreEqual(ImpExpedition.DoorSealedReason, panel.ReasonLabel.text);
        Assert.IsTrue(panel.ReasonLabel.gameObject.activeSelf, "The reason is shown");
        Assert.IsFalse(panel.ClickSend());
        Assert.AreEqual(ImpPanel.WorkingText, panel.StatusText);

        OpenDoorWithTunnel();
        panel.Repaint();
        Assert.IsTrue(panel.SendButton.interactable);
        Assert.IsFalse(panel.ReasonLabel.gameObject.activeSelf);

        Assert.IsTrue(panel.ClickSend());
        Assert.IsTrue(expedition.IsActive);
        Assert.AreEqual(ImpPanel.DepartingText, panel.StatusText);
        Assert.IsFalse(panel.SendButton.interactable, "Out: disabled");
        Assert.AreEqual(ImpExpedition.AlreadyOutReason, panel.ReasonLabel.text);

        StepUntil(() => expedition.Phase == ImpExpedition.ExpeditionPhase.Away, 10f, "departure");
        expedition.Tick(20f);
        panel.Repaint();
        Assert.AreEqual("On an expedition: back in 1:40.", panel.StatusText);

        panel.Close();
        Assert.IsFalse(panel.PanelObject.activeSelf);
    }

    [Test]
    public void AReturn_RaisesOneToast()
    {
        var toastsGo = new GameObject("TestToasts");
        spawned.Add(toastsGo);
        var toasts = toastsGo.AddComponent<AlertToasts>();
        toasts.PlacementManager = manager;
        toasts.ImpExpedition = expedition;
        toasts.CheckAlerts(); // Baseline

        SendStarterAndDepart();
        toasts.CheckAlerts();
        CollectionAssert.IsEmpty(toasts.ActiveToastTexts, "No toast on the way out");

        expedition.Roll = () => 0f;
        FinishExpedition();
        toasts.CheckAlerts();
        toasts.CheckAlerts();
        CollectionAssert.AreEqual(new[] { "Imp back with Goblin4!" }, toasts.ActiveToastTexts.ToArray(), "Once");
    }
}
