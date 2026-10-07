using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class TileHover : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private DungeonBoard dungeonBoard;
    [SerializeField] private BoardRenderer boardRenderer;
    [SerializeField] private Camera mainCamera;

    [Header("Highlight")]
    [SerializeField] private Color highlightColor = new Color(1f, 1f, 1f, 0.35f);

    [Header("Tooltip")]
    [SerializeField] private Vector2 tooltipOffset = new Vector2(15f, -30f);

    public DungeonBoard Board { get => dungeonBoard; set => dungeonBoard = value; }
    public BoardRenderer Renderer { get => boardRenderer; set => boardRenderer = value; }
    public Camera MainCamera { get => mainCamera; set => mainCamera = value; }

    private SpriteRenderer highlightSprite;
    private Canvas tooltipCanvas;
    private Text tooltipText;
    private RectTransform tooltipRect;
    private InputConfig.Actions input; // Bindings come from InputConfig (rebindable)

    private void Awake()
    {
        input = InputConfig.CreateActions();
        input.Enable();
    }

    private void Start()
    {
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
        highlightSprite.sortingOrder = 1;
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
        tooltipRect.sizeDelta = new Vector2(160f, 28f);

        var textGo = new GameObject("TooltipText");
        textGo.transform.SetParent(panelGo.transform, false);
        tooltipText = textGo.AddComponent<Text>();
        tooltipText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        tooltipText.fontSize = 13;
        tooltipText.color = Color.white;
        tooltipText.alignment = TextAnchor.MiddleCenter;
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

        TileState state = dungeonBoard.GetTile(coords.x, coords.y);
        Vector3 center = boardRenderer.GetTileCenterWorldPosition(coords.x, coords.y);

        // Flat on the tile's top surface (a block's top or the floor), just above it.
        float surfaceY = boardRenderer.GetTileSurfaceHeight(coords.x, coords.y) + boardRenderer.OverlayLift;
        highlightSprite.transform.position = new Vector3(center.x, surfaceY, center.z);
        highlightSprite.enabled = true;

        if (tooltipText != null) tooltipText.text = FormatTileLabel(state, coords.x, coords.y);
        PositionTooltip(mouseScreen);
        tooltipCanvas?.gameObject.SetActive(true);
    }

    private void SetHoverActive(bool active)
    {
        if (highlightSprite != null) highlightSprite.enabled = active;
        tooltipCanvas?.gameObject.SetActive(active);
    }

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
}
