using NUnit.Framework;
using UnityEngine;

public class BoardRendererTests
{
    private GameObject managerGo;
    private DungeonBoard board;
    private BoardRenderer boardRenderer;

    [SetUp]
    public void SetUp()
    {
        // Default board: 48x32 Rock with the 6x6 Floor cavern at (21,13)-(26,18) and the door at (24,31).
        managerGo = new GameObject("TestDungeonManager");
        board = managerGo.AddComponent<DungeonBoard>();
        board.InitializeBoard();
        boardRenderer = managerGo.AddComponent<BoardRenderer>();
        boardRenderer.Board = board;
    }

    [TearDown]
    public void TearDown()
    {
        if (managerGo != null) Object.DestroyImmediate(managerGo);
    }

    // --- Mapping: the ground is the XZ plane (y = 0) ---

    [Test]
    public void BoardOrigin_IsCenteredOnTheGround()
    {
        Assert.AreEqual(new Vector3(-24f, 0f, -16f), boardRenderer.BoardOrigin);
    }

    [Test]
    public void TileCenters_LieOnTheGroundPlane()
    {
        Assert.AreEqual(new Vector3(-23.5f, 0f, -15.5f), boardRenderer.GetTileCenterWorldPosition(0, 0));
        Assert.AreEqual(new Vector3(0.5f, 0f, 0.5f), boardRenderer.GetTileCenterWorldPosition(24, 16));
        Assert.AreEqual(new Vector3(23.5f, 0f, 15.5f), boardRenderer.GetTileCenterWorldPosition(47, 31));
        Assert.AreEqual(new Vector3(-24f, 0f, -16f), boardRenderer.GetTileWorldPosition(0, 0), "Tile corner");
    }

    [Test]
    public void WorldToBoardCoords_IsTheExactInverse_ForEveryTile()
    {
        for (int x = 0; x < board.Width; x++)
            for (int y = 0; y < board.Height; y++)
                Assert.AreEqual(new Vector2Int(x, y), boardRenderer.WorldToBoardCoords(boardRenderer.GetTileCenterWorldPosition(x, y)));
    }

    [Test]
    public void WorldToBoardCoords_IgnoresHeight_AndFloorsCellEdges()
    {
        Assert.AreEqual(new Vector2Int(24, 16), boardRenderer.WorldToBoardCoords(new Vector3(0.5f, 3f, 0.5f)), "A point above the tile");
        Assert.AreEqual(new Vector2Int(24, 16), boardRenderer.WorldToBoardCoords(new Vector3(0f, 0f, 0f)), "Cell's min corner belongs to it");
        Assert.AreEqual(new Vector2Int(23, 15), boardRenderer.WorldToBoardCoords(new Vector3(-0.01f, 0f, -0.01f)));
    }

    [Test]
    public void UnanchoredTileCenter_IsOnTheGround()
    {
        Assert.AreEqual(new Vector3(3.5f, 0f, 7.5f), BoardRenderer.UnanchoredTileCenter(new Vector2Int(3, 7)));
    }

    // --- State -> shape/color ---

    [Test]
    public void Floor_IsAFlatSlabTopAtTheGround()
    {
        BoardRenderer.TileShape floor = boardRenderer.ShapeFor(TileState.Floor);
        Assert.AreEqual(new Vector3(1f, 0.1f, 1f), floor.Scale);
        Assert.AreEqual(0f, floor.TopY, 1e-6f, "Top face is the ground");
        Assert.AreEqual(floor.Scale, boardRenderer.ShapeFor(TileState.Entrance).Scale, "The entrance is a floor slab");
    }

    [Test]
    public void Rock_IsABlockStandingOnTheGround_DesignatedMatchesIt()
    {
        BoardRenderer.TileShape rock = boardRenderer.ShapeFor(TileState.Rock);
        Assert.AreEqual(new Vector3(1f, 0.6f, 1f), rock.Scale, "Default rockHeight 0.6");
        Assert.AreEqual(0f, rock.BottomY, 1e-6f);
        Assert.AreEqual(0.6f, rock.TopY, 1e-6f);

        BoardRenderer.TileShape designated = boardRenderer.ShapeFor(TileState.Designated);
        Assert.AreEqual(rock.Scale, designated.Scale);
        Assert.AreNotEqual(boardRenderer.ColorFor(TileState.Rock), boardRenderer.ColorFor(TileState.Designated), "Amber tint");
    }

    [Test]
    public void RockHeight_DrivesTheBlockAndTheSurface()
    {
        boardRenderer.RockHeight = 0.9f;
        Assert.AreEqual(0.9f, boardRenderer.ShapeFor(TileState.Rock).Scale.y, 1e-6f);
        Assert.AreEqual(0.9f, boardRenderer.GetTileSurfaceHeight(0, 0), 1e-6f, "Rock tile");
        Assert.AreEqual(0f, boardRenderer.GetTileSurfaceHeight(24, 16), "Cavern floor");
        Assert.AreEqual(0f, boardRenderer.GetTileSurfaceHeight(24, 31), "Entrance");
        Assert.AreEqual(0f, boardRenderer.GetTileSurfaceHeight(-1, 0), "Off the board");
    }

    // --- Views ---

    [Test]
    public void RenderFullBoard_BuildsOneViewPerTile_StyledByState()
    {
        boardRenderer.RenderFullBoard();

        Assert.AreEqual(board.Width * board.Height + 1, boardRenderer.ViewRoot.childCount, "1,536 tiles plus the door");

        Transform rock = boardRenderer.GetTileView(0, 0).transform;
        Assert.AreEqual(new Vector3(1f, 0.6f, 1f), rock.localScale);
        Assert.AreEqual(new Vector3(-23.5f, 0.3f, -15.5f), rock.position, "Block bottom on the ground");

        Transform floor = boardRenderer.GetTileView(24, 16).transform;
        Assert.AreEqual(new Vector3(1f, 0.1f, 1f), floor.localScale);
        Assert.AreEqual(new Vector3(0.5f, -0.05f, 0.5f), floor.position, "Slab top at y = 0");
    }

    [Test]
    public void RefreshTile_RestylesTheSameView_NeverRecreates()
    {
        boardRenderer.RenderFullBoard();
        GameObject view = boardRenderer.GetTileView(27, 15);
        var meshRenderer = view.GetComponent<MeshRenderer>();
        Assert.AreSame(boardRenderer.MaterialFor(boardRenderer.ColorFor(TileState.Rock)), meshRenderer.sharedMaterial);

        board.SetTile(27, 15, TileState.Designated);
        boardRenderer.RefreshTile(27, 15);
        Assert.AreSame(view, boardRenderer.GetTileView(27, 15));
        Assert.AreSame(boardRenderer.MaterialFor(boardRenderer.ColorFor(TileState.Designated)), meshRenderer.sharedMaterial, "Restyled amber");
        Assert.AreEqual(0.6f, view.transform.localScale.y, 1e-6f, "Still a block");

        board.SetTile(27, 15, TileState.Floor);
        boardRenderer.RefreshTile(27, 15);
        Assert.AreSame(view, boardRenderer.GetTileView(27, 15));
        Assert.AreSame(boardRenderer.MaterialFor(boardRenderer.ColorFor(TileState.Floor)), meshRenderer.sharedMaterial);
        Assert.AreEqual(0.1f, view.transform.localScale.y, 1e-6f, "Dug out to a slab");
        Assert.AreEqual(0f, view.transform.position.y + view.transform.localScale.y * 0.5f, 1e-6f, "Top at the ground");
    }

    // --- Lit materials (#75) ---

    [Test]
    public void MaterialFor_OneLitMaterialPerColor_CarryingThatColor()
    {
        Color rock = boardRenderer.ColorFor(TileState.Rock);
        Material a = boardRenderer.MaterialFor(rock);
        Assert.AreSame(a, boardRenderer.MaterialFor(rock), "Cached per color");
        Assert.AreNotSame(a, boardRenderer.MaterialFor(boardRenderer.ColorFor(TileState.Floor)));
        Assert.AreEqual(BoardRenderer.LitShaderName, a.shader.name);
        Color baseColor = a.GetColor("_BaseColor");
        Assert.AreEqual(rock.r, baseColor.r, 1e-4f);
        Assert.AreEqual(rock.g, baseColor.g, 1e-4f);
        Assert.AreEqual(rock.b, baseColor.b, 1e-4f);
    }

    [Test]
    public void Views_CastAndReceiveShadows_AndShareOneLitMaterialPerState()
    {
        boardRenderer.RenderFullBoard();

        var rock = boardRenderer.GetTileView(0, 0).GetComponent<MeshRenderer>();
        var otherRock = boardRenderer.GetTileView(1, 0).GetComponent<MeshRenderer>();
        Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.On, rock.shadowCastingMode);
        Assert.IsTrue(rock.receiveShadows);
        Assert.AreSame(rock.sharedMaterial, otherRock.sharedMaterial, "Same state, same material");

        var floor = boardRenderer.GetTileView(24, 16).GetComponent<MeshRenderer>();
        Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.On, floor.shadowCastingMode);
        Assert.IsTrue(floor.receiveShadows);

        Mesh mesh = boardRenderer.GetTileView(0, 0).GetComponent<MeshFilter>().sharedMesh;
        Assert.AreEqual(24, mesh.vertexCount, "Unit box, 4 vertices per face");
        Assert.AreEqual(24, mesh.normals.Length, "Per-face normals for lighting");
        Assert.AreEqual(0, mesh.colors.Length, "No baked vertex shading");
        Assert.AreSame(mesh, boardRenderer.GetTileView(24, 16).GetComponent<MeshFilter>().sharedMesh, "One shared box mesh");
    }

    [Test]
    public void BoxFaces_FaceOutward()
    {
        boardRenderer.RenderFullBoard();
        Mesh mesh = boardRenderer.GetTileView(0, 0).GetComponent<MeshFilter>().sharedMesh;
        Vector3[] v = mesh.vertices;
        Vector3[] n = mesh.normals;
        int[] t = mesh.triangles;
        for (int i = 0; i < t.Length; i += 3)
        {
            // Unity front faces wind clockwise seen from outside, so Cross(b - a, c - a) points along the outward normal.
            Vector3 cross = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
            Assert.Greater(Vector3.Dot(cross, n[t[i]]), 0f, $"Triangle {i / 3} faces outward");
        }
    }

    // --- Picking: the tile the ray actually lands on ---

    [Test]
    public void RaycastBoard_StraightDown_PicksTheTileBelow()
    {
        Vector3 c = boardRenderer.GetTileCenterWorldPosition(27, 15);
        Assert.IsTrue(boardRenderer.RaycastBoard(new Ray(c + Vector3.up * 10f, Vector3.down), out Vector3 p));
        Assert.AreEqual(new Vector2Int(27, 15), boardRenderer.WorldToBoardCoords(p));
        Assert.AreEqual(0f, p.y);
    }

    [Test]
    public void RaycastBoard_PitchedRayOntoARockTop_PicksThatRock_NotTheTileBehind()
    {
        // 45° down toward +Z, aimed at the far half of rock (27,15)'s top face (z + 0.3 from its center, at y = 0.6).
        // A plain ground raycast continues 0.6 further and lands in (27,16), the tile behind.
        Vector3 c = boardRenderer.GetTileCenterWorldPosition(27, 15);
        Vector3 onTop = new Vector3(c.x, 0.6f, c.z + 0.3f);
        Vector3 dir = new Vector3(0f, -1f, 1f).normalized;
        var ray = new Ray(onTop - dir * 20f, dir);

        Assert.IsTrue(TileHover.RaycastGroundPlane(ray, out Vector3 ground));
        Assert.AreEqual(new Vector2Int(27, 16), boardRenderer.WorldToBoardCoords(ground), "The plain ground hit is behind");

        Assert.IsTrue(boardRenderer.RaycastBoard(ray, out Vector3 picked));
        Assert.AreEqual(new Vector2Int(27, 15), boardRenderer.WorldToBoardCoords(picked));
    }

    [Test]
    public void RaycastBoard_PitchedRayOntoFloor_PicksTheFloorTile()
    {
        // Floor (24,16) inside the cavern: no block in the way, so the ground hit stands.
        Vector3 c = boardRenderer.GetTileCenterWorldPosition(24, 16);
        Vector3 dir = new Vector3(0f, -1f, 1f).normalized;
        var ray = new Ray(c - dir * 20f, dir);

        Assert.IsTrue(boardRenderer.RaycastBoard(ray, out Vector3 picked));
        Assert.AreEqual(new Vector2Int(24, 16), boardRenderer.WorldToBoardCoords(picked));
    }

    [Test]
    public void RaycastBoard_RayIntoARockFrontFace_PicksThatRock()
    {
        // Cavern floor (24,18) is the last floor row; rock (24,19) stands behind it. A ray hitting the rock's
        // near (-Z) face low down would reach the ground inside (24,19) or beyond; it must pick (24,19).
        Vector3 rockCenter = boardRenderer.GetTileCenterWorldPosition(24, 19);
        Vector3 onFace = new Vector3(rockCenter.x, 0.2f, rockCenter.z - 0.5f);
        Vector3 dir = new Vector3(0f, -1f, 1f).normalized;
        var ray = new Ray(onFace - dir * 20f, dir);

        Assert.IsTrue(boardRenderer.RaycastBoard(ray, out Vector3 picked));
        Assert.AreEqual(new Vector2Int(24, 19), boardRenderer.WorldToBoardCoords(picked));
    }
}
