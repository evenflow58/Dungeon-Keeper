using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class AlertToastsTests
{
    private static readonly Vector2Int HeartTile = new Vector2Int(24, 14);
    private static readonly Vector2Int PlaceFrom = new Vector2Int(24, 16);
    private const float Dt = 0.02f;

    private GameObject managerGo;
    private DungeonBoard board;
    private BoardRenderer boardRenderer;
    private PlacementManager manager;
    private GameObject heartGo;
    private Heart heart;
    private GameObject directorGo;
    private HeroSpawner spawner;
    private AlertToasts alerts;
    private readonly List<GameObject> goblinGos = new List<GameObject>();

    [SetUp]
    public void SetUp()
    {
        managerGo = new GameObject("TestDungeonManager");
        board = managerGo.AddComponent<DungeonBoard>();
        board.InitializeBoard();
        boardRenderer = managerGo.AddComponent<BoardRenderer>();
        boardRenderer.Board = board;
        manager = managerGo.AddComponent<PlacementManager>();
        manager.Board = board;
        manager.Renderer = boardRenderer;

        heartGo = new GameObject("TestHeart");
        heart = heartGo.AddComponent<Heart>();
        heart.Board = board;
        heart.Renderer = boardRenderer;
        heart.Health = heartGo.GetComponent<Health>();
        heart.PlaceOnTile(HeartTile);

        directorGo = new GameObject("TestDirector");
        spawner = directorGo.AddComponent<HeroSpawner>();
        spawner.PlacementManager = manager;
        spawner.Board = board;
        spawner.Renderer = boardRenderer;
        spawner.Heart = heart;

        alerts = directorGo.AddComponent<AlertToasts>();
        alerts.PlacementManager = manager;
        alerts.HeroSpawner = spawner;
    }

    [TearDown]
    public void TearDown()
    {
        foreach (Hero h in Object.FindObjectsByType<Hero>(FindObjectsInactive.Include))
            if (h.name.StartsWith("Hero ")) Object.DestroyImmediate(h.gameObject);
        foreach (GameObject go in goblinGos) if (go != null) Object.DestroyImmediate(go);
        goblinGos.Clear();
        if (directorGo != null) Object.DestroyImmediate(directorGo);
        if (heartGo != null) Object.DestroyImmediate(heartGo);
        if (managerGo != null) Object.DestroyImmediate(managerGo);
    }

    private Goblin AddGoblin(int x, int y, float hunger = 100f)
    {
        var go = new GameObject("TestGoblin" + goblinGos.Count);
        goblinGos.Add(go);
        var goblin = go.AddComponent<Goblin>();
        goblin.Board = board;
        goblin.Renderer = boardRenderer;
        goblin.LogStats = false;
        goblin.StarvationSecondsToDie = float.PositiveInfinity; // starving, not dying, for these tests
        goblin.PlaceOnTile(new Vector2Int(x, y));
        goblin.Hunger = hunger;
        return goblin;
    }

    /// <summary>Ticks the goblin's needs clock until his hunger runs out (240 s to empty at the default rate).</summary>
    private static void StarveByTicking(Goblin g)
    {
        for (int i = 0; i < 100 && !g.IsWeakened; i++) g.Tick(1f);
        Assert.IsTrue(g.IsWeakened);
    }

    private SpikeTrap PlaceTrap(int x, int y)
    {
        Assert.IsTrue(manager.TryPlaceBuilt(PlaceableType.SpikeTrap, new Vector2Int(x, y), PlaceFrom, out Placeable p));
        return p.GetComponent<SpikeTrap>();
    }

    private void DigColumnToDoor()
    {
        for (int y = 19; y <= 30; y++) board.SetTile(24, y, TileState.Floor);
    }

    private int Count(string message)
    {
        int n = 0;
        foreach (string t in alerts.ActiveToastTexts) if (t == message) n++;
        return n;
    }

    // --- Baseline ---

    [Test]
    public void FirstPoll_OnlySnapshots_StagedStatesDontFire()
    {
        AddGoblin(23, 15, hunger: 0f);       // already starving
        PlaceTrap(22, 14).TryTrigger(out _); // already spent

        alerts.CheckAlerts();
        alerts.CheckAlerts();

        Assert.AreEqual(0, alerts.ActiveToastTexts.Count);
    }

    // --- Starving ---

    [Test]
    public void Starving_FiresOncePerOnset()
    {
        Goblin g = AddGoblin(23, 15, hunger: 1f);
        alerts.CheckAlerts(); // baseline: fed

        StarveByTicking(g);
        alerts.CheckAlerts();
        Assert.AreEqual(1, Count(AlertToasts.StarvingMessage));
        Assert.AreEqual("Goblin starving!", alerts.ActiveToastTexts[0], "DESIGN §A.7 verbatim");

        g.Tick(5f);
        alerts.CheckAlerts();
        alerts.CheckAlerts();
        Assert.AreEqual(1, Count(AlertToasts.StarvingMessage), "Not repeated while he stays starving");
    }

    [Test]
    public void Starving_RearmsWhenHeEats_FiresAgainNextTime()
    {
        Goblin g = AddGoblin(23, 15, hunger: 1f);
        alerts.CheckAlerts();
        StarveByTicking(g);
        alerts.CheckAlerts();
        alerts.AgeToasts(alerts.ToastVisibleSeconds); // clear the first toast

        g.Hunger = 50f; // he eats
        alerts.CheckAlerts();
        Assert.AreEqual(0, alerts.ActiveToastTexts.Count, "Eating is silent");

        g.Hunger = 0f;  // starving again
        alerts.CheckAlerts();
        Assert.AreEqual(1, Count(AlertToasts.StarvingMessage));
    }

    [Test]
    public void DeadGoblin_DroppedSilently()
    {
        Goblin g = AddGoblin(23, 15, hunger: 1f);
        g.Health = g.gameObject.AddComponent<Health>();
        alerts.CheckAlerts();

        g.Health.TakeDamage(1000);
        g.Tick(Dt); // dies and deactivates
        Assert.DoesNotThrow(() => alerts.CheckAlerts());
        Assert.AreEqual(0, alerts.ActiveToastTexts.Count);
    }

    // --- Traps ---

    [Test]
    public void Trap_FiresOncePerSpend_RearmSilent_FiresAgain()
    {
        SpikeTrap trap = PlaceTrap(22, 14);
        alerts.CheckAlerts(); // baseline: armed

        Assert.IsTrue(trap.TryTrigger(out _));
        alerts.CheckAlerts();
        alerts.CheckAlerts();
        Assert.AreEqual(1, Count(AlertToasts.TrapSpentMessage));
        Assert.AreEqual("Trap needs rearming.", alerts.ActiveToastTexts[0], "DESIGN §A.7 verbatim");
        alerts.AgeToasts(alerts.ToastVisibleSeconds);

        trap.Rearm(); // the imp
        alerts.CheckAlerts();
        Assert.AreEqual(0, alerts.ActiveToastTexts.Count, "Rearming is silent");

        Assert.IsTrue(trap.TryTrigger(out _));
        alerts.CheckAlerts();
        Assert.AreEqual(1, Count(AlertToasts.TrapSpentMessage), "The next spend fires again");
    }

    [Test]
    public void NewTrap_PlacedArmed_IsBaselinedNotFired()
    {
        alerts.CheckAlerts();
        PlaceTrap(22, 14);
        alerts.CheckAlerts();
        Assert.AreEqual(0, alerts.ActiveToastTexts.Count);
    }

    // --- Heroes ---

    [Test]
    public void HeroSpawn_FiresOnce()
    {
        DigColumnToDoor();
        alerts.CheckAlerts(); // baseline: no hero

        spawner.Tick(300f);
        Assert.AreEqual(1, spawner.HeroesSpawned);
        alerts.CheckAlerts();
        alerts.CheckAlerts();

        Assert.AreEqual(1, Count(AlertToasts.HeroApproachingMessage));
        Assert.AreEqual("Hero approaching!", alerts.ActiveToastTexts[0], "DESIGN §A.7 verbatim");
    }

    // --- Stack behavior ---

    [Test]
    public void TwoOnsetsInOneWindow_BothShown_NewestOnTop()
    {
        Goblin g = AddGoblin(23, 15, hunger: 1f);
        SpikeTrap trap = PlaceTrap(22, 14);
        alerts.CheckAlerts();

        StarveByTicking(g);
        trap.TryTrigger(out _);
        alerts.CheckAlerts(); // goblins are checked before traps

        CollectionAssert.AreEqual(new[] { AlertToasts.TrapSpentMessage, AlertToasts.StarvingMessage }, alerts.ActiveToastTexts,
            "Display order: newest (fired last) on top");
    }

    [Test]
    public void Expiry_AfterVisibleSeconds()
    {
        alerts.Show(AlertToasts.StarvingMessage);

        alerts.AgeToasts(alerts.ToastVisibleSeconds - 0.1f);
        Assert.AreEqual(1, alerts.ActiveToastTexts.Count, "Still showing just before expiry");

        alerts.AgeToasts(0.1f);
        Assert.AreEqual(0, alerts.ActiveToastTexts.Count);
    }

    [Test]
    public void Cap_OldestDropsWhenAFourthArrives()
    {
        alerts.Show("1");
        alerts.Show("2");
        alerts.Show("3");
        alerts.Show("4");

        CollectionAssert.AreEqual(new[] { "4", "3", "2" }, alerts.ActiveToastTexts);
    }

    [Test]
    public void Defaults_MatchTheBrief()
    {
        Assert.AreEqual(0.25f, alerts.PollIntervalSeconds);
        Assert.AreEqual(4f, alerts.ToastVisibleSeconds);
        Assert.AreEqual(3, alerts.MaxVisibleToasts);
    }

    [Test]
    public void Unwired_NoExceptions_NothingShown()
    {
        alerts.PlacementManager = null;
        alerts.HeroSpawner = null;

        Assert.DoesNotThrow(() =>
        {
            alerts.CheckAlerts();
            alerts.CheckAlerts();
            alerts.AgeToasts(10f);
        });
        Assert.AreEqual(0, alerts.ActiveToastTexts.Count);
    }
}
