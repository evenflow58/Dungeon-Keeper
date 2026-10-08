using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// The imp as builder (#92): construction sites join the unified dig / build / rearm selection, progress is
/// written onto the site, and the site completes through #91's CompleteConstruction seam.
/// </summary>
public class ImpBuilderTests
{
    private static readonly Vector2Int ImpTile = new Vector2Int(24, 16); // Imp spawn: cavern center
    private const float Dt = 0.02f;

    private GameObject managerGo;
    private GameObject impGo;
    private DungeonBoard board;
    private BoardRenderer boardRenderer;
    private PlacementManager manager;
    private Imp imp;
    private ImpDigger digger;

    [SetUp]
    public void SetUp()
    {
        // Default board: 48x32 Rock with the 6x6 Floor cavern at (21,13)-(26,18). Imp spawns at (24,16).
        managerGo = new GameObject("TestDungeonManager");
        board = managerGo.AddComponent<DungeonBoard>();
        board.InitializeBoard();
        boardRenderer = managerGo.AddComponent<BoardRenderer>();
        boardRenderer.Board = board;
        manager = managerGo.AddComponent<PlacementManager>();
        manager.Board = board;
        manager.Renderer = boardRenderer;
        manager.MushroomGrowthSecondsPerFood = 1f;

        impGo = new GameObject("TestImp");
        imp = impGo.AddComponent<Imp>();
        imp.Board = board;
        imp.Renderer = boardRenderer;
        imp.MoveSpeed = 4f;
        imp.Spawn();

        digger = impGo.AddComponent<ImpDigger>();
        digger.Board = board;
        digger.Imp = imp;
        digger.PlacementManager = manager;
        digger.DigSecondsPerTile = 1f;
        digger.RearmSecondsPerTrap = 1f;
    }

    [TearDown]
    public void TearDown()
    {
        if (impGo != null) Object.DestroyImmediate(impGo);
        if (managerGo != null) Object.DestroyImmediate(managerGo);
    }

    private Placeable PlaceSite(PlaceableType type, int x, int y)
    {
        Assert.IsTrue(manager.TryPlace(type, new Vector2Int(x, y), ImpTile, out Placeable p));
        Assert.IsFalse(p.IsBuilt);
        return p;
    }

    private SpikeTrap PlaceSpentTrap(int x, int y)
    {
        Assert.IsTrue(manager.TryPlaceBuilt(PlaceableType.SpikeTrap, new Vector2Int(x, y), ImpTile, out Placeable p));
        var trap = p.GetComponent<SpikeTrap>();
        Assert.IsTrue(trap.TryTrigger(out _));
        return trap;
    }

    private void Designate(int x, int y) => board.SetTile(x, y, TileState.Designated);

    private void Enclose(int x, int y)
    {
        board.SetTile(x + 1, y, TileState.Rock);
        board.SetTile(x - 1, y, TileState.Rock);
        board.SetTile(x, y + 1, TileState.Rock);
        board.SetTile(x, y - 1, TileState.Rock);
    }

    private bool Select(out Vector2Int target, out Vector2Int stand, out Placeable site, out SpikeTrap trap) =>
        ImpDigger.TrySelectWork(board, manager.GetConstructionSites(), manager.GetBuiltSpikeTraps(), ImpTile,
            out target, out stand, out site, out trap);

    private int NeighborCost(int x, int y) => Pathfinder.FindPathToNeighbor(board, ImpTile, new Vector2Int(x, y)).Count;
    private int AdjacentCost(int x, int y) => Pathfinder.FindPathToAdjacent(board, ImpTile, new Vector2Int(x, y)).Count;

    private void Step(float dt = Dt)
    {
        imp.Advance(dt);
        digger.Tick(dt);
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

    // --- Selection ---

    [Test]
    public void LoneSite_IsSelected_WithAStandPointBesideIt_NeverOnIt()
    {
        Placeable cot = PlaceSite(PlaceableType.LairCot, 22, 14);
        Assert.IsTrue(Select(out Vector2Int target, out Vector2Int stand, out Placeable site, out SpikeTrap trap));
        Assert.AreSame(cot, site);
        Assert.IsNull(trap);
        Assert.AreEqual(cot.Tile, target);
        Assert.AreEqual(1, Steps(stand, cot.Tile), "Stands beside the site");
        Assert.AreNotEqual(cot.Tile, stand, "Never on it");
        Assert.IsTrue(board.IsWalkable(stand.x, stand.y));
    }

    [Test]
    public void BuiltPlaceables_AreNotBuildWork()
    {
        Assert.IsTrue(manager.TryPlaceBuilt(PlaceableType.LairCot, new Vector2Int(22, 14), ImpTile, out _));
        Assert.IsFalse(Select(out _, out _, out Placeable site, out _));
        Assert.IsNull(site);
        CollectionAssert.IsEmpty(manager.GetConstructionSites());
    }

    [Test]
    public void GetConstructionSites_AllTypes_UnbuiltOnly_ScanOrder()
    {
        Placeable c = PlaceSite(PlaceableType.SpikeTrap, 25, 13);
        Placeable a = PlaceSite(PlaceableType.LairCot, 21, 18);
        Placeable b = PlaceSite(PlaceableType.MushroomPlot, 22, 14);
        Assert.IsTrue(manager.TryPlaceBuilt(PlaceableType.LairCot, new Vector2Int(21, 13), ImpTile, out _));
        CollectionAssert.AreEqual(new[] { a, b, c }, manager.GetConstructionSites());
    }

    [Test]
    public void NearestWins_AcrossDigBuildAndRearm()
    {
        // Build nearest: site beside the imp; a dig and a spent trap farther away.
        Placeable near = PlaceSite(PlaceableType.LairCot, 23, 17);
        Designate(27, 16);
        PlaceSpentTrap(21, 13);
        Assert.Less(NeighborCost(23, 17), AdjacentCost(27, 16));
        Assert.Less(NeighborCost(23, 17), NeighborCost(21, 13));
        Assert.IsTrue(Select(out _, out _, out Placeable site, out SpikeTrap trap));
        Assert.AreSame(near, site, "The nearest job is the build");
        Assert.IsNull(trap);
    }

    [Test]
    public void StrictlyNearerRearm_BeatsABuild()
    {
        SpikeTrap spent = PlaceSpentTrap(24, 14);          // Stand (24,15): beside the imp
        PlaceSite(PlaceableType.MushroomPlot, 21, 18);     // Far corner
        Assert.Less(NeighborCost(24, 14), NeighborCost(21, 18));
        Assert.IsTrue(Select(out _, out _, out Placeable site, out SpikeTrap trap));
        Assert.AreSame(spent, trap);
        Assert.IsNull(site, "Exactly one outcome");
    }

    [Test]
    public void StrictlyNearerDig_BeatsABuild()
    {
        Designate(24, 19);                                 // Stand (24,18)
        PlaceSite(PlaceableType.LairCot, 21, 13);          // Far corner
        Assert.Less(AdjacentCost(24, 19), NeighborCost(21, 13));
        Assert.IsTrue(Select(out Vector2Int target, out _, out Placeable site, out SpikeTrap trap));
        Assert.AreEqual(new Vector2Int(24, 19), target);
        Assert.IsNull(site);
        Assert.IsNull(trap);
    }

    [Test]
    public void ExactTie_DigBeatsBuild()
    {
        // The site comes first in scan order (x=24 < 27), so the dig wins on kind, not position.
        PlaceSite(PlaceableType.LairCot, 24, 13); // Stand (24,14)
        Designate(27, 16);                        // Stand (26,16)
        Assert.AreEqual(NeighborCost(24, 13), AdjacentCost(27, 16), "Precondition: equal path lengths");
        Assert.IsTrue(Select(out Vector2Int target, out _, out Placeable site, out _));
        Assert.IsNull(site);
        Assert.AreEqual(new Vector2Int(27, 16), target);
    }

    [Test]
    public void ExactTie_BuildBeatsRearm()
    {
        // The trap comes first in scan order (x=21 < 24), so the build wins on kind, not position.
        PlaceSpentTrap(21, 16);                                  // Stand (22,16)
        Placeable cot = PlaceSite(PlaceableType.LairCot, 24, 13); // Stand (24,14)
        Assert.AreEqual(NeighborCost(21, 16), NeighborCost(24, 13), "Precondition: equal path lengths");
        Assert.IsTrue(Select(out _, out _, out Placeable site, out SpikeTrap trap));
        Assert.AreSame(cot, site);
        Assert.IsNull(trap);
    }

    [Test]
    public void ExactTie_DigStillBeatsRearm_AsBefore()
    {
        PlaceSpentTrap(24, 13);
        Designate(27, 16);
        Assert.AreEqual(NeighborCost(24, 13), AdjacentCost(27, 16), "Precondition: equal path lengths");
        Assert.IsTrue(Select(out Vector2Int target, out _, out Placeable site, out SpikeTrap trap));
        Assert.IsNull(site);
        Assert.IsNull(trap);
        Assert.AreEqual(new Vector2Int(27, 16), target);
    }

    [Test]
    public void UnreachableSite_IsSkipped_LeftPending_WhileOtherWorkProceeds()
    {
        Placeable walledIn = PlaceSite(PlaceableType.LairCot, 22, 14);
        Enclose(22, 14);
        Designate(27, 16);

        Assert.IsTrue(Select(out Vector2Int target, out _, out Placeable site, out _));
        Assert.IsNull(site, "No stand-point: skipped");
        Assert.AreEqual(new Vector2Int(27, 16), target);

        StepUntil(() => board.GetTile(27, 16) == TileState.Floor, 5f, "the dig to finish");
        for (int i = 0; i < 50; i++) Step();
        Assert.IsFalse(walledIn.IsBuilt, "Left pending");
        Assert.IsNotNull(walledIn.MaterialsPile);
        Assert.IsNull(digger.CurrentSite);
        Assert.IsFalse(digger.IsBuilding);

        // The dungeon changes: open a neighbor and the pending site gets built.
        board.SetTile(23, 14, TileState.Floor);
        StepUntil(() => walledIn.IsBuilt, 10f, "the now-reachable site to be built");
    }

    // --- Building ---

    [Test]
    public void Build_ProgressAccruesOnTheSite_ThenCompletesThroughTheSeam()
    {
        manager.SpikeTrapBuildSeconds = 1f;
        Placeable site = PlaceSite(PlaceableType.SpikeTrap, 22, 14);
        SpikeTrap trap = site.GetComponent<SpikeTrap>();

        StepUntil(() => digger.IsBuilding, 5f, "imp to arrive beside the site and start building");
        Assert.AreSame(site, digger.CurrentSite);
        Assert.IsNull(digger.CurrentTrap, "One job at a time");
        Assert.IsNull(digger.CurrentTarget);
        Assert.AreEqual(digger.StandPoint, imp.CurrentTile);
        Assert.AreEqual(1, Steps(imp.CurrentTile, site.Tile), "Builds from beside the site");
        Assert.AreEqual(0f, site.BuildProgress, 1e-6f);

        for (int i = 0; i < 25; i++) Step(); // 0.5 s of a 1 s build
        Assert.AreEqual(0.5f, site.BuildProgress, 1e-3f, "Progress is the site's");
        Assert.IsFalse(site.IsBuilt);
        Assert.IsFalse(trap.IsArmed, "Not working until built");
        Assert.IsFalse(imp.IsMoving, "Stationary while building");

        float rest = StepUntil(() => site.IsBuilt, 1f, "the build to complete");
        Assert.AreEqual(0.5f, rest, Dt + 1e-4f, "Completes at the type's full duration");
        Assert.IsNull(site.MaterialsPile, "Pile gone");
        Assert.IsTrue(site.Model.activeSelf, "Model in");
        Assert.IsTrue(trap.IsArmed, "Works now: the trap is armed");
        Assert.IsNull(digger.CurrentSite);
        Assert.IsFalse(digger.IsBuilding);

        for (int i = 0; i < 30; i++) Step();
        Assert.IsNull(digger.CurrentSite, "Nothing left: idles");
        Assert.IsFalse(imp.IsMoving);
    }

    private float MeasureBuild(Placeable site)
    {
        StepUntil(() => digger.IsBuilding && digger.CurrentSite == site, 10f, "imp to start building " + site.Type);
        return StepUntil(() => site.IsBuilt, 20f, site.Type + " to complete");
    }

    [Test]
    public void BuildTimes_ArePerType_AtTheDefaults()
    {
        Assert.AreEqual(4f, manager.BuildSecondsFor(PlaceableType.LairCot));
        Assert.AreEqual(6f, manager.BuildSecondsFor(PlaceableType.MushroomPlot));
        Assert.AreEqual(5f, manager.BuildSecondsFor(PlaceableType.SpikeTrap));

        Placeable cot = PlaceSite(PlaceableType.LairCot, 23, 17);
        float cotTime = MeasureBuild(cot);
        Placeable plot = PlaceSite(PlaceableType.MushroomPlot, 23, 15);
        float plotTime = MeasureBuild(plot);

        Assert.AreEqual(4f, cotTime, Dt + 1e-3f);
        Assert.AreEqual(6f, plotTime, Dt + 1e-3f);
        Assert.Greater(plotTime, cotTime, "A plot takes longer than a cot");
    }

    [Test]
    public void Build_PlotStartsGrowingOnlyAtItsOwnCompletion()
    {
        manager.MushroomPlotBuildSeconds = 1f;
        Placeable site = PlaceSite(PlaceableType.MushroomPlot, 22, 14);
        MushroomPlot plot = site.GetComponent<MushroomPlot>();
        StepUntil(() => site.IsBuilt, 5f, "the plot to be built");
        Assert.AreEqual(0, plot.FoodCount, "Starts empty at completion");
        plot.Tick(1f);
        Assert.AreEqual(1, plot.FoodCount, "First food one interval after completion");
    }

    [Test]
    public void Resume_PulledAwayMidBuild_PartialProgressStays_FinishTakesOnlyTheRemainder()
    {
        manager.LairCotBuildSeconds = 2f;
        Placeable site = PlaceSite(PlaceableType.LairCot, 22, 14);
        StepUntil(() => digger.IsBuilding, 5f, "imp to start building");
        for (int i = 0; i < 60; i++) Step(); // 1.2 s of 2 s
        Assert.AreEqual(0.6f, site.BuildProgress, 1e-3f);

        // Pulled away: the imp is no longer beside the site, so the digger's own adjacency check abandons the job.
        imp.PlaceOnTile(new Vector2Int(26, 18));
        Step();
        Assert.IsFalse(digger.IsBuilding, "Job dropped");
        Assert.AreEqual(0.6f, site.BuildProgress, 1e-3f, "Partial work stays on the site");
        Assert.IsFalse(site.IsBuilt);

        StepUntil(() => digger.IsBuilding && digger.CurrentSite == site, 5f, "imp to come back to the site");
        Assert.AreEqual(0.6f, site.BuildProgress, 1e-3f, "Resumes from the site's progress, not zero");
        float rest = StepUntil(() => site.IsBuilt, 2f, "the build to finish");
        Assert.AreEqual(0.8f, rest, Dt + 1e-3f, "Only the remaining 40% of 2 s");
    }

    [Test]
    public void Build_GameTime_PauseFreezes_DoubleSpeedDoubles()
    {
        manager.LairCotBuildSeconds = 2f;
        Placeable site = PlaceSite(PlaceableType.LairCot, 22, 14);
        StepUntil(() => digger.IsBuilding, 5f, "imp to start building");

        for (int i = 0; i < 50; i++) Step(0f);
        Assert.AreEqual(0f, site.BuildProgress, 1e-6f, "Paused: frozen");
        Assert.IsTrue(digger.IsBuilding, "Still on the job");

        Step(Dt);
        float one = site.BuildProgress;
        Step(2f * Dt);
        Assert.AreEqual(2f * one, site.BuildProgress - one, 1e-5f, "2x step: twice the progress");
    }

    [Test]
    public void WhileBuilding_TheImpFacesTheSite()
    {
        imp.CreateModel();
        Placeable site = PlaceSite(PlaceableType.LairCot, 22, 14);
        while (!digger.IsBuilding) { Step(); imp.Pose(Dt); }
        for (int i = 0; i < 30; i++) { Step(); imp.Pose(Dt); }

        Vector3 toSite = site.transform.position - imp.transform.position;
        Assert.AreEqual(CreatureMotion.HeadingYaw(toSite), imp.Motion.TargetYaw, 1e-3f, "Face target is the site");
        Assert.AreEqual(Mathf.Abs(CreatureMotion.HeadingYaw(toSite)), Mathf.Abs(imp.Motion.Yaw), 1e-2f, "Turned to it");
    }
}
