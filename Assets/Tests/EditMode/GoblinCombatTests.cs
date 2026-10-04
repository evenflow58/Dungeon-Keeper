using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

public class GoblinCombatTests
{
    private static readonly Vector2Int PlaceFrom = new Vector2Int(24, 16);
    private const float Dt = 0.02f;
    private const float Frozen = float.PositiveInfinity; // Stops a clock exactly (needs decay, starvation, wander pauses)

    private GameObject managerGo;
    private DungeonBoard board;
    private BoardRenderer boardRenderer;
    private PlacementManager manager;
    private readonly List<GameObject> spawned = new List<GameObject>();
    private readonly List<Goblin> bodies = new List<Goblin>();   // Every goblin body, stepped in creation order
    private readonly List<GoblinAI> ais = new List<GoblinAI>();

    [SetUp]
    public void SetUp()
    {
        // Default board: 48x32 Rock with the 6x6 Floor cavern at (21,13)-(26,18).
        managerGo = new GameObject("TestDungeonManager");
        board = managerGo.AddComponent<DungeonBoard>();
        board.InitializeBoard();
        boardRenderer = managerGo.AddComponent<BoardRenderer>();
        boardRenderer.Board = board;
        manager = managerGo.AddComponent<PlacementManager>();
        manager.Board = board;
        manager.Renderer = boardRenderer;
        manager.MushroomGrowthSecondsPerFood = 1f;
    }

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject go in spawned) if (go != null) Object.DestroyImmediate(go);
        spawned.Clear();
        bodies.Clear();
        ais.Clear();
        if (managerGo != null) Object.DestroyImmediate(managerGo);
    }

    private Goblin AddBody(string name, int x, int y, HealthTeam team, int maxHealth)
    {
        var go = new GameObject(name);
        spawned.Add(go);
        var goblin = go.AddComponent<Goblin>();
        goblin.Board = board;
        goblin.Renderer = boardRenderer;
        goblin.MoveSpeed = 3f;
        goblin.LogStats = false;
        goblin.HungerSecondsToEmpty = Frozen;
        goblin.EnergySecondsToEmpty = Frozen;
        goblin.StarvationSecondsToDie = Frozen;
        goblin.PlaceOnTile(new Vector2Int(x, y));

        var health = go.AddComponent<Health>();
        health.Team = team;
        health.MaxHealth = maxHealth;
        goblin.Health = health;

        bodies.Add(goblin);
        return goblin;
    }

    /// <summary>A Monster-team goblin with an AI. Wander pauses are frozen so it stands still unless a goal moves it.</summary>
    private (Goblin goblin, GoblinAI ai) AddFighter(int x, int y, int maxHealth = 20)
    {
        Goblin goblin = AddBody("Fighter" + ais.Count, x, y, HealthTeam.Monster, maxHealth);
        var ai = goblin.gameObject.AddComponent<GoblinAI>();
        ai.Goblin = goblin;
        ai.PlacementManager = manager;
        ai.Rng = new System.Random(1);
        ai.WanderPauseSeconds = Frozen;
        ai.AttackIntervalSeconds = 1f;
        ai.AttackDamage = 1;
        ais.Add(ai);
        return (goblin, ai);
    }

    /// <summary>A goblin-bodied Hero-team dummy with no AI: it only stands there (the Play-mode stand-in too).</summary>
    private Health AddHostile(int x, int y, int maxHealth = 100) =>
        AddBody("Hostile" + bodies.Count, x, y, HealthTeam.Hero, maxHealth).Health;

    private void Step()
    {
        foreach (GoblinAI ai in ais)
        {
            ai.Goblin.Tick(Dt);
            ai.Tick(Dt);
        }
        foreach (Goblin body in bodies)
        {
            if (body.GetComponent<GoblinAI>() == null) body.Tick(Dt); // Dummies: their death sync
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

    private float StepUntil(Func<bool> condition, float maxSeconds, string because)
    {
        float elapsed = 0f;
        while (!condition())
        {
            Assert.Less(elapsed, maxSeconds, "Timed out waiting: " + because);
            Step();
            elapsed += Dt;
        }
        return elapsed;
    }

    private static int Manhattan(Vector2Int a, Vector2Int b) => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);

    private void Enclose(int x, int y)
    {
        board.SetTile(x + 1, y, TileState.Rock);
        board.SetTile(x - 1, y, TileState.Rock);
        board.SetTile(x, y + 1, TileState.Rock);
        board.SetTile(x, y - 1, TileState.Rock);
    }

    // --- Selection ---

    [Test]
    public void TrySelectTarget_NearestLivingOpponentInRange()
    {
        Health far = AddHostile(24, 13);   // distance 3 from (24,16)
        Health near = AddHostile(25, 16);  // distance 1
        Health dead = AddHostile(24, 17);  // distance 1 but dead
        dead.TakeDamage(1000);
        Health ally = AddBody("Ally", 23, 16, HealthTeam.Monster, 5).Health; // distance 1, same team

        Health picked = GoblinAI.TrySelectTarget(board, PlaceFrom, HealthTeam.Monster, new[] { far, dead, ally, near }, 4);
        Assert.AreSame(near, picked);
    }

    [Test]
    public void TrySelectTarget_Tie_KeepsCandidateOrder()
    {
        Health a = AddHostile(22, 16); // both distance 2
        Health b = AddHostile(26, 16);

        Assert.AreSame(b, GoblinAI.TrySelectTarget(board, PlaceFrom, HealthTeam.Monster, new[] { b, a }, 4));
        Assert.AreSame(a, GoblinAI.TrySelectTarget(board, PlaceFrom, HealthTeam.Monster, new[] { a, b }, 4));
    }

    [Test]
    public void TrySelectTarget_NoneInRange_ReturnsNull()
    {
        Health h = AddHostile(26, 13); // distance 5 from (21,13)
        Assert.IsNull(GoblinAI.TrySelectTarget(board, new Vector2Int(21, 13), HealthTeam.Monster, new[] { h }, 4));
    }

    // --- Engaging ---

    [Test]
    public void EngageInRange_PathsBeside_AttacksToDeath_ThenResumesNeeds()
    {
        var (attacker, ai) = AddFighter(21, 16);
        Health target = AddHostile(24, 16, maxHealth: 3); // distance 3
        Goblin targetBody = target.GetComponent<Goblin>();

        Step();
        Assert.AreEqual(GoblinAI.Goal.Fight, ai.CurrentGoal);
        Assert.AreSame(target, ai.CurrentTarget);

        int lastHp = target.CurrentHealth;
        StepUntil(() =>
        {
            Assert.AreNotEqual(targetBody.CurrentTile, attacker.CurrentTile, "Never stands on the target's tile");
            Assert.That(lastHp - target.CurrentHealth, Is.InRange(0, 1), "One attackDamage per attack");
            lastHp = target.CurrentHealth;
            return target.IsDead;
        }, 6f, "attacker to kill the target");

        Assert.AreEqual(1, Manhattan(attacker.CurrentTile, targetBody.CurrentTile), "Fought from beside it");
        Step(); // The dummy's own Tick syncs its death
        Assert.IsTrue(targetBody.IsDead);
        Assert.IsFalse(targetBody.gameObject.activeSelf);
        Assert.AreNotEqual(GoblinAI.Goal.Fight, ai.CurrentGoal);
        Assert.IsNull(ai.CurrentTarget);
        Assert.AreEqual(GoblinAI.Goal.Wander, ai.CurrentGoal, "Back to its needs AI");
    }

    [Test]
    public void RangeGate_DistanceFive_NeverEngaged()
    {
        var (_, ai) = AddFighter(21, 13);
        Health target = AddHostile(26, 13); // Manhattan 5

        StepFor(5f, () => Assert.AreNotEqual(GoblinAI.Goal.Fight, ai.CurrentGoal));
        Assert.AreEqual(100, target.CurrentHealth);
        Assert.AreEqual(GoblinAI.Goal.Wander, ai.CurrentGoal);
    }

    [Test]
    public void RangeGate_DistanceFour_Engaged()
    {
        var (_, ai) = AddFighter(21, 13);
        Health target = AddHostile(25, 13); // Manhattan 4

        Step();
        Assert.AreEqual(GoblinAI.Goal.Fight, ai.CurrentGoal);
        StepUntil(() => target.CurrentHealth < 100, 3f, "first attack to land");
    }

    [Test]
    public void UnreachableHostileInRange_Ignored_NeedsCarryOn()
    {
        var (attacker, ai) = AddFighter(21, 16);
        MushroomPlot plot = PlaceStockedPlot(21, 13, 2);
        Health target = AddHostile(24, 16); // distance 3
        Enclose(24, 16);
        attacker.Hunger = 20f;

        StepUntil(() => plot.FoodCount == 1, 6f, "attacker to go and eat instead");
        StepFor(2f, () => Assert.AreNotEqual(GoblinAI.Goal.Fight, ai.CurrentGoal));
        Assert.AreEqual(100, target.CurrentHealth);
    }

    private MushroomPlot PlaceStockedPlot(int x, int y, int food)
    {
        Assert.IsTrue(manager.TryPlace(PlaceableType.MushroomPlot, new Vector2Int(x, y), PlaceFrom, out Placeable p));
        var plot = p.GetComponent<MushroomPlot>();
        plot.Tick(food);
        return plot;
    }

    // --- Cadence ---

    [Test]
    public void Cadence_OneAttackPerInterval_FirstOnAdjacency()
    {
        AddFighter(23, 16);
        Health target = AddHostile(24, 16); // already adjacent

        StepFor(0.5f);
        Assert.AreEqual(99, target.CurrentHealth, "First attack lands immediately");
        StepFor(1f);   // t = 1.5
        Assert.AreEqual(98, target.CurrentHealth);
        StepFor(1f);   // t = 2.5
        Assert.AreEqual(97, target.CurrentHealth);
        StepFor(8f);   // t = 10.5: attacks at ~0, 1, …, 10
        Assert.AreEqual(89, target.CurrentHealth);
    }

    [Test]
    public void WeakenedAttackIntervalMultiplier_IsTunable()
    {
        var (attacker, ai) = AddFighter(23, 16);
        Assert.AreEqual(2f, ai.WeakenedAttackIntervalMultiplier, "DESIGN §A.5 default");
        ai.WeakenedAttackIntervalMultiplier = 3f;
        attacker.Hunger = 0f;
        Assert.AreEqual(3f, ai.EffectiveAttackInterval);
    }

    [Test]
    public void WeakenedCadence_HalfTheAttackRate()
    {
        var (attacker, ai) = AddFighter(23, 16);
        attacker.Hunger = 0f; // weakened; starvation clock frozen in AddBody
        Assert.IsTrue(attacker.IsWeakened);
        Assert.AreEqual(2f, ai.EffectiveAttackInterval);
        Health target = AddHostile(24, 16);

        StepFor(10.5f); // attacks at ~0, 2, 4, 6, 8, 10
        Assert.AreEqual(94, target.CurrentHealth, "6 hits in 10.5 s, vs 11 when fed");
        Assert.IsFalse(attacker.IsDead);
    }

    // --- Disengaging ---

    [Test]
    public void TargetKilledExternally_DisengagesAndResumesNeeds()
    {
        var (_, ai) = AddFighter(23, 16);
        Health target = AddHostile(24, 16);

        StepFor(0.5f);
        Assert.AreEqual(GoblinAI.Goal.Fight, ai.CurrentGoal);

        target.TakeDamage(1000);
        Step();
        Assert.AreNotEqual(GoblinAI.Goal.Fight, ai.CurrentGoal);
        Assert.IsNull(ai.CurrentTarget);
        Assert.AreEqual(GoblinAI.Goal.Wander, ai.CurrentGoal);
        Assert.AreEqual(0, target.CurrentHealth);
    }

    [Test]
    public void TargetLeavesRange_Disengages()
    {
        var (_, ai) = AddFighter(23, 16);
        Health target = AddHostile(24, 16);

        StepFor(0.5f);
        Assert.AreEqual(GoblinAI.Goal.Fight, ai.CurrentGoal);
        int hp = target.CurrentHealth;

        target.GetComponent<Goblin>().PlaceOnTile(new Vector2Int(26, 13)); // Manhattan 6 from (23,16)
        StepUntil(() => ai.CurrentGoal != GoblinAI.Goal.Fight, 0.3f, "attacker to disengage on the next poll");
        Assert.IsNull(ai.CurrentTarget);
        StepFor(2f, () => Assert.AreNotEqual(GoblinAI.Goal.Fight, ai.CurrentGoal));
        Assert.AreEqual(hp, target.CurrentHealth, "No hits once out of range");
    }

    [Test]
    public void SameTeam_NeverTargeted()
    {
        var (_, ai) = AddFighter(23, 16);
        Health ally = AddBody("Ally", 24, 16, HealthTeam.Monster, 10).Health;

        StepFor(3f, () => Assert.AreNotEqual(GoblinAI.Goal.Fight, ai.CurrentGoal));
        Assert.AreEqual(10, ally.CurrentHealth);
    }

    // --- Combat vs. sleep / death ---

    [Test]
    public void CombatPreemptsSleep_ReleasesCot()
    {
        var (attacker, ai) = AddFighter(23, 15);
        attacker.Energy = 20f;
        Assert.IsTrue(manager.TryPlace(PlaceableType.LairCot, new Vector2Int(26, 18), PlaceFrom, out Placeable cot));

        StepUntil(() => ai.IsSleeping, 5f, "attacker to fall asleep");
        Assert.IsTrue(cot.IsClaimed);

        Health target = AddHostile(24, 18); // distance 2 from the cot
        StepUntil(() => ai.CurrentGoal == GoblinAI.Goal.Fight, 0.3f, "combat poll to preempt sleep");
        Assert.IsFalse(ai.IsSleeping);
        Assert.IsFalse(cot.IsClaimed);
        Assert.IsNull(ai.ClaimedCot);
        Assert.AreSame(target, ai.CurrentTarget);
    }

    [Test]
    public void HpDeath_WhileSleeping_ReleasesCot()
    {
        var (attacker, ai) = AddFighter(23, 15);
        attacker.Energy = 20f;
        Assert.IsTrue(manager.TryPlace(PlaceableType.LairCot, new Vector2Int(26, 18), PlaceFrom, out Placeable cot));
        StepUntil(() => ai.IsSleeping, 5f, "attacker to fall asleep");

        attacker.Health.TakeDamage(1000);
        Step();

        Assert.IsTrue(attacker.IsDead);
        Assert.IsFalse(attacker.gameObject.activeSelf);
        Assert.IsFalse(cot.IsClaimed, "A goblin killed in its sleep frees the cot");
        Assert.IsNull(ai.ClaimedCot);
    }

    [Test]
    public void GoblinWithoutHealth_StillStarvesAsBefore()
    {
        var go = new GameObject("NoHealth");
        spawned.Add(go);
        var g = go.AddComponent<Goblin>();
        g.Board = board;
        g.LogStats = false;
        g.PlaceOnTile(new Vector2Int(24, 16));
        g.Hunger = 0f;

        g.Tick(59.9f);
        Assert.IsFalse(g.IsDead);
        g.Tick(0.2f);
        Assert.IsTrue(g.IsDead);
    }
}
