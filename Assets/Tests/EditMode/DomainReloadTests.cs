using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// #137: a script recompile during Play (Recompile And Continue Playing) reloads the domain. Unity restores each
/// component's serializable fields, drops everything else, and calls OnEnable, but not Awake or Start. These tests
/// stage exactly that (DomainReload.Simulate) and check that the input consumers re-ensure their actions and that the
/// code-built chrome comes back wired: listeners present once, ModeChanged heard once. The plain disable→enable cycle
/// must stay a no-op (same actions, same chrome, no extra subscription).
/// </summary>
public class DomainReloadTests
{
    private GameObject managerGo;
    private GameObject speedGo;
    private GameObject hoverGo;
    private GameObject directorGo;
    private PlacementController controller;
    private DigDesignator dig;
    private Stockpile stockpile;
    private BuildRail rail;
    private DetailCard card;
    private StockPanel panel;
    private GameSpeed speed;
    private TileHover hover;
    private EndScreen screen;
    private float savedTimeScale;

    [SetUp]
    public void SetUp()
    {
        savedTimeScale = Time.timeScale; // GameSpeed's buttons write the global timeScale

        // Components on one object, matching DungeonManager. Awake/Start don't run in EditMode, so seams are wired here.
        managerGo = new GameObject("TestDungeonManager");
        var board = managerGo.AddComponent<DungeonBoard>();
        board.InitializeBoard();
        var boardRenderer = managerGo.AddComponent<BoardRenderer>();
        boardRenderer.Board = board;
        var manager = managerGo.AddComponent<PlacementManager>();
        manager.Board = board;
        manager.Renderer = boardRenderer;
        stockpile = managerGo.AddComponent<Stockpile>(); // 10 stone
        manager.Stockpile = stockpile;
        stockpile.DiscoverAll(); // Every chip and row present; counts unchanged

        dig = managerGo.AddComponent<DigDesignator>();
        controller = managerGo.AddComponent<PlacementController>();
        controller.DigDesignator = dig;
        controller.PlacementManager = manager;

        rail = managerGo.AddComponent<BuildRail>();
        rail.PlacementController = controller;
        rail.PlacementManager = manager;

        card = managerGo.AddComponent<DetailCard>();
        card.PlacementController = controller;
        card.PlacementManager = manager;

        panel = managerGo.AddComponent<StockPanel>();
        panel.PlacementController = controller; // The setter subscribes, as Start's call does
        panel.PlacementManager = manager;
        panel.Stockpile = stockpile;
        panel.DetailCard = card;
        panel.BuildRail = rail;
        rail.StockPanel = panel;

        speedGo = new GameObject("TestGameSpeed");
        speed = speedGo.AddComponent<GameSpeed>();
        hoverGo = new GameObject("TestTileHover");
        hover = hoverGo.AddComponent<TileHover>();
        directorGo = new GameObject("TestDirector");
        screen = directorGo.AddComponent<EndScreen>();
    }

    [TearDown]
    public void TearDown()
    {
        foreach (Component c in new Component[] { rail, panel, controller, dig, speed, hover }) DomainReload.ReleaseInput(c);
        if (panel != null) panel.DestroyChrome();
        if (card != null) card.DestroyChrome();
        if (rail != null) rail.DestroyChrome();
        foreach (string name in new[] { "GameSpeedCanvas", "EndScreenCanvas" })
        {
            GameObject go = GameObject.Find(name);
            if (go != null) Object.DestroyImmediate(go);
        }
        foreach (GameObject go in new[] { managerGo, speedGo, hoverGo, directorGo })
            if (go != null) Object.DestroyImmediate(go);
        Time.timeScale = savedTimeScale;
    }

    // ---- input: every InputConfig consumer ----

    private static readonly object[] Consumers =
    {
        new object[] { typeof(BuildRail), InputConfig.Rail1 },
        new object[] { typeof(StockPanel), InputConfig.Abort },
        new object[] { typeof(GameSpeed), InputConfig.TogglePause },
        new object[] { typeof(PlacementController), InputConfig.Abort },
        new object[] { typeof(DigDesignator), InputConfig.Designate },
        new object[] { typeof(TileHover), InputConfig.PointerPosition },
    };

    private Component Consumer(Type type)
    {
        if (type == typeof(BuildRail)) return rail;
        if (type == typeof(StockPanel)) return panel;
        if (type == typeof(GameSpeed)) return speed;
        if (type == typeof(PlacementController)) return controller;
        if (type == typeof(DigDesignator)) return dig;
        return hover;
    }

    [TestCaseSource(nameof(Consumers))]
    public void Input_PlainReEnable_KeepsTheSameActions(Type type, string action)
    {
        Component c = Consumer(type);
        DomainReload.Invoke(c, "Awake");
        DomainReload.Invoke(c, "OnEnable");
        InputConfig.Actions first = DomainReload.Input(c);
        Assert.IsNotNull(first, "Awake creates the actions");
        Assert.IsTrue(first.Map.enabled, "OnEnable enables them");

        DomainReload.Invoke(c, "OnDisable");
        Assert.IsFalse(first.Map.enabled, "Disabled with the component");

        DomainReload.Invoke(c, "OnEnable");
        Assert.AreSame(first, DomainReload.Input(c), "Re-enabling ensures; it doesn't recreate");
        Assert.IsTrue(first.Map.enabled);
    }

    [TestCaseSource(nameof(Consumers))]
    public void Input_AfterReload_ActionsAreBackAndEnabled(Type type, string action)
    {
        Component c = Consumer(type);
        DomainReload.Invoke(c, "Awake");
        DomainReload.Invoke(c, "OnEnable");

        DomainReload.Simulate(c);

        InputConfig.Actions actions = DomainReload.Input(c);
        Assert.IsNotNull(actions, "OnEnable rebuilt what only Awake built");
        Assert.IsTrue(actions.Map.enabled);
        Assert.IsNotNull(actions.Map.FindAction(action), action);
        Assert.IsTrue(actions.Map.FindAction(action).enabled, action + " is live");
    }

    // ---- chrome: listeners and subscriptions come back exactly once ----

    [Test]
    public void Rail_AfterReload_RebuiltOnce_EntriesClick_ModeHeardOnce()
    {
        rail.Build();
        DomainReload.Invoke(rail, "SubscribeToMode"); // Start's subscription
        Canvas before = DomainReload.Field<Canvas>(rail, "canvas");

        DomainReload.Simulate(controller, rail);

        Canvas after = DomainReload.Field<Canvas>(rail, "canvas");
        Assert.IsNotNull(after);
        Assert.AreNotSame(before, after, "Rebuilt");
        Assert.IsTrue(before == null, "The old chrome is gone (Unity null)");
        Assert.AreEqual(1, Object.FindObjectsByType<BuildRail>(FindObjectsSortMode.None).Length);
        Assert.AreEqual(1, CanvasesNamed("BuildRailCanvas"), "No orphaned copy");
        Assert.AreEqual(1, DomainReload.Subscriptions(controller, rail), "ModeChanged heard once");

        Button living = after.transform.Find("Rail/LivingEntry").GetComponent<Button>();
        Assert.AreEqual(1, DomainReload.RuntimeListeners(living.onClick));
        living.onClick.Invoke();
        Assert.AreEqual(BuildCategory.Living, rail.OpenCategory, "The entry is live again");
    }

    [Test]
    public void Rail_PlainReEnable_KeepsChrome_NoExtraSubscription()
    {
        rail.Build();
        DomainReload.Invoke(rail, "SubscribeToMode");
        Canvas before = DomainReload.Field<Canvas>(rail, "canvas");

        DomainReload.Invoke(rail, "OnDisable");
        DomainReload.Invoke(rail, "OnEnable");
        DomainReload.Invoke(rail, "OnEnable");

        Assert.AreSame(before, DomainReload.Field<Canvas>(rail, "canvas"), "Not rebuilt");
        Assert.AreEqual(1, DomainReload.Subscriptions(controller, rail));
        Assert.AreEqual(1, DomainReload.RuntimeListeners(before.transform.Find("Rail/LivingEntry").GetComponent<Button>().onClick));
    }

    [Test]
    public void StockPanel_AfterReload_RowsAndFilterLive_ClosesOnModeChange()
    {
        panel.Build();
        DomainReload.Simulate(controller, panel);

        Assert.AreEqual(1, DomainReload.Subscriptions(controller, panel), "ModeChanged heard once");
        Assert.AreEqual(1, CanvasesNamed("StockPanelCanvas"));

        panel.Open();
        Transform stockPanelCanvas = DomainReload.Field<Canvas>(panel, "canvas").transform;
        Button row = stockPanelCanvas.Find("Panel/List/Content/GlimmerstoneRow").GetComponent<Button>();
        Assert.AreEqual(1, DomainReload.RuntimeListeners(row.onClick));
        row.onClick.Invoke();
        Assert.AreEqual(MaterialType.Glimmerstone, panel.SelectedMaterial, "Rows are live again");

        stockPanelCanvas.Find("Panel/Filter").GetComponent<InputField>().text = "deep";
        Assert.AreEqual("deep", panel.Filter, "The filter field is live again");

        controller.SelectDig();
        Assert.IsFalse(panel.IsOpen, "The panel hears the mode change and closes");
    }

    [Test]
    public void DetailCard_AfterReload_FollowsTheMode_ChipsLive()
    {
        card.Build();
        DomainReload.Invoke(card, "SubscribeToMode");

        DomainReload.Simulate(controller, card);

        Assert.AreEqual(1, DomainReload.Subscriptions(controller, card), "ModeChanged heard once");
        Assert.AreEqual(1, CanvasesNamed("DetailCardCanvas"));

        controller.Select(PlaceableType.LairCot); // Only the ModeChanged repaint builds the chips
        Transform chips = DomainReload.Field<Canvas>(card, "canvas").transform.Find("Card/Chips");
        Transform glimmer = chips.Find("GlimmerstoneChip");
        Assert.IsNotNull(glimmer, "The card repainted on the mode change");
        Assert.AreEqual(1, DomainReload.RuntimeListeners(glimmer.GetComponent<Button>().onClick));
        glimmer.GetComponent<Button>().onClick.Invoke();
        Assert.AreEqual(MaterialType.Glimmerstone, controller.SelectedMaterial, "Chips are live again");
    }

    [Test]
    public void DetailCard_PlainReEnable_KeepsChrome_NoExtraSubscription()
    {
        card.Build();
        DomainReload.Invoke(card, "SubscribeToMode");
        Canvas before = DomainReload.Field<Canvas>(card, "canvas");

        DomainReload.Invoke(card, "OnEnable");

        Assert.AreSame(before, DomainReload.Field<Canvas>(card, "canvas"));
        Assert.AreEqual(1, DomainReload.Subscriptions(controller, card));
    }

    [Test]
    public void GameSpeed_AfterReload_ButtonsWiredOnce()
    {
        speed.CreateButtons();
        DomainReload.Simulate(speed);
        DomainReload.Invoke(speed, "OnEnable"); // A plain re-enable afterwards adds nothing

        Button fast = GameObject.Find("GameSpeedCanvas").transform.Find("SpeedControls/2×Button").GetComponent<Button>();
        Assert.AreEqual(1, DomainReload.RuntimeListeners(fast.onClick));
        fast.onClick.Invoke();
        Assert.AreEqual(2f, speed.Speed, "The button is live again");
        Assert.AreEqual(2f, speed.HighlightedSpeed);
    }

    [Test]
    public void EndScreen_AfterReload_PlayAgainWiredOnce()
    {
        DomainReload.Invoke(screen, "CreateOverlay");
        Button playAgain = GameObject.Find("EndScreenCanvas").transform.Find("EndScreen/PlayAgainButton").GetComponent<Button>();
        Assert.AreEqual(1, DomainReload.RuntimeListeners(playAgain.onClick), "Wired at build");

        DomainReload.Simulate(screen);
        Assert.AreEqual(1, DomainReload.RuntimeListeners(playAgain.onClick), "Rewired after the reload");

        DomainReload.Invoke(screen, "OnEnable");
        Assert.AreEqual(1, DomainReload.RuntimeListeners(playAgain.onClick), "Never twice");
    }

    private static int CanvasesNamed(string name)
    {
        int n = 0;
        foreach (Canvas c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            if (c.name == name) n++;
        return n;
    }
}

/// <summary>
/// Test staging for a domain reload (#137), by reflection. What Unity does on a reload mid-Play: fields it can't
/// serialize come back default ([NonSerialized], delegates such as event backing fields, the InputConfig actions);
/// listeners added in code are gone; then each component gets OnEnable, never Awake or Start.
/// </summary>
internal static class DomainReload
{
    private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static void Simulate(params Component[] components)
    {
        foreach (Component c in components)
        {
            foreach (FieldInfo f in c.GetType().GetFields(AnyInstance))
            {
                bool lost = f.IsNotSerialized || typeof(Delegate).IsAssignableFrom(f.FieldType) || f.FieldType == typeof(InputConfig.Actions);
                if (!lost || f.IsInitOnly) continue;
                (f.GetValue(c) as InputConfig.Actions)?.Dispose(); // The old domain's actions die with it
                f.SetValue(c, f.FieldType.IsValueType ? Activator.CreateInstance(f.FieldType) : null);
            }
        }

        foreach (Button b in Object.FindObjectsByType<Button>(FindObjectsSortMode.None)) b.onClick.RemoveAllListeners();
        foreach (InputField i in Object.FindObjectsByType<InputField>(FindObjectsSortMode.None)) i.onValueChanged.RemoveAllListeners();

        foreach (Component c in components) Invoke(c, "OnEnable");
    }

    public static void Invoke(Component c, string method) => c.GetType().GetMethod(method, AnyInstance)?.Invoke(c, null);

    public static T Field<T>(Component c, string name) where T : class => c.GetType().GetField(name, AnyInstance)?.GetValue(c) as T;

    public static InputConfig.Actions Input(Component c) => Field<InputConfig.Actions>(c, "input");

    /// <summary>Disables and disposes a consumer's actions: OnDisable/OnDestroy don't run for EditMode-added components.</summary>
    public static void ReleaseInput(Component c)
    {
        if (c == null) return;
        InputConfig.Actions actions = Input(c);
        if (actions == null) return;
        actions.Disable();
        actions.Dispose();
        c.GetType().GetField("input", AnyInstance).SetValue(c, null);
    }

    /// <summary>How many of the controller's ModeChanged handlers belong to the subscriber.</summary>
    public static int Subscriptions(PlacementController controller, object subscriber)
    {
        var handlers = typeof(PlacementController).GetField("ModeChanged", AnyInstance).GetValue(controller) as Delegate;
        if (handlers == null) return 0;
        int n = 0;
        foreach (Delegate d in handlers.GetInvocationList())
            if (ReferenceEquals(d.Target, subscriber)) n++;
        return n;
    }

    /// <summary>Listeners added in code (UnityEventBase keeps them apart from the persistent, serialized ones).</summary>
    public static int RuntimeListeners(UnityEngine.Events.UnityEventBase e)
    {
        object calls = typeof(UnityEngine.Events.UnityEventBase).GetField("m_Calls", AnyInstance).GetValue(e);
        var runtime = calls.GetType().GetField("m_RuntimeCalls", AnyInstance).GetValue(calls) as IList;
        return runtime?.Count ?? 0;
    }
}
