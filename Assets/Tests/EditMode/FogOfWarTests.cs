using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// #131: the explored record (imp-sight discs, permanent), the starter state, sight-gated designation and its
/// preview, the hover tooltip's suppression, and the renderer's fog style.
/// </summary>
public class FogOfWarTests
{
    private GameObject managerGo;
    private GameObject impGo;
    private DungeonBoard board;
    private FogOfWar fog;

    private static readonly Vector2Int CavernCenter = new Vector2Int(24, 16);
    private static readonly Vector2Int Entrance = new Vector2Int(24, 31);

    [SetUp]
    public void SetUp()
    {
        // Default board: 48x32 Rock, the 6x6 Floor cavern at (21,13)-(26,18), the door at (24,31).
        managerGo = new GameObject("TestDungeonManager");
        board = managerGo.AddComponent<DungeonBoard>();
        board.InitializeBoard();
        fog = managerGo.AddComponent<FogOfWar>();
        fog.Board = board;
    }

    [TearDown]
    public void TearDown()
    {
        if (impGo != null) Object.DestroyImmediate(impGo);
        if (managerGo != null) Object.DestroyImmediate(managerGo);
    }

    private static List<Vector2Int> TilesIn(RectInt rect)
    {
        var tiles = new List<Vector2Int>();
        for (int x = rect.xMin; x < rect.xMax; x++)
            for (int y = rect.yMin; y < rect.yMax; y++)
                tiles.Add(new Vector2Int(x, y));
        return tiles;
    }

    // ---- 1. Disc geometry ----

    [Test]
    public void ImpSightRange_DefaultsToThree()
    {
        Assert.AreEqual(3, fog.ImpSightRange);
    }

    [Test]
    public void RevealAround_BoardCenter_RevealsTheFullSevenBySevenDisc()
    {
        Assert.AreEqual(0, fog.ExploredCount, "A fresh record has seen nothing");

        int revealed = fog.RevealAround(new Vector2Int(10, 10));

        Assert.AreEqual(49, revealed, "(2r+1)^2 for r = 3");
        Assert.AreEqual(49, fog.ExploredCount);
        foreach (Vector2Int tile in TilesIn(new RectInt(7, 7, 7, 7)))
            Assert.IsTrue(fog.IsExplored(tile), $"{tile} is within Chebyshev 3");
        Assert.IsFalse(fog.IsExplored(new Vector2Int(14, 10)), "Distance 4 on x");
        Assert.IsFalse(fog.IsExplored(new Vector2Int(10, 6)), "Distance 4 on y");
        Assert.IsFalse(fog.IsExplored(new Vector2Int(14, 14)), "Distance 4 on the diagonal");
    }

    [Test]
    public void RevealAround_Corner_ClipsToTheBoard()
    {
        Assert.AreEqual(16, fog.RevealAround(new Vector2Int(0, 0)), "4x4 on board at the min corner");
        Assert.AreEqual(16, fog.RevealAround(new Vector2Int(47, 31)), "4x4 on board at the max corner");
        Assert.AreEqual(32, fog.ExploredCount);
        Assert.IsTrue(fog.IsExplored(new Vector2Int(3, 3)));
        Assert.IsTrue(fog.IsExplored(new Vector2Int(44, 28)));
    }

    [Test]
    public void RevealAround_OverlappingSecondCall_CountsOnlyTheNewTiles()
    {
        Assert.AreEqual(49, fog.RevealAround(new Vector2Int(10, 10)));
        // One tile east: the disc shifts by one column, so exactly one new column of 7 appears.
        Assert.AreEqual(7, fog.RevealAround(new Vector2Int(11, 10)));
        Assert.AreEqual(56, fog.ExploredCount);
    }

    [Test]
    public void SightRect_IsTheClippedChebyshevDisc()
    {
        Assert.AreEqual(new RectInt(7, 7, 7, 7), FogOfWar.SightRect(new Vector2Int(10, 10), 3, 48, 32));
        Assert.AreEqual(new RectInt(0, 0, 4, 4), FogOfWar.SightRect(Vector2Int.zero, 3, 48, 32));
        Assert.AreEqual(new RectInt(10, 10, 1, 1), FogOfWar.SightRect(new Vector2Int(10, 10), 0, 48, 32), "Range 0 sees its own tile");
        Assert.AreEqual(0, FogOfWar.SightRect(new Vector2Int(-10, -10), 3, 48, 32).width, "A disc off the board is empty");
    }

    // ---- 2. Permanence and accumulation ----

    [Test]
    public void Discs_Union_AndNothingEverUnreveals()
    {
        fog.RevealAround(new Vector2Int(5, 5));
        fog.RevealAround(new Vector2Int(40, 25));
        Assert.AreEqual(98, fog.ExploredCount, "Disjoint discs union");

        Assert.AreEqual(0, fog.RevealAround(new Vector2Int(5, 5)), "Re-revealing reveals nothing new");
        Assert.AreEqual(98, fog.ExploredCount);

        // The board changing underneath (a dig, a designation) never touches the record.
        board.SetTile(5, 5, TileState.Designated);
        board.SetTile(6, 5, TileState.Floor);
        Assert.IsTrue(fog.IsExplored(new Vector2Int(5, 5)));
        Assert.IsTrue(fog.IsExplored(new Vector2Int(40, 25)), "The first-seen disc stays seen after the imp is far away");
    }

    [Test]
    public void IsExplored_OutOfBounds_ReadsUnexplored()
    {
        fog.RevealAround(Vector2Int.zero);
        Assert.IsFalse(fog.IsExplored(new Vector2Int(-1, 0)));
        Assert.IsFalse(fog.IsExplored(new Vector2Int(0, 32)));
    }

    [Test]
    public void TileRevealed_FiresOncePerNewTile()
    {
        var heard = new List<Vector2Int>();
        fog.TileRevealed += heard.Add;

        fog.RevealAround(new Vector2Int(10, 10));
        fog.RevealAround(new Vector2Int(11, 10));

        Assert.AreEqual(56, heard.Count);
        Assert.AreEqual(56, new HashSet<Vector2Int>(heard).Count, "No tile announced twice");
    }

    // ---- 3. Starter state, through the imp ----

    private Imp SpawnImp()
    {
        impGo = new GameObject("TestImp");
        Imp imp = impGo.AddComponent<Imp>();
        imp.Board = board;
        imp.Spawn(); // The cavern center, as Imp.Start does
        fog.Imp = imp;
        return imp;
    }

    [Test]
    public void StarterState_ImpAtCavernCenter_RevealsTheWholeCavern_NotTheEntrance()
    {
        Imp imp = SpawnImp();
        Assert.AreEqual(CavernCenter, imp.CurrentTile);

        fog.RevealFromImp();

        foreach (Vector2Int tile in TilesIn(new RectInt(21, 13, 6, 6)))
            Assert.IsTrue(fog.IsExplored(tile), $"Cavern tile {tile}");
        Assert.AreEqual(49, fog.ExploredCount, "Exactly the imp-sight disc, (21..27, 13..19)");
        Assert.IsFalse(fog.IsExplored(Entrance), "The door is dark at the start");
        Assert.IsFalse(fog.IsExplored(new Vector2Int(24, 20)), "Four tiles north of the imp");
    }

    [Test]
    public void RevealFromImp_RevealsOnlyWhenTheImpChangesTile()
    {
        Imp imp = SpawnImp();
        fog.RevealFromImp();
        Assert.AreEqual(49, fog.ExploredCount);

        var heard = new List<Vector2Int>();
        fog.TileRevealed += heard.Add;
        fog.RevealFromImp();
        Assert.AreEqual(0, heard.Count, "Standing still reveals nothing");

        imp.PlaceOnTile(new Vector2Int(24, 18)); // Two north, still in the cavern
        fog.RevealFromImp();
        Assert.AreEqual(14, heard.Count, "Rows 20 and 21 come into sight");
        Assert.IsTrue(fog.IsExplored(new Vector2Int(24, 21)));

        imp.PlaceOnTile(CavernCenter); // Walk back
        fog.RevealFromImp();
        Assert.AreEqual(63, fog.ExploredCount, "Walking back re-fogs nothing and reveals nothing");
    }

    [Test]
    public void IsSeen_WithoutFog_EverythingIsSeen()
    {
        Assert.IsTrue(FogOfWar.IsSeen(null, Entrance));
        Assert.IsTrue(FogOfWar.IsSeen(null, new Vector2Int(0, 0)));
        Assert.IsFalse(FogOfWar.IsSeen(fog, Entrance), "With fog, unexplored is unseen");
    }

    // ---- 4. Sight-gated designation ----

    // The starter disc: x 21..27, y 13..19 explored. A rect from (25,17) to (30,21) crosses its east and north edges.
    private static readonly RectInt FrontierRect = new RectInt(25, 17, 6, 5);

    [Test]
    public void ApplyRect_Designate_AcrossTheFrontier_DesignatesOnlyTheSeenRock()
    {
        fog.RevealAround(CavernCenter);

        int changed = DigDesignator.ApplyRect(board, fog, FrontierRect, DigDesignator.DesignateMode.Designate);

        // Seen part: x 25..27, y 17..19 = 9 tiles; Floor among them: x 25..26, y 17..18 = 4. Seen Rock = 5.
        Assert.AreEqual(5, changed);
        foreach (Vector2Int tile in TilesIn(FrontierRect))
        {
            bool seenRock = fog.IsExplored(tile) && !(tile.x <= 26 && tile.y <= 18);
            Assert.AreEqual(seenRock ? TileState.Designated : (tile.x <= 26 && tile.y <= 18 ? TileState.Floor : TileState.Rock),
                board.GetTile(tile.x, tile.y), $"{tile}");
        }
    }

    [Test]
    public void ApplyRect_Designate_AllFog_ChangesNothing()
    {
        fog.RevealAround(CavernCenter);
        int changed = DigDesignator.ApplyRect(board, fog, new RectInt(0, 0, 10, 10), DigDesignator.DesignateMode.Designate);
        Assert.AreEqual(0, changed);
        Assert.AreEqual(TileState.Rock, board.GetTile(0, 0));
    }

    [Test]
    public void ApplyRect_Clear_OverDesignatedTiles_IsNeverBlocked()
    {
        fog.RevealAround(CavernCenter);
        DigDesignator.ApplyRect(board, fog, FrontierRect, DigDesignator.DesignateMode.Designate);

        int cleared = DigDesignator.ApplyRect(board, fog, FrontierRect, DigDesignator.DesignateMode.Clear);

        Assert.AreEqual(5, cleared);
        Assert.AreEqual(TileState.Rock, board.GetTile(27, 19));
    }

    [Test]
    public void ApplyRect_WithoutFog_BehavesAsBefore()
    {
        // Same rect, no FogOfWar: every Rock tile in it designates (30 tiles minus the 4 Floor = 26).
        Assert.AreEqual(26, DigDesignator.ApplyRect(board, null, FrontierRect, DigDesignator.DesignateMode.Designate));
        Assert.AreEqual(26, DigDesignator.ApplyRect(board, null, FrontierRect, DigDesignator.DesignateMode.Clear));
        Assert.AreEqual(26, DigDesignator.ApplyRect(board, FrontierRect, DigDesignator.DesignateMode.Designate), "The fogless overload");
    }

    // ---- 5. Preview eligibility == apply eligibility ----

    private static readonly RectInt[] StagedRects =
    {
        FrontierRect,               // frontier-crossing
        new RectInt(0, 0, 5, 5),    // corner, all fog
        new RectInt(40, 26, 10, 8), // max corner, partly seen (revealed below), clipped by the board edge
        new RectInt(21, 13, 7, 7),  // fully seen, cavern floor plus its rock rim
        new RectInt(22, 25, 5, 7),  // the entrance column, all fog
    };

    [Test]
    public void PreviewSet_EqualsTheSetApplyRectChanges_ForStagedRects()
    {
        fog.RevealAround(CavernCenter);
        fog.RevealAround(new Vector2Int(47, 31)); // Seen at the max corner: x 44..47, y 28..31

        var preview = new List<Vector2Int>();
        foreach (RectInt rect in StagedRects)
        {
            DigDesignator.EligibleTiles(board, fog, rect, DigDesignator.DesignateMode.Designate, preview);
            var expected = new HashSet<Vector2Int>(preview);

            var before = new Dictionary<Vector2Int, TileState>();
            foreach (Vector2Int tile in TilesIn(rect)) before[tile] = board.GetTile(tile.x, tile.y);

            int changed = DigDesignator.ApplyRect(board, fog, rect, DigDesignator.DesignateMode.Designate);

            var actual = new HashSet<Vector2Int>();
            foreach (Vector2Int tile in TilesIn(rect))
                if (board.GetTile(tile.x, tile.y) != before[tile]) actual.Add(tile);

            Assert.AreEqual(expected.Count, changed, $"{rect}: count");
            Assert.IsTrue(expected.SetEquals(actual), $"{rect}: preview and apply sets differ");

            DigDesignator.ApplyRect(board, fog, rect, DigDesignator.DesignateMode.Clear); // Reset for the next rect
        }
    }

    [Test]
    public void EligibleTiles_NoFog_IsEveryRockTile()
    {
        var preview = new List<Vector2Int>();
        DigDesignator.EligibleTiles(board, null, new RectInt(20, 12, 3, 3), DigDesignator.DesignateMode.Designate, preview);
        Assert.AreEqual(5, preview.Count, "The 9-tile rect minus 4 cavern Floor tiles");
    }

    // ---- 6. Tooltip suppression ----

    [Test]
    public void TooltipLabel_Unexplored_IsNoTooltip_Seen_IsTodaysLabel()
    {
        fog.RevealAround(CavernCenter);

        Assert.IsNull(TileHover.TooltipLabel(fog, TileState.Rock, new Vector2Int(0, 0)));
        Assert.IsNull(TileHover.TooltipLabel(fog, TileState.Entrance, Entrance));
        Assert.AreEqual("Rock (27, 19)", TileHover.TooltipLabel(fog, TileState.Rock, new Vector2Int(27, 19)));
        Assert.AreEqual("Floor (24, 16)", TileHover.TooltipLabel(fog, TileState.Floor, CavernCenter));
        Assert.AreEqual("Entrance (24, 31)", TileHover.TooltipLabel(null, TileState.Entrance, Entrance), "No fog: as today");
    }

    // ---- 7. Style resolution ----

    private BoardRenderer AddRenderer()
    {
        BoardRenderer boardRenderer = managerGo.AddComponent<BoardRenderer>();
        boardRenderer.Board = board;
        return boardRenderer;
    }

    [Test]
    public void Style_UnexploredWinsOverState_ForAllFourStates()
    {
        BoardRenderer boardRenderer = AddRenderer();
        Color fogColor = boardRenderer.ColorFor(TileState.Rock, false);
        BoardRenderer.TileShape fogShape = boardRenderer.ShapeFor(TileState.Rock, false);
        Assert.AreEqual(0.6f, fogShape.Scale.y, 1e-6f, "A block at rock height");
        Assert.AreEqual(0f, fogShape.BottomY, 1e-6f, "Standing on the ground");
        Assert.AreNotEqual(boardRenderer.ColorFor(TileState.Rock), fogColor, "Fog is its own tone, not rock");

        foreach (TileState state in System.Enum.GetValues(typeof(TileState)))
        {
            Assert.AreEqual(fogColor, boardRenderer.ColorFor(state, false), $"{state}: one fog color");
            Assert.AreEqual(fogShape.Scale, boardRenderer.ShapeFor(state, false).Scale, $"{state}: one fog shape");
            Assert.AreEqual(fogShape.CenterY, boardRenderer.ShapeFor(state, false).CenterY, $"{state}");
            Assert.IsFalse(BoardRenderer.ShowsDoor(state, false), $"{state}: no door in the fog");

            Assert.AreEqual(boardRenderer.ColorFor(state), boardRenderer.ColorFor(state, true), $"{state}: explored is today's color");
            Assert.AreEqual(boardRenderer.ShapeFor(state).Scale, boardRenderer.ShapeFor(state, true).Scale, $"{state}: explored is today's shape");
        }
        Assert.IsTrue(BoardRenderer.ShowsDoor(TileState.Entrance, true));
    }

    [Test]
    public void Views_FogTheUnexplored_HideTheDoor_AndRestyleOnReveal()
    {
        BoardRenderer boardRenderer = AddRenderer();
        boardRenderer.Fog = fog;
        fog.RevealAround(CavernCenter);
        boardRenderer.RenderFullBoard();

        Material fogMaterial = boardRenderer.MaterialFor(boardRenderer.ColorFor(TileState.Rock, false));
        Assert.AreSame(fogMaterial, boardRenderer.GetTileView(0, 0).GetComponent<MeshRenderer>().sharedMaterial, "Unexplored rock");
        Assert.AreSame(fogMaterial, boardRenderer.GetTileView(24, 31).GetComponent<MeshRenderer>().sharedMaterial, "Unexplored entrance");
        Assert.AreEqual(0.6f, boardRenderer.GetTileView(24, 31).transform.localScale.y, 1e-6f, "The entrance is a fog block");
        Assert.IsNull(boardRenderer.GetDoorView(24, 31), "No door is built in the fog");
        Assert.AreSame(boardRenderer.MaterialFor(boardRenderer.ColorFor(TileState.Floor)),
            boardRenderer.GetTileView(24, 16).GetComponent<MeshRenderer>().sharedMaterial, "Seen cavern floor as today");
        Assert.AreEqual(0.6f, boardRenderer.GetTileSurfaceHeight(24, 31), 1e-6f, "The hover sits on the fog block");

        // The imp's sight reaches the door: the reveal notification restyles it on the spot.
        fog.RevealAround(new Vector2Int(24, 28));

        Assert.AreSame(boardRenderer.MaterialFor(boardRenderer.ColorFor(TileState.Entrance)),
            boardRenderer.GetTileView(24, 31).GetComponent<MeshRenderer>().sharedMaterial);
        Assert.AreEqual(0.1f, boardRenderer.GetTileView(24, 31).transform.localScale.y, 1e-6f, "A slab again");
        Assert.IsNotNull(boardRenderer.GetDoorView(24, 31));
        Assert.IsTrue(boardRenderer.GetDoorView(24, 31).activeSelf, "The real door, once explored");
        Assert.AreSame(boardRenderer.MaterialFor(boardRenderer.ColorFor(TileState.Rock)),
            boardRenderer.GetTileView(24, 30).GetComponent<MeshRenderer>().sharedMaterial, "Seen rock as today");
    }

    [Test]
    public void Views_StateChangeOnASeenTile_StillRestyles()
    {
        BoardRenderer boardRenderer = AddRenderer();
        boardRenderer.Fog = fog;
        fog.RevealAround(CavernCenter);
        boardRenderer.RenderFullBoard();

        board.SetTile(27, 15, TileState.Designated);
        boardRenderer.RefreshTile(27, 15); // EditMode doesn't run OnEnable, so the OnTileChanged hookup is stepped here

        Assert.AreSame(boardRenderer.MaterialFor(boardRenderer.ColorFor(TileState.Designated)),
            boardRenderer.GetTileView(27, 15).GetComponent<MeshRenderer>().sharedMaterial);
    }

    [Test]
    public void RaycastBoard_FoggedEntrance_IsPickedLikeTheBlockItLooksLike()
    {
        BoardRenderer boardRenderer = AddRenderer();
        boardRenderer.Fog = fog;
        fog.RevealAround(CavernCenter);

        // A 45° ray from the south onto the fog block's top centre. As a ground slab the door tile would let it pass
        // and land past the board edge (z + 0.6); as the block on screen, it's picked.
        Vector3 c = boardRenderer.GetTileCenterWorldPosition(24, 31);
        Vector3 top = new Vector3(c.x, boardRenderer.RockHeight, c.z);
        Vector3 dir = new Vector3(0f, -1f, 1f).normalized;
        Assert.IsTrue(boardRenderer.RaycastBoard(new Ray(top - dir * 10f, dir), out Vector3 hit));
        Assert.AreEqual(Entrance, boardRenderer.WorldToBoardCoords(hit));

        fog.RevealAround(new Vector2Int(24, 28)); // Seen: a slab again, so the same ray passes over it
        Assert.IsTrue(boardRenderer.RaycastBoard(new Ray(top - dir * 10f, dir), out hit));
        Assert.AreEqual(new Vector2Int(24, 32), boardRenderer.WorldToBoardCoords(hit), "Picking as today once seen");
    }

    [Test]
    public void Renderer_WithoutFog_RendersEverythingAsExplored()
    {
        BoardRenderer boardRenderer = AddRenderer();
        boardRenderer.RenderFullBoard();

        Assert.IsTrue(boardRenderer.IsExplored(24, 31));
        Assert.IsNotNull(boardRenderer.GetDoorView(24, 31), "The door as today");
        Assert.AreSame(boardRenderer.MaterialFor(boardRenderer.ColorFor(TileState.Rock)),
            boardRenderer.GetTileView(0, 0).GetComponent<MeshRenderer>().sharedMaterial);
    }
}
