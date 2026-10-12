using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Pause / 1× / 2× (DESIGN §A.7). The mechanism is Time.timeScale: every simulation component steps from
/// Time.deltaTime, so the whole sim (digging, needs, growth, combat, hero countdowns) scales uniformly, while
/// view and UI chrome (camera, build-rail feedback, top-bar poll) run on unscaled time and stay responsive.
/// Speed is per run: a freshly loaded scene always starts at 1× (Awake), since a scene reload doesn't reset
/// Time.timeScale by itself. The buttons sit in the top bar's strip at its right end (#124), on this component's own
/// canvas one sorting order above the bar's; uGUI input ignores timeScale, so they work while paused. Only the three
/// buttons take pointer input: a press anywhere else on the strip reaches the board. The pause hotkey (TogglePause,
/// Space by default) comes from InputConfig and returns to the speed held before pausing.
/// </summary>
[DisallowMultipleComponent]
public class GameSpeed : MonoBehaviour
{
    public const float Paused = 0f;
    public const float Normal = 1f;
    public const float Fast = 2f;

    [Header("Buttons (in the top bar's strip)")]
    [SerializeField] private Vector2 buttonSize = new Vector2(64f, 28f);
    [SerializeField] private float barPadding = 6f;   // Between buttons and at the cluster's ends
    [SerializeField] private float barInset = 16f;    // From the screen's right edge: TopBar.sidePadding
    [SerializeField] private float stripHeight = 36f; // The strip the buttons center in: TopBar.barHeight
    [SerializeField] private int sortingOrder = 1;    // One above TopBar's canvas (0), so the buttons draw and raycast over the bar
    [SerializeField] private Color panelColor = new Color(0f, 0f, 0f, 0f); // The bar is the backdrop
    [SerializeField] private Color buttonNormalColor = new Color(0.25f, 0.25f, 0.30f, 1f);
    [SerializeField] private Color buttonSelectedColor = new Color(0.85f, 0.65f, 0.15f, 1f); // Amber marks the active speed

    private static readonly float[] Speeds = { Paused, Normal, Fast };
    private static readonly string[] Labels = { "Pause", "1×", "2×" };

    /// <summary>The current speed: 0 (paused), 1 or 2. Also Time.timeScale.</summary>
    public float Speed { get; private set; } = Normal;

    public bool IsPaused => Speed == Paused;

    /// <summary>The speed TogglePause returns to: the one held when the game last entered Paused.</summary>
    public float SpeedBeforePause { get; private set; } = Normal;

    /// <summary>The button cluster's width. TopBar.speedClusterWidth reserves this much (plus a gap) at the bar's right.</summary>
    public float PanelWidth => Speeds.Length * buttonSize.x + (Speeds.Length + 1) * barPadding;
    public float BarInset => barInset;
    public float StripHeight => stripHeight;
    public int SortingOrder => sortingOrder;

    private InputConfig.Actions input; // Bindings come from InputConfig (rebindable); enabled with this component
    private Canvas canvas;
    private Image[] buttonImages;

    private void Awake()
    {
        input = InputConfig.CreateActions();
        ResetToNormalSpeed();
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
    }

    private void Start()
    {
        PlacementController.EnsureEventSystem();
        CreateButtons();
    }

    private void Update()
    {
        if (input?.TogglePause.WasPressedThisFrame() ?? false) TogglePause();
    }

    /// <summary>A fresh run starts at 1×. Awake calls this; public so tests can drive the same path.</summary>
    public void ResetToNormalSpeed()
    {
        SetSpeed(Normal);
        SpeedBeforePause = Normal;
    }

    /// <summary>Sets the game speed. Only 0, 1 and 2 are supported; anything else is ignored.</summary>
    public void SetSpeed(float speed)
    {
        if (System.Array.IndexOf(Speeds, speed) < 0) return;

        if (speed == Paused && Speed != Paused) SpeedBeforePause = Speed; // Remembered however the pause came: button or hotkey
        Speed = speed;
        Time.timeScale = speed;
        RefreshButtons();
    }

    /// <summary>The pause hotkey: pauses, or from Paused returns to the speed held before pausing.</summary>
    public void TogglePause()
    {
        SetSpeed(IsPaused ? SpeedBeforePause : Paused);
    }

    // ---- UI ----

    /// <summary>Builds the three buttons in the top bar's strip. Start calls this; public so tests can build without Play.</summary>
    public void CreateButtons()
    {
        if (canvas != null) return;

        var canvasGo = new GameObject("GameSpeedCanvas");
        canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        canvasGo.AddComponent<CanvasScaler>();
        canvasGo.AddComponent<GraphicRaycaster>(); // Clickable, unlike the top bar

        var panelGo = new GameObject("SpeedControls");
        panelGo.transform.SetParent(canvasGo.transform, false);
        var panel = panelGo.AddComponent<Image>();
        panel.color = panelColor;
        panel.raycastTarget = false; // Only the buttons block the board: the player digs under the strip
        var panelRect = panelGo.GetComponent<RectTransform>();
        // The right end of the top bar's strip: as tall as the bar, inset from the screen edge like the bar's readouts.
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(1f, 1f);
        panelRect.pivot = new Vector2(1f, 1f);
        panelRect.anchoredPosition = new Vector2(-barInset, 0f);
        panelRect.sizeDelta = new Vector2(PanelWidth, stripHeight);

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        buttonImages = new Image[Speeds.Length];

        for (int i = 0; i < Speeds.Length; i++)
        {
            float speed = Speeds[i];

            var buttonGo = new GameObject(Labels[i] + "Button");
            buttonGo.transform.SetParent(panelGo.transform, false);
            var image = buttonGo.AddComponent<Image>();
            var button = buttonGo.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => SetSpeed(speed));

            var rect = buttonGo.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f); // Vertically centered in the strip
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
            text.text = Labels[i];
            text.raycastTarget = false;
            var textRect = textGo.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = textRect.offsetMax = Vector2.zero;

            buttonImages[i] = image;
        }

        RefreshButtons();
    }

    // Not built yet (Awake, EditMode tests), or an array a recompile emptied (#83: under no-domain-reload a null array
    // comes back as an empty one): either way there is nothing to index.
    private bool HasButtons => buttonImages != null && buttonImages.Length == Speeds.Length;

    private void RefreshButtons()
    {
        if (!HasButtons) return;
        for (int i = 0; i < Speeds.Length; i++)
        {
            if (buttonImages[i] != null) buttonImages[i].color = Speeds[i] == Speed ? buttonSelectedColor : buttonNormalColor;
        }
    }

    /// <summary>The speed whose button is tinted as active, or -1 before the buttons exist (for verification).</summary>
    public float HighlightedSpeed
    {
        get
        {
            if (!HasButtons) return -1f;
            for (int i = 0; i < Speeds.Length; i++)
                if (buttonImages[i] != null && buttonImages[i].color == buttonSelectedColor) return Speeds[i];
            return -1f;
        }
    }
}
