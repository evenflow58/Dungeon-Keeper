using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Pipe-mode input (#145), ConduitDesignator's twin: enabled by PlacementController exactly in Pipe mode. Left-drag lays
/// pending pipe over the rect's eligible tiles (explored Floor with no pipe yet: sight-gated like dig designation);
/// right-drag removes pipe (pending clears instantly, built is removed instantly, no refund). Picking is
/// BoardRenderer.RaycastBoard. The preview is honest: each drag frame tints exactly the tiles a release would change, one
/// pooled quad per tile, from the same ManaFlow.EligiblePipeTiles that ApplyPipeRect uses. Esc (or the other button)
/// aborts a live drag; with no drag, Esc is the controller's (it exits Pipe).
/// </summary>
[DisallowMultipleComponent]
public class PipeDesignator : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private DungeonBoard dungeonBoard;
    [SerializeField] private BoardRenderer boardRenderer;
    [SerializeField] private Camera mainCamera;
    [SerializeField] private ManaFlow manaFlow;
    [SerializeField] private FogOfWar fogOfWar; // Found at Start when unwired; with none at all, every tile counts as seen

    [Header("Visual")]
    [SerializeField] private Color layPreviewColor = new Color(0.70f, 0.60f, 1.00f, 0.45f);    // Violet: pipe will go here
    [SerializeField] private Color removePreviewColor = new Color(0.90f, 0.30f, 0.30f, 0.45f); // Red: pipe will come out

    public DungeonBoard Board { get => dungeonBoard; set => dungeonBoard = value; }
    public BoardRenderer Renderer { get => boardRenderer; set => boardRenderer = value; }
    public Camera MainCamera { get => mainCamera; set => mainCamera = value; }
    public ManaFlow Mana { get => manaFlow; set => manaFlow = value; }
    public FogOfWar Fog { get => fogOfWar; set => fogOfWar = value; }

    /// <summary>True while a lay or remove drag is live (the mode owner tells a pipe drag from idle pipe mode).</summary>
    public bool IsDragging => isDragging;

    /// <summary>The tiles the preview currently tints (empty when hidden).</summary>
    public IReadOnlyList<Vector2Int> PreviewTiles => previewTiles;

    /// <summary>The live drag's mode (meaningful while IsDragging).</summary>
    public ManaFlow.PipeMode DragMode => dragMode;

    private InputConfig.Actions input; // Bindings come from InputConfig (rebindable); enabled with this component

    private bool isDragging;
    private bool dragEnteredBoard;
    private ManaFlow.PipeMode dragMode;
    private Vector2Int dragAnchor;
    private Vector2Int dragCurrent;

    private Sprite quadSprite;
    private readonly List<SpriteRenderer> tilePreviews = new List<SpriteRenderer>();
    private readonly List<Vector2Int> previewTiles = new List<Vector2Int>();

    private void Awake()
    {
        EnsureInput();
    }

    // #137: a domain reload mid-Play re-runs OnEnable but not Awake, and the actions don't survive it, so they're ensured here.
    private void OnEnable()
    {
        EnsureInput();
        input.Enable();
    }

    private void EnsureInput() => input ??= InputConfig.CreateActions();

    private void OnDisable()
    {
        input?.Disable();
        isDragging = false; // Leaving Pipe mid-drag drops the drag so it can't resume later
        HidePreview();
    }

    private void OnDestroy()
    {
        input?.Dispose();
        foreach (SpriteRenderer quad in tilePreviews) if (quad != null) Destroy(quad.gameObject);
        tilePreviews.Clear();
    }

    private void Start()
    {
        dungeonBoard ??= FindAnyObjectByType<DungeonBoard>();
        boardRenderer ??= FindAnyObjectByType<BoardRenderer>();
        manaFlow ??= FindAnyObjectByType<ManaFlow>();
        fogOfWar ??= FindAnyObjectByType<FogOfWar>(); // Sight-gating must hold even though no scene field wires it
        mainCamera ??= Camera.main;
    }

    private void Update()
    {
        if (dungeonBoard == null || boardRenderer == null || mainCamera == null || manaFlow == null) return;

        Vector2 mouseScreen = input?.PointerPosition.ReadValue<Vector2>() ?? Vector2.zero;
        bool hitGround = boardRenderer.RaycastBoard(mainCamera.ScreenPointToRay(mouseScreen), out Vector3 worldPos);

        if (!isDragging) HandleIdleInput(hitGround, worldPos);
        else HandleDragInput(hitGround, worldPos);
    }

    private void HandleIdleInput(bool hitGround, Vector3 worldPos)
    {
        bool leftPressed = input?.Designate.WasPressedThisFrame() ?? false;
        bool rightPressed = input?.ClearOrAbort.WasPressedThisFrame() ?? false;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return; // Presses on the chrome

        if (leftPressed && hitGround) StartDrag(worldPos, ManaFlow.PipeMode.Lay);
        else if (rightPressed && hitGround) StartDrag(worldPos, ManaFlow.PipeMode.Remove);
    }

    private void StartDrag(Vector3 worldPos, ManaFlow.PipeMode mode)
    {
        Vector2Int coords = boardRenderer.WorldToBoardCoords(worldPos);
        isDragging = true;
        dragMode = mode;
        dragAnchor = DigDesignator.ClampToBoard(coords, dungeonBoard.Width, dungeonBoard.Height);
        dragCurrent = dragAnchor;
        dragEnteredBoard = dungeonBoard.IsInBounds(coords.x, coords.y);
        UpdatePreview();
    }

    private void HandleDragInput(bool hitGround, Vector3 worldPos)
    {
        bool esc = input?.Abort.WasPressedThisFrame() ?? false;
        bool otherPressed = dragMode == ManaFlow.PipeMode.Lay
            ? input?.ClearOrAbort.WasPressedThisFrame() ?? false
            : input?.Designate.WasPressedThisFrame() ?? false;
        if (esc || otherPressed)
        {
            EndDrag(apply: false);
            return;
        }

        if (hitGround)
        {
            Vector2Int raw = boardRenderer.WorldToBoardCoords(worldPos);
            dragCurrent = DigDesignator.ClampToBoard(raw, dungeonBoard.Width, dungeonBoard.Height);
            if (dungeonBoard.IsInBounds(raw.x, raw.y)) dragEnteredBoard = true;
        }

        UpdatePreview();

        bool released = dragMode == ManaFlow.PipeMode.Lay
            ? input?.Designate.WasReleasedThisFrame() ?? false
            : input?.ClearOrAbort.WasReleasedThisFrame() ?? false;
        if (released) EndDrag(apply: true);
    }

    private void EndDrag(bool apply)
    {
        if (apply && dragEnteredBoard) manaFlow.ApplyPipeRect(fogOfWar, DigDesignator.NormalizeRect(dragAnchor, dragCurrent), dragMode);
        isDragging = false;
        HidePreview();
    }

    // Re-evaluated every drag frame: the imp can reveal tiles (or finish wiring) under a live rect.
    private void UpdatePreview()
    {
        if (!dragEnteredBoard)
        {
            HidePreview();
            return;
        }

        manaFlow.EligiblePipeTiles(fogOfWar, DigDesignator.NormalizeRect(dragAnchor, dragCurrent), dragMode, previewTiles);
        Color color = dragMode == ManaFlow.PipeMode.Lay ? layPreviewColor : removePreviewColor;
        for (int i = 0; i < previewTiles.Count; i++)
        {
            if (i == tilePreviews.Count) tilePreviews.Add(CreateTileQuad());
            Vector3 center = boardRenderer.GetTileCenterWorldPosition(previewTiles[i].x, previewTiles[i].y);
            tilePreviews[i].transform.position = new Vector3(center.x, boardRenderer.OverlayLift, center.z); // On the floor
            tilePreviews[i].color = color;
            tilePreviews[i].enabled = true;
        }
        for (int i = previewTiles.Count; i < tilePreviews.Count; i++)
            if (tilePreviews[i] != null) tilePreviews[i].enabled = false;
    }

    private SpriteRenderer CreateTileQuad()
    {
        quadSprite ??= CreateWhiteSprite();
        var go = new GameObject("PipePreviewTile");
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // Lies flat (XZ), facing up
        var quad = go.AddComponent<SpriteRenderer>();
        quad.sprite = quadSprite;
        return quad;
    }

    // The quads are scene roots: a scene teardown (exiting Play, Play Again's reload) may destroy them before this
    // component's OnDisable runs, so each is null-checked (#140's lesson).
    private void HidePreview()
    {
        previewTiles.Clear();
        foreach (SpriteRenderer quad in tilePreviews) if (quad != null) quad.enabled = false;
    }

    private static Sprite CreateWhiteSprite()
    {
        const int res = 16;
        var tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
        var pixels = new Color[res * res];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
        tex.SetPixels(pixels);
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.Apply();
        var sprite = Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), res);
        sprite.name = "PipePreview_Sprite";
        return sprite;
    }
}
