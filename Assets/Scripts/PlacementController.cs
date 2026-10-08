using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Build bar UI + placement mode. Selecting a placeable type enters placement mode (dig designation
/// is disabled); left-click/drag places on every valid tile in the rect; Esc or right-click exits.
/// Each button shows its type's cost (#104), red while an order wouldn't fund right now; the drag preview takes the
/// unaffordable tint then too. Placing is never blocked by cost: the order waits for materials.
/// A material selector (#109) at the bar's end reads the current material and cycles through the materials the
/// selected type can be ordered in; orders are placed in it, and the cost labels follow it.
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
    [SerializeField] private Color unaffordablePreviewColor = new Color(0.95f, 0.45f, 0.2f, 0.35f); // Placeable, but it will wait (#104)

    [Header("Costs (#104)")]
    [SerializeField] private Color unaffordableCostColor = new Color(1f, 0.38f, 0.38f, 1f);
    [SerializeField] private float costRefreshSeconds = 0.25f; // How often the bar re-checks affordability (unscaled)
    [SerializeField] private float materialButtonWidth = 110f;  // The material selector at the bar's end (#109)
    [SerializeField] private float feedbackFlashSeconds = 0.25f;

    public DungeonBoard Board { get => dungeonBoard; set => dungeonBoard = value; }
    public BoardRenderer Renderer { get => boardRenderer; set => boardRenderer = value; }
    public Imp Imp { get => imp; set => imp = value; }
    public DigDesignator DigDesignator { get => digDesignator; set => digDesignator = value; }
    public PlacementManager PlacementManager { get => placementManager; set => placementManager = value; }
    public Camera MainCamera { get => mainCamera; set => mainCamera = value; }

    public PlaceableType? SelectedType { get; private set; }

    /// <summary>
    /// The bar's current material (#109): what an order of the selected type is placed in. Stone by default; selecting a
    /// type that can't be ordered in it falls back to that type's first cost row.
    /// </summary>
    public MaterialType SelectedMaterial { get; private set; } = MaterialType.Stone;
    public bool IsPlacing => SelectedType != null;
    public bool IsDragging => isDragging;

    /// <summary>How many placeables the last drag-release placed (0 = rejected).</summary>
    public int LastApplyCount { get; private set; }

    /// <summary>True while the red rejection preview is being shown after a 0-tile release.</summary>
    public bool IsShowingRejection => feedbackTimer > 0f;

    private static readonly PlaceableType[] BarTypes = { PlaceableType.LairCot, PlaceableType.MushroomPlot, PlaceableType.SpikeTrap };
    private static readonly string[] BarLabels = { "Lair Cot", "Mushroom Plot", "Spike Trap" };

    // ---- input ----
    private InputConfig.Actions input; // Bindings come from InputConfig (rebindable); enabled with this component

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
    private Text[] buttonLabels;
    private Text materialLabel;
    private float costRefreshTimer;

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
    }

    private void OnDestroy()
    {
        input?.Dispose();
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
        SelectedMaterial = MaterialFor(type);
        digReenableFrame = -1;
        if (digDesignator != null) digDesignator.enabled = false;
        RefreshButtons();
        RefreshCostLabels();
    }

    /// <summary>The materials the selected type can be ordered in (#109): its cost rows'; empty with no type selected.</summary>
    public List<MaterialType> AvailableMaterials =>
        SelectedType != null && placementManager != null ? placementManager.MaterialsFor(SelectedType.Value) : new List<MaterialType>();

    /// <summary>Sets the bar's material (#109), if the selected type (or, with none, any) can be ordered in it.</summary>
    public bool SelectMaterial(MaterialType material)
    {
        if (SelectedType != null && !AvailableMaterials.Contains(material)) return false;
        SelectedMaterial = material;
        RefreshCostLabels();
        return true;
    }

    /// <summary>The selector's click (#109): the next material the selected type can be ordered in, wrapping.</summary>
    public void CycleMaterial()
    {
        List<MaterialType> available = AvailableMaterials;
        if (available.Count == 0) return;
        int i = available.IndexOf(SelectedMaterial);
        SelectMaterial(available[(i + 1) % available.Count]);
    }

    // The material an order of this type would use: the bar's current material if the type has a row for it, else the
    // type's default (first row).
    private MaterialType MaterialFor(PlaceableType type)
    {
        if (placementManager == null) return SelectedMaterial;
        if (placementManager.TryGetCost(type, SelectedMaterial, out _)) return SelectedMaterial;
        return placementManager.TryGetDefaultMaterial(type, out MaterialType fallback) ? fallback : SelectedMaterial;
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

        costRefreshTimer += Time.unscaledDeltaTime; // Prices track the stockpile even while paused
        if (costRefreshTimer >= costRefreshSeconds)
        {
            costRefreshTimer = 0f;
            RefreshCostLabels();
        }

        if (feedbackTimer > 0f)
        {
            feedbackTimer -= Time.unscaledDeltaTime; // UI feedback: the rejection flash clears even while paused
            if (feedbackTimer <= 0f && !isDragging && previewSprite != null) previewSprite.enabled = false;
        }

        if (!IsPlacing || dungeonBoard == null || boardRenderer == null || mainCamera == null) return;

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
        bool esc = input?.Abort.WasPressedThisFrame() ?? false;
        bool leftPressed  = input?.Designate.WasPressedThisFrame() ?? false;
        bool rightPressed = input?.ClearOrAbort.WasPressedThisFrame() ?? false;

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
        bool esc = input?.Abort.WasPressedThisFrame() ?? false;
        bool rightPressed = input?.ClearOrAbort.WasPressedThisFrame() ?? false;

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

        bool leftReleased = input?.Designate.WasReleasedThisFrame() ?? false;
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
            ? placementManager.ApplyRect(SelectedType.Value, SelectedMaterial, rect, imp.CurrentTile)
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

        // Flat on the floor it places onto, just above the ground.
        previewSprite.transform.position   = new Vector3(center.x, boardRenderer.OverlayLift, center.z);
        previewSprite.transform.localScale = new Vector3(rect.width, rect.height, 1f);
        bool affordable = SelectedType == null || placementManager == null || placementManager.WouldFundNow(SelectedType.Value, SelectedMaterial);
        previewSprite.color   = !previewValid.Value ? invalidPreviewColor : affordable ? validPreviewColor : unaffordablePreviewColor;
        previewSprite.enabled = true;
    }

    private static bool IsPointerOverUI() =>
        EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

    // ---- visuals ----

    private void CreatePreview()
    {
        var go = new GameObject("PlacementPreview");
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // Lies flat (XZ), facing up
        previewSprite = go.AddComponent<SpriteRenderer>();
        previewSprite.sprite = CreateWhiteSprite("PlacementPreview_Sprite");
        previewSprite.color = validPreviewColor;
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

    internal static void EnsureEventSystem() // Shared with GameSpeed's buttons; guarded, so call order doesn't matter
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
            BarTypes.Length * buttonSize.x + (BarTypes.Length + 1) * barPadding + materialButtonWidth + barPadding,
            buttonSize.y + 2f * barPadding);

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        buttonImages = new Image[BarTypes.Length];
        buttonLabels = new Text[BarTypes.Length];

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
            text.supportRichText = true; // The cost is colored on its own
            text.raycastTarget = false;
            var textRect = textGo.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = textRect.offsetMax = Vector2.zero;

            buttonImages[i] = image;
            buttonLabels[i] = text;
        }

        CreateMaterialSelector(barGo.transform, font);

        RefreshButtons();
        RefreshCostLabels();
    }

    // The selector (#109): one button at the bar's end, reading the current material; a click cycles it.
    private void CreateMaterialSelector(Transform bar, Font font)
    {
        var go = new GameObject("MaterialSelector");
        go.transform.SetParent(bar, false);
        var image = go.AddComponent<Image>();
        image.color = buttonNormalColor;
        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(CycleMaterial);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = new Vector2(barPadding + BarTypes.Length * (buttonSize.x + barPadding), 0f);
        rect.sizeDelta = new Vector2(materialButtonWidth, buttonSize.y);

        var textGo = new GameObject("Label");
        textGo.transform.SetParent(go.transform, false);
        materialLabel = textGo.AddComponent<Text>();
        materialLabel.font = font;
        materialLabel.fontSize = 14;
        materialLabel.color = Color.white;
        materialLabel.alignment = TextAnchor.MiddleCenter;
        materialLabel.raycastTarget = false;
        var textRect = textGo.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = textRect.offsetMax = Vector2.zero;
    }

    /// <summary>
    /// Re-labels each button with its cost in the material an order of that type would use (#109), red where it
    /// wouldn't fund right now, and the selector with the current material's name.
    /// </summary>
    private void RefreshCostLabels()
    {
        if (placementManager == null) return;
        if (buttonLabels != null)
        {
            for (int i = 0; i < BarTypes.Length; i++)
            {
                if (buttonLabels[i] == null) continue;
                MaterialType material = MaterialFor(BarTypes[i]);
                buttonLabels[i].text = placementManager.TryGetCost(BarTypes[i], material, out MaterialAmount cost)
                    ? CostLabel(BarLabels[i], cost.amount, placementManager.WouldFundNow(BarTypes[i], material), unaffordableCostColor)
                    : BarLabels[i];
            }
        }
        if (materialLabel != null)
        {
            Stockpile stockpile = placementManager.Stockpile;
            materialLabel.text = stockpile != null ? stockpile.DisplayName(SelectedMaterial) : SelectedMaterial.ToString();
        }
    }

    /// <summary>A build button's label (#104): "Lair Cot · 3", the cost in rich-text red when it wouldn't fund now.</summary>
    public static string CostLabel(string name, int cost, bool affordable, Color unaffordableColor) =>
        affordable ? $"{name} · {cost}" : $"{name} · <color=#{ColorUtility.ToHtmlStringRGB(unaffordableColor)}>{cost}</color>";

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
