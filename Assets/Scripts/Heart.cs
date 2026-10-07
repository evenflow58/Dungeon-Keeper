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

    [Header("Model (code-built, ticket #77)")]
    // The centerpiece: a faceted crystal on a low stone plinth, the only emissive surface in the game.
    [SerializeField] private Color crystalColor = new Color(0.80f, 0.10f, 0.35f, 1f); // Deep red-magenta (its sprite-era color)
    [SerializeField] private float emissionIntensity = 1.6f;                          // Glow strength (HDR multiplier)
    [SerializeField] private float crystalHeight = 1.2f;                              // Tiles, plinth included
    [SerializeField] private float crystalWidth = 0.75f;                              // Tiles across its equator
    [SerializeField] private Color plinthColor = new Color(0.36f, 0.34f, 0.33f, 1f);  // Stone
    [SerializeField] private float plinthHeight = 0.12f;                              // Tiles
    [SerializeField] private float modelSmoothness = 0.7f;                            // Polished crystal

    public DungeonBoard Board { get => dungeonBoard; set => dungeonBoard = value; }
    public BoardRenderer Renderer { get => boardRenderer; set => boardRenderer = value; }
    public Health Health { get => health; set => health = value; }
    public Vector2Int Tile { get => heartTile; set => heartTile = value; }

    public bool IsDestroyed => health != null && health.IsDead;

    private GameObject model;

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

        CreateModel();
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

    /// <summary>The code-built model (null until CreateModel, which Start calls).</summary>
    public GameObject Model => model;

    /// <summary>Builds the crystal-on-plinth model once, standing on the ground point; the crystal glows.</summary>
    public void CreateModel()
    {
        if (model != null) return;
        model = CreatureModel.Build(transform, "HeartModel",
            PropModel.HeartRecipe(crystalHeight, crystalWidth, crystalColor, plinthColor, plinthHeight), modelSmoothness);
        model.transform.Find("Crystal").GetComponent<MeshRenderer>().sharedMaterial =
            CreatureModel.EmissiveMaterialFor(crystalColor, emissionIntensity, modelSmoothness);
    }
}
