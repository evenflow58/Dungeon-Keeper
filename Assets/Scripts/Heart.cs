using UnityEngine;

/// <summary>
/// The Dungeon Heart (DESIGN §A.4, §A.6): pre-placed in the starter cavern with 100 HP. Destroying it ends
/// the game — GameManager watches IsDestroyed. The Heart doesn't block its tile: the tile stays Floor and
/// walkable, like placeables. Its Health only holds the HP; the Heart stays visible after death so the
/// defeat screen (Epic #6) can show it.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Health))]
public class Heart : MonoBehaviour
{
    public const int DesignMaxHealth = 100; // DESIGN §A.6: fixed, not a provisional tunable

    [Header("References")]
    [SerializeField] private DungeonBoard dungeonBoard;
    [SerializeField] private BoardRenderer boardRenderer;
    [SerializeField] private Health health;

    [Header("Placement")]
    [SerializeField] private Vector2Int heartTile = new Vector2Int(24, 14); // Starter cavern, not a spawn tile

    [Header("Appearance (Placeholder Art)")]
    // A crystal (diamond) silhouette: distinct from square placeables and round goblin tokens.
    [SerializeField] private Color bodyColor = new Color(0.80f, 0.10f, 0.35f, 1f);    // Deep red-magenta
    [SerializeField] private Color outlineColor = new Color(0.25f, 0.02f, 0.10f, 1f); // Near-black crimson
    [SerializeField] private float outlineWidth = 0.12f;                               // Fraction of the half-size
    [SerializeField] private float bodySize = 1.2f;                                    // Tiles
    [SerializeField] private GroundShadowStyle groundShadow = new GroundShadowStyle(1.0f, 0.4f); // Disc under the body (sprites cast no shadows)

    public DungeonBoard Board { get => dungeonBoard; set => dungeonBoard = value; }
    public BoardRenderer Renderer { get => boardRenderer; set => boardRenderer = value; }
    public Health Health { get => health; set => health = value; }
    public Vector2Int Tile { get => heartTile; set => heartTile = value; }

    public bool IsDestroyed => health != null && health.IsDead;

    private SpriteRenderer bodySprite;

    // Editor-only: runs when the component is first added (RequireComponent has added Health already).
    // Configures that Health to the design's fixed 100 HP, Monster team, and wires it.
    private void Reset()
    {
        health = GetComponent<Health>();
        if (health == null) return;
        health.MaxHealth = DesignMaxHealth;
        health.Team = HealthTeam.Monster;
    }

    private void Start()
    {
        dungeonBoard ??= FindAnyObjectByType<DungeonBoard>();
        boardRenderer ??= FindAnyObjectByType<BoardRenderer>();
        health ??= GetComponent<Health>();

        CreateBody();
        PlaceOnTile(heartTile);
    }

    /// <summary>Sets the Heart's tile and snaps the transform to that tile's center.</summary>
    public void PlaceOnTile(Vector2Int tile)
    {
        heartTile = tile;
        transform.position = boardRenderer != null
            ? boardRenderer.GetTileCenterWorldPosition(tile.x, tile.y)
            : BoardRenderer.UnanchoredTileCenter(tile);
    }

    private void CreateBody()
    {
        if (bodySprite != null) return;

        var go = new GameObject("HeartBody");
        go.transform.SetParent(transform, false);
        go.transform.localScale = new Vector3(bodySize, bodySize, 1f);
        go.transform.localPosition = new Vector3(0f, bodySize * 0.5f, 0f); // Standing on the ground point
        bodySprite = go.AddComponent<SpriteRenderer>();

        // A filled diamond with a dark rim, colors baked in (the renderer tint stays white).
        const int res = 32;
        var tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
        tex.name = "Heart_Texture";
        var pixels = new Color[res * res];
        float half = res * 0.5f;
        float rimStart = half * (1f - outlineWidth);
        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                float d = Mathf.Abs(x + 0.5f - half) + Mathf.Abs(y + 0.5f - half); // Manhattan: a diamond
                pixels[y * res + x] = d > half ? Color.clear : d > rimStart ? outlineColor : bodyColor;
            }
        }
        tex.SetPixels(pixels);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.Apply();

        var sprite = Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), res);
        sprite.name = "Heart_Sprite";
        bodySprite.sprite = sprite;
        bodySprite.color = Color.white;
        GroundShadow.Create(transform, groundShadow, BoardRenderer.OverlayLiftOf(boardRenderer));
    }
}
