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
}
