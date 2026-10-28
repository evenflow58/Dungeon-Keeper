using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

/// <summary>
/// The tool-mode owner (#123) and placement input. Three modes: Neutral (nothing selected; board clicks are inert),
/// Dig (DigDesignator enabled) and Armed (a placeable type selected; left-click/drag places on every valid tile in the
/// rect). The game starts in Neutral. Esc unwinds one level per press: a live drag aborts first, then the tool exits to
/// Neutral; right-click on the board exits Armed too (in Dig it is dig's own Clear verb). BuildRail is the view: it
/// calls this API and paints from it, as DetailCard does for the material; this class keeps no display data
/// (BuildPresentation holds that).
/// Placing is never blocked by cost (#104): the order waits for materials, and the drag preview takes the
/// unaffordable tint then. Orders are placed in the selected material (#109).
/// Wire mode (#142): the Power flyout's Lay conduit verb arms it; ConduitDesignator is enabled exactly then (as
/// DigDesignator is in Dig), left-drag lays conduit, right-drag removes it, and Esc exits unless a wire drag is live.
/// Pipe mode (#145): the Magic flyout's Lay pipe verb arms it, exactly as Wire mode is armed, with PipeDesignator.
/// Placed selection (#142): in Neutral only, a left-click on a built placeable's tile selects it (the detail card's placed
/// mode); a click on any other board tile clears it, and so do Esc (its own press: the rail and the STOCK panel leave that
/// press alone), leaving Neutral, and the STOCK panel selecting a material (most recent wins).
/// </summary>
[DisallowMultipleComponent]
public class PlacementController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private DungeonBoard dungeonBoard;
    [SerializeField] private BoardRenderer boardRenderer;
    [SerializeField] private Imp imp;
    [SerializeField] private DigDesignator digDesignator;
    [SerializeField] private ConduitDesignator conduitDesignator; // #142: enabled exactly in Wire mode
    [SerializeField] private PipeDesignator pipeDesignator;       // #145: enabled exactly in Pipe mode
    [SerializeField] private PlacementManager placementManager;
    [SerializeField] private Camera mainCamera;

    [Header("Drag Preview")]
    [SerializeField] private Color validPreviewColor = new Color(0.4f, 0.9f, 0.4f, 0.35f);
    [SerializeField] private Color invalidPreviewColor = new Color(0.9f, 0.3f, 0.3f, 0.35f);
    [SerializeField] private Color unaffordablePreviewColor = new Color(0.95f, 0.45f, 0.2f, 0.35f); // Placeable, but it will wait (#104)

    [SerializeField] private float feedbackFlashSeconds = 0.25f;

    public DungeonBoard Board { get => dungeonBoard; set => dungeonBoard = value; }
    public BoardRenderer Renderer { get => boardRenderer; set => boardRenderer = value; }
    public Imp Imp { get => imp; set => imp = value; }
    public DigDesignator DigDesignator { get => digDesignator; set => digDesignator = value; }
    public ConduitDesignator ConduitDesignator { get => conduitDesignator; set => conduitDesignator = value; }
    public PipeDesignator PipeDesignator { get => pipeDesignator; set => pipeDesignator = value; }
    public PlacementManager PlacementManager { get => placementManager; set => placementManager = value; }
    public Camera MainCamera { get => mainCamera; set => mainCamera = value; }

    public PlaceableType? SelectedType { get; private set; }

    /// <summary>
    /// The current material (#109): what an order of the selected type is placed in. Stone by default; selecting a
    /// type that can't be ordered in it falls back to that type's first cost row.
    /// </summary>
    public MaterialType SelectedMaterial { get; private set; } = MaterialType.Stone;
    public bool IsPlacing => SelectedType != null;
    public bool IsDragging => isDragging;

    public enum ToolMode { Neutral, Dig, Armed, Wire, Pipe }

    /// <summary>True in Dig mode (#123): the dig tool is selected and DigDesignator is enabled.</summary>
    public bool IsDigging { get; private set; }

    /// <summary>True in Wire mode (#142): Lay conduit is armed and ConduitDesignator is enabled.</summary>
    public bool IsWiring { get; private set; }

    /// <summary>True in Pipe mode (#145): Lay pipe is armed and PipeDesignator is enabled.</summary>
    public bool IsPiping { get; private set; }

    public ToolMode Mode => IsPlacing ? ToolMode.Armed : IsDigging ? ToolMode.Dig : IsWiring ? ToolMode.Wire
        : IsPiping ? ToolMode.Pipe : ToolMode.Neutral;

    /// <summary>
    /// The built placeable selected in Neutral (#142): the detail card's placed mode shows it. Null when nothing is
    /// selected; never set outside Neutral.
    /// </summary>
    public Placeable SelectedPlaceable => selectedPlaceable != null ? selectedPlaceable : null; // Unity null: a destroyed one reads null

    public bool HasPlacedSelection => SelectedPlaceable != null;

    /// <summary>
    /// Raised whenever the mode, the armed type, the selected material or the placed selection changes, so the chrome
    /// repaints at once.
    /// </summary>
    public event System.Action ModeChanged;

    /// <summary>
    /// True on the frame a board Esc / right-click exited a tool here, or an Esc cleared the placed selection (#142).
    /// BuildRail and StockPanel check it so the same press can't also close their panels, whichever updates first.
    /// </summary>
    public bool ExitedToolThisFrame => toolExitFrame == Time.frameCount;

    /// <summary>How many placeables the last drag-release placed (0 = rejected).</summary>
    public int LastApplyCount { get; private set; }

    /// <summary>True while the red rejection preview is being shown after a 0-tile release.</summary>
    public bool IsShowingRejection => feedbackTimer > 0f;

    // ---- input ----
    private InputConfig.Actions input; // Bindings come from InputConfig (rebindable); enabled with this component

    // ---- drag state ----
    private bool isDragging;
    private bool dragEnteredBoard; // true once the cursor enters the board; gates apply & preview
    private Vector2Int dragAnchor;
    private Vector2Int dragCurrent;
    private float feedbackTimer;

    // ---- mode state ----
    private int toolExitFrame = -1;
    private bool digWasDragging; // DigDesignator's drag state as of our last Update: an Esc that aborted a dig drag is not ours
    private bool wireWasDragging; // The same for ConduitDesignator in Wire mode (#142)
    private bool pipeWasDragging; // The same for PipeDesignator in Pipe mode (#145)
    private Placeable selectedPlaceable;

    // ---- visuals ----
    private SpriteRenderer previewSprite;

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
    }

    private void OnDestroy()
    {
        input?.Dispose();
        if (previewSprite != null) Destroy(previewSprite.gameObject);
    }

    private void Start()
    {
        dungeonBoard ??= FindAnyObjectByType<DungeonBoard>();
        boardRenderer ??= FindAnyObjectByType<BoardRenderer>();
        imp ??= FindAnyObjectByType<Imp>();
        digDesignator ??= FindAnyObjectByType<DigDesignator>();
        conduitDesignator ??= FindAnyObjectByType<ConduitDesignator>();
        pipeDesignator ??= FindAnyObjectByType<PipeDesignator>();
        placementManager ??= FindAnyObjectByType<PlacementManager>();
        mainCamera ??= Camera.main;

        CreatePreview();
        EnsureEventSystem();
        ApplyMode(); // The mode decides DigDesignator's enablement, whatever the scene checkbox says: Neutral at start
    }

    // ---- mode API ----

    /// <summary>Arms the type (Armed mode); selecting the armed type again exits to Neutral.</summary>
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
        IsDigging = false;
        IsWiring = false;
        IsPiping = false;
        ApplyMode();
    }

    /// <summary>Selects the dig tool (Dig mode): disarms any type and enables dig designation.</summary>
    public void SelectDig()
    {
        CancelDrag();
        SelectedType = null;
        IsDigging = true;
        IsWiring = false;
        IsPiping = false;
        ApplyMode();
    }

    /// <summary>Arms Lay conduit (Wire mode, #142); selecting it again while armed exits to Neutral, like a type tile.</summary>
    public void SelectWire()
    {
        if (IsWiring)
        {
            ClearSelection();
            return;
        }
        CancelDrag();
        SelectedType = null;
        IsDigging = false;
        IsWiring = true;
        IsPiping = false;
        ApplyMode();
    }

    /// <summary>Arms Lay pipe (Pipe mode, #145); selecting it again while armed exits to Neutral, like Lay conduit.</summary>
    public void SelectPipe()
    {
        if (IsPiping)
        {
            ClearSelection();
            return;
        }
        CancelDrag();
        SelectedType = null;
        IsDigging = false;
        IsWiring = false;
        IsPiping = true;
        ApplyMode();
    }

    /// <summary>Selects a built placeable (#142): Neutral only, built only. False (and nothing changes) otherwise.</summary>
    public bool SelectPlaced(Placeable placeable)
    {
        if (Mode != ToolMode.Neutral || placeable == null || !placeable.IsBuilt) return false;
        if (selectedPlaceable == placeable) return true;
        selectedPlaceable = placeable;
        ModeChanged?.Invoke();
        return true;
    }

    /// <summary>Clears the placed selection (#142). No-op when nothing is selected.</summary>
    public void ClearPlacedSelection()
    {
        if (selectedPlaceable == null) return;
        selectedPlaceable = null;
        ModeChanged?.Invoke();
    }

    /// <summary>
    /// A Neutral board click (#142): selects the built placeable on the tile, else clears the selection (an empty tile, a
    /// site, an order). Ignored outside Neutral, where board clicks keep their tool meanings.
    /// </summary>
    public void ClickBoardTile(Vector2Int tile)
    {
        if (Mode != ToolMode.Neutral) return;
        Placeable placeable = placementManager != null ? placementManager.GetAt(tile) : null;
        if (placeable != null && placeable.IsBuilt) SelectPlaced(placeable);
        else ClearPlacedSelection();
        NeutralTileClicked?.Invoke(tile);
    }

    /// <summary>
    /// Raised after a Neutral board click is handled, with the clicked tile (#161: the Heart panel opens on a click on the
    /// Heart's tile). Never raised outside Neutral.
    /// </summary>
    public event System.Action<Vector2Int> NeutralTileClicked;

    /// <summary>
    /// The materials the selected type can be ordered in (#109): its cost rows', limited to discovered materials when a
    /// stockpile is wired (#126; unwired, every row's, as WouldFundNow degrades). Empty with no type selected.
    /// </summary>
    public List<MaterialType> AvailableMaterials =>
        SelectedType != null ? DiscoveredMaterialsFor(SelectedType.Value) : new List<MaterialType>();

    /// <summary>
    /// A type's orderable materials under the discovery rule (#126): its row materials in table order, without the ones
    /// the stockpile has never held. With no manager, none; with no stockpile, every row's.
    /// </summary>
    public List<MaterialType> DiscoveredMaterialsFor(PlaceableType type) => DiscoveredMaterialsFor(placementManager, type);

    public static List<MaterialType> DiscoveredMaterialsFor(PlacementManager manager, PlaceableType type)
    {
        if (manager == null) return new List<MaterialType>();
        List<MaterialType> materials = manager.MaterialsFor(type);
        Stockpile stockpile = manager.Stockpile;
        if (stockpile != null) materials.RemoveAll(m => !stockpile.IsDiscovered(m)); // Unity null check: unwired keeps all
        return materials;
    }

    /// <summary>Sets the current material (#109), if the selected type (or, with none, any) can be ordered in it.</summary>
    public bool SelectMaterial(MaterialType material)
    {
        if (SelectedType != null && !AvailableMaterials.Contains(material)) return false;
        SelectedMaterial = material;
        ModeChanged?.Invoke();
        return true;
    }

    /// <summary>The next material the selected type can be ordered in, wrapping (#109). The detail card's chips select directly.</summary>
    public void CycleMaterial()
    {
        List<MaterialType> available = AvailableMaterials;
        if (available.Count == 0) return;
        int i = available.IndexOf(SelectedMaterial);
        SelectMaterial(available[(i + 1) % available.Count]);
    }

    /// <summary>
    /// The material an order of this type would use: the current material if the type can be ordered in it (a row, and
    /// discovered, #126), else the type's first discovered row in table order. Safety net: a type with no discovered row
    /// falls back to its first row, so no type ever becomes unorderable.
    /// </summary>
    public MaterialType MaterialFor(PlaceableType type)
    {
        if (placementManager == null) return SelectedMaterial;
        List<MaterialType> orderable = DiscoveredMaterialsFor(type);
        if (orderable.Contains(SelectedMaterial)) return SelectedMaterial;
        if (orderable.Count > 0) return orderable[0];
        return placementManager.TryGetDefaultMaterial(type, out MaterialType fallback) ? fallback : SelectedMaterial;
    }

    /// <summary>Exits the current tool, armed, dig or wire, to Neutral: nothing selected, designation off.</summary>
    public void ClearSelection()
    {
        CancelDrag();
        SelectedType = null;
        IsDigging = false;
        IsWiring = false;
        IsPiping = false;
        ApplyMode();
    }

    /// <summary>
    /// Esc in Wire (#142): exits to Neutral unless a wire drag is live, or was when this component last updated (the
    /// designator aborts its own drag on that press). False when the press isn't ours.
    /// </summary>
    public bool TryExitWireOnAbort()
    {
        if (!IsWiring) return false;
        if (wireWasDragging || (conduitDesignator != null && conduitDesignator.IsDragging)) return false;
        ExitFromBoardInput();
        return true;
    }

    /// <summary>Esc in Pipe (#145): exits to Neutral unless a pipe drag is live, or was last update. False when not ours.</summary>
    public bool TryExitPipeOnAbort()
    {
        if (!IsPiping) return false;
        if (pipeWasDragging || (pipeDesignator != null && pipeDesignator.IsDragging)) return false;
        ExitFromBoardInput();
        return true;
    }

    /// <summary>
    /// Esc in Dig: exits to Neutral unless a dig drag is live, or was when this component last updated (DigDesignator
    /// aborts its own drag on that press, and may have done so already this frame). False when the press isn't ours.
    /// </summary>
    public bool TryExitDigOnAbort()
    {
        if (!IsDigging) return false;
        if (digWasDragging || (digDesignator != null && digDesignator.IsDragging)) return false;
        ExitFromBoardInput();
        return true;
    }

    // A board Esc / right-click exit. DigDesignator stays disabled in Neutral, so the press can't reach it.
    private void ExitFromBoardInput()
    {
        toolExitFrame = Time.frameCount;
        ClearSelection();
    }

    /// <summary>
    /// Enforces the mode on the designators: DigDesignator enabled in Dig, ConduitDesignator in Wire, and at no other
    /// time. Leaving Neutral clears the placed selection. Start calls this too.
    /// </summary>
    public void ApplyMode()
    {
        if (digDesignator != null) digDesignator.enabled = IsDigging;
        if (conduitDesignator != null) conduitDesignator.enabled = IsWiring;
        if (pipeDesignator != null) pipeDesignator.enabled = IsPiping;
        if (Mode != ToolMode.Neutral) selectedPlaceable = null;
        ModeChanged?.Invoke();
    }

    // ---- update ----

    private void Update()
    {
        if (IsDigging && (input?.Abort.WasPressedThisFrame() ?? false)) TryExitDigOnAbort();
        digWasDragging = IsDigging && digDesignator != null && digDesignator.IsDragging;
        if (IsWiring && (input?.Abort.WasPressedThisFrame() ?? false)) TryExitWireOnAbort();
        wireWasDragging = IsWiring && conduitDesignator != null && conduitDesignator.IsDragging;
        if (IsPiping && (input?.Abort.WasPressedThisFrame() ?? false)) TryExitPipeOnAbort();
        pipeWasDragging = IsPiping && pipeDesignator != null && pipeDesignator.IsDragging;
        if (Mode == ToolMode.Neutral) HandleNeutralInput();

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

    // Neutral (#142): Esc clears the placed selection first (its own press); a board click selects or clears.
    private void HandleNeutralInput()
    {
        if (HasPlacedSelection && !TextInputFocus.IsTyping && (input?.Abort.WasPressedThisFrame() ?? false))
        {
            toolExitFrame = Time.frameCount; // The rail and the STOCK panel leave this press alone
            ClearPlacedSelection();
            return;
        }

        if (!(input?.Designate.WasPressedThisFrame() ?? false) || IsPointerOverUI()) return;
        if (dungeonBoard == null || boardRenderer == null || mainCamera == null) return;
        Vector2 mouseScreen = input?.PointerPosition.ReadValue<Vector2>() ?? Vector2.zero;
        if (!boardRenderer.RaycastBoard(mainCamera.ScreenPointToRay(mouseScreen), out Vector3 worldPos)) return;
        ClickBoardTile(boardRenderer.WorldToBoardCoords(worldPos));
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

        // Presses that start on the build rail (or any UI) never reach the board.
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

        // During a drag, Esc / right-click abort the drag only (the type stays armed).
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

    /// <summary>A cost-carrying label (#104): "Lair Cot · 3", the cost in rich-text red when it wouldn't fund now.</summary>
    public static string CostLabel(string name, int cost, bool affordable, Color unaffordableColor) =>
        $"{name} · {CostText(cost, affordable, unaffordableColor)}";

    /// <summary>The cost number on its own, in rich-text red when it wouldn't fund now.</summary>
    public static string CostText(int cost, bool affordable, Color unaffordableColor) =>
        affordable ? cost.ToString() : $"<color=#{ColorUtility.ToHtmlStringRGB(unaffordableColor)}>{cost}</color>";
}
