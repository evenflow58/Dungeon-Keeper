using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Pause / 1× / 2× (DESIGN §A.7). The mechanism is Time.timeScale: every simulation component steps from
/// Time.deltaTime, so the whole sim (digging, needs, growth, combat, hero countdowns) scales uniformly, while
/// view and UI chrome (camera, build-bar feedback, top-bar poll) run on unscaled time and stay responsive.
/// Speed is per run: a freshly loaded scene always starts at 1× (Awake), since a scene reload doesn't reset
/// Time.timeScale by itself. Buttons are code-built like the build bar; uGUI input ignores timeScale, so they
/// work while paused. Hotkeys are #30's story.
/// </summary>
[DisallowMultipleComponent]
public class GameSpeed : MonoBehaviour
{
    public const float Paused = 0f;
    public const float Normal = 1f;
    public const float Fast = 2f;

    [Header("Buttons (Placeholder Art)")]
    [SerializeField] private Vector2 buttonSize = new Vector2(140f, 36f);                    // As the build bar's
    [SerializeField] private float barPadding = 8f;
    [SerializeField] private float rowAboveBuildBar = 60f; // Bottom-right, raised above the build bar's row so they never overlap
    [SerializeField] private Color buttonNormalColor = new Color(0.25f, 0.25f, 0.30f, 1f);
    [SerializeField] private Color buttonSelectedColor = new Color(0.85f, 0.65f, 0.15f, 1f); // Amber marks the active speed

    private static readonly float[] Speeds = { Paused, Normal, Fast };
    private static readonly string[] Labels = { "Pause", "1×", "2×" };

    /// <summary>The current speed: 0 (paused), 1 or 2. Also Time.timeScale.</summary>
    public float Speed { get; private set; } = Normal;

    public bool IsPaused => Speed == Paused;

    private Image[] buttonImages;

    private void Awake()
    {
        ResetToNormalSpeed();
    }

    private void Start()
    {
        PlacementController.EnsureEventSystem();
        CreateButtons();
        RefreshButtons();
    }

    /// <summary>A fresh run starts at 1×. Awake calls this; public so tests can drive the same path.</summary>
    public void ResetToNormalSpeed()
    {
        SetSpeed(Normal);
    }

    /// <summary>Sets the game speed. Only 0, 1 and 2 are supported; anything else is ignored.</summary>
    public void SetSpeed(float speed)
    {
        if (System.Array.IndexOf(Speeds, speed) < 0) return;

        Speed = speed;
        Time.timeScale = speed;
        RefreshButtons();
    }

    // ---- UI ----

    private void CreateButtons()
    {
        var canvasGo = new GameObject("GameSpeedCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGo.AddComponent<CanvasScaler>();
        canvasGo.AddComponent<GraphicRaycaster>(); // Clickable, unlike the top bar

        var panelGo = new GameObject("SpeedControls");
        panelGo.transform.SetParent(canvasGo.transform, false);
        var panel = panelGo.AddComponent<Image>();
        panel.color = new Color(0f, 0f, 0f, 0.6f);
        var panelRect = panelGo.GetComponent<RectTransform>();
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(1f, 0f);
        panelRect.pivot = new Vector2(1f, 0f);
        panelRect.anchoredPosition = new Vector2(-barPadding, rowAboveBuildBar);
        panelRect.sizeDelta = new Vector2(
            Speeds.Length * buttonSize.x + (Speeds.Length + 1) * barPadding,
            buttonSize.y + 2f * barPadding);

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
            text.text = Labels[i];
            text.raycastTarget = false;
            var textRect = textGo.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = textRect.offsetMax = Vector2.zero;

            buttonImages[i] = image;
        }
    }

    private void RefreshButtons()
    {
        if (buttonImages == null) return; // Not built yet (Awake, or EditMode tests)
        for (int i = 0; i < Speeds.Length; i++)
            buttonImages[i].color = Speeds[i] == Speed ? buttonSelectedColor : buttonNormalColor;
    }

    /// <summary>The speed whose button is tinted as active, or -1 before the buttons exist (for verification).</summary>
    public float HighlightedSpeed
    {
        get
        {
            if (buttonImages == null) return -1f;
            for (int i = 0; i < Speeds.Length; i++)
                if (buttonImages[i].color == buttonSelectedColor) return Speeds[i];
            return -1f;
        }
    }
}
