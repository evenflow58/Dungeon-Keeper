using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The victory and defeat screens (DESIGN §A.10 step 6, §A.8). Polls GameManager.State (unscaled time, like the
/// other chrome): GameOver shows GameManager.GameOverLine, Victory shows GameManager.VictoryLine — both read from
/// GameManager, the single source. A full-screen modal: its backdrop takes raycasts, so the board and the other
/// chrome take no clicks while it's up. "Play again" reloads the active scene by build index; the fresh scene's
/// GameSpeed.Awake puts the speed back to 1×.
/// </summary>
[DisallowMultipleComponent]
public class EndScreen : MonoBehaviour
{
    public const string PlayAgainLabel = "Play again";

    [Header("References")]
    [SerializeField] private GameManager gameManager;

    [Header("Polling")]
    [SerializeField] private float pollIntervalSeconds = 0.25f;

    [Header("Appearance (Placeholder Art)")]
    [SerializeField] private Color backdropColor = new Color(0f, 0f, 0f, 0.75f);
    [SerializeField] private int headlineFontSize = 36;
    [SerializeField] private Vector2 buttonSize = new Vector2(140f, 36f);                    // As the build bar's
    [SerializeField] private Color buttonNormalColor = new Color(0.25f, 0.25f, 0.30f, 1f);   // As the build bar's
    [SerializeField] private int sortingOrder = 100;                                         // Above every other canvas

    public GameManager GameManager { get => gameManager; set => gameManager = value; }
    public float PollIntervalSeconds { get => pollIntervalSeconds; set => pollIntervalSeconds = value; }

    /// <summary>True once the game has ended (GameOver or Victory).</summary>
    public bool IsShowing { get; private set; }

    /// <summary>The line shown: GameManager's GameOverLine or VictoryLine; empty while playing.</summary>
    public string ShownText { get; private set; } = "";

    private GameObject overlay;
    private Text headline;
    private float pollTimer;

    private void Start()
    {
        gameManager ??= FindAnyObjectByType<GameManager>();

        PlacementController.EnsureEventSystem();
        CreateOverlay();
        Refresh();
    }

    private void Update()
    {
        pollTimer += Time.unscaledDeltaTime;
        if (pollTimer < pollIntervalSeconds) return;
        pollTimer = 0f;
        Refresh();
    }

    /// <summary>
    /// Recomputes IsShowing / ShownText from GameManager.State and shows or hides the overlay. Called on the poll;
    /// public so tests can call it without the UI.
    /// </summary>
    public void Refresh()
    {
        ShownText = LineFor(gameManager);
        IsShowing = ShownText.Length > 0;

        if (overlay == null) return; // No UI (EditMode tests)
        if (headline.text != ShownText) headline.text = ShownText;
        if (overlay.activeSelf != IsShowing) overlay.SetActive(IsShowing);
    }

    /// <summary>The end line for the game's state: GameOverLine, VictoryLine, or empty while playing or unwired.</summary>
    public static string LineFor(GameManager game)
    {
        if (game == null) return "";
        switch (game.State)
        {
            case GameManager.GameState.GameOver: return GameManager.GameOverLine;
            case GameManager.GameState.Victory: return GameManager.VictoryLine;
            default: return "";
        }
    }

    /// <summary>Starts a fresh run: reloads the active scene by build index (GameSpeed.Awake resets the speed).</summary>
    public void PlayAgain()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // ---- UI (code-built, built once, toggled with SetActive) ----

    private void CreateOverlay()
    {
        var canvasGo = new GameObject("EndScreenCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        canvasGo.AddComponent<CanvasScaler>();
        canvasGo.AddComponent<GraphicRaycaster>(); // The button is clickable, and the backdrop blocks what's behind

        overlay = new GameObject("EndScreen");
        overlay.transform.SetParent(canvasGo.transform, false);
        var backdrop = overlay.AddComponent<Image>();
        backdrop.color = backdropColor;
        backdrop.raycastTarget = true; // Modal: the board and other chrome take no clicks while it's up
        var rect = (RectTransform)overlay.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var headlineGo = new GameObject("Headline");
        headlineGo.transform.SetParent(overlay.transform, false);
        headline = headlineGo.AddComponent<Text>();
        headline.font = font;
        headline.fontSize = headlineFontSize;
        headline.color = Color.white;
        headline.alignment = TextAnchor.MiddleCenter;
        headline.horizontalOverflow = HorizontalWrapMode.Overflow;
        headline.raycastTarget = false;
        var headlineRect = (RectTransform)headlineGo.transform;
        headlineRect.anchorMin = headlineRect.anchorMax = new Vector2(0.5f, 0.5f);
        headlineRect.anchoredPosition = new Vector2(0f, headlineFontSize);
        headlineRect.sizeDelta = new Vector2(800f, headlineFontSize * 2f);

        var buttonGo = new GameObject("PlayAgainButton");
        buttonGo.transform.SetParent(overlay.transform, false);
        var image = buttonGo.AddComponent<Image>();
        image.color = buttonNormalColor;
        var button = buttonGo.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(PlayAgain);
        var buttonRect = (RectTransform)buttonGo.transform;
        buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
        buttonRect.anchoredPosition = new Vector2(0f, -buttonSize.y);
        buttonRect.sizeDelta = buttonSize;

        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(buttonGo.transform, false);
        var label = labelGo.AddComponent<Text>();
        label.font = font;
        label.fontSize = 14;
        label.color = Color.white;
        label.alignment = TextAnchor.MiddleCenter;
        label.text = PlayAgainLabel;
        label.raycastTarget = false;
        var labelRect = (RectTransform)labelGo.transform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;

        overlay.SetActive(false);
    }
}
