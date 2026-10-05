using UnityEngine;

/// <summary>
/// Win/loss state for the slice (DESIGN §A.6, §A.8). Heart destroyed → GameOver. All of the hero director's
/// heroes resolved (killed or escaped) with the Heart standing → Victory. Heart death wins a same-tick race.
/// State and log lines only — the victory/defeat screens are Epic #6 (§A.10 step 6).
/// </summary>
[DisallowMultipleComponent]
public class GameManager : MonoBehaviour
{
    public enum GameState { Playing, GameOver, Victory }

    public const string GameOverLine = "The dark goes quiet."; // DESIGN §A.6
    public const string VictoryLine = "The dark endures.";     // Coined: DESIGN supplies no victory line

    [SerializeField] private Heart heart;
    [SerializeField] private HeroSpawner heroSpawner; // Optional: no spawner, no Victory

    public Heart Heart { get => heart; set => heart = value; }
    public HeroSpawner HeroSpawner { get => heroSpawner; set => heroSpawner = value; }

    public GameState State { get; private set; } = GameState.Playing;

    private void Start()
    {
        heart ??= FindAnyObjectByType<Heart>();
        heroSpawner ??= FindAnyObjectByType<HeroSpawner>();
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
            return; // Checked first: Heart death wins a same-tick race with the last hero's resolution
        }

        if (heroSpawner != null && heroSpawner.TotalHeroes > 0 && heroSpawner.HeroesResolved >= heroSpawner.TotalHeroes)
        {
            State = GameState.Victory;
            Debug.Log(VictoryLine, this);
        }
    }
}
