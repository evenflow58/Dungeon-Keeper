using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// #161: Dungeon Heart printing — the game-time cycle (fixed boundaries, pause, picking doesn't reset it), candidate
/// lists (3 per cycle, replaced at each boundary, default stats, imp-convention names), Pick (spawn tile, fallback,
/// worker wiring, one per cycle, free), decline and ignore, no automatic spawning, the Heart panel and the toast.
/// </summary>
public class HeartPrintingTests
{
    private static readonly Vector2Int HeartTile = new Vector2Int(24, 14);
    private static readonly Vector2Int SpawnTile = new Vector2Int(24, 16); // The starter's spawn: cavern center
    private static readonly Vector2Int Aside = new Vector2Int(22, 17);     // Somewhere in the cavern off the spawn tile
    private const float Cycle = 300f;
    private const float Dt = 0.02f;

    private GameObject managerGo;
    private DungeonBoard board;
    private BoardRenderer boardRenderer;
    private PlacementManager manager;
    private Stockpile stockpile;
    private GameObject heartGo;
    private Heart heart;
    private GameObject starterGo;
    private Imp starter;
    private ImpDigger starterDigger;
    private GameObject printerGo;
    private HeartPrinting printing;

    [SetUp]
    public void SetUp()
    {
        // Default board: 48x32 Rock with the 6x6 Floor cavern at (21,13)-(26,18).
        managerGo = new GameObject("TestDungeonManager");
        board = managerGo.AddComponent<DungeonBoard>();
        board.InitializeBoard();
        boardRenderer = managerGo.AddComponent<BoardRenderer>();
        boardRenderer.Board = board;
        manager = managerGo.AddComponent<PlacementManager>();
        manager.Board = board;
        manager.Renderer = boardRenderer;
        stockpile = managerGo.AddComponent<Stockpile>();

        heartGo = new GameObject("TestHeart");
        heart = heartGo.AddComponent<Heart>();
        heart.Board = board;
        heart.Renderer = boardRenderer;
        heart.Health = heartGo.GetComponent<Health>();
        heart.PlaceOnTile(HeartTile);

        // The scene's starter imp: named "Imp", standing on the spawn tile.
        starterGo = new GameObject("Imp");
        starter = starterGo.AddComponent<Imp>();
        starter.Board = board;
        starter.Renderer = boardRenderer;
        starter.Spawn();
        starterDigger = starterGo.AddComponent<ImpDigger>();
        starterDigger.Board = board;
        starterDigger.Imp = starter;
        starterDigger.PlacementManager = manager;
        starterDigger.Stockpile = stockpile;

        printerGo = new GameObject("TestPrinter");
        printing = printerGo.AddComponent<HeartPrinting>();
        printing.Board = board;
        printing.Renderer = boardRenderer;
        printing.Heart = heart;
        printing.TemplateImp = starter;
        printing.PlacementManager = manager;
        printing.Stockpile = stockpile;
    }

    [TearDown]
    public void TearDown()
    {
        foreach (Imp imp in Object.FindObjectsByType<Imp>(FindObjectsInactive.Include))
            Object.DestroyImmediate(imp.gameObject); // The starter and every printed imp
        foreach (Canvas c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include))
            Object.DestroyImmediate(c.gameObject);   // The panel's chrome
        if (printerGo != null) Object.DestroyImmediate(printerGo);
        if (heartGo != null) Object.DestroyImmediate(heartGo);
        if (managerGo != null) Object.DestroyImmediate(managerGo);
    }

    private static int ImpCount => Object.FindObjectsByType<Imp>().Length;

    private static string Names(IEnumerable<ImpCandidate> list) => string.Join(",", list.Select(c => c.Name));

    // ---- cycle timer ----

    [Test]
    public void TheFirstList_IsThereAtGameStart_ThreeCandidates_NamedAfterTheStarter_WithDefaultStats()
    {
        Assert.AreEqual(-1, printing.Cycle, "Nothing before the clock starts");
        Assert.AreEqual(0, printing.Candidates.Count);

        printing.Tick(0f); // Start's call: game start

        Assert.AreEqual(0, printing.Cycle);
        Assert.AreEqual(HeartPrinting.CycleState.Open, printing.State);
        Assert.AreEqual("Imp2,Imp3,Imp4", Names(printing.Candidates), "The starter \"Imp\" is Imp1");
        foreach (ImpCandidate c in printing.Candidates)
        {
            Assert.AreEqual(1f, c.Stats.DigSpeed, c.Name);
            Assert.AreEqual(1f, c.Stats.BuildSpeed, c.Name);
            Assert.AreEqual(1f, c.Stats.MoveSpeed, c.Name);
        }
        Assert.AreEqual(Cycle, printing.SecondsToNextCycle, 1e-3f);
        Assert.AreEqual(300f, printing.CycleSeconds, "Serialized default");
        Assert.AreEqual(3, printing.CandidatesPerCycle, "Serialized default");
        Assert.AreEqual(1f, ImpStats.Default.DigSpeed);
        Assert.AreEqual(1f, ImpStats.Default.BuildSpeed);
        Assert.AreEqual(1f, ImpStats.Default.MoveSpeed);
        Assert.AreEqual(1f, starter.Stats.MoveSpeed, "The starter carries the defaults too");
    }

    [Test]
    public void Boundaries_AreFixedFromGameStart_AndEachOneReplacesTheList()
    {
        printing.Tick(0f);
        ImpCandidate[] first = printing.Candidates.ToArray();

        printing.Tick(Cycle - 0.01f);
        Assert.AreEqual(0, printing.Cycle, "Just before 300 s: still the first list");
        CollectionAssert.AreEqual(first, printing.Candidates.ToArray(), "The same list, untouched");

        printing.Tick(0.02f);
        Assert.AreEqual(1, printing.Cycle, "300 s: the second list");
        Assert.AreEqual(3, printing.Candidates.Count);
        CollectionAssert.IsEmpty(printing.Candidates.Intersect(first), "Replaced, not appended");
        Assert.AreEqual(Cycle * 2f - printing.Elapsed, printing.SecondsToNextCycle, 1e-3f);

        printing.Tick(Cycle);
        Assert.AreEqual(2, printing.Cycle, "600 s: the third");
    }

    [Test]
    public void TheClock_RunsOnGameTime_PausedFramesDontAdvanceIt_AndDoubleSpeedFramesCountDouble()
    {
        printing.Tick(0f);
        printing.Tick(Cycle - 1f);
        for (int i = 0; i < 1000; i++) printing.Tick(0f); // Paused: Time.deltaTime is 0 at timeScale 0
        Assert.AreEqual(0, printing.Cycle, "No boundary while paused, however many frames");
        Assert.AreEqual(Cycle - 1f, printing.Elapsed, 1e-3f);
        printing.Tick(-5f);
        Assert.AreEqual(Cycle - 1f, printing.Elapsed, 1e-3f, "A negative step is ignored");

        // At 2× a 0.5 s real frame is a 1 s game frame: it reaches the boundary in half the real time.
        printing.Tick(0.5f * GameSpeed.Fast);
        Assert.AreEqual(1, printing.Cycle);
    }

    [Test]
    public void Picking_DoesNotResetTheClock_TheNextListComesAtTheNextBoundary()
    {
        printing.Tick(0f);
        starter.PlaceOnTile(Aside);
        printing.Tick(100f);

        Assert.IsNotNull(printing.Pick(0));
        Assert.AreEqual(Cycle - 100f, printing.SecondsToNextCycle, 1e-3f, "Still counting to 300, not to 400");

        printing.Tick(Cycle - 100f - 0.01f);
        Assert.AreEqual(0, printing.Cycle);
        Assert.AreEqual(HeartPrinting.CycleState.Picked, printing.State);
        printing.Tick(0.02f);
        Assert.AreEqual(1, printing.Cycle, "The next list at 300 s, 200 s after the pick");
        Assert.AreEqual(HeartPrinting.CycleState.Open, printing.State);
        Assert.AreEqual(3, printing.Candidates.Count);
    }

    // ---- pick ----

    [Test]
    public void Pick_SpawnsTheChosenImp_OnTheSpawnTile_ForFree_AndClosesTheCycle()
    {
        printing.Tick(0f);
        starter.PlaceOnTile(Aside);
        int stoneBefore = stockpile.Count(MaterialType.Stone);
        ImpCandidate chosen = printing.Candidates[1];

        Imp imp = printing.Pick(chosen);

        Assert.IsNotNull(imp);
        Assert.AreEqual("Imp3", imp.name);
        Assert.AreEqual(SpawnTile, imp.CurrentTile, "The starter's own spawn tile");
        Assert.AreEqual(boardRenderer.GetTileCenterWorldPosition(SpawnTile.x, SpawnTile.y), imp.transform.position);
        Assert.AreEqual(1f, imp.Stats.DigSpeed, "The candidate's stats ride along");
        Assert.AreEqual(2, ImpCount);
        Assert.AreEqual(stoneBefore, stockpile.Count(MaterialType.Stone), "Free: nothing is spent");
        Assert.AreEqual(HeartPrinting.CycleState.Picked, printing.State);
        Assert.AreEqual("Imp3", printing.PickedName);
        Assert.AreEqual(0, printing.Candidates.Count, "The list is used up");
        Assert.AreEqual(1, printing.ImpsPrinted);

        Assert.IsNull(printing.Pick(chosen), "At most one pick per cycle");
        Assert.IsNull(printing.Pick(0));
        Assert.AreEqual(2, ImpCount);
    }

    [Test]
    public void Pick_RejectsACandidateFromAnOldList_AndNull()
    {
        printing.Tick(0f);
        starter.PlaceOnTile(Aside);
        ImpCandidate stale = printing.Candidates[0];
        printing.Tick(Cycle);

        Assert.IsNull(printing.Pick(stale), "Replaced at the boundary: no longer pickable");
        Assert.IsNull(printing.Pick(null));
        Assert.IsNull(printing.Pick(3));
        Assert.IsNull(printing.Pick(-1));
        Assert.AreEqual(HeartPrinting.CycleState.Open, printing.State, "Nothing changed");
        Assert.AreEqual(1, ImpCount);
    }

    [Test]
    public void ThePickedImp_HasTheStartersWiring_AndCopiesItsSerializedValues()
    {
        var grid = managerGo.AddComponent<PowerGrid>();
        var mana = managerGo.AddComponent<ManaFlow>();
        var fog = managerGo.AddComponent<FogOfWar>();
        fog.Board = board;
        fog.Imp = starter;
        fog.RevealAround(HeartTile);
        printing.PowerGrid = grid;
        printing.ManaFlow = mana;
        printing.FogOfWar = fog;
        starter.MoveSpeed = 3.5f;               // Tuned in the Inspector: the printed imp matches
        starterDigger.DigSecondsPerTile = 1.75f;
        starter.PlaceOnTile(Aside);
        printing.Tick(0f);

        Imp imp = printing.Pick(0);

        var digger = imp.GetComponent<ImpDigger>();
        Assert.IsNotNull(digger, "An ImpDigger, like the starter");
        Assert.AreSame(imp, digger.Imp, "Its own Imp, not the starter's");
        Assert.AreSame(board, digger.Board);
        Assert.AreSame(manager, digger.PlacementManager);
        Assert.AreSame(stockpile, digger.Stockpile);
        Assert.AreSame(grid, digger.PowerGrid);
        Assert.AreSame(mana, digger.ManaFlow);
        Assert.AreSame(board, imp.Board);
        Assert.AreSame(boardRenderer, imp.Renderer);
        Assert.AreEqual(3.5f, imp.MoveSpeed);
        Assert.AreEqual(1.75f, digger.DigSecondsPerTile);
        CollectionAssert.Contains(fog.ExtraRevealers.ToArray(), imp, "It reveals fog");
    }

    [Test]
    public void ThePickedImp_TakesDigAndBuildJobs_LikeTheStarter()
    {
        printing.Tick(0f);
        starter.PlaceOnTile(Aside);
        Imp imp = printing.Pick(0);
        var digger = imp.GetComponent<ImpDigger>();
        digger.DigSecondsPerTile = 0.5f;
        int stoneBefore = stockpile.Count(MaterialType.Stone) + stockpile.Count(MaterialType.Glimmerstone)
            + stockpile.Count(MaterialType.Deepstone);

        // Dig: rock east of the cavern.
        board.SetTile(27, 16, TileState.Designated);
        for (int i = 0; i < 1000 && board.GetTile(27, 16) != TileState.Floor; i++)
        {
            digger.Tick(Dt);
            imp.Advance(Dt);
        }
        Assert.AreEqual(TileState.Floor, board.GetTile(27, 16), "The printed imp dug it");
        int stoneAfter = stockpile.Count(MaterialType.Stone) + stockpile.Count(MaterialType.Glimmerstone)
            + stockpile.Count(MaterialType.Deepstone);
        Assert.AreEqual(stoneBefore + 1, stoneAfter, "And the dig paid into the stockpile");

        // Build: a construction site in the cavern.
        Assert.IsTrue(manager.TryPlace(PlaceableType.LairCot, new Vector2Int(22, 14), SpawnTile, out Placeable site));
        Assert.IsFalse(site.IsBuilt);
        for (int i = 0; i < 5000 && !site.IsBuilt; i++)
        {
            digger.Tick(Dt);
            imp.Advance(Dt);
        }
        Assert.IsTrue(site.IsBuilt, "The printed imp built it");
    }

    [Test]
    public void AnOccupiedSpawnTile_SendsTheImpToTheNearestFreeFloorToTheHeart()
    {
        printing.Tick(0f); // The starter still stands on (24,16)

        Assert.IsTrue(printing.TryFindSpawnTile(out Vector2Int tile));
        Assert.AreEqual(new Vector2Int(25, 14), tile, "Walking distance 1 from the Heart, first in +x, −x, +y, −y order");
        Imp imp = printing.Pick(0);
        Assert.AreEqual(new Vector2Int(25, 14), imp.CurrentTile);

        // Next cycle: (24,16) still taken, (25,14) now taken by Imp2: the next nearest.
        printing.Tick(Cycle);
        Imp second = printing.Pick(0);
        Assert.AreEqual(new Vector2Int(23, 14), second.CurrentTile);
    }

    [Test]
    public void ABlockedSpawnTile_FallsBack_AndUnexploredOrBuiltOnTilesNeverTakeAnImp()
    {
        printing.Tick(0f);
        starter.PlaceOnTile(Aside);
        Assert.IsTrue(manager.TryPlaceBuilt(PlaceableType.LairCot, SpawnTile, Aside, out _), "A cot on the spawn tile");
        Assert.IsTrue(manager.TryPlaceBuilt(PlaceableType.LairCot, new Vector2Int(25, 14), Aside, out _));
        Assert.IsTrue(printing.TryFindSpawnTile(out Vector2Int tile));
        Assert.AreEqual(new Vector2Int(23, 14), tile, "Past the cot beside the Heart");

        // Only explored floor counts.
        var fog = managerGo.AddComponent<FogOfWar>();
        fog.Board = board;
        printing.FogOfWar = fog;
        Assert.IsFalse(printing.TryFindSpawnTile(out _), "Nothing explored: nowhere to spawn");
        Assert.IsNull(printing.Pick(0), "The pick fails...");
        Assert.AreEqual(HeartPrinting.CycleState.Open, printing.State, "...and the list stays pickable");
        Assert.AreEqual(1, ImpCount);

        fog.RevealAround(new Vector2Int(26, 18)); // Sight 3: x 23..29, y 15..21 (not the Heart's row)
        Assert.IsTrue(printing.TryFindSpawnTile(out tile));
        Assert.IsTrue(fog.IsExplored(tile));
        Assert.IsTrue(board.IsWalkable(tile.x, tile.y));
        Assert.IsNull(manager.GetAt(tile));
        Assert.IsNotNull(printing.Pick(0));
    }

    [Test]
    public void ADestroyedHeart_PrintsNothing()
    {
        printing.Tick(0f);
        starter.PlaceOnTile(Aside);
        heart.Health.MaxHealth = Heart.DesignMaxHealth;
        heart.Health.TakeDamage(100000);
        Assume.That(heart.IsDestroyed);

        Assert.IsNull(printing.Pick(0));
        Assert.AreEqual(1, ImpCount);
    }

    // ---- decline, ignore, no auto-spawn ----

    [Test]
    public void DeclineAll_SpawnsNothing_AndTheNextBoundaryBringsANewList()
    {
        printing.Tick(0f);
        starter.PlaceOnTile(Aside);

        Assert.IsTrue(printing.DeclineAll());
        Assert.AreEqual(HeartPrinting.CycleState.Declined, printing.State);
        Assert.AreEqual(0, printing.Candidates.Count);
        Assert.IsNull(printing.Pick(0), "Nothing left to pick this cycle");
        Assert.IsFalse(printing.DeclineAll(), "Already declined");
        Assert.AreEqual(1, ImpCount);

        printing.Tick(Cycle);
        Assert.AreEqual(HeartPrinting.CycleState.Open, printing.State);
        Assert.AreEqual("Imp2,Imp3,Imp4", Names(printing.Candidates), "Unpicked numbers come round again");
        Assert.AreEqual(1, ImpCount);
    }

    [Test]
    public void NoImpEverAppearsWithoutAPick_AcrossSeveralFullCycles_IgnoredListsAreReplaced()
    {
        printing.Tick(0f);
        var seen = new List<ImpCandidate>(printing.Candidates);
        int lists = 1;
        int lastCycle = printing.Cycle;
        for (float t = 0f; t < Cycle * 3f + 1f; t += 1f)
        {
            printing.Tick(1f);
            if (printing.Cycle == lastCycle) continue;
            lastCycle = printing.Cycle;
            lists++;
            Assert.AreEqual(3, printing.Candidates.Count);
            CollectionAssert.IsEmpty(printing.Candidates.Intersect(seen), "Each boundary brings a fresh list");
            seen.AddRange(printing.Candidates);
        }

        Assert.AreEqual(4, lists, "Game start, then 300, 600 and 900 s");
        Assert.AreEqual(1, ImpCount, "Only the starter");
        Assert.AreEqual(0, printing.ImpsPrinted);
    }

    [Test]
    public void Names_ContinueFromTheHighestImpOnTheBoard()
    {
        printing.Tick(0f);
        starter.PlaceOnTile(Aside);
        Assert.AreEqual("Imp3", printing.Pick(1).name);

        printing.Tick(Cycle);
        Assert.AreEqual("Imp4,Imp5,Imp6", Names(printing.Candidates), "Past Imp3; Imp2 and Imp4 were never born");
        Assert.AreEqual(3, printing.HighestImpNumber());
    }

    // ---- fog ----

    [Test]
    public void FogOfWar_RevealsAroundEveryAddedImp_OncePerMove()
    {
        var fog = managerGo.AddComponent<FogOfWar>();
        fog.Board = board;
        fog.Imp = starter;
        fog.RevealFromImp();
        int starterOnly = fog.ExploredCount;

        var otherGo = new GameObject("Imp9");
        var other = otherGo.AddComponent<Imp>();
        other.Board = board;
        other.Renderer = boardRenderer;
        other.PlaceOnTile(new Vector2Int(10, 5)); // Far from the starter, in rock: sight still reveals
        fog.AddRevealer(other);
        fog.AddRevealer(other);
        fog.AddRevealer(starter);
        fog.AddRevealer(null);
        Assert.AreEqual(1, fog.ExtraRevealers.Count, "Added once; the wired imp and null are ignored");

        fog.RevealFromImp();
        Assert.IsTrue(fog.IsExplored(new Vector2Int(10, 5)));
        Assert.Greater(fog.ExploredCount, starterOnly);
        int afterBoth = fog.ExploredCount;
        fog.RevealFromImp();
        Assert.AreEqual(afterBoth, fog.ExploredCount, "Nothing new without a move");
    }

    // ---- Heart panel ----

    private HeartPanel MakePanel(PlacementController controller = null)
    {
        var panel = printerGo.AddComponent<HeartPanel>();
        panel.HeartPrinting = printing;
        panel.Heart = heart;
        if (controller != null) panel.PlacementController = controller;
        panel.Build();
        return panel;
    }

    private static Text Find(GameObject root, string name) =>
        root.GetComponentsInChildren<Text>(true).First(t => t.name == name);

    [Test]
    public void ThePanel_ListsTheCurrentCandidates_WithPick_AndDecline_ReadLive()
    {
        printing.Tick(0f);
        HeartPanel panel = MakePanel();
        Assert.IsFalse(panel.PanelObject.activeSelf, "Built hidden");

        panel.Open();

        Assert.IsTrue(panel.PanelObject.activeSelf);
        Assert.AreEqual(HeartPanel.TitleText, Find(panel.PanelObject, "Title").text);
        Assert.AreEqual(HeartPanel.OpenStatusText, Find(panel.PanelObject, "Status").text);
        var names = panel.PanelObject.GetComponentsInChildren<Text>(false).Where(t => t.name == "Name").Select(t => t.text);
        Assert.AreEqual("Imp2,Imp3,Imp4", string.Join(",", names));
        var stats = panel.PanelObject.GetComponentsInChildren<Text>(false).Where(t => t.name == "Stats").Select(t => t.text).ToArray();
        Assert.AreEqual(3, stats.Length);
        Assert.AreEqual("Dig 1.0×  ·  Build 1.0×  ·  Move 1.0×", stats[0]);
        for (int i = 0; i < 3; i++) Assert.IsTrue(panel.PickButton(i).gameObject.activeInHierarchy, $"Pick {i}");
        Assert.IsTrue(panel.DeclineButton.gameObject.activeInHierarchy);
        Assert.AreEqual("Next candidates in 5:00", Find(panel.PanelObject, "Next").text);
    }

    [Test]
    public void ThePanel_FollowsABoundary_WhileOpen()
    {
        printing.Tick(0f);
        starter.PlaceOnTile(Aside);
        HeartPanel panel = MakePanel();
        panel.Open();
        panel.PickButton(0).onClick.Invoke();
        Assert.IsFalse(panel.PickButton(0).gameObject.activeInHierarchy, "Picked: the rows go");

        printing.Tick(Cycle);
        panel.Repaint(); // The panel's 0.25 s refresh

        Assert.AreEqual(HeartPanel.OpenStatusText, Find(panel.PanelObject, "Status").text);
        var names = panel.PanelObject.GetComponentsInChildren<Text>(false).Where(t => t.name == "Name").Select(t => t.text);
        Assert.AreEqual("Imp3,Imp4,Imp5", string.Join(",", names), "The new list, live");
        Assert.IsTrue(panel.DeclineButton.gameObject.activeInHierarchy);
    }

    [Test]
    public void ThePanelsPickButton_BirthsThatImp_AndSaysWhoJoined()
    {
        printing.Tick(0f);
        starter.PlaceOnTile(Aside);
        printing.Tick(42f);
        HeartPanel panel = MakePanel();
        panel.Open();

        panel.PickButton(2).onClick.Invoke(); // A real Button click

        Assert.AreEqual(2, ImpCount);
        Assert.IsNotNull(GameObject.Find("Imp4"));
        Assert.AreEqual("Imp4 has joined your imps.", Find(panel.PanelObject, "Status").text);
        Assert.IsFalse(panel.DeclineButton.gameObject.activeInHierarchy, "Nothing left to decline");
        Assert.AreEqual("Next candidates in 4:18", Find(panel.PanelObject, "Next").text);
    }

    [Test]
    public void ThePanelsDeclineButton_SpawnsNothing_AndClosePutsItAway()
    {
        printing.Tick(0f);
        HeartPanel panel = MakePanel();
        panel.Open();

        panel.DeclineButton.onClick.Invoke();
        Assert.AreEqual(HeartPrinting.CycleState.Declined, printing.State);
        Assert.AreEqual(HeartPanel.DeclinedStatusText, Find(panel.PanelObject, "Status").text);
        Assert.IsFalse(panel.PickButton(0).gameObject.activeInHierarchy);
        Assert.AreEqual(1, ImpCount);

        panel.CloseButton.onClick.Invoke();
        Assert.IsFalse(panel.IsOpen);
        Assert.IsFalse(panel.PanelObject.activeSelf);
    }

    [Test]
    public void ClosingThePanel_DeclinesNothing()
    {
        printing.Tick(0f);
        HeartPanel panel = MakePanel();
        panel.Open();
        panel.Close();
        Assert.AreEqual(HeartPrinting.CycleState.Open, printing.State);
        Assert.AreEqual(3, printing.Candidates.Count);
    }

    [Test]
    public void ANeutralClickOnTheHeart_OpensThePanel_OtherTilesDont()
    {
        printing.Tick(0f);
        var controllerGo = new GameObject("TestController");
        try
        {
            var controller = controllerGo.AddComponent<PlacementController>();
            controller.PlacementManager = manager;
            HeartPanel panel = MakePanel(controller);

            controller.ClickBoardTile(new Vector2Int(22, 16));
            Assert.IsFalse(panel.IsOpen, "Another tile");
            controller.ClickBoardTile(HeartTile);
            Assert.IsTrue(panel.IsOpen, "The Heart's tile");

            panel.Close();
            controller.SelectDig();
            controller.ClickBoardTile(HeartTile);
            Assert.IsFalse(panel.IsOpen, "Outside Neutral a board click keeps its tool meaning");
        }
        finally
        {
            Object.DestroyImmediate(controllerGo);
        }
    }

    [Test]
    public void FormatTime_IsMinutesAndSeconds_RoundedUp()
    {
        Assert.AreEqual("5:00", HeartPanel.FormatTime(300f));
        Assert.AreEqual("4:59", HeartPanel.FormatTime(298.2f));
        Assert.AreEqual("0:01", HeartPanel.FormatTime(0.01f));
        Assert.AreEqual("0:00", HeartPanel.FormatTime(0f));
    }

    // ---- toast ----

    [Test]
    public void OneClickableToastPerCycle_IncludingTheGameStartList_NotRepeatedWhenIgnored()
    {
        var alerts = printerGo.AddComponent<AlertToasts>();
        alerts.HeartPrinting = printing;
        alerts.MaxVisibleToasts = 10;
        printing.Tick(0f);

        alerts.CheckAlerts(); // The baseline poll: other alerts stay quiet, the Heart's first list is news
        CollectionAssert.AreEqual(new[] { AlertToasts.ImpsAwaitMessage }, alerts.ActiveToastTexts.ToArray());
        Assert.IsTrue(alerts.IsClickable(0));

        for (int i = 0; i < 10; i++) alerts.CheckAlerts();
        alerts.Show(AlertToasts.TrapSpentMessage);
        alerts.AgeToasts(alerts.ToastVisibleSeconds + 0.5f);
        CollectionAssert.AreEqual(new[] { AlertToasts.ImpsAwaitMessage }, alerts.ActiveToastTexts.ToArray(),
            "The clickable toast outlasts a display-only one");
        Assert.AreEqual(12f, alerts.ClickableToastVisibleSeconds, "Serialized default");
        alerts.AgeToasts(alerts.ClickableToastVisibleSeconds);
        for (int i = 0; i < 10; i++) alerts.CheckAlerts();
        Assert.AreEqual(0, alerts.ActiveToastTexts.Count, "Ignored: expired, never repeated");

        printing.Tick(Cycle);
        alerts.CheckAlerts();
        CollectionAssert.AreEqual(new[] { AlertToasts.ImpsAwaitMessage }, alerts.ActiveToastTexts.ToArray(), "The next list's toast");
        alerts.CheckAlerts();
        Assert.AreEqual(1, alerts.ActiveToastTexts.Count);
    }

    [Test]
    public void ClickingTheToast_OpensTheHeartPanel_AndDismissesIt_OtherToastsArentClickable()
    {
        HeartPanel panel = MakePanel();
        var alerts = printerGo.AddComponent<AlertToasts>();
        alerts.HeartPrinting = printing;
        alerts.HeartPanel = panel;
        printing.Tick(0f);
        alerts.CheckAlerts();
        alerts.Show(AlertToasts.TrapSpentMessage);

        Assert.IsFalse(alerts.IsClickable(0), "A §A.7 toast is display-only");
        Assert.IsFalse(alerts.ClickToast(0));
        Assert.IsTrue(alerts.IsClickable(1));

        Assert.IsTrue(alerts.ClickToast(1));
        Assert.IsTrue(panel.IsOpen);
        CollectionAssert.AreEqual(new[] { AlertToasts.TrapSpentMessage }, alerts.ActiveToastTexts.ToArray());
    }
}
