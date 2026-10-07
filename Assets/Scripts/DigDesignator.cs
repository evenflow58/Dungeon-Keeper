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

    [Header("Visual")]
    [SerializeField] private Color previewColor = new Color(0.85f, 0.65f, 0.15f, 0.35f);

    public DungeonBoard Board       { get => dungeonBoard;  set => dungeonBoard = value; }
    public BoardRenderer Renderer   { get => boardRenderer; set => boardRenderer = value; }
    public Camera        MainCamera { get => mainCamera;    set => mainCamera = value; }

    public enum DesignateMode { Designate, Clear }

    // ---- input ----
    private InputConfig.Actions input; // Bindings come from InputConfig (rebindable); enabled with this component

    // ---- drag state ----
    private bool isDragging;
    private bool dragEnteredBoard;  // true once cursor enters the board; gates apply & preview
    private DesignateMode dragMode;
    private Vector2Int dragAnchor;
    private Vector2Int dragCurrent;

    // ---- preview ----
    private SpriteRenderer previewSprite;

    // ---- lifecycle ----

    private void Awake()
    {
        input = InputConfig.CreateActions();
    }

    private void OnEnable()
    {
        input?.Enable();
    }

    private void OnDisable()
    {
        input?.Disable();

        // Disabled mid-drag (e.g. placement mode entered): drop the drag so it can't resume later.
        isDragging = false;
        if (previewSprite != null) previewSprite.enabled = false;
    }

    private void OnDestroy()
    {
        input?.Dispose();
        if (previewSprite != null) Destroy(previewSprite.gameObject);
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

        // Presses that start on UI (e.g. the build bar) never reach the board.
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
        if (!dragEnteredBoard) { previewSprite.enabled = false; return; }

        RectInt rect = NormalizeRect(dragAnchor, dragCurrent);
        Vector3 minCenter = boardRenderer.GetTileCenterWorldPosition(rect.x, rect.y);
        Vector3 maxCenter = boardRenderer.GetTileCenterWorldPosition(rect.xMax - 1, rect.yMax - 1);
        Vector3 center    = (minCenter + maxCenter) * 0.5f;

        // Flat over the rock tops it designates (at ground level it would be hidden inside the blocks).
        float previewY = boardRenderer.RockHeight + boardRenderer.OverlayLift;
        previewSprite.transform.position   = new Vector3(center.x, previewY, center.z);
        previewSprite.transform.localScale = new Vector3(rect.width, rect.height, 1f);
        previewSprite.enabled = true;
    }

    private void EndDrag(bool apply)
    {
        if (apply && dragEnteredBoard)
        {
            RectInt rect = NormalizeRect(dragAnchor, dragCurrent);
            ApplyRect(dungeonBoard, rect, dragMode);
        }

        isDragging = false;
        if (previewSprite != null) previewSprite.enabled = false;
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

    public static int ApplyRect(DungeonBoard board, RectInt rect, DesignateMode mode)
    {
        int changed = 0;
        for (int x = rect.x; x < rect.xMax; x++)
        {
            for (int y = rect.y; y < rect.yMax; y++)
            {
                if (!board.IsInBounds(x, y)) continue;
                TileState state = board.GetTile(x, y);
                if (mode == DesignateMode.Designate && state == TileState.Rock)
                {
                    board.SetTile(x, y, TileState.Designated);
                    changed++;
                }
                else if (mode == DesignateMode.Clear && state == TileState.Designated)
                {
                    board.SetTile(x, y, TileState.Rock);
                    changed++;
                }
            }
        }
        return changed;
    }
}
