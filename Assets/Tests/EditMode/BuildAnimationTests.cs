using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Building animation (#93): the imp's crouch-and-rise build pose, the site's progressive assembly in thirds with a
/// shrinking materials pile, and the completion pop. Visuals only.
/// </summary>
public class BuildAnimationTests
{
    private static readonly Vector2Int PlaceFrom = new Vector2Int(24, 16);
    private const float Dt = 0.02f;

    private GameObject managerGo;
    private GameObject creatureGo;
    private DungeonBoard board;
    private BoardRenderer boardRenderer;
    private PlacementManager manager;

    [SetUp]
    public void SetUp()
    {
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
        if (creatureGo != null) Object.DestroyImmediate(creatureGo);
        if (managerGo != null) Object.DestroyImmediate(managerGo);
    }

    private Placeable PlaceSite(PlaceableType type, int x = 22, int y = 14)
    {
        Assert.IsTrue(manager.TryPlace(type, new Vector2Int(x, y), PlaceFrom, out Placeable p));
        return p;
    }

    private static int ActiveParts(Placeable p)
    {
        if (!p.Model.activeSelf) return 0;
        int n = 0;
        foreach (Transform part in p.Model.transform) if (part.gameObject.activeSelf) n++;
        return n;
    }

    private static bool[] PartStates(Placeable p)
    {
        var states = new bool[p.Model.transform.childCount + 1];
        for (int i = 0; i < p.Model.transform.childCount; i++) states[i] = p.Model.transform.GetChild(i).gameObject.activeSelf;
        states[states.Length - 1] = p.Model.activeSelf;
        return states;
    }

    // --- The build pose ---

    [Test]
    public void SelectPose_BuildingSharesTheDigTier()
    {
        Assert.AreEqual(CreatureMotion.PoseKind.Building, CreatureMotion.SelectPose(false, false, false, true, true), "Building over walk");
        Assert.AreEqual(CreatureMotion.PoseKind.Building, CreatureMotion.SelectPose(false, false, false, true, false), "Building over idle");
        Assert.AreEqual(CreatureMotion.PoseKind.Dig, CreatureMotion.SelectPose(false, false, true, true, false), "Dig checked first in the tier");
        Assert.AreEqual(CreatureMotion.PoseKind.Sleep, CreatureMotion.SelectPose(false, true, false, true, false), "Sleep over building");
        Assert.AreEqual(CreatureMotion.PoseKind.Dead, CreatureMotion.SelectPose(true, false, false, true, false), "Death over everything");
        Assert.AreEqual(CreatureMotion.PoseKind.Walk, CreatureMotion.SelectPose(false, false, false, false, true), "The 4-arg rules are unchanged");
    }

    [Test]
    public void BuildLoop_CrouchesAndRisesOncePerCycle_ArmPatsAtTheBottom()
    {
        MotionTuning t = MotionTuning.Imp();
        float c = t.buildCycleSeconds;
        Assert.AreEqual(0f, CreatureMotion.BuildCrouch(0f, t), 1e-5f, "Starts upright");
        Assert.AreEqual(0.5f, CreatureMotion.BuildCrouch(c * 0.25f, t), 1e-4f, "Quarter: halfway down");
        Assert.AreEqual(1f, CreatureMotion.BuildCrouch(c * 0.5f, t), 1e-5f, "Half: the bottom");
        Assert.AreEqual(0f, CreatureMotion.BuildCrouch(c, t), 1e-4f, "Full cycle: back up");
        Assert.AreEqual(CreatureMotion.BuildCrouch(0.3f, t), CreatureMotion.BuildCrouch(0.3f + c, t), 1e-4f, "Loops");

        Assert.AreEqual(t.buildRaisePoint, CreatureMotion.BuildAimPoint(0f, t), "Aimed up and forward at the top");
        Assert.AreEqual(t.buildStrikePoint, CreatureMotion.BuildAimPoint(c * 0.5f, t), "The pat: aimed at the site at the bottom");
        Assert.Less(t.buildStrikePoint.z, -0.5f, "The strike point is out on the site, a tile ahead");
        Assert.AreEqual(0f, t.buildStrikePoint.x, "Straight ahead: the imp faces the site");

        // The aim rotation turns the tool's rest direction onto the shoulder→aim line.
        Vector3 shoulder = new Vector3(0.1f, 0.24f, 0f), rest = new Vector3(0.6f, 0.77f, -0.2f).normalized;
        Quaternion q = CreatureMotion.ArmAim(shoulder, rest, t.buildStrikePoint);
        Assert.Less(Vector3.Angle(q * rest, t.buildStrikePoint - shoulder), 0.01f);
    }

    private Imp ModeledImp()
    {
        creatureGo = new GameObject("TestImp");
        var imp = creatureGo.AddComponent<Imp>();
        imp.Board = board;
        imp.Renderer = boardRenderer;
        imp.PlaceOnTile(PlaceFrom);
        imp.CreateModel();
        return imp;
    }

    [Test]
    public void BuildingPose_BlendsIn_AndDipsTheBody()
    {
        Imp imp = ModeledImp();
        CreatureMotion m = imp.Motion;
        MotionTuning t = MotionTuning.Imp();

        m.Step(Dt, new CreatureMotion.Flags { Building = true });
        Assert.AreEqual(CreatureMotion.PoseKind.Building, m.CurrentPose);
        Assert.Greater(m.BuildWeight, 0f);
        Assert.Less(m.BuildWeight, 0.5f, "Eases in over poseBlendSeconds, no snap");

        // Run to the bottom of a crouch at full weight: half a cycle past a whole number of cycles (1.5 cycles).
        float clock = Dt;
        float bottom = t.buildCycleSeconds * 1.5f;
        while (clock < bottom - 1e-4f) { m.Step(Dt, new CreatureMotion.Flags { Building = true }); clock += Dt; }
        Assert.AreEqual(1f, CreatureMotion.BuildCrouch(bottom, t), 1e-4f, "Precondition: at the bottom");
        Assert.AreEqual(1f, m.BuildWeight, 1e-4f);
        Assert.Less(imp.Model.transform.localPosition.y, -0.03f, "The whole imp dips at the bottom");
    }

    /// <summary>Steps a building imp facing site until clock (seconds of building) is reached; returns the pick's world ray.</summary>
    private static Ray PickRay(Imp imp, Placeable site, float seconds)
    {
        for (float c = 0f; c < seconds - 1e-4f; c += Dt)
            imp.Motion.Step(Dt, new CreatureMotion.Flags { Building = true, FaceTarget = site.transform.position });
        Transform arm = CreatureModel.FindPivot(imp.Model, CreatureModel.ArmRightPivot);
        Vector3 head = arm.Find("PickHead").position;
        return new Ray(arm.position, head - arm.position);
    }

    [Test]
    public void BuildingPose_ThePickPointsAtTheSite_AtAnyHeading()
    {
        // A site beside the imp to each side: the imp turns to face it, and the pick (shoulder → pick head) aims at
        // the site's tile — not off to the side, not behind, not overhead (the bug Evan caught in Play).
        MotionTuning t = MotionTuning.Imp();
        foreach (Vector2Int offset in new[] { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) })
        {
            Imp imp = ModeledImp();
            Placeable site = PlaceSite(PlaceableType.LairCot, PlaceFrom.x + offset.x, PlaceFrom.y + offset.y);
            Vector3 toSite = site.transform.position - imp.transform.position;
            toSite.y = 0f;

            // Bottom of the crouch (the pat): the pick's line comes down onto the site's tile.
            Ray strike = PickRay(imp, site, t.buildCycleSeconds * 1.5f);
            Assert.Less(strike.direction.y, 0f, $"{offset}: pointing down at the bottom");
            float s = (0.05f - strike.origin.y) / strike.direction.y;
            Vector3 hit = strike.origin + strike.direction * s;
            Assert.Less(Mathf.Abs(hit.x - site.transform.position.x), 0.4f, $"{offset}: lands on the site (x), hit {hit}");
            Assert.Less(Mathf.Abs(hit.z - site.transform.position.z), 0.4f, $"{offset}: lands on the site (z), hit {hit}");

            // Top of the rise: still aimed toward the site (raised), never sideways or back.
            Ray raise = PickRay(imp, site, t.buildCycleSeconds * 0.5f);
            Vector3 flat = raise.direction;
            flat.y = 0f;
            Assert.Greater(Vector3.Dot(flat.normalized, toSite.normalized), 0.95f, $"{offset}: raised toward the site");
            Assert.Greater(raise.direction.y, 0f, $"{offset}: raised at the top");

            Object.DestroyImmediate(creatureGo);
            Object.DestroyImmediate(site.gameObject);
        }

        // Contrast: the dig winds the same pick up overhead and back; the build's pat stays low and in front.
        Assert.Greater(t.digRaisePoint.y, t.buildRaisePoint.y, "The dig's wind-up is higher");
        Assert.Greater(t.digRaisePoint.z, t.buildRaisePoint.z, "...and goes back over the shoulder; the build's stays in front");
    }

    [Test]
    public void BuildingPose_PausedFreezes()
    {
        Imp imp = ModeledImp();
        CreatureMotion m = imp.Motion;
        for (int i = 0; i < 17; i++) m.Step(Dt, new CreatureMotion.Flags { Building = true });
        Quaternion arm = CreatureModel.FindPivot(imp.Model, CreatureModel.ArmRightPivot).localRotation;
        Vector3 root = imp.Model.transform.localPosition;
        for (int i = 0; i < 30; i++) m.Step(0f, new CreatureMotion.Flags { Building = true });
        Assert.AreEqual(0f, Quaternion.Angle(arm, CreatureModel.FindPivot(imp.Model, CreatureModel.ArmRightPivot).localRotation), 1e-4f);
        Assert.AreEqual(root, imp.Model.transform.localPosition);
    }

    // --- Progressive assembly ---

    [Test]
    public void RevealedParts_PiecewiseThirds()
    {
        float a = 1f / 3f, b = 2f / 3f, all = 0.95f;
        Assert.AreEqual(0, Placeable.RevealedParts(0f, 7, a, b, all));
        Assert.AreEqual(0, Placeable.RevealedParts(0.33f, 7, a, b, all), "Not before a third");
        Assert.AreEqual(3, Placeable.RevealedParts(0.34f, 7, a, b, all), "A third, rounded up");
        Assert.AreEqual(3, Placeable.RevealedParts(0.6f, 7, a, b, all), "No per-part drip within a third");
        Assert.AreEqual(5, Placeable.RevealedParts(0.67f, 7, a, b, all), "Two thirds, rounded up");
        Assert.AreEqual(7, Placeable.RevealedParts(0.96f, 7, a, b, all), "All, near the end");
        Assert.AreEqual(1, Placeable.RevealedParts(0.34f, 3, a, b, all));
        Assert.AreEqual(2, Placeable.RevealedParts(0.67f, 3, a, b, all), "Exact thirds stay exact");
        Assert.AreEqual(1, Placeable.RevealedParts(0.34f, 1, a, b, all), "A one-part model shows from the first third");
    }

    [Test]
    public void Sync_Cot_RevealsByRecipeOrder_AndThePileShrinks()
    {
        Placeable cot = PlaceSite(PlaceableType.LairCot);
        cot.SyncConstructionVisuals();
        Assert.IsFalse(cot.Model.activeSelf, "Progress 0: #91's site exactly");
        Assert.AreEqual(Vector3.one, cot.MaterialsPile.transform.localScale, "Full-size pile");

        cot.BuildProgress = 0.34f;
        cot.SyncConstructionVisuals();
        Assert.AreEqual(1, ActiveParts(cot));
        Assert.IsTrue(cot.Model.transform.Find("Frame").gameObject.activeSelf, "The frame first");
        Assert.IsFalse(cot.Model.transform.Find("Pillow").gameObject.activeSelf);
        Assert.Less(cot.MaterialsPile.transform.localScale.x, 1f);

        cot.BuildProgress = 0.67f;
        cot.SyncConstructionVisuals();
        Assert.AreEqual(2, ActiveParts(cot));
        Assert.IsTrue(cot.Model.transform.Find("Mattress").gameObject.activeSelf, "Then the mattress on it");

        cot.BuildProgress = 0.96f;
        cot.SyncConstructionVisuals();
        Assert.AreEqual(3, ActiveParts(cot), "All parts near the end");
        Assert.AreEqual(3, cot.RevealedPartCount);

        cot.BuildProgress = 1f;
        cot.SyncConstructionVisuals();
        Assert.AreEqual(0.25f, cot.MaterialsPile.transform.localScale.x, 1e-5f, "Pile at its floor by 1");
        Assert.IsFalse(cot.IsBuilt, "Visuals only: progress 1 still doesn't complete");
    }

    [Test]
    public void Sync_Plot_SevenPartsInThirds()
    {
        Placeable plot = PlaceSite(PlaceableType.MushroomPlot);
        Assert.AreEqual(7, plot.Model.transform.childCount, "Precondition: mound + three stems and caps");
        int[] expected = { 0, 3, 5, 7 };
        float[] at = { 0.1f, 0.4f, 0.7f, 0.97f };
        for (int i = 0; i < at.Length; i++)
        {
            plot.BuildProgress = at[i];
            plot.SyncConstructionVisuals();
            Assert.AreEqual(expected[i], ActiveParts(plot), $"at {at[i]}");
        }
        Assert.IsTrue(plot.Model.transform.Find("Mound").gameObject.activeSelf, "The mound came first");
    }

    [Test]
    public void Sync_IsIdempotent()
    {
        Placeable plot = PlaceSite(PlaceableType.MushroomPlot);
        plot.BuildProgress = 0.5f;
        plot.SyncConstructionVisuals();
        bool[] once = PartStates(plot);
        Vector3 pile = plot.MaterialsPile.transform.localScale;
        plot.SyncConstructionVisuals();
        CollectionAssert.AreEqual(once, PartStates(plot));
        Assert.AreEqual(pile, plot.MaterialsPile.transform.localScale);
    }

    [Test]
    public void Tick_SyncsWheneverProgressChanges_AndAnUntouchedSiteStaysAFullPile()
    {
        Placeable untouched = PlaceSite(PlaceableType.SpikeTrap, 21, 13);
        Placeable worked = PlaceSite(PlaceableType.LairCot, 22, 13);
        for (int i = 0; i < 200; i++) { untouched.Tick(Dt); worked.Tick(Dt); }
        Assert.IsFalse(untouched.Model.activeSelf, "Never worked: no parts");
        Assert.AreEqual(Vector3.one, untouched.MaterialsPile.transform.localScale, "Full-size pile, indefinitely");

        worked.BuildProgress = 0.7f; // As the imp writes it
        worked.Tick(Dt);
        Assert.AreEqual(2, ActiveParts(worked), "Picked up by the per-frame check");
    }

    [Test]
    public void TrapSite_ShowsItsPlateEarly_SpikesOnlyAtCompletion()
    {
        Placeable p = PlaceSite(PlaceableType.SpikeTrap);
        SpikeTrap trap = p.GetComponent<SpikeTrap>();
        p.BuildProgress = 0.4f;
        p.SyncConstructionVisuals();
        Assert.AreEqual(1, ActiveParts(p), "The plate");
        p.BuildProgress = 0.99f;
        p.SyncConstructionVisuals();
        Assert.IsFalse(trap.Spikes.gameObject.activeSelf, "Spikes are the last stage: completion");
        p.CompleteConstruction();
        Assert.IsTrue(trap.Spikes.gameObject.activeSelf);
    }

    // --- Completion ---

    [Test]
    public void Completion_FromAPartialReveal_ShowsEveryPart_AndPops()
    {
        Placeable plot = PlaceSite(PlaceableType.MushroomPlot);
        plot.BuildProgress = 0.4f;
        plot.SyncConstructionVisuals();
        Assert.AreEqual(3, ActiveParts(plot));

        plot.CompleteConstruction();
        Assert.AreEqual(7, ActiveParts(plot), "Every part, whatever stage the assembly reached");
        Assert.IsNull(plot.MaterialsPile, "Pile gone");
        Assert.IsTrue(plot.IsBouncing);
        Assert.AreEqual(1.12f, plot.Model.transform.localScale.x, 1e-5f, "The pop starts at the overshoot");
        Assert.AreEqual(Vector3.one, plot.transform.localScale, "The pop scales the model root, never the placeable root");

        plot.SyncConstructionVisuals(); // Inert once built
        Assert.AreEqual(7, ActiveParts(plot));

        float t = 0f;
        while (plot.IsBouncing && t < 1f) { plot.Tick(Dt); t += Dt; }
        Assert.AreEqual(0.3f, t, Dt + 1e-4f, "Settles over completionBounceSeconds");
        Assert.AreEqual(Vector3.one, plot.Model.transform.localScale, "Lands exactly on 1");
        for (int i = 0; i < 20; i++) plot.Tick(Dt);
        Assert.AreEqual(Vector3.one, plot.Model.transform.localScale, "And stays there");
        Assert.AreEqual(Vector3.one, plot.transform.localScale, "Tile anchoring untouched");
    }

    [Test]
    public void CompletionBounce_Curve_StartsAtTheOvershoot_LandsOnOne_PauseHolds()
    {
        Assert.AreEqual(1.12f, Placeable.CompletionBounce(0f, 0.3f, 1.12f), 1e-6f);
        Assert.AreEqual(1f, Placeable.CompletionBounce(0.3f, 0.3f, 1.12f));
        Assert.AreEqual(1f, Placeable.CompletionBounce(5f, 0.3f, 1.12f), "Stays on 1");
        for (float e = 0f; e < 0.3f; e += 0.01f)
        {
            float s = Placeable.CompletionBounce(e, 0.3f, 1.12f);
            Assert.LessOrEqual(s, 1.12f + 1e-6f, "Never past the overshoot");
            Assert.Greater(s, 0.9f, "A settle, not a collapse");
        }
        Assert.Less(Mathf.Abs(Placeable.CompletionBounce(0.299f, 0.3f, 1.12f) - 1f), 1e-3f, "Continuous into the landing");

        Placeable cot = PlaceSite(PlaceableType.LairCot);
        cot.CompleteConstruction();
        for (int i = 0; i < 5; i++) cot.Tick(Dt);
        float mid = cot.Model.transform.localScale.x;
        for (int i = 0; i < 30; i++) cot.Tick(0f);
        Assert.AreEqual(mid, cot.Model.transform.localScale.x, "Paused mid-pop: holds");
        Assert.IsTrue(cot.IsBouncing);
    }
}
