using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// DESIGN §A.7's three alerts as transient toasts: "Goblin starving!", "Hero approaching!", "Trap needs
/// rearming." No event bus — like the top bar, this polls game state and fires on transitions:
/// a goblin becoming weakened (once per onset; re-arms when he eats), each hero spawn, and a spike trap going
/// from armed to spent (once per spend; the imp's rearm is silent and re-arms it). The first poll only
/// snapshots, so a staged or loaded state is never an "onset".
/// Display: a top-right stack under the top bar, newest on top, each toast lasting toastVisibleSeconds.
/// Display-only (no raycaster, raycastTarget off) and on unscaled time, so toasts never block board input and
/// still expire while the game is paused.
/// </summary>
[DisallowMultipleComponent]
public class AlertToasts : MonoBehaviour
{
    public const string StarvingMessage = "Goblin starving!";        // DESIGN §A.7, verbatim
    public const string HeroApproachingMessage = "Hero approaching!"; // DESIGN §A.7, verbatim
    public const string TrapSpentMessage = "Trap needs rearming.";    // DESIGN §A.7, verbatim

    [Header("References")]
    [SerializeField] private PlacementManager placementManager; // Spike traps
    [SerializeField] private HeroSpawner heroSpawner;           // Optional: unwired = no hero toasts

    [Header("Polling")]
    [SerializeField] private float pollIntervalSeconds = 0.25f;

    [Header("Toasts")]
    [SerializeField] private float toastVisibleSeconds = 4f;
    [SerializeField] private int maxVisibleToasts = 3;
    [SerializeField] private int fontSize = 18;                      // Between the build bar (14) and the top bar (20)
    [SerializeField] private Vector2 toastSize = new Vector2(240f, 30f);
    [SerializeField] private float gapBelowTopBar = 44f;             // Top bar is 36 tall, plus an 8 px gap
    [SerializeField] private float spacing = 6f;
    [SerializeField] private float edgePadding = 8f;
    [SerializeField] private Color panelColor = new Color(0f, 0f, 0f, 0.6f);

    public PlacementManager PlacementManager { get => placementManager; set => placementManager = value; }
    public HeroSpawner HeroSpawner { get => heroSpawner; set => heroSpawner = value; }
    public float PollIntervalSeconds { get => pollIntervalSeconds; set => pollIntervalSeconds = value; }
    public float ToastVisibleSeconds { get => toastVisibleSeconds; set => toastVisibleSeconds = value; }
    public int MaxVisibleToasts { get => maxVisibleToasts; set => maxVisibleToasts = value; }

    /// <summary>The visible toasts' texts in display order: newest first (top of the stack).</summary>
    public IReadOnlyList<string> ActiveToastTexts => activeTexts;

    private class Toast
    {
        public string Text;
        public float Age;
        public GameObject View; // null until the UI exists (e.g. EditMode tests)
    }

    private readonly List<Toast> toasts = new List<Toast>(); // Index 0 = newest
    private readonly List<string> activeTexts = new List<string>();

    // Last observed state, for edge detection.
    private bool baselined;
    private readonly Dictionary<Goblin, bool> goblinWeakened = new Dictionary<Goblin, bool>();
    private readonly Dictionary<SpikeTrap, bool> trapArmed = new Dictionary<SpikeTrap, bool>();
    private int lastHeroesSpawned;

    private RectTransform stack;
    private Font font;
    private float pollTimer;

    private void Start()
    {
        placementManager ??= FindAnyObjectByType<PlacementManager>();
        heroSpawner ??= FindAnyObjectByType<HeroSpawner>();

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
    }

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
        foreach (SpikeTrap trap in placementManager.GetSpikeTraps())
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

    /// <summary>Shows a toast on top of the stack; the oldest goes at once if the cap would be exceeded.</summary>
    public void Show(string text)
    {
        while (toasts.Count >= Mathf.Max(1, maxVisibleToasts)) RemoveAt(toasts.Count - 1);

        var toast = new Toast { Text = text, Age = 0f, View = CreateToastView(text) };
        toasts.Insert(0, toast);
        Relayout();
    }

    /// <summary>Ages every toast and removes the expired ones. Called each frame on unscaled time; public for tests.</summary>
    public void AgeToasts(float deltaSeconds)
    {
        bool changed = false;
        for (int i = toasts.Count - 1; i >= 0; i--)
        {
            toasts[i].Age += deltaSeconds;
            if (toasts[i].Age < toastVisibleSeconds) continue;
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
        // No GraphicRaycaster: toasts must never take input from the board under them.

        var stackGo = new GameObject("ToastStack", typeof(RectTransform));
        stackGo.transform.SetParent(canvasGo.transform, false);
        stack = (RectTransform)stackGo.transform;
        stack.anchorMin = stack.anchorMax = new Vector2(1f, 1f); // Top-right, under the top bar
        stack.pivot = new Vector2(1f, 1f);
        stack.anchoredPosition = new Vector2(-edgePadding, -gapBelowTopBar);
        stack.sizeDelta = toastSize;

        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    private GameObject CreateToastView(string text)
    {
        if (stack == null) return null; // No UI (EditMode tests)

        var panelGo = new GameObject("Toast");
        panelGo.transform.SetParent(stack, false);
        var panel = panelGo.AddComponent<Image>();
        panel.color = panelColor;
        panel.raycastTarget = false;
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
