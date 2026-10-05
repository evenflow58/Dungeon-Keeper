using UnityEngine;
using UnityEngine.Tilemaps;

public class BoardRenderer : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private DungeonBoard dungeonBoard;
    [SerializeField] private Tilemap tilemap;

    [Header("Tile Colors (Placeholder Art)")]
    [SerializeField] private Color rockColor = new Color(0.22f, 0.22f, 0.22f, 1f);       // Dark gray
    [SerializeField] private Color floorColor = new Color(0.48f, 0.32f, 0.18f, 1f);      // Brown
    [SerializeField] private Color designatedColor = new Color(0.85f, 0.65f, 0.15f, 1f); // Amber/Gold
    [SerializeField] private Color doorPanelColor = new Color(0.72f, 0.46f, 0.22f, 1f);  // Warm wood, lighter than Floor
    [SerializeField] private Color doorFrameColor = new Color(0.16f, 0.09f, 0.04f, 1f);  // Near-black frame and crossbar

    public DungeonBoard Board { get => dungeonBoard; set => dungeonBoard = value; }
    public Tilemap Tilemap => tilemap;
    public Vector3 BoardOrigin => dungeonBoard != null
        ? new Vector3(-dungeonBoard.Width * 0.5f, -dungeonBoard.Height * 0.5f, 0f)
        : Vector3.zero;

    private Tile rockTile;
    private Tile floorTile;
    private Tile designatedTile;
    private Tile entranceTile;
    private bool tilesInitialized;

    private void Awake()
    {
        InitializeReferences();
        AlignGridPosition();
        EnsureTilesInitialized();
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
        AlignGridPosition();
        EnsureTilesInitialized();
        RenderFullBoard();
    }

    public void InitializeReferences()
    {
        dungeonBoard ??= GetComponent<DungeonBoard>() ?? FindAnyObjectByType<DungeonBoard>();
        tilemap ??= GetComponentInChildren<Tilemap>() ?? FindAnyObjectByType<Tilemap>();
    }

    public void AlignGridPosition()
    {
        if (tilemap == null) return;

        // Position the parent Grid (or Tilemap itself) so tile (0,0) is at BoardOrigin
        Transform gridTransform = tilemap.layoutGrid != null ? tilemap.layoutGrid.transform : tilemap.transform;
        gridTransform.position = BoardOrigin;
    }

    public void EnsureTilesInitialized()
    {
        if (tilesInitialized) return;

        rockTile = CreateSolidColorTile(rockColor, "RockTile");
        floorTile = CreateSolidColorTile(floorColor, "FloorTile");
        designatedTile = CreateSolidColorTile(designatedColor, "DesignatedTile");
        entranceTile = CreateDoorTile("EntranceTile");

        tilesInitialized = true;
    }

    private Tile CreateSolidColorTile(Color color, string tileName, int resolution = 16)
    {
        Texture2D texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
        texture.name = tileName + "_Texture";
        Color[] pixels = new Color[resolution * resolution];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = color;
        }
        texture.SetPixels(pixels);
        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.Apply();

        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0, 0, resolution, resolution),
            new Vector2(0.5f, 0.5f),
            resolution
        );
        sprite.name = tileName + "_Sprite";

        Tile tile = ScriptableObject.CreateInstance<Tile>();
        tile.name = tileName;
        tile.sprite = sprite;
        tile.color = Color.white;
        return tile;
    }

    // A framed wooden door: dark frame around a plank panel, with a dark crossbar and plank seams.
    private Tile CreateDoorTile(string tileName, int resolution = 16)
    {
        Texture2D texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
        texture.name = tileName + "_Texture";
        Color[] pixels = new Color[resolution * resolution];
        int mid = resolution / 2;
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                bool frame = x < 2 || x >= resolution - 2 || y >= resolution - 2 || y < 1; // Sides and lintel, a sill
                bool crossbar = y == mid || y == mid - 1;
                bool seam = (x == 5 || x == 10) && !frame;                                   // Plank lines
                pixels[y * resolution + x] = frame || crossbar ? doorFrameColor
                    : seam ? Color.Lerp(doorPanelColor, doorFrameColor, 0.45f)
                    : doorPanelColor;
            }
        }
        texture.SetPixels(pixels);
        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.Apply();

        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0, 0, resolution, resolution),
            new Vector2(0.5f, 0.5f),
            resolution
        );
        sprite.name = tileName + "_Sprite";

        Tile tile = ScriptableObject.CreateInstance<Tile>();
        tile.name = tileName;
        tile.sprite = sprite;
        tile.color = Color.white;
        return tile;
    }

    public TileBase GetTileAsset(TileState state)
    {
        EnsureTilesInitialized();

        switch (state)
        {
            case TileState.Rock:
                return rockTile;
            case TileState.Floor:
                return floorTile;
            case TileState.Designated:
                return designatedTile;
            case TileState.Entrance:
                return entranceTile;
            default:
                return rockTile;
        }
    }

    public void RenderFullBoard()
    {
        if (dungeonBoard == null || tilemap == null) return;

        EnsureTilesInitialized();
        tilemap.ClearAllTiles();

        int width = dungeonBoard.Width;
        int height = dungeonBoard.Height;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                RefreshTile(x, y);
            }
        }
    }

    public void RefreshTile(int x, int y)
    {
        if (dungeonBoard == null || tilemap == null) return;
        if (!dungeonBoard.IsInBounds(x, y)) return;

        EnsureTilesInitialized();

        TileState state = dungeonBoard.GetTile(x, y);
        TileBase tileAsset = GetTileAsset(state);
        Vector3Int cellPos = new Vector3Int(x, y, 0);
        tilemap.SetTile(cellPos, tileAsset);
    }

    private void HandleTileChanged(int x, int y, TileState newState)
    {
        RefreshTile(x, y);
    }

    public Vector3 GetTileWorldPosition(int x, int y)
    {
        if (tilemap != null)
        {
            return tilemap.CellToWorld(new Vector3Int(x, y, 0));
        }
        return BoardOrigin + new Vector3(x, y, 0);
    }

    public Vector3 GetTileCenterWorldPosition(int x, int y)
    {
        if (tilemap != null)
        {
            return tilemap.GetCellCenterWorld(new Vector3Int(x, y, 0));
        }
        return BoardOrigin + new Vector3(x + 0.5f, y + 0.5f, 0);
    }

    public Vector2Int WorldToBoardCoords(Vector3 worldPosition)
    {
        if (tilemap != null)
        {
            Vector3Int cell = tilemap.WorldToCell(worldPosition);
            return new Vector2Int(cell.x, cell.y);
        }
        Vector3 origin = BoardOrigin;
        return new Vector2Int(
            Mathf.FloorToInt(worldPosition.x - origin.x),
            Mathf.FloorToInt(worldPosition.y - origin.y)
        );
    }
}
