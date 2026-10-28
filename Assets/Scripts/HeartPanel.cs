using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Heart panel (#161): the Dungeon Heart's current candidate imps, each with Pick, plus "Wait for next cycle"
/// (decline all). It opens from the "Imps await" toast or a Neutral click on the Heart's tile, and closes with its Close
/// button. It is a view of HeartPrinting: every repaint reads the live list and state, never a cached copy, so a cycle
/// boundary while it's open shows the new list on the next 0.25 s (unscaled) refresh. After a pick or a decline it
/// says so and counts down to the next list. Built in code like the rest of the chrome (own overlay Canvas, no
/// scene-authored UI); the panel is a raycast target, so a press on it never reaches the board.
/// </summary>
[DisallowMultipleComponent]
public class HeartPanel : MonoBehaviour
{
    public const string TitleText = "Dungeon Heart";
    public const string OpenStatusText = "Pick one imp to birth (free), or wait.";
    public const string DeclinedStatusText = "Waiting for the next cycle.";
    public const string PickLabel = "Pick";
    public const string DeclineLabel = "Wait for next cycle";
    public const string CloseLabel = "Close";

    [Header("References")]
    [SerializeField] private HeartPrinting heartPrinting;
    [SerializeField] private PlacementController placementController; // Heart-tile clicks open the panel
    [SerializeField] private Heart heart;

    [Header("Layout")]
    [SerializeField] private float panelWidth = 300f;
    [SerializeField] private float rightOffset = 8f;         // AlertToasts.edgePadding: lined up with the toasts
    [SerializeField] private float topOffset = 160f;         // Below a full toast stack (44 + 3 × 36) with a gap
    [SerializeField] private float padding = 8f;
    [SerializeField] private float sectionGap = 6f;
    [SerializeField] private float headerHeight = 22f;
    [SerializeField] private float lineHeight = 16f;
    [SerializeField] private float rowHeight = 34f;          // A candidate: name over stats, Pick on the right
    [SerializeField] private float rowGap = 4f;
    [SerializeField] private float buttonWidth = 70f;
    [SerializeField] private float buttonHeight = 24f;
    [SerializeField] private float closeButtonWidth = 56f;
    [SerializeField] private int titleFontSize = 15;
    [SerializeField] private int textFontSize = 12;
    [SerializeField] private int statsFontSize = 10;
    [SerializeField] private int buttonFontSize = 12;
    [SerializeField] private float refreshSeconds = 0.25f;  // Unscaled: the countdown runs on game time, the chrome doesn't

    [Header("Colors")]
    [SerializeField] private Color panelColor = new Color(0f, 0f, 0f, 0.6f);                 // Matches the detail card
    [SerializeField] private Color rowColor = new Color(1f, 1f, 1f, 0.06f);
    [SerializeField] private Color pickColor = new Color(0.55f, 0.42f, 0.12f, 1f);           // Amber, the card's action color
    [SerializeField] private Color buttonColor = new Color(0.25f, 0.25f, 0.30f, 1f);
    [SerializeField] private Color secondaryTextColor = new Color(0.82f, 0.82f, 0.82f, 1f);

    public HeartPrinting HeartPrinting { get => heartPrinting; set => heartPrinting = value; }
    public PlacementController PlacementController { get => placementController; set => placementController = value; }
    public Heart Heart { get => heart; set => heart = value; }

    public bool IsOpen { get; private set; }

    /// <summary>The status line, from live state: open, picked (who joined) or declined.</summary>
    public string StatusText => heartPrinting == null ? ""
        : heartPrinting.State == HeartPrinting.CycleState.Picked ? heartPrinting.PickedName + " has joined your imps."
        : heartPrinting.State == HeartPrinting.CycleState.Declined ? DeclinedStatusText
        : OpenStatusText;

    /// <summary>The countdown line: game time to the next list.</summary>
    public string NextListText => heartPrinting == null ? "" : "Next candidates in " + FormatTime(heartPrinting.SecondsToNextCycle);

    /// <summary>The rows the panel shows now: the live list (empty after a pick or decline).</summary>
    public IReadOnlyList<ImpCandidate> ShownCandidates =>
        heartPrinting != null ? heartPrinting.Candidates : System.Array.Empty<ImpCandidate>();

    /// <summary>Whether "Wait for next cycle" is offered: only while the list can still be picked from.</summary>
    public bool CanDecline => heartPrinting != null && heartPrinting.State == HeartPrinting.CycleState.Open;

    /// <summary>The line under a candidate's name: its stats block.</summary>
    public static string StatsLine(ImpStats stats) =>
        string.Format(CultureInfo.InvariantCulture, "Dig {0:0.0}×  ·  Build {1:0.0}×  ·  Move {2:0.0}×",
            stats.DigSpeed, stats.BuildSpeed, stats.MoveSpeed);

    /// <summary>m:ss, rounded up so the line never reads 0:00 before the boundary.</summary>
    public static string FormatTime(float seconds)
    {
        int total = Mathf.Max(0, Mathf.CeilToInt(seconds));
        return (total / 60).ToString(CultureInfo.InvariantCulture) + ":" + (total % 60).ToString("00", CultureInfo.InvariantCulture);
    }

    private class RowView
    {
        public GameObject root;
        public Text name;
        public Text stats;
        public Button button;
        public ImpCandidate candidate;
    }

    private Canvas canvas;
    private RectTransform panelRect;
    private Font font;
    private Text titleText;
    private Text statusText;
    private Text nextText;
    private Button declineButton;
    private Button closeButton;
    private RectTransform rowList;
    private readonly List<RowView> rows = new List<RowView>();
    private float refreshTimer;
    // #137: whether the click hook is on the current controller in this domain (a reload drops C# event subscriptions).
    [System.NonSerialized] private PlacementController subscribedTo;

    private void OnEnable()
    {
        Subscribe();
        if (canvas != null) RewireButtons(); // #137: listeners added in code don't survive a domain reload
    }

    private void OnDisable()
    {
        if (subscribedTo != null) subscribedTo.NeutralTileClicked -= OnNeutralTileClicked;
        subscribedTo = null;
    }

    private void Start()
    {
        heartPrinting ??= FindAnyObjectByType<HeartPrinting>();
        placementController ??= FindAnyObjectByType<PlacementController>();
        heart ??= FindAnyObjectByType<Heart>();
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

    private void OnNeutralTileClicked(Vector2Int tile)
    {
        if (heart != null && tile == heart.Tile) Open();
    }

    /// <summary>Shows the panel with the current list.</summary>
    public void Open()
    {
        IsOpen = true;
        refreshTimer = 0f;
        Repaint();
    }

    /// <summary>Hides the panel. Closing declines nothing: the list stays pickable until the boundary.</summary>
    public void Close()
    {
        IsOpen = false;
        Repaint();
    }

    /// <summary>Pick on a row: births that candidate, then repaints. Returns the new imp, or null.</summary>
    public Imp ClickPick(int index)
    {
        Imp imp = heartPrinting != null ? heartPrinting.Pick(index) : null;
        Repaint();
        return imp;
    }

    /// <summary>"Wait for next cycle": declines the list, then repaints.</summary>
    public bool ClickDecline()
    {
        bool declined = heartPrinting != null && heartPrinting.DeclineAll();
        Repaint();
        return declined;
    }

    // ---- chrome ----

    /// <summary>Builds the (hidden) panel. Start calls this; public so tests can paint without Play.</summary>
    public void Build()
    {
        if (canvas != null) return;

        var canvasGo = new GameObject("HeartPanelCanvas");
        canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGo.AddComponent<CanvasScaler>();
        canvasGo.AddComponent<GraphicRaycaster>(); // Pick, decline and close are buttons

        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var panelGo = new GameObject("Panel");
        panelGo.transform.SetParent(canvasGo.transform, false);
        panelGo.AddComponent<Image>().color = panelColor; // A raycast target: a press on the panel never reaches the board
        panelRect = panelGo.GetComponent<RectTransform>();
        panelRect.anchorMin = panelRect.anchorMax = Vector2.one;
        panelRect.pivot = Vector2.one;
        panelRect.anchoredPosition = new Vector2(-rightOffset, -topOffset);

        titleText = CreateText(panelGo.transform, "Title", titleFontSize, Color.white, TextAnchor.MiddleLeft);
        titleText.text = TitleText;
        closeButton = CreateButton(panelGo.transform, "Close", CloseLabel, buttonColor);
        statusText = CreateText(panelGo.transform, "Status", textFontSize, Color.white, TextAnchor.MiddleLeft);
        var listGo = new GameObject("Candidates", typeof(RectTransform));
        listGo.transform.SetParent(panelGo.transform, false);
        rowList = (RectTransform)listGo.transform;
        declineButton = CreateButton(panelGo.transform, "Decline", DeclineLabel, buttonColor);
        nextText = CreateText(panelGo.transform, "Next", textFontSize, secondaryTextColor, TextAnchor.MiddleLeft);

        RewireButtons();
        Repaint();
    }

    private void RewireButtons()
    {
        closeButton.onClick.RemoveAllListeners();
        closeButton.onClick.AddListener(Close);
        declineButton.onClick.RemoveAllListeners();
        declineButton.onClick.AddListener(() => ClickDecline());
        foreach (RowView view in rows) WireRow(view);
    }

    /// <summary>Re-reads the live state into the panel. Called on open, after each click and on the refresh while open.</summary>
    public void Repaint()
    {
        if (canvas == null) return;
        panelRect.gameObject.SetActive(IsOpen);
        if (!IsOpen) return;

        float inner = panelWidth - 2f * padding;
        float y = padding;
        PlaceTopLeft(titleText.rectTransform, padding, y, inner - closeButtonWidth - 4f, headerHeight);
        PlaceTopLeft((RectTransform)closeButton.transform, padding + inner - closeButtonWidth, y, closeButtonWidth, headerHeight);
        y += headerHeight + sectionGap;

        statusText.text = StatusText;
        PlaceTopLeft(statusText.rectTransform, padding, y, inner, lineHeight);
        y += lineHeight + sectionGap;

        IReadOnlyList<ImpCandidate> shown = ShownCandidates;
        while (rows.Count < shown.Count) rows.Add(CreateRow(rows.Count));
        float listY = 0f;
        for (int i = 0; i < rows.Count; i++)
        {
            RowView view = rows[i];
            bool used = i < shown.Count;
            view.root.SetActive(used);
            if (!used) continue;
            view.candidate = shown[i];
            view.name.text = shown[i].Name;
            view.stats.text = StatsLine(shown[i].Stats);
            PlaceTopLeft((RectTransform)view.root.transform, 0f, listY, inner, rowHeight);
            listY += rowHeight + rowGap;
        }
        if (shown.Count > 0) listY -= rowGap;
        PlaceTopLeft(rowList, padding, y, inner, listY);
        if (shown.Count > 0) y += listY + sectionGap;

        declineButton.gameObject.SetActive(CanDecline);
        if (CanDecline)
        {
            PlaceTopLeft((RectTransform)declineButton.transform, padding, y, inner, buttonHeight);
            y += buttonHeight + sectionGap;
        }

        nextText.text = NextListText;
        PlaceTopLeft(nextText.rectTransform, padding, y, inner, lineHeight);
        y += lineHeight + padding;

        panelRect.sizeDelta = new Vector2(panelWidth, y);
    }

    /// <summary>The painted panel's root (tests): null before Build.</summary>
    public GameObject PanelObject => panelRect != null ? panelRect.gameObject : null;

    /// <summary>The painted Pick button of row index (tests), or null.</summary>
    public Button PickButton(int index) => index >= 0 && index < rows.Count ? rows[index].button : null;

    /// <summary>The painted decline button (tests), or null before Build.</summary>
    public Button DeclineButton => declineButton;

    /// <summary>The painted close button (tests), or null before Build.</summary>
    public Button CloseButton => closeButton;

    private RowView CreateRow(int index)
    {
        var view = new RowView();
        view.root = new GameObject("Candidate" + index);
        view.root.transform.SetParent(rowList, false);
        view.root.AddComponent<RectTransform>();
        view.root.AddComponent<Image>().color = rowColor;

        view.name = CreateText(view.root.transform, "Name", textFontSize, Color.white, TextAnchor.UpperLeft);
        Stretch(view.name.rectTransform, 6f, rowHeight * 0.5f, buttonWidth + 8f, 2f);
        view.stats = CreateText(view.root.transform, "Stats", statsFontSize, secondaryTextColor, TextAnchor.LowerLeft);
        Stretch(view.stats.rectTransform, 6f, 2f, buttonWidth + 8f, rowHeight * 0.5f);

        view.button = CreateButton(view.root.transform, "Pick", PickLabel, pickColor);
        var rect = (RectTransform)view.button.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.anchoredPosition = new Vector2(-4f, 0f);
        rect.sizeDelta = new Vector2(buttonWidth, buttonHeight);
        WireRow(view);
        return view;
    }

    private void WireRow(RowView view)
    {
        view.button.onClick.RemoveAllListeners();
        view.button.onClick.AddListener(() =>
        {
            int index = view.candidate != null && heartPrinting != null ? IndexOf(heartPrinting.Candidates, view.candidate) : -1;
            ClickPick(index);
        });
    }

    private static int IndexOf(IReadOnlyList<ImpCandidate> list, ImpCandidate candidate)
    {
        for (int i = 0; i < list.Count; i++)
            if (list[i] == candidate) return i;
        return -1;
    }

    private Button CreateButton(Transform parent, string name, string label, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        var image = go.AddComponent<Image>();
        image.color = color;
        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        Text text = CreateText(go.transform, "Label", buttonFontSize, Color.white, TextAnchor.MiddleCenter);
        text.text = label;
        Stretch(text.rectTransform, 0f, 0f, 0f, 0f);
        return button;
    }

    private Text CreateText(Transform parent, string name, int size, Color color, TextAnchor alignment)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        return text;
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
}
