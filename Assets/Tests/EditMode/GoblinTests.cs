using NUnit.Framework;
using UnityEngine;

public class GoblinTests
{
    private const float Dt = 0.02f;

    private GameObject managerGo;
    private GameObject goblinGo;
    private DungeonBoard board;
    private BoardRenderer boardRenderer;
    private Goblin goblin;

    [SetUp]
    public void SetUp()
    {
        // DungeonBoard and BoardRenderer on the same object, matching the real scene layout.
        // Default board: 48x32 Rock with the 6x6 Floor cavern at (21,13)-(26,18).
        managerGo = new GameObject("TestDungeonManager");
        board = managerGo.AddComponent<DungeonBoard>();
        board.InitializeBoard();
        boardRenderer = managerGo.AddComponent<BoardRenderer>();
        boardRenderer.Board = board;

        goblinGo = new GameObject("TestGoblin");
        goblin = goblinGo.AddComponent<Goblin>();
        // Start() is not called in EditMode tests, so wire and place explicitly.
        goblin.Board = board;
        goblin.Renderer = boardRenderer;
        goblin.MoveSpeed = 3f;
        goblin.PlaceOnTile(new Vector2Int(23, 15));
    }

    [TearDown]
    public void TearDown()
    {
        if (goblinGo != null) Object.DestroyImmediate(goblinGo);
        if (managerGo != null) Object.DestroyImmediate(managerGo);
    }

    private Vector3 Center(int x, int y) => boardRenderer.GetTileCenterWorldPosition(x, y);

    /// <summary>Steps Advance until the goblin stops; returns the stepped seconds.</summary>
    private float AdvanceUntilIdle(int maxSteps = 2000)
    {
        float elapsed = 0f;
        for (int i = 0; i < maxSteps && goblin.IsMoving; i++)
        {
            goblin.Advance(Dt);
            elapsed += Dt;
        }
        Assert.IsFalse(goblin.IsMoving, "Goblin never finished its path");
        return elapsed;
    }

    // --- Placement and movement ---

    [Test]
    public void PlaceOnTile_SetsTileAndWorldPosition()
    {
        goblin.PlaceOnTile(new Vector2Int(25, 17));

        Assert.AreEqual(new Vector2Int(25, 17), goblin.CurrentTile);
        Assert.AreEqual(new Vector2Int(25, 17), goblin.NextTile);
        Assert.AreEqual(Center(25, 17), goblinGo.transform.position);
        Assert.IsFalse(goblin.IsMoving);
    }

    [Test]
    public void Spawn_PlacesOnSpawnTile()
    {
        goblin.SpawnTile = new Vector2Int(24, 17);
        goblin.Spawn();

        Assert.AreEqual(new Vector2Int(24, 17), goblin.CurrentTile);
        Assert.AreEqual(Center(24, 17), goblinGo.transform.position);
    }

    [Test]
    public void SetDestination_Reachable_ArrivesExactlyOnCenter()
    {
        Assert.IsTrue(goblin.SetDestination(new Vector2Int(26, 18)));
        Assert.IsTrue(goblin.IsMoving);

        AdvanceUntilIdle();
        Assert.AreEqual(new Vector2Int(26, 18), goblin.CurrentTile);
        Assert.AreEqual(Center(26, 18), goblinGo.transform.position);
    }

    [Test]
    public void Advance_MovesAtTilesPerSecond_CurrentTileFlipsAtMidpoint()
    {
        Assert.IsTrue(goblin.SetDestination(new Vector2Int(26, 15)));

        goblin.Advance(0.4f / 3f); // 0.4 tiles: still in (23,15)'s cell
        Assert.AreEqual(new Vector2Int(23, 15), goblin.CurrentTile);
        goblin.Advance(0.2f / 3f); // 0.6 tiles: now in (24,15)'s cell
        Assert.AreEqual(new Vector2Int(24, 15), goblin.CurrentTile);
        goblin.Advance(0.4f / 3f); // 1.0 tile total at 3 tiles/s
        Assert.That(Vector3.Distance(Center(24, 15), goblinGo.transform.position), Is.LessThan(1e-4f));
    }

    [Test]
    public void SetDestination_RockTile_StaysPut()
    {
        Assert.IsFalse(goblin.SetDestination(new Vector2Int(0, 0)));
        goblin.Advance(1f);

        Assert.IsFalse(goblin.IsMoving);
        Assert.AreEqual(new Vector2Int(23, 15), goblin.CurrentTile);
        Assert.AreEqual(Center(23, 15), goblinGo.transform.position);
    }

    [Test]
    public void SetDestination_EnclosedFloor_StaysPut()
    {
        board.SetTile(5, 5, TileState.Floor); // isolated pocket surrounded by Rock

        Assert.IsFalse(goblin.SetDestination(new Vector2Int(5, 5)));
        goblin.Advance(1f);

        Assert.IsFalse(goblin.IsMoving);
        Assert.AreEqual(Center(23, 15), goblinGo.transform.position);
    }

    // --- Needs ---

    [Test]
    public void Needs_StartFull()
    {
        Assert.AreEqual(100f, goblin.Hunger);
        Assert.AreEqual(100f, goblin.Energy);
        Assert.IsFalse(goblin.IsWeakened);
        Assert.IsFalse(goblin.IsDead);
    }

    [Test]
    public void Tick_DecaysAtDesignRates()
    {
        goblin.Tick(60f);

        Assert.AreEqual(75f, goblin.Hunger, 1e-3f, "100 over 240 s");
        Assert.AreEqual(80f, goblin.Energy, 1e-3f, "100 over 300 s");
    }

    [Test]
    public void Tick_LongStep_ClampsAtZero()
    {
        goblin.Tick(1000f);

        Assert.AreEqual(0f, goblin.Hunger);
        Assert.AreEqual(0f, goblin.Energy);
    }

    [Test]
    public void Needs_SetterClampsToRange()
    {
        goblin.Hunger = 150f;
        goblin.Energy = -20f;

        Assert.AreEqual(100f, goblin.Hunger);
        Assert.AreEqual(0f, goblin.Energy);
    }

    // --- Weakened ---

    [Test]
    public void Weakened_OnlyAtHungerZero_HalvesMoveSpeed()
    {
        goblin.Hunger = 0.5f;
        Assert.IsFalse(goblin.IsWeakened);
        Assert.AreEqual(3f, goblin.EffectiveMoveSpeed);

        goblin.Hunger = 0f;
        Assert.IsTrue(goblin.IsWeakened);
        Assert.AreEqual(1.5f, goblin.EffectiveMoveSpeed);
    }

    [Test]
    public void WeakenedMoveSpeedMultiplier_IsTunable()
    {
        Assert.AreEqual(0.5f, goblin.WeakenedMoveSpeedMultiplier, "DESIGN §A.5 default");
        goblin.WeakenedMoveSpeedMultiplier = 0.25f;
        goblin.Hunger = 0f;
        Assert.AreEqual(0.75f, goblin.EffectiveMoveSpeed);
    }

    [Test]
    public void StartingNeeds_DefaultFull()
    {
        Assert.AreEqual(100f, goblin.StartingHunger);
        Assert.AreEqual(100f, goblin.StartingEnergy);
    }

    [Test]
    public void Weakened_TravelTakesTwiceAsLong()
    {
        Assert.IsTrue(goblin.SetDestination(new Vector2Int(26, 15))); // 3 tiles straight east
        float fed = AdvanceUntilIdle();

        goblin.PlaceOnTile(new Vector2Int(23, 15));
        goblin.Hunger = 0f;
        Assert.IsTrue(goblin.SetDestination(new Vector2Int(26, 15)));
        float starving = AdvanceUntilIdle();

        Assert.AreEqual(1f, fed, Dt + 1e-4f, "3 tiles at 3 tiles/s");
        Assert.AreEqual(2f * fed, starving, Dt + 1e-4f);
    }

    // --- Starvation ---

    [Test]
    public void Starvation_DiesAfterSixtySecondsAtZero()
    {
        goblin.Hunger = 0f;

        goblin.Tick(59.9f);
        Assert.IsFalse(goblin.IsDead);
        Assert.IsTrue(goblinGo.activeSelf);
        Assert.AreEqual(59.9f, goblin.StarvationElapsed, 1e-3f);

        goblin.Tick(0.1f);
        Assert.IsTrue(goblin.IsDead);
        Assert.IsFalse(goblinGo.activeSelf, "Death deactivates the GameObject");
    }

    [Test]
    public void Starvation_TimerResetsWhenFed_PeriodsDoNotSum()
    {
        goblin.Hunger = 0f;
        goblin.Tick(30f);
        Assert.AreEqual(30f, goblin.StarvationElapsed, 1e-3f);

        goblin.Hunger = 50f;
        goblin.Tick(0.01f);
        Assert.AreEqual(0f, goblin.StarvationElapsed, "Fed: timer resets");

        goblin.Hunger = 0f;
        goblin.Tick(59.9f);
        Assert.IsFalse(goblin.IsDead, "30 s + 59.9 s must not sum to a death");

        goblin.Tick(0.2f);
        Assert.IsTrue(goblin.IsDead);
    }

    [Test]
    public void Starvation_NotStartedWhileHungerAboveZero()
    {
        goblin.Hunger = 1f;
        goblin.Tick(0.1f);

        Assert.Greater(goblin.Hunger, 0f);
        Assert.AreEqual(0f, goblin.StarvationElapsed);
    }

    // --- Debug stats line ---

    [Test]
    public void StatsLine_ReportsNeedsAndStates()
    {
        goblin.Tick(60f);
        StringAssert.Contains("tile=(23, 15) hunger=75.0 energy=80.0 speed=3.0", goblin.StatsLine());
        StringAssert.DoesNotContain("WEAKENED", goblin.StatsLine());

        goblin.Hunger = 0f;
        goblin.Tick(10f);
        StringAssert.Contains("speed=1.5", goblin.StatsLine());
        StringAssert.Contains("WEAKENED starving=10.0/60s", goblin.StatsLine());
    }

    // --- Dead ---

    [Test]
    public void Dead_IgnoresOrders_AndStopsChanging()
    {
        goblin.Hunger = 0f;
        goblin.Tick(60f);
        Assert.IsTrue(goblin.IsDead);
        float energy = goblin.Energy;
        Vector3 pos = goblinGo.transform.position;

        Assert.IsFalse(goblin.SetDestination(new Vector2Int(26, 15)));
        goblin.Advance(1f);
        goblin.Tick(30f);

        Assert.IsFalse(goblin.IsMoving);
        Assert.AreEqual(pos, goblinGo.transform.position);
        Assert.AreEqual(new Vector2Int(23, 15), goblin.CurrentTile);
        Assert.AreEqual(energy, goblin.Energy, "Needs freeze once dead");
        Assert.AreEqual(0f, goblin.Hunger);
    }

    [Test]
    public void Dead_MidMove_StopsWhereItIs()
    {
        Assert.IsTrue(goblin.SetDestination(new Vector2Int(26, 15)));
        goblin.Hunger = 0f;
        goblin.StarvationSecondsToDie = 0.1f;
        goblin.Tick(0.1f); // moves 0.15 tiles, then dies

        Assert.IsTrue(goblin.IsDead);
        Assert.IsFalse(goblin.IsMoving, "Orders cleared on death");
        Vector3 pos = goblinGo.transform.position;
        goblin.Advance(1f);
        Assert.AreEqual(pos, goblinGo.transform.position);
    }
}
