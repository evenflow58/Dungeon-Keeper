using NUnit.Framework;
using UnityEngine;

public class EscalationTests
{
    private static readonly Vector2Int HeartTile = new Vector2Int(24, 14);
    private const float Dt = 0.02f;
    private const float Delay = 10f; // Staged nextHeroDelaySeconds (escaped: ×0.5 = 5 s)

    private GameObject managerGo;
    private DungeonBoard board;
    private BoardRenderer boardRenderer;
    private PlacementManager manager;
    private GameObject heartGo;
    private Heart heart;
    private GameObject spawnerGo;
    private HeroSpawner spawner;
    private GameManager game;
    private int victoryLogs, gameOverLogs;

    [SetUp]
    public void SetUp()
    {
        managerGo = new GameObject("TestDungeonManager");
        board = managerGo.AddComponent<DungeonBoard>();
        board.InitializeBoard();
        boardRenderer = managerGo.AddComponent<BoardRenderer>();
        boardRenderer.Board = board;
        manager = managerGo.AddComponent<PlacementManager>();
        manager.Board = board;
        manager.Renderer = boardRenderer;

        heartGo = new GameObject("TestHeart");
        heart = heartGo.AddComponent<Heart>(); // Health 100 / Monster via Reset
        heart.Board = board;
        heart.Renderer = boardRenderer;
        heart.Health = heartGo.GetComponent<Health>();
        heart.PlaceOnTile(HeartTile);

        spawnerGo = new GameObject("TestDirector");
        spawner = spawnerGo.AddComponent<HeroSpawner>();
        spawner.PlacementManager = manager;
        spawner.Board = board;
        spawner.Renderer = boardRenderer;
        spawner.Heart = heart;
        spawner.NextHeroDelaySeconds = Delay;
        game = spawnerGo.AddComponent<GameManager>();
        game.Heart = heart;
        game.HeroSpawner = spawner;
        spawner.GameManager = game;

        DigColumnToDoor();

        victoryLogs = 0;
        gameOverLogs = 0;
        Application.logMessageReceived += CountEndLines;
    }

    [TearDown]
    public void TearDown()
    {
        Application.logMessageReceived -= CountEndLines;
        foreach (Hero h in Object.FindObjectsByType<Hero>(FindObjectsInactive.Include)) Object.DestroyImmediate(h.gameObject);
        if (spawnerGo != null) Object.DestroyImmediate(spawnerGo);
        if (heartGo != null) Object.DestroyImmediate(heartGo);
        if (managerGo != null) Object.DestroyImmediate(managerGo);
    }

    private void CountEndLines(string message, string stackTrace, LogType type)
    {
        if (message == GameManager.VictoryLine) victoryLogs++;
        if (message == GameManager.GameOverLine) gameOverLogs++;
    }

    private void DigColumnToDoor()
    {
        for (int y = 19; y <= 30; y++) board.SetTile(24, y, TileState.Floor);
    }

    /// <summary>Advances the director (and the manager) by seconds in 0.5 s steps.</summary>
    private void Advance(float seconds)
    {
        for (float t = 0f; t < seconds - 1e-4f; t += 0.5f)
        {
            spawner.Tick(Mathf.Min(0.5f, seconds - t));
            game.Tick(Dt);
        }
    }

    private Hero SpawnFirstHero()
    {
        spawner.Tick(300f); // the time trigger (route already dug)
        Assert.AreEqual(1, spawner.HeroesSpawned);
        return spawner.ActiveHero;
    }

    private void Kill(Hero hero)
    {
        hero.Health.TakeDamage(10000);
        hero.Tick(Dt);   // the body dies
        spawner.Tick(Dt); // the director sees it
        Assert.IsTrue(hero.IsDead);
    }

    /// <summary>He's standing on the door: below the flee fraction he leaves at once.</summary>
    private void Escape(Hero hero)
    {
        hero.Health.TakeDamage(hero.Health.MaxHealth - 1);
        hero.Tick(Dt);
        hero.GetComponent<HeroAI>().Tick(Dt);
        spawner.Tick(Dt);
        Assert.IsTrue(hero.GetComponent<HeroAI>().HasEscaped);
    }

    private static int Hp(Hero h) => h.Health.MaxHealth;
    private static int Dmg(Hero h) => h.GetComponent<HeroAI>().AttackDamage;

    // --- The escalation table ---

    [Test]
    public void Defaults_AreTheProvisionalLadder()
    {
        var fresh = new GameObject("Fresh").AddComponent<HeroSpawner>();
        try
        {
            Assert.AreEqual(3, fresh.TotalHeroes);
            Assert.AreEqual(120f, fresh.NextHeroDelaySeconds);
            Assert.AreEqual(0.5f, fresh.EscapedDelayMultiplier);
            Assert.AreEqual(new[] { 30, 45, 60 }, new[] { fresh.MaxHealthForWave(1, false), fresh.MaxHealthForWave(2, false), fresh.MaxHealthForWave(3, false) });
            Assert.AreEqual(new[] { 2, 3, 4 }, new[] { fresh.AttackDamageForWave(1, false), fresh.AttackDamageForWave(2, false), fresh.AttackDamageForWave(3, false) });
            Assert.AreEqual(55, fresh.MaxHealthForWave(2, true));
            Assert.AreEqual(4, fresh.AttackDamageForWave(2, true));
            Assert.AreEqual(70, fresh.MaxHealthForWave(3, true));
            Assert.AreEqual(5, fresh.AttackDamageForWave(3, true));
        }
        finally
        {
            Object.DestroyImmediate(fresh.gameObject);
        }
    }

    [Test]
    public void Wave2_ArrivesOnlyAfterTheDelay_WithEscalatedStats()
    {
        Hero h1 = SpawnFirstHero();
        Assert.AreEqual(30, Hp(h1));
        Assert.AreEqual(2, Dmg(h1));
        Kill(h1);
        Assert.AreEqual(HeroSpawner.HeroOutcome.Killed, spawner.Outcome);
        Assert.IsFalse(spawner.NextHeroHasEscapeBonus, "Killed: no bonus");

        Advance(Delay - 0.5f);
        Assert.AreEqual(1, spawner.HeroesSpawned, "Not before the delay");
        Advance(1f);
        Assert.AreEqual(2, spawner.HeroesSpawned);

        Hero h2 = spawner.ActiveHero;
        Assert.AreNotSame(h1, h2);
        Assert.AreEqual(45, Hp(h2));
        Assert.AreEqual(45, h2.Health.CurrentHealth);
        Assert.AreEqual(3, Dmg(h2));
        Assert.AreEqual(3f, h2.MoveSpeed, "Tempo stays at base");
        Assert.AreEqual(1f, h2.GetComponent<HeroAI>().AttackIntervalSeconds);
        Assert.AreEqual(0.3f, h2.GetComponent<HeroAI>().FleeHealthFraction);
        Assert.AreEqual(board.EntranceTile, h2.CurrentTile, "Arrives at the door");
    }

    [Test]
    public void Wave3_Stats_ThenNoFourthHero()
    {
        Kill(SpawnFirstHero());
        Advance(Delay + 0.5f);
        Kill(spawner.ActiveHero);
        Advance(Delay + 0.5f);

        Hero h3 = spawner.ActiveHero;
        Assert.AreEqual(3, spawner.HeroesSpawned);
        Assert.AreEqual(60, Hp(h3));
        Assert.AreEqual(4, Dmg(h3));

        Kill(h3);
        Assert.AreEqual(3, spawner.HeroesResolved);
        Advance(1000f);
        Assert.AreEqual(3, spawner.HeroesSpawned, "The director is done after the third");
        Assert.AreEqual(3, Object.FindObjectsByType<Hero>(FindObjectsInactive.Include).Length);
    }

    // --- Reporting back ---

    [Test]
    public void Escape_NextComesSooner_AndTougher()
    {
        Escape(SpawnFirstHero());
        Assert.AreEqual(HeroSpawner.HeroOutcome.Escaped, spawner.Outcome);
        Assert.IsTrue(spawner.NextHeroScheduled);
        Assert.AreEqual(Delay * 0.5f, spawner.SecondsUntilNextHero, 0.05f, "Half the delay");
        Assert.IsTrue(spawner.NextHeroHasEscapeBonus);

        Advance(Delay * 0.5f - 0.5f);
        Assert.AreEqual(1, spawner.HeroesSpawned, "Not before the shortened delay");
        Advance(1f);
        Assert.AreEqual(2, spawner.HeroesSpawned, "Well before the normal delay");

        Hero h2 = spawner.ActiveHero;
        Assert.AreEqual(55, Hp(h2), "Wave 2 (45) + escape bonus (10)");
        Assert.AreEqual(4, Dmg(h2), "Wave 2 (3) + escape bonus (1)");
    }

    [Test]
    public void TwoEscapes_BonusDoesNotStack()
    {
        Escape(SpawnFirstHero());
        Advance(Delay);
        Escape(spawner.ActiveHero);
        Advance(Delay);

        Hero h3 = spawner.ActiveHero;
        Assert.AreEqual(3, spawner.HeroesSpawned);
        Assert.AreEqual(70, Hp(h3), "Wave 3 (60) + one bonus, not two");
        Assert.AreEqual(5, Dmg(h3));
    }

    [Test]
    public void EscapeThenKill_BonusOnlyForTheNextHero()
    {
        Escape(SpawnFirstHero());
        Advance(Delay);
        Kill(spawner.ActiveHero);
        Assert.IsFalse(spawner.NextHeroHasEscapeBonus);
        Assert.AreEqual(Delay, spawner.SecondsUntilNextHero, 0.05f, "Killed: the full delay");
        Advance(Delay + 0.5f);

        Hero h3 = spawner.ActiveHero;
        Assert.AreEqual(60, Hp(h3), "Plain wave 3");
        Assert.AreEqual(4, Dmg(h3));
    }

    // --- Gating, countdown data, game end ---

    [Test]
    public void LaterWaves_StayRouteGated()
    {
        Kill(SpawnFirstHero());

        // Move the door; regenerating the board wipes the dug route.
        board.EntranceTile = new Vector2Int(0, 16);
        board.InitializeBoard();
        spawner.EntranceTile = new Vector2Int(0, 16);
        Assert.IsFalse(spawner.RouteExists());

        Advance(Delay + 5f);
        Assert.AreEqual(1, spawner.HeroesSpawned, "Countdown over, but sealed: no hero");
        Assert.AreEqual(0f, spawner.SecondsUntilNextHero);
        Assert.IsTrue(spawner.NextHeroScheduled, "Still waiting to send him");

        for (int x = 1; x <= 20; x++) board.SetTile(x, 16, TileState.Floor); // route from the new door
        spawner.Tick(Dt);
        Assert.AreEqual(2, spawner.HeroesSpawned);
        Assert.AreEqual(new Vector2Int(0, 16), spawner.ActiveHero.CurrentTile);
    }

    [Test]
    public void Countdown_IsExposedForTheTopBar()
    {
        Assert.IsFalse(spawner.NextHeroScheduled);
        Assert.AreEqual(0f, spawner.SecondsUntilNextHero);

        Hero h1 = SpawnFirstHero();
        Assert.IsFalse(spawner.NextHeroScheduled, "A hero is active");
        Assert.AreEqual(0f, spawner.SecondsUntilNextHero);

        Kill(h1);
        Assert.IsTrue(spawner.NextHeroScheduled);
        Assert.AreEqual(Delay, spawner.SecondsUntilNextHero, 0.05f);
        Advance(3f);
        Assert.AreEqual(Delay - 3f, spawner.SecondsUntilNextHero, 0.05f, "Counts down with Tick");

        Advance(Delay);
        Assert.IsFalse(spawner.NextHeroScheduled, "Hero 2 is active");
        Kill(spawner.ActiveHero);
        Advance(Delay + 0.5f);
        Kill(spawner.ActiveHero);
        Assert.AreEqual(3, spawner.HeroesResolved);
        Assert.IsFalse(spawner.NextHeroScheduled, "No hero after the last");
        Assert.AreEqual(0f, spawner.SecondsUntilNextHero);
    }

    [Test]
    public void GameOver_DirectorGoesQuiet()
    {
        Kill(SpawnFirstHero());
        Advance(2f);
        float left = spawner.SecondsUntilNextHero;

        heart.Health.TakeDamage(100);
        game.Tick(Dt);
        Assert.AreEqual(GameManager.GameState.GameOver, game.State);

        Advance(1000f);
        Assert.AreEqual(1, spawner.HeroesSpawned, "No heroes into a finished game");
        Assert.AreEqual(left, spawner.SecondsUntilNextHero, 1e-4f, "Countdown frozen");
    }

    // --- Victory ---

    [Test]
    public void ThirdResolution_Victory_ExactlyOnce()
    {
        spawner.NextHeroDelaySeconds = 0.1f;
        Kill(SpawnFirstHero());
        game.Tick(Dt);
        Assert.AreEqual(GameManager.GameState.Playing, game.State, "1 of 3");

        Advance(0.5f);
        Escape(spawner.ActiveHero); // escapes count as dealt with
        game.Tick(Dt);
        Assert.AreEqual(GameManager.GameState.Playing, game.State, "2 of 3");

        Advance(0.5f);
        Kill(spawner.ActiveHero);
        game.Tick(Dt);
        Assert.AreEqual(GameManager.GameState.Victory, game.State);
        Assert.AreEqual(1, victoryLogs, "\"The dark endures.\" once");

        Advance(100f);
        for (int i = 0; i < 20; i++) game.Tick(Dt);
        Assert.AreEqual(GameManager.GameState.Victory, game.State);
        Assert.AreEqual(1, victoryLogs);
        Assert.AreEqual(0, gameOverLogs);
    }

    [Test]
    public void HeartDeathInTheSameTick_WinsOverVictory()
    {
        spawner.NextHeroDelaySeconds = 0.1f;
        Kill(SpawnFirstHero());
        Advance(0.5f);
        Kill(spawner.ActiveHero);
        Advance(0.5f);

        Kill(spawner.ActiveHero);       // the third resolution…
        heart.Health.TakeDamage(100);   // …and the Heart falls before the manager's next Tick
        Assert.AreEqual(3, spawner.HeroesResolved);
        game.Tick(Dt);

        Assert.AreEqual(GameManager.GameState.GameOver, game.State);
        Assert.AreEqual(1, gameOverLogs);
        Assert.AreEqual(0, victoryLogs);
    }

    [Test]
    public void VictoryLine_IsTheCoinedText()
    {
        Assert.AreEqual("The dark endures.", GameManager.VictoryLine);
    }
}
