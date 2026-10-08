using System;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Digging yields stone (#103): a dig the imp completes pays the tile's yield into the stockpile, once, at the
/// conversion. Nothing else pays: abandoned digs, rearms, builds, and board changes from anywhere else.
/// </summary>
public class DigYieldTests
{
    private const float Dt = 0.02f;
    private static readonly Vector2Int ImpTile = new Vector2Int(24, 16);

    private GameObject managerGo;
    private GameObject impGo;
    private DungeonBoard board;
    private BoardRenderer boardRenderer;
    private PlacementManager manager;
    private Stockpile stockpile;
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
        stockpile = managerGo.AddComponent<Stockpile>(); // 10 stone to start

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
        digger.Stockpile = stockpile; // Wired explicitly (the scene wires the DungeonManager's)
        digger.DigSecondsPerTile = 1f;
        digger.RearmSecondsPerTrap = 1f;
    }

    [TearDown]
    public void TearDown()
    {
        if (impGo != null) Object.DestroyImmediate(impGo);
        if (managerGo != null) Object.DestroyImmediate(managerGo);
    }

    private int Stone => stockpile.Count(MaterialType.Stone);

    private void Designate(int x, int y) => board.SetTile(x, y, TileState.Designated);

    private void Step()
    {
        imp.Advance(Dt);
        digger.Tick(Dt);
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

    private bool IsIdle() => digger.CurrentTarget == null && digger.CurrentTrap == null && digger.CurrentSite == null && !imp.IsMoving;

    [Test]
    public void Defaults_OneStonePerTile_Stone()
    {
        Assert.AreEqual(1, digger.StoneYieldPerTile, "Serialized default (SetUp doesn't change it)");
        Assert.IsTrue(digger.TryGetTileYield(new Vector2Int(27, 16), out MaterialType material, out int amount));
        Assert.AreEqual(MaterialType.Stone, material);
        Assert.AreEqual(1, amount);
    }

    [Test]
    public void ACompletedDig_PaysExactlyOnce_AtTheConversion()
    {
        Designate(27, 16);
        Assert.AreEqual(10, Stone);

        StepUntil(() => digger.IsDigging, 5f, "imp to start digging");
        for (int i = 0; i < 45; i++) Step(); // 0.9 s of a 1 s dig
        Assert.AreEqual(TileState.Designated, board.GetTile(27, 16));
        Assert.AreEqual(10, Stone, "No partial-dig yield");

        StepUntil(() => board.GetTile(27, 16) == TileState.Floor, 1f, "the dig to complete");
        Assert.AreEqual(11, Stone, "Paid at the conversion");

        for (int i = 0; i < 100; i++) Step();
        Assert.IsTrue(IsIdle());
        Assert.AreEqual(11, Stone, "Exactly once");
    }

    [Test]
    public void NCompletedDigs_PayNTimesTheYield_AndAConfiguredYieldPaysThatMuch()
    {
        digger.StoneYieldPerTile = 3;
        Designate(27, 15);
        Designate(27, 16);
        Designate(27, 17);
        StepUntil(() => board.GetTile(27, 15) == TileState.Floor && board.GetTile(27, 16) == TileState.Floor
                        && board.GetTile(27, 17) == TileState.Floor, 20f, "all three digs");
        Assert.AreEqual(10 + 3 * 3, Stone, "3 tiles x 3 stone");
    }

    [Test]
    public void AnAbandonedDig_PaysNothing()
    {
        Designate(27, 16);
        StepUntil(() => digger.IsDigging, 5f, "imp to start digging");
        for (int i = 0; i < 25; i++) Step(); // Halfway

        board.SetTile(27, 16, TileState.Rock); // The player clears the designation mid-dig
        for (int i = 0; i < 100; i++) Step();
        Assert.AreEqual(TileState.Rock, board.GetTile(27, 16), "Never converted");
        Assert.IsTrue(IsIdle(), "Job dropped");
        Assert.AreEqual(10, Stone, "Nothing paid");
    }

    [Test]
    public void RearmsAndBuilds_PayNothing()
    {
        // A build: a cot site, built to completion.
        manager.LairCotBuildSeconds = 1f;
        Assert.IsTrue(manager.TryPlace(PlaceableType.LairCot, new Vector2Int(22, 14), ImpTile, out Placeable cot));
        StepUntil(() => cot.IsBuilt, 10f, "the cot to be built");
        Assert.AreEqual(10, Stone, "A build pays nothing");

        // A rearm: a spent built trap, rearmed.
        Assert.IsTrue(manager.TryPlaceBuilt(PlaceableType.SpikeTrap, new Vector2Int(26, 18), ImpTile, out Placeable trapP));
        SpikeTrap trap = trapP.GetComponent<SpikeTrap>();
        Assert.IsTrue(trap.TryTrigger(out _));
        StepUntil(() => trap.IsArmed, 10f, "the trap to be rearmed");
        Assert.AreEqual(10, Stone, "A rearm pays nothing");
    }

    [Test]
    public void BoardChangesFromAnywhereElse_PayNothing()
    {
        board.SetTile(27, 16, TileState.Floor); // Staging / setup / future systems: not an imp-completed dig
        board.InitializeBoard();
        for (int i = 0; i < 20; i++) Step();
        Assert.AreEqual(10, Stone, "Only an imp-completed dig earns");
    }

    [Test]
    public void UnwiredStockpile_TheDigStillConverts_NothingThrows()
    {
        digger.Stockpile = null;
        Designate(27, 16);
        Assert.DoesNotThrow(() => StepUntil(() => board.GetTile(27, 16) == TileState.Floor, 5f, "the dig to complete"));
        Assert.AreEqual(10, Stone, "Nothing paid anywhere");
    }

    [Test]
    public void ZeroYield_PaysNothing()
    {
        digger.StoneYieldPerTile = 0;
        Assert.IsFalse(digger.TryGetTileYield(new Vector2Int(27, 16), out _, out _));
        Designate(27, 16);
        StepUntil(() => board.GetTile(27, 16) == TileState.Floor, 5f, "the dig to complete");
        Assert.AreEqual(10, Stone);
    }
}
