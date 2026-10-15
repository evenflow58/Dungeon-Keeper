using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class DigDesignator : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private DungeonBoard dungeonBoard;
    [SerializeField] private BoardRenderer boardRenderer;
    [SerializeField] private Camera mainCamera;
    [SerializeField] private FogOfWar fogOfWar; // Optional (#131): unwired, every tile counts as seen

    [Header("Visual")]
    [SerializeField] private Color previewColor = new Color(0.85f, 0.65f, 0.15f, 0.35f);

    public DungeonBoard Board       { get => dungeonBoard;  set => dungeonBoard = value; }
    public BoardRenderer Renderer   { get => boardRenderer; set => boardRenderer = value; }
    public Camera        MainCamera { get => mainCamera;    set => mainCamera = value; }
    public FogOfWar      Fog        { get => fogOfWar;      set => fogOfWar = value; }

    public enum DesignateMode { Designate, Clear }

    /// <summary>True while a designate or clear drag is live (#123: the mode owner tells a dig drag from idle dig).</summary>
    public bool IsDragging => isDragging;

    // ---- input ----
    private InputConfig.Actions input; // Bindings come from InputConfig (rebindable); enabled with this component

    // ---- drag state ----
    private bool isDragging;
    private bool dragEnteredBoard;  // true once cursor enters the board; gates apply & preview
    private DesignateMode dragMode;
    private Vector2Int dragAnchor;
    private Vector2Int dragCurrent;

    // ---- preview ----
    // Clear drags show one rect (previewSprite). Designate drags tint exactly the tiles a release would designate
    // (#131), one pooled quad per eligible tile, from the same EligibleTiles that ApplyRect uses.
    private SpriteRenderer previewSprite;
    private readonly List<SpriteRenderer> tilePreviews = new List<SpriteRenderer>();
    private readonly List<Vector2Int> previewTiles = new List<Vector2Int>();

    /// <summary>The tiles the Designate preview currently tints (empty when it's hidden or the drag is a Clear).</summary>
    public IReadOnlyList<Vector2Int> PreviewTiles => previewTiles;

    // ---- lifecycle ----

    private void Awake()
    {
        EnsureInput();
    }

    // #137: a domain reload mid-Play (Recompile And Continue Playing) re-runs OnEnable but not Awake, and the actions
    // don't survive it, so they're ensured here.
    private void OnEnable()
    {
        EnsureInput();
        input.Enable();
    }

    private void EnsureInput() => input ??= InputConfig.CreateActions();

    private void OnDisable()
    {
        input?.Disable();

        // Disabled mid-drag (e.g. placement mode entered): drop the drag so it can't resume later.
        isDragging = false;
        HidePreview();
    }

    private void OnDestroy()
    {
        input?.Dispose();
        if (previewSprite != null) Destroy(previewSprite.gameObject);
        foreach (SpriteRenderer quad in tilePreviews) if (quad != null) Destroy(quad.gameObject);
        tilePreviews.Clear();
    }

    private void Start()
    {
        CreatePreview();
    }

    private void CreatePreview()
    {
        var go = new GameObject("DigPreview");
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // Lies flat (XZ), facing up
        previewSprite = go.AddComponent<SpriteRenderer>();

        const int res = 16;
        var tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
        var pixels = new Color[res * res];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
        tex.SetPixels(pixels);
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.Apply();

        var sprite = Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), res);
        sprite.name = "DigPreview_Sprite";
        previewSprite.sprite = sprite;
        previewSprite.color = previewColor;
        previewSprite.enabled = false;
    }

    // ---- update ----

    private void Update()
    {
        if (dungeonBoard == null || boardRenderer == null || mainCamera == null) return;

        Vector2 mouseScreen = input?.PointerPosition.ReadValue<Vector2>() ?? Vector2.zero;

        Ray ray = mainCamera.ScreenPointToRay(mouseScreen);
        bool hitGround = boardRenderer.RaycastBoard(ray, out Vector3 worldPos);

        if (!isDragging)
            HandleIdleInput(hitGround, worldPos);
        else
            HandleDragInput(hitGround, worldPos);
    }

    private void HandleIdleInput(bool hitGround, Vector3 worldPos)
    {
        bool leftPressed  = input?.Designate.WasPressedThisFrame() ?? false;
        bool rightPressed = input?.ClearOrAbort.WasPressedThisFrame() ?? false;

        // Presses that start on UI (e.g. the build rail) never reach the board.
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        if      (leftPressed  && hitGround) TryStartDrag(worldPos, DesignateMode.Designate);
        else if (rightPressed && hitGround) TryStartDrag(worldPos, DesignateMode.Clear);
    }

    private void TryStartDrag(Vector3 worldPos, DesignateMode mode)
    {
        Vector2Int coords = boardRenderer.WorldToBoardCoords(worldPos);
        isDragging       = true;
        dragMode         = mode;
        dragAnchor       = ClampToBoard(coords, dungeonBoard.Width, dungeonBoard.Height);
        dragCurrent      = dragAnchor;
        // A press outside the board only commits once the cursor enters a board tile.
        dragEnteredBoard = dungeonBoard.IsInBounds(coords.x, coords.y);
        UpdatePreview();
    }

    private void HandleDragInput(bool hitGround, Vector3 worldPos)
    {
        bool esc = input?.Abort.WasPressedThisFrame() ?? false;
        bool rightPressed = input?.ClearOrAbort.WasPressedThisFrame() ?? false;

        // Right-click or Esc aborts a left (Designate) drag; Esc also aborts a right drag.
        bool abort = esc || (dragMode == DesignateMode.Designate && rightPressed);
        if (abort)
        {
            EndDrag(apply: false);
            return;
        }

        // Update current tile: clamp to board when ray hits; keep last valid otherwise.
        if (hitGround)
        {
            Vector2Int raw = boardRenderer.WorldToBoardCoords(worldPos);
            dragCurrent = ClampToBoard(raw, dungeonBoard.Width, dungeonBoard.Height);
            if (dungeonBoard.IsInBounds(raw.x, raw.y)) dragEnteredBoard = true;
        }

        UpdatePreview();

        bool leftReleased  = input?.Designate.WasReleasedThisFrame() ?? false;
        bool rightReleased = input?.ClearOrAbort.WasReleasedThisFrame() ?? false;

        bool released = dragMode == DesignateMode.Designate ? leftReleased : rightReleased;
        if (released)
            EndDrag(apply: true);
    }

    private void UpdatePreview()
    {
        if (previewSprite == null) return;
        if (!dragEnteredBoard) { HidePreview(); return; }

        RectInt rect = NormalizeRect(dragAnchor, dragCurrent);
        // Flat over the rock tops it designates (at ground level it would be hidden inside the blocks).
        float previewY = boardRenderer.RockHeight + boardRenderer.OverlayLift;

        if (dragMode == DesignateMode.Designate)
        {
            previewSprite.enabled = false;
            ShowTilePreview(rect, previewY);
            return;
        }

        HideTilePreview();
        Vector3 minCenter = boardRenderer.GetTileCenterWorldPosition(rect.x, rect.y);
        Vector3 maxCenter = boardRenderer.GetTileCenterWorldPosition(rect.xMax - 1, rect.yMax - 1);
        Vector3 center    = (minCenter + maxCenter) * 0.5f;

        previewSprite.transform.position   = new Vector3(center.x, previewY, center.z);
        previewSprite.transform.localScale = new Vector3(rect.width, rect.height, 1f);
        previewSprite.enabled = true;
    }

    private void EndDrag(bool apply)
    {
        if (apply && dragEnteredBoard)
        {
            RectInt rect = NormalizeRect(dragAnchor, dragCurrent);
            ApplyRect(dungeonBoard, fogOfWar, rect, dragMode);
        }

        isDragging = false;
        HidePreview();
    }

    // Re-evaluated every frame of the drag: the imp can reveal tiles under a live rect.
    private void ShowTilePreview(RectInt rect, float previewY)
    {
        EligibleTiles(dungeonBoard, fogOfWar, rect, DesignateMode.Designate, previewTiles);
        for (int i = 0; i < previewTiles.Count; i++)
        {
            if (i == tilePreviews.Count) tilePreviews.Add(CreateTileQuad());
            Vector3 center = boardRenderer.GetTileCenterWorldPosition(previewTiles[i].x, previewTiles[i].y);
            tilePreviews[i].transform.position = new Vector3(center.x, previewY, center.z);
            tilePreviews[i].enabled = true;
        }
        for (int i = previewTiles.Count; i < tilePreviews.Count; i++) tilePreviews[i].enabled = false;
    }

    // One tile's tint: the rect preview's sprite and color at unit scale.
    private SpriteRenderer CreateTileQuad()
    {
        var go = new GameObject("DigPreviewTile");
        go.transform.rotation = previewSprite.transform.rotation;
        var quad = go.AddComponent<SpriteRenderer>();
        quad.sprite = previewSprite.sprite;
        quad.color = previewColor;
        return quad;
    }

    // The quads are scene roots: on a scene teardown (exiting Play, Play Again's reload) Unity may destroy them
    // before this component's OnDisable runs, so each is null-checked like previewSprite.
    private void HideTilePreview()
    {
        previewTiles.Clear();
        foreach (SpriteRenderer quad in tilePreviews) if (quad != null) quad.enabled = false;
    }

    private void HidePreview()
    {
        if (previewSprite != null) previewSprite.enabled = false;
        HideTilePreview();
    }

    // ---- pure logic (testable) ----

    public static RectInt NormalizeRect(Vector2Int anchor, Vector2Int current)
    {
        int x = Mathf.Min(anchor.x, current.x);
        int y = Mathf.Min(anchor.y, current.y);
        int w = Mathf.Abs(anchor.x - current.x) + 1;
        int h = Mathf.Abs(anchor.y - current.y) + 1;
        return new RectInt(x, y, w, h);
    }

    public static Vector2Int ClampToBoard(Vector2Int coords, int boardWidth, int boardHeight)
    {
        return new Vector2Int(
            Mathf.Clamp(coords.x, 0, boardWidth - 1),
            Mathf.Clamp(coords.y, 0, boardHeight - 1)
        );
    }

    /// <summary>
    /// Whether a drag in this mode changes the tile: Designate takes seen Rock (#131: unexplored rock is skipped),
    /// Clear takes Designated tiles (never fogged: designating needs sight, and nothing re-fogs).
    /// </summary>
    public static bool IsEligible(DungeonBoard board, FogOfWar fog, Vector2Int tile, DesignateMode mode)
    {
        if (!board.IsInBounds(tile.x, tile.y)) return false;
        TileState state = board.GetTile(tile.x, tile.y);
        return mode == DesignateMode.Designate
            ? state == TileState.Rock && FogOfWar.IsSeen(fog, tile)
            : state == TileState.Designated;
    }

    /// <summary>
    /// The tiles in rect a release would change, in x-then-y order, into results (cleared first). The one
    /// eligibility seam: ApplyRect changes exactly these and the Designate preview tints exactly these.
    /// </summary>
    public static void EligibleTiles(DungeonBoard board, FogOfWar fog, RectInt rect, DesignateMode mode, List<Vector2Int> results)
    {
        results.Clear();
        for (int x = rect.x; x < rect.xMax; x++)
        {
            for (int y = rect.y; y < rect.yMax; y++)
            {
                var tile = new Vector2Int(x, y);
                if (IsEligible(board, fog, tile, mode)) results.Add(tile);
            }
        }
    }

    /// <summary>ApplyRect without fog: every tile counts as seen (the game before #131, and fogless staging).</summary>
    public static int ApplyRect(DungeonBoard board, RectInt rect, DesignateMode mode) => ApplyRect(board, null, rect, mode);

    /// <summary>Designates (or clears) the eligible tiles in rect and returns how many changed.</summary>
    public static int ApplyRect(DungeonBoard board, FogOfWar fog, RectInt rect, DesignateMode mode)
    {
        var tiles = new List<Vector2Int>();
        EligibleTiles(board, fog, rect, mode, tiles);
        TileState target = mode == DesignateMode.Designate ? TileState.Designated : TileState.Rock;
        foreach (Vector2Int tile in tiles) board.SetTile(tile.x, tile.y, target);
        return tiles.Count;
    }
}
