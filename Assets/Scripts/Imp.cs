using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class Imp : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private DungeonBoard dungeonBoard;
    [SerializeField] private BoardRenderer boardRenderer;

    [Header("Movement")]
    [SerializeField] private float moveSpeedTilesPerSecond = 4f;

    [Header("Appearance (Placeholder Art)")]
    [SerializeField] private Color bodyColor = new Color(0.85f, 0.2f, 0.2f, 1f); // Imp red
    [SerializeField] private float bodySize = 0.6f;                               // Fraction of a tile
    [SerializeField] private int sortingOrder = 2;                                // Above tiles (0) and hover highlight (1)

    public DungeonBoard Board { get => dungeonBoard; set => dungeonBoard = value; }
    public BoardRenderer Renderer { get => boardRenderer; set => boardRenderer = value; }
    public float MoveSpeed { get => moveSpeedTilesPerSecond; set => moveSpeedTilesPerSecond = value; }

    /// <summary>The board tile whose cell contains the imp's sprite.</summary>
    public Vector2Int CurrentTile { get; private set; }

    /// <summary>The waypoint the imp is currently walking toward; equals CurrentTile when idle.</summary>
    public Vector2Int NextTile { get; private set; }

    public bool IsMoving => path.Count > 0;

    // Waypoints still to visit; path[0] is the tile being walked toward (NextTile).
    private readonly List<Vector2Int> path = new List<Vector2Int>();
    private Vector2Int segmentStart;
    private SpriteRenderer bodySprite;

    private void Start()
    {
        dungeonBoard ??= FindAnyObjectByType<DungeonBoard>();
        boardRenderer ??= FindAnyObjectByType<BoardRenderer>();

        CreateBody();
        Spawn();
    }

    private void Update()
    {
        Advance(Time.deltaTime);
    }

    /// <summary>
    /// Places the imp on the starter cavern's center tile and clears any orders.
    /// </summary>
    public void Spawn()
    {
        if (dungeonBoard == null) return;

        PlaceOnTile(GetCavernCenterTile(dungeonBoard.Width, dungeonBoard.Height, dungeonBoard.CavernSize));
    }

    /// <summary>
    /// Snaps the imp to the given tile's center and clears any orders.
    /// </summary>
    public void PlaceOnTile(Vector2Int tile)
    {
        path.Clear();
        CurrentTile = tile;
        NextTile = tile;
        segmentStart = tile;
        transform.position = TileCenter(tile);
    }

    /// <summary>
    /// Paths to the given tile and starts walking it. Mid-move, the new path starts from
    /// the waypoint the imp is already heading toward (so it never backtracks to a tile
    /// center it has left). If no path exists the request is ignored: an idle imp stays
    /// put, a moving imp keeps its current orders. Returns whether a path was found.
    /// </summary>
    public bool SetDestination(Vector2Int tile)
    {
        if (dungeonBoard == null) return false;

        // Between tile centers, the imp is committed to the waypoint ahead; otherwise it
        // stands on segmentStart's center (which is CurrentTile).
        bool betweenTiles = IsMoving && transform.position != TileCenter(segmentStart);
        Vector2Int origin = betweenTiles ? NextTile : segmentStart;

        List<Vector2Int> newPath = Pathfinder.FindPath(dungeonBoard, origin, tile);
        if (newPath.Count == 0) return false;

        path.Clear();
        if (betweenTiles)
        {
            // Finish the current segment first: newPath[0] is the waypoint ahead.
            path.AddRange(newPath);
        }
        else
        {
            // newPath[0] is the tile the imp is already standing on.
            path.AddRange(newPath.GetRange(1, newPath.Count - 1));
            CurrentTile = origin;
        }

        NextTile = path.Count > 0 ? path[0] : CurrentTile;
        return true;
    }

    /// <summary>
    /// Moves the imp along its path by deltaTime seconds' worth of travel.
    /// Called from Update; public so tests can step movement deterministically.
    /// </summary>
    public void Advance(float deltaTime)
    {
        float remaining = moveSpeedTilesPerSecond * deltaTime; // In tiles (= world units, 1 unit per tile)

        while (path.Count > 0 && remaining > 0f)
        {
            Vector3 target = TileCenter(path[0]);
            float distance = Vector3.Distance(transform.position, target);

            if (distance <= remaining)
            {
                // Reach this waypoint exactly, then carry leftover travel into the next one.
                transform.position = target;
                remaining -= distance;
                segmentStart = path[0];
                CurrentTile = path[0];
                path.RemoveAt(0);
                NextTile = path.Count > 0 ? path[0] : CurrentTile;
            }
            else
            {
                transform.position = Vector3.MoveTowards(transform.position, target, remaining);
                // Mid-segment, the sprite's cell is whichever segment end it's closer to.
                CurrentTile = distance - remaining < 0.5f * SegmentLength() ? path[0] : segmentStart;
                remaining = 0f;
            }
        }
    }

    /// <summary>
    /// The starter cavern's center tile, matching DungeonBoard's centered carve.
    /// For even cavern sizes this is the upper-right of the four middle tiles.
    /// </summary>
    public static Vector2Int GetCavernCenterTile(int width, int height, int cavernSize)
    {
        int startX = (width - cavernSize) / 2;
        int startY = (height - cavernSize) / 2;
        return new Vector2Int(startX + cavernSize / 2, startY + cavernSize / 2);
    }

    private float SegmentLength() =>
        Vector3.Distance(TileCenter(segmentStart), TileCenter(NextTile));

    private Vector3 TileCenter(Vector2Int tile)
    {
        Vector3 center = boardRenderer != null
            ? boardRenderer.GetTileCenterWorldPosition(tile.x, tile.y)
            : new Vector3(tile.x + 0.5f, tile.y + 0.5f, 0f);
        return new Vector3(center.x, center.y, transform.position.z);
    }

    private void CreateBody()
    {
        if (bodySprite != null) return;

        var go = new GameObject("ImpBody");
        go.transform.SetParent(transform, false);
        go.transform.localScale = new Vector3(bodySize, bodySize, 1f);
        go.transform.localPosition = new Vector3(0f, 0f, -0.2f);
        bodySprite = go.AddComponent<SpriteRenderer>();

        const int res = 16;
        var tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
        tex.name = "Imp_Texture";
        var pixels = new Color[res * res];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
        tex.SetPixels(pixels);
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.Apply();

        var sprite = Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), res);
        sprite.name = "Imp_Sprite";
        bodySprite.sprite = sprite;
        bodySprite.color = bodyColor;
        bodySprite.sortingOrder = sortingOrder;
    }
}
