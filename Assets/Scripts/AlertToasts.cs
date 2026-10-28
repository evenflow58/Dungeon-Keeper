using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// DESIGN §A.7's three alerts as transient toasts: "Goblin starving!", "Hero approaching!", "Trap needs
/// rearming." No event bus — like the top bar, this polls game state and fires on transitions:
/// a goblin becoming weakened (once per onset; re-arms when he eats), each hero spawn, and a spike trap going
/// from armed to spent (once per spend; the imp's rearm is silent and re-arms it). The first poll only
/// snapshots, so a staged or loaded state is never an "onset".
/// Heart printing (#161): one "Imps await at the Heart!" toast per cycle, when the Heart's new candidate list arrives
/// (the game-start list included: this one isn't held back by the baseline), never repeated for a list that's ignored.
/// It is the one clickable toast: a click opens the Heart panel and dismisses the toast.
/// Display: a top-right stack under the top bar, newest on top, each toast lasting toastVisibleSeconds (a clickable one
/// clickableToastVisibleSeconds, so there's time to reach it), on unscaled time
/// so toasts still expire while the game is paused. Only a clickable toast is a raycast target; every other toast (and
/// the stack) has raycastTarget off, so they never block board input.
/// </summary>
[DisallowMultipleComponent]
public class AlertToasts : MonoBehaviour
{
    public const string StarvingMessage = "Goblin starving!";        // DESIGN §A.7, verbatim
    public const string HeroApproachingMessage = "Hero approaching!"; // DESIGN §A.7, verbatim
    public const string TrapSpentMessage = "Trap needs rearming.";    // DESIGN §A.7, verbatim
    public const string ImpsAwaitMessage = "Imps await at the Heart!"; // #161: a new candidate list

    [Header("References")]
    [SerializeField] private PlacementManager placementManager; // Spike traps
    [SerializeField] private HeroSpawner heroSpawner;           // Optional: unwired = no hero toasts
    [SerializeField] private HeartPrinting heartPrinting;       // #161, optional: unwired = no Heart toasts
    [SerializeField] private HeartPanel heartPanel;             // #161: what the Heart toast opens

    [Header("Polling")]
    [SerializeField] private float pollIntervalSeconds = 0.25f;

    [Header("Toasts")]
    [SerializeField] private float toastVisibleSeconds = 4f;
    [SerializeField] private float clickableToastVisibleSeconds = 12f; // #161: long enough to reach and click
    [SerializeField] private int maxVisibleToasts = 3;
    [SerializeField] private int fontSize = 18;                      // Above the build chrome's text (at most 15), below the top bar (20)
    [SerializeField] private Vector2 toastSize = new Vector2(240f, 30f);
    [SerializeField] private float gapBelowTopBar = 44f;             // Top bar is 36 tall, plus an 8 px gap
    [SerializeField] private float spacing = 6f;
    [SerializeField] private float edgePadding = 8f;
    [SerializeField] private Color panelColor = new Color(0f, 0f, 0f, 0.6f);

    public PlacementManager PlacementManager { get => placementManager; set => placementManager = value; }
    public HeroSpawner HeroSpawner { get => heroSpawner; set => heroSpawner = value; }
    public HeartPrinting HeartPrinting { get => heartPrinting; set => heartPrinting = value; }
    public HeartPanel HeartPanel { get => heartPanel; set => heartPanel = value; }
    public float PollIntervalSeconds { get => pollIntervalSeconds; set => pollIntervalSeconds = value; }
    public float ToastVisibleSeconds { get => toastVisibleSeconds; set => toastVisibleSeconds = value; }
    public float ClickableToastVisibleSeconds { get => clickableToastVisibleSeconds; set => clickableToastVisibleSeconds = value; }
    public int MaxVisibleToasts { get => maxVisibleToasts; set => maxVisibleToasts = value; }

    /// <summary>The visible toasts' texts in display order: newest first (top of the stack).</summary>
    public IReadOnlyList<string> ActiveToastTexts => activeTexts;

    private class Toast
    {
        public string Text;
        public float Age;
        public GameObject View; // null until the UI exists (e.g. EditMode tests)
        public System.Action OnClick; // #161: null = display-only
    }

    private readonly List<Toast> toasts = new List<Toast>(); // Index 0 = newest
    private readonly List<string> activeTexts = new List<string>();

    // Last observed state, for edge detection.
    private bool baselined;
    private readonly Dictionary<Goblin, bool> goblinWeakened = new Dictionary<Goblin, bool>();
    private readonly Dictionary<SpikeTrap, bool> trapArmed = new Dictionary<SpikeTrap, bool>();
    private int lastHeroesSpawned;
    private int lastAnnouncedCycle = -1; // #161: the Heart cycle whose list has had its toast

    private RectTransform stack;
    private Font font;
    private float pollTimer;

    private void Start()
    {
        placementManager ??= FindAnyObjectByType<PlacementManager>();
        heroSpawner ??= FindAnyObjectByType<HeroSpawner>();
        heartPrinting ??= FindAnyObjectByType<HeartPrinting>();
        heartPanel ??= FindAnyObjectByType<HeartPanel>();

        CreateStack();
        CheckAlerts(); // Baseline only
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime; // Chrome: live while paused
        AgeToasts(dt);

        pollTimer += dt;
        if (pollTimer < pollIntervalSeconds) return;
        pollTimer = 0f;
        CheckAlerts();
    }

    /// <summary>
    /// One poll: compares current state with the last poll and raises a toast for each new onset. The first call
    /// only records the baseline. Called on the poll; public so tests can step it.
    /// </summary>
    public void CheckAlerts()
    {
        bool fire = baselined;
        baselined = true;

        CheckGoblins(fire);
        CheckHeroes(fire);
        CheckTraps(fire);
        CheckHeart();
    }

    // #161: once per cycle, as soon as its list is up. Not gated on the baseline: the game-start list is news too.
    private void CheckHeart()
    {
        if (heartPrinting == null || heartPrinting.Cycle < 0 || heartPrinting.Cycle == lastAnnouncedCycle) return;
        lastAnnouncedCycle = heartPrinting.Cycle;
        if (heartPrinting.State != HeartPrinting.CycleState.Open) return; // Already used (can't be, at a fresh boundary)
        Show(ImpsAwaitMessage, OpenHeartPanel);
    }

    private void OpenHeartPanel() => heartPanel?.Open();

    private void CheckGoblins(bool fire)
    {
        var seen = new HashSet<Goblin>();
        foreach (Goblin g in FindObjectsByType<Goblin>()) // Active only: the dead are deactivated
        {
            if (g.IsDead) continue;
            seen.Add(g);
            bool weakened = g.IsWeakened;
            bool known = goblinWeakened.TryGetValue(g, out bool wasWeakened);
            goblinWeakened[g] = weakened;
            if (fire && known && weakened && !wasWeakened) Show(StarvingMessage);
        }
        DropUnseen(goblinWeakened, seen); // Dead or gone: dropped silently (no death alert in §A.7)
    }

    private void CheckHeroes(bool fire)
    {
        if (heroSpawner == null) return;
        int spawned = heroSpawner.HeroesSpawned;
        if (fire)
        {
            for (int i = lastHeroesSpawned; i < spawned; i++) Show(HeroApproachingMessage); // Once per hero
        }
        lastHeroesSpawned = spawned;
    }

    private void CheckTraps(bool fire)
    {
        if (placementManager == null) return;
        var seen = new HashSet<SpikeTrap>();
        foreach (SpikeTrap trap in placementManager.GetBuiltSpikeTraps()) // A site isn't a trap yet
        {
            seen.Add(trap);
            bool armed = trap.IsArmed;
            bool known = trapArmed.TryGetValue(trap, out bool wasArmed);
            trapArmed[trap] = armed;
            if (fire && known && wasArmed && !armed) Show(TrapSpentMessage); // Rearming is silent
        }
        DropUnseen(trapArmed, seen);
    }

    private static void DropUnseen<T>(Dictionary<T, bool> tracked, HashSet<T> seen)
    {
        var gone = new List<T>();
        foreach (T key in tracked.Keys)
            if (!seen.Contains(key)) gone.Add(key);
        foreach (T key in gone) tracked.Remove(key);
    }

    // ---- toasts ----

    /// <summary>
    /// Shows a toast on top of the stack; the oldest goes at once if the cap would be exceeded. With onClick (#161) the
    /// toast is a button: a click runs it and dismisses the toast.
    /// </summary>
    public void Show(string text, System.Action onClick = null)
    {
        while (toasts.Count >= Mathf.Max(1, maxVisibleToasts)) RemoveAt(toasts.Count - 1);

        var toast = new Toast { Text = text, Age = 0f, OnClick = onClick };
        toast.View = CreateToastView(toast);
        toasts.Insert(0, toast);
        Relayout();
    }

    /// <summary>
    /// Clicks the visible toast at this display index (0 = newest): runs its action and dismisses it. False (nothing
    /// happens) for a display-only toast or an index out of range. The clickable toast's button calls this.
    /// </summary>
    public bool ClickToast(int index)
    {
        if (index < 0 || index >= toasts.Count || toasts[index].OnClick == null) return false;
        System.Action onClick = toasts[index].OnClick;
        RemoveAt(index);
        onClick();
        return true;
    }

    /// <summary>Whether the visible toast at this display index is clickable (tests).</summary>
    public bool IsClickable(int index) => index >= 0 && index < toasts.Count && toasts[index].OnClick != null;

    /// <summary>The visible toast's view at this display index (tests): null without UI.</summary>
    public GameObject ToastView(int index) => index >= 0 && index < toasts.Count ? toasts[index].View : null;

    /// <summary>Ages every toast and removes the expired ones. Called each frame on unscaled time; public for tests.</summary>
    public void AgeToasts(float deltaSeconds)
    {
        bool changed = false;
        for (int i = toasts.Count - 1; i >= 0; i--)
        {
            toasts[i].Age += deltaSeconds;
            float lifetime = toasts[i].OnClick != null ? clickableToastVisibleSeconds : toastVisibleSeconds;
            if (toasts[i].Age < lifetime) continue;
            RemoveAt(i, relayout: false);
            changed = true;
        }
        if (changed) Relayout();
    }

    private void RemoveAt(int index, bool relayout = true)
    {
        GameObject view = toasts[index].View;
        toasts.RemoveAt(index);
        if (view != null)
        {
            if (Application.isPlaying) Destroy(view);
            else DestroyImmediate(view);
        }
        if (relayout) Relayout();
    }

    private void Relayout()
    {
        activeTexts.Clear();
        for (int i = 0; i < toasts.Count; i++)
        {
            activeTexts.Add(toasts[i].Text);
            if (toasts[i].View == null) continue;
            var rect = (RectTransform)toasts[i].View.transform;
            rect.anchoredPosition = new Vector2(0f, -i * (toastSize.y + spacing));
        }
    }

    // ---- UI (code-built, display-only) ----

    private void CreateStack()
    {
        var canvasGo = new GameObject("AlertToastsCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGo.AddComponent<CanvasScaler>();
        canvasGo.AddComponent<GraphicRaycaster>(); // #161: for the clickable toast; every other graphic has raycastTarget off

        var stackGo = new GameObject("ToastStack", typeof(RectTransform));
        stackGo.transform.SetParent(canvasGo.transform, false);
        stack = (RectTransform)stackGo.transform;
        stack.anchorMin = stack.anchorMax = new Vector2(1f, 1f); // Top-right, under the top bar
        stack.pivot = new Vector2(1f, 1f);
        stack.anchoredPosition = new Vector2(-edgePadding, -gapBelowTopBar);
        stack.sizeDelta = toastSize;

        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    private GameObject CreateToastView(Toast toast)
    {
        if (stack == null) return null; // No UI (EditMode tests)
        string text = toast.Text;

        var panelGo = new GameObject("Toast");
        panelGo.transform.SetParent(stack, false);
        var panel = panelGo.AddComponent<Image>();
        panel.color = panelColor;
        panel.raycastTarget = toast.OnClick != null; // Only a clickable toast takes the pointer
        if (toast.OnClick != null)
        {
            var button = panelGo.AddComponent<Button>();
            button.targetGraphic = panel;
            button.onClick.AddListener(() => ClickToast(toasts.IndexOf(toast)));
        }
        var rect = (RectTransform)panelGo.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.sizeDelta = toastSize;

        var labelGo = new GameObject("Text");
        labelGo.transform.SetParent(panelGo.transform, false);
        var label = labelGo.AddComponent<Text>();
        label.font = font;
        label.fontSize = fontSize;
        label.color = Color.white;
        label.alignment = TextAnchor.MiddleCenter;
        label.text = text;
        label.raycastTarget = false;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        var labelRect = (RectTransform)labelGo.transform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;

        return panelGo;
    }
}
