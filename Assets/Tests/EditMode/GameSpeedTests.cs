using NUnit.Framework;
using UnityEngine;

public class GameSpeedTests
{
    private GameObject go;
    private GameSpeed speed;
    private float savedTimeScale;

    [SetUp]
    public void SetUp()
    {
        // Time.timeScale is global and persists in the editor: a leaked 0 would freeze the next Play session.
        savedTimeScale = Time.timeScale;
        go = new GameObject("TestGameSpeed");
        speed = go.AddComponent<GameSpeed>(); // Awake doesn't run for EditMode-added components
    }

    [TearDown]
    public void TearDown()
    {
        GameObject canvas = GameObject.Find("GameSpeedCanvas");
        if (canvas != null) Object.DestroyImmediate(canvas);
        if (go != null) Object.DestroyImmediate(go);
        Time.timeScale = savedTimeScale;
    }

    [Test]
    public void Fresh_IsNormalSpeed_NotPaused()
    {
        Assert.AreEqual(1f, speed.Speed);
        Assert.IsFalse(speed.IsPaused);
    }

    [Test]
    public void Pause_SetsTimeScaleZero()
    {
        speed.SetSpeed(0f);
        Assert.AreEqual(0f, Time.timeScale);
        Assert.AreEqual(0f, speed.Speed);
        Assert.IsTrue(speed.IsPaused);
    }

    [Test]
    public void Fast_SetsTimeScaleTwo()
    {
        speed.SetSpeed(2f);
        Assert.AreEqual(2f, Time.timeScale);
        Assert.AreEqual(2f, speed.Speed);
        Assert.IsFalse(speed.IsPaused);
    }

    [Test]
    public void BackToNormal_FromPause()
    {
        speed.SetSpeed(0f);
        speed.SetSpeed(1f);
        Assert.AreEqual(1f, Time.timeScale);
        Assert.IsFalse(speed.IsPaused);
    }

    [Test]
    public void UnsupportedSpeeds_AreIgnored()
    {
        speed.SetSpeed(2f);

        Assert.DoesNotThrow(() => speed.SetSpeed(3f));
        Assert.AreEqual(2f, speed.Speed);
        Assert.AreEqual(2f, Time.timeScale);

        speed.SetSpeed(-1f);
        speed.SetSpeed(0.5f);
        Assert.AreEqual(2f, speed.Speed);
        Assert.AreEqual(2f, Time.timeScale);
    }

    [Test]
    public void FreshRun_ResetsToNormal_EvenIfTimeScaleWasLeftChanged()
    {
        Time.timeScale = 2f; // e.g. the previous run ended at 2× (a scene reload doesn't reset this)
        speed.ResetToNormalSpeed(); // the path Awake runs

        Assert.AreEqual(1f, Time.timeScale);
        Assert.AreEqual(1f, speed.Speed);

        Time.timeScale = 0f;
        speed.ResetToNormalSpeed();
        Assert.AreEqual(1f, Time.timeScale);
    }

    [Test]
    public void Buttons_NotBuiltInEditMode_NoHighlight_NoException()
    {
        Assert.DoesNotThrow(() => speed.SetSpeed(2f));
        Assert.AreEqual(-1f, speed.HighlightedSpeed, "Buttons are built in Start (Play only)");
    }

    // --- The pause toggle (#124) ---

    [Test]
    public void TogglePause_FromNormal_PausesAndReturnsToNormal()
    {
        speed.TogglePause();
        Assert.IsTrue(speed.IsPaused);
        Assert.AreEqual(0f, Time.timeScale);

        speed.TogglePause();
        Assert.AreEqual(1f, speed.Speed);
        Assert.AreEqual(1f, Time.timeScale);
    }

    [Test]
    public void TogglePause_FromFast_ReturnsToFast()
    {
        speed.SetSpeed(2f);
        speed.TogglePause();
        Assert.IsTrue(speed.IsPaused);
        Assert.AreEqual(2f, speed.SpeedBeforePause);

        speed.TogglePause();
        Assert.AreEqual(2f, speed.Speed, "Not 1×: the speed held before pausing");
        Assert.AreEqual(2f, Time.timeScale);
    }

    [Test]
    public void ThePauseButtonPath_IsRememberedTheSameWay()
    {
        speed.SetSpeed(2f);
        speed.SetSpeed(0f); // The Pause button
        speed.SetSpeed(0f); // Pressed again while paused: still remembers 2×, not "paused"
        Assert.AreEqual(2f, speed.SpeedBeforePause);

        speed.TogglePause();
        Assert.AreEqual(2f, speed.Speed);

        speed.SetSpeed(0f);
        speed.SetSpeed(1f); // Left pause by a button
        speed.TogglePause();
        speed.TogglePause();
        Assert.AreEqual(1f, speed.Speed, "The toggle follows the latest speed");
    }

    [Test]
    public void TogglePause_IgnoresUnsupportedSpeeds_AndAFreshRunForgetsTheOldSpeed()
    {
        speed.SetSpeed(2f);
        speed.SetSpeed(3f);
        speed.SetSpeed(0.5f);
        speed.TogglePause();
        speed.TogglePause();
        Assert.AreEqual(2f, speed.Speed);

        speed.TogglePause(); // Paused from 2×
        speed.ResetToNormalSpeed(); // Play Again
        Assert.AreEqual(1f, speed.Speed);
        speed.TogglePause();
        speed.TogglePause();
        Assert.AreEqual(1f, speed.Speed, "The last run's 2× is not carried over");
    }

    // --- The buttons (#124: in the top bar's strip) ---

    [Test]
    public void TheButtons_SitTopRight_OneOrderAboveTheBar_AndOnlyTheButtonsBlockThePointer()
    {
        speed.CreateButtons();
        GameObject canvasGo = GameObject.Find("GameSpeedCanvas");
        Assert.IsNotNull(canvasGo);
        Assert.AreEqual(1, canvasGo.GetComponent<Canvas>().sortingOrder, "TopBar's canvas is 0");
        Assert.AreEqual(1, speed.SortingOrder);

        var panel = (RectTransform)canvasGo.transform.Find("SpeedControls");
        Assert.AreEqual(new Vector2(1f, 1f), panel.anchorMin);
        Assert.AreEqual(new Vector2(1f, 1f), panel.anchorMax);
        Assert.AreEqual(new Vector2(1f, 1f), panel.pivot);
        Assert.AreEqual(new Vector2(-speed.BarInset, 0f), panel.anchoredPosition);
        Assert.AreEqual(new Vector2(speed.PanelWidth, speed.StripHeight), panel.sizeDelta);
        Assert.IsFalse(panel.GetComponent<UnityEngine.UI.Image>().raycastTarget, "A press on the panel's background reaches the board");

        int blockers = 0;
        foreach (UnityEngine.UI.Graphic g in canvasGo.GetComponentsInChildren<UnityEngine.UI.Graphic>())
            if (g.raycastTarget) { blockers++; Assert.IsNotNull(g.GetComponent<UnityEngine.UI.Button>(), g.name + " blocks the pointer"); }
        Assert.AreEqual(3, blockers, "Pause, 1× and 2×, and nothing else");

        Assert.AreEqual(1f, speed.HighlightedSpeed);
        speed.SetSpeed(2f);
        Assert.AreEqual(2f, speed.HighlightedSpeed);
        speed.TogglePause();
        Assert.AreEqual(0f, speed.HighlightedSpeed, "The tint follows the hotkey path too");
    }

    // --- #83: a recompile under no-domain-reload hands back an empty button array ---

    [Test]
    public void AnEmptyButtonArray_IsTreatedAsNoButtons_NoException()
    {
        // Staged by reflection: the empty array is what Unity's serialization leaves after a script recompile.
        typeof(GameSpeed).GetField("buttonImages", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .SetValue(speed, new UnityEngine.UI.Image[0]);

        Assert.DoesNotThrow(() => speed.ResetToNormalSpeed(), "The path Awake runs, where #83 crashed");
        Assert.DoesNotThrow(() => speed.SetSpeed(2f));
        Assert.DoesNotThrow(() => speed.TogglePause());
        Assert.AreEqual(0f, speed.Speed, "The mechanism still works");
        Assert.AreEqual(-1f, speed.HighlightedSpeed);
    }
}
