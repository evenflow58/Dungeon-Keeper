using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The hero's body (DESIGN §A.6): tile movement with Goblin's API and semantics, plus hit-point death.
/// No needs clock. Decisions live in HeroAI; the HeroSpawner configures the Health (team Hero, max HP).
/// </summary>
[DisallowMultipleComponent]
public class Hero : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private DungeonBoard dungeonBoard;
    [SerializeField] private BoardRenderer boardRenderer;
    [SerializeField] private Health health; // Optional: a hero with no Health never dies

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 3f; // Tiles per second (provisional Fighter stat)

    [Header("Appearance (Placeholder Art)")]
    // An upward triangle: distinct from round goblins, square placeables and the Heart's diamond.
    [SerializeField] private Color bodyColor = new Color(0.78f, 0.83f, 0.92f, 1f);    // Pale steel
    [SerializeField] private Color outlineColor = new Color(0.12f, 0.14f, 0.20f, 1f); // Dark slate
    [SerializeField] private float outlineWidth = 0.12f;                               // Fraction of the half-size
    [SerializeField] private float bodySize = 0.8f;                                    // Tiles
    [SerializeField] private int sortingOrder = 2;                                     // Minion layer

    public DungeonBoard Board { get => dungeonBoard; set => dungeonBoard = value; }
    public BoardRenderer Renderer { get => boardRenderer; set => boardRenderer = value; }
    public Health Health { get => health; set => health = value; }
    public float MoveSpeed { get => moveSpeed; set => moveSpeed = value; }

    public bool IsDead { get; private set; }

    /// <summary>The board tile whose cell contains the hero's sprite.</summary>
    public Vector2Int CurrentTile { get; private set; }

    /// <summary>The waypoint the hero is currently walking toward; equals CurrentTile when idle.</summary>
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
        health ??= GetComponent<Health>();

        CreateBody();
    }

    private void Update()
    {
        Tick(Time.deltaTime);
    }

    /// <summary>
    /// Dies if its Health has run out, else moves. Called from Update; public so tests can step it.
    /// No-op once dead.
    /// </summary>
    public void Tick(float deltaTime)
    {
        if (IsDead) return;
        if (health != null && health.IsDead)
        {
            Die();
            return;
        }

        Advance(deltaTime);
    }

    /// <summary>Snaps the hero to the given tile's center and clears any orders.</summary>
    public void PlaceOnTile(Vector2Int tile)
    {
        path.Clear();
        CurrentTile = tile;
        NextTile = tile;
        segmentStart = tile;
        transform.position = TileCenter(tile);
    }

    /// <summary>
    /// Paths to the given tile and starts walking it, with Goblin.SetDestination's semantics: mid-move the
    /// new path starts from the waypoint ahead; with no path the request is ignored (current orders kept).
    /// Returns whether a path was found. Always false once dead.
    /// </summary>
    public bool SetDestination(Vector2Int tile)
    {
        if (dungeonBoard == null || IsDead) return false;

        // Between tile centers, the hero is committed to the waypoint ahead; otherwise it
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
            // newPath[0] is the tile the hero is already standing on.
            path.AddRange(newPath.GetRange(1, newPath.Count - 1));
            CurrentTile = origin;
        }

        NextTile = path.Count > 0 ? path[0] : CurrentTile;
        return true;
    }

    /// <summary>
    /// Moves the hero along its path by deltaTime seconds' worth of travel. Tick calls it; public so tests
    /// can step movement alone. No-op once dead.
    /// </summary>
    public void Advance(float deltaTime)
    {
        if (IsDead) return;

        float remaining = moveSpeed * deltaTime; // In tiles (= world units, 1 unit per tile)

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

    /// <summary>The waypoints still ahead (NextTile first); empty when idle.</summary>
    public IReadOnlyList<Vector2Int> RemainingPath => path;

    private void Die()
    {
        IsDead = true;
        path.Clear();
        NextTile = CurrentTile;
        gameObject.SetActive(false); // Slice placeholder for death: no corpse system
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

        var go = new GameObject("HeroBody");
        go.transform.SetParent(transform, false);
        go.transform.localScale = new Vector3(bodySize, bodySize, 1f);
        go.transform.localPosition = new Vector3(0f, 0f, -0.2f);
        bodySprite = go.AddComponent<SpriteRenderer>();

        // An upward triangle (apex at the top, base along the bottom) with a dark rim, colors baked in.
        const int res = 32;
        var tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
        tex.name = "Hero_Texture";
        var pixels = new Color[res * res];
        float rim = res * 0.5f * outlineWidth;
        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                float halfWidthAtY = (res - py) * 0.5f;                  // 16 at the base, 0 at the apex
                float side = halfWidthAtY - Mathf.Abs(px - res * 0.5f);  // Distance inside the slanted sides
                float edge = Mathf.Min(side * 0.894f, py);               // ≈ perpendicular distance to the nearest edge
                pixels[y * res + x] = side < 0f ? Color.clear : edge < rim ? outlineColor : bodyColor;
            }
        }
        tex.SetPixels(pixels);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.Apply();

        var sprite = Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), res);
        sprite.name = "Hero_Sprite";
        bodySprite.sprite = sprite;
        bodySprite.color = Color.white;
        bodySprite.sortingOrder = sortingOrder;
    }
}
