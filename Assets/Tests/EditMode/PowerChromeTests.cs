using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// #144: the power chrome. The overlay's rules over plain data (node looks, grid hues, dimming, Normal as the identity),
/// the grid service's read seams (per-grid totals, brownout, NodeAt, NetworkChanged), the live overlay on a two-grid
/// board (distinct hues, the brownout hue, node colors, fog, an exact restore), the TopBar power segment (aggregate,
/// warning, text, the click that toggles only the view), and the conduit tooltip line.
/// </summary>
public class PowerChromeTests
{
    private static readonly Vector2Int ImpTile = new Vector2Int(24, 16); // Cavern centre; cavern (21,13)-(26,18)
    // Grid A (id 0): a wheel and a trap, both on the run (21,14)-(22,14).
    private static readonly Vector2Int WheelA = new Vector2Int(21, 14);
    private static readonly Vector2Int TrapA = new Vector2Int(22, 14);
    // Grid B (id 1): a wheel, a bare conduit tile and a lamp on the run (21,17)-(23,17).
    private static readonly Vector2Int WheelB = new Vector2Int(21, 17);
    private static readonly Vector2Int RunB = new Vector2Int(22, 17);
    private static readonly Vector2Int LampB = new Vector2Int(23, 17);
    private static readonly Vector2Int OffNetwork = new Vector2Int(25, 15); // Explored floor, no conduit, no node
    private static readonly Vector2Int Fogged = new Vector2Int(30, 15);     // Floor with built conduit, never seen

    private GameObject managerGo;
    private DungeonBoard board;
    private BoardRenderer boardRenderer;
    private PlacementManager manager;
    private PowerGrid grid;
    private FogOfWar fog;
    private GameObject barGo;
    private TopBar bar;

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
        manager = managerGo.AddComponent<PlacementManager>();
        manager.Board = board;
        manager.Renderer = boardRenderer;
        manager.PowerGrid = grid;
        UseUntendedWheel();

        fog = managerGo.AddComponent<FogOfWar>();
        fog.Board = board;
        fog.RevealAround(new Vector2Int(23, 15)); // Chebyshev 3: (20..26, 12..18), the whole cavern
        boardRenderer.Fog = fog;
        boardRenderer.Grid = grid;
    }

    [TearDown]
    public void TearDown()
    {
        if (bar != null && bar.PowerSegment != null) Object.DestroyImmediate(bar.PowerSegment.transform.root.gameObject);
        if (barGo != null) Object.DestroyImmediate(barGo);
        if (managerGo != null) Object.DestroyImmediate(managerGo);
    }

    // ---- helpers ----

    // The shipped rows, with the wheel producing whenever built so tests don't need an imp tending it.
    private void UseUntendedWheel()
    {
        var rows = new List<PlaceablePower>(manager.Power);
        rows[rows.FindIndex(r => r.type == PlaceableType.ImpWheel)] = new PlaceablePower(PlaceableType.ImpWheel, PowerRole.Producer, 40);
        manager.Power = rows;
    }

    private PowerNode PlaceBuiltNode(PlaceableType type, Vector2Int tile)
    {
        Assert.IsTrue(manager.TryPlaceBuilt(type, tile, ImpTile, out Placeable p), $"{type} at {tile}");
        return p.GetComponent<PowerNode>();
    }

    private void BuildConduit(params Vector2Int[] tiles)
    {
        foreach (Vector2Int tile in tiles)
        {
            Assert.AreEqual(1, grid.ApplyRect(null, new RectInt(tile.x, tile.y, 1, 1), PowerGrid.ConduitMode.Lay), $"Lay {tile}");
            Assert.IsTrue(grid.CompleteConduit(tile));
        }
    }

    // Two grids: A (wheel 40 W + trap 10 W) and B (wheel 40 W + lamp 20 W), plus a built run under fog.
    private (PowerNode wheelA, PowerNode trapA, PowerNode wheelB, PowerNode lampB) TwoGrids()
    {
        BuildConduit(WheelA, TrapA, WheelB, RunB, LampB);
        PowerNode wheelA = PlaceBuiltNode(PlaceableType.ImpWheel, WheelA);
        PowerNode trapA = PlaceBuiltNode(PlaceableType.SpikeTrap, TrapA);
        PowerNode wheelB = PlaceBuiltNode(PlaceableType.ImpWheel, WheelB);
        PowerNode lampB = PlaceBuiltNode(PlaceableType.GrowLamp, LampB);
        board.SetTile(Fogged.x, Fogged.y, TileState.Floor);
        BuildConduit(Fogged); // Staged: no fog check on the lay
        Assert.AreEqual(3, grid.GridCount, "A, B and the fogged run");
        Assert.AreEqual(0, grid.GridAt(WheelA));
        Assert.AreEqual(1, grid.GridAt(WheelB));
        return (wheelA, trapA, wheelB, lampB);
    }

    private TopBar MakeBar()
    {
        barGo = new GameObject("TestTopBar");
        bar = barGo.AddComponent<TopBar>();
        bar.PowerGrid = grid;
        bar.BoardRenderer = boardRenderer;
        return bar;
    }

    private Material TileMaterial(Vector2Int t) => boardRenderer.GetTileView(t.x, t.y).GetComponent<MeshRenderer>().sharedMaterial;

    private Material ConduitMaterial(Vector2Int t) =>
        boardRenderer.GetConduitView(t.x, t.y).GetComponentInChildren<MeshRenderer>().sharedMaterial;

    private static Vector2Int T(int x, int y) => new Vector2Int(x, y);

    private static PowerGrid.GridTotals G(int supply, int demand) => new PowerGrid.GridTotals(true, supply, demand);

    // ---- 1. overlay rules over plain data ----

    [Test]
    public void LookOf_TruthTable()
    {
        // role, enabled, connected, active, powered → look
        Assert.AreEqual(NodeLook.ActiveProducer, PowerOverlay.LookOf(PowerRole.Producer, true, true, true, true));
        Assert.AreEqual(NodeLook.ActiveProducer, PowerOverlay.LookOf(PowerRole.Producer, true, true, true, false), "A producer reads by active, not powered");
        Assert.AreEqual(NodeLook.IdleProducer, PowerOverlay.LookOf(PowerRole.Producer, true, true, false, true));
        Assert.AreEqual(NodeLook.PoweredConsumer, PowerOverlay.LookOf(PowerRole.Consumer, true, true, true, true));
        Assert.AreEqual(NodeLook.UnpoweredConsumer, PowerOverlay.LookOf(PowerRole.Consumer, true, true, true, false));
        Assert.AreEqual(NodeLook.Unconnected, PowerOverlay.LookOf(PowerRole.Producer, true, false, true, false));
        Assert.AreEqual(NodeLook.Unconnected, PowerOverlay.LookOf(PowerRole.Consumer, true, false, true, false));
        Assert.AreEqual(NodeLook.Disabled, PowerOverlay.LookOf(PowerRole.Producer, false, true, false, false), "Disabled wins");
        Assert.AreEqual(NodeLook.Disabled, PowerOverlay.LookOf(PowerRole.Consumer, false, false, false, false), "Even unconnected");
    }

    [Test]
    public void GridHue_IsDeterministicPerId_NeighborsDiffer_AndFallsBack()
    {
        var palette = new List<Color> { Color.blue, Color.green, Color.magenta };
        Assert.AreEqual(Color.blue, PowerOverlay.GridHue(palette, 0, Color.black));
        Assert.AreEqual(Color.green, PowerOverlay.GridHue(palette, 1, Color.black));
        Assert.AreEqual(Color.blue, PowerOverlay.GridHue(palette, 3, Color.black), "Wraps: palette[id % count]");
        Assert.AreEqual(PowerOverlay.GridHue(palette, 7, Color.black), PowerOverlay.GridHue(palette, 7, Color.black), "Same id, same hue");
        for (int id = 0; id < 10; id++)
            Assert.AreNotEqual(PowerOverlay.GridHue(palette, id, Color.black), PowerOverlay.GridHue(palette, id + 1, Color.black));

        Assert.AreEqual(Color.black, PowerOverlay.GridHue(new List<Color>(), 0, Color.black), "Empty palette");
        Assert.AreEqual(Color.black, PowerOverlay.GridHue(null, 0, Color.black), "No palette");
        Assert.AreEqual(Color.black, PowerOverlay.GridHue(palette, -1, Color.black), "Not a grid");

        IReadOnlyList<Color> shipped = boardRenderer.GridPalette;
        Assert.GreaterOrEqual(shipped.Count, 2, "Two grids are always separable");
        Assert.AreNotEqual(shipped[0], shipped[1]);
    }

    [Test]
    public void Dim_ScalesRgb_KeepsAlpha_Clamps()
    {
        Color dimmed = PowerOverlay.Dim(new Color(0.8f, 0.4f, 0.2f, 0.5f), 0.5f);
        Assert.AreEqual(0.4f, dimmed.r, 1e-6f);
        Assert.AreEqual(0.2f, dimmed.g, 1e-6f);
        Assert.AreEqual(0.1f, dimmed.b, 1e-6f);
        Assert.AreEqual(0.5f, dimmed.a, 1e-6f, "Alpha kept");
        Assert.AreEqual(Color.white, PowerOverlay.Dim(Color.white, 1f), "Factor 1: unchanged");
        Assert.AreEqual(Color.white, PowerOverlay.Dim(Color.white, 3f), "Clamped to 1");
        Assert.AreEqual(new Color(0f, 0f, 0f, 1f), PowerOverlay.Dim(Color.white, -1f), "Clamped to 0");
    }

    [Test]
    public void Resolve_NormalIsTheIdentity_FogWins_OverrideThenDim()
    {
        Color normal = new Color(0.5f, 0.3f, 0.2f, 1f);
        Color over = Color.cyan;
        foreach (bool explored in new[] { true, false })
        foreach (bool dimmed in new[] { true, false })
        {
            Assert.AreEqual(normal, PowerOverlay.Resolve(ViewMode.Normal, explored, normal, over, dimmed, 0.35f), "Normal: no override anywhere");
            Assert.AreEqual(normal, PowerOverlay.Resolve(ViewMode.Normal, explored, normal, null, dimmed, 0.35f));
        }

        Assert.AreEqual(normal, PowerOverlay.Resolve(ViewMode.Power, false, normal, over, true, 0.35f), "Fog: nothing shows through");
        Assert.AreEqual(over, PowerOverlay.Resolve(ViewMode.Power, true, normal, over, true, 0.35f), "The override wins");
        Assert.AreEqual(PowerOverlay.Dim(normal, 0.35f), PowerOverlay.Resolve(ViewMode.Power, true, normal, null, true, 0.35f), "Else dimmed");
        Assert.AreEqual(normal, PowerOverlay.Resolve(ViewMode.Power, true, normal, null, false, 0.35f), "Undimmed kept (a designation)");
    }

    // ---- 2. grid read seams ----

    [Test]
    public void TotalsPerGrid_SumsEachGrid_AndLeavesUnconnectedNodesOut()
    {
        Dictionary<Vector2Int, int> comps = PowerGrid.Components(new[] { T(1, 1), T(2, 1), T(5, 5), T(5, 6) });
        Assert.AreEqual(2, PowerGrid.CountGrids(comps));
        Assert.AreEqual(0, PowerGrid.CountGrids(new Dictionary<Vector2Int, int>()));
        var states = new[]
        {
            new PowerGrid.NodeState(T(1, 1), PowerRole.Producer, 40, true),
            new PowerGrid.NodeState(T(2, 1), PowerRole.Consumer, 10, true),
            new PowerGrid.NodeState(T(5, 5), PowerRole.Producer, 25, false), // Idle: no supply
            new PowerGrid.NodeState(T(5, 6), PowerRole.Consumer, 20, true),
            new PowerGrid.NodeState(T(9, 9), PowerRole.Producer, 99, true),  // On no grid
            new PowerGrid.NodeState(T(3, 1), PowerRole.Consumer, 99, true),  // Beside a run, not on it: no grid
        };
        List<PowerGrid.GridTotals> totals = PowerGrid.TotalsPerGrid(states, comps, 2);
        Assert.AreEqual(2, totals.Count);
        Assert.AreEqual((40, 10), (totals[0].Supply, totals[0].Demand));
        Assert.AreEqual((0, 20), (totals[1].Supply, totals[1].Demand));
        Assert.IsTrue(totals[0].Connected && totals[1].Connected);
    }

    [Test]
    public void IsBrownout_ShortIsBrownout_EqualityIsNot_UnconnectedNever()
    {
        Assert.IsTrue(PowerGrid.IsBrownout(G(10, 20)));
        Assert.IsFalse(PowerGrid.IsBrownout(G(20, 20)), "Equality is powered");
        Assert.IsFalse(PowerGrid.IsBrownout(G(40, 10)));
        Assert.IsFalse(PowerGrid.IsBrownout(G(0, 0)), "A bare run");
        Assert.IsFalse(PowerGrid.IsBrownout(new PowerGrid.GridTotals(false, 0, 99)), "Not a grid");
    }

    [Test]
    public void LiveSeams_AllGridTotals_NodeAt_TotalsOfGrid()
    {
        var (wheelA, trapA, wheelB, lampB) = TwoGrids();
        PowerNode loose = PlaceBuiltNode(PlaceableType.ImpWheel, T(25, 14)); // No conduit under it

        List<PowerGrid.GridTotals> all = grid.AllGridTotals();
        Assert.AreEqual(3, all.Count);
        Assert.AreEqual((40, 10), (all[0].Supply, all[0].Demand), "A");
        Assert.AreEqual((40, 20), (all[1].Supply, all[1].Demand), "B");
        Assert.AreEqual((0, 0), (all[2].Supply, all[2].Demand), "The bare fogged run");
        Assert.AreEqual(all[1].Supply, grid.TotalsOfGrid(1).Supply);
        Assert.IsFalse(grid.TotalsOfGrid(3).Connected, "No grid 3");
        Assert.IsFalse(grid.TotalsOfGrid(-1).Connected);

        Assert.AreSame(wheelA, grid.NodeAt(WheelA));
        Assert.AreSame(lampB, grid.NodeAt(LampB));
        Assert.AreSame(loose, grid.NodeAt(T(25, 14)), "Unconnected nodes are still nodes");
        Assert.IsNull(grid.NodeAt(RunB), "Conduit, no node");

        Assert.IsTrue(manager.TryPlace(PlaceableType.SpikeTrap, T(24, 13), ImpTile, out Placeable site));
        Assert.IsNull(grid.NodeAt(T(24, 13)), "A site is not a node");
        site.CompleteConstruction();
        Assert.IsNotNull(grid.NodeAt(T(24, 13)), "The index follows registration");
    }

    [Test]
    public void NetworkChanged_OnBuiltConduitAndNodeChanges_NotOnPendingLays()
    {
        int raised = 0;
        grid.NetworkChanged += () => raised++;

        grid.ApplyRect(null, new RectInt(22, 15, 1, 1), PowerGrid.ConduitMode.Lay);
        Assert.AreEqual(0, raised, "Pending conduit isn't part of any grid");
        grid.CompleteConduit(T(22, 15));
        Assert.AreEqual(1, raised, "Built");
        PlaceBuiltNode(PlaceableType.SpikeTrap, T(22, 15));
        Assert.AreEqual(2, raised, "A node joined");
        grid.RemoveConduit(T(22, 15));
        Assert.AreEqual(3, raised, "Built conduit removed");

        grid.ApplyRect(null, new RectInt(23, 15, 1, 1), PowerGrid.ConduitMode.Lay);
        grid.RemoveConduit(T(23, 15));
        Assert.AreEqual(3, raised, "Removing pending conduit changes no grid");
    }

    // ---- 3. the live overlay ----

    [Test]
    public void Overlay_TwoGridsTwoHues_NodesByState_OffNetworkDims_FogUntouched()
    {
        var (wheelA, trapA, wheelB, lampB) = TwoGrids();
        boardRenderer.RenderFullBoard();
        Color floorNormal = boardRenderer.DisplayColorAt(OffNetwork.x, OffNetwork.y);
        Color foggedNormal = boardRenderer.DisplayColorAt(Fogged.x, Fogged.y);
        Assert.IsNull(boardRenderer.GetConduitView(Fogged.x, Fogged.y), "Fogged conduit isn't drawn");

        boardRenderer.SetViewMode(ViewMode.Power);
        Assert.AreEqual(ViewMode.Power, boardRenderer.Mode);

        Color hueA = boardRenderer.GridColor(0);
        Color hueB = boardRenderer.GridColor(1);
        Assert.AreEqual(boardRenderer.GridPalette[0], hueA);
        Assert.AreEqual(boardRenderer.GridPalette[1], hueB);
        Assert.AreNotEqual(hueA, hueB, "Each grid in its own hue");
        Assert.AreEqual(hueA, boardRenderer.ConduitDisplayColor(WheelA, true));
        Assert.AreSame(boardRenderer.MaterialFor(hueA), ConduitMaterial(TrapA), "A's run is drawn in A's hue");
        Assert.AreSame(boardRenderer.MaterialFor(hueB), ConduitMaterial(RunB), "B's run in B's");

        Color dimmedFloor = PowerOverlay.Dim(floorNormal, boardRenderer.OverlayDimFactor);
        Assert.AreEqual(dimmedFloor, boardRenderer.DisplayColorAt(OffNetwork.x, OffNetwork.y), "Off the network: dimmed");
        Assert.AreSame(boardRenderer.MaterialFor(dimmedFloor), TileMaterial(OffNetwork));
        Assert.AreNotEqual(dimmedFloor, boardRenderer.DisplayColorAt(RunB.x, RunB.y), "A bare run's floor leans to its hue");
        Assert.AreEqual(foggedNormal, boardRenderer.DisplayColorAt(Fogged.x, Fogged.y), "Fog: exactly as in Normal");
        Assert.IsNull(boardRenderer.GetConduitView(Fogged.x, Fogged.y), "Still not drawn");

        Assert.AreEqual(boardRenderer.NodeColor(NodeLook.ActiveProducer), boardRenderer.DisplayColorAt(WheelA.x, WheelA.y));
        Assert.AreEqual(boardRenderer.NodeColor(NodeLook.PoweredConsumer), boardRenderer.DisplayColorAt(TrapA.x, TrapA.y));
        Assert.AreEqual(boardRenderer.NodeColor(NodeLook.PoweredConsumer), boardRenderer.DisplayColorAt(LampB.x, LampB.y));
        Assert.AreSame(boardRenderer.MaterialFor(boardRenderer.NodeColor(NodeLook.PoweredConsumer)), TileMaterial(LampB));

        PowerNode loose = PlaceBuiltNode(PlaceableType.SpikeTrap, T(25, 17)); // No conduit under it
        Assert.AreEqual(boardRenderer.NodeColor(NodeLook.Unconnected), boardRenderer.DisplayColorAt(25, 17));
        Assert.AreEqual(NodeLook.Unconnected, BoardRenderer.LookOfNode(loose));
    }

    [Test]
    public void Overlay_BrowningOutOneGrid_FlipsItToTheWarningHueOnTheNextRepaint_RepoweringClearsIt()
    {
        var (wheelA, trapA, wheelB, lampB) = TwoGrids();
        boardRenderer.RenderFullBoard();
        boardRenderer.SetViewMode(ViewMode.Power);
        Color hueA = boardRenderer.GridColor(0);
        Color hueB = boardRenderer.GridColor(1);

        wheelB.SetEnabled(false); // B: 0 W for the lamp's 20 W
        Assert.AreEqual(boardRenderer.BrownoutGridColor, boardRenderer.GridColor(1), "B is browned out");
        Assert.AreEqual(hueA, boardRenderer.GridColor(0), "A is untouched");
        Assert.AreSame(boardRenderer.MaterialFor(hueB), ConduitMaterial(RunB), "Views change on the repaint, not before");

        boardRenderer.RepaintOverlay(); // The cadence
        Assert.AreSame(boardRenderer.MaterialFor(boardRenderer.BrownoutGridColor), ConduitMaterial(RunB), "B's run: the warning hue");
        Assert.AreSame(boardRenderer.MaterialFor(hueA), ConduitMaterial(TrapA), "A's run keeps its hue");
        Assert.AreSame(boardRenderer.MaterialFor(boardRenderer.NodeColor(NodeLook.UnpoweredConsumer)), TileMaterial(LampB), "The lamp reads fault red");
        Assert.AreSame(boardRenderer.MaterialFor(boardRenderer.NodeColor(NodeLook.Disabled)), TileMaterial(WheelB), "The wheel greys");

        wheelB.SetEnabled(true);
        boardRenderer.RepaintOverlay();
        Assert.AreSame(boardRenderer.MaterialFor(hueB), ConduitMaterial(RunB), "Repowered: B's own hue again");
        Assert.AreSame(boardRenderer.MaterialFor(boardRenderer.NodeColor(NodeLook.PoweredConsumer)), TileMaterial(LampB));
        Assert.AreSame(boardRenderer.MaterialFor(boardRenderer.NodeColor(NodeLook.ActiveProducer)), TileMaterial(WheelB));
    }

    [Test]
    public void Overlay_PendingConduitIsANeutralGhost_DesignationsKeepTheirAmber()
    {
        grid.ApplyRect(null, new RectInt(22, 15, 1, 1), PowerGrid.ConduitMode.Lay);
        board.SetTile(26, 13, TileState.Designated);
        boardRenderer.RenderFullBoard();
        Color designated = boardRenderer.DisplayColorAt(26, 13);

        boardRenderer.SetViewMode(ViewMode.Power);
        Assert.AreEqual(boardRenderer.PendingOverlayColor, boardRenderer.ConduitDisplayColor(T(22, 15), false));
        Assert.AreSame(CreatureModel.GhostMaterialFor(boardRenderer.PendingOverlayColor), ConduitMaterial(T(22, 15)));
        Assert.AreEqual(designated, boardRenderer.DisplayColorAt(26, 13), "A designation isn't dimmed");
    }

    [Test]
    public void TogglingBackToNormal_RestoresEveryTileAndConduitExactly()
    {
        TwoGrids();
        grid.ApplyRect(null, new RectInt(25, 16, 1, 1), PowerGrid.ConduitMode.Lay); // A pending ghost too
        boardRenderer.RenderFullBoard();

        var before = new Dictionary<Vector2Int, Material>();
        for (int x = 0; x < board.Width; x++)
            for (int y = 0; y < board.Height; y++)
                before[T(x, y)] = TileMaterial(T(x, y));
        var conduitBefore = new Dictionary<Vector2Int, Material>();
        foreach (Vector2Int t in new[] { WheelA, TrapA, WheelB, RunB, LampB, T(25, 16) }) conduitBefore[t] = ConduitMaterial(t);

        boardRenderer.ToggleViewMode(ViewMode.Power);
        Assert.AreEqual(ViewMode.Power, boardRenderer.Mode);
        Assert.AreNotSame(before[OffNetwork], TileMaterial(OffNetwork), "The overlay changed it");
        boardRenderer.ToggleViewMode(ViewMode.Power);
        Assert.AreEqual(ViewMode.Normal, boardRenderer.Mode, "The same toggle turns it off");

        foreach (KeyValuePair<Vector2Int, Material> entry in before)
            Assert.AreSame(entry.Value, TileMaterial(entry.Key), $"Tile {entry.Key}");
        foreach (KeyValuePair<Vector2Int, Material> entry in conduitBefore)
            Assert.AreSame(entry.Value, ConduitMaterial(entry.Key), $"Conduit {entry.Key}");

        boardRenderer.RepaintOverlay(); // A stray cadence tick in Normal does nothing
        Assert.AreSame(before[OffNetwork], TileMaterial(OffNetwork));
    }

    [Test]
    public void ViewModeChanged_FiresOnChangesOnly()
    {
        var seen = new List<ViewMode>();
        boardRenderer.ViewModeChanged += m => seen.Add(m);
        boardRenderer.SetViewMode(ViewMode.Normal);
        boardRenderer.SetViewMode(ViewMode.Power);
        boardRenderer.SetViewMode(ViewMode.Power);
        boardRenderer.ToggleViewMode(ViewMode.Power);
        CollectionAssert.AreEqual(new[] { ViewMode.Power, ViewMode.Normal }, seen);
        Assert.AreEqual(0.25f, boardRenderer.OverlayRefreshSeconds, "The serialized cadence");
    }

    // ---- 4. the TopBar power segment ----

    [Test]
    public void SummarizePower_SumsAcrossGrids_AnyShortGridWarns()
    {
        TopBar.PowerSummary two = TopBar.SummarizePower(new[] { G(40, 10), G(100, 150) });
        Assert.AreEqual(140, two.Supply);
        Assert.AreEqual(160, two.Demand);
        Assert.IsTrue(two.Brownout, "One grid short");

        TopBar.PowerSummary hidden = TopBar.SummarizePower(new[] { G(200, 10), G(5, 20) });
        Assert.IsTrue(hidden.Brownout, "Summed 205 ≥ 30, but the second grid is short: a surplus elsewhere doesn't hide it");

        Assert.IsFalse(TopBar.SummarizePower(new[] { G(20, 20), G(40, 0) }).Brownout, "Equality is not a brownout");

        TopBar.PowerSummary none = TopBar.SummarizePower(new PowerGrid.GridTotals[0]);
        Assert.AreEqual((0, 0, false), (none.Supply, none.Demand, none.Brownout), "No grids: neutral");
        TopBar.PowerSummary unwired = TopBar.SummarizePower(null);
        Assert.AreEqual((0, 0, false), (unwired.Supply, unwired.Demand, unwired.Brownout));
    }

    [Test]
    public void PowerReadout_Format()
    {
        Assert.AreEqual("⚡ 140/160", TopBar.PowerReadout(140, 160));
        Assert.AreEqual("⚡ 0/0", TopBar.PowerReadout(0, 0));
    }

    [Test]
    public void NormalizedShares_WeightsOverTheirSum()
    {
        float[] shares = TopBar.NormalizedShares(1f, 3f);
        Assert.AreEqual(0.25f, shares[0], 1e-6f);
        Assert.AreEqual(0.75f, shares[1], 1e-6f);

        // The scene's four shares (sum 1) plus power's weight: still exactly the full width.
        float[] bar = TopBar.NormalizedShares(0.1885f, 0.133f, 0.136f, 0.38f, 0.2985f);
        float sum = 0f;
        foreach (float s in bar) sum += s;
        Assert.AreEqual(1f, sum, 1e-5f);
        Assert.AreEqual(0.1885f / 1.136f, bar[0], 1e-5f, "The four keep their proportions");

        float[] zeros = TopBar.NormalizedShares(0f, 0f, -1f);
        CollectionAssert.AreEqual(new[] { 1f / 3f, 1f / 3f, 1f / 3f }, zeros, "Nothing to weigh: even");
    }

    [Test]
    public void Refresh_MatchesTheLiveGrids_RedExactlyWhileAGridIsShort_UnconnectedNodesCountNowhere()
    {
        var (wheelA, trapA, wheelB, lampB) = TwoGrids();
        PlaceBuiltNode(PlaceableType.ImpWheel, T(25, 14)); // Unconnected producer: not on the books
        PlaceBuiltNode(PlaceableType.SpikeTrap, T(25, 17)); // Unconnected consumer: not on the books
        TopBar topBar = MakeBar();
        topBar.CreateBar();

        topBar.Refresh();
        Assert.AreEqual("⚡ 80/30", topBar.PowerText, "A 40/10 + B 40/20");
        Assert.IsFalse(topBar.PowerWarning);
        UnityEngine.UI.Text label = topBar.PowerSegment.GetComponentInChildren<UnityEngine.UI.Text>();
        Assert.AreEqual("⚡ 80/30", label.text);
        Assert.AreEqual(Color.white, label.color);

        wheelB.SetEnabled(false);
        topBar.Refresh();
        Assert.AreEqual("⚡ 40/30", topBar.PowerText, "Totals 40 ≥ 30, but B is short");
        Assert.IsTrue(topBar.PowerWarning);
        Assert.AreEqual(topBar.PowerWarningColor, label.color, "Red while any grid is browned out");

        wheelB.SetEnabled(true);
        topBar.Refresh();
        Assert.IsFalse(topBar.PowerWarning, "Repowered: neutral again");
        Assert.AreEqual(Color.white, label.color);
    }

    [Test]
    public void Refresh_NoGridOrUnwired_ReadsZeroNeutral()
    {
        TopBar topBar = MakeBar();
        topBar.Refresh();
        Assert.AreEqual("⚡ 0/0", topBar.PowerText, "A grid service with no grids");
        Assert.IsFalse(topBar.PowerWarning);

        topBar.PowerGrid = null;
        topBar.BoardRenderer = null;
        Assert.DoesNotThrow(() => topBar.Refresh());
        Assert.AreEqual("⚡ 0/0", topBar.PowerText);
        Assert.DoesNotThrow(() => topBar.TogglePowerOverlay(), "Unwired: the toggle is a no-op");
    }

    [Test]
    public void TheSegment_IsTheBarsOnlyRaycastTarget_AndItsClickTogglesTheOverlayWithATint()
    {
        TopBar topBar = MakeBar();
        topBar.CreateBar();
        Button segment = topBar.PowerSegment;
        topBar.CreateBar(); // Idempotent
        Assert.AreSame(segment, topBar.PowerSegment, "Built once");

        GameObject canvas = topBar.PowerSegment.transform.root.gameObject;
        Assert.IsNotNull(canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>());
        var targets = new List<string>();
        foreach (UnityEngine.UI.Graphic g in canvas.GetComponentsInChildren<UnityEngine.UI.Graphic>())
            if (g.raycastTarget) targets.Add(g.name);
        CollectionAssert.AreEqual(new[] { "PowerSegment" }, targets, "The bar background stays dig-through");

        var image = topBar.PowerSegment.GetComponent<UnityEngine.UI.Image>();
        Color idle = image.color;
        topBar.PowerSegment.onClick.Invoke();
        Assert.AreEqual(ViewMode.Power, boardRenderer.Mode);
        Assert.AreNotEqual(idle, image.color, "Tinted while the overlay is on");
        topBar.PowerSegment.onClick.Invoke();
        Assert.AreEqual(ViewMode.Normal, boardRenderer.Mode);
        Assert.AreEqual(idle, image.color);

        Assert.AreEqual(5, canvas.GetComponentsInChildren<UnityEngine.UI.Text>().Length, "Five readouts");
    }

    [Test]
    public void TheToggle_FlipsOnlyTheViewMode_ToolSelectionAndStockUntouched()
    {
        var controller = managerGo.AddComponent<PlacementController>();
        controller.DigDesignator = managerGo.AddComponent<DigDesignator>();
        controller.PlacementManager = manager;
        var stockpile = managerGo.AddComponent<Stockpile>();
        manager.Stockpile = stockpile;
        var panel = managerGo.AddComponent<StockPanel>();
        panel.PlacementController = controller;
        panel.PlacementManager = manager;
        panel.Stockpile = stockpile;
        TopBar topBar = MakeBar();
        topBar.CreateBar();
        try
        {
            controller.Select(PlaceableType.SpikeTrap);
            PlacementController.ToolMode mode = controller.Mode;
            topBar.PowerSegment.onClick.Invoke();
            Assert.AreEqual(ViewMode.Power, boardRenderer.Mode);
            Assert.AreEqual(PlaceableType.SpikeTrap, controller.SelectedType, "Still armed");
            Assert.AreEqual(mode, controller.Mode);

            controller.ClearSelection();
            panel.Open();
            topBar.TogglePowerOverlay(); // The hotkey's path
            Assert.AreEqual(ViewMode.Normal, boardRenderer.Mode);
            Assert.IsTrue(panel.IsOpen, "STOCK stays open");
            topBar.TogglePowerOverlay();
            Assert.IsTrue(panel.IsOpen);

            controller.SelectWire(); // Switching tools leaves the overlay on
            Assert.AreEqual(ViewMode.Power, boardRenderer.Mode);
        }
        finally
        {
            panel.DestroyChrome();
        }
    }

    // ---- 5. conduit tooltips ----

    [Test]
    public void ConduitLine_Composition()
    {
        Assert.AreEqual("Conduit — grid: 40 produced · 10 demanded", TileHover.ConduitLine(true, true, false, 40, 10));
        Assert.AreEqual("Conduit — grid: 0 produced · 0 demanded", TileHover.ConduitLine(true, true, false, 0, 0), "A bare run, truthfully");
        Assert.AreEqual("Conduit (planned)", TileHover.ConduitLine(true, false, false, 0, 0));
        Assert.IsNull(TileHover.ConduitLine(true, true, true, 40, 10), "A node tile: the node is what the tile is");
        Assert.IsNull(TileHover.ConduitLine(false, false, false, 0, 0), "No conduit");
    }

    [Test]
    public void TooltipLabel_AddsTheConduitLineBeneath_AndFogSuppressesEverything()
    {
        var identity = new TileIdentity(TileState.Floor, null);
        Assert.AreEqual("Floor (22, 17)\nConduit (planned)", TileHover.TooltipLabel(fog, identity, RunB, null, "Conduit (planned)"));
        Assert.AreEqual("Floor (22, 17)", TileHover.TooltipLabel(fog, identity, RunB, null, null));
        Assert.AreEqual("Floor (22, 17)", TileHover.TooltipLabel(fog, identity, RunB, null), "The #132 overload is unchanged");
        Assert.IsNull(TileHover.TooltipLabel(fog, identity, Fogged, null, "Conduit (planned)"), "Under fog: nothing");
    }

    [Test]
    public void ConduitLine_FromTheLiveGrid()
    {
        TwoGrids();
        grid.ApplyRect(null, new RectInt(25, 16, 1, 1), PowerGrid.ConduitMode.Lay);

        Assert.AreEqual("Conduit — grid: 40 produced · 20 demanded", TileHover.ConduitLine(grid, RunB), "B's live totals");
        Assert.AreEqual("Conduit (planned)", TileHover.ConduitLine(grid, T(25, 16)));
        Assert.IsNull(TileHover.ConduitLine(grid, LampB), "The lamp's tile: node first, no conduit line");
        Assert.IsNull(TileHover.ConduitLine(grid, OffNetwork));
        Assert.IsNull(TileHover.ConduitLine(null, RunB), "No grid service");

        board.SetTile(26, 14, TileState.Floor);
        BuildConduit(T(26, 14));
        Assert.AreEqual("Conduit — grid: 0 produced · 0 demanded", TileHover.ConduitLine(grid, T(26, 14)), "A bare run");
    }
}
