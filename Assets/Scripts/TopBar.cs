using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The top bar (DESIGN §A.7): living goblins, food stock, power (#144), a materials summary (#124: kinds and total, so
/// the bar no longer grows with the catalog), and the next-hero state, polled every pollIntervalSeconds. Built in code
/// like the rest of the chrome (own overlay Canvas, no scene-authored UI). raycastTarget is off on everything but the
/// power segment, so the bar never blocks board input — the player digs right under it (the door column). The speed
/// buttons at its right end are GameSpeed's, on GameSpeed's canvas; the readouts leave that width free.
/// Power (#144): "⚡ supply/demand" summed across every grid (a node on no grid counts nowhere), in the warning color
/// while any grid is browned out. The segment is the power overlay's switch: clicking it, or the rebindable
/// TogglePowerView key, flips the board renderer's view mode, and the segment is tinted while the overlay is on.
/// Mana (#145): a sixth segment, "✦ total" — every mana type across every store — is the Magic view's switch the same way
/// (click, or ToggleMagicView). The two views replace each other; they never stack.
/// Updates after the simulation each frame (execution order), so a poll never catches a half-updated frame,
/// e.g. the route opening before the spawner has spawned the hero it unlocks.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(1000)]
public class TopBar : MonoBehaviour
{
    public const string Dash = "—";
    public const string DoorSealedText = "Next hero: door sealed"; // #163: no hero clock until the door is dug out

    [Header("References")]
    [SerializeField] private PlacementManager placementManager;
    [SerializeField] private HeroSpawner heroSpawner;
    [SerializeField] private GameManager gameManager; // Optional: once the game has ended, the hero readout shows a dash
    [SerializeField] private Stockpile stockpile;     // The materials summary's source (#102)
    [SerializeField] private PowerGrid powerGrid;     // #144: the power readout's source; unwired, "⚡ 0/0"
    [SerializeField] private BoardRenderer boardRenderer; // #144: holds the view mode the power segment toggles
    [SerializeField] private ManaFlow manaFlow;       // #145: the mana readout's source; unwired, "✦ 0"

    [Header("Appearance")]
    [SerializeField] private int fontSize = 20;                                    // Persistent HUD: larger than the build chrome's
    [SerializeField] private int minFontSize = 10;                                 // #111: the readouts shrink together to fit a narrow view, never below this (#145: 12 → 10 for six readouts)
    [SerializeField] private float barHeight = 36f;   // GameSpeed.stripHeight and BuildRail.topOffset agree with this
    [SerializeField] private float sidePadding = 16f; // At the bar's outer edges; GameSpeed.barInset agrees with this
    [SerializeField] private float slotGap = 4f;      // Between readouts (#114)
    [SerializeField] private Color panelColor = new Color(0f, 0f, 0f, 0.6f);      // Matches the build rail

    [Header("Layout (#124)")]
    // Kept free at the bar's right end, inside sidePadding, for GameSpeed's buttons: its panel (GameSpeed.PanelWidth,
    // 216) plus a slotGap to the next-hero readout.
    [SerializeField] private float speedClusterWidth = 220f;
    // Each readout's share of the width left of that reserve: weights over their sum (#144). #145 retuned all six to the
    // measured widths of their longest contents plus each slot's padding, at size 10 ("Goblins: 10" 53+20, "Food: 15"
    // 42+8, "⚡ 140/160" 56+8, "✦ 120" 31+8, "Materials: 3 kinds · 123 total" 130+8, "Next hero: needs a route" 116+4 px:
    // 484 of a 732 px view's 495), so the bar fits there at size 10. From about 1000 px wide it fits at the full 20.
    [SerializeField] private float goblinShare = 0.151f;
    [SerializeField] private float foodShare = 0.103f;
    [SerializeField] private float materialShare = 0.285f;
    [SerializeField] private float heroShare = 0.248f;
    [SerializeField] private float powerShare = 0.132f; // #144: the fifth readout
    [SerializeField] private float magicShare = 0.081f; // #145: the sixth, "✦ 120"

    [Header("Power (#144)")]
    [SerializeField] private Color powerWarningColor = new Color(1f, 0.38f, 0.32f, 1f);     // Any grid browned out
    [SerializeField] private Color segmentIdleColor = new Color(1f, 1f, 1f, 0f);            // Invisible, still clickable
    [SerializeField] private Color segmentActiveColor = new Color(0.35f, 0.65f, 1f, 0.35f); // The overlay is on
    [SerializeField] private Color magicSegmentActiveColor = new Color(0.66f, 0.48f, 1f, 0.35f); // #145: the Magic view is on

    [Header("Refresh")]
    [SerializeField] private float pollIntervalSeconds = 0.25f;

    public PlacementManager PlacementManager { get => placementManager; set => placementManager = value; }
    public HeroSpawner HeroSpawner { get => heroSpawner; set => heroSpawner = value; }
    public GameManager GameManager { get => gameManager; set => gameManager = value; }
    public Stockpile Stockpile { get => stockpile; set => stockpile = value; }
    public PowerGrid PowerGrid { get => powerGrid; set => powerGrid = value; }
    public BoardRenderer BoardRenderer { get => boardRenderer; set => boardRenderer = value; }
    public Color PowerWarningColor => powerWarningColor;
    public ManaFlow ManaFlow { get => manaFlow; set => manaFlow = value; }
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
    public string PowerText { get; private set; } = "";

    /// <summary>True while any grid is browned out as of the last Refresh: the power readout shows the warning color.</summary>
    public bool PowerWarning { get; private set; }

    /// <summary>The power segment's click target (null until the bar is built).</summary>
    public Button PowerSegment => powerButton;

    /// <summary>The mana readout as of the last Refresh (#145): "✦ 12".</summary>
    public string MagicText { get; private set; } = "";

    /// <summary>The mana segment's click target (#145; null until the bar is built).</summary>
    public Button MagicSegment => magicButton;

    private Text goblinLabel;
    private Text foodLabel;
    private Text materialLabel;
    private Text nextHeroLabel;
    private Text powerLabel;
    private Image powerSegmentImage;
    private Button powerButton;
    private Text magicLabel;
    private Image magicSegmentImage;
    private Button magicButton;
    private float pollTimer;
    private InputConfig.Actions input; // Bindings come from InputConfig (rebindable); enabled with this component
    // #137: true once the segment's click listener is wired in the current domain. NonSerialized, so a domain reload
    // resets it while the button reference survives (the listener, added in code, doesn't).
    [System.NonSerialized] private bool segmentWired;

    private void Awake()
    {
        EnsureInput();
    }

    // #137: a domain reload mid-Play re-runs OnEnable but not Awake or Start, so the input is ensured here and a segment
    // that outlived the reload gets its listener back.
    private void OnEnable()
    {
        EnsureInput();
        input.Enable();
        if ((powerButton != null || magicButton != null) && !segmentWired) WireSegment();
    }

    private void OnDisable()
    {
        input?.Disable();
    }

    private void OnDestroy()
    {
        input?.Dispose();
    }

    private void EnsureInput() => input ??= InputConfig.CreateActions();

    private void Start()
    {
        placementManager ??= FindAnyObjectByType<PlacementManager>();
        heroSpawner ??= FindAnyObjectByType<HeroSpawner>();
        gameManager ??= FindAnyObjectByType<GameManager>();
        stockpile ??= FindAnyObjectByType<Stockpile>();
        powerGrid ??= FindAnyObjectByType<PowerGrid>();
        manaFlow ??= FindAnyObjectByType<ManaFlow>();
        boardRenderer ??= FindAnyObjectByType<BoardRenderer>();

        PlacementController.EnsureEventSystem(); // The power segment takes clicks
        CreateBar();
        Refresh();
    }

    private void Update()
    {
        // #126: V types into a focused field; it doesn't flip the overlay.
        if (!TextInputFocus.IsTyping && (input?.TogglePowerView.WasPressedThisFrame() ?? false)) TogglePowerOverlay();
        if (!TextInputFocus.IsTyping && (input?.ToggleMagicView.WasPressedThisFrame() ?? false)) ToggleMagicOverlay(); // #145
        if (!TextInputFocus.IsTyping && (input?.ToggleRoomView.WasPressedThisFrame() ?? false)) ToggleRoomView();      // #165

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
        // Unity null check, not ?.: an unwired bar reads "⚡ 0/0", neutral.
        PowerSummary power = SummarizePower(powerGrid != null ? powerGrid.AllGridTotals() : null);
        PowerText = PowerReadout(power.Supply, power.Demand);
        PowerWarning = power.Brownout;
        MagicText = MagicReadout(manaFlow != null ? manaFlow.TotalAll() : 0); // Unity null check: unwired reads "✦ 0"

        if (goblinLabel != null) goblinLabel.text = GoblinText;
        if (foodLabel != null) foodLabel.text = FoodText;
        if (materialLabel != null) materialLabel.text = MaterialText;
        if (nextHeroLabel != null) nextHeroLabel.text = NextHeroText;
        if (powerLabel != null)
        {
            powerLabel.text = PowerText;
            powerLabel.color = PowerWarning ? powerWarningColor : Color.white;
        }
        if (magicLabel != null) magicLabel.text = MagicText;
        RefreshSegmentTint();
        FitLabels();
    }

    // ---- power (#144) ----

    /// <summary>All grids' totals together: summed supply and demand, and whether any grid is browned out.</summary>
    public readonly struct PowerSummary
    {
        public readonly int Supply;
        public readonly int Demand;
        public readonly bool Brownout;

        public PowerSummary(int supply, int demand, bool brownout)
        {
            Supply = supply;
            Demand = demand;
            Brownout = brownout;
        }
    }

    /// <summary>
    /// Sums every grid's supply and demand; Brownout is true when any one grid is short (PowerGrid.IsBrownout: equality is
    /// not a brownout). Grids are summed, not netted, so a surplus on one grid never hides a shortfall on another. No grids
    /// (or null) is 0/0, neutral.
    /// </summary>
    public static PowerSummary SummarizePower(IEnumerable<PowerGrid.GridTotals> grids)
    {
        int supply = 0;
        int demand = 0;
        bool brownout = false;
        if (grids != null)
        {
            foreach (PowerGrid.GridTotals grid in grids)
            {
                supply += grid.Supply;
                demand += grid.Demand;
                brownout |= PowerGrid.IsBrownout(grid);
            }
        }
        return new PowerSummary(supply, demand, brownout);
    }

    /// <summary>The power readout: "⚡ 140/160" (watts produced / demanded across all grids).</summary>
    public static string PowerReadout(int supply, int demand) => $"⚡ {supply}/{demand}";

    /// <summary>The mana readout (#145): "✦ 120", every type across every store.</summary>
    public static string MagicReadout(int total) => $"✦ {total}";

    /// <summary>
    /// Flips the Magic view (#145: the mana segment's click and the ToggleMagicView key). Like the power overlay it changes
    /// only the view mode, and it replaces the power view rather than stacking on it.
    /// </summary>
    public void ToggleMagicOverlay()
    {
        if (boardRenderer == null) return;
        boardRenderer.ToggleViewMode(ViewMode.Magic);
        RefreshSegmentTint();
    }

    /// <summary>
    /// Flips the Room view (#165: the ToggleRoomView key, R). Like the other overlays it changes only the view mode and
    /// replaces whichever overlay is showing; it has no top-bar segment.
    /// </summary>
    public void ToggleRoomView()
    {
        if (boardRenderer == null) return;
        boardRenderer.ToggleViewMode(ViewMode.Room);
        RefreshSegmentTint();
    }

    /// <summary>
    /// Flips the power overlay (the segment's click and the TogglePowerView key). It changes only the board's view mode:
    /// the tool, the selection and the STOCK panel are untouched, and Esc doesn't close it.
    /// </summary>
    public void TogglePowerOverlay()
    {
        if (boardRenderer == null) return;
        boardRenderer.ToggleViewMode(ViewMode.Power);
        RefreshSegmentTint();
    }

    private void RefreshSegmentTint()
    {
        ViewMode mode = boardRenderer != null ? boardRenderer.Mode : ViewMode.Normal;
        if (powerSegmentImage != null) powerSegmentImage.color = mode == ViewMode.Power ? segmentActiveColor : segmentIdleColor;
        if (magicSegmentImage != null) magicSegmentImage.color = mode == ViewMode.Magic ? magicSegmentActiveColor : segmentIdleColor;
    }

    // Wires exactly once whether CreateBar or a reload calls it.
    private void WireSegment()
    {
        if (powerButton != null)
        {
            powerButton.onClick.RemoveAllListeners();
            powerButton.onClick.AddListener(OnSegmentClicked);
        }
        if (magicButton != null)
        {
            magicButton.onClick.RemoveAllListeners();
            magicButton.onClick.AddListener(OnMagicSegmentClicked);
        }
        segmentWired = true;
    }

    // A clicked Button stays selected and would take keyboard submits; the segment never keeps focus.
    private void OnSegmentClicked()
    {
        TogglePowerOverlay();
        ReleaseFocus();
    }

    private void OnMagicSegmentClicked()
    {
        ToggleMagicOverlay();
        ReleaseFocus();
    }

    private static void ReleaseFocus()
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem != null) eventSystem.SetSelectedGameObject(null);
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

    // Layout only: one shared size for all five readouts, so the bar stays even. Re-fitted on each refresh, which
    // also follows a resized view.
    private void FitLabels()
    {
        if (goblinLabel == null || powerLabel == null || magicLabel == null) return;
        Text[] labels = { goblinLabel, foodLabel, powerLabel, magicLabel, materialLabel, nextHeroLabel };
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
    /// The materials summary (#124): "Materials: 3 kinds · 73 total" for the given kinds (the discovered ones, #126) and
    /// the sum of their counts ("1 kind" for exactly one). Nothing to count reads "Materials: 0 kinds · 0 total".
    /// </summary>
    public static string MaterialsSummary(IList<(string name, int count)> materials)
    {
        int kinds = materials?.Count ?? 0;
        int total = 0;
        for (int i = 0; i < kinds; i++) total += materials[i].count;
        return $"Materials: {kinds} {(kinds == 1 ? "kind" : "kinds")} · {total} total";
    }

    /// <summary>
    /// Each discovered catalog material's name and count (#126: undiscovered kinds don't count; their count is 0 anyway,
    /// since holding one discovers it), in catalog order. Unwired (Unity null check) is no kinds at all; an empty
    /// catalog degrades to stone alone, as before #109.
    /// </summary>
    public static List<(string name, int count)> CatalogCounts(Stockpile stockpile)
    {
        var list = new List<(string name, int count)>();
        if (stockpile == null) return list;
        bool anyDefinition = false;
        foreach (MaterialDefinition d in stockpile.Definitions)
        {
            if (d == null) continue;
            anyDefinition = true;
            if (stockpile.IsDiscovered(d.type)) list.Add((stockpile.DisplayName(d.type), stockpile.Count(d.type)));
        }
        if (!anyDefinition) list.Add((stockpile.DisplayName(MaterialType.Stone), stockpile.Count(MaterialType.Stone)));
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
    /// between heroes → the live countdown ("needs a route" once it's up with no route in, #165); before the door is dug out (#163) → "door sealed" (the clock hasn't started);
    /// before hero 1 → "needs a route" if the timer has run out but the dungeon
    /// is sealed, else the countdown to the first hero.
    /// </summary>
    public static string NextHeroReadout(HeroSpawner spawner, GameManager game)
    {
        if (spawner == null) return $"Next hero: {Dash}";
        bool gameEnded = game != null && game.State != GameManager.GameState.Playing;
        bool allDealtWith = spawner.TotalHeroes > 0 && spawner.HeroesResolved >= spawner.TotalHeroes;
        if (gameEnded || allDealtWith) return $"Next hero: {Dash}";

        if (spawner.HeroesSpawned > spawner.HeroesResolved) return "Hero in the dungeon";
        if (spawner.NextHeroScheduled) // #165: a later hero whose time is up but whose way in is walled or doored off waits too
            return spawner.SecondsUntilNextHero <= 0f && !spawner.RouteExists()
                ? "Next hero: needs a route"
                : $"Next hero: {FormatCountdown(spawner.SecondsUntilNextHero)}";
        if (spawner.HeroesSpawned == 0 && !spawner.ClockStarted) return DoorSealedText; // #163: setup time

        if (spawner.FirstHeroDue && !spawner.RouteExists()) return "Next hero: needs a route";
        return $"Next hero: {FormatCountdown(spawner.FirstHeroSeconds - spawner.ElapsedSeconds)}";
    }

    /// <summary>m:ss of the seconds rounded up (5:00, 1:30, 0:07); never negative.</summary>
    public static string FormatCountdown(float seconds)
    {
        int total = Mathf.CeilToInt(Mathf.Max(0f, seconds));
        return $"{total / 60}:{total % 60:00}";
    }

    // ---- UI (code-built, display-only) ----

    /// <summary>Builds the bar's UI once. Start calls this; public so tests can build it without Play.</summary>
    public void CreateBar()
    {
        if (goblinLabel != null) return;
        var canvasGo = new GameObject("TopBarCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGo.AddComponent<CanvasScaler>();
        // #144: a raycaster for the power segment only. Every other graphic has raycastTarget off, so the rest of the bar
        // still never takes input from the board under it.
        canvasGo.AddComponent<GraphicRaycaster>();

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
        float[] shares = NormalizedShares(goblinShare, foodShare, powerShare, magicShare, materialShare, heroShare);
        float x = 0f;
        goblinLabel = CreateLabel(readoutsRect, "GoblinReadout", font, x, shares[0], TextAnchor.MiddleLeft, sidePadding, slotGap);
        x += shares[0];
        foodLabel = CreateLabel(readoutsRect, "FoodReadout", font, x, shares[1], TextAnchor.MiddleCenter, slotGap, slotGap);
        x += shares[1];
        powerLabel = CreateViewSegment(readoutsRect, "PowerSegment", "PowerReadout", font, x, shares[2],
            out powerSegmentImage, out powerButton);
        x += shares[2];
        magicLabel = CreateViewSegment(readoutsRect, "MagicSegment", "MagicReadout", font, x, shares[3], // #145
            out magicSegmentImage, out magicButton);
        x += shares[3];
        materialLabel = CreateLabel(readoutsRect, "MaterialReadout", font, x, shares[4], TextAnchor.MiddleCenter, slotGap, slotGap);
        x += shares[4];
        nextHeroLabel = CreateLabel(readoutsRect, "NextHeroReadout", font, x, shares[5], TextAnchor.MiddleRight, slotGap, 0f);
        WireSegment();
    }

    /// <summary>
    /// The readouts' shares as fractions of the readout width: each weight over their sum (#144), so adding a readout
    /// never overflows the bar. Negative weights count as 0; all-zero weights split the width evenly.
    /// </summary>
    public static float[] NormalizedShares(params float[] weights)
    {
        var shares = new float[weights.Length];
        float sum = 0f;
        foreach (float w in weights) sum += Mathf.Max(0f, w);
        for (int i = 0; i < weights.Length; i++)
            shares[i] = sum > 0f ? Mathf.Max(0f, weights[i]) / sum : 1f / weights.Length;
        return shares;
    }

    // A readout in a clickable slot (#144 power, #145 mana): the slot's Image is one of the bar's only raycast targets,
    // the label on it raycasts nothing.
    private Text CreateViewSegment(Transform parent, string segmentName, string readoutName, Font font, float xMin, float width,
        out Image image, out Button button)
    {
        var go = new GameObject(segmentName);
        go.transform.SetParent(parent, false);
        image = go.AddComponent<Image>();
        image.color = segmentIdleColor;
        image.raycastTarget = true;
        button = go.AddComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.None; // The tint shows the overlay's state, not hover or press
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(xMin, 0f);
        rect.anchorMax = new Vector2(xMin + width, 1f);
        rect.offsetMin = new Vector2(slotGap, 0f);
        rect.offsetMax = new Vector2(-slotGap, 0f);
        return CreateLabel(go.transform, readoutName, font, 0f, 1f, TextAnchor.MiddleCenter, 0f, 0f);
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
