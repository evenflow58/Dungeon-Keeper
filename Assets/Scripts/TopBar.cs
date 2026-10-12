using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The top bar (DESIGN §A.7): living goblins, food stock, a materials summary (#124: kinds and total, so the bar no
/// longer grows with the catalog), and the next-hero state, polled every pollIntervalSeconds. Built in code like the
/// rest of the chrome (own overlay Canvas, no scene-authored UI) and display-only: no raycaster and raycastTarget off
/// on everything, so it never blocks board input — the player digs right under it (the door column). The speed
/// buttons at its right end are GameSpeed's, on GameSpeed's canvas; the readouts leave that width free.
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
    [SerializeField] private Stockpile stockpile;     // The materials summary's source (#102)

    [Header("Appearance")]
    [SerializeField] private int fontSize = 20;                                    // Persistent HUD: larger than the build chrome's
    [SerializeField] private int minFontSize = 12;                                 // #111: the readouts shrink together to fit a narrow view, never below this
    [SerializeField] private float barHeight = 36f;   // GameSpeed.stripHeight and BuildRail.topOffset agree with this
    [SerializeField] private float sidePadding = 16f; // At the bar's outer edges; GameSpeed.barInset agrees with this
    [SerializeField] private float slotGap = 4f;      // Between readouts (#114)
    [SerializeField] private Color panelColor = new Color(0f, 0f, 0f, 0.6f);      // Matches the build rail

    [Header("Layout (#124)")]
    // Kept free at the bar's right end, inside sidePadding, for GameSpeed's buttons: its panel (GameSpeed.PanelWidth,
    // 216) plus a slotGap to the next-hero readout.
    [SerializeField] private float speedClusterWidth = 220f;
    // Each readout's share of the width left of that reserve. At a 732 px view that is 496 px, where these shares fit
    // the longest contents ("Goblins: 10", "Food: 15", "Materials: 3 kinds · 123 total", "Hero in the dungeon") at
    // size 14: 72 + 57 + 179 + 130 px of text plus 40 px of padding. "Next hero: needs a route" (158 px at 14) fits at
    // 13 there. From about 1000 px wide everything fits at the full size 20.
    [SerializeField] private float goblinShare = 0.1885f;
    [SerializeField] private float foodShare = 0.133f;
    [SerializeField] private float materialShare = 0.38f;
    [SerializeField] private float heroShare = 0.2985f;

    [Header("Refresh")]
    [SerializeField] private float pollIntervalSeconds = 0.25f;

    public PlacementManager PlacementManager { get => placementManager; set => placementManager = value; }
    public HeroSpawner HeroSpawner { get => heroSpawner; set => heroSpawner = value; }
    public GameManager GameManager { get => gameManager; set => gameManager = value; }
    public Stockpile Stockpile { get => stockpile; set => stockpile = value; }
    public int FontSize { get => fontSize; set => fontSize = value; }
    public int MinFontSize { get => minFontSize; set => minFontSize = value; }
    public float BarHeight => barHeight;
    public float SidePadding => sidePadding;
    /// <summary>The width kept free at the bar's right end for the speed buttons, sidePadding included.</summary>
    public float RightReserve => sidePadding + speedClusterWidth;
    /// <summary>The font size the readouts currently show at (#111): fontSize, or smaller when the view is too narrow.</summary>
    public int FittedFontSize { get; private set; }
    public float PollIntervalSeconds { get => pollIntervalSeconds; set => pollIntervalSeconds = value; }

    /// <summary>The readouts as of the last Refresh (also what the labels show).</summary>
    public string GoblinText { get; private set; } = "";
    public string FoodText { get; private set; } = "";
    public string MaterialText { get; private set; } = "";
    public string NextHeroText { get; private set; } = "";

    private Text goblinLabel;
    private Text foodLabel;
    private Text materialLabel;
    private Text nextHeroLabel;
    private float pollTimer;

    private void Start()
    {
        placementManager ??= FindAnyObjectByType<PlacementManager>();
        heroSpawner ??= FindAnyObjectByType<HeroSpawner>();
        gameManager ??= FindAnyObjectByType<GameManager>();
        stockpile ??= FindAnyObjectByType<Stockpile>();

        CreateBar();
        Refresh();
    }

    private void Update()
    {
        pollTimer += Time.unscaledDeltaTime; // Keeps refreshing while paused (e.g. a building placed mid-pause)
        if (pollTimer < pollIntervalSeconds) return;
        pollTimer = 0f;
        Refresh();
    }

    /// <summary>Recomputes the readouts and shows them. Called on the poll; public so tests can call it.</summary>
    public void Refresh()
    {
        GoblinText = $"Goblins: {CountLivingGoblins()}";
        FoodText = $"Food: {(placementManager != null ? placementManager.TotalFood : 0)}";
        // Unity null check, not ?.: an unwired bar degrades to "0 kinds · 0 total" like the food readout.
        MaterialText = MaterialsSummary(CatalogCounts(stockpile));
        NextHeroText = NextHeroReadout(heroSpawner, gameManager);

        if (goblinLabel != null) goblinLabel.text = GoblinText;
        if (foodLabel != null) foodLabel.text = FoodText;
        if (materialLabel != null) materialLabel.text = MaterialText;
        if (nextHeroLabel != null) nextHeroLabel.text = NextHeroText;
        FitLabels();
    }

    /// <summary>
    /// The largest font size from fontSize down to minFontSize at which fitsAt answers true (#111); minFontSize when
    /// none does. The readouts and the speed buttons together are wider than a narrow view, so the readouts shrink
    /// together rather than overlap.
    /// </summary>
    public static int FitFontSize(int fontSize, int minFontSize, System.Func<int, bool> fitsAt)
    {
        int floor = Mathf.Min(minFontSize, fontSize);
        for (int size = fontSize; size > floor; size--)
            if (fitsAt == null || fitsAt(size)) return size;
        return floor;
    }

    // Layout only: one shared size for all four readouts, so the bar stays even. Re-fitted on each refresh, which
    // also follows a resized view.
    private void FitLabels()
    {
        if (goblinLabel == null) return;
        Text[] labels = { goblinLabel, foodLabel, materialLabel, nextHeroLabel };
        FittedFontSize = FitFontSize(fontSize, minFontSize, size =>
        {
            foreach (Text label in labels)
            {
                float available = label.rectTransform.rect.width;
                if (available > 0f && PreferredWidth(label, size) > available) return false; // No width yet: nothing to fit
            }
            return true;
        });
        foreach (Text label in labels) label.fontSize = FittedFontSize;
    }

    private static float PreferredWidth(Text label, int size)
    {
        TextGenerationSettings settings = label.GetGenerationSettings(Vector2.zero);
        settings.fontSize = size;
        return label.cachedTextGeneratorForLayout.GetPreferredWidth(label.text, settings) / label.pixelsPerUnit;
    }

    /// <summary>
    /// The materials summary (#124): "Materials: 3 kinds · 73 total" for the catalog's kinds and the sum of their
    /// counts ("1 kind" for exactly one). Nothing to count reads "Materials: 0 kinds · 0 total".
    /// </summary>
    public static string MaterialsSummary(IList<(string name, int count)> materials)
    {
        int kinds = materials?.Count ?? 0;
        int total = 0;
        for (int i = 0; i < kinds; i++) total += materials[i].count;
        return $"Materials: {kinds} {(kinds == 1 ? "kind" : "kinds")} · {total} total";
    }

    // Each catalog material's name and count, in catalog order. Unwired (Unity null check) is no kinds at all; an
    // empty catalog degrades to stone alone, as before #109.
    private static List<(string name, int count)> CatalogCounts(Stockpile stockpile)
    {
        var list = new List<(string name, int count)>();
        if (stockpile == null) return list;
        foreach (MaterialDefinition d in stockpile.Definitions)
            if (d != null) list.Add((stockpile.DisplayName(d.type), stockpile.Count(d.type)));
        if (list.Count == 0) list.Add((stockpile.DisplayName(MaterialType.Stone), stockpile.Count(MaterialType.Stone)));
        return list;
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

        // The readouts share the bar left of the reserve: full padding at the bar's left edge, a small gap between slots,
        // and none at the right, where the reserve already holds the gap to the speed buttons.
        var readoutsGo = new GameObject("Readouts", typeof(RectTransform));
        readoutsGo.transform.SetParent(barGo.transform, false);
        var readoutsRect = (RectTransform)readoutsGo.transform;
        readoutsRect.anchorMin = Vector2.zero;
        readoutsRect.anchorMax = Vector2.one;
        readoutsRect.offsetMin = Vector2.zero;
        readoutsRect.offsetMax = new Vector2(-RightReserve, 0f);

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        float x = 0f;
        goblinLabel = CreateLabel(readoutsRect, "GoblinReadout", font, x, goblinShare, TextAnchor.MiddleLeft, sidePadding, slotGap);
        x += goblinShare;
        foodLabel = CreateLabel(readoutsRect, "FoodReadout", font, x, foodShare, TextAnchor.MiddleCenter, slotGap, slotGap);
        x += foodShare;
        materialLabel = CreateLabel(readoutsRect, "MaterialReadout", font, x, materialShare, TextAnchor.MiddleCenter, slotGap, slotGap);
        x += materialShare;
        nextHeroLabel = CreateLabel(readoutsRect, "NextHeroReadout", font, x, heroShare, TextAnchor.MiddleRight, slotGap, 0f);
    }

    // A label filling width (a fraction of the readout area) from xMin, inset by leftPad / rightPad.
    private Text CreateLabel(Transform parent, string name, Font font, float xMin, float width, TextAnchor alignment,
        float leftPad, float rightPad)
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
        rect.anchorMax = new Vector2(xMin + width, 1f);
        rect.offsetMin = new Vector2(leftPad, 0f);
        rect.offsetMax = new Vector2(-rightPad, 0f);
        return text;
    }
}
