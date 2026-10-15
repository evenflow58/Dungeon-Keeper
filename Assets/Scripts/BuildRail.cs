using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// The build chrome (#123): a slim icon rail at the left edge (DIG, then one entry per non-empty build category) and a
/// flyout of item tiles beside it. It is the tool mode's view: every behavior stays in PlacementController (the mode
/// owner) and PlacementManager (costs, funding); this class only tracks which category's flyout is open, calls their
/// API and paints from it. Presentation data comes from BuildPresentation. The armed type's description and its
/// material choice are DetailCard's (#125). Below the categories, set apart, the STOCK entry opens StockPanel (#126);
/// at most one of {a category's flyout, the STOCK panel} is open. Built in code like the rest of the chrome
/// (own overlay Canvas, no scene-authored UI). Digits 1-6 activate the rail's visible entries in order, except while the
/// player types into a field (TextInputFocus). Esc closes the flyout only from Neutral: while a tool is selected the controller owns the press, so one press
/// unwinds one level.
/// </summary>
[DisallowMultipleComponent]
public class BuildRail : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlacementController placementController;
    [SerializeField] private PlacementManager placementManager;
    [SerializeField] private StockPanel stockPanel; // #126: what the STOCK entry opens

    [Header("Layout")]
    [SerializeField] private float railWidth = 54f;
    [SerializeField] private float flyoutWidth = 200f;
    [SerializeField] private float topOffset = 36f;          // The top bar's height: the chrome starts below it
    [SerializeField] private float entryHeight = 52f;
    [SerializeField] private float stockGap = 14f;           // Sets the STOCK entry apart from the build categories
    [SerializeField] private float tileHeight = 46f;
    [SerializeField] private float titleHeight = 24f;
    [SerializeField] private float padding = 4f;
    [SerializeField] private float iconSize = 20f;
    [SerializeField] private float iconInset = 8f;           // Icon to its entry's top edge / its tile's left edge, and on to the tile text
    [SerializeField] private float refreshSeconds = 0.25f;   // How often costs and tints re-check (unscaled)

    [Header("Colors")]
    [SerializeField] private Color panelColor = new Color(0f, 0f, 0f, 0.6f);
    [SerializeField] private Color buttonNormalColor = new Color(0.25f, 0.25f, 0.30f, 1f);
    [SerializeField] private Color buttonSelectedColor = new Color(0.85f, 0.65f, 0.15f, 1f);
    [SerializeField] private Color unaffordableCostColor = new Color(1f, 0.38f, 0.38f, 1f);
    [SerializeField] private Color secondaryTextColor = new Color(0.82f, 0.82f, 0.82f, 1f);

    public PlacementController PlacementController { get => placementController; set => placementController = value; }
    public PlacementManager PlacementManager { get => placementManager; set => placementManager = value; }
    public StockPanel StockPanel { get => stockPanel; set => stockPanel = value; }
    public Color ButtonNormalColor => buttonNormalColor;
    public Color ButtonSelectedColor => buttonSelectedColor;
    public Color UnaffordableCostColor => unaffordableCostColor;

    /// <summary>The category whose flyout is open; null while the flyout is closed.</summary>
    public BuildCategory? OpenCategory { get; private set; }
    public bool IsFlyoutOpen => OpenCategory != null;

    /// <summary>True while the STOCK panel is open (#126).</summary>
    public bool IsStockOpen => stockPanel != null && stockPanel.IsOpen;

    /// <summary>One rail entry: the dig tool, a build category, or STOCK (#126).</summary>
    public readonly struct RailEntry
    {
        public readonly bool isDig;
        public readonly BuildCategory category; // Meaningless for the dig and STOCK entries
        public readonly bool isStock;

        public RailEntry(bool isDig, BuildCategory category, bool isStock = false)
        {
            this.isDig = isDig;
            this.category = category;
            this.isStock = isStock;
        }
    }

    // ---- input ----
    private InputConfig.Actions input; // Bindings come from InputConfig (rebindable); enabled with this component
    private float refreshTimer;

    // ---- chrome (code-built; a recompile can null or empty any of it, so every reader guards for null and length) ----
    private Canvas canvas;
    // #137: true once Build has wired this chrome in the current domain. NonSerialized, so a domain reload resets it
    // while the canvas reference survives: that pair is how OnEnable tells a reload (listeners and the ModeChanged
    // subscription lost) from an ordinary re-enable.
    [System.NonSerialized] private bool chromeWired;
    private Image[] entryImages;
    private RailEntry[] builtEntries;
    private RectTransform flyoutRect;
    private Text flyoutTitle;
    private PlaceableType[] tileTypes;
    private RectTransform[] tileRects;
    private Image[] tileImages;
    private Text[] tileCostLabels;
    private readonly List<Object> generatedAssets = new List<Object>(); // Icon textures and sprites, destroyed with the chrome

    // ---- lifecycle ----

    private void Awake()
    {
        EnsureInput();
    }

    // #137: a domain reload mid-Play (Recompile And Continue Playing) re-runs OnEnable but not Awake or Start, so the
    // input is ensured here and chrome that outlived the reload is rebuilt with its wiring.
    private void OnEnable()
    {
        EnsureInput();
        input.Enable();
        if (canvas != null && !chromeWired) RebuildAfterReload();
    }

    private void EnsureInput() => input ??= InputConfig.CreateActions();

    private void RebuildAfterReload()
    {
        DestroyChrome();
        Build();
        SubscribeToMode();
    }

    // Idempotent: Start and a reload both come through here, and the rail must hear each mode change once.
    private void SubscribeToMode()
    {
        if (placementController == null) return;
        placementController.ModeChanged -= Repaint;
        placementController.ModeChanged += Repaint; // Tints follow the mode at once
    }

    private void OnDisable()
    {
        input?.Disable();
    }

    private void OnDestroy()
    {
        input?.Dispose();
        if (placementController != null) placementController.ModeChanged -= Repaint;
        DestroyChrome();
    }

    private void Start()
    {
        placementController ??= FindAnyObjectByType<PlacementController>();
        placementManager ??= FindAnyObjectByType<PlacementManager>();
        stockPanel ??= FindAnyObjectByType<StockPanel>();

        PlacementController.EnsureEventSystem();
        Build();
        SubscribeToMode();
    }

    private void Update()
    {
        if (!TextInputFocus.IsTyping) // #126: digits and Esc typed into a field are the field's
        {
            for (int i = 0; i < RailActionCount; i++)
            {
                if (RailAction(i)?.WasPressedThisFrame() ?? false) ActivateEntry(i);
            }
            if (input?.Abort.WasPressedThisFrame() ?? false) HandleAbort();
        }

        refreshTimer += Time.unscaledDeltaTime; // Affordability tracks the stockpile even while paused
        if (refreshTimer >= refreshSeconds)
        {
            refreshTimer = 0f;
            Repaint();
        }
    }

    private const int RailActionCount = 6;

    private InputAction RailAction(int index)
    {
        if (input == null) return null;
        switch (index)
        {
            case 0: return input.Rail1;
            case 1: return input.Rail2;
            case 2: return input.Rail3;
            case 3: return input.Rail4;
            case 4: return input.Rail5;
            case 5: return input.Rail6;
            default: return null;
        }
    }

    // ---- rail state (the view's own: which flyout is open) ----

    /// <summary>The rail's entries top to bottom: DIG, each non-empty category in presentation order, then STOCK.</summary>
    public static List<RailEntry> VisibleEntries()
    {
        var entries = new List<RailEntry> { new RailEntry(true, default) };
        foreach (BuildCategory category in BuildPresentation.VisibleCategories()) entries.Add(new RailEntry(false, category));
        entries.Add(new RailEntry(false, default, isStock: true));
        return entries;
    }

    /// <summary>
    /// A click on the index-th visible entry, or its digit (index 0 = digit 1): DIG selects the dig tool, a category
    /// toggles its flyout, STOCK toggles the materials panel. False, and nothing happens, for an index past the visible
    /// entries.
    /// </summary>
    public bool ActivateEntry(int index)
    {
        List<RailEntry> entries = VisibleEntries();
        if (index < 0 || index >= entries.Count) return false;

        if (entries[index].isDig) ActivateDig();
        else if (entries[index].isStock) ToggleStock();
        else ToggleCategory(entries[index].category);
        return true;
    }

    /// <summary>The DIG entry: closes the flyout and the STOCK panel and selects the dig tool.</summary>
    public void ActivateDig()
    {
        CloseFlyout();
        if (stockPanel != null) stockPanel.Close();
        if (placementController != null) placementController.SelectDig();
        Repaint();
    }

    /// <summary>
    /// The STOCK entry (#126): opens the materials panel (closing any flyout; the panel puts any tool down), or closes it
    /// if it is open. No panel wired, no effect.
    /// </summary>
    public void ToggleStock()
    {
        if (stockPanel == null) return;
        if (stockPanel.IsOpen) stockPanel.Close();
        else
        {
            CloseFlyout();
            stockPanel.Open();
        }
        Repaint();
    }

    /// <summary>A category entry: opens its flyout, or closes it if it is the open one.</summary>
    public void ToggleCategory(BuildCategory category)
    {
        if (OpenCategory == category) CloseFlyout();
        else OpenFlyout(category);
    }

    /// <summary>
    /// Opens the category's flyout (a category with no types has none). Any other open flyout closes first, and the
    /// dig tool is put down: opening a category from Dig leaves the mode Neutral.
    /// </summary>
    public void OpenFlyout(BuildCategory category)
    {
        if (OpenCategory == category) return;
        if (BuildPresentation.TypesInCategory(category).Count == 0) return;

        CloseFlyout();
        if (stockPanel != null) stockPanel.Close(); // One open thing: the flyout or the STOCK panel
        if (placementController != null && placementController.IsDigging) placementController.ClearSelection();
        OpenCategory = category;
        Repaint();
    }

    /// <summary>
    /// Closes the flyout. A type armed from the closing category is disarmed first, so what's open never disagrees
    /// with what's armed.
    /// </summary>
    public void CloseFlyout()
    {
        if (OpenCategory == null) return;

        BuildCategory closing = OpenCategory.Value;
        OpenCategory = null;
        if (IsArmedIn(closing)) placementController.ClearSelection();
        Repaint();
    }

    /// <summary>A tile's click: arms its type through the controller (whose toggle stands: the armed tile disarms).</summary>
    public void SelectTile(PlaceableType type)
    {
        if (placementController != null) placementController.Select(type);
        Repaint();
    }

    /// <summary>
    /// Esc at the neutral level: closes the open flyout. While a tool is selected the press is the controller's (disarm,
    /// or exit dig), as it is on the frame the controller just spent it. True when the flyout closed.
    /// </summary>
    public bool HandleAbort()
    {
        if (OpenCategory == null) return false;
        if (placementController != null &&
            (placementController.Mode != PlacementController.ToolMode.Neutral || placementController.ExitedToolThisFrame))
            return false;

        CloseFlyout();
        return true;
    }

    private bool IsArmedIn(BuildCategory category) =>
        placementController != null && placementController.SelectedType != null &&
        BuildPresentation.TryGetCategory(placementController.SelectedType.Value, out BuildCategory armed) && armed == category;

    // ---- what the tiles read ----

    /// <summary>
    /// The type's cheapest cost row among the materials it can be ordered in (#126: the discovered ones, as on the detail
    /// card's chips): the least amount, the first such row on a tie. A type with no discovered row reads all its rows,
    /// matching the controller's safety-net fallback.
    /// </summary>
    public static bool TryGetCheapestCost(PlacementManager manager, PlaceableType type, out MaterialAmount cheapest)
    {
        cheapest = default;
        if (manager == null) return false;

        List<MaterialType> materials = PlacementController.DiscoveredMaterialsFor(manager, type);
        if (materials.Count == 0) materials = manager.MaterialsFor(type);
        bool found = false;
        foreach (MaterialType material in materials)
        {
            if (!manager.TryGetCost(type, material, out MaterialAmount cost)) continue;
            if (found && cost.amount >= cheapest.amount) continue;
            cheapest = cost;
            found = true;
        }
        return found;
    }

    /// <summary>A tile's cost line: "from 3 Stone", the amount red when that row wouldn't fund now. Empty with no row.</summary>
    public string TileCostLine(PlaceableType type)
    {
        if (!TryGetCheapestCost(placementManager, type, out MaterialAmount cost)) return "";
        bool affordable = placementManager.WouldFundNow(type, cost.type);
        return $"from {PlacementController.CostText(cost.amount, affordable, unaffordableCostColor)} {MaterialName(cost.type)}";
    }

    private string MaterialName(MaterialType material)
    {
        Stockpile stockpile = placementManager != null ? placementManager.Stockpile : null;
        return stockpile != null ? stockpile.DisplayName(material) : material.ToString();
    }

    // ---- chrome ----

    /// <summary>Builds the rail and the (hidden) flyout. Start calls this; public so tests can paint without Play.</summary>
    public void Build()
    {
        if (canvas != null) return;

        var canvasGo = new GameObject("BuildRailCanvas");
        canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGo.AddComponent<CanvasScaler>();
        canvasGo.AddComponent<GraphicRaycaster>(); // Buttons need a raycaster to receive clicks

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        CreateRail(canvasGo.transform, font);
        CreateFlyout(canvasGo.transform, font);
        chromeWired = true;
        Repaint();
    }

    /// <summary>Destroys everything Build made.</summary>
    public void DestroyChrome()
    {
        if (canvas != null) DestroyChromeObject(canvas.gameObject);
        foreach (Object asset in generatedAssets)
            if (asset != null) DestroyChromeObject(asset);
        generatedAssets.Clear();

        canvas = null;
        chromeWired = false;
        entryImages = null;
        builtEntries = null;
        flyoutRect = null;
        flyoutTitle = null;
        tileTypes = null;
        tileRects = null;
        tileImages = null;
        tileCostLabels = null;
    }

    private static void DestroyChromeObject(Object target)
    {
        if (Application.isPlaying) Destroy(target);
        else DestroyImmediate(target);
    }

    // A left-edge panel from under the top bar to the screen bottom, entries stacked from its top.
    private void CreateRail(Transform parent, Font font)
    {
        var railGo = new GameObject("Rail");
        railGo.transform.SetParent(parent, false);
        railGo.AddComponent<Image>().color = panelColor; // A raycast target: a press on the rail never reaches the board
        var railRect = railGo.GetComponent<RectTransform>();
        railRect.anchorMin = new Vector2(0f, 0f);
        railRect.anchorMax = new Vector2(0f, 1f);
        railRect.pivot = new Vector2(0f, 1f);
        railRect.offsetMin = new Vector2(0f, 0f);
        railRect.offsetMax = new Vector2(railWidth, -topOffset);

        List<RailEntry> entries = VisibleEntries();
        builtEntries = entries.ToArray();
        entryImages = new Image[entries.Count];

        for (int i = 0; i < entries.Count; i++)
        {
            int index = i;
            RailEntry entry = entries[i];
            string label = entry.isDig ? BuildPresentation.DigLabel
                : entry.isStock ? BuildPresentation.StockLabel : BuildPresentation.RailLabel(entry.category);
            BuildIconShape shape = entry.isDig ? BuildPresentation.DigShape
                : entry.isStock ? BuildPresentation.StockShape : BuildPresentation.Shape(entry.category);
            Color accent = entry.isDig ? BuildPresentation.DigAccent
                : entry.isStock ? BuildPresentation.StockAccent : BuildPresentation.Accent(entry.category);
            string name = entry.isDig ? "Dig" : entry.isStock ? "Stock" : entry.category.ToString();

            GameObject entryGo = CreateButton(railGo.transform, name + "Entry", () => ActivateEntry(index), out entryImages[i]);
            float gap = entry.isStock ? stockGap : 0f; // STOCK sits apart, below the build categories
            PlaceTopLeft((RectTransform)entryGo.transform, padding, padding + i * (entryHeight + padding) + gap,
                railWidth - 2f * padding, entryHeight);

            Image icon = CreateIcon(entryGo.transform, shape, accent);
            icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            icon.rectTransform.pivot = new Vector2(0.5f, 1f);
            icon.rectTransform.anchoredPosition = new Vector2(0f, -iconInset);
            icon.rectTransform.sizeDelta = new Vector2(iconSize, iconSize);

            Text micro = CreateText(entryGo.transform, "Label", font, 9, Color.white, TextAnchor.LowerCenter);
            micro.text = label;
            micro.horizontalOverflow = HorizontalWrapMode.Overflow;
            Stretch(micro.rectTransform, 0f, 5f, 0f, 0f);
        }
    }

    // One panel beside the rail holding every type's tile; Repaint shows the open category's and sizes the panel to them.
    private void CreateFlyout(Transform parent, Font font)
    {
        var flyoutGo = new GameObject("Flyout");
        flyoutGo.transform.SetParent(parent, false);
        flyoutGo.AddComponent<Image>().color = panelColor;
        flyoutRect = flyoutGo.GetComponent<RectTransform>();
        flyoutRect.anchorMin = flyoutRect.anchorMax = new Vector2(0f, 1f);
        flyoutRect.pivot = new Vector2(0f, 1f);
        flyoutRect.anchoredPosition = new Vector2(railWidth, -topOffset);

        flyoutTitle = CreateText(flyoutGo.transform, "Title", font, 13, secondaryTextColor, TextAnchor.MiddleLeft);
        PlaceTopLeft(flyoutTitle.rectTransform, iconInset, padding, flyoutWidth - 2f * iconInset, titleHeight);

        var types = new List<PlaceableType>();
        foreach (BuildCategory category in BuildPresentation.VisibleCategories()) types.AddRange(BuildPresentation.TypesInCategory(category));
        tileTypes = types.ToArray();
        tileRects = new RectTransform[tileTypes.Length];
        tileImages = new Image[tileTypes.Length];
        tileCostLabels = new Text[tileTypes.Length];

        for (int i = 0; i < tileTypes.Length; i++)
        {
            PlaceableType type = tileTypes[i];
            BuildPresentation.TryGet(type, out BuildPresentation.Entry entry);
            BuildPresentation.TryGetCategory(type, out BuildCategory category);

            GameObject tileGo = CreateButton(flyoutGo.transform, type + "Tile", () => SelectTile(type), out tileImages[i]);
            tileRects[i] = (RectTransform)tileGo.transform;

            Image icon = CreateIcon(tileGo.transform, BuildPresentation.Shape(category), entry?.accent ?? Color.white);
            icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            icon.rectTransform.pivot = new Vector2(0f, 0.5f);
            icon.rectTransform.anchoredPosition = new Vector2(iconInset, 0f);
            icon.rectTransform.sizeDelta = new Vector2(iconSize, iconSize);

            float textLeft = iconInset + iconSize + iconInset;
            Text name = CreateText(tileGo.transform, "Name", font, 13, Color.white, TextAnchor.LowerLeft);
            name.text = BuildPresentation.DisplayName(type);
            name.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            name.rectTransform.anchorMax = new Vector2(1f, 1f);
            name.rectTransform.offsetMin = new Vector2(textLeft, 0f);
            name.rectTransform.offsetMax = new Vector2(-4f, -4f);

            Text cost = CreateText(tileGo.transform, "Cost", font, 11, secondaryTextColor, TextAnchor.UpperLeft);
            cost.supportRichText = true; // The amount is colored on its own
            cost.rectTransform.anchorMin = new Vector2(0f, 0f);
            cost.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            cost.rectTransform.offsetMin = new Vector2(textLeft, 4f);
            cost.rectTransform.offsetMax = new Vector2(-4f, -1f);
            tileCostLabels[i] = cost;
        }

        flyoutGo.SetActive(false);
    }

    /// <summary>
    /// Repaints from the mode and the stockpile: the rail's selected tints (DIG while digging, a category while its
    /// flyout is open, STOCK while its panel is open), and the open category's tiles with their cost lines and armed tint.
    /// </summary>
    public void Repaint()
    {
        if (canvas == null) return; // Not built (EditMode), or lost to a recompile

        if (entryImages != null && builtEntries != null)
        {
            for (int i = 0; i < entryImages.Length && i < builtEntries.Length; i++)
            {
                if (entryImages[i] == null) continue;
                bool selected = builtEntries[i].isDig ? placementController != null && placementController.IsDigging
                    : builtEntries[i].isStock ? IsStockOpen
                    : OpenCategory == builtEntries[i].category;
                entryImages[i].color = selected ? buttonSelectedColor : buttonNormalColor;
            }
        }

        if (flyoutRect == null) return;
        flyoutRect.gameObject.SetActive(OpenCategory != null);
        if (OpenCategory == null) return;

        if (flyoutTitle != null) flyoutTitle.text = BuildPresentation.CategoryName(OpenCategory.Value);

        float y = padding + titleHeight + padding;
        if (tileTypes != null && tileRects != null)
        {
            for (int i = 0; i < tileTypes.Length && i < tileRects.Length; i++)
            {
                if (tileRects[i] == null) continue;
                bool shown = BuildPresentation.TryGetCategory(tileTypes[i], out BuildCategory category) && category == OpenCategory;
                tileRects[i].gameObject.SetActive(shown);
                if (!shown) continue;

                PlaceTopLeft(tileRects[i], padding, y, flyoutWidth - 2f * padding, tileHeight);
                y += tileHeight + padding;

                if (tileImages != null && i < tileImages.Length && tileImages[i] != null)
                {
                    bool armed = placementController != null && placementController.SelectedType == tileTypes[i];
                    tileImages[i].color = armed ? buttonSelectedColor : buttonNormalColor;
                }
                if (tileCostLabels != null && i < tileCostLabels.Length && tileCostLabels[i] != null)
                    tileCostLabels[i].text = TileCostLine(tileTypes[i]);
            }
        }

        flyoutRect.sizeDelta = new Vector2(flyoutWidth, y);
    }

    // ---- chrome helpers ----

    private GameObject CreateButton(Transform parent, string name, UnityEngine.Events.UnityAction onClick, out Image image)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        image = go.AddComponent<Image>();
        image.color = buttonNormalColor;
        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(onClick);
        return go;
    }

    private static Text CreateText(Transform parent, string name, Font font, int size, Color color, TextAnchor alignment)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        text.raycastTarget = false;
        return text;
    }

    private Image CreateIcon(Transform parent, BuildIconShape shape, Color accent)
    {
        var go = new GameObject("Icon");
        go.transform.SetParent(parent, false);
        var image = go.AddComponent<Image>();
        image.sprite = CreateShapeSprite(shape);
        image.color = accent;
        image.raycastTarget = false;
        return image;
    }

    // Fills the parent, inset by the given margins.
    private static void Stretch(RectTransform rect, float left, float bottom, float right, float top)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    // x right and y down from the parent's top-left corner.
    private static void PlaceTopLeft(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
    }

    // A white filled shape on transparent, tinted by its Image; edges are smoothed by supersampling.
    private Sprite CreateShapeSprite(BuildIconShape shape)
    {
        const int res = 32;
        const int samples = 4;
        var tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
        var pixels = new Color[res * res];
        for (int py = 0; py < res; py++)
        {
            for (int px = 0; px < res; px++)
            {
                int inside = 0;
                for (int sy = 0; sy < samples; sy++)
                {
                    for (int sx = 0; sx < samples; sx++)
                    {
                        float u = ((px + (sx + 0.5f) / samples) / res) * 2f - 1f; // -1..1, right
                        float v = ((py + (sy + 0.5f) / samples) / res) * 2f - 1f; // -1..1, up
                        if (IsInsideShape(shape, u, v)) inside++;
                    }
                }
                pixels[py * res + px] = new Color(1f, 1f, 1f, inside / (float)(samples * samples));
            }
        }
        tex.SetPixels(pixels);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.Apply();

        var sprite = Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), res);
        sprite.name = "BuildIcon_" + shape;
        generatedAssets.Add(tex);
        generatedAssets.Add(sprite);
        return sprite;
    }

    /// <summary>Whether the point (u right, v up, each -1..1) lies inside the icon shape.</summary>
    public static bool IsInsideShape(BuildIconShape shape, float u, float v)
    {
        float au = Mathf.Abs(u);
        float av = Mathf.Abs(v);
        switch (shape)
        {
            case BuildIconShape.Triangle: return av <= 0.8f && au <= 0.9f * (v + 0.8f) / 1.6f; // Point down: digging
            case BuildIconShape.Bar: return au <= 0.9f && av <= 0.5f;
            case BuildIconShape.Circle: return u * u + v * v <= 0.81f;
            case BuildIconShape.Diamond: return au + av <= 0.95f;
            case BuildIconShape.Stack: return au <= 0.85f && (Mathf.Abs(v - 0.55f) <= 0.17f || av <= 0.17f || Mathf.Abs(v + 0.55f) <= 0.17f);
            default: return (au <= 0.3f && av <= 0.9f) || (av <= 0.3f && au <= 0.9f);
        }
    }
}
