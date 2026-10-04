using NUnit.Framework;
using UnityEngine;

public class GameManagerTests
{
    private GameObject heartGo;
    private GameObject managerGo;
    private Heart heart;
    private Health health;
    private GameManager game;
    private int gameOverLogs;

    [SetUp]
    public void SetUp()
    {
        heartGo = new GameObject("TestHeart");
        heart = heartGo.AddComponent<Heart>();
        health = heartGo.GetComponent<Health>();
        heart.Health = health;

        managerGo = new GameObject("TestGameManager");
        game = managerGo.AddComponent<GameManager>();
        game.Heart = heart;

        gameOverLogs = 0;
        Application.logMessageReceived += CountGameOverLogs;
    }

    [TearDown]
    public void TearDown()
    {
        Application.logMessageReceived -= CountGameOverLogs;
        if (managerGo != null) Object.DestroyImmediate(managerGo);
        if (heartGo != null) Object.DestroyImmediate(heartGo);
    }

    private void CountGameOverLogs(string message, string stackTrace, LogType type)
    {
        if (message == GameManager.GameOverLine) gameOverLogs++;
    }

    [Test]
    public void StartsPlaying()
    {
        Assert.AreEqual(GameManager.GameState.Playing, game.State);
    }

    [Test]
    public void HeartAlive_StaysPlaying()
    {
        health.TakeDamage(99);
        for (int i = 0; i < 100; i++) game.Tick(0.02f);

        Assert.AreEqual(GameManager.GameState.Playing, game.State);
        Assert.AreEqual(0, gameOverLogs);
    }

    [Test]
    public void HeartDestroyed_GameOverOnNextTick_ExactlyOnce()
    {
        game.Tick(0.02f);
        health.TakeDamage(100);
        Assert.AreEqual(GameManager.GameState.Playing, game.State, "Nothing changes until the next Tick");

        game.Tick(0.02f);
        Assert.AreEqual(GameManager.GameState.GameOver, game.State);
        Assert.AreEqual(1, gameOverLogs, "\"The dark goes quiet.\" logged once");

        for (int i = 0; i < 50; i++) game.Tick(0.02f);
        Assert.AreEqual(GameManager.GameState.GameOver, game.State);
        Assert.AreEqual(1, gameOverLogs, "Still exactly once");
    }

    [Test]
    public void GameOverLine_IsDesignText()
    {
        Assert.AreEqual("The dark goes quiet.", GameManager.GameOverLine);
    }

    [Test]
    public void Unwired_NoHeart_StaysPlaying_NoException()
    {
        game.Heart = null;
        Assert.DoesNotThrow(() => { for (int i = 0; i < 10; i++) game.Tick(0.02f); });
        Assert.AreEqual(GameManager.GameState.Playing, game.State);
    }

    [Test]
    public void HeartWithoutHealth_StaysPlaying()
    {
        heart.Health = null;
        health.TakeDamage(100); // the component is dead, but this Heart isn't wired to it
        game.Tick(0.02f);
        Assert.AreEqual(GameManager.GameState.Playing, game.State);
    }
}
