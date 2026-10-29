using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The imp order panel (#163): click an imp (a Neutral board click on the tile it stands on) to select it; the panel shows
/// its name and what it's doing, and offers "Send on expedition". The button is enabled only when ImpExpedition can send
/// this imp now (door dug out, a walkable path to it, no expedition already out); otherwise it's disabled and the reason
/// is shown under it, so a click is never a silent no-op. While the expedition is out the panel counts down to the
/// return. A view of live state: it repaints on open, after a click and every 0.25 s (unscaled) while open. Built in code
/// like the Heart panel (own overlay Canvas); the panel is a raycast target, so a press on it never reaches the board.
/// </summary>
[DisallowMultipleComponent]
public class ImpPanel : MonoBehaviour
{
    public const string SendLabel = "Send on expedition";
    public const string CloseLabel = "Close";
    public const string WorkingText = "Working for the dungeon.";
    public const string DepartingText = "Heading out through the door.";

    [Header("References")]
    [SerializeField] private ImpExpedition impExpedition;
    [SerializeField] private PlacementController placementController; // Clicks on an imp's tile open the panel

    [Header("Layout")]
    [SerializeField] private float panelWidth = 260f;
    [SerializeField] private float rightOffset = 316f;       // Left of the Heart panel (8 + its 300 + 8), so both can be open
    [SerializeField] private float topOffset = 160f;         // The Heart panel's row: below a full toast stack
    [SerializeField] private float padding = 8f;
    [SerializeField] private float sectionGap = 6f;
    [SerializeField] private float headerHeight = 22f;
    [SerializeField] private float lineHeight = 16f;
    [SerializeField] private float buttonHeight = 24f;
    [SerializeField] private float closeButtonWidth = 56f;
    [SerializeField] private int titleFontSize = 15;
    [SerializeField] private int textFontSize = 12;
    [SerializeField] private int buttonFontSize = 12;
    [SerializeField] private float refreshSeconds = 0.25f;  // Unscaled: the countdown runs on game time, the chrome doesn't

    [Header("Colors")]
    [SerializeField] private Color panelColor = new Color(0f, 0f, 0f, 0.6f);                 // Matches the Heart panel
    [SerializeField] private Color sendColor = new Color(0.55f, 0.42f, 0.12f, 1f);           // Amber, the action color
    [SerializeField] private Color buttonColor = new Color(0.25f, 0.25f, 0.30f, 1f);
    [SerializeField] private Color secondaryTextColor = new Color(0.82f, 0.82f, 0.82f, 1f);

    public ImpExpedition ImpExpedition { get => impExpedition; set => impExpedition = value; }
    public PlacementController PlacementController
    {
        get => placementController;
        set
        {
            Unsubscribe();
            placementController = value;
            Subscribe(); // The click hook follows the reference
        }
    }

    public bool IsOpen { get; private set; }

    /// <summary>The selected imp (stays selected while it's away), or null.</summary>
    public Imp SelectedImp { get; private set; }

    /// <summary>Whether "Send on expedition" is enabled now.</summary>
    public bool CanSend => impExpedition != null && impExpedition.CanSend(SelectedImp);

    /// <summary>Why the order is disabled (shown under the button), or "" when it's available.</summary>
    public string ReasonText =>
        impExpedition == null ? "" : impExpedition.UnavailableReason(SelectedImp) ?? "";

    /// <summary>What the selected imp is doing, from live state.</summary>
    public string StatusText
    {
        get
        {
            if (SelectedImp == null) return "";
            bool onThisExpedition = impExpedition != null && impExpedition.ExpeditionImp == SelectedImp;
            if (!onThisExpedition) return WorkingText;
            return impExpedition.Phase == ImpExpedition.ExpeditionPhase.Departing
                ? DepartingText
                : "On an expedition: back in " + HeartPanel.FormatTime(impExpedition.SecondsRemaining) + ".";
        }
    }

    private Canvas canvas;
    private RectTransform panelRect;
    private Font font;
    private Text titleText;
    private Text statusText;
    private Text reasonText;
    private Button sendButton;
    private Button closeButton;
    private float refreshTimer;
    // #137: whether the click hook is on the current controller in this domain (a reload drops C# event subscriptions).
    [System.NonSerialized] private PlacementController subscribedTo;

    private void OnEnable()
    {
        Subscribe();
        if (canvas != null) RewireButtons(); // #137: listeners added in code don't survive a domain reload
    }

    private void OnDisable() => Unsubscribe();

    private void Start()
    {
        impExpedition ??= FindAnyObjectByType<ImpExpedition>();
        placementController ??= FindAnyObjectByType<PlacementController>();
        Subscribe();
        PlacementController.EnsureEventSystem();
        Build();
    }

    private void Update()
    {
        if (!IsOpen) return;
        refreshTimer += Time.unscaledDeltaTime;
        if (refreshTimer < refreshSeconds) return;
        refreshTimer = 0f;
        Repaint();
    }

    private void Subscribe()
    {
        if (placementController == null || subscribedTo == placementController) return;
        placementController.NeutralTileClicked += OnNeutralTileClicked;
        subscribedTo = placementController;
    }

    private void Unsubscribe()
    {
        if (subscribedTo != null) subscribedTo.NeutralTileClicked -= OnNeutralTileClicked;
        subscribedTo = null;
    }

    private void OnNeutralTileClicked(Vector2Int tile)
    {
        Imp imp = ImpAt(tile);
        if (imp != null) Open(imp);
    }

    /// <summary>The imp standing on this tile (active imps only: one away isn't on the board), or null.</summary>
    public static Imp ImpAt(Vector2Int tile)
    {
        foreach (Imp imp in FindObjectsByType<Imp>())
            if (imp.CurrentTile == tile) return imp;
        return null;
    }

    /// <summary>Selects the imp and shows the panel for it.</summary>
    public void Open(Imp imp)
    {
        if (imp == null) return;
        SelectedImp = imp;
        IsOpen = true;
        refreshTimer = 0f;
        Repaint();
    }

    /// <summary>Hides the panel and clears the selection.</summary>
    public void Close()
    {
        IsOpen = false;
        SelectedImp = null;
        Repaint();
    }

    /// <summary>"Send on expedition": sends the selected imp, then repaints. False when the order isn't available.</summary>
    public bool ClickSend()
    {
        bool sent = impExpedition != null && impExpedition.Send(SelectedImp);
        Repaint();
        return sent;
    }

    // ---- chrome ----

    /// <summary>Builds the (hidden) panel. Start calls this; public so tests can paint without Play.</summary>
    public void Build()
    {
        if (canvas != null) return;

        var canvasGo = new GameObject("ImpPanelCanvas");
        canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGo.AddComponent<CanvasScaler>();
        canvasGo.AddComponent<GraphicRaycaster>();

        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var panelGo = new GameObject("Panel");
        panelGo.transform.SetParent(canvasGo.transform, false);
        panelGo.AddComponent<Image>().color = panelColor; // A raycast target: a press on the panel never reaches the board
        panelRect = panelGo.GetComponent<RectTransform>();
        panelRect.anchorMin = panelRect.anchorMax = Vector2.one;
        panelRect.pivot = Vector2.one;
        panelRect.anchoredPosition = new Vector2(-rightOffset, -topOffset);

        titleText = CreateText(panelGo.transform, "Title", titleFontSize, Color.white);
        closeButton = CreateButton(panelGo.transform, "Close", CloseLabel, buttonColor);
        statusText = CreateText(panelGo.transform, "Status", textFontSize, Color.white);
        sendButton = CreateButton(panelGo.transform, "Send", SendLabel, sendColor);
        reasonText = CreateText(panelGo.transform, "Reason", textFontSize, secondaryTextColor);

        RewireButtons();
        Repaint();
    }

    private void RewireButtons()
    {
        closeButton.onClick.RemoveAllListeners();
        closeButton.onClick.AddListener(Close);
        sendButton.onClick.RemoveAllListeners();
        sendButton.onClick.AddListener(() => ClickSend());
    }

    /// <summary>Re-reads the live state into the panel. Called on open, after each click and on the refresh while open.</summary>
    public void Repaint()
    {
        if (canvas == null) return;
        if (IsOpen && SelectedImp == null) IsOpen = false; // Unity null: the imp is gone
        panelRect.gameObject.SetActive(IsOpen);
        if (!IsOpen) return;

        float inner = panelWidth - 2f * padding;
        float y = padding;
        titleText.text = SelectedImp.name;
        PlaceTopLeft(titleText.rectTransform, padding, y, inner - closeButtonWidth - 4f, headerHeight);
        PlaceTopLeft((RectTransform)closeButton.transform, padding + inner - closeButtonWidth, y, closeButtonWidth, headerHeight);
        y += headerHeight + sectionGap;

        statusText.text = StatusText;
        PlaceTopLeft(statusText.rectTransform, padding, y, inner, lineHeight);
        y += lineHeight + sectionGap;

        sendButton.interactable = CanSend; // Disabled, never a silent no-op
        PlaceTopLeft((RectTransform)sendButton.transform, padding, y, inner, buttonHeight);
        y += buttonHeight + sectionGap;

        string reason = ReasonText;
        reasonText.text = reason;
        reasonText.gameObject.SetActive(reason.Length > 0);
        if (reason.Length > 0)
        {
            PlaceTopLeft(reasonText.rectTransform, padding, y, inner, lineHeight);
            y += lineHeight + sectionGap;
        }

        panelRect.sizeDelta = new Vector2(panelWidth, y - sectionGap + padding);
    }

    /// <summary>The painted panel's root (tests): null before Build.</summary>
    public GameObject PanelObject => panelRect != null ? panelRect.gameObject : null;

    /// <summary>The painted send button (tests), or null before Build.</summary>
    public Button SendButton => sendButton;

    /// <summary>The painted reason line (tests), or null before Build.</summary>
    public Text ReasonLabel => reasonText;

    private Button CreateButton(Transform parent, string name, string label, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        var image = go.AddComponent<Image>();
        image.color = color;
        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        Text text = CreateText(go.transform, "Label", buttonFontSize, Color.white);
        text.alignment = TextAnchor.MiddleCenter;
        text.text = label;
        var rect = text.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        return button;
    }

    private Text CreateText(Transform parent, string name, int size, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.color = color;
        text.alignment = TextAnchor.MiddleLeft;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        return text;
    }

    // x right and y down from the parent's top-left corner.
    private static void PlaceTopLeft(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
    }
}
