using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

public class HeroAITests
{
    private static readonly Vector2Int HeartTile = new Vector2Int(24, 14);
    private static readonly Vector2Int PlaceFrom = new Vector2Int(21, 13);
    private const float Dt = 0.02f;
    private const float Frozen = float.PositiveInfinity;

    private GameObject managerGo;
    private DungeonBoard board;
    private BoardRenderer boardRenderer;
    private PlacementManager manager;
    private GameObject heartGo;
    private Heart heart;
    private GameObject heroGo;
    private Hero hero;
    private HeroAI ai;
    private readonly List<GameObject> extras = new List<GameObject>();
    private readonly List<Goblin> goblins = new List<Goblin>();      // Stepped after the hero (body Tick only)
    private readonly List<GoblinAI> goblinAis = new List<GoblinAI>();

    [SetUp]
    public void SetUp()
    {
        // Default board: 48x32 Rock with the 6x6 Floor cavern at (21,13)-(26,18). Heart at (24,14).
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
    }

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject go in extras) if (go != null) Object.DestroyImmediate(go);
        extras.Clear();
        goblins.Clear();
        goblinAis.Clear();
        if (heroGo != null) Object.DestroyImmediate(heroGo);
        if (heartGo != null) Object.DestroyImmediate(heartGo);
        if (managerGo != null) Object.DestroyImmediate(managerGo);
    }

    private void AddHero(int x, int y, Vector2Int? entrance = null)
    {
        heroGo = new GameObject("TestHero");
        var health = heroGo.AddComponent<Health>();
        health.MaxHealth = 30;
        health.Team = HealthTeam.Hero;
        hero = heroGo.AddComponent<Hero>();
        hero.Board = board;
        hero.Renderer = boardRenderer;
        hero.Health = health;
        hero.MoveSpeed = 3f;
        ai = heroGo.AddComponent<HeroAI>();
        ai.Hero = hero;
        ai.Heart = heart;
        ai.PlacementManager = manager;
        ai.AttackDamage = 2;
        ai.AttackIntervalSeconds = 1f;
        ai.EntranceTile = entrance ?? new Vector2Int(24, 19);
        hero.PlaceOnTile(new Vector2Int(x, y));
    }

    private Goblin AddGoblin(int x, int y, bool withAI = false)
    {
        var go = new GameObject("TestGoblin" + extras.Count);
        extras.Add(go);
        var goblin = go.AddComponent<Goblin>();
        goblin.Board = board;
        goblin.Renderer = boardRenderer;
        goblin.LogStats = false;
        goblin.HungerSecondsToEmpty = Frozen;
        goblin.EnergySecondsToEmpty = Frozen;
        goblin.PlaceOnTile(new Vector2Int(x, y));
        var health = go.AddComponent<Health>();
        health.MaxHealth = 20;
        health.Team = HealthTeam.Monster;
        goblin.Health = health;
        goblins.Add(goblin);

        if (withAI)
        {
            var gai = go.AddComponent<GoblinAI>();
            gai.Goblin = goblin;
            gai.PlacementManager = manager;
            gai.WanderPauseSeconds = Frozen;
            goblinAis.Add(gai);
        }
        return goblin;
    }

    private void Step()
    {
        if (heroGo != null)
        {
            hero.Tick(Dt);
            ai.Tick(Dt);
        }
        foreach (Goblin g in goblins)
        {
            g.Tick(Dt);
            GoblinAI gai = g.GetComponent<GoblinAI>();
            if (gai != null) gai.Tick(Dt);
        }
    }

    private void StepFor(float seconds, Action afterEachStep = null)
    {
        int steps = Mathf.RoundToInt(seconds / Dt);
        for (int i = 0; i < steps; i++)
        {
            Step();
            afterEachStep?.Invoke();
        }
    }

    private float StepUntil(Func<bool> condition, float maxSeconds, string because, Action afterEachStep = null)
    {
        float elapsed = 0f;
        while (!condition())
        {
            Assert.Less(elapsed, maxSeconds, "Timed out waiting: " + because);
            Step();
            afterEachStep?.Invoke();
            elapsed += Dt;
        }
        return elapsed;
    }

    private static int Manhattan(Vector2Int a, Vector2Int b) => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);

    private int HeartHp => heart.Health.CurrentHealth;

    // --- Delve ---

    [Test]
    public void Delve_PathsBesideHeart_AttacksOnInterval()
    {
        AddHero(24, 17); // Manhattan 3 from the Heart

        Action neverOnHeart = () => Assert.AreNotEqual(HeartTile, hero.CurrentTile, "Never stands on the Heart");
        StepUntil(() => HeartHp < 100, 3f, "hero to reach the Heart and strike", neverOnHeart);
        Assert.AreEqual(98, HeartHp, "First hit lands on arrival");
        Assert.AreEqual(1, Manhattan(hero.CurrentTile, HeartTile), "Beside the Heart");
        Assert.AreEqual(HeroAI.Goal.Delve, ai.CurrentGoal);

        float t = StepUntil(() => HeartHp < 98, 1.2f, "second hit", neverOnHeart);
        Assert.AreEqual(96, HeartHp);
        Assert.AreEqual(1f, t, 0.05f, "One attack interval apart");
    }

    [Test]
    public void NoPath_WaitsInPlace_ThenProceedsWhenOpened()
    {
        foreach (Vector2Int n in new[] { new Vector2Int(25, 14), new Vector2Int(23, 14), new Vector2Int(24, 15), new Vector2Int(24, 13) })
            board.SetTile(n.x, n.y, TileState.Rock);
        AddHero(21, 18);
        Vector3 start = heroGo.transform.position;

        Assert.DoesNotThrow(() => StepFor(3f));
        Assert.AreEqual(new Vector2Int(21, 18), hero.CurrentTile);
        Assert.AreEqual(start, heroGo.transform.position, "Waits; never teleports");
        Assert.IsFalse(hero.IsMoving);
        Assert.AreEqual(100, HeartHp);

        board.SetTile(24, 15, TileState.Floor); // a route opens
        StepUntil(() => HeartHp < 100, 6f, "hero to take the new route and strike");
        Assert.AreEqual(new Vector2Int(24, 15), hero.CurrentTile);
    }

    [Test]
    public void NoHeartWired_IdlesWithoutException()
    {
        AddHero(24, 17);
        ai.Heart = null;

        Assert.DoesNotThrow(() => StepFor(2f));
        Assert.AreEqual(new Vector2Int(24, 17), hero.CurrentTile);
    }

    [Test]
    public void HeartDestroyed_Done()
    {
        AddHero(24, 15); // already beside it
        heart.Health.TakeDamage(99);

        StepUntil(() => heart.IsDestroyed, 2f, "the finishing blow");
        Step();
        Assert.AreEqual(HeroAI.Goal.Done, ai.CurrentGoal);
        StepFor(2f);
        Assert.AreEqual(0, HeartHp);
    }

    [Test]
    public void GameOver_HeroStops()
    {
        AddHero(24, 18);
        var gmGo = new GameObject("TestGameManager");
        extras.Add(gmGo);
        var gm = gmGo.AddComponent<GameManager>();
        gm.Heart = heart;
        ai.GameManager = gm;

        heart.Health.TakeDamage(100); // destroyed by something else
        gm.Tick(Dt);
        Assert.AreEqual(GameManager.GameState.GameOver, gm.State);

        StepFor(2f);
        Assert.AreEqual(HeroAI.Goal.Done, ai.CurrentGoal);
        Assert.AreEqual(new Vector2Int(24, 18), hero.CurrentTile, "No orders once the game is over");
    }

    // --- Traps ---

    [Test]
    public void ArmedTrapOnPath_WoundsOnce_LeavesItSpent_CrossingBackDoesNothing()
    {
        manager.SpikeDamage = 3;
        Assert.IsTrue(manager.TryPlace(PlaceableType.SpikeTrap, new Vector2Int(24, 16), PlaceFrom, out Placeable p));
        SpikeTrap trap = p.GetComponent<SpikeTrap>();
        AddHero(24, 18, entrance: new Vector2Int(24, 19)); // his route down the x=24 column crosses (24,16)
        Health health = hero.Health;

        StepUntil(() => !trap.IsArmed, 3f, "hero to step on the trap");
        Assert.AreEqual(27, health.CurrentHealth, "Exactly the trap's damage");
        StepUntil(() => HeartHp < 100, 3f, "hero to carry on to the Heart");
        Assert.AreEqual(27, health.CurrentHealth, "Fired once");

        health.TakeDamage(18); // 9/30: flee back up the column over the spent trap
        StepUntil(() => ai.HasEscaped, 5f, "hero to flee back past the trap");
        Assert.AreEqual(9, health.CurrentHealth, "A spent trap doesn't fire again");
        Assert.IsFalse(trap.IsArmed);
    }

    // --- Blockers ---

    [Test]
    public void GoblinOnPath_HeroFightsIt_ThenResumesDelve()
    {
        AddHero(24, 18);
        Goblin goblin = AddGoblin(24, 16); // standing on his route down the column (body only, no AI)
        Health gh = goblin.Health;

        StepUntil(() => ai.CurrentGoal == HeroAI.Goal.Fight, 0.5f, "hero to engage the blocker");
        Assert.AreSame(gh, ai.CurrentTarget);

        StepUntil(() => gh.CurrentHealth == 18, 3f, "first hit");
        float t = StepUntil(() => gh.CurrentHealth == 16, 1.2f, "second hit");
        Assert.AreEqual(1f, t, 0.05f, "2 damage per attack interval");
        Assert.AreEqual(100, HeartHp, "Busy fighting, not hitting the Heart");

        StepUntil(() => goblin.IsDead, 10f, "the goblin to die");
        Assert.IsFalse(goblin.gameObject.activeSelf);
        StepUntil(() => ai.CurrentGoal == HeroAI.Goal.Delve, 0.5f, "hero to resume delving");
        StepUntil(() => HeartHp < 100, 3f, "hero to attack the Heart");
    }

    [Test]
    public void GoblinOffPath_Ignored_HeartAttackedOnSchedule()
    {
        AddHero(24, 17);
        Goblin goblin = AddGoblin(21, 17); // 3 tiles away, not on his route; its AI isn't stepped

        StepUntil(() => HeartHp < 100, 3f, "hero to reach the Heart",
            () => Assert.AreNotEqual(HeroAI.Goal.Fight, ai.CurrentGoal, "Doesn't chase"));
        StepFor(2.05f, () => Assert.AreNotEqual(HeroAI.Goal.Fight, ai.CurrentGoal));
        Assert.AreEqual(94, HeartHp, "Three hits: arrival, +1 s, +2 s");
        Assert.AreEqual(20, goblin.Health.CurrentHealth);
    }

    [Test]
    public void FindBlocker_AdjacentOrOnRoute_Only()
    {
        AddHero(24, 18);
        Health onRoute = AddGoblin(24, 15).Health;
        Health adjacent = AddGoblin(25, 18).Health;
        Health elsewhere = AddGoblin(21, 13).Health;
        var route = new List<Vector2Int> { new Vector2Int(24, 18), new Vector2Int(24, 17), new Vector2Int(24, 16), new Vector2Int(24, 15) };

        Assert.AreSame(adjacent, HeroAI.FindBlocker(hero.CurrentTile, route, new[] { elsewhere, onRoute, adjacent }), "Nearest blocker first");
        Assert.AreSame(onRoute, HeroAI.FindBlocker(hero.CurrentTile, route, new[] { elsewhere, onRoute }));
        Assert.IsNull(HeroAI.FindBlocker(hero.CurrentTile, route, new[] { elsewhere }));

        onRoute.TakeDamage(100);
        Assert.IsNull(HeroAI.FindBlocker(hero.CurrentTile, route, new[] { onRoute }), "Dead minions don't block");
        Assert.IsNull(HeroAI.FindBlocker(hero.CurrentTile, route, new[] { hero.Health }), "Heroes aren't minions");
    }

    // --- Fleeing ---

    [Test]
    public void LowHealth_Flees_EscapesAtEntrance()
    {
        AddHero(24, 18, entrance: new Vector2Int(24, 19));
        StepFor(0.3f); // under way toward the Heart
        Assert.AreEqual(HeroAI.Goal.Delve, ai.CurrentGoal);

        hero.Health.TakeDamage(21); // 9/30 = 30%
        Step();
        Assert.AreEqual(HeroAI.Goal.Flee, ai.CurrentGoal);

        StepUntil(() => ai.HasEscaped, 5f, "hero to reach the entrance");
        Assert.AreEqual(HeroAI.Goal.Done, ai.CurrentGoal);
        Assert.AreEqual(1, Manhattan(hero.CurrentTile, new Vector2Int(24, 19)), "Left from beside the entrance");
        Assert.IsFalse(heroGo.activeSelf);
        Assert.AreEqual(100, HeartHp);
    }

    [Test]
    public void AboveFleeThreshold_KeepsDelving()
    {
        AddHero(24, 17);
        hero.Health.TakeDamage(20); // 10/30 > 30%
        StepUntil(() => HeartHp < 100, 3f, "hero to keep delving");
        Assert.AreEqual(HeroAI.Goal.Delve, ai.CurrentGoal);
    }

    [Test]
    public void KilledHero_StaysDead_GoalDone()
    {
        AddHero(24, 17);
        hero.Health.TakeDamage(30);
        Step();

        Assert.IsTrue(hero.IsDead);
        Assert.IsFalse(heroGo.activeSelf);
        Assert.AreEqual(HeroAI.Goal.Done, ai.CurrentGoal);
        Assert.IsFalse(ai.HasEscaped);
    }

    // --- Goblins target hero bodies (generalized #47 targeting) ---

    [Test]
    public void GoblinTrySelectTarget_FindsHeroBody()
    {
        AddHero(25, 16);
        Assert.IsTrue(GoblinAI.TryGetBodyTile(hero.Health, out Vector2Int tile));
        Assert.AreEqual(new Vector2Int(25, 16), tile);

        Health picked = GoblinAI.TrySelectTarget(board, new Vector2Int(23, 16), HealthTeam.Monster, new[] { hero.Health }, 4);
        Assert.AreSame(hero.Health, picked);
    }

    [Test]
    public void SteppedGoblinAI_CommitsFightAgainstHero()
    {
        AddHero(25, 16);
        ai.Heart = null; // keep the hero standing still for this test
        Goblin goblin = AddGoblin(23, 16, withAI: true);
        GoblinAI gai = goblin.GetComponent<GoblinAI>();

        StepUntil(() => gai.CurrentGoal == GoblinAI.Goal.Fight, 0.3f, "goblin to engage the hero");
        Assert.AreSame(hero.Health, gai.CurrentTarget);
        StepUntil(() => hero.Health.CurrentHealth < 30, 3f, "goblin to land a hit");
    }
}
