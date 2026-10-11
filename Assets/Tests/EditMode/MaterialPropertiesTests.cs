using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Material properties (#110): the material × placeable table behind PlacementManager.TryGetProperties, and its first
/// consumer — a cot's ordered material sets how fast a sleeping goblin restores energy. Sleep timings assert against
/// the table's own numbers.
/// </summary>
public class MaterialPropertiesTests
{
    private static readonly Vector2Int PlaceFrom = new Vector2Int(24, 16); // Reachability origin for placement
    private static readonly Vector2Int CotTile = new Vector2Int(26, 18);
    private const float Dt = 0.02f;
    private const float Frozen = float.PositiveInfinity; // Needs-decay duration that stops the clock exactly (100/∞ = 0)
    private const int SleepTicks = 50;                   // 1 s of sleep at Dt

    private GameObject managerGo;
    private GameObject goblinGo;
    private DungeonBoard board;
    private BoardRenderer boardRenderer;
    private PlacementManager manager;
    private Goblin goblin;
    private GoblinAI ai;

    [SetUp]
    public void SetUp()
    {
        // Default board: 48x32 Rock with the 6x6 Floor cavern at (21,13)-(26,18). No stockpile: orders fund at once.
        managerGo = new GameObject("TestDungeonManager");
        board = managerGo.AddComponent<DungeonBoard>();
        board.InitializeBoard();
        boardRenderer = managerGo.AddComponent<BoardRenderer>();
        boardRenderer.Board = board;
        manager = managerGo.AddComponent<PlacementManager>();
        manager.Board = board;
        manager.Renderer = boardRenderer;
    }

    [TearDown]
    public void TearDown()
    {
        if (goblinGo != null) Object.DestroyImmediate(goblinGo);
        if (managerGo != null) Object.DestroyImmediate(managerGo);
    }

    private PlaceableProperty Row(PlaceableType type, MaterialType material)
    {
        Assert.IsTrue(manager.TryGetProperties(type, material, out PlaceableProperty row), $"{type}/{material} has a row");
        Assert.IsNotNull(row);
        return row;
    }

    private void AssertMiss(PlaceableType type, MaterialType material)
    {
        PlaceableProperty row = null;
        Assert.DoesNotThrow(() => Assert.IsFalse(manager.TryGetProperties(type, material, out row), $"{type}/{material} has no row"));
        Assert.IsNull(row);
    }

    // --- The seam ---

    [Test]
    public void TheTable_ShipsCotRowsOnly_StoneBaseline_GlimmerstoneRests()
    {
        Assert.AreEqual(3, manager.Properties.Count, "Cot rows only (one per material, #111 added deepstone's): plots and traps have no rest axis");
        foreach (PlaceableProperty row in manager.Properties) Assert.AreEqual(PlaceableType.LairCot, row.type);

        PlaceableProperty stone = Row(PlaceableType.LairCot, MaterialType.Stone);
        Assert.AreEqual(1f, stone.restRateMultiplier, "Stone is the baseline");
        Assert.AreEqual(1f, stone.restQuality);
        Assert.IsFalse(stone.restsMagicalCreatures);

        PlaceableProperty glimmer = Row(PlaceableType.LairCot, MaterialType.Glimmerstone);
        Assert.AreEqual(1.25f, glimmer.restRateMultiplier);
        Assert.AreEqual(1f, glimmer.restQuality);
        Assert.IsTrue(glimmer.restsMagicalCreatures);
    }

    [Test]
    public void ARow_RoundTripsEveryColumn_ThroughTheSeam()
    {
        manager.Properties = new List<PlaceableProperty>
        {
            new PlaceableProperty(PlaceableType.SpikeTrap, MaterialType.Glimmerstone, 0.5f, 3.5f, true),
        };
        PlaceableProperty row = Row(PlaceableType.SpikeTrap, MaterialType.Glimmerstone);
        Assert.AreEqual(PlaceableType.SpikeTrap, row.type);
        Assert.AreEqual(MaterialType.Glimmerstone, row.material);
        Assert.AreEqual(0.5f, row.restRateMultiplier);
        Assert.AreEqual(3.5f, row.restQuality, "Data only: carried, not consumed");
        Assert.IsTrue(row.restsMagicalCreatures, "Data only: carried, not consumed");
    }

    [Test]
    public void ARowWithOnlyItsKey_HasTheDefaults()
    {
        var row = new PlaceableProperty(PlaceableType.MushroomPlot, MaterialType.Stone);
        Assert.AreEqual(1f, row.restRateMultiplier);
        Assert.AreEqual(1f, row.restQuality);
        Assert.IsFalse(row.restsMagicalCreatures);
    }

    [Test]
    public void AMissingRow_IsFalseAndNull_NeverAnError()
    {
        AssertMiss(PlaceableType.MushroomPlot, MaterialType.Stone);
        AssertMiss(PlaceableType.SpikeTrap, MaterialType.Glimmerstone);
        AssertMiss(PlaceableType.SpikeTrap, MaterialType.Stone);
    }

    [Test]
    public void AClearedTable_MakesEveryQueryAMiss_NoException()
    {
        manager.Properties.Clear();
        AssertMiss(PlaceableType.LairCot, MaterialType.Stone);
        AssertMiss(PlaceableType.LairCot, MaterialType.Glimmerstone);

        manager.Properties = null; // The accessor never leaves the table null
        Assert.IsNotNull(manager.Properties);
        AssertMiss(PlaceableType.LairCot, MaterialType.Stone);

        manager.Properties = new List<PlaceableProperty> { null }; // A hole in the table is skipped, not thrown on
        AssertMiss(PlaceableType.LairCot, MaterialType.Stone);
    }

    [Test]
    public void ThePropertiesTable_LeavesTheCostTableAlone()
    {
        int costRows = manager.Costs.Count;
        manager.Properties.Clear();
        Assert.AreEqual(costRows, manager.Costs.Count);
        Assert.IsTrue(manager.TryGetCost(PlaceableType.LairCot, MaterialType.Glimmerstone, out _), "Orderable without a property row");
    }

    // --- Sleep reads it ---

    /// <summary>A tired goblin and a built cot of this material; steps until the goblin is asleep on it.</summary>
    private void SleepOnACotOf(MaterialType material)
    {
        Assert.IsTrue(manager.TryPlace(PlaceableType.LairCot, material, CotTile, PlaceFrom, out Placeable cot));
        cot.CompleteConstruction();
        Assert.AreEqual(material, cot.OrderedMaterial);

        goblinGo = new GameObject("TestGoblin");
        goblin = goblinGo.AddComponent<Goblin>();
        goblin.Board = board;
        goblin.Renderer = boardRenderer;
        goblin.MoveSpeed = 3f;
        goblin.LogStats = false;
        goblin.HungerSecondsToEmpty = Frozen; // Needs change only when the AI changes them
        goblin.EnergySecondsToEmpty = Frozen;
        goblin.PlaceOnTile(new Vector2Int(23, 15));
        goblin.Energy = 20f;

        ai = goblinGo.AddComponent<GoblinAI>();
        ai.Goblin = goblin;
        ai.PlacementManager = manager;
        ai.Rng = new System.Random(1);

        StepUntil(() => ai.IsSleeping, 5f, "the goblin to reach the cot and sleep");
        Assert.AreSame(cot, ai.ClaimedCot);
    }

    private void Step()
    {
        goblin.Tick(Dt);
        ai.Tick(Dt);
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

    /// <summary>Energy after SleepTicks ticks asleep, with the energy the sleep started from.</summary>
    private float EnergyAfterSleeping(out float start)
    {
        start = goblin.Energy;
        for (int i = 0; i < SleepTicks; i++) Step();
        Assert.IsTrue(ai.IsSleeping, "Still asleep: the measurement never hit the wake clamp");
        return goblin.Energy;
    }

    /// <summary>The pre-#110 restore, tick for tick: Energy += MaxNeed / sleepSecondsToFull * dt.</summary>
    private float PreStoryEnergy(float start)
    {
        var energy = new float[] { start }; // An array cell, so each tick rounds to float exactly as the Energy field does
        for (int i = 0; i < SleepTicks; i++) energy[0] += Goblin.MaxNeed / ai.SleepSecondsToFull * Dt;
        return energy[0];
    }

    [Test]
    public void AStoneCot_RestoresAtExactlyThePreStoryRate()
    {
        SleepOnACotOf(MaterialType.Stone);
        float after = EnergyAfterSleeping(out float start);
        Assert.AreEqual(PreStoryEnergy(start), after, "Bit for bit: the stone row's 1.0 changes nothing");
    }

    [Test]
    public void ACotWithNoRow_RestoresAtExactlyThePreStoryRate()
    {
        manager.Properties.Clear();
        SleepOnACotOf(MaterialType.Glimmerstone); // Would be 1.25× with its row
        float after = EnergyAfterSleeping(out float start);
        Assert.AreEqual(PreStoryEnergy(start), after, "A miss is all-defaults: rate 1.0");
    }

    [Test]
    public void AGlimmerstoneCot_RestoresAtTheTablesRate()
    {
        float rate = Row(PlaceableType.LairCot, MaterialType.Glimmerstone).restRateMultiplier;
        Assert.AreNotEqual(1f, rate, "The row must differ from the baseline for this test to mean anything");

        SleepOnACotOf(MaterialType.Glimmerstone);
        float after = EnergyAfterSleeping(out float start);
        float baselineGain = PreStoryEnergy(start) - start;
        Assert.AreEqual(baselineGain * rate, after - start, 1e-3f, "The baseline gain × the row's multiplier");
    }

    [Test]
    public void TheRate_IsReadFromTheTable_NotBakedIn()
    {
        Row(PlaceableType.LairCot, MaterialType.Stone).restRateMultiplier = 0.5f; // Retuned, as in the Inspector
        SleepOnACotOf(MaterialType.Stone);
        float after = EnergyAfterSleeping(out float start);
        Assert.AreEqual((PreStoryEnergy(start) - start) * 0.5f, after - start, 1e-3f);
    }

    [Test]
    public void SleepDuration_ScalesInverselyWithTheRate_AndWakesTheSameWay()
    {
        SleepOnACotOf(MaterialType.Stone);
        float stoneSeconds = StepUntil(() => !ai.IsSleeping, 20f, "the stone sleeper to wake");
        Assert.AreEqual(Goblin.MaxNeed, goblin.Energy, "Wakes full");
        Assert.IsNull(ai.ClaimedCot, "Wakes and releases, as before");

        TearDown();
        SetUp();
        float rate = Row(PlaceableType.LairCot, MaterialType.Glimmerstone).restRateMultiplier;
        SleepOnACotOf(MaterialType.Glimmerstone);
        Placeable cot = ai.ClaimedCot;
        float glimmerSeconds = StepUntil(() => !ai.IsSleeping, 20f, "the glimmerstone sleeper to wake");
        Assert.AreEqual(Goblin.MaxNeed, goblin.Energy, "Wakes full");
        Assert.IsNull(ai.ClaimedCot);
        Assert.IsFalse(cot.IsClaimed, "Released on waking");

        Assert.AreEqual(stoneSeconds / rate, glimmerSeconds, 2f * Dt, "Same energy to restore, at the row's rate");
    }
}
