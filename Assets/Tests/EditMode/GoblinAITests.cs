using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

public class GoblinAITests
{
    private static readonly Vector2Int PlaceFrom = new Vector2Int(24, 16); // Reachability origin for placement
    private const float Dt = 0.02f;
    private const float Frozen = float.PositiveInfinity; // Needs-decay duration that stops the clock exactly (100/∞ = 0)

    private GameObject managerGo;
    private DungeonBoard board;
    private BoardRenderer boardRenderer;
    private PlacementManager manager;
    private readonly List<GameObject> goblinGos = new List<GameObject>();
    private readonly List<(Goblin goblin, GoblinAI ai)> troupe = new List<(Goblin, GoblinAI)>();

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
        manager.MushroomGrowthSecondsPerFood = 1f; // set before placing: stock plots by ticking 1 s per food
        manager.MushroomCapacity = 5;
    }

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject go in goblinGos) if (go != null) Object.DestroyImmediate(go);
        goblinGos.Clear();
        troupe.Clear();
        if (managerGo != null) Object.DestroyImmediate(managerGo);
    }

    private (Goblin goblin, GoblinAI ai) AddGoblin(int x, int y, float hunger = 100f, float energy = 100f, int seed = 1)
    {
        var go = new GameObject("TestGoblin" + goblinGos.Count);
        goblinGos.Add(go);
        var goblin = go.AddComponent<Goblin>();
        goblin.Board = board;
        goblin.Renderer = boardRenderer;
        goblin.MoveSpeed = 3f;
        goblin.LogStats = false;
        goblin.HungerSecondsToEmpty = Frozen; // needs change only when the test or the AI changes them
        goblin.EnergySecondsToEmpty = Frozen;
        goblin.PlaceOnTile(new Vector2Int(x, y));
        goblin.Hunger = hunger;
        goblin.Energy = energy;

        var ai = go.AddComponent<GoblinAI>();
        ai.Goblin = goblin;
        ai.PlacementManager = manager;
        ai.Rng = new System.Random(seed);

        troupe.Add((goblin, ai));
        return (goblin, ai);
    }

    private MushroomPlot PlacePlot(int x, int y, int food)
    {
        Assert.IsTrue(manager.TryPlace(PlaceableType.MushroomPlot, new Vector2Int(x, y), PlaceFrom, out Placeable p));
        var plot = p.GetComponent<MushroomPlot>();
        plot.Tick(food); // 1 s per food
        Assert.AreEqual(food, plot.FoodCount);
        return plot;
    }

    private Placeable PlaceCot(int x, int y)
    {
        Assert.IsTrue(manager.TryPlace(PlaceableType.LairCot, new Vector2Int(x, y), PlaceFrom, out Placeable cot));
        return cot;
    }

    private void Enclose(int x, int y)
    {
        board.SetTile(x + 1, y, TileState.Rock);
        board.SetTile(x - 1, y, TileState.Rock);
        board.SetTile(x, y + 1, TileState.Rock);
        board.SetTile(x, y - 1, TileState.Rock);
    }

    private void Step()
    {
        foreach (var (goblin, ai) in troupe)
        {
            goblin.Tick(Dt);
            ai.Tick(Dt);
        }
    }

    private void StepFor(float seconds, Action afterEachStep = null)
    {
        for (float t = 0f; t < seconds; t += Dt)
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

    private static int Steps(Vector2Int a, Vector2Int b) => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);

    // --- Registry queries ---

    [Test]
    public void GetMushroomPlotsAndCots_ScanOrder_FilterByType()
    {
        Placeable cotB = PlaceCot(25, 13);
        MushroomPlot plotB = PlacePlot(23, 17, 0);
        Placeable cotA = PlaceCot(22, 18);
        MushroomPlot plotA = PlacePlot(22, 14, 1);
        manager.TryPlace(PlaceableType.SpikeTrap, new Vector2Int(21, 13), PlaceFrom, out _);
        cotA.TryClaim(); // claimed cots are still listed

        CollectionAssert.AreEqual(new[] { plotA, plotB }, manager.GetMushroomPlots());
        CollectionAssert.AreEqual(new[] { cotA, cotB }, manager.GetCots());
    }

    // --- Eating ---

    [Test]
    public void EatCycle_EatsFromBesidePlot_OneFood_HungerRestored()
    {
        var (goblin, ai) = AddGoblin(24, 16, hunger: 20f);
        MushroomPlot plot = PlacePlot(21, 13, 2);

        Step();
        Assert.AreEqual(GoblinAI.Goal.Eat, ai.CurrentGoal);
        Assert.AreSame(plot, ai.TargetPlot);

        StepUntil(() => plot.FoodCount == 1, 10f, "goblin to reach the plot and eat");
        Assert.AreEqual(70f, goblin.Hunger, 1e-3f, "20 + 50 per food");
        Assert.AreEqual(1, Steps(goblin.CurrentTile, plot.Tile), "Eats from beside the plot");
        Assert.AreNotEqual(plot.Tile, goblin.CurrentTile);

        Step();
        Assert.AreNotEqual(GoblinAI.Goal.Eat, ai.CurrentGoal, "Fed: goal complete");
        Assert.IsNull(ai.TargetPlot);
        StepFor(5f);
        Assert.AreEqual(1, plot.FoodCount, "One food per trip; Hunger 70 doesn't send it back");
    }

    [Test]
    public void StillHungryAfterEating_GoesBackForMore()
    {
        var (goblin, ai) = AddGoblin(24, 16, hunger: 5f);
        ai.HungerPerFood = 10f; // 5 → 15 → 25 → 35: three trips to clear the threshold
        MushroomPlot plot = PlacePlot(21, 13, 5);

        StepUntil(() => goblin.Hunger >= 30f, 20f, "repeat trips until no longer hungry");
        Assert.AreEqual(35f, goblin.Hunger, 1e-3f);
        Assert.AreEqual(2, plot.FoodCount, "Exactly three foods eaten");

        StepFor(5f);
        Assert.AreEqual(2, plot.FoodCount, "Stops once Hunger is at or above the threshold");
    }

    [Test]
    public void HungerAtThreshold_DoesNotEat_Wanders()
    {
        var (_, ai) = AddGoblin(24, 16, hunger: 30f);
        MushroomPlot plot = PlacePlot(21, 13, 3);

        bool wandered = false;
        StepFor(10f, () => wandered |= ai.CurrentGoal == GoblinAI.Goal.Wander);

        Assert.AreEqual(3, plot.FoodCount);
        Assert.IsTrue(wandered);
    }

    [Test]
    public void TrySelectPlot_NearestStocked_SkipsNearerEmpty()
    {
        PlacePlot(23, 16, 0);                       // adjacent to the goblin, but empty
        PlacePlot(21, 13, 2);                       // stocked, stand-point 5 steps away
        MushroomPlot near = PlacePlot(26, 18, 2);   // stocked, stand-point 3 steps away

        Assert.IsTrue(GoblinAI.TrySelectPlot(board, manager.GetMushroomPlots(), PlaceFrom, out MushroomPlot plot, out Vector2Int stand));
        Assert.AreSame(near, plot);
        Assert.AreEqual(1, Steps(stand, near.Tile));
        Assert.IsTrue(board.IsWalkable(stand.x, stand.y));
    }

    [Test]
    public void TrySelectPlot_Tie_KeepsScanOrder()
    {
        MushroomPlot first = PlacePlot(22, 16, 1); // stand-point (23,16): 1 step
        PlacePlot(26, 16, 1);                      // stand-point (25,16): 1 step

        Assert.IsTrue(GoblinAI.TrySelectPlot(board, manager.GetMushroomPlots(), PlaceFrom, out MushroomPlot plot, out _));
        Assert.AreSame(first, plot);
    }

    [Test]
    public void TwoGoblinsRaceForLastFood_ExactlyOneEats_OtherRedecides()
    {
        var (g1, a1) = AddGoblin(23, 16, hunger: 20f);
        var (g2, a2) = AddGoblin(25, 16, hunger: 20f);
        MushroomPlot plot = PlacePlot(24, 18, 1);

        Step();
        Assert.AreEqual(GoblinAI.Goal.Eat, a1.CurrentGoal);
        Assert.AreEqual(GoblinAI.Goal.Eat, a2.CurrentGoal);

        StepFor(5f);
        Assert.AreEqual(0, plot.FoodCount);
        Assert.AreEqual(90f, g1.Hunger + g2.Hunger, 1e-3f, "Exactly one ate: 70 + 20");
        Assert.AreNotEqual(GoblinAI.Goal.Eat, a1.CurrentGoal);
        Assert.AreNotEqual(GoblinAI.Goal.Eat, a2.CurrentGoal, "The loser dropped its goal (no stock left)");
    }

    [Test]
    public void UnreachableStockedPlot_NoStall_SleepsInstead()
    {
        var (goblin, ai) = AddGoblin(24, 16, hunger: 20f, energy: 20f);
        goblin.StarvationSecondsToDie = Frozen;
        MushroomPlot plot = PlacePlot(21, 13, 3);
        Enclose(21, 13);
        Placeable cot = PlaceCot(26, 18);

        StepUntil(() => ai.IsSleeping, 10f, "goblin to give up on unreachable food and sleep");
        Assert.AreSame(cot, ai.ClaimedCot);
        Assert.AreEqual(3, plot.FoodCount);
    }

    // --- Sleeping ---

    [Test]
    public void SleepCycle_ClaimFromSelection_SleepsOnCot_RestoresAtRate_WakesAndReleases()
    {
        var (goblin, ai) = AddGoblin(23, 15, energy: 20f);
        Placeable cot = PlaceCot(26, 18);

        Step();
        Assert.AreEqual(GoblinAI.Goal.Sleep, ai.CurrentGoal);
        Assert.AreSame(cot, ai.ClaimedCot);
        Assert.IsTrue(cot.IsClaimed, "Claimed at selection");
        Assert.IsTrue(goblin.IsMoving, "Still traveling while the claim is held");
        Assert.IsFalse(ai.IsSleeping);

        StepUntil(() => ai.IsSleeping, 5f, "goblin to reach the cot");
        Assert.AreEqual(cot.Tile, goblin.CurrentTile, "Sleeps on the cot tile");
        float e0 = goblin.Energy;

        StepFor(1f); // 10 s to full → +10 per second
        Assert.AreEqual(e0 + 10f, goblin.Energy, 0.25f);
        Assert.IsTrue(ai.IsSleeping);
        Assert.IsFalse(goblin.IsMoving, "No movement orders while sleeping");

        StepUntil(() => !ai.IsSleeping, 10f, "goblin to wake when full");
        Assert.AreEqual(100f, goblin.Energy);
        Assert.IsFalse(cot.IsClaimed);
        Assert.IsNull(ai.ClaimedCot);
    }

    [Test]
    public void CotContention_OnlyOneGoblinClaimsAndSleeps()
    {
        var (_, a1) = AddGoblin(23, 15, energy: 20f);
        var (_, a2) = AddGoblin(25, 15, energy: 20f);
        Placeable cot = PlaceCot(24, 18);

        int maxSleepers = 0;
        bool someoneWandered = false;
        StepFor(5f, () =>
        {
            int sleepers = (a1.IsSleeping ? 1 : 0) + (a2.IsSleeping ? 1 : 0);
            maxSleepers = Mathf.Max(maxSleepers, sleepers);
            Assert.IsFalse(a1.ClaimedCot != null && a2.ClaimedCot != null, "Both hold the one cot");
            someoneWandered |= a1.CurrentGoal == GoblinAI.Goal.Wander || a2.CurrentGoal == GoblinAI.Goal.Wander;
        });

        Assert.AreEqual(1, maxSleepers);
        Assert.IsTrue(a1.IsSleeping ^ a2.IsSleeping);
        Assert.IsTrue(cot.IsClaimed);
        Assert.IsTrue(someoneWandered, "The loser keeps wandering");
    }

    [Test]
    public void NoFreeCot_KeepsWandering_ThenSleepsWhenOneFreesUp()
    {
        var (_, ai) = AddGoblin(24, 16, energy: 20f);

        bool wandered = false;
        StepFor(3f, () => wandered |= ai.CurrentGoal == GoblinAI.Goal.Wander); // no cots at all
        Assert.IsTrue(wandered);
        Assert.IsNull(ai.ClaimedCot);

        Placeable cot = PlaceCot(26, 18);
        Assert.IsTrue(cot.TryClaim()); // taken by someone else
        StepFor(3f);
        Assert.IsFalse(ai.IsSleeping);
        Assert.IsNull(ai.ClaimedCot);

        cot.Release();
        StepUntil(() => ai.IsSleeping, 10f, "goblin to notice the freed cot and sleep");
        Assert.AreSame(cot, ai.ClaimedCot);
    }

    [Test]
    public void HungerPriority_SleepingGoblinWakesReleasesAndEats()
    {
        var (goblin, ai) = AddGoblin(23, 15, energy: 20f);
        Placeable cot = PlaceCot(26, 18);
        MushroomPlot plot = PlacePlot(21, 13, 2);

        StepUntil(() => ai.IsSleeping, 5f, "goblin to fall asleep");
        goblin.Hunger = 20f;

        StepUntil(() => !cot.IsClaimed, 0.5f, "hunger to wake the goblin within a poll");
        Assert.IsFalse(ai.IsSleeping);
        Assert.IsNull(ai.ClaimedCot);
        Assert.AreEqual(GoblinAI.Goal.Eat, ai.CurrentGoal);

        StepUntil(() => plot.FoodCount == 1, 10f, "goblin to go and eat");
    }

    [Test]
    public void HungrySleeper_NoFood_KeepsSleeping()
    {
        var (goblin, ai) = AddGoblin(23, 15, energy: 20f);
        goblin.StarvationSecondsToDie = Frozen;
        Placeable cot = PlaceCot(26, 18);
        PlacePlot(21, 13, 0); // empty

        StepUntil(() => ai.IsSleeping, 5f, "goblin to fall asleep");
        goblin.Hunger = 20f;
        StepFor(1f, () => Assert.IsTrue(ai.IsSleeping, "No food to wake for: keeps sleeping (no wake/re-sleep thrash)"));
        Assert.IsTrue(cot.IsClaimed);
    }

    // --- Wandering ---

    [Test]
    public void Wander_NeedsFull_VisitsOtherTiles_AllWalkable()
    {
        var (goblin, ai) = AddGoblin(24, 16);
        var start = goblin.CurrentTile;

        bool moved = false;
        StepFor(20f, () =>
        {
            Assert.IsTrue(board.IsWalkable(goblin.CurrentTile.x, goblin.CurrentTile.y));
            Assert.AreNotEqual(GoblinAI.Goal.Eat, ai.CurrentGoal);
            Assert.AreNotEqual(GoblinAI.Goal.Sleep, ai.CurrentGoal);
            moved |= goblin.CurrentTile != start;
        });
        Assert.IsTrue(moved);
    }

    [Test]
    public void PickWanderTarget_Seeded_ReturnsReachableWalkableTile()
    {
        for (int seed = 0; seed < 10; seed++)
        {
            Vector2Int t = GoblinAI.PickWanderTarget(board, PlaceFrom, new System.Random(seed));
            Assert.IsTrue(board.IsWalkable(t.x, t.y));
            Assert.Greater(Pathfinder.FindPath(board, PlaceFrom, t).Count, 0);
        }
    }

    [Test]
    public void PickWanderTarget_IsolatedPocket_ReturnsFromTile()
    {
        board.SetTile(5, 5, TileState.Floor); // enclosed one-tile pocket; the cavern is unreachable from it
        var from = new Vector2Int(5, 5);

        Assert.AreEqual(from, GoblinAI.PickWanderTarget(board, from, new System.Random(3)));
    }

    // --- Death / missing manager ---

    [Test]
    public void Death_ReleasesClaimedCot()
    {
        var (goblin, ai) = AddGoblin(23, 15, energy: 20f);
        Placeable cot = PlaceCot(26, 18);

        StepUntil(() => ai.IsSleeping, 5f, "goblin to fall asleep");
        goblin.Hunger = 0f;               // no plots: it stays asleep and starves
        goblin.StarvationSecondsToDie = 0.5f;

        StepUntil(() => goblin.IsDead, 1f, "goblin to starve");
        Assert.IsFalse(cot.IsClaimed, "A dead goblin must not squat a cot");
        Assert.IsNull(ai.ClaimedCot);
        Assert.IsFalse(ai.IsSleeping);
        Assert.AreEqual(GoblinAI.Goal.None, ai.CurrentGoal);
    }

    [Test]
    public void NoManager_WandersOnly_NoExceptions()
    {
        var (goblin, ai) = AddGoblin(24, 16, hunger: 20f, energy: 20f);
        goblin.StarvationSecondsToDie = Frozen;
        ai.PlacementManager = null;
        MushroomPlot plot = PlacePlot(21, 13, 2);
        Placeable cot = PlaceCot(26, 18);

        bool wandered = false;
        StepFor(5f, () => wandered |= ai.CurrentGoal == GoblinAI.Goal.Wander);

        Assert.IsTrue(wandered);
        Assert.AreEqual(2, plot.FoodCount);
        Assert.IsFalse(cot.IsClaimed);
    }
}
