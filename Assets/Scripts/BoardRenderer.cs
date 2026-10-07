using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The board's 3D view and the single source of tile ↔ world mapping (ticket #74). The ground is the XZ
/// plane at y = 0 (Y up): tile (x, y) has its center — its ground point — at BoardOrigin + (x + 0.5, 0, y + 0.5),
/// with the origin at (−Width/2, 0, −Height/2). Each tile is one code-built box view, created once under a
/// code-made root and restyled from OnTileChanged (never destroyed/recreated): Floor a thin slab whose top is
/// the ground, Rock/Designated a raised block, Entrance a slab with a simple box-built door.
/// Lit (ticket #75): every box is a URP Lit surface, one shared material per color, casting and receiving
/// shadows, so faces shade from the scene's light instead of baked tints.
/// </summary>
public class BoardRenderer : MonoBehaviour
{
    public const string LitShaderName = "Universal Render Pipeline/Lit";
    public const float DefaultOverlayLift = 0.01f; // overlayLift's default; also the lift for code without a renderer

    /// <summary>The overlay lift of a renderer, or the default when there's none (tests, unwired objects).</summary>
    public static float OverlayLiftOf(BoardRenderer renderer) => renderer != null ? renderer.OverlayLift : DefaultOverlayLift;
    private const int PickSamples = 32; // Ray steps between the block-top plane and the ground in RaycastBoard

    [Header("References")]
    [SerializeField] private DungeonBoard dungeonBoard;

    [Header("Tile Shapes")]
    [SerializeField] private float rockHeight = 0.6f;     // Rock and designated blocks, in tiles
    [SerializeField] private float floorThickness = 0.1f; // Floor slab; its top face is the ground (y = 0)
    [SerializeField] private float overlayLift = DefaultOverlayLift; // Highlight, previews, ground shadows sit this far above a surface

    [Header("Tile Colors (Placeholder Art)")]
    [SerializeField] private Color rockColor = new Color(0.22f, 0.22f, 0.22f, 1f);       // Dark gray
    [SerializeField] private Color floorColor = new Color(0.48f, 0.32f, 0.18f, 1f);      // Brown
    [SerializeField] private Color designatedColor = new Color(0.85f, 0.65f, 0.15f, 1f); // Amber/Gold
    [SerializeField] private Color doorPanelColor = new Color(0.72f, 0.46f, 0.22f, 1f);  // Warm wood, lighter than Floor
    [SerializeField] private Color doorFrameColor = new Color(0.16f, 0.09f, 0.04f, 1f);  // Near-black frame and lintel

    [Header("Door (Placeholder Art)")]
    [SerializeField] private float doorHeight = 0.9f;  // Taller than the rock so the entrance reads from afar
    [SerializeField] private float doorPostWidth = 0.12f;

    [Header("Surface")]
    [SerializeField] private float surfaceSmoothness = 0f; // Lit materials: matte rock and earth (no highlights, no reflections)

    public DungeonBoard Board { get => dungeonBoard; set => dungeonBoard = value; }
    public float RockHeight { get => rockHeight; set => rockHeight = value; }
    public float FloorThickness => floorThickness;
    public float OverlayLift => overlayLift;
    public Vector3 BoardOrigin => dungeonBoard != null
        ? new Vector3(-dungeonBoard.Width * 0.5f, 0f, -dungeonBoard.Height * 0.5f)
        : Vector3.zero;

    /// <summary>Parent of the tile views; null until RenderFullBoard has run (Start).</summary>
    public Transform ViewRoot => viewRoot;

    /// <summary>How a tile state is shaped: a unit box's scale and the height of its center.</summary>
    public struct TileShape
    {
        public Vector3 Scale;
        public float CenterY;
        public TileShape(Vector3 scale, float centerY) { Scale = scale; CenterY = centerY; }
        public float TopY => CenterY + Scale.y * 0.5f;
        public float BottomY => CenterY - Scale.y * 0.5f;
    }

    private Transform viewRoot;
    private GameObject[,] tileViews;
    private readonly Dictionary<Vector2Int, GameObject> doors = new Dictionary<Vector2Int, GameObject>();
    private readonly Dictionary<Color, Material> materials = new Dictionary<Color, Material>();
    private Mesh boxMesh;

    private void Awake()
    {
        InitializeReferences();
    }

    private void OnEnable()
    {
        if (dungeonBoard != null)
        {
            dungeonBoard.OnTileChanged += HandleTileChanged;
        }
    }

    private void OnDisable()
    {
        if (dungeonBoard != null)
        {
            dungeonBoard.OnTileChanged -= HandleTileChanged;
        }
    }

    private void Start()
    {
        InitializeReferences();
        RenderFullBoard();
    }

    private void OnDestroy()
    {
        foreach (Material material in materials.Values) DestroyRuntimeObject(material);
        materials.Clear();
        DestroyRuntimeObject(boxMesh); // The views are children of this object and go with it
    }

    public void InitializeReferences()
    {
        dungeonBoard ??= GetComponent<DungeonBoard>() ?? FindAnyObjectByType<DungeonBoard>();
    }

    // ---- shapes and colors (pure: what each state looks like) ----

    public static bool IsRaised(TileState state) => state == TileState.Rock || state == TileState.Designated;

    /// <summary>Floor and Entrance: a slab with its top at the ground. Rock and Designated: a block standing on it.</summary>
    public TileShape ShapeFor(TileState state) =>
        IsRaised(state)
            ? new TileShape(new Vector3(1f, rockHeight, 1f), rockHeight * 0.5f)
            : new TileShape(new Vector3(1f, floorThickness, 1f), -floorThickness * 0.5f);

    public Color ColorFor(TileState state)
    {
        switch (state)
        {
            case TileState.Floor:      return floorColor;
            case TileState.Designated: return designatedColor;
            case TileState.Entrance:   return floorColor; // The slab; the door on it is separate geometry
            default:                   return rockColor;
        }
    }

    /// <summary>Height of a tile's top surface: the block top for raised tiles, the ground (0) otherwise.</summary>
    public float GetTileSurfaceHeight(int x, int y) =>
        dungeonBoard != null && dungeonBoard.IsInBounds(x, y) && IsRaised(dungeonBoard.GetTile(x, y)) ? rockHeight : 0f;

    // ---- views ----

    public void RenderFullBoard()
    {
        if (dungeonBoard == null) return;

        EnsureViews();
        for (int x = 0; x < dungeonBoard.Width; x++)
        {
            for (int y = 0; y < dungeonBoard.Height; y++)
            {
                RefreshTile(x, y);
            }
        }
    }

    public void RefreshTile(int x, int y)
    {
        if (dungeonBoard == null || tileViews == null) return;
        if (!dungeonBoard.IsInBounds(x, y)) return;

        TileState state = dungeonBoard.GetTile(x, y);
        TileShape shape = ShapeFor(state);
        Vector3 center = GetTileCenterWorldPosition(x, y);

        GameObject view = tileViews[x, y];
        view.transform.position = new Vector3(center.x, shape.CenterY, center.z);
        view.transform.localScale = shape.Scale;
        view.GetComponent<MeshRenderer>().sharedMaterial = MaterialFor(ColorFor(state));

        Vector2Int tile = new Vector2Int(x, y);
        bool isEntrance = state == TileState.Entrance;
        if (isEntrance && !doors.ContainsKey(tile)) doors[tile] = CreateDoor(center);
        if (doors.TryGetValue(tile, out GameObject door)) door.SetActive(isEntrance);
    }

    /// <summary>The tile's view object (its box), or null before RenderFullBoard / out of bounds.</summary>
    public GameObject GetTileView(int x, int y) =>
        tileViews != null && dungeonBoard != null && dungeonBoard.IsInBounds(x, y) ? tileViews[x, y] : null;

    /// <summary>The shared lit material for a color: one per distinct color, created on first use.</summary>
    public Material MaterialFor(Color color)
    {
        if (materials.TryGetValue(color, out Material cached) && cached != null) return cached;

        Shader shader = Shader.Find(LitShaderName) ?? Shader.Find("Universal Render Pipeline/Simple Lit");
        var material = new Material(shader) { name = "Tile " + ColorUtility.ToHtmlStringRGB(color) };
        material.SetColor("_BaseColor", color); // Material colors are sRGB-authored; Unity converts for linear
        material.SetFloat("_Smoothness", surfaceSmoothness);
        // Matte surfaces: no specular highlights or environment (skybox) reflections on a dungeon floor.
        material.SetFloat("_SpecularHighlights", 0f);
        material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        material.SetFloat("_EnvironmentReflections", 0f);
        material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        materials[color] = material;
        return material;
    }

    /// <summary>The door built on an Entrance tile (active while the tile is the Entrance), else null.</summary>
    public GameObject GetDoorView(int x, int y) =>
        doors.TryGetValue(new Vector2Int(x, y), out GameObject door) ? door : null;

    private void HandleTileChanged(int x, int y, TileState newState)
    {
        RefreshTile(x, y);
    }

    private void EnsureViews()
    {
        int width = dungeonBoard.Width;
        int height = dungeonBoard.Height;
        if (tileViews != null && tileViews.GetLength(0) == width && tileViews.GetLength(1) == height) return;

        if (viewRoot == null)
        {
            viewRoot = new GameObject("BoardView").transform;
            viewRoot.SetParent(transform, false);
        }

        tileViews = new GameObject[width, height];
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                tileViews[x, y] = CreateBox($"Tile ({x}, {y})", viewRoot, rockColor);
            }
        }
    }

    // A minimal door from boxes: two posts and a lintel in the frame color, a plank panel between them.
    private GameObject CreateDoor(Vector3 groundPoint)
    {
        var door = new GameObject("Door");
        door.transform.SetParent(viewRoot, false);
        door.transform.position = groundPoint;

        float halfSpan = 0.5f - doorPostWidth * 0.5f;
        float panelWidth = 1f - doorPostWidth * 2f;
        AddDoorPart(door, "PostLeft", doorFrameColor, new Vector3(-halfSpan, doorHeight * 0.5f, 0f), new Vector3(doorPostWidth, doorHeight, doorPostWidth));
        AddDoorPart(door, "PostRight", doorFrameColor, new Vector3(halfSpan, doorHeight * 0.5f, 0f), new Vector3(doorPostWidth, doorHeight, doorPostWidth));
        AddDoorPart(door, "Lintel", doorFrameColor, new Vector3(0f, doorHeight - doorPostWidth * 0.5f, 0f), new Vector3(1f, doorPostWidth, doorPostWidth));
        float panelHeight = doorHeight - doorPostWidth;
        AddDoorPart(door, "Panel", doorPanelColor, new Vector3(0f, panelHeight * 0.5f, 0f), new Vector3(panelWidth, panelHeight, doorPostWidth * 0.5f));
        return door;
    }

    private void AddDoorPart(GameObject door, string partName, Color color, Vector3 localPosition, Vector3 scale)
    {
        GameObject part = CreateBox(partName, door.transform, color);
        part.transform.localPosition = localPosition;
        part.transform.localScale = scale;
    }

    private GameObject CreateBox(string boxName, Transform parent, Color color)
    {
        var go = new GameObject(boxName);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = BoxMesh();
        var meshRenderer = go.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = MaterialFor(color);
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        meshRenderer.receiveShadows = true;
        return go;
    }

    // One unit box centered on the origin, shared by every view: per-face normals for lighting, UVs per face.
    private Mesh BoxMesh()
    {
        if (boxMesh != null) return boxMesh;

        var vertices = new List<Vector3>(24);
        var normals = new List<Vector3>(24);
        var uvs = new List<Vector2>(24);
        var triangles = new List<int>(36);
        AddFace(vertices, normals, uvs, triangles, Vector3.up, Vector3.right, Vector3.forward);
        AddFace(vertices, normals, uvs, triangles, Vector3.down, Vector3.right, Vector3.back);
        AddFace(vertices, normals, uvs, triangles, Vector3.back, Vector3.right, Vector3.up);
        AddFace(vertices, normals, uvs, triangles, Vector3.forward, Vector3.left, Vector3.up);
        AddFace(vertices, normals, uvs, triangles, Vector3.right, Vector3.forward, Vector3.up);
        AddFace(vertices, normals, uvs, triangles, Vector3.left, Vector3.back, Vector3.up);

        boxMesh = new Mesh { name = "TileBox" };
        boxMesh.SetVertices(vertices);
        boxMesh.SetNormals(normals);
        boxMesh.SetUVs(0, uvs);
        boxMesh.SetTriangles(triangles, 0);
        boxMesh.RecalculateBounds();
        boxMesh.RecalculateTangents();
        return boxMesh;
    }

    // One outward face: u x v = -normal, so the two triangles wind clockwise seen from outside (Unity's front face).
    private static void AddFace(List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles,
        Vector3 normal, Vector3 u, Vector3 v)
    {
        int start = vertices.Count;
        Vector3 c = normal * 0.5f;
        vertices.Add(c - u * 0.5f - v * 0.5f);
        vertices.Add(c - u * 0.5f + v * 0.5f);
        vertices.Add(c + u * 0.5f + v * 0.5f);
        vertices.Add(c + u * 0.5f - v * 0.5f);
        for (int i = 0; i < 4; i++) normals.Add(normal);
        uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(0f, 1f)); uvs.Add(new Vector2(1f, 1f)); uvs.Add(new Vector2(1f, 0f));

        triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
        triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
    }

    private static void DestroyRuntimeObject(Object obj)
    {
        if (obj == null) return;
        if (Application.isPlaying) Destroy(obj);
        else DestroyImmediate(obj);
    }

    // ---- mapping (the single source of tile ↔ world) ----

    /// <summary>A tile's center on the ground for code without a renderer: the board origin taken as (0, 0, 0).</summary>
    public static Vector3 UnanchoredTileCenter(Vector2Int tile) => new Vector3(tile.x + 0.5f, 0f, tile.y + 0.5f);

    public Vector3 GetTileWorldPosition(int x, int y) => BoardOrigin + new Vector3(x, 0f, y);

    /// <summary>The ground point (y = 0) at the center of tile (x, y).</summary>
    public Vector3 GetTileCenterWorldPosition(int x, int y) => BoardOrigin + new Vector3(x + 0.5f, 0f, y + 0.5f);

    /// <summary>Exact inverse of the mapping: the tile whose ground cell contains the point (its y is ignored).</summary>
    public Vector2Int WorldToBoardCoords(Vector3 worldPosition)
    {
        Vector3 origin = BoardOrigin;
        return new Vector2Int(
            Mathf.FloorToInt(worldPosition.x - origin.x),
            Mathf.FloorToInt(worldPosition.z - origin.z)
        );
    }

    /// <summary>
    /// Picks the tile a screen ray actually lands on. A plain ground raycast lands behind a raised block (it
    /// passes over the block's top to y = 0), so this walks the ray from the block-top plane down to the ground
    /// and returns the first point inside a raised tile's column — its top or near face — else the ground hit.
    /// The result is a ground-plane point inside the picked tile, ready for WorldToBoardCoords.
    /// </summary>
    public bool RaycastBoard(Ray ray, out Vector3 groundPoint)
    {
        if (!TileHover.RaycastGroundPlane(ray, out groundPoint)) return false;
        if (dungeonBoard == null || rockHeight <= 0f || ray.direction.y >= 0f) return true;

        float tTop = Mathf.Max(0f, (rockHeight - ray.origin.y) / ray.direction.y);
        float tGround = -ray.origin.y / ray.direction.y;
        for (int i = 0; i <= PickSamples; i++)
        {
            Vector3 p = ray.GetPoint(Mathf.Lerp(tTop, tGround, i / (float)PickSamples));
            Vector2Int tile = WorldToBoardCoords(p);
            if (dungeonBoard.IsInBounds(tile.x, tile.y) && IsRaised(dungeonBoard.GetTile(tile.x, tile.y)))
            {
                groundPoint = new Vector3(p.x, 0f, p.z);
                return true;
            }
        }
        return true;
    }
}
