using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Construction sites (#91): placement creates an inert site (a materials pile, the finished model hidden) that
/// works only after Placeable.CompleteConstruction. Behavior once built is covered by the type suites, which
/// stage through PlacementStaging.TryPlaceBuilt.
/// </summary>
public class ConstructionSiteTests
{
    private static readonly Vector2Int PlaceFrom = new Vector2Int(24, 16);
    private static readonly Vector2Int HeartTile = new Vector2Int(24, 14);

    private GameObject managerGo;
    private DungeonBoard board;
    private PlacementManager manager;
    private GameObject heartGo;
    private GameObject spawnerGo;
    private HeroSpawner spawner;

    [SetUp]
    public void SetUp()
    {
        // Default board: Rock, the 6x6 Floor cavern at (21,13)-(26,18), no dig designations.
        managerGo = new GameObject("TestDungeonManager");
        board = managerGo.AddComponent<DungeonBoard>();
        board.InitializeBoard();
        var boardRenderer = managerGo.AddComponent<BoardRenderer>();
        boardRenderer.Board = board;
        manager = managerGo.AddComponent<PlacementManager>();
        manager.Board = board;
        manager.Renderer = boardRenderer;
        manager.MushroomGrowthSecondsPerFood = 1f;
        manager.MushroomCapacity = 5;

        heartGo = new GameObject("TestHeart");
        var heart = heartGo.AddComponent<Heart>();
        heart.Board = board;
        heart.Renderer = boardRenderer;
        heart.Health = heartGo.GetComponent<Health>();
        heart.PlaceOnTile(HeartTile);

        spawnerGo = new GameObject("TestHeroSpawner");
        spawner = spawnerGo.AddComponent<HeroSpawner>();
        spawner.PlacementManager = manager;
        spawner.Board = board;
        spawner.Renderer = boardRenderer;
        spawner.Heart = heart;
    }

    [TearDown]
    public void TearDown()
    {
        if (spawnerGo != null) Object.DestroyImmediate(spawnerGo);
        if (heartGo != null) Object.DestroyImmediate(heartGo);
        if (managerGo != null) Object.DestroyImmediate(managerGo);
    }

    private Placeable PlaceSite(PlaceableType type, int x, int y)
    {
        Assert.IsTrue(manager.TryPlace(type, new Vector2Int(x, y), PlaceFrom, out Placeable p));
        return p;
    }

    // --- The site ---

    [Test]
    public void Placing_AnyType_StartsAsASite_MaterialsPileShown_FinishedModelHidden()
    {
        int x = 21;
        foreach (PlaceableType type in new[] { PlaceableType.LairCot, PlaceableType.MushroomPlot, PlaceableType.SpikeTrap })
        {
            Placeable site = PlaceSite(type, x++, 13);
            Assert.IsFalse(site.IsBuilt, $"{type}: placement is an order, not a conjuring");
            Assert.AreEqual(0f, site.BuildProgress, $"{type}: nothing built yet");
            Assert.IsNotNull(site.MaterialsPile, $"{type}: a materials pile");
            Assert.IsTrue(site.MaterialsPile.activeInHierarchy, $"{type}: the pile shows");
            Assert.Greater(site.MaterialsPile.GetComponentsInChildren<MeshRenderer>().Length, 2, $"{type}: a pile, not a token");
            Assert.IsNotNull(site.Model, $"{type}: the finished model is built");
            Assert.IsFalse(site.Model.activeSelf, $"{type}: but hidden");

            // The pile sits on its own tile.
            Bounds b = site.MaterialsPile.GetComponentsInChildren<Renderer>()[0].bounds;
            foreach (Renderer r in site.MaterialsPile.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
            Assert.LessOrEqual(b.size.x, 1f, $"{type}: within the tile (x)");
            Assert.LessOrEqual(b.size.z, 1f, $"{type}: within the tile (z)");
            Assert.Less(Mathf.Abs(b.center.x - site.transform.position.x), 0.2f, $"{type}: centered on it");
        }
    }

    [Test]
    public void ASite_OccupiesItsTile_AtOnce()
    {
        PlaceSite(PlaceableType.LairCot, 22, 14);
        Assert.IsTrue(manager.IsOccupied(new Vector2Int(22, 14)));
        Assert.IsFalse(manager.CanPlace(new Vector2Int(22, 14), PlaceFrom), "Placement validity is unchanged: taken");
    }

    [Test]
    public void CompleteConstruction_RemovesThePile_ShowsTheModel_IsIdempotent()
    {
        Placeable p = PlaceSite(PlaceableType.LairCot, 22, 14);
        GameObject model = p.Model;

        p.CompleteConstruction();
        Assert.IsTrue(p.IsBuilt);
        Assert.AreEqual(1f, p.BuildProgress);
        Assert.IsNull(p.MaterialsPile, "Pile gone");
        Assert.IsNull(p.transform.Find("LairCotMaterials"), "Destroyed, not just hidden");
        Assert.IsTrue(p.Model.activeSelf, "Finished model shows");

        Assert.DoesNotThrow(() => p.CompleteConstruction());
        Assert.AreSame(model, p.Model, "Same model, nothing rebuilt");
        Assert.IsTrue(p.IsBuilt);
    }

    [Test]
    public void BuildProgress_IsStorageOnly_ClampedAndNeverCompletes()
    {
        Placeable p = PlaceSite(PlaceableType.MushroomPlot, 22, 14);
        p.BuildProgress = 0.4f;
        Assert.AreEqual(0.4f, p.BuildProgress, 1e-6f);
        p.BuildProgress = 3f;
        Assert.AreEqual(1f, p.BuildProgress, "Clamped");
        Assert.IsFalse(p.IsBuilt, "Only CompleteConstruction builds");
        p.BuildProgress = -1f;
        Assert.AreEqual(0f, p.BuildProgress);

        p.CompleteConstruction();
        p.BuildProgress = 0.2f;
        Assert.AreEqual(1f, p.BuildProgress, "A built placeable stays complete");
    }

    // --- Inertness, type by type ---

    [Test]
    public void CotSite_CannotBeClaimed_OrSelected_UntilBuilt()
    {
        Placeable cot = PlaceSite(PlaceableType.LairCot, 22, 14);
        Assert.IsFalse(cot.TryClaim(), "Nothing can sleep on a site");
        Assert.IsFalse(cot.IsClaimed);
        Assert.IsFalse(GoblinAI.TrySelectCot(board, new List<Placeable> { cot }, PlaceFrom, out _), "Not a sleep target");

        cot.CompleteConstruction();
        Assert.IsTrue(GoblinAI.TrySelectCot(board, new List<Placeable> { cot }, PlaceFrom, out Placeable chosen));
        Assert.AreSame(cot, chosen);
        Assert.IsTrue(cot.TryClaim(), "Works exactly as before once built");
    }

    [Test]
    public void PlotSite_GrowsNothing_HoldsNothing_ThenGrowsFromEmptyOnceBuilt()
    {
        MushroomPlot plot = PlaceSite(PlaceableType.MushroomPlot, 22, 14).GetComponent<MushroomPlot>();
        plot.Tick(60f);
        Assert.AreEqual(0, plot.FoodCount, "No growth on a site");
        Assert.AreEqual(0f, plot.GrowthElapsed, "Not even banked progress");
        Assert.AreEqual(0, plot.VisiblePipCount, "Pips show the (zero) count");
        Assert.IsFalse(plot.TryTakeFood());
        Assert.IsFalse(GoblinAI.TrySelectPlot(board, new List<MushroomPlot> { plot }, PlaceFrom, out _, out _));

        plot.GetComponent<Placeable>().CompleteConstruction();
        Assert.AreEqual(0, plot.FoodCount, "Starts empty");
        plot.Tick(0.99f);
        Assert.AreEqual(0, plot.FoodCount, "A full interval from completion");
        plot.Tick(0.02f);
        Assert.AreEqual(1, plot.FoodCount, "First food after one interval");
        Assert.AreEqual(1, plot.VisiblePipCount);
        Assert.IsTrue(plot.TryTakeFood());
    }

    [Test]
    public void TrapSite_IsUnarmed_Untriggerable_NotRearmable_ShowsNoSpikes()
    {
        SpikeTrap trap = PlaceSite(PlaceableType.SpikeTrap, 22, 14).GetComponent<SpikeTrap>();
        Assert.IsFalse(trap.IsArmed);
        Assert.IsFalse(trap.SpikesVisible);
        Assert.IsFalse(trap.Spikes.gameObject.activeSelf, "No spikes at all, not even retracted ones");
        Assert.IsFalse(trap.TryTrigger(out int damage));
        Assert.AreEqual(0, damage);
        trap.Rearm();
        Assert.IsFalse(trap.IsArmed, "Rearm can't finish a trap: only construction can");

        trap.GetComponent<Placeable>().CompleteConstruction();
        Assert.IsTrue(trap.IsArmed, "Built: armed");
        Assert.IsTrue(trap.SpikesVisible);
        Assert.IsTrue(trap.Spikes.gameObject.activeSelf);
        Assert.IsTrue(trap.TryTrigger(out damage));
        Assert.AreEqual(manager.SpikeDamage, damage);
        trap.Rearm();
        Assert.IsTrue(trap.IsArmed, "Rearms as before");
    }

    [Test]
    public void RearmSelection_NeverPicksAnUnbuiltTrap_ItIsNotASpentTrap()
    {
        SpikeTrap site = PlaceSite(PlaceableType.SpikeTrap, 23, 16).GetComponent<SpikeTrap>(); // Right next to the imp
        List<SpikeTrap> everything = manager.GetSpikeTraps(); // Sites included, deliberately
        Assert.IsFalse(ImpDigger.TrySelectWork(board, everything, PlaceFrom, out _, out _, out SpikeTrap none), "No work at all");
        Assert.IsNull(none);

        Assert.IsTrue(manager.TryPlaceBuilt(PlaceableType.SpikeTrap, new Vector2Int(21, 13), PlaceFrom, out Placeable farP));
        SpikeTrap far = farP.GetComponent<SpikeTrap>();
        far.TryTrigger(out _); // Spent: real rearm work, farther away than the site
        Assert.IsTrue(ImpDigger.TrySelectWork(board, manager.GetSpikeTraps(), PlaceFrom, out _, out _, out SpikeTrap chosen));
        Assert.AreSame(far, chosen, "The spent built trap, never the nearer site");
        Assert.AreNotSame(site, chosen);
    }

    // --- Built-only counting ---

    [Test]
    public void BuiltOnlyQueries_ExcludeSites_TheAllQueriesIncludeThem()
    {
        Assert.IsTrue(manager.TryPlaceBuilt(PlaceableType.LairCot, new Vector2Int(21, 13), PlaceFrom, out Placeable builtCot));
        PlaceSite(PlaceableType.LairCot, 22, 13);
        Assert.IsTrue(manager.TryPlaceBuilt(PlaceableType.MushroomPlot, new Vector2Int(23, 13), PlaceFrom, out Placeable builtPlot));
        Placeable sitePlot = PlaceSite(PlaceableType.MushroomPlot, 25, 13);
        Assert.IsTrue(manager.TryPlaceBuilt(PlaceableType.SpikeTrap, new Vector2Int(21, 17), PlaceFrom, out Placeable builtTrap));
        PlaceSite(PlaceableType.SpikeTrap, 22, 17);

        CollectionAssert.AreEqual(new[] { builtCot }, manager.GetBuiltCots());
        CollectionAssert.AreEqual(new[] { builtPlot.GetComponent<MushroomPlot>() }, manager.GetBuiltMushroomPlots());
        CollectionAssert.AreEqual(new[] { builtTrap.GetComponent<SpikeTrap>() }, manager.GetBuiltSpikeTraps());
        Assert.AreEqual(1, manager.CountBuilt(PlaceableType.LairCot));
        Assert.AreEqual(2, manager.CountOfType(PlaceableType.LairCot), "Sites still occupy and are still listed");
        Assert.AreEqual(2, manager.GetCots().Count);
        Assert.AreEqual(2, manager.GetMushroomPlots().Count);
        Assert.AreEqual(2, manager.GetSpikeTraps().Count);

        builtPlot.GetComponent<MushroomPlot>().Tick(3f);
        sitePlot.GetComponent<MushroomPlot>().Tick(3f);
        Assert.AreEqual(3, manager.TotalFood, "Food totals count built plots only");
    }

    [Test]
    public void HeroTrigger_TwoBuiltCotsAndACotSite_NotMet_CompletingTheSiteMeetsIt()
    {
        Assert.IsTrue(manager.TryPlaceBuilt(PlaceableType.LairCot, new Vector2Int(21, 13), PlaceFrom, out _));
        Assert.IsTrue(manager.TryPlaceBuilt(PlaceableType.LairCot, new Vector2Int(22, 13), PlaceFrom, out _));
        Placeable third = PlaceSite(PlaceableType.LairCot, 23, 13);
        Assert.IsTrue(manager.TryPlaceBuilt(PlaceableType.MushroomPlot, new Vector2Int(21, 18), PlaceFrom, out _));
        Assert.IsTrue(manager.TryPlaceBuilt(PlaceableType.MushroomPlot, new Vector2Int(22, 18), PlaceFrom, out _));

        Assert.IsFalse(spawner.BuildingsTriggerMet(), "An unbuilt third cot doesn't advance the trigger");
        third.CompleteConstruction();
        Assert.IsTrue(spawner.BuildingsTriggerMet(), "Built: three cots and two plots");
    }

    [Test]
    public void HeroTrigger_PlotSitesDontCountEither()
    {
        for (int x = 21; x <= 23; x++) Assert.IsTrue(manager.TryPlaceBuilt(PlaceableType.LairCot, new Vector2Int(x, 13), PlaceFrom, out _));
        Assert.IsTrue(manager.TryPlaceBuilt(PlaceableType.MushroomPlot, new Vector2Int(21, 18), PlaceFrom, out _));
        Placeable plotSite = PlaceSite(PlaceableType.MushroomPlot, 22, 18);

        Assert.IsFalse(spawner.BuildingsTriggerMet());
        plotSite.CompleteConstruction();
        Assert.IsTrue(spawner.BuildingsTriggerMet());
    }
}
