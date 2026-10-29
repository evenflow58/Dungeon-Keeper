using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

/// <summary>
/// #157: the Warren placeable and goblin leashing — the radii (leashed Chebyshev from the warren, unleashed Manhattan
/// from the goblin), the engage / chase / break / return transitions, the leashed wander bounds and the unleashed ranges.
/// </summary>
public class WarrenLeashTests
{
    private static readonly Vector2Int W = new Vector2Int(24, 16);     // The warren in most tests
    private static readonly Vector2Int PlaceFrom = new Vector2Int(24, 14);
    private const float Dt = 0.02f;
    private const float Frozen = float.PositiveInfinity;

    private GameObject managerGo;
    private DungeonBoard board;
    private BoardRenderer boardRenderer;
    private PlacementManager manager;
    private readonly List<GameObject> spawned = new List<GameObject>();
    private readonly List<Goblin> bodies = new List<Goblin>();
    private readonly List<GoblinAI> ais = new List<GoblinAI>();

    [SetUp]
    public void SetUp()
    {
        managerGo = new GameObject("TestDungeonManager");
        board = managerGo.AddComponent<DungeonBoard>();
        board.InitializeBoard();
        // An open hall around the starter cavern, wider than the leash: x 8..40, y 4..28.
        for (int x = 8; x <= 40; x++)
            for (int y = 4; y <= 28; y++)
                board.SetTile(x, y, TileState.Floor);
        boardRenderer = managerGo.AddComponent<BoardRenderer>();
        boardRenderer.Board = board;
        manager = managerGo.AddComponent<PlacementManager>();
        manager.Board = board;
        manager.Renderer = boardRenderer;
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

    // ---- staging ----

    private Placeable PlaceWarren(Vector2Int tile)
    {
        Assert.IsTrue(manager.TryPlaceBuilt(PlaceableType.Warren, tile, PlaceFrom, out Placeable p), $"Warren at {tile}");
        return p;
    }

    private Goblin AddBody(string name, Vector2Int tile, HealthTeam team, int maxHealth)
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
        goblin.PlaceOnTile(tile);

        var health = go.AddComponent<Health>();
        health.Team = team;
        health.MaxHealth = maxHealth;
        goblin.Health = health;
        bodies.Add(goblin);
        return goblin;
    }

    /// <summary>A Monster-team goblin with an AI; its wander pauses are frozen, so it stands still unless a goal moves it.</summary>
    private (Goblin goblin, GoblinAI ai) AddFighter(Vector2Int tile, Placeable warren = null)
    {
        Goblin goblin = AddBody("Fighter" + ais.Count, tile, HealthTeam.Monster, 20);
        var ai = goblin.gameObject.AddComponent<GoblinAI>();
        ai.Goblin = goblin;
        ai.PlacementManager = manager;
        ai.Rng = new System.Random(1);
        ai.WanderPauseSeconds = Frozen;
        ai.AttackIntervalSeconds = 1f;
        ai.AttackDamage = 1;
        if (warren != null) Assert.IsTrue(ai.SetLeash(warren));
        ais.Add(ai);
        return (goblin, ai);
    }

    /// <summary>A goblin-bodied Hero-team dummy with no AI: it stands where it's put.</summary>
    private Health AddHero(Vector2Int tile, int maxHealth = 100) =>
        AddBody("Hero" + bodies.Count, tile, HealthTeam.Hero, maxHealth).Health;

    private static void MoveTo(Health body, Vector2Int tile) => body.GetComponent<Goblin>().PlaceOnTile(tile);

    private void Step()
    {
        foreach (GoblinAI ai in ais)
        {
            ai.Goblin.Tick(Dt);
            ai.Tick(Dt);
        }
        foreach (Goblin body in bodies)
        {
            if (body.GetComponent<GoblinAI>() == null) body.Tick(Dt);
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

    private void StepUntil(Func<bool> condition, float maxSeconds, string because, Action afterEachStep = null)
    {
        float elapsed = 0f;
        while (!condition())
        {
            Assert.Less(elapsed, maxSeconds, "Timed out waiting: " + because);
            Step();
            afterEachStep?.Invoke();
            elapsed += Dt;
        }
    }

    private static int Manhattan(Vector2Int a, Vector2Int b) => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);

    private void Enclose(Vector2Int t)
    {
        board.SetTile(t.x + 1, t.y, TileState.Rock);
        board.SetTile(t.x - 1, t.y, TileState.Rock);
        board.SetTile(t.x, t.y + 1, TileState.Rock);
        board.SetTile(t.x, t.y - 1, TileState.Rock);
    }

    // ---- the warren ----

    [Test]
    public void Warren_ShippedData_Cost6InEveryMaterial_Build8s_InLiving()
    {
        foreach (MaterialType material in new[] { MaterialType.Stone, MaterialType.Glimmerstone, MaterialType.Deepstone })
        {
            Assert.IsTrue(manager.TryGetCost(PlaceableType.Warren, material, out MaterialAmount cost), $"{material}");
            Assert.AreEqual(material, cost.type);
            Assert.AreEqual(6, cost.amount);
        }
        Assert.AreEqual(36, manager.Costs.Count, "One row per material: 27 → 30 (#165: the wall and the door, → 36)");
        Assert.AreEqual(8f, manager.BuildSecondsFor(PlaceableType.Warren), 1e-6f);
        Assert.IsTrue(BuildPresentation.TryGet(PlaceableType.Warren, out BuildPresentation.Entry entry));
        Assert.AreEqual(BuildCategory.Living, entry.category, "Beside the cot");
        CollectionAssert.IsEmpty(entry.stats, "Standard building info only");
        Assert.AreEqual("Warren", BuildPresentation.DisplayName(PlaceableType.Warren));
        Assert.AreEqual(9, (int)PlaceableType.Warren, "Appended: every earlier value keeps its number");
    }

    [Test]
    public void Warren_Placed_HasTheDenModel_NoHealth_AndGoesSeeThroughInOverlays()
    {
        Placeable warren = PlaceWarren(W);
        Assert.IsTrue(warren.IsBuilt);
        Assert.IsNotNull(warren.Model);
        Assert.IsNotNull(warren.Model.transform.Find("Mound"), "The den's mound");
        Assert.IsNotNull(warren.Model.transform.Find("Door"), "Its doorway");
        Assert.IsFalse(warren.TryGetComponent(out Health _), "Nothing to attack: heroes can't target it");
        Assert.IsTrue(board.IsWalkable(W.x, W.y), "Placement leaves the tile Floor, as for every placeable");

        foreach (ViewMode mode in new[] { ViewMode.Power, ViewMode.Magic })
        {
            boardRenderer.SetViewMode(mode);
            manager.RefreshSeeThrough();
            Assert.IsTrue(warren.IsSeeThrough, $"{mode}: the standard non-node, non-mana rule");
        }
        boardRenderer.SetViewMode(ViewMode.Normal);
        manager.RefreshSeeThrough();
        Assert.IsFalse(warren.IsSeeThrough, "Solid in Normal view");
    }

    [Test]
    public void Heroes_NeverTargetAWarren()
    {
        Placeable warren = PlaceWarren(W);
        var route = new List<Vector2Int> { new Vector2Int(23, 16), W, new Vector2Int(25, 16) };
        Assert.IsNull(HeroAI.FindBlocker(new Vector2Int(23, 16), route, Object.FindObjectsByType<Health>()),
            "A warren on the route, beside the hero, is never a blocker");
        Assert.IsNotNull(warren);
    }

    // ---- leash state ----

    [Test]
    public void Defaults_Leashed4And8_Unleashed2And2_AllTunable()
    {
        var (_, ai) = AddFighter(W + Vector2Int.right);
        Assert.AreEqual(4, ai.LeashedDetectRadius);
        Assert.AreEqual(8, ai.LeashRadius);
        Assert.AreEqual(2, ai.UnleashedDetectRange, "Down from today's 4");
        Assert.AreEqual(2, ai.UnleashedDisengageRange, "Down from today's 4");
        Assert.IsFalse(ai.IsLeashed, "Unassigned by default");
        Assert.IsNull(ai.Warren);
    }

    [Test]
    public void SetLeash_Twice_KeepsOnlyTheLatest_RejectsNonWarrens_ClearUnleashes()
    {
        Placeable a = PlaceWarren(W);
        Placeable b = PlaceWarren(new Vector2Int(36, 16));
        Assert.IsTrue(manager.TryPlaceBuilt(PlaceableType.LairCot, new Vector2Int(30, 10), PlaceFrom, out Placeable cot));
        var (_, ai) = AddFighter(new Vector2Int(30, 16));

        Assert.IsTrue(ai.SetLeash(a));
        Assert.IsTrue(ai.SetLeash(b));
        Assert.AreSame(b, ai.Warren);
        Assert.IsFalse(ai.SetLeash(cot), "A cot isn't a warren");
        Assert.IsFalse(ai.SetLeash(null));
        Assert.AreSame(b, ai.Warren, "A rejected assignment changes nothing");

        // Only b's zone counts now: a hero in a's zone (2 from a, 12 from b) is ignored, one in b's zone is fought.
        Health nearA = AddHero(new Vector2Int(26, 16));
        StepFor(1f, () => Assert.AreNotEqual(GoblinAI.Goal.Fight, ai.CurrentGoal));
        Health nearB = AddHero(new Vector2Int(34, 16));
        StepUntil(() => ai.CurrentGoal == GoblinAI.Goal.Fight, 0.3f, "engaging in b's zone");
        Assert.AreSame(nearB, ai.CurrentTarget);
        Assert.AreEqual(100, nearA.CurrentHealth);

        ai.ClearLeash();
        Assert.IsFalse(ai.IsLeashed);
        Assert.IsNull(ai.Warren);
    }

    // ---- radii ----

    [Test]
    public void Chebyshev_DiagonalAt4IsIn_At5IsOut()
    {
        Assert.AreEqual(4, GoblinAI.Chebyshev(W, W + new Vector2Int(4, 4)));
        Assert.AreEqual(5, GoblinAI.Chebyshev(W, W + new Vector2Int(5, 4)));
        Assert.AreEqual(5, GoblinAI.Chebyshev(W, W + new Vector2Int(-5, -5)));
    }

    [Test]
    public void LeashedSelection_DetectZoneIsAChebyshevSquare_AroundTheWarren()
    {
        Vector2Int goblinTile = W + new Vector2Int(-1, 0);
        Health corner = AddHero(W + new Vector2Int(4, 4));   // Diagonal 4: in (Manhattan 8 — a diamond would miss it)
        Health past = AddHero(W + new Vector2Int(5, 4));     // 5: out
        Health far = AddHero(W + new Vector2Int(-4, -5));    // 5: out

        Assert.AreSame(corner, GoblinAI.TrySelectLeashedTarget(board, goblinTile, HealthTeam.Monster, new[] { corner }, W, 4));
        Assert.IsNull(GoblinAI.TrySelectLeashedTarget(board, goblinTile, HealthTeam.Monster, new[] { past, far }, W, 4));
    }

    [Test]
    public void LeashedSelection_NearestToTheGoblinWins_TiesKeepOrder_UnreachableSkipped()
    {
        Vector2Int goblinTile = W + new Vector2Int(3, 0);
        Health nearGoblin = AddHero(W + new Vector2Int(4, 1));   // 2 from the goblin
        Health nearWarren = AddHero(W + new Vector2Int(-1, 0));  // 4 from the goblin, beside the warren
        Assert.AreSame(nearGoblin, GoblinAI.TrySelectLeashedTarget(board, goblinTile, HealthTeam.Monster,
            new[] { nearWarren, nearGoblin }, W, 4), "Among candidates, nearer to the goblin wins, as today");

        Health tieA = AddHero(W + new Vector2Int(3, 2));  // 2 from the goblin
        Health tieB = AddHero(W + new Vector2Int(3, -2)); // 2 from the goblin
        Assert.AreSame(tieB, GoblinAI.TrySelectLeashedTarget(board, goblinTile, HealthTeam.Monster, new[] { tieB, tieA }, W, 4));

        Enclose(nearGoblin.GetComponent<Goblin>().CurrentTile);
        Assert.AreSame(nearWarren, GoblinAI.TrySelectLeashedTarget(board, goblinTile, HealthTeam.Monster,
            new[] { nearGoblin, nearWarren }, W, 4), "The walled-in one is skipped");
    }

    // ---- leashed combat ----

    [Test]
    public void Leashed_IgnoresAHeroOutsideDetect_EvenWhenAdjacentToTheGoblin()
    {
        // This must fail on goblin-radius logic: the hero stands right beside the goblin (Manhattan 1).
        Placeable warren = PlaceWarren(W);
        var (_, ai) = AddFighter(W + new Vector2Int(7, 0), warren);
        Health hero = AddHero(W + new Vector2Int(8, 0)); // 8 from the warren

        StepFor(3f, () => Assert.AreNotEqual(GoblinAI.Goal.Fight, ai.CurrentGoal));
        Assert.AreEqual(100, hero.CurrentHealth);
    }

    [Test]
    public void Leashed_EngagesAHeroThatEntersDetect()
    {
        Placeable warren = PlaceWarren(W);
        var (_, ai) = AddFighter(W + Vector2Int.left, warren);
        Health hero = AddHero(W + new Vector2Int(5, 3)); // 5: outside

        StepFor(1f, () => Assert.AreNotEqual(GoblinAI.Goal.Fight, ai.CurrentGoal));
        MoveTo(hero, W + new Vector2Int(4, 3));         // 4: inside
        StepUntil(() => ai.CurrentGoal == GoblinAI.Goal.Fight, 0.3f, "engaging on the next poll");
        Assert.AreSame(hero, ai.CurrentTarget);
        StepUntil(() => hero.CurrentHealth < 100, 4f, "the first hit");
    }

    [Test]
    public void Leashed_FightsOnPastDetect_UpToTheLeash_ThenBreaksAndWalksHome()
    {
        Placeable warren = PlaceWarren(W);
        var (goblin, ai) = AddFighter(W + Vector2Int.right, warren);
        Health hero = AddHero(W + new Vector2Int(4, 0)); // 4: inside detect
        StepUntil(() => hero.CurrentHealth < 100, 3f, "the first hit");

        // Out of detect (7) but inside the leash: the fight goes on, and the goblin follows.
        MoveTo(hero, W + new Vector2Int(7, 0));
        int hp = hero.CurrentHealth;
        StepFor(4f, () => Assert.AreEqual(GoblinAI.Goal.Fight, ai.CurrentGoal, "No break at the detect boundary"));
        Assert.Less(hero.CurrentHealth, hp, "Pursued and hit beyond detect");
        Assert.Greater(GoblinAI.Chebyshev(W, goblin.CurrentTile), 4, "The goblin itself is out of its zone now");

        // Exactly 8 is still inside the leash.
        MoveTo(hero, W + new Vector2Int(8, 0));
        StepFor(1f, () => Assert.AreEqual(GoblinAI.Goal.Fight, ai.CurrentGoal, "8 is inside the leash"));

        // Past the leash (9): disengage and head home.
        MoveTo(hero, W + new Vector2Int(9, 0));
        StepUntil(() => ai.CurrentGoal != GoblinAI.Goal.Fight, 0.3f, "the break on the next poll");
        Assert.AreEqual(GoblinAI.Goal.Return, ai.CurrentGoal);
        Assert.IsNull(ai.CurrentTarget);
        Assert.AreEqual(W + Vector2Int.right, ai.ReturnTile, "The nearest tile beside the warren");

        hp = hero.CurrentHealth;
        StepUntil(() => ai.CurrentGoal != GoblinAI.Goal.Return, 5f, "the walk home",
            () => Assert.AreNotEqual(GoblinAI.Goal.Fight, ai.CurrentGoal));
        Assert.AreEqual(W + Vector2Int.right, goblin.CurrentTile, "Home, beside the warren");
        Assert.AreEqual(GoblinAI.Goal.Wander, ai.CurrentGoal, "Then the leashed wander");
        Assert.AreEqual(hp, hero.CurrentHealth, "No hits after the break");
    }

    [Test]
    public void Leashed_BreakWhileInsideDetect_SkipsTheWalk()
    {
        Placeable warren = PlaceWarren(W);
        var (goblin, ai) = AddFighter(W + Vector2Int.right, warren);
        Health hero = AddHero(W + new Vector2Int(3, 0));
        StepUntil(() => hero.CurrentHealth < 100, 3f, "the first hit");
        Assert.LessOrEqual(GoblinAI.Chebyshev(W, goblin.CurrentTile), 4);

        MoveTo(hero, W + new Vector2Int(10, 0)); // Past the leash in one jump; the goblin is still home
        StepUntil(() => ai.CurrentGoal != GoblinAI.Goal.Fight, 0.3f, "the break", () => Assert.AreNotEqual(GoblinAI.Goal.Return, ai.CurrentGoal));
        Assert.AreEqual(GoblinAI.Goal.Wander, ai.CurrentGoal, "Straight back to the wander, no walk");
        StepFor(1f, () => Assert.AreNotEqual(GoblinAI.Goal.Return, ai.CurrentGoal));
    }

    [Test]
    public void Leashed_TargetDeath_StillDisengages()
    {
        Placeable warren = PlaceWarren(W);
        var (_, ai) = AddFighter(W + Vector2Int.right, warren);
        Health hero = AddHero(W + new Vector2Int(2, 0));
        StepUntil(() => ai.CurrentGoal == GoblinAI.Goal.Fight, 0.3f, "engaging");

        hero.TakeDamage(1000);
        Step();
        Assert.IsNull(ai.CurrentTarget);
        Assert.AreEqual(GoblinAI.Goal.Wander, ai.CurrentGoal);
    }

    [Test]
    public void Leashed_UnreachableHeroInsideDetect_IsSkipped()
    {
        Placeable warren = PlaceWarren(W);
        var (_, ai) = AddFighter(W + Vector2Int.left, warren);
        Vector2Int walled = W + new Vector2Int(3, 0);
        Health hero = AddHero(walled);
        Enclose(walled);

        StepFor(2f, () => Assert.AreNotEqual(GoblinAI.Goal.Fight, ai.CurrentGoal));
        Assert.AreEqual(100, hero.CurrentHealth);
    }

    [Test]
    public void Leashed_EngagesFromSleep_ReleasingTheCot()
    {
        Placeable warren = PlaceWarren(W);
        Assert.IsTrue(manager.TryPlaceBuilt(PlaceableType.LairCot, W + new Vector2Int(1, 1), PlaceFrom, out Placeable cot));
        var (goblin, ai) = AddFighter(W + Vector2Int.right, warren);
        goblin.Energy = 20f;
        StepUntil(() => ai.IsSleeping, 5f, "falling asleep");

        Health hero = AddHero(W + new Vector2Int(-3, 0)); // 3 from the warren, 4+ from the cot
        StepUntil(() => ai.CurrentGoal == GoblinAI.Goal.Fight, 0.3f, "the combat poll preempting sleep");
        Assert.AreSame(hero, ai.CurrentTarget);
        Assert.IsFalse(ai.IsSleeping);
        Assert.IsFalse(cot.IsClaimed);
    }

    // ---- return destination ----

    [Test]
    public void ReturnTile_NearestBesideTheWarren_ElseNearestInTheZone_ExploredOnly()
    {
        Vector2Int from = W + new Vector2Int(7, 0);
        Assert.IsTrue(GoblinAI.TryPickReturnTile(board, from, W, 4, null, out Vector2Int tile));
        Assert.AreEqual(W + Vector2Int.right, tile, "The nearest of the four sides, by path");

        // The right side unexplored: the next nearest side.
        Assert.IsTrue(GoblinAI.TryPickReturnTile(board, from, W, 4, t => t != W + Vector2Int.right, out tile));
        Assert.AreNotEqual(W + Vector2Int.right, tile);
        Assert.AreEqual(1, Manhattan(tile, W));

        // Every side walled: the zone tile nearest the warren (a diagonal), not the zone's edge nearest the goblin;
        // the two diagonals on its side tie on path, so scan order picks the lower one. Never the warren's own tile.
        Enclose(W);
        Assert.IsTrue(GoblinAI.TryPickReturnTile(board, from, W, 4, null, out tile));
        Assert.AreEqual(W + new Vector2Int(1, -1), tile);

        // The diagonals walled too: the next ring in (Chebyshev 2), the one nearest the goblin.
        foreach (Vector2Int d in new[] { new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(-1, -1) })
            board.SetTile(W.x + d.x, W.y + d.y, TileState.Rock);
        Assert.IsTrue(GoblinAI.TryPickReturnTile(board, from, W, 4, null, out tile));
        Assert.AreEqual(W + new Vector2Int(2, 0), tile);

        // Nothing in the zone explored: no return tile.
        Assert.IsFalse(GoblinAI.TryPickReturnTile(board, from, W, 4, _ => false, out _));
    }

    // ---- leashed wander ----

    [Test]
    public void LeashedWanderPicks_StayInTheZone_OnWalkableExploredTiles_NeverTheWarren()
    {
        var rng = new System.Random(7);
        Vector2Int dark = W + new Vector2Int(2, 2);
        board.SetTile(W.x - 3, W.y, TileState.Rock);
        var seen = new HashSet<Vector2Int>();
        for (int i = 0; i < 400; i++)
        {
            Assert.IsTrue(GoblinAI.TryPickLeashedWanderTarget(board, W + Vector2Int.right, rng, W, 4, t => t != dark, out Vector2Int pick));
            Assert.LessOrEqual(GoblinAI.Chebyshev(W, pick), 4, $"{pick}");
            Assert.AreNotEqual(W, pick);
            Assert.AreNotEqual(dark, pick, "Unexplored");
            Assert.IsTrue(board.IsWalkable(pick.x, pick.y));
            seen.Add(pick);
        }
        Assert.Greater(seen.Count, 40, "It spreads over the zone (81 tiles less 3), not one spot");
    }

    [Test]
    public void LeashedGoblin_IdleWanderStaysInsideDetect()
    {
        Placeable warren = PlaceWarren(W);
        var (goblin, ai) = AddFighter(W + Vector2Int.right, warren);
        ai.WanderPauseSeconds = 0.2f;
        var visited = new HashSet<Vector2Int>();

        StepFor(90f, () =>
        {
            Assert.LessOrEqual(GoblinAI.Chebyshev(W, goblin.CurrentTile), 4, $"At {goblin.CurrentTile}");
            visited.Add(goblin.CurrentTile);
        });
        Assert.Greater(visited.Count, 15, "It wanders (not standing still)");
        Assert.AreEqual(0, ai.LeashedWanderFallbacks);
    }

    [Test]
    public void LeashedGoblin_AssignedFromAfar_WandersIntoItsZone_AndStays()
    {
        Placeable warren = PlaceWarren(W);
        var (goblin, ai) = AddFighter(new Vector2Int(38, 26), warren);
        ai.WanderPauseSeconds = 0.2f;

        StepUntil(() => GoblinAI.Chebyshev(W, goblin.CurrentTile) <= 4 && !goblin.IsMoving, 20f, "reaching the zone");
        StepFor(30f, () => Assert.LessOrEqual(GoblinAI.Chebyshev(W, goblin.CurrentTile), 4));
    }

    [Test]
    public void UnleashedGoblin_StillWandersTheDungeon()
    {
        // The contrast for the bounds test above: the same goblin with no leash leaves that zone.
        var (goblin, ai) = AddFighter(W + Vector2Int.right);
        ai.WanderPauseSeconds = 0.2f;
        bool left = false;
        StepFor(90f, () => left |= GoblinAI.Chebyshev(W, goblin.CurrentTile) > 4);
        Assert.IsTrue(left);
    }

    [Test]
    public void LeashedWander_NoReachableZoneTile_FallsBackToTodaysWander_AndCountsIt()
    {
        Placeable warren = PlaceWarren(W);
        // Wall the whole zone except the warren tile itself.
        for (int x = W.x - 4; x <= W.x + 4; x++)
            for (int y = W.y - 4; y <= W.y + 4; y++)
                if (new Vector2Int(x, y) != W) board.SetTile(x, y, TileState.Rock);
        var (goblin, ai) = AddFighter(new Vector2Int(36, 16), warren);
        ai.WanderPauseSeconds = 0.2f;
        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("no reachable tile within 4 of its warren"));

        Vector2Int start = goblin.CurrentTile;
        StepUntil(() => goblin.CurrentTile != start, 5f, "the fallback wander moving it");
        Assert.GreaterOrEqual(ai.LeashedWanderFallbacks, 1);
    }

    // ---- unleashed ranges ----

    [Test]
    public void Unleashed_DetectsAt2_NotAt3()
    {
        var (_, ai) = AddFighter(new Vector2Int(20, 10));
        Health hero = AddHero(new Vector2Int(23, 10)); // Manhattan 3 (today's 4 would have engaged)
        StepFor(2f, () => Assert.AreNotEqual(GoblinAI.Goal.Fight, ai.CurrentGoal));

        MoveTo(hero, new Vector2Int(22, 10));          // Manhattan 2
        StepUntil(() => ai.CurrentGoal == GoblinAI.Goal.Fight, 0.3f, "engaging at 2");
    }

    [Test]
    public void Unleashed_DisengagesPast2_FromTheGoblin()
    {
        var (goblin, ai) = AddFighter(new Vector2Int(20, 10));
        Health hero = AddHero(new Vector2Int(21, 10));
        StepUntil(() => hero.CurrentHealth < 100, 1f, "the first hit");

        MoveTo(hero, goblin.CurrentTile + new Vector2Int(2, 0)); // 2: still in
        StepFor(0.6f, () => Assert.AreEqual(GoblinAI.Goal.Fight, ai.CurrentGoal));
        MoveTo(hero, goblin.CurrentTile + new Vector2Int(3, 0)); // 3: out
        StepUntil(() => ai.CurrentGoal != GoblinAI.Goal.Fight, 0.3f, "disengaging past 2");
        Assert.AreNotEqual(GoblinAI.Goal.Return, ai.CurrentGoal, "Unleashed goblins have no home to walk to");
    }

    [Test]
    public void Unleashed_RangesAreTunable()
    {
        var (_, ai) = AddFighter(new Vector2Int(20, 10));
        ai.UnleashedDetectRange = 4;
        AddHero(new Vector2Int(24, 10)); // Manhattan 4: the old default reach, restored by tuning
        StepUntil(() => ai.CurrentGoal == GoblinAI.Goal.Fight, 0.3f, "engaging at the tuned range");
    }
}
