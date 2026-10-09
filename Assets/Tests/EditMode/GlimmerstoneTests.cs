using System;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Glimmerstone (#114): the second material — catalogued, held in the stockpile (none to start), and dug from the
/// shallow band nearest the entrance through #103's yield seam.
/// </summary>
public class GlimmerstoneTests
{
    private const float Dt = 0.02f;

    private GameObject managerGo;
    private GameObject impGo;
    private DungeonBoard board;
    private Stockpile stockpile;
    private Imp imp;
    private ImpDigger digger;

    [SetUp]
    public void SetUp()
    {
        // Default board: 48x32, the 6x6 Floor cavern at (21,13)-(26,18), the door at (24,31) on the south edge.
        managerGo = new GameObject("TestDungeonManager");
        board = managerGo.AddComponent<DungeonBoard>();
        board.InitializeBoard();
        var boardRenderer = managerGo.AddComponent<BoardRenderer>();
        boardRenderer.Board = board;
        stockpile = managerGo.AddComponent<Stockpile>();

        impGo = new GameObject("TestImp");
        imp = impGo.AddComponent<Imp>();
        imp.Board = board;
        imp.Renderer = boardRenderer;
        imp.MoveSpeed = 4f;
        imp.Spawn();
        digger = impGo.AddComponent<ImpDigger>();
        digger.Board = board;
        digger.Imp = imp;
        digger.Stockpile = stockpile;
        digger.DigSecondsPerTile = 1f;
    }

    [TearDown]
    public void TearDown()
    {
        if (impGo != null) Object.DestroyImmediate(impGo);
        if (managerGo != null) Object.DestroyImmediate(managerGo);
    }

    private int Count(MaterialType m) => stockpile.Count(m);

    private MaterialType YieldAt(int x, int y)
    {
        Assert.IsTrue(digger.TryGetTileYield(new Vector2Int(x, y), out MaterialType material, out int amount));
        Assert.AreEqual(1, amount, "The per-tile amount is unchanged");
        return material;
    }

    private void Step()
    {
        imp.Advance(Dt);
        digger.Tick(Dt);
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

    // --- The enum and the catalog ---

    [Test]
    public void TheEnumGrewByAppendage_StoneIsStillZero()
    {
        Assert.AreEqual(0, (int)MaterialType.Stone, "Scenes store Stone as 0: never reorder");
        Assert.AreEqual(1, (int)MaterialType.Glimmerstone);
    }

    [Test]
    public void TheCatalog_GlimmerstoneResolves_NamedAndLavender_NotStonesGray()
    {
        Assert.IsTrue(stockpile.TryGetDefinition(MaterialType.Glimmerstone, out MaterialDefinition glimmer));
        Assert.AreEqual("Glimmerstone", glimmer.displayName);
        Assert.AreEqual("Glimmerstone", stockpile.DisplayName(MaterialType.Glimmerstone));
        Assert.AreEqual(new Color(0.72f, 0.62f, 0.95f, 1f), glimmer.color);
        Assert.IsTrue(stockpile.TryGetDefinition(MaterialType.Stone, out MaterialDefinition stone));
        Assert.AreNotEqual(stone.color, glimmer.color);
        Assert.Greater(glimmer.color.b - glimmer.color.g, 0.2f, "A violet cast, not a gray");
    }

    [Test]
    public void AFreshStockpile_HoldsNoGlimmerstone_AndItsSeamsRoundTrip()
    {
        Assert.AreEqual(0, Count(MaterialType.Glimmerstone), "Earned, never given");
        Assert.AreEqual(10, Count(MaterialType.Stone), "Stone's start is unchanged");
        Assert.IsFalse(stockpile.TrySpend(MaterialType.Glimmerstone, 1));
        stockpile.Add(MaterialType.Glimmerstone, 3);
        Assert.AreEqual(3, Count(MaterialType.Glimmerstone));
        Assert.IsTrue(stockpile.TrySpend(MaterialType.Glimmerstone, 2));
        Assert.AreEqual(1, Count(MaterialType.Glimmerstone));
        Assert.AreEqual(10, Count(MaterialType.Stone), "Materials don't share a count");
    }

    // --- The band ---

    [Test]
    public void TheYield_GlimmerstoneInTheBand_StoneBelowIt_ExactEdge()
    {
        Assert.AreEqual(6, digger.GlimmerstoneBandRows, "Serialized default");
        Assert.AreEqual(0, ImpDigger.DepthOf(board, new Vector2Int(24, 31)), "The door's row is depth 0");
        Assert.AreEqual(MaterialType.Glimmerstone, YieldAt(24, 31), "Depth 0");
        Assert.AreEqual(MaterialType.Glimmerstone, YieldAt(23, 26), "Depth 5: the last band row");
        Assert.AreEqual(MaterialType.Stone, YieldAt(23, 25), "Depth 6: the first mid row");
        Assert.AreEqual(MaterialType.Stone, YieldAt(27, 16), "Mid-board, around the cavern");
        Assert.AreEqual(MaterialType.Stone, YieldAt(10, 0), "The far edge: no deep band yet");
    }

    [Test]
    public void TheBandSize_IsTheSerializedValue()
    {
        digger.GlimmerstoneBandRows = 2;
        Assert.AreEqual(MaterialType.Glimmerstone, YieldAt(24, 31));
        Assert.AreEqual(MaterialType.Glimmerstone, YieldAt(24, 30), "Rows 30-31 only");
        Assert.AreEqual(MaterialType.Stone, YieldAt(24, 29));

        digger.GlimmerstoneBandRows = 0;
        Assert.AreEqual(MaterialType.Stone, YieldAt(24, 31), "No band at all");
    }

    [Test]
    public void MaterialAtDepth_IsTheBandRule()
    {
        Assert.AreEqual(MaterialType.Glimmerstone, ImpDigger.MaterialAtDepth(0, 6));
        Assert.AreEqual(MaterialType.Glimmerstone, ImpDigger.MaterialAtDepth(5, 6));
        Assert.AreEqual(MaterialType.Stone, ImpDigger.MaterialAtDepth(6, 6));
        Assert.AreEqual(MaterialType.Stone, ImpDigger.MaterialAtDepth(-1, 6), "Off the board: not the band");
    }

    // --- Digging it ---

    /// <summary>Opens a floor corridor up column 24 from the cavern to just short of the band (rows 19-25).</summary>
    private void TunnelToTheBand()
    {
        for (int y = 19; y <= 25; y++) board.SetTile(24, y, TileState.Floor); // Staging: not imp digs, so they pay nothing
    }

    [Test]
    public void ACompletedInBandDig_PaysGlimmerstone_NotStone()
    {
        TunnelToTheBand();
        Assert.AreEqual(10, Count(MaterialType.Stone), "Staging the tunnel paid nothing");
        board.SetTile(24, 26, TileState.Designated); // Depth 5: in the band

        StepUntil(() => board.GetTile(24, 26) == TileState.Floor, 15f, "the in-band dig");
        Assert.AreEqual(1, Count(MaterialType.Glimmerstone), "+1 glimmerstone");
        Assert.AreEqual(10, Count(MaterialType.Stone), "Stone untouched");
    }

    [Test]
    public void AMidBoardDig_StillPaysStone()
    {
        board.SetTile(27, 16, TileState.Designated);
        StepUntil(() => board.GetTile(27, 16) == TileState.Floor, 10f, "the mid-board dig");
        Assert.AreEqual(11, Count(MaterialType.Stone));
        Assert.AreEqual(0, Count(MaterialType.Glimmerstone));
    }

    [Test]
    public void AnAbandonedInBandDig_PaysNothing()
    {
        TunnelToTheBand();
        board.SetTile(24, 26, TileState.Designated);
        StepUntil(() => digger.IsDigging, 15f, "the imp to start the in-band dig");
        for (int i = 0; i < 25; i++) Step(); // Halfway
        board.SetTile(24, 26, TileState.Rock); // Designation cleared mid-dig
        for (int i = 0; i < 100; i++) Step();
        Assert.AreEqual(TileState.Rock, board.GetTile(24, 26));
        Assert.AreEqual(0, Count(MaterialType.Glimmerstone));
        Assert.AreEqual(10, Count(MaterialType.Stone));
    }
}
