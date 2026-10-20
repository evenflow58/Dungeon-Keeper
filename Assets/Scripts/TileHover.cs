using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class TileHover : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private DungeonBoard dungeonBoard;
    [SerializeField] private BoardRenderer boardRenderer;
    [SerializeField] private Camera mainCamera;
    [SerializeField] private FogOfWar fogOfWar; // Optional (#131): unwired, every tile counts as seen
    [SerializeField] private ImpDigger impDigger; // Optional (#132): names rock's material; unwired, labels as before
    [SerializeField] private Stockpile stockpile; // Optional (#132): the catalog's display names; unwired, enum names
    [SerializeField] private PowerGrid powerGrid; // Optional (#144): the conduit line; unwired, found at Start (none in a scene without power)
    [SerializeField] private ManaFlow manaFlow;   // Optional (#145): vent, mana and pipe lines; unwired, found at Start

    [Header("Highlight")]
    [SerializeField] private Color highlightColor = new Color(1f, 1f, 1f, 0.35f);

    [Header("Tooltip")]
    [SerializeField] private Vector2 tooltipOffset = new Vector2(15f, -30f);
    [SerializeField] private float tooltipMinWidth = 160f; // Grows past this to fit longer labels (#132: "Designated — Glimmerstone (x, y)")
    [SerializeField] private float tooltipMinHeight = 28f; // One line; grows to fit a second (#144: the conduit line)

    public DungeonBoard Board { get => dungeonBoard; set => dungeonBoard = value; }
    public BoardRenderer Renderer { get => boardRenderer; set => boardRenderer = value; }
    public Camera MainCamera { get => mainCamera; set => mainCamera = value; }
    public FogOfWar Fog { get => fogOfWar; set => fogOfWar = value; }
    public ImpDigger Digger { get => impDigger; set => impDigger = value; }
    public Stockpile Stockpile { get => stockpile; set => stockpile = value; }
    public PowerGrid Grid { get => powerGrid; set => powerGrid = value; }
    public ManaFlow Mana { get => manaFlow; set => manaFlow = value; }

    private SpriteRenderer highlightSprite;
    private Canvas tooltipCanvas;
    private Text tooltipText;
    private RectTransform tooltipRect;
    private InputConfig.Actions input; // Bindings come from InputConfig (rebindable)

    private void Awake()
    {
        EnsureInput();
    }

    // #137: a domain reload mid-Play (Recompile And Continue Playing) re-runs OnEnable but not Awake, and the actions
    // don't survive it, so they're ensured (and enabled) here; disabled again with the component, like the other
    // InputConfig consumers.
    private void OnEnable()
    {
        EnsureInput();
        input.Enable();
    }

    private void OnDisable()
    {
        input?.Disable();
    }

    private void EnsureInput() => input ??= InputConfig.CreateActions();

    private void Start()
    {
        powerGrid ??= FindAnyObjectByType<PowerGrid>();
        manaFlow ??= FindAnyObjectByType<ManaFlow>();
        CreateHighlight();
        CreateTooltipUI();
    }

    private void OnDestroy()
    {
        input?.Disable();
        input?.Dispose();
        if (highlightSprite != null) Destroy(highlightSprite.gameObject);
        if (tooltipCanvas != null) Destroy(tooltipCanvas.gameObject);
    }

    private void CreateHighlight()
    {
        var go = new GameObject("TileHighlight");
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // Lies on the ground (XZ), facing up
        highlightSprite = go.AddComponent<SpriteRenderer>();

        const int res = 16;
        var tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
        var pixels = new Color[res * res];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
        tex.SetPixels(pixels);
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.Apply();

        var sprite = Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), res);
        sprite.name = "TileHighlight_Sprite";
        highlightSprite.sprite = sprite;
        highlightSprite.color = highlightColor;
        highlightSprite.enabled = false;
    }

    private void CreateTooltipUI()
    {
        var canvasGo = new GameObject("TileTooltipCanvas");
        tooltipCanvas = canvasGo.AddComponent<Canvas>();
        tooltipCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGo.AddComponent<CanvasScaler>();

        var panelGo = new GameObject("TooltipPanel");
        panelGo.transform.SetParent(canvasGo.transform, false);
        var bg = panelGo.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.7f);

        tooltipRect = panelGo.GetComponent<RectTransform>();
        tooltipRect.anchorMin = tooltipRect.anchorMax = new Vector2(0f, 0f);
        tooltipRect.pivot = new Vector2(0f, 1f);
        tooltipRect.sizeDelta = new Vector2(tooltipMinWidth, tooltipMinHeight);

        var textGo = new GameObject("TooltipText");
        textGo.transform.SetParent(panelGo.transform, false);
        tooltipText = textGo.AddComponent<Text>();
        tooltipText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        tooltipText.fontSize = 13;
        tooltipText.color = Color.white;
        tooltipText.alignment = TextAnchor.MiddleCenter;
        tooltipText.horizontalOverflow = HorizontalWrapMode.Overflow; // No wrapping; the panel is sized to the text
        tooltipText.verticalOverflow = VerticalWrapMode.Overflow;     // #144: a second line (conduit) never clips
        var textRect = textGo.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(4f, 2f);
        textRect.offsetMax = new Vector2(-4f, -2f);

        canvasGo.SetActive(false);
    }

    private void Update()
    {
        if (dungeonBoard == null || boardRenderer == null || mainCamera == null || highlightSprite == null)
        {
            SetHoverActive(false);
            return;
        }

        // Over the chrome (rail, flyout, detail card, top bar) the pointer is not on a tile: a click there never
        // reaches the board, so neither the highlight nor the tooltip shows (#125).
        if (IsPointerOverUI())
        {
            SetHoverActive(false);
            return;
        }

        Vector2 mouseScreen = input?.PointerPosition.ReadValue<Vector2>() ?? Vector2.zero;

        Ray ray = mainCamera.ScreenPointToRay(mouseScreen);
        if (!boardRenderer.RaycastBoard(ray, out Vector3 worldPos))
        {
            SetHoverActive(false);
            return;
        }

        Vector2Int coords = boardRenderer.WorldToBoardCoords(worldPos);
        if (!dungeonBoard.IsInBounds(coords.x, coords.y))
        {
            SetHoverActive(false);
            return;
        }

        Vector3 center = boardRenderer.GetTileCenterWorldPosition(coords.x, coords.y);

        // Flat on the tile's top surface (a block's top or the floor), just above it.
        float surfaceY = boardRenderer.GetTileSurfaceHeight(coords.x, coords.y) + boardRenderer.OverlayLift;
        highlightSprite.transform.position = new Vector3(center.x, surfaceY, center.z);
        highlightSprite.enabled = true;

        // Unexplored (#131): the highlight still marks the position, but no tooltip, so hovering can't probe the fog.
        string label = TooltipLabel(fogOfWar, TileIdentity.Of(dungeonBoard, impDigger, coords), coords, stockpile,
            ConduitLine(powerGrid, coords), VentLine(manaFlow, coords), ManaLine(manaFlow, coords), PipeLine(manaFlow, coords));
        if (label == null)
        {
            tooltipCanvas?.gameObject.SetActive(false);
            return;
        }

        SetTooltipText(label);
        PositionTooltip(mouseScreen);
        tooltipCanvas?.gameObject.SetActive(true);
    }

    /// <summary>The hover tooltip for a tile known only by state (no material), or null for none (#131).</summary>
    public static string TooltipLabel(FogOfWar fog, TileState state, Vector2Int tile) =>
        TooltipLabel(fog, new TileIdentity(state, null), tile, null);

    /// <summary>
    /// The hover tooltip for a tile, or null for none: an unexplored tile shows nothing (#131); a seen one names its
    /// identity (#132). Rock names its material whether or not the stockpile has ever held it: discovery governs
    /// menus, not what a seen tile is called.
    /// </summary>
    public static string TooltipLabel(FogOfWar fog, TileIdentity identity, Vector2Int tile, Stockpile stockpile) =>
        TooltipLabel(fog, identity, tile, stockpile, null);

    /// <summary>
    /// The hover tooltip with extra lines (#144 conduit, #145 vent, mana and pipe): the tile label, then each non-null line
    /// beneath it in order. Fog suppresses the whole tooltip, every line included, exactly as for every other fact.
    /// </summary>
    public static string TooltipLabel(FogOfWar fog, TileIdentity identity, Vector2Int tile, Stockpile stockpile,
        params string[] extraLines)
    {
        if (!FogOfWar.IsSeen(fog, tile)) return null;
        string label = FormatTileLabel(identity, tile.x, tile.y, stockpile);
        if (extraLines == null) return label;
        foreach (string line in extraLines)
            if (!string.IsNullOrEmpty(line)) label += "\n" + line;
        return label;
    }

    /// <summary>The vent's line (#145): "Fire Vent · Emitting" or "Fire Vent · Dormant".</summary>
    public static string VentLine(string type, bool dormant) => $"{type} Vent · {(dormant ? "Dormant" : "Emitting")}";

    /// <summary>The vent's line for a tile from a live service, or null when the tile isn't the vent's.</summary>
    public static string VentLine(ManaFlow mana, Vector2Int tile) =>
        mana != null && mana.IsVentTile(tile) ? VentLine(mana.VentManaType, mana.VentDormant) : null;

    /// <summary>A mana tile's line (#145): its type ("Fire mana"), with a count past one.</summary>
    public static string ManaLine(string type, int units) =>
        type == null || units <= 0 ? null : units == 1 ? $"{type} mana" : $"{type} mana ×{units}";

    public static string ManaLine(ManaFlow mana, Vector2Int tile) =>
        mana != null ? ManaLine(mana.TypeAt(tile), mana.UnitsAt(tile)) : null;

    /// <summary>
    /// The pipe line (#145): "Pipe" or "Pipe (planned)"; none on a store's tile (the building is what the tile is, as for
    /// conduit under a power node) or with no pipe.
    /// </summary>
    public static string PipeLine(bool hasPipe, bool built, bool hasStore) =>
        !hasPipe || hasStore ? null : built ? "Pipe" : "Pipe (planned)";

    public static string PipeLine(ManaFlow mana, Vector2Int tile) =>
        mana != null ? PipeLine(mana.HasPipe(tile), mana.IsPipeBuilt(tile), mana.PortAt(tile) != null) : null;

    /// <summary>
    /// The conduit line for a tile (#144), or null for none: built conduit reads its grid's live totals ("Conduit — grid:
    /// 40 produced · 10 demanded"; a bare run truthfully reads 0 · 0), pending conduit "Conduit (planned)". A tile with a
    /// built power node shows none: the node is what the tile is, and its placed card carries the grid detail.
    /// </summary>
    public static string ConduitLine(bool hasConduit, bool built, bool hasNode, int supply, int demand)
    {
        if (!hasConduit || hasNode) return null;
        return built ? $"Conduit — grid: {supply} produced · {demand} demanded" : "Conduit (planned)";
    }

    /// <summary>The conduit line for a tile from a live grid (Unity null check: no grid, no line).</summary>
    public static string ConduitLine(PowerGrid grid, Vector2Int tile)
    {
        if (grid == null || !grid.HasConduit(tile)) return null;
        bool built = grid.IsBuilt(tile);
        PowerGrid.GridTotals totals = built ? grid.TotalsOfGrid(grid.GridAt(tile)) : default;
        return ConduitLine(true, built, grid.NodeAt(tile) != null, totals.Supply, totals.Demand);
    }

    // The label's size changes with the material name and the conduit line, so the panel follows the text (never smaller
    // than before).
    private void SetTooltipText(string label)
    {
        if (tooltipText == null) return;
        tooltipText.text = label;
        if (tooltipRect == null) return;
        RectTransform textRect = tooltipText.rectTransform;
        float widthInsets = -textRect.offsetMax.x + textRect.offsetMin.x;
        float heightInsets = -textRect.offsetMax.y + textRect.offsetMin.y;
        tooltipRect.sizeDelta = new Vector2(Mathf.Max(tooltipMinWidth, tooltipText.preferredWidth + widthInsets),
            Mathf.Max(tooltipMinHeight, tooltipText.preferredHeight + heightInsets));
    }

    private void SetHoverActive(bool active)
    {
        if (highlightSprite != null) highlightSprite.enabled = active;
        tooltipCanvas?.gameObject.SetActive(active);
    }

    // The tooltip's own canvas has no raycaster, so it never counts as UI under the pointer.
    private static bool IsPointerOverUI() =>
        EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

    private void PositionTooltip(Vector2 mouseScreen)
    {
        if (tooltipRect == null) return;

        Vector2 pos = mouseScreen + tooltipOffset;
        float w = tooltipRect.sizeDelta.x;
        float h = tooltipRect.sizeDelta.y;
        pos.x = Mathf.Clamp(pos.x, 0f, Screen.width - w);
        pos.y = Mathf.Clamp(pos.y, h, Screen.height);

        tooltipRect.position = new Vector3(pos.x, pos.y, 0f);
    }

    /// <summary>
    /// Pure-logic method: maps worldPos through boardRenderer and checks dungeonBoard.
    /// Called by tests without a camera or Update loop.
    /// </summary>
    public bool TryGetHoverState(Vector3 worldPos, out TileState state, out Vector2Int coords)
    {
        state = TileState.Rock;
        coords = Vector2Int.zero;
        if (dungeonBoard == null || boardRenderer == null) return false;

        coords = boardRenderer.WorldToBoardCoords(worldPos);
        if (!dungeonBoard.IsInBounds(coords.x, coords.y)) return false;

        state = dungeonBoard.GetTile(coords.x, coords.y);
        return true;
    }

    /// <summary>Intersects the ray with the ground plane (y = 0). False for a ray parallel to the ground.</summary>
    public static bool RaycastGroundPlane(Ray ray, out Vector3 hitPoint)
    {
        hitPoint = Vector3.zero;
        if (Mathf.Abs(ray.direction.y) < 1e-5f) return false;
        float t = -ray.origin.y / ray.direction.y;
        hitPoint = ray.origin + ray.direction * t;
        hitPoint.y = 0f;
        return true;
    }

    public static string FormatTileLabel(TileState state, int x, int y) =>
        $"{state} ({x}, {y})";

    /// <summary>
    /// A tile's label from its identity (#132): today's "State (x, y)", with rock's material inserted as
    /// "Rock — Glimmerstone (x, y)". Each identity fact adds its own part here (the seam for later facts).
    /// </summary>
    public static string FormatTileLabel(TileIdentity identity, int x, int y, Stockpile stockpile) =>
        identity.Material is MaterialType material
            ? $"{identity.State} — {MaterialName(stockpile, material)} ({x}, {y})"
            : FormatTileLabel(identity.State, x, y);

    /// <summary>A material's catalog display name, or its enum name with no stockpile wired.</summary>
    public static string MaterialName(Stockpile stockpile, MaterialType material) =>
        stockpile != null ? stockpile.DisplayName(material) : material.ToString();
}
