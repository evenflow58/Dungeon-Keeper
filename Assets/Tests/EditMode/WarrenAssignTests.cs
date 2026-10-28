using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// #158: assigning goblins to a warren from its placed card — the live lists (assigned here, unassigned, at another
/// warren), Assign / Unassign / Move here, one warren per goblin, nothing cached on the card, and what an assignment
/// change does to a goblin mid-fight or mid-Return.
/// </summary>
public class WarrenAssignTests
{
    private static readonly Vector2Int A = new Vector2Int(24, 16); // The first warren
    private static readonly Vector2Int B = new Vector2Int(36, 16); // A second, its zone (x 32..40) clear of A's (x 20..28)
    private static readonly Vector2Int PlaceFrom = new Vector2Int(24, 14);
    private const float Dt = 0.02f;
    private const float Frozen = float.PositiveInfinity;

    private GameObject managerGo;
    private GameObject uiGo;
    private DungeonBoard board;
    private BoardRenderer boardRenderer;
    private PlacementManager manager;
    private PlacementController controller;
    private DetailCard card;
    private readonly List<GameObject> spawned = new List<GameObject>();
    private readonly List<Goblin> bodies = new List<Goblin>();
    private readonly List<GoblinAI> ais = new List<GoblinAI>();

    [SetUp]
    public void SetUp()
    {
        managerGo = new GameObject("TestDungeonManager");
        board = managerGo.AddComponent<DungeonBoard>();
        board.InitializeBoard();
        // An open hall, wider than either leash: x 8..40, y 4..28.
        for (int x = 8; x <= 40; x++)
            for (int y = 4; y <= 28; y++)
                board.SetTile(x, y, TileState.Floor);
        boardRenderer = managerGo.AddComponent<BoardRenderer>();
        boardRenderer.Board = board;
        manager = managerGo.AddComponent<PlacementManager>();
        manager.Board = board;
        manager.Renderer = boardRenderer;

        uiGo = new GameObject("TestCardAndController");
        controller = uiGo.AddComponent<PlacementController>();
        controller.PlacementManager = manager;
        card = uiGo.AddComponent<DetailCard>();
        card.PlacementController = controller;
        card.PlacementManager = manager;
    }

    [TearDown]
    public void TearDown()
    {
        if (uiGo != null) Object.DestroyImmediate(uiGo); // OnDestroy takes the card's chrome with it
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

    /// <summary>A named Monster-team goblin with an AI. Frozen wander pauses keep it still unless a goal moves it.</summary>
    private GoblinAI AddGoblin(string name, Vector2Int tile, float wanderPause = Frozen)
    {
        Goblin goblin = AddBody(name, tile, HealthTeam.Monster, 20);
        var ai = goblin.gameObject.AddComponent<GoblinAI>();
        ai.Goblin = goblin;
        ai.PlacementManager = manager;
        ai.Rng = new System.Random(ais.Count + 1);
        ai.WanderPauseSeconds = wanderPause;
        ai.AttackIntervalSeconds = 1f;
        ai.AttackDamage = 1;
        ais.Add(ai);
        return ai;
    }

    /// <summary>A goblin-bodied Hero-team dummy with no AI: it stands where it's put.</summary>
    private Health AddHero(Vector2Int tile, int maxHealth = 1000) =>
        AddBody("Hero" + bodies.Count, tile, HealthTeam.Hero, maxHealth).Health;

    private static void MoveTo(Health body, Vector2Int tile) => body.GetComponent<Goblin>().PlaceOnTile(tile);

    private static Vector2Int TileOf(Health body) => body.GetComponent<Goblin>().CurrentTile;

    private void Step()
    {
        foreach (GoblinAI ai in ais)
        {
            if (!ai.gameObject.activeInHierarchy) continue;
            ai.Goblin.Tick(Dt);
            ai.Tick(Dt);
        }
        foreach (Goblin body in bodies)
            if (body.GetComponent<GoblinAI>() == null) body.Tick(Dt);
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

    private void StepUntil(Func<bool> condition, float maxSeconds, string because)
    {
        float elapsed = 0f;
        while (!condition())
        {
            Assert.Less(elapsed, maxSeconds, "Timed out waiting: " + because);
            Step();
            elapsed += Dt;
        }
    }

    private static int Manhattan(Vector2Int a, Vector2Int b) => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);

    private void Open(Placeable warren) => controller.ClickBoardTile(warren.Tile);

    // The rows as "header" or "name:Action" strings, for whole-list asserts.
    private static List<string> Describe(List<DetailCard.AssignmentRow> rows) =>
        rows.Select(r => r.IsHeader ? r.text : $"{r.text}:{r.action}").ToList();

    private DetailCard.AssignmentRow RowFor(GoblinAI goblin) =>
        card.ShownAssignmentRows.Single(r => r.goblin == goblin);

    // ---- the lists ----

    [Test]
    public void ABuiltWarrensCard_StartsWithAnEmptyAssignedList_AndEveryLivingGoblinUnassigned()
    {
        Placeable warren = PlaceWarren(A);
        AddGoblin("Goblin2", T(12, 6));
        AddGoblin("Goblin1", T(12, 8));
        AddGoblin("Goblin3", T(12, 10));

        Assert.IsFalse(card.HasAssignment, "Nothing selected");
        Open(warren);
        Assert.IsTrue(card.HasAssignment);
        Assert.AreSame(warren, card.ShownWarren);
        CollectionAssert.AreEqual(new[]
        {
            "Assigned: none",
            "Unassigned (3)", "Goblin1:Assign", "Goblin2:Assign", "Goblin3:Assign", // By name; no "elsewhere" section
        }, Describe(card.ShownAssignmentRows));
    }

    [Test]
    public void TheLists_HoldExactlyAssignedHere_Unassigned_AndAtAnotherWarren_NeverTheDeadOrInactive()
    {
        Placeable a = PlaceWarren(A);
        Placeable b = PlaceWarren(B);
        GoblinAI here = AddGoblin("Goblin1", T(12, 6));
        GoblinAI free = AddGoblin("Goblin2", T(12, 8));
        GoblinAI elsewhere = AddGoblin("Goblin3", T(12, 10));
        GoblinAI dead = AddGoblin("Goblin4", T(12, 12));
        GoblinAI inactive = AddGoblin("Goblin5", T(12, 14));
        GoblinAI deadAssigned = AddGoblin("Goblin6", T(12, 16));
        Assert.IsTrue(here.SetLeash(a));
        Assert.IsTrue(elsewhere.SetLeash(b));
        Assert.IsTrue(deadAssigned.SetLeash(a));

        foreach (GoblinAI d in new[] { dead, deadAssigned })
        {
            d.Goblin.Health.TakeDamage(1000);
            d.Goblin.Tick(Dt); // The one death path: no model to tip over, so it deactivates at once
            Assert.IsTrue(d.Goblin.IsDead);
            Assert.IsFalse(d.gameObject.activeInHierarchy);
        }
        inactive.gameObject.SetActive(false);

        CollectionAssert.AreEqual(new[] { here, free, elsewhere }, DetailCard.LivingGoblins());

        Open(a);
        CollectionAssert.AreEqual(new[]
        {
            "Assigned (1)", "Goblin1:Unassign",
            "Unassigned (1)", "Goblin2:Assign",
            "At another warren (1)", "Goblin3:MoveHere",
        }, Describe(card.ShownAssignmentRows));

        Open(b);
        CollectionAssert.AreEqual(new[]
        {
            "Assigned (1)", "Goblin3:Unassign",
            "Unassigned (1)", "Goblin2:Assign",
            "At another warren (1)", "Goblin1:MoveHere",
        }, Describe(card.ShownAssignmentRows));
    }

    [Test]
    public void OnlyAPlacedWarrensCard_CarriesTheList()
    {
        Placeable warren = PlaceWarren(A);
        Assert.IsTrue(manager.TryPlaceBuilt(PlaceableType.LairCot, T(20, 10), PlaceFrom, out Placeable cot));
        GoblinAI goblin = AddGoblin("Goblin1", T(12, 6));

        controller.ClickBoardTile(cot.Tile);
        Assert.IsFalse(card.HasAssignment, "A cot has no assignment list");
        Assert.IsNull(card.ShownWarren);
        Assert.IsEmpty(card.ShownAssignmentRows);
        Assert.IsFalse(card.AssignToShown(goblin), "No warren shown: nothing to assign to");
        Assert.IsFalse(goblin.IsLeashed);

        Open(warren);
        Assert.IsTrue(card.HasAssignment);
        controller.Select(PlaceableType.Warren);
        Assert.IsFalse(card.HasAssignment, "Armed: buildable mode, no list");
        Assert.IsEmpty(card.ShownAssignmentRows);
    }

    // ---- assign ----

    [Test]
    public void Assign_MovesTheGoblinToAssigned_AndItsWanderBecomesBoundedByTheWarren()
    {
        Placeable warren = PlaceWarren(A);
        GoblinAI goblin = AddGoblin("Goblin1", T(10, 6), wanderPause: 0.5f); // Far outside the zone

        Open(warren);
        Assert.IsTrue(card.ClickAssignmentRow(RowFor(goblin)));
        Assert.AreSame(warren, goblin.Warren);
        CollectionAssert.AreEqual(new[] { "Assigned (1)", "Goblin1:Unassign", "Unassigned: none" },
            Describe(card.ShownAssignmentRows));

        StepUntil(() => GoblinAI.Chebyshev(A, goblin.Goblin.CurrentTile) <= goblin.LeashedDetectRadius, 40f,
            "the assigned goblin walks into its warren's zone");
        int worst = 0;
        StepFor(60f, () => worst = Mathf.Max(worst, GoblinAI.Chebyshev(A, goblin.Goblin.CurrentTile)));
        Assert.LessOrEqual(worst, goblin.LeashedDetectRadius, "Bounded wander once assigned");
        Assert.AreEqual(0, goblin.LeashedWanderFallbacks);
    }

    [Test]
    public void Assign_SwitchesTheGoblinToZoneDefense()
    {
        Placeable warren = PlaceWarren(A);
        GoblinAI goblin = AddGoblin("Goblin1", T(24, 10));    // Chebyshev 6 from A: outside its detect zone
        Health inZone = AddHero(T(28, 13));                   // Manhattan 7 from the goblin, Chebyshev 4 from A

        StepFor(1f);
        Assert.AreNotEqual(GoblinAI.Goal.Fight, goblin.CurrentGoal, "Unassigned: the hero is out of its 2");

        Open(warren);
        Assert.IsTrue(card.AssignToShown(goblin));
        StepFor(0.5f);
        Assert.AreEqual(GoblinAI.Goal.Fight, goblin.CurrentGoal, "Assigned: a hero in the warren's zone is engaged");
        Assert.AreSame(inZone, goblin.CurrentTarget);
    }

    [Test]
    public void AssignRejects_ADeadGoblin_AndAHeaderClickDoesNothing()
    {
        Placeable warren = PlaceWarren(A);
        GoblinAI goblin = AddGoblin("Goblin1", T(12, 6));
        Open(warren);

        Assert.IsFalse(card.ClickAssignmentRow(card.ShownAssignmentRows[0]), "A header has no action");
        goblin.Goblin.Health.TakeDamage(1000);
        goblin.Goblin.Tick(Dt);
        Assert.IsFalse(card.AssignToShown(goblin), "The dead are never assignable");
        Assert.IsFalse(goblin.IsLeashed);
        Assert.IsFalse(card.AssignToShown(null));
        Assert.IsFalse(card.UnassignFromShown(null));
    }

    // ---- one warren per goblin, nothing cached ----

    [Test]
    public void MoveHere_OnASecondWarrensCard_ReleashesIt_AndTheFirstCardDropsIt()
    {
        Placeable a = PlaceWarren(A);
        Placeable b = PlaceWarren(B);
        GoblinAI goblin = AddGoblin("Goblin1", T(12, 6));
        AddGoblin("Goblin2", T(12, 8));

        Open(a);
        Assert.IsTrue(card.ClickAssignmentRow(RowFor(goblin)));
        Assert.AreSame(a, goblin.Warren);

        Open(b);
        DetailCard.AssignmentRow move = RowFor(goblin);
        Assert.AreEqual(DetailCard.AssignAction.MoveHere, move.action);
        Assert.AreEqual("Move here", DetailCard.AssignButtonLabel(move.action));
        Assert.IsTrue(card.ClickAssignmentRow(move));
        Assert.AreSame(b, goblin.Warren, "One warren per goblin: B replaces A");
        CollectionAssert.AreEqual(new[] { "Assigned (1)", "Goblin1:Unassign", "Unassigned (1)", "Goblin2:Assign" },
            Describe(card.ShownAssignmentRows));

        Open(a); // Reopen: rebuilt from live state, not remembered from the first visit
        CollectionAssert.AreEqual(new[]
        {
            "Assigned: none",
            "Unassigned (1)", "Goblin2:Assign",
            "At another warren (1)", "Goblin1:MoveHere",
        }, Describe(card.ShownAssignmentRows));
        Assert.IsFalse(card.UnassignFromShown(goblin), "Not assigned here any more: A's card can't unassign it");
        Assert.AreSame(b, goblin.Warren);
    }

    [Test]
    public void TheOpenCard_FollowsAChangeMadeElsewhere_OnItsNextRead()
    {
        Placeable a = PlaceWarren(A);
        Placeable b = PlaceWarren(B);
        GoblinAI goblin = AddGoblin("Goblin1", T(12, 6));

        Open(a);
        Assert.IsTrue(card.AssignToShown(goblin));
        Assert.AreEqual("Goblin1:Unassign", Describe(card.ShownAssignmentRows)[1]);

        Assert.IsTrue(goblin.SetLeash(b)); // Moved without this card hearing about it
        CollectionAssert.AreEqual(new[] { "Assigned: none", "Unassigned: none", "At another warren (1)", "Goblin1:MoveHere" },
            Describe(card.ShownAssignmentRows));
    }

    [Test]
    public void ThePaintedCard_ShowsTheRows_AndItsButtonsAct()
    {
        Placeable warren = PlaceWarren(A);
        GoblinAI goblin = AddGoblin("Goblin1", T(12, 6));
        card.Build();
        Open(warren);
        card.Repaint();

        Transform list = FindCardChild("Assignment");
        Assert.IsNotNull(list, "The warren's card builds its assignment list");
        Assert.IsTrue(list.gameObject.activeInHierarchy);
        Button button = ActiveRowButtons(list).Single();
        Assert.AreEqual("Assign", button.GetComponentInChildren<Text>().text);

        button.onClick.Invoke();
        Assert.AreSame(warren, goblin.Warren);
        button = ActiveRowButtons(list).Single();
        Assert.AreEqual("Unassign", button.GetComponentInChildren<Text>().text, "Repainted after the click");

        button.onClick.Invoke();
        Assert.IsFalse(goblin.IsLeashed);

        Assert.IsTrue(manager.TryPlaceBuilt(PlaceableType.LairCot, T(20, 10), PlaceFrom, out Placeable cot));
        controller.ClickBoardTile(cot.Tile);
        card.Repaint();
        Assert.IsFalse(list.gameObject.activeInHierarchy, "Hidden on a cot's card");
    }

    private Transform FindCardChild(string name)
    {
        foreach (Canvas canvas in Object.FindObjectsByType<Canvas>())
        {
            if (canvas.name != "DetailCardCanvas") continue;
            Transform found = canvas.transform.Find("Card/" + name);
            if (found != null) return found;
        }
        return null;
    }

    private static List<Button> ActiveRowButtons(Transform list) =>
        list.GetComponentsInChildren<Button>(false).ToList();

    // ---- unassign ----

    [Test]
    public void Unassign_ReturnsTheGoblinToUnleashedRangesAndTheDungeonWideWander()
    {
        Placeable warren = PlaceWarren(A);
        GoblinAI goblin = AddGoblin("Goblin1", T(24, 15), wanderPause: 0.5f);
        Assert.IsTrue(goblin.SetLeash(warren));

        Open(warren);
        Assert.IsTrue(card.ClickAssignmentRow(RowFor(goblin)));
        Assert.IsFalse(goblin.IsLeashed);
        Assert.IsNull(goblin.Warren);
        CollectionAssert.AreEqual(new[] { "Assigned: none", "Unassigned (1)", "Goblin1:Assign" }, Describe(card.ShownAssignmentRows));

        // Dungeon-wide wander: it leaves the old zone.
        int worst = 0;
        StepFor(90f, () => worst = Mathf.Max(worst, GoblinAI.Chebyshev(A, goblin.Goblin.CurrentTile)));
        Assert.Greater(worst, goblin.LeashedDetectRadius, "Unassigned, its wander isn't bounded by the warren");

        // The unleashed ranges: a hero 3 from it is ignored, one 2 from it is fought.
        goblin.WanderPauseSeconds = Frozen;
        StepUntil(() => goblin.CurrentGoal != GoblinAI.Goal.Wander || !goblin.Goblin.IsMoving, 20f, "the leg in progress ends");
        Vector2Int at = goblin.Goblin.CurrentTile;
        Vector2Int three = at.x <= 30 ? new Vector2Int(at.x + 3, at.y) : new Vector2Int(at.x - 3, at.y);
        Health hero = AddHero(three);
        StepFor(1f);
        Assert.AreNotEqual(GoblinAI.Goal.Fight, goblin.CurrentGoal, "Manhattan 3: out of the unleashed 2");
        MoveTo(hero, at.x <= 30 ? new Vector2Int(at.x + 2, at.y) : new Vector2Int(at.x - 2, at.y));
        StepFor(0.5f);
        Assert.AreEqual(GoblinAI.Goal.Fight, goblin.CurrentGoal, "Manhattan 2: engaged");
    }

    // ---- assignment changes mid-goal ----

    [Test]
    public void AssignedMidFight_FightsOnInsideTheNewLeash_ThenBreaksAndReturnsToTheNewWarren()
    {
        Placeable warren = PlaceWarren(A);
        GoblinAI goblin = AddGoblin("Goblin1", T(24, 10));
        Health hero = AddHero(T(24, 9)); // Adjacent: an unleashed goblin engages

        StepFor(0.5f);
        Assert.AreEqual(GoblinAI.Goal.Fight, goblin.CurrentGoal);

        Open(warren);
        Assert.IsTrue(card.AssignToShown(goblin));
        // Manhattan 5 from the goblin — past the unleashed 2 — but Chebyshev 7 from A, inside the new leash of 8.
        MoveTo(hero, T(28, 9));
        Assert.Greater(Manhattan(goblin.Goblin.CurrentTile, TileOf(hero)), goblin.UnleashedDisengageRange);
        StepFor(1f);
        Assert.AreEqual(GoblinAI.Goal.Fight, goblin.CurrentGoal, "The new leash's rules: fights on inside 8 of A");
        Assert.AreSame(hero, goblin.CurrentTarget);

        MoveTo(hero, T(24, 5)); // Chebyshev 11 from A: past the new leash
        StepUntil(() => goblin.CurrentGoal != GoblinAI.Goal.Fight, 1f, "the leash breaks on the next poll");
        Assert.AreEqual(GoblinAI.Goal.Return, goblin.CurrentGoal, "Outside A's zone, so it walks home");
        Assert.AreEqual(1, Manhattan(goblin.ReturnTile, A), "Home is beside the NEW warren");

        StepUntil(() => goblin.CurrentGoal != GoblinAI.Goal.Return, 10f, "it arrives");
        Assert.AreNotEqual(GoblinAI.Goal.None, goblin.CurrentGoal, "Never stuck");
        Assert.LessOrEqual(GoblinAI.Chebyshev(A, goblin.Goblin.CurrentTile), goblin.LeashedDetectRadius);
    }

    [Test]
    public void UnassignedMidFight_FightsOnWithinTwo_ThenDisengagesPastIt()
    {
        Placeable warren = PlaceWarren(A);
        GoblinAI goblin = AddGoblin("Goblin1", T(24, 14));
        Assert.IsTrue(goblin.SetLeash(warren));
        Health hero = AddHero(T(24, 12)); // Chebyshev 4 from A: inside the zone

        StepFor(0.5f);
        Assert.AreEqual(GoblinAI.Goal.Fight, goblin.CurrentGoal);
        StepUntil(() => !goblin.Goblin.IsMoving, 5f, "it reaches the hero");

        Open(warren);
        Assert.IsTrue(card.UnassignFromShown(goblin));
        StepFor(1f);
        Assert.AreEqual(GoblinAI.Goal.Fight, goblin.CurrentGoal, "Within 2 of it: the fight goes on");
        Assert.AreSame(hero, goblin.CurrentTarget);

        // Manhattan 4 from the goblin, Chebyshev 4 from A: a leashed goblin would fight on; unassigned, it disengages.
        Vector2Int at = goblin.Goblin.CurrentTile;
        MoveTo(hero, new Vector2Int(at.x + 4, at.y));
        Assert.LessOrEqual(GoblinAI.Chebyshev(A, TileOf(hero)), goblin.LeashRadius);
        StepUntil(() => goblin.CurrentGoal != GoblinAI.Goal.Fight, 1f, "it disengages on the next poll");
        Assert.AreNotEqual(GoblinAI.Goal.Return, goblin.CurrentGoal, "Unassigned: no walk home");
        Assert.AreNotEqual(GoblinAI.Goal.None, goblin.CurrentGoal, "Never stuck");
        Assert.IsNull(goblin.CurrentTarget);
    }

    [Test]
    public void MovedMidReturn_FinishesTheWalk_ThenWandersIntoTheNewWarrensZone()
    {
        Placeable a = PlaceWarren(A);
        Placeable b = PlaceWarren(B);
        GoblinAI goblin = AddGoblin("Goblin1", T(24, 10)); // Chebyshev 6 from A: outside its zone
        Assert.IsTrue(goblin.SetLeash(a));
        Health hero = AddHero(T(24, 12)); // Chebyshev 4 from A

        StepFor(0.5f);
        Assert.AreEqual(GoblinAI.Goal.Fight, goblin.CurrentGoal);
        StepUntil(() => !goblin.Goblin.IsMoving, 5f, "it reaches the hero");
        Assert.Greater(GoblinAI.Chebyshev(A, goblin.Goblin.CurrentTile), goblin.LeashedDetectRadius,
            "Staging: it fights from outside A's zone, so a break means a walk");

        MoveTo(hero, T(12, 26)); // Far past A's leash (and nowhere near B's zone)
        StepUntil(() => goblin.CurrentGoal == GoblinAI.Goal.Return, 1f, "the leash breaks");

        Open(b);
        Assert.IsTrue(card.ClickAssignmentRow(RowFor(goblin)), "Move here, mid-Return");
        Assert.AreSame(b, goblin.Warren);
        goblin.WanderPauseSeconds = 0.5f;

        StepUntil(() => goblin.CurrentGoal != GoblinAI.Goal.Return, 10f, "the walk ends");
        Assert.AreNotEqual(GoblinAI.Goal.None, goblin.CurrentGoal, "Never stuck");
        StepUntil(() => GoblinAI.Chebyshev(B, goblin.Goblin.CurrentTile) <= goblin.LeashedDetectRadius, 40f,
            "it wanders into the NEW warren's zone");
        int worst = 0;
        StepFor(30f, () => worst = Mathf.Max(worst, GoblinAI.Chebyshev(B, goblin.Goblin.CurrentTile)));
        Assert.LessOrEqual(worst, goblin.LeashedDetectRadius, "And stays there, not stranded at A");
        Assert.AreEqual(GoblinAI.Goal.Wander, goblin.CurrentGoal);
    }

    private static Vector2Int T(int x, int y) => new Vector2Int(x, y);
}
