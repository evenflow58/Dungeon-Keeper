using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// #145: the mana chrome. STOCK's mana rows (discovery-gated, global sums, filtered, read-only), the TopBar ✦ segment
/// (total, text, the Magic view switch, exclusive with Power), the Magic overlay's color composition (Normal untouched, fog
/// wins), the board's mana / pipe / vent views (explored only), tooltip lines, the two buildings' cards and stat lines,
/// the Lay pipe verb through the controller, rail and card, the see-through rule, the M binding and the shipped data.
/// </summary>
public class ManaChromeTests
{
    private static readonly Vector2Int ImpTile = new Vector2Int(24, 16);
    private const string Fire = ManaFlow.FireId;
    private const string Water = ManaFlow.WaterId;
    private const string TestKey = "DungeonKeeper.BindingOverrides.ManaChromeTest";

    private GameObject managerGo;
    private DungeonBoard board;
    private BoardRenderer boardRenderer;
    private PlacementManager manager;
    private ManaFlow mana;
    private FogOfWar fog;
    private GameObject barGo;
    private TopBar bar;
    private string savedKey;

    private sealed class FakePort : IManaPort
    {
        public Vector2Int Tile { get; set; }
        public ManaPortRole Role { get; set; } = ManaPortRole.InputOutput;
        public ManaStore Store { get; set; } = new ManaStore(100);
    }

    [SetUp]
    public void SetUp()
    {
        savedKey = InputConfig.PrefsKey;
        InputConfig.PrefsKey = TestKey;
        PlayerPrefs.DeleteKey(TestKey);

        managerGo = new GameObject("TestDungeonManager");
        board = managerGo.AddComponent<DungeonBoard>();
        board.InitializeBoard();
        boardRenderer = managerGo.AddComponent<BoardRenderer>();
        boardRenderer.Board = board;
        mana = managerGo.AddComponent<ManaFlow>();
        mana.Board = board;
        mana.HasVent = false;
        manager = managerGo.AddComponent<PlacementManager>();
        manager.Board = board;
        manager.Renderer = boardRenderer;
        manager.ManaFlow = mana;
        fog = managerGo.AddComponent<FogOfWar>();
        fog.Board = board;
        fog.RevealAround(new Vector2Int(23, 15)); // (20..26, 12..18): the cavern
        boardRenderer.Fog = fog;
        boardRenderer.Mana = mana;
    }

    [TearDown]
    public void TearDown()
    {
        if (bar != null && bar.PowerSegment != null) Object.DestroyImmediate(bar.PowerSegment.transform.root.gameObject);
        if (barGo != null) Object.DestroyImmediate(barGo);
        if (managerGo != null) Object.DestroyImmediate(managerGo);
        PlayerPrefs.DeleteKey(TestKey);
        InputConfig.PrefsKey = savedKey;
    }

    private static Vector2Int T(int x, int y) => new Vector2Int(x, y);

    private FakePort HeldPort(string type, int amount, Vector2Int tile)
    {
        var port = new FakePort { Tile = tile };
        port.Store.Deposit(type, amount);
        mana.Register(port);
        mana.RefreshDiscovery();
        return port;
    }

    private TopBar MakeBar()
    {
        barGo = new GameObject("TestTopBar");
        bar = barGo.AddComponent<TopBar>();
        bar.ManaFlow = mana;
        bar.BoardRenderer = boardRenderer;
        return bar;
    }

    private void BuildPipe(params Vector2Int[] tiles)
    {
        foreach (Vector2Int tile in tiles)
        {
            Assert.AreEqual(1, mana.ApplyPipeRect(null, new RectInt(tile.x, tile.y, 1, 1), ManaFlow.PipeMode.Lay));
            Assert.IsTrue(mana.CompletePipe(tile));
        }
    }

    // ---- STOCK ----

    [Test]
    public void StockRows_ManaAppearsOnlyOnceDiscovered_WithItsGlobalSum_UnderTheManaHeader_AndFilters()
    {
        var stockpile = managerGo.AddComponent<Stockpile>();
        List<StockPanel.Row> before = StockPanel.Rows(stockpile, mana, "");
        foreach (StockPanel.Row row in before) Assert.IsFalse(row.IsMana, "Nothing held yet: no mana anywhere");

        HeldPort(Fire, 7, T(1, 1));
        HeldPort(Fire, 5, T(2, 1));
        List<StockPanel.Row> rows = StockPanel.Rows(stockpile, mana, "");
        int header = rows.FindIndex(r => r.isHeader && r.family == MaterialFamily.Mana);
        Assert.GreaterOrEqual(header, 0, "A Mana header");
        Assert.AreEqual(rows.Count - 2, header, "After the materials");
        StockPanel.Row fire = rows[header + 1];
        Assert.IsTrue(fire.IsMana);
        Assert.AreEqual(Fire, fire.manaType);
        Assert.AreEqual("Fire Mana", fire.name);
        Assert.AreEqual(12, fire.count, "The global sum across every store");
        Assert.AreEqual(mana.ColorOf(Fire), fire.color);
        Assert.IsFalse(rows.Exists(r => r.manaType == Water), "Water was never held: no row");

        Assert.AreEqual(2, StockPanel.Rows(stockpile, mana, "fire").Count, "Header + row, case-insensitive");
        Assert.IsFalse(StockPanel.Rows(stockpile, mana, "stone").Exists(r => r.IsMana), "Filtered out");
        Assert.AreEqual(StockPanel.Rows(stockpile, "").Count, StockPanel.Rows(stockpile, null, "").Count, "No service: materials only");
    }

    [Test]
    public void StockPanel_ManaRowsAreReadOnly()
    {
        var stockpile = managerGo.AddComponent<Stockpile>();
        var panel = managerGo.AddComponent<StockPanel>();
        panel.Stockpile = stockpile;
        panel.Mana = mana;
        HeldPort(Fire, 3, T(1, 1));
        try
        {
            panel.Build();
            panel.Open();
            GameObject row = GameObject.Find("FireManaRow");
            Assert.IsNotNull(row, "The row is built");
            Assert.IsNull(row.GetComponent<Button>(), "Mana isn't a build material: the row selects nothing");
            Assert.AreEqual("3", row.transform.Find("Count").GetComponent<Text>().text);
            Assert.IsTrue(row.transform.Find("Swatch").GetComponent<Image>().enabled, "Its color chip shows");
        }
        finally
        {
            panel.DestroyChrome();
        }
    }

    // ---- TopBar ----

    [Test]
    public void TopBar_ManaSegment_ShowsTheTotal_AndTogglesTheMagicView_ExclusiveWithPower()
    {
        Assert.AreEqual("✦ 0", TopBar.MagicReadout(0));
        Assert.AreEqual("✦ 120", TopBar.MagicReadout(120));
        TopBar topBar = MakeBar();
        topBar.CreateBar();
        topBar.Refresh();
        Assert.AreEqual("✦ 0", topBar.MagicText, "No stores: 0");

        HeldPort(Fire, 9, T(1, 1));
        HeldPort(Water, 4, T(2, 1));
        topBar.Refresh();
        Assert.AreEqual("✦ 13", topBar.MagicText, "Every type across every store");
        Assert.AreEqual("✦ 13", topBar.MagicSegment.GetComponentInChildren<Text>().text);

        Image magicTint = topBar.MagicSegment.GetComponent<Image>();
        Image powerTint = topBar.PowerSegment.GetComponent<Image>();
        Color idle = magicTint.color;
        topBar.MagicSegment.onClick.Invoke();
        Assert.AreEqual(ViewMode.Magic, boardRenderer.Mode);
        Assert.AreNotEqual(idle, magicTint.color, "Tinted while on");

        topBar.TogglePowerOverlay(); // Switching views replaces, never stacks
        Assert.AreEqual(ViewMode.Power, boardRenderer.Mode);
        Assert.AreEqual(idle, magicTint.color, "Magic's tint goes with it");
        Assert.AreNotEqual(idle, powerTint.color);

        topBar.ToggleMagicOverlay();
        Assert.AreEqual(ViewMode.Magic, boardRenderer.Mode);
        topBar.ToggleMagicOverlay();
        Assert.AreEqual(ViewMode.Normal, boardRenderer.Mode, "The same switch turns it off");

        topBar.ManaFlow = null;
        topBar.Refresh();
        Assert.AreEqual("✦ 0", topBar.MagicText, "Unwired: 0");
    }

    // ---- the Magic overlay ----

    [Test]
    public void MagicOverlay_ManaTypeStorePipeAndVentColors_NormalUntouched_FogWins()
    {
        mana.HasVent = true;
        mana.VentTile = T(22, 13); // In the cavern's edge: explored, with free neighbors
        Assert.IsTrue(mana.TryAddUnit(T(24, 15), Fire));
        FakePort tank = HeldPort(Water, 3, T(25, 16));
        var emptyTank = new FakePort { Tile = T(21, 16) };
        mana.Register(emptyTank);
        BuildPipe(T(22, 17), T(23, 17));
        board.SetTile(30, 15, TileState.Floor);
        mana.FlowTickSeconds = 1000f;
        Assert.IsTrue(mana.TryAddUnit(T(30, 15), Fire), "Mana under fog");

        Color floorNormal = boardRenderer.DisplayColorAt(26, 18);
        Color fogged = boardRenderer.DisplayColorAt(30, 15);
        var normal = new Dictionary<Vector2Int, Color>();
        foreach (Vector2Int t in new[] { T(24, 15), T(25, 16), T(22, 17), T(22, 13), T(30, 15) }) normal[t] = boardRenderer.DisplayColorAt(t.x, t.y);

        boardRenderer.SetViewMode(ViewMode.Magic);
        Color dimmed = PowerOverlay.Dim(floorNormal, boardRenderer.OverlayDimFactor);
        Assert.AreEqual(dimmed, boardRenderer.DisplayColorAt(26, 18), "Nothing magic: dimmed");
        Assert.AreEqual(Color.Lerp(dimmed, mana.ColorOf(Fire), boardRenderer.ManaTileTint), boardRenderer.DisplayColorAt(24, 15),
            "A mana tile leans to its type's color");
        Assert.AreEqual(mana.ColorOf(Water), boardRenderer.DisplayColorAt(25, 16), "A store reads its type's color");
        Assert.AreEqual(boardRenderer.StoreOverlayColor(emptyTank.Store), boardRenderer.DisplayColorAt(21, 16));
        Assert.AreNotEqual(mana.ColorOf(Water), boardRenderer.DisplayColorAt(21, 16), "An empty store reads neutral");
        Assert.AreNotEqual(dimmed, boardRenderer.DisplayColorAt(22, 17), "Built pipe tints the floor under it");
        Assert.AreEqual(boardRenderer.VentActiveOverlayColor, boardRenderer.DisplayColorAt(22, 13), "The vent, emitting");
        Assert.AreEqual(fogged, boardRenderer.DisplayColorAt(30, 15), "Fog wins: no mana peeks through");

        tank.Store.Withdraw(3);
        Assert.AreEqual(boardRenderer.StoreOverlayColor(tank.Store), boardRenderer.DisplayColorAt(25, 16), "Emptied: neutral");

        boardRenderer.SetViewMode(ViewMode.Normal);
        foreach (KeyValuePair<Vector2Int, Color> entry in normal)
            Assert.AreEqual(entry.Value, boardRenderer.DisplayColorAt(entry.Key.x, entry.Key.y), $"{entry.Key}: Normal untouched");
    }

    [Test]
    public void MagicOverlay_DormantVentReadsDull()
    {
        mana.HasVent = true;
        mana.VentTile = T(20, 15); // In the cavern's west wall: its only open neighbor is (21,15)
        board.SetTile(20, 15, TileState.Floor);
        Assert.IsTrue(mana.TryAddUnit(T(21, 15), Water), "A unit of another type blocks the only free neighbor");
        Assert.IsTrue(mana.VentDormant);
        boardRenderer.SetViewMode(ViewMode.Magic);
        Assert.AreEqual(boardRenderer.VentDormantOverlayColor, boardRenderer.DisplayColorAt(20, 15));
    }

    [Test]
    public void BoardViews_ManaGlowsPipesAndTheVent_ExploredOnly()
    {
        mana.HasVent = true;
        mana.VentTile = T(24, 11); // Sealed below the cavern: unexplored (the reveal reaches y 12)
        mana.CarveVent();
        Assert.IsTrue(mana.TryAddUnit(T(23, 15), Fire));
        board.SetTile(30, 15, TileState.Floor);
        Assert.IsTrue(mana.TryAddUnit(T(30, 15), Fire));
        BuildPipe(T(22, 14), T(23, 14));
        mana.ApplyPipeRect(null, new RectInt(24, 14, 1, 1), ManaFlow.PipeMode.Lay);
        boardRenderer.RenderFullBoard();

        Assert.IsNotNull(boardRenderer.GetManaView(23, 15), "An explored unit glows");
        Assert.IsNull(boardRenderer.GetManaView(30, 15), "Under fog: not drawn");
        Assert.AreEqual("Pipe", boardRenderer.GetPipeView(22, 14)?.name);
        Assert.AreEqual("PipePending", boardRenderer.GetPipeView(24, 14)?.name);
        Assert.IsNotNull(boardRenderer.GetPipeView(22, 14).transform.Find("Arm10"), "Joined toward its neighbor");
        Assert.IsNull(boardRenderer.VentView, "The vent is under fog");

        fog.RevealAround(T(24, 9));
        boardRenderer.RefreshVent();
        Assert.IsNotNull(boardRenderer.VentView, "Seen: the vent shows");

        Assert.IsTrue(mana.TryTakeUnit(T(23, 15), out _));
        boardRenderer.RefreshMana(T(23, 15));
        Assert.IsNull(boardRenderer.GetManaView(23, 15), "Taken: its glow goes");

        boardRenderer.SetViewMode(ViewMode.Magic);
        Assert.AreSame(boardRenderer.MaterialFor(boardRenderer.PipeOverlayColor),
            boardRenderer.GetPipeView(22, 14).GetComponentInChildren<MeshRenderer>().sharedMaterial, "Pipes take the network tint");
    }

    [Test]
    public void SeesThrough_InMagic_EverythingButManaStores()
    {
        Assert.IsTrue(PowerOverlay.SeesThrough(ViewMode.Magic, false, false), "A cot in the Magic view");
        Assert.IsTrue(PowerOverlay.SeesThrough(ViewMode.Magic, true, false), "A power node too");
        Assert.IsFalse(PowerOverlay.SeesThrough(ViewMode.Magic, false, true), "Stores stay solid");
        Assert.IsFalse(PowerOverlay.SeesThrough(ViewMode.Power, true, false), "Power: nodes solid, as before");
        Assert.IsFalse(PowerOverlay.SeesThrough(ViewMode.Normal, false, false));

        Assert.IsTrue(manager.TryPlaceBuilt(PlaceableType.LairCot, T(22, 15), ImpTile, out Placeable cot));
        Assert.IsTrue(manager.TryPlaceBuilt(PlaceableType.ManaContainer, T(24, 15), ImpTile, out Placeable tank));
        boardRenderer.SetViewMode(ViewMode.Magic);
        manager.RefreshSeeThrough();
        Assert.IsTrue(cot.IsSeeThrough);
        Assert.IsFalse(tank.IsSeeThrough);
    }

    // ---- tooltips ----

    [Test]
    public void TooltipLines_VentManaPipe_AndTheirComposition()
    {
        Assert.AreEqual("Fire Vent · Emitting", TileHover.VentLine(Fire, false));
        Assert.AreEqual("Fire Vent · Dormant", TileHover.VentLine(Fire, true));
        Assert.AreEqual("Fire mana", TileHover.ManaLine(Fire, 1));
        Assert.AreEqual("Fire mana ×2", TileHover.ManaLine(Fire, 2));
        Assert.IsNull(TileHover.ManaLine(null, 0));
        Assert.AreEqual("Pipe", TileHover.PipeLine(true, true, false));
        Assert.AreEqual("Pipe (planned)", TileHover.PipeLine(true, false, false));
        Assert.IsNull(TileHover.PipeLine(true, true, true), "A store's tile: the building is what the tile is");
        Assert.IsNull(TileHover.PipeLine(false, false, false));

        var identity = new TileIdentity(TileState.Floor, null);
        Assert.AreEqual("Floor (23, 15)\nFire mana\nPipe", TileHover.TooltipLabel(fog, identity, T(23, 15), null, null, null, "Fire mana", "Pipe"),
            "Each line beneath the label, nulls skipped");
        Assert.IsNull(TileHover.TooltipLabel(fog, identity, T(30, 15), null, "Fire mana"), "Under fog: nothing");

        mana.HasVent = true;
        mana.VentTile = T(22, 13);
        Assert.IsTrue(mana.TryAddUnit(T(23, 15), Fire));
        BuildPipe(T(23, 15));
        Assert.AreEqual("Fire Vent · Emitting", TileHover.VentLine(mana, T(22, 13)));
        Assert.IsNull(TileHover.VentLine(mana, T(23, 15)));
        Assert.AreEqual("Fire mana", TileHover.ManaLine(mana, T(23, 15)));
        Assert.AreEqual("Pipe", TileHover.PipeLine(mana, T(23, 15)));
        Assert.IsNull(TileHover.PipeLine((ManaFlow)null, T(23, 15)));
    }

    // ---- cards ----

    [Test]
    public void CardLines_AccumulatorAndContainer_InEveryState()
    {
        CollectionAssert.AreEqual(new[] { "Buffer: Fire 12/20", "Collecting", "Collected 4 in the last 10 s",
                "On a pipe · keeps 5, the rest flows to containers" },
            DetailCard.AccumulatorLines(Fire, 12, 20, true, 4, 10f, true, 5));
        CollectionAssert.AreEqual(new[] { "Buffer: empty (0/20)", "Waiting: no mana it can take beside it", "Collected 0 in the last 10 s",
                "Not on a pipe · keeps what it collects" },
            DetailCard.AccumulatorLines(null, 0, 20, false, 0, 10f, false, 5), "A starved accumulator explains itself");
        Assert.AreEqual("Full · stopped collecting", DetailCard.AccumulatorLines(Fire, 20, 20, true, 3, 10f, false, 5)[1]);

        CollectionAssert.AreEqual(new[] { "Fire 40/100", "On a pipe" }, DetailCard.ContainerLines(Fire, 40, 100, true));
        CollectionAssert.AreEqual(new[] { "Empty — adopts the first type piped in (0/100)", "Not on a pipe · lay pipe onto its tile to connect it" },
            DetailCard.ContainerLines(null, 0, 100, false));
    }

    [Test]
    public void PlacedLines_ReadTheLiveBuildings_AndStatLinesReadTheManager()
    {
        mana.FlowTickSeconds = 1000f;
        Assert.IsTrue(manager.TryPlaceBuilt(PlaceableType.Accumulator, T(22, 15), ImpTile, out Placeable accP));
        Assert.IsTrue(manager.TryPlaceBuilt(PlaceableType.ManaContainer, T(25, 15), ImpTile, out Placeable tankP));
        Assert.IsTrue(mana.TryAddUnit(T(23, 15), Fire));
        for (int i = 0; i < 60; i++) mana.Tick(0.02f);

        List<string> acc = DetailCard.PlacedLines(accP, manager);
        Assert.AreEqual("Buffer: Fire 1/20", acc[0]);
        Assert.AreEqual("Waiting: no mana it can take beside it", acc[1]);
        Assert.AreEqual("Collected 1 in the last 10 s", acc[2]);
        Assert.AreEqual("Not on a pipe · keeps what it collects", acc[3]);
        CollectionAssert.AreEqual(new[] { "Empty — adopts the first type piped in (0/100)", "Not on a pipe · lay pipe onto its tile to connect it" },
            DetailCard.PlacedLines(tankP, manager));

        Assert.AreEqual("Holds up to 20 mana", DetailCard.StatLine(BuildStat.ManaCapacity, manager, PlaceableType.Accumulator, MaterialType.Stone));
        Assert.AreEqual("Holds up to 100 mana of one type", DetailCard.StatLine(BuildStat.ManaCapacity, manager, PlaceableType.ManaContainer, MaterialType.Stone));
        Assert.AreEqual("Collects 1 mana per 1 s from beside it", DetailCard.StatLine(BuildStat.ManaCollection, manager, PlaceableType.Accumulator, MaterialType.Stone));
        CollectionAssert.AreEqual(new[] { "Imp time: 0.5s per tile", "Carries up to 5 mana/s per network", "No material cost · Removing is instant, no refund" },
            DetailCard.PipeLines(mana));
        CollectionAssert.IsEmpty(DetailCard.PipeLines(null));
    }

    // ---- the verb through the controller, rail and card ----

    [Test]
    public void LayPipe_ArmsPipeMode_ExclusiveWithTheOtherTools_AndTheCardDescribesIt()
    {
        var controller = managerGo.AddComponent<PlacementController>();
        controller.PlacementManager = manager;
        var pipes = managerGo.AddComponent<PipeDesignator>();
        var conduits = managerGo.AddComponent<ConduitDesignator>();
        controller.PipeDesignator = pipes;
        controller.ConduitDesignator = conduits;
        var rail = managerGo.AddComponent<BuildRail>();
        rail.PlacementController = controller;
        var card = managerGo.AddComponent<DetailCard>();
        card.PlacementController = controller;
        card.PlacementManager = manager;

        rail.SelectVerb(BuildVerb.LayPipe);
        Assert.AreEqual(PlacementController.ToolMode.Pipe, controller.Mode);
        Assert.IsTrue(controller.IsPiping);
        Assert.IsTrue(rail.IsVerbArmed(BuildVerb.LayPipe));
        Assert.IsFalse(rail.IsVerbArmed(BuildVerb.LayConduit));
        Assert.IsTrue(pipes.enabled, "PipeDesignator enabled exactly in Pipe mode");
        Assert.IsFalse(conduits.enabled);
        Assert.AreEqual("Lay pipe", card.Title);
        Assert.AreEqual("Magic", card.CategoryTag);
        Assert.AreEqual(DetailCard.PipeHintText, card.Hint);
        Assert.AreEqual("Imp time: 0.5s per tile", card.StatLines()[0]);

        controller.SelectWire(); // Another tool replaces it
        Assert.AreEqual(PlacementController.ToolMode.Wire, controller.Mode);
        Assert.IsFalse(controller.IsPiping);
        Assert.IsFalse(pipes.enabled);

        controller.SelectPipe();
        controller.Select(PlaceableType.Accumulator);
        Assert.AreEqual(PlacementController.ToolMode.Armed, controller.Mode);
        Assert.IsFalse(controller.IsPiping);

        controller.SelectPipe();
        controller.SelectPipe(); // Again: disarms
        Assert.AreEqual(PlacementController.ToolMode.Neutral, controller.Mode);
        Assert.IsTrue(controller.TryExitPipeOnAbort() == false, "Not in Pipe: the press isn't ours");
        card.DestroyChrome();
        rail.DestroyChrome();
    }

    // ---- data and bindings ----

    [Test]
    public void ShippedData_MagicCategory_Costs_BuildTimes_Params_Verb()
    {
        CollectionAssert.AreEqual(new[] { PlaceableType.Accumulator, PlaceableType.ManaContainer },
            BuildPresentation.TypesInCategory(BuildCategory.Magic));
        CollectionAssert.AreEqual(new[] { BuildVerb.LayPipe }, BuildPresentation.VerbsInCategory(BuildCategory.Magic));
        Assert.AreEqual("Accumulator", BuildPresentation.DisplayName(PlaceableType.Accumulator));
        Assert.AreEqual("Mana Container", BuildPresentation.DisplayName(PlaceableType.ManaContainer));
        Assert.IsTrue(BuildPresentation.TryGetVerb(BuildVerb.LayPipe, out BuildPresentation.VerbEntry verb));
        Assert.AreEqual("Lay pipe", verb.displayName);
        Assert.AreEqual("Imp time only", verb.costLine);

        Assert.AreEqual(24, manager.Costs.Count);
        foreach (MaterialType material in new[] { MaterialType.Stone, MaterialType.Glimmerstone, MaterialType.Deepstone })
        {
            Assert.IsTrue(manager.TryGetCost(PlaceableType.Accumulator, material, out MaterialAmount a));
            Assert.AreEqual(5, a.amount);
            Assert.IsTrue(manager.TryGetCost(PlaceableType.ManaContainer, material, out MaterialAmount c));
            Assert.AreEqual(6, c.amount);
        }
        Assert.AreEqual(6f, manager.BuildSecondsFor(PlaceableType.Accumulator));
        Assert.AreEqual(6f, manager.BuildSecondsFor(PlaceableType.ManaContainer));
        Assert.AreEqual(100, manager.ManaContainerCapacity);
        Assert.IsFalse(manager.TryGetPower(PlaceableType.Accumulator, out _), "No power behavior in this story");

        Assert.AreEqual(1, mana.MaxManaPerTile);
        Assert.AreEqual(new Vector2Int(24, 5), new ManaFlowDefaults().VentTile, "Deep in the rock south of the cavern");
        Assert.AreEqual(5f, mana.PipeThroughputPerSecond);
        Assert.AreEqual(0.25f, mana.TransferSeconds);
        Assert.AreEqual(5, mana.AccumulatorReserve);
    }

    // A fresh service's shipped defaults (the test fixture's own one has its vent turned off).
    private sealed class ManaFlowDefaults
    {
        public readonly Vector2Int VentTile;

        public ManaFlowDefaults()
        {
            var go = new GameObject("FreshMana");
            try { VentTile = go.AddComponent<ManaFlow>().VentTile; }
            finally { Object.DestroyImmediate(go); }
        }
    }

    [Test]
    public void ToggleMagicView_DefaultsToM_WithAStableId_AndRebinds()
    {
        Assert.AreEqual("ToggleMagicView", InputConfig.ToggleMagicView);
        using (var a = InputConfig.CreateActions())
        using (var b = InputConfig.CreateActions())
        {
            Assert.AreEqual(UnityEngine.InputSystem.InputActionType.Button, a.ToggleMagicView.type);
            CollectionAssert.AreEqual(new[] { "<Keyboard>/m" }, InputConfig.EffectivePaths(a.ToggleMagicView));
            Assert.AreEqual(a.ToggleMagicView.bindings[0].id, b.ToggleMagicView.bindings[0].id, "Same id in both builds");
            foreach (UnityEngine.InputSystem.InputAction other in a.Map.actions)
            {
                if (other == a.ToggleMagicView) continue;
                CollectionAssert.DoesNotContain(InputConfig.EffectivePaths(other), "<Keyboard>/m", $"{other.name} doesn't share M");
            }
        }
        InputConfig.SetOverride(InputConfig.ToggleMagicView, "<Keyboard>/n");
        using (var a = InputConfig.CreateActions())
            CollectionAssert.AreEqual(new[] { "<Keyboard>/n" }, InputConfig.EffectivePaths(a.ToggleMagicView));
    }
}
