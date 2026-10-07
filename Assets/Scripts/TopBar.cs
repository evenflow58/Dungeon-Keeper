using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The top bar (DESIGN §A.7): living goblins, food stock, and the next-hero state, polled every
/// pollIntervalSeconds. Built in code like the build bar (own overlay Canvas, no scene-authored UI) and
/// display-only: no raycaster and raycastTarget off on everything, so it never blocks board input — the
/// player digs right under it (the door column).
/// Updates after the simulation each frame (execution order), so a poll never catches a half-updated frame,
/// e.g. the route opening before the spawner has spawned the hero it unlocks.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(1000)]
public class TopBar : MonoBehaviour
{
    public const string Dash = "—";

    [Header("References")]
    [SerializeField] private PlacementManager placementManager;
    [SerializeField] private HeroSpawner heroSpawner;
    [SerializeField] private GameManager gameManager; // Optional: once the game has ended, the hero readout shows a dash

    [Header("Appearance")]
    [SerializeField] private int fontSize = 20;                                    // Persistent HUD: larger than the build bar's 14
    [SerializeField] private float barHeight = 36f;
    [SerializeField] private float sidePadding = 16f;
    [SerializeField] private Color panelColor = new Color(0f, 0f, 0f, 0.6f);      // Matches the build bar

    [Header("Refresh")]
    [SerializeField] private float pollIntervalSeconds = 0.25f;

    public PlacementManager PlacementManager { get => placementManager; set => placementManager = value; }
    public HeroSpawner HeroSpawner { get => heroSpawner; set => heroSpawner = value; }
    public GameManager GameManager { get => gameManager; set => gameManager = value; }
    public int FontSize { get => fontSize; set => fontSize = value; }
    public float PollIntervalSeconds { get => pollIntervalSeconds; set => pollIntervalSeconds = value; }

    /// <summary>The three readouts as of the last Refresh (also what the labels show).</summary>
    public string GoblinText { get; private set; } = "";
    public string FoodText { get; private set; } = "";
    public string NextHeroText { get; private set; } = "";

    private Text goblinLabel;
    private Text foodLabel;
    private Text nextHeroLabel;
    private float pollTimer;

    private void Start()
    {
        placementManager ??= FindAnyObjectByType<PlacementManager>();
        heroSpawner ??= FindAnyObjectByType<HeroSpawner>();
        gameManager ??= FindAnyObjectByType<GameManager>();

        CreateBar();
        Refresh();
    }

    private void Update()
    {
        pollTimer += Time.deltaTime;
        if (pollTimer < pollIntervalSeconds) return;
        pollTimer = 0f;
        Refresh();
    }

    /// <summary>Recomputes the three readouts and shows them. Called on the poll; public so tests can call it.</summary>
    public void Refresh()
    {
        GoblinText = $"Goblins: {CountLivingGoblins()}";
        FoodText = $"Food: {(placementManager != null ? placementManager.TotalFood : 0)}";
        NextHeroText = NextHeroReadout(heroSpawner, gameManager);

        if (goblinLabel != null) goblinLabel.text = GoblinText;
        if (foodLabel != null) foodLabel.text = FoodText;
        if (nextHeroLabel != null) nextHeroLabel.text = NextHeroText;
    }

    private static int CountLivingGoblins()
    {
        int living = 0;
        foreach (Goblin g in FindObjectsByType<Goblin>()) // Active only: a dead goblin is deactivated anyway
        {
            if (!g.IsDead) living++;
        }
        return living;
    }

    /// <summary>
    /// The next-hero readout, by phase (first match wins):
    /// game ended or all heroes dealt with → "Next hero: —"; a hero in the dungeon → "Hero in the dungeon";
    /// between heroes → the live countdown; before hero 1 → "needs a route" if a trigger is met but the dungeon
    /// is sealed, else the countdown to the time trigger (the building trigger may bring him sooner).
    /// </summary>
    public static string NextHeroReadout(HeroSpawner spawner, GameManager game)
    {
        if (spawner == null) return $"Next hero: {Dash}";
        bool gameEnded = game != null && game.State != GameManager.GameState.Playing;
        bool allDealtWith = spawner.TotalHeroes > 0 && spawner.HeroesResolved >= spawner.TotalHeroes;
        if (gameEnded || allDealtWith) return $"Next hero: {Dash}";

        if (spawner.HeroesSpawned > spawner.HeroesResolved) return "Hero in the dungeon";
        if (spawner.NextHeroScheduled) return $"Next hero: {FormatCountdown(spawner.SecondsUntilNextHero)}";

        bool triggerMet = spawner.BuildingsTriggerMet() || spawner.ElapsedSeconds >= spawner.FirstHeroSeconds;
        if (triggerMet && !spawner.RouteExists()) return "Next hero: needs a route";
        return $"Next hero: {FormatCountdown(spawner.FirstHeroSeconds - spawner.ElapsedSeconds)}";
    }

    /// <summary>m:ss of the seconds rounded up (5:00, 1:30, 0:07); never negative.</summary>
    public static string FormatCountdown(float seconds)
    {
        int total = Mathf.CeilToInt(Mathf.Max(0f, seconds));
        return $"{total / 60}:{total % 60:00}";
    }

    // ---- UI (code-built, display-only) ----

    private void CreateBar()
    {
        var canvasGo = new GameObject("TopBarCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGo.AddComponent<CanvasScaler>();
        // No GraphicRaycaster: the bar is display-only and must never take input from the board under it.

        var barGo = new GameObject("TopBar");
        barGo.transform.SetParent(canvasGo.transform, false);
        var panel = barGo.AddComponent<Image>();
        panel.color = panelColor;
        panel.raycastTarget = false;
        var barRect = barGo.GetComponent<RectTransform>();
        barRect.anchorMin = new Vector2(0f, 1f); // Stretched across the top edge
        barRect.anchorMax = new Vector2(1f, 1f);
        barRect.pivot = new Vector2(0.5f, 1f);
        barRect.anchoredPosition = Vector2.zero;
        barRect.sizeDelta = new Vector2(0f, barHeight);

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        goblinLabel = CreateLabel(barGo.transform, "GoblinReadout", font, 0f, TextAnchor.MiddleLeft);
        foodLabel = CreateLabel(barGo.transform, "FoodReadout", font, 1f / 3f, TextAnchor.MiddleCenter);
        nextHeroLabel = CreateLabel(barGo.transform, "NextHeroReadout", font, 2f / 3f, TextAnchor.MiddleRight);
    }

    // A label filling one third of the bar (starting at xMin), padded at the outer edges.
    private Text CreateLabel(Transform parent, string name, Font font, float xMin, TextAnchor alignment)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<Text>();
        text.font = font;
        text.fontSize = fontSize;
        text.color = Color.white;
        text.alignment = alignment;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;

        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(xMin, 0f);
        rect.anchorMax = new Vector2(xMin + 1f / 3f, 1f);
        rect.offsetMin = new Vector2(sidePadding, 0f);
        rect.offsetMax = new Vector2(-sidePadding, 0f);
        return text;
    }
}
