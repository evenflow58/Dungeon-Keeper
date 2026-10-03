using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Build bar UI + placement mode. Selecting a placeable type enters placement mode (dig designation
/// is disabled); left-click/drag places on every valid tile in the rect; Esc or right-click exits.
/// </summary>
[DisallowMultipleComponent]
public class PlacementController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private DungeonBoard dungeonBoard;
    [SerializeField] private BoardRenderer boardRenderer;
    [SerializeField] private Imp imp;
    [SerializeField] private DigDesignator digDesignator;
    [SerializeField] private PlacementManager placementManager;
    [SerializeField] private Camera mainCamera;

    [Header("Build Bar")]
    [SerializeField] private Color buttonNormalColor = new Color(0.25f, 0.25f, 0.30f, 1f);
    [SerializeField] private Color buttonSelectedColor = new Color(0.85f, 0.65f, 0.15f, 1f);
    [SerializeField] private Vector2 buttonSize = new Vector2(140f, 36f);
    [SerializeField] private float barPadding = 8f;

    [Header("Drag Preview")]
    [SerializeField] private Color validPreviewColor = new Color(0.4f, 0.9f, 0.4f, 0.35f);
    [SerializeField] private Color invalidPreviewColor = new Color(0.9f, 0.3f, 0.3f, 0.35f);
    [SerializeField] private float feedbackFlashSeconds = 0.25f;

    public DungeonBoard Board { get => dungeonBoard; set => dungeonBoard = value; }
    public BoardRenderer Renderer { get => boardRenderer; set => boardRenderer = value; }
    public Imp Imp { get => imp; set => imp = value; }
    public DigDesignator DigDesignator { get => digDesignator; set => digDesignator = value; }
    public PlacementManager PlacementManager { get => placementManager; set => placementManager = value; }
    public Camera MainCamera { get => mainCamera; set => mainCamera = value; }

    public PlaceableType? SelectedType { get; private set; }
    public bool IsPlacing => SelectedType != null;
    public bool IsDragging => isDragging;

    /// <summary>How many placeables the last drag-release placed (0 = rejected).</summary>
    public int LastApplyCount { get; private set; }

    /// <summary>True while the red rejection preview is being shown after a 0-tile release.</summary>
    public bool IsShowingRejection => feedbackTimer > 0f;

    private static readonly PlaceableType[] BarTypes = { PlaceableType.LairCot, PlaceableType.MushroomPlot, PlaceableType.SpikeTrap };
    private static readonly string[] BarLabels = { "Lair Cot", "Mushroom Plot", "Spike Trap" };

    // ---- input ----
    private InputAction leftPressAction;
    private InputAction rightPressAction;
    private InputAction escAction;
    private InputAction mousePosAction;

    // ---- drag state ----
    private bool isDragging;
    private bool dragEnteredBoard; // true once the cursor enters the board; gates apply & preview
    private Vector2Int dragAnchor;
    private Vector2Int dragCurrent;
    private float feedbackTimer;

    // Exiting via right-click re-enables DigDesignator a frame later, so the same press
    // can't also start a dig-clear drag.
    private int digReenableFrame = -1;

    // ---- visuals ----
    private SpriteRenderer previewSprite;
    private Canvas barCanvas;
    private Image[] buttonImages;

    // ---- lifecycle ----

    private void Awake()
    {
        leftPressAction  = new InputAction("PlaceLeft",  InputActionType.Button, "<Mouse>/leftButton");
        rightPressAction = new InputAction("PlaceRight", InputActionType.Button, "<Mouse>/rightButton");
        escAction        = new InputAction("PlaceEsc",   InputActionType.Button, "<Keyboard>/escape");
        mousePosAction   = new InputAction("PlaceMouse", InputActionType.Value,  "<Mouse>/position");
    }

    private void OnEnable()
    {
        leftPressAction?.Enable();
        rightPressAction?.Enable();
        escAction?.Enable();
        mousePosAction?.Enable();
    }

    private void OnDisable()
    {
        leftPressAction?.Disable();
        rightPressAction?.Disable();
        escAction?.Disable();
        mousePosAction?.Disable();
    }

    private void OnDestroy()
    {
        leftPressAction?.Dispose();
        rightPressAction?.Dispose();
        escAction?.Dispose();
        mousePosAction?.Dispose();
        if (previewSprite != null) Destroy(previewSprite.gameObject);
        if (barCanvas != null) Destroy(barCanvas.gameObject);
    }

    private void Start()
    {
        dungeonBoard ??= FindAnyObjectByType<DungeonBoard>();
        boardRenderer ??= FindAnyObjectByType<BoardRenderer>();
        imp ??= FindAnyObjectByType<Imp>();
        digDesignator ??= FindAnyObjectByType<DigDesignator>();
        placementManager ??= FindAnyObjectByType<PlacementManager>();
        mainCamera ??= Camera.main;

        CreatePreview();
        EnsureEventSystem();
        CreateBuildBar();
    }

    // ---- mode API ----

    /// <summary>Enters placement mode for the type; selecting the current type again exits.</summary>
    public void Select(PlaceableType type)
    {
        if (SelectedType == type)
        {
            ClearSelection();
            return;
        }

        CancelDrag();
        SelectedType = type;
        digReenableFrame = -1;
        if (digDesignator != null) digDesignator.enabled = false;
        RefreshButtons();
    }

    /// <summary>Exits placement mode and restores dig designation immediately.</summary>
    public void ClearSelection()
    {
        CancelDrag();
        SelectedType = null;
        digReenableFrame = -1;
        if (digDesignator != null) digDesignator.enabled = true;
        RefreshButtons();
    }

    private void ExitFromBoardInput()
    {
        CancelDrag();
        SelectedType = null;
        digReenableFrame = Time.frameCount + 1;
        RefreshButtons();
    }

    // ---- update ----

    private void Update()
    {
        if (digReenableFrame >= 0 && Time.frameCount >= digReenableFrame)
        {
            digReenableFrame = -1;
            if (!IsPlacing && digDesignator != null) digDesignator.enabled = true;
        }

        if (feedbackTimer > 0f)
        {
            feedbackTimer -= Time.deltaTime;
            if (feedbackTimer <= 0f && !isDragging && previewSprite != null) previewSprite.enabled = false;
        }

        if (!IsPlacing || dungeonBoard == null || boardRenderer == null || mainCamera == null) return;

        Vector2 mouseScreen = mousePosAction?.ReadValue<Vector2>() ?? Vector2.zero;
        if (mouseScreen == Vector2.zero && Mouse.current != null)
            mouseScreen = Mouse.current.position.ReadValue();

        Ray ray = mainCamera.ScreenPointToRay(mouseScreen);
        bool hitGround = TileHover.RaycastGroundPlane(ray, out Vector3 worldPos);

        if (!isDragging)
            HandleIdleInput(hitGround, worldPos);
        else
            HandleDragInput(hitGround, worldPos);
    }

    private void HandleIdleInput(bool hitGround, Vector3 worldPos)
    {
        bool esc = (escAction?.WasPressedThisFrame() ?? false)
                || (Keyboard.current?.escapeKey.wasPressedThisFrame ?? false);
        bool leftPressed  = (leftPressAction?.WasPressedThisFrame()  ?? false)
                         || (Mouse.current?.leftButton.wasPressedThisFrame  ?? false);
        bool rightPressed = (rightPressAction?.WasPressedThisFrame() ?? false)
                         || (Mouse.current?.rightButton.wasPressedThisFrame ?? false);

        if (esc || rightPressed)
        {
            ExitFromBoardInput();
            return;
        }

        // Presses that start on the build bar (or any UI) never reach the board.
        if (leftPressed && hitGround && !IsPointerOverUI()) StartDrag(worldPos);
    }

    private void StartDrag(Vector3 worldPos)
    {
        Vector2Int coords = boardRenderer.WorldToBoardCoords(worldPos);
        isDragging       = true;
        feedbackTimer    = 0f;
        previewValid     = null;
        dragAnchor       = DigDesignator.ClampToBoard(coords, dungeonBoard.Width, dungeonBoard.Height);
        dragCurrent      = dragAnchor;
        // A press outside the board only commits once the cursor enters a board tile.
        dragEnteredBoard = dungeonBoard.IsInBounds(coords.x, coords.y);
        UpdatePreview();
    }

    private void HandleDragInput(bool hitGround, Vector3 worldPos)
    {
        bool esc = (escAction?.WasPressedThisFrame() ?? false)
                || (Keyboard.current?.escapeKey.wasPressedThisFrame ?? false);
        bool rightPressed = (rightPressAction?.WasPressedThisFrame() ?? false)
                         || (Mouse.current?.rightButton.wasPressedThisFrame ?? false);

        // During a drag, Esc / right-click abort the drag only (placement mode stays active).
        if (esc || rightPressed)
        {
            CancelDrag();
            return;
        }

        if (hitGround)
        {
            Vector2Int raw = boardRenderer.WorldToBoardCoords(worldPos);
            Vector2Int clamped = DigDesignator.ClampToBoard(raw, dungeonBoard.Width, dungeonBoard.Height);
            if (dungeonBoard.IsInBounds(raw.x, raw.y)) dragEnteredBoard = true;
            if (clamped != dragCurrent) previewValid = null; // rect changed: re-evaluate tint
            dragCurrent = clamped;
        }

        UpdatePreview();

        bool leftReleased = (leftPressAction?.WasReleasedThisFrame() ?? false)
                         || (Mouse.current?.leftButton.wasReleasedThisFrame ?? false);
        if (leftReleased) EndDrag();
    }

    private void EndDrag()
    {
        isDragging = false;
        if (!dragEnteredBoard)
        {
            HidePreview();
            return;
        }

        RectInt rect = DigDesignator.NormalizeRect(dragAnchor, dragCurrent);
        LastApplyCount = placementManager != null && imp != null && SelectedType != null
            ? placementManager.ApplyRect(SelectedType.Value, rect, imp.CurrentTile)
            : 0;

        if (LastApplyCount > 0)
        {
            HidePreview();
            return;
        }

        // Rejected: hold the red preview briefly so the player sees nothing was placed.
        if (previewSprite != null)
        {
            previewSprite.color = invalidPreviewColor;
            previewSprite.enabled = true;
        }
        feedbackTimer = feedbackFlashSeconds;
    }

    private void CancelDrag()
    {
        isDragging = false;
        HidePreview();
    }

    private void HidePreview()
    {
        feedbackTimer = 0f;
        previewValid = null;
        if (previewSprite != null) previewSprite.enabled = false;
    }

    // Cached "rect has at least one valid tile" so CanPlace's pathfinding runs only when the rect changes.
    private bool? previewValid;

    private void UpdatePreview()
    {
        if (previewSprite == null) return;
        if (!dragEnteredBoard) { previewSprite.enabled = false; return; }

        RectInt rect = DigDesignator.NormalizeRect(dragAnchor, dragCurrent);
        previewValid ??= placementManager != null && imp != null && placementManager.AnyPlaceable(rect, imp.CurrentTile);

        Vector3 minCenter = boardRenderer.GetTileCenterWorldPosition(rect.x, rect.y);
        Vector3 maxCenter = boardRenderer.GetTileCenterWorldPosition(rect.xMax - 1, rect.yMax - 1);
        Vector3 center    = (minCenter + maxCenter) * 0.5f;

        previewSprite.transform.position   = new Vector3(center.x, center.y, -0.1f);
        previewSprite.transform.localScale = new Vector3(rect.width, rect.height, 1f);
        previewSprite.color   = previewValid.Value ? validPreviewColor : invalidPreviewColor;
        previewSprite.enabled = true;
    }

    private static bool IsPointerOverUI() =>
        EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

    // ---- visuals ----

    private void CreatePreview()
    {
        var go = new GameObject("PlacementPreview");
        previewSprite = go.AddComponent<SpriteRenderer>();
        previewSprite.sprite = CreateWhiteSprite("PlacementPreview_Sprite");
        previewSprite.color = validPreviewColor;
        previewSprite.sortingOrder = 2;
        previewSprite.enabled = false;
    }

    private static Sprite CreateWhiteSprite(string spriteName)
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
        sprite.name = spriteName;
        return sprite;
    }

    private static void EnsureEventSystem()
    {
        if (FindAnyObjectByType<EventSystem>() != null) return;

        // The project uses the Input System only, so the UI module must be InputSystemUIInputModule
        // (StandaloneInputModule reads the legacy Input class).
        var go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        var module = go.AddComponent<InputSystemUIInputModule>();
        module.AssignDefaultActions();
    }

    private void CreateBuildBar()
    {
        var canvasGo = new GameObject("BuildBarCanvas");
        barCanvas = canvasGo.AddComponent<Canvas>();
        barCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGo.AddComponent<CanvasScaler>();
        canvasGo.AddComponent<GraphicRaycaster>(); // Buttons need a raycaster to receive clicks

        var barGo = new GameObject("BuildBar");
        barGo.transform.SetParent(canvasGo.transform, false);
        var barImage = barGo.AddComponent<Image>();
        barImage.color = new Color(0f, 0f, 0f, 0.6f);
        var barRect = barGo.GetComponent<RectTransform>();
        barRect.anchorMin = barRect.anchorMax = new Vector2(0.5f, 0f);
        barRect.pivot = new Vector2(0.5f, 0f);
        barRect.anchoredPosition = new Vector2(0f, barPadding);
        barRect.sizeDelta = new Vector2(
            BarTypes.Length * buttonSize.x + (BarTypes.Length + 1) * barPadding,
            buttonSize.y + 2f * barPadding);

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        buttonImages = new Image[BarTypes.Length];

        for (int i = 0; i < BarTypes.Length; i++)
        {
            PlaceableType type = BarTypes[i];

            var buttonGo = new GameObject(BarLabels[i] + "Button");
            buttonGo.transform.SetParent(barGo.transform, false);
            var image = buttonGo.AddComponent<Image>();
            image.color = buttonNormalColor;
            var button = buttonGo.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => Select(type));

            var rect = buttonGo.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(barPadding + i * (buttonSize.x + barPadding), 0f);
            rect.sizeDelta = buttonSize;

            var textGo = new GameObject("Label");
            textGo.transform.SetParent(buttonGo.transform, false);
            var text = textGo.AddComponent<Text>();
            text.font = font;
            text.fontSize = 14;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            text.text = BarLabels[i];
            text.raycastTarget = false;
            var textRect = textGo.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = textRect.offsetMax = Vector2.zero;

            buttonImages[i] = image;
        }

        RefreshButtons();
    }

    private void RefreshButtons()
    {
        if (buttonImages == null) return; // Start() hasn't run (EditMode tests)

        for (int i = 0; i < BarTypes.Length; i++)
        {
            if (buttonImages[i] != null)
                buttonImages[i].color = SelectedType == BarTypes[i] ? buttonSelectedColor : buttonNormalColor;
        }
    }
}
