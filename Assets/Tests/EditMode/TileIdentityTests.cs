using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// #132: tile identity. Explored rock is tinted by the material a dig would yield, and the hover tooltip names it.
/// The renderer, the tooltip and the digger all answer "what is this rock" from ImpDigger's one band seam, so they
/// can't drift apart. Designated stays amber, the fog still hides everything, and the name ignores discovery.
/// </summary>
public class TileIdentityTests
{
    private GameObject managerGo;
    private GameObject impGo;
    private DungeonBoard board;
    private BoardRenderer boardRenderer;
    private Stockpile stockpile;
    private ImpDigger digger;
    private FogOfWar fog;

    private static readonly Vector2Int Entrance = new Vector2Int(24, 31);
    private static readonly Vector2Int CavernFloor = new Vector2Int(24, 16);

    // Rows either side of each band edge on the default 48x32 board (depth = 31 - y): glimmerstone is depth 0-5
    // (y 31-26), deepstone depth 20+ (y 11 and below), stone between.
    private static readonly int[] EdgeRows = { 31, 27, 26, 25, 24, 16, 13, 12, 11, 10, 0 };

    [SetUp]
    public void SetUp()
    {
        managerGo = new GameObject("TestDungeonManager");
        board = managerGo.AddComponent<DungeonBoard>();
        board.InitializeBoard();
        stockpile = managerGo.AddComponent<Stockpile>(); // 10 stone to start: Stone is the only discovered material
        fog = managerGo.AddComponent<FogOfWar>();
        fog.Board = board;
        boardRenderer = managerGo.AddComponent<BoardRenderer>();
        boardRenderer.Board = board;

        impGo = new GameObject("TestImp");
        Imp imp = impGo.AddComponent<Imp>();
        imp.Board = board;
        imp.Renderer = boardRenderer;
        digger = impGo.AddComponent<ImpDigger>();
        digger.Board = board;
        digger.Imp = imp;
        digger.Stockpile = stockpile;
        boardRenderer.Digger = digger;
    }

    [TearDown]
    public void TearDown()
    {
        if (impGo != null) Object.DestroyImmediate(impGo);
        if (managerGo != null) Object.DestroyImmediate(managerGo);
    }

    private MaterialType YieldOf(Vector2Int tile)
    {
        Assert.IsTrue(digger.TryGetTileYield(tile, out MaterialType material, out _), $"{tile} pays");
        return material;
    }

    private Material ViewMaterial(Vector2Int tile) =>
        boardRenderer.GetTileView(tile.x, tile.y).GetComponent<MeshRenderer>().sharedMaterial;

    // ---- 1. Anti-drift: the renderer's and the tooltip's material is the digger's yield ----

    [Test]
    public void Identity_AtTheBandEdges_IsWhatDiggingYields()
    {
        foreach (int y in EdgeRows)
        {
            foreach (int x in new[] { 0, 10, 47 })
            {
                var tile = new Vector2Int(x, y);
                MaterialType yield = YieldOf(tile);
                Assert.AreEqual(yield, boardRenderer.IdentityAt(x, y).Material, $"Rock {tile}: renderer vs yield");

                board.SetTile(x, y, TileState.Designated);
                Assert.AreEqual(yield, boardRenderer.IdentityAt(x, y).Material, $"Designated {tile}: renderer vs yield");
                StringAssert.Contains(" — " + yield + " ", TileHover.TooltipLabel(null, TileIdentity.Of(board, digger, tile), tile, stockpile),
                    $"Designated {tile}: tooltip vs yield");
                board.SetTile(x, y, TileState.Rock);
            }
        }
    }

    [Test]
    public void Identity_BandEdges_SwitchAtTheYieldRows()
    {
        Assert.AreEqual(MaterialType.Glimmerstone, boardRenderer.IdentityAt(10, 26).Material, "Depth 5: last glimmerstone row");
        Assert.AreEqual(MaterialType.Stone, boardRenderer.IdentityAt(10, 25).Material, "Depth 6: first stone row");
        Assert.AreEqual(MaterialType.Stone, boardRenderer.IdentityAt(10, 12).Material, "Depth 19: last stone row");
        Assert.AreEqual(MaterialType.Deepstone, boardRenderer.IdentityAt(10, 11).Material, "Depth 20: first deepstone row");
    }

    [Test]
    public void Identity_FollowsTheDiggersSerializedBands_NotACopy()
    {
        digger.GlimmerstoneBandRows = 2;
        digger.DeepstoneDepthThreshold = 10;

        foreach (int y in new[] { 30, 29, 22, 21, 0 })
        {
            var tile = new Vector2Int(5, y);
            Assert.AreEqual(YieldOf(tile), boardRenderer.IdentityAt(5, y).Material, $"{tile} with retuned bands");
        }
        Assert.AreEqual(MaterialType.Stone, boardRenderer.IdentityAt(5, 29).Material, "Depth 2 is stone once the band is 2 rows");
        Assert.AreEqual(MaterialType.Deepstone, boardRenderer.IdentityAt(5, 21).Material, "Depth 10 is deep at threshold 10");
    }

    [Test]
    public void Identity_FloorAndEntrance_HaveNoMaterial_AndNoDigger_KnowsNone()
    {
        Assert.IsNull(boardRenderer.IdentityAt(CavernFloor.x, CavernFloor.y).Material, "Floor isn't rock");
        Assert.IsNull(boardRenderer.IdentityAt(Entrance.x, Entrance.y).Material, "The entrance isn't rock");
        Assert.AreEqual(TileState.Entrance, boardRenderer.IdentityAt(Entrance.x, Entrance.y).State);

        boardRenderer.Digger = null;
        Assert.IsNull(boardRenderer.IdentityAt(10, 28).Material, "Unwired: the material is unknown");
        Assert.AreEqual(TileState.Rock, boardRenderer.IdentityAt(10, 28).State);
    }

    // ---- 2. Rock visuals ----

    [Test]
    public void ColorFor_ExploredRock_IsItsBandsTint_AllThreeDistinct()
    {
        Color stone = boardRenderer.ColorFor(boardRenderer.IdentityAt(10, 20), true);
        Color glimmer = boardRenderer.ColorFor(boardRenderer.IdentityAt(10, 28), true);
        Color deep = boardRenderer.ColorFor(boardRenderer.IdentityAt(10, 5), true);

        Assert.AreEqual(boardRenderer.ColorFor(TileState.Rock), stone, "Stone keeps today's rock color");
        Assert.AreEqual(boardRenderer.RockColorFor(MaterialType.Glimmerstone), glimmer);
        Assert.AreEqual(boardRenderer.RockColorFor(MaterialType.Deepstone), deep);
        Assert.AreNotEqual(stone, glimmer);
        Assert.AreNotEqual(stone, deep);
        Assert.AreNotEqual(glimmer, deep);
        Assert.Greater(glimmer.b, glimmer.g, "Glimmerstone rock leans lavender");
        Assert.Greater(deep.b, deep.r, "Deepstone rock leans slate");
    }

    [Test]
    public void ColorFor_Designated_IsAmberInEveryBand()
    {
        Color amber = boardRenderer.ColorFor(TileState.Designated);
        foreach (MaterialType material in new[] { MaterialType.Stone, MaterialType.Glimmerstone, MaterialType.Deepstone })
            Assert.AreEqual(amber, boardRenderer.ColorFor(new TileIdentity(TileState.Designated, material), true), $"{material}");
    }

    [Test]
    public void ColorFor_Unexplored_IsTheFogColor_ForEveryBandAndState()
    {
        Color fogColor = boardRenderer.ColorFor(TileState.Rock, false);
        foreach (MaterialType material in new[] { MaterialType.Stone, MaterialType.Glimmerstone, MaterialType.Deepstone })
        {
            Assert.AreEqual(fogColor, boardRenderer.ColorFor(new TileIdentity(TileState.Rock, material), false), $"Rock {material}");
            Assert.AreEqual(fogColor, boardRenderer.ColorFor(new TileIdentity(TileState.Designated, material), false), $"Designated {material}");
        }
        Assert.AreEqual(fogColor, boardRenderer.ColorFor(new TileIdentity(TileState.Floor, null), false));
        Assert.AreEqual(fogColor, boardRenderer.ColorFor(new TileIdentity(TileState.Entrance, null), false));
    }

    [Test]
    public void ColorFor_FloorEntranceAndUnknownRock_AreTodaysColors()
    {
        Assert.AreEqual(boardRenderer.ColorFor(TileState.Floor), boardRenderer.ColorFor(new TileIdentity(TileState.Floor, null), true));
        Assert.AreEqual(boardRenderer.ColorFor(TileState.Entrance), boardRenderer.ColorFor(new TileIdentity(TileState.Entrance, null), true));
        Assert.AreEqual(boardRenderer.ColorFor(TileState.Rock), boardRenderer.ColorFor(new TileIdentity(TileState.Rock, null), true),
            "No digger: rock is today's rock");
    }

    [Test]
    public void Views_TintRockByBand_DesignationIsAmber_ClearingRestoresTheTint()
    {
        boardRenderer.RenderFullBoard();
        Material glimmer = boardRenderer.MaterialFor(boardRenderer.RockColorFor(MaterialType.Glimmerstone));
        Material stone = boardRenderer.MaterialFor(boardRenderer.RockColorFor(MaterialType.Stone));
        Material deep = boardRenderer.MaterialFor(boardRenderer.RockColorFor(MaterialType.Deepstone));

        Assert.AreSame(glimmer, ViewMaterial(new Vector2Int(10, 26)));
        Assert.AreSame(stone, ViewMaterial(new Vector2Int(10, 25)));
        Assert.AreSame(stone, ViewMaterial(new Vector2Int(10, 12)));
        Assert.AreSame(deep, ViewMaterial(new Vector2Int(10, 11)));
        Assert.AreSame(glimmer, ViewMaterial(new Vector2Int(40, 30)), "Shared per color, not per tile");

        board.SetTile(10, 26, TileState.Designated);
        boardRenderer.RefreshTile(10, 26); // EditMode doesn't run OnEnable, so the OnTileChanged hookup is stepped here
        Assert.AreSame(boardRenderer.MaterialFor(boardRenderer.ColorFor(TileState.Designated)), ViewMaterial(new Vector2Int(10, 26)));

        board.SetTile(10, 26, TileState.Rock);
        boardRenderer.RefreshTile(10, 26);
        Assert.AreSame(glimmer, ViewMaterial(new Vector2Int(10, 26)), "Cleared: the band tint returns");
    }

    [Test]
    public void Views_WithFog_TintOnlyOnceSeen()
    {
        boardRenderer.Fog = fog;
        boardRenderer.RenderFullBoard();
        Material fogMaterial = boardRenderer.MaterialFor(boardRenderer.ColorFor(TileState.Rock, false));
        Assert.AreSame(fogMaterial, ViewMaterial(new Vector2Int(10, 28)), "Unexplored glimmerstone is fog");

        fog.RevealAround(new Vector2Int(10, 28)); // The reveal notification restyles it

        Assert.AreSame(boardRenderer.MaterialFor(boardRenderer.RockColorFor(MaterialType.Glimmerstone)), ViewMaterial(new Vector2Int(10, 28)));
    }

    // ---- 3. Tooltip ----

    [Test]
    public void TooltipLabel_SeenRock_NamesEachBand_EvenUndiscovered()
    {
        Assert.IsFalse(stockpile.IsDiscovered(MaterialType.Glimmerstone), "Never held");
        Assert.IsFalse(stockpile.IsDiscovered(MaterialType.Deepstone), "Never held");

        Assert.AreEqual("Rock — Glimmerstone (26, 26)", Label(new Vector2Int(26, 26)));
        Assert.AreEqual("Rock — Stone (26, 25)", Label(new Vector2Int(26, 25)));
        Assert.AreEqual("Rock — Stone (24, 12)", Label(new Vector2Int(24, 12)));
        Assert.AreEqual("Rock — Deepstone (24, 11)", Label(new Vector2Int(24, 11)));
    }

    [Test]
    public void TooltipLabel_SeenDesignated_NamesItsMaterial()
    {
        board.SetTile(24, 11, TileState.Designated);
        board.SetTile(24, 30, TileState.Designated);
        Assert.AreEqual("Designated — Deepstone (24, 11)", Label(new Vector2Int(24, 11)));
        Assert.AreEqual("Designated — Glimmerstone (24, 30)", Label(new Vector2Int(24, 30)));
    }

    [Test]
    public void TooltipLabel_FloorAndEntrance_AreByteIdenticalToToday()
    {
        Assert.AreEqual(TileHover.FormatTileLabel(TileState.Floor, 24, 16), Label(CavernFloor));
        Assert.AreEqual("Floor (24, 16)", Label(CavernFloor));
        Assert.AreEqual("Entrance (24, 31)", Label(Entrance));
    }

    [Test]
    public void TooltipLabel_Unexplored_IsNoTooltip()
    {
        fog.RevealAround(CavernFloor);
        Assert.IsNull(TileHover.TooltipLabel(fog, TileIdentity.Of(board, digger, new Vector2Int(24, 30)), new Vector2Int(24, 30), stockpile));
        Assert.IsNull(TileHover.TooltipLabel(fog, TileIdentity.Of(board, digger, Entrance), Entrance, stockpile));
        Assert.AreEqual("Rock — Stone (27, 19)",
            TileHover.TooltipLabel(fog, TileIdentity.Of(board, digger, new Vector2Int(27, 19)), new Vector2Int(27, 19), stockpile), "Seen");
    }

    [Test]
    public void TooltipLabel_UsesTheCatalogName_AndDegradesToTheEnumName()
    {
        stockpile.Definitions = new List<MaterialDefinition>
        {
            new MaterialDefinition(MaterialType.Glimmerstone, "Glimmer Stone", Color.magenta),
        };
        var tile = new Vector2Int(26, 26);
        TileIdentity identity = TileIdentity.Of(board, digger, tile);

        Assert.AreEqual("Rock — Glimmer Stone (26, 26)", TileHover.TooltipLabel(null, identity, tile, stockpile), "Catalog display name");
        Assert.AreEqual("Rock — Stone (26, 25)", Label(new Vector2Int(26, 25)), "No definition: enum name");
        Assert.AreEqual("Rock — Glimmerstone (26, 26)", TileHover.TooltipLabel(null, identity, tile, null), "No stockpile: enum name");
        Assert.AreEqual("Rock (26, 26)", TileHover.TooltipLabel(null, TileIdentity.Of(board, null, tile), tile, stockpile),
            "No digger: today's label");
    }

    private string Label(Vector2Int tile) =>
        TileHover.TooltipLabel(null, TileIdentity.Of(board, digger, tile), tile, stockpile);
}
