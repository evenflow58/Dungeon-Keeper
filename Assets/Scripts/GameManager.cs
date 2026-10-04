using UnityEngine;

/// <summary>
/// Win/loss state for the slice (DESIGN §A.6). Watches the Heart: once it's destroyed the game is over.
/// State and a log line only — the victory/defeat screens are Epic #6 (§A.10 step 6).
/// </summary>
[DisallowMultipleComponent]
public class GameManager : MonoBehaviour
{
    public enum GameState { Playing, GameOver, Victory } // Victory: set by the hero-escalation story

    public const string GameOverLine = "The dark goes quiet."; // DESIGN §A.6

    [SerializeField] private Heart heart;

    public Heart Heart { get => heart; set => heart = value; }

    public GameState State { get; private set; } = GameState.Playing;

    private void Start()
    {
        heart ??= FindAnyObjectByType<Heart>();
    }

    private void Update()
    {
        Tick(Time.deltaTime);
    }

    /// <summary>
    /// Checks for the end of the game. Called from Update; public so tests can step it. With no Heart wired
    /// the game simply stays Playing.
    /// </summary>
    public void Tick(float deltaTime)
    {
        if (State != GameState.Playing) return; // Transitions happen once

        if (heart != null && heart.IsDestroyed)
        {
            State = GameState.GameOver;
            Debug.Log(GameOverLine, this);
        }
    }
}
