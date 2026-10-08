using NUnit.Framework;
using UnityEngine;

public class CreatureMotionTests
{
    private const float Dt = 0.02f;
    private GameObject managerGo;
    private GameObject creatureGo;
    private DungeonBoard board;
    private BoardRenderer boardRenderer;

    [SetUp]
    public void SetUp()
    {
        managerGo = new GameObject("TestDungeonManager");
        board = managerGo.AddComponent<DungeonBoard>();
        board.InitializeBoard();
        boardRenderer = managerGo.AddComponent<BoardRenderer>();
        boardRenderer.Board = board;
    }

    [TearDown]
    public void TearDown()
    {
        if (creatureGo != null) Object.DestroyImmediate(creatureGo);
        if (managerGo != null) Object.DestroyImmediate(managerGo);
    }

    /// <summary>A goblin with its model and pose driver, on the cavern floor (24,16).</summary>
    private Goblin ModeledGoblin()
    {
        creatureGo = new GameObject("TestGoblin");
        var goblin = creatureGo.AddComponent<Goblin>();
        goblin.Board = board;
        goblin.Renderer = boardRenderer;
        goblin.LogStats = false;
        goblin.PlaceOnTile(new Vector2Int(24, 16));
        goblin.CreateModel();
        return goblin;
    }

    private static Transform Pivot(Goblin g, string name) => CreatureModel.FindPivot(g.Model, name);

    // --- Walk phase: distance, not time ---

    [Test]
    public void AdvancePhase_OneStrideIsOneFullCycle()
    {
        Assert.AreEqual(Mathf.PI, CreatureMotion.AdvancePhase(0f, 0.175f, 0.35f), 1e-5f, "Half a stride, half a cycle");
        Assert.AreEqual(0f, CreatureMotion.AdvancePhase(0f, 0.35f, 0.35f) % (2f * Mathf.PI), 1e-4f, "A full stride wraps");
        Assert.AreEqual(1.5f, CreatureMotion.AdvancePhase(1.5f, 0f, 0.35f), 1e-6f, "Standing still: no change");
    }

    [Test]
    public void FootFrequency_MatchesGroundSpeed_AtAnySpeed_NoSkating()
    {
        // Cycles completed = distance / stride, whatever the speed or frame rate: 1x, 2x (bigger steps) and the
        // weakened half speed all put the same number of footfalls on the same stretch of ground.
        Goblin goblin = ModeledGoblin();
        float stride = new MotionTuning().strideLength;

        foreach (float speed in new[] { 3f, 6f, 1.5f })
        {
            goblin.PlaceOnTile(new Vector2Int(21, 16));
            goblin.Pose(Dt); // Settle lastPosition at the start
            float totalPhase = 0f, prev = goblin.Motion.Phase;
            float traveled = 0f;
            for (int i = 0; i < 50; i++)
            {
                goblin.transform.position += Vector3.right * speed * Dt; // Move the body as Advance would
                traveled += speed * Dt;
                goblin.Pose(Dt);
                float d = goblin.Motion.Phase - prev;
                if (d < 0f) d += 2f * Mathf.PI;
                totalPhase += d;
                prev = goblin.Motion.Phase;
            }
            Assert.AreEqual(traveled / stride, totalPhase / (2f * Mathf.PI), 1e-3f, $"Cycles per distance at {speed} t/s");
        }
    }

    [Test]
    public void Paused_FreezesMidStride()
    {
        Goblin goblin = ModeledGoblin();
        goblin.Pose(Dt);
        for (int i = 0; i < 10; i++) { goblin.transform.position += Vector3.right * 0.06f; goblin.Pose(Dt); }
        Quaternion boot = Pivot(goblin, CreatureModel.BootLeftPivot).localRotation;
        float phase = goblin.Motion.Phase;

        for (int i = 0; i < 10; i++) goblin.Pose(0f); // Time.timeScale 0: deltaTime 0

        Assert.AreEqual(phase, goblin.Motion.Phase, "Phase frozen");
        Assert.AreEqual(boot, Pivot(goblin, CreatureModel.BootLeftPivot).localRotation, "Pose frozen");
    }

    [Test]
    public void Walk_BootsSwingInOppositePhase_ArmsCounterSwing()
    {
        Assert.AreEqual(28f, CreatureMotion.BootSwing(Mathf.PI / 2f, 28f, 1f, true), 1e-4f);
        Assert.AreEqual(-28f, CreatureMotion.BootSwing(Mathf.PI / 2f, 28f, 1f, false), 1e-4f, "Right foot half a cycle behind");
        Assert.AreEqual(14f, CreatureMotion.BootSwing(Mathf.PI / 2f, 28f, 0.5f, true), 1e-4f, "Scaled by the walk weight");

        Goblin goblin = ModeledGoblin();
        goblin.Pose(Dt);
        for (int i = 0; i < 40; i++) { goblin.transform.position += Vector3.right * 0.06f; goblin.Pose(Dt); }
        Transform bootL = Pivot(goblin, CreatureModel.BootLeftPivot), bootR = Pivot(goblin, CreatureModel.BootRightPivot);
        Transform armL = Pivot(goblin, CreatureModel.ArmLeftPivot);
        Vector3 axis = Vector3.Cross(Vector3.up, Vector3.back); // Model-local: limbs swing along the model's own front (−Z)
        float l = SignedAngle(bootL.localRotation, axis), r = SignedAngle(bootR.localRotation, axis), a = SignedAngle(armL.localRotation, axis);
        Assert.AreEqual(-l, r, 0.5f, "Boots mirror each other");
        Assert.LessOrEqual(a * l, 0f, "Left arm swings against the left boot");
    }

    private static float SignedAngle(Quaternion q, Vector3 axis)
    {
        q.ToAngleAxis(out float angle, out Vector3 qAxis);
        if (angle > 180f) angle -= 360f;
        return Vector3.Dot(qAxis, axis) >= 0f ? angle : -angle;
    }

    [Test]
    public void Walk_Waddles_AndFeetLiftAndStepOut_SoItReadsFromTheCamera()
    {
        // The rotation-only swing of small boots under the vest was too subtle to see at game zoom: the walk
        // must change the silhouette — the body rocks foot to foot and the feet visibly step.
        Assert.AreEqual(8f, CreatureMotion.WaddleAngle(Mathf.PI / 2f, 8f, 1f), 1e-4f);
        Assert.AreEqual(-8f, CreatureMotion.WaddleAngle(3f * Mathf.PI / 2f, 8f, 1f), 1e-4f, "Rocks back the other way");

        Goblin goblin = ModeledGoblin();
        Transform bootL = Pivot(goblin, CreatureModel.BootLeftPivot), bootR = Pivot(goblin, CreatureModel.BootRightPivot);
        Vector3 restL = bootL.localPosition, restR = bootR.localPosition;
        goblin.Pose(Dt);

        float maxRock = 0f, maxLiftL = 0f, maxLiftR = 0f, maxReach = 0f;
        bool bothUpAtOnce = false;
        for (int i = 0; i < 60; i++)
        {
            goblin.transform.position += Vector3.right * 0.06f; // 3 t/s along +X
            goblin.Pose(Dt);
            maxRock = Mathf.Max(maxRock, Vector3.Angle(goblin.Model.transform.up, Vector3.up)); // Yaw doesn't tilt
            float liftL = bootL.localPosition.y - restL.y, liftR = bootR.localPosition.y - restR.y;
            maxLiftL = Mathf.Max(maxLiftL, liftL);
            maxLiftR = Mathf.Max(maxLiftR, liftR);
            maxReach = Mathf.Max(maxReach, Mathf.Abs(bootL.localPosition.z - restL.z)); // Along the model's front
            if (liftL > 0.01f && liftR > 0.01f) bothUpAtOnce = true;
        }
        Assert.Greater(maxRock, 6f, "The body rocks visibly");
        Assert.Greater(maxLiftL, 0.04f, "Left foot lifts");
        Assert.Greater(maxLiftR, 0.04f, "Right foot lifts");
        Assert.IsFalse(bothUpAtOnce, "Feet alternate: never both off the ground");
        Assert.Greater(maxReach, 0.04f, "Feet step out along travel");

        for (int i = 0; i < 30; i++) goblin.Pose(Dt); // Stop: settle back
        Assert.Less(Vector3.Distance(restL, bootL.localPosition), 1e-3f, "Feet plant back at rest");
        Assert.Less(Vector3.Angle(goblin.Model.transform.up, Vector3.up), 0.5f, "Upright at rest");
    }

    // --- Facing (#94): yaw 0 puts the model's front (−Z) toward the camera ---

    private static Vector3 Front(Goblin g) => g.Model.transform.TransformDirection(Vector3.back);

    [Test]
    public void HeadingYaw_PutsTheFrontAlongEachBoardDirection()
    {
        Assert.AreEqual(0f, CreatureMotion.HeadingYaw(Vector3.back), 1e-4f, "South (toward the camera): face visible");
        Assert.AreEqual(180f, Mathf.Abs(CreatureMotion.HeadingYaw(Vector3.forward)), 1e-4f, "North (away): back to camera");
        Assert.AreEqual(-90f, CreatureMotion.HeadingYaw(Vector3.right), 1e-4f, "East: profile");
        Assert.AreEqual(90f, CreatureMotion.HeadingYaw(Vector3.left), 1e-4f, "West: profile");
        foreach (Vector3 d in new[] { Vector3.back, Vector3.forward, Vector3.right, Vector3.left })
        {
            Vector3 front = CreatureMotion.YawRotation(CreatureMotion.HeadingYaw(d)) * Vector3.back;
            Assert.Less(Vector3.Distance(d, front), 1e-4f, $"Front lies along {d}");
        }
    }

    [Test]
    public void TurnToward_TakesTheShortArc_AtTheRate_WithoutOvershoot()
    {
        Assert.AreEqual(10f, CreatureMotion.TurnToward(0f, 90f, 10f), 1e-4f, "90° one way: turns that way");
        Assert.AreEqual(-10f, CreatureMotion.TurnToward(0f, -90f, 10f), 1e-4f, "90° the other way");
        Assert.AreEqual(-10f, CreatureMotion.TurnToward(0f, 270f, 10f), 1e-4f, "270° is 90° the short way");
        Assert.AreEqual(180f, CreatureMotion.TurnToward(170f, -170f, 10f), 1e-4f, "Across the ±180 seam, not the long way");
        Assert.AreEqual(5f, CreatureMotion.TurnToward(0f, 5f, 10f), 1e-4f, "Lands on the target, no overshoot");
        Assert.AreEqual(30f, CreatureMotion.TurnToward(30f, 90f, 0f), "Zero step: no change");

        float yaw = 0f, prevGap = 180f;
        for (int i = 0; i < 40; i++)
        {
            yaw = CreatureMotion.TurnToward(yaw, 135f, 10.8f);
            float gap = Mathf.Abs(Mathf.DeltaAngle(yaw, 135f));
            Assert.LessOrEqual(gap, prevGap, "Closes monotonically, never hunts");
            prevGap = gap;
        }
        Assert.AreEqual(135f, yaw, 1e-4f, "Arrives and holds");
    }

    [Test]
    public void SelectFacingYaw_MovingBeatsWorkingBeatsLastHeading()
    {
        Assert.AreEqual(-90f, CreatureMotion.SelectFacingYaw(Vector3.right, false, Vector3.left, 45f), 1e-4f, "Moving wins over work");
        Assert.AreEqual(90f, CreatureMotion.SelectFacingYaw(null, false, Vector3.left, 45f), 1e-4f, "Stationary: face the work");
        Assert.AreEqual(45f, CreatureMotion.SelectFacingYaw(null, false, null, 45f), "Idle: keep the last heading");
        Assert.AreEqual(180f, CreatureMotion.SelectFacingYaw(null, true, Vector3.left, 150f), "Asleep: squared to the nearer of ±Z");
        Assert.AreEqual(0f, CreatureMotion.SleepYaw(-60f));
        Assert.AreEqual(0f, CreatureMotion.SleepYaw(90f), "A tie faces the camera");
        Assert.AreEqual(180f, CreatureMotion.SleepYaw(-120f));
    }

    [Test]
    public void Walking_TurnsTowardTravel_AtTheTurnRate_ThenKeepsTheHeadingWhenIdle()
    {
        Goblin goblin = ModeledGoblin();
        float rate = new MotionTuning().turnDegreesPerSecond;
        goblin.Pose(Dt);
        Assert.AreEqual(0f, goblin.Motion.Yaw, 1e-4f, "Spawns facing the camera");

        goblin.transform.position += Vector3.forward * 0.06f; // North, away from the camera
        goblin.Pose(Dt);
        Assert.AreEqual(rate * Dt, Mathf.Abs(goblin.Motion.Yaw), 1e-3f, "One step of turn, not a snap");

        for (int i = 0; i < 30; i++) { goblin.transform.position += Vector3.forward * 0.06f; goblin.Pose(Dt); }
        Assert.Less(Vector3.Distance(Front(goblin), Vector3.forward), 0.01f, "Back to the camera, walking north");

        for (int i = 0; i < 100; i++) goblin.Pose(Dt); // Idle for 2 s
        Assert.Less(Vector3.Distance(Front(goblin), Vector3.forward), 0.01f, "Keeps its heading: no snap back to the camera");

        for (int i = 0; i < 30; i++) { goblin.transform.position += Vector3.right * 0.06f; goblin.Pose(Dt); }
        // Mid-walk the waddle (a roll about Z) tilts an east-pointing front out of the ground plane, so read the yaw.
        Assert.AreEqual(-90f, goblin.Motion.Yaw, 1e-3f, "Corner: turns to profile walking east");
    }

    [Test]
    public void Paused_FreezesMidTurn_ResumeCompletesIt()
    {
        Goblin goblin = ModeledGoblin();
        goblin.Pose(Dt);
        for (int i = 0; i < 5; i++) { goblin.transform.position += Vector3.left * 0.06f; goblin.Pose(Dt); }
        float mid = goblin.Motion.Yaw;
        Assert.That(mid, Is.GreaterThan(5f).And.LessThan(85f), "Mid-turn toward west (90)");

        Quaternion frozen = goblin.Model.transform.localRotation;
        for (int i = 0; i < 20; i++) goblin.Pose(0f);
        Assert.AreEqual(mid, goblin.Motion.Yaw, "Paused: the turn holds");
        Assert.AreEqual(0f, Quaternion.Angle(frozen, goblin.Model.transform.localRotation), 1e-3f);

        for (int i = 0; i < 20; i++) goblin.Pose(Dt);
        Assert.AreEqual(90f, goblin.Motion.Yaw, 1e-3f, "Resumed: finishes turning west");
    }

    [Test]
    public void Stationary_Work_TurnsToFaceIt()
    {
        Goblin goblin = ModeledGoblin();
        CreatureMotion m = goblin.Motion;
        Vector3 east = goblin.transform.position + Vector3.right;
        for (int i = 0; i < 20; i++) m.Step(Dt, new CreatureMotion.Flags { Digging = true, FaceTarget = east });
        Assert.Less(Vector3.Distance(Front(goblin), Vector3.right), 0.01f, "Faces the work tile");

        for (int i = 0; i < 20; i++) m.Step(Dt, new CreatureMotion.Flags());
        Assert.Less(Vector3.Distance(Front(goblin), Vector3.right), 0.01f, "Work done: keeps facing it");

        for (int i = 0; i < 20; i++)
        {
            goblin.transform.position += Vector3.back * 0.06f;
            m.Step(Dt, new CreatureMotion.Flags { FaceTarget = goblin.transform.position + Vector3.right });
        }
        Assert.Less(Vector3.Distance(Front(goblin), Vector3.back), 0.01f, "Walking off: travel beats the work");
    }

    [Test]
    public void Sleeping_AfterWalkingNorth_LiesOnTheCotFacingNorth_HeadToThePillow()
    {
        Goblin goblin = ModeledGoblin();
        CreatureMotion m = goblin.Motion;
        m.Step(Dt, new CreatureMotion.Flags());
        for (int i = 0; i < 30; i++) { goblin.transform.position += Vector3.forward * 0.06f; m.Step(Dt, new CreatureMotion.Flags()); }
        for (int i = 0; i < 30; i++) m.Step(Dt, new CreatureMotion.Flags { Sleeping = true });

        Assert.Less(goblin.Model.transform.up.x, -0.95f, "Lying along the cot, head toward the pillow (−X)");
        Assert.Greater(Vector3.Dot(Front(goblin), Vector3.forward), 0.95f, "Still facing north, on its side");
    }

    [Test]
    public void Sleeping_AfterWalkingEast_SquaresToTheCot_HeadStillToThePillow()
    {
        Goblin goblin = ModeledGoblin();
        CreatureMotion m = goblin.Motion;
        m.Step(Dt, new CreatureMotion.Flags());
        for (int i = 0; i < 30; i++) { goblin.transform.position += Vector3.right * 0.06f; m.Step(Dt, new CreatureMotion.Flags()); }
        for (int i = 0; i < 30; i++) m.Step(Dt, new CreatureMotion.Flags { Sleeping = true });

        Assert.Less(goblin.Model.transform.up.x, -0.95f, "Head on the pillow whatever the arrival heading");
        Assert.Greater(Mathf.Abs(Front(goblin).z), 0.95f, "On its side (facing ±Z), not face-down or face-up");
    }

    [Test]
    public void Death_TipsOverSidewaysRelativeToFacing()
    {
        Goblin goblin = ModeledGoblin();
        goblin.Pose(Dt);
        for (int i = 0; i < 30; i++) { goblin.transform.position += Vector3.right * 0.06f; goblin.Pose(Dt); }
        for (int i = 0; i < 20; i++) goblin.Pose(Dt); // Stand still facing east

        goblin.Motion.BeginDeath();
        for (int i = 0; i < 30; i++) goblin.Motion.Step(Dt, new CreatureMotion.Flags { Dead = true });
        Vector3 up = goblin.Model.transform.up;
        Assert.Greater(Mathf.Abs(up.z), 0.95f, "Fell to its side (across its east facing), not on its face or back");
        Assert.Less(Mathf.Abs(up.x), 0.1f);
    }

    // --- Pose selection and blending ---

    [Test]
    public void SelectPose_DeathOverSleepOverDigOverWalkOverIdle()
    {
        Assert.AreEqual(CreatureMotion.PoseKind.Dead, CreatureMotion.SelectPose(true, true, true, true));
        Assert.AreEqual(CreatureMotion.PoseKind.Sleep, CreatureMotion.SelectPose(false, true, true, true));
        Assert.AreEqual(CreatureMotion.PoseKind.Dig, CreatureMotion.SelectPose(false, false, true, true));
        Assert.AreEqual(CreatureMotion.PoseKind.Walk, CreatureMotion.SelectPose(false, false, false, true));
        Assert.AreEqual(CreatureMotion.PoseKind.Idle, CreatureMotion.SelectPose(false, false, false, false));
    }

    [Test]
    public void StartingToWalk_BlendsIn_NoSnap()
    {
        Goblin goblin = ModeledGoblin();
        goblin.Pose(Dt);
        goblin.transform.position += Vector3.right * 0.06f; // 3 t/s
        goblin.Pose(Dt);

        float w1 = goblin.Motion.WalkWeight;
        Assert.Greater(w1, 0f, "Started");
        Assert.Less(w1, 0.5f, "But only a blend step in (poseBlendSeconds 0.2 at 0.02 s steps)");
        for (int i = 0; i < 20; i++) { goblin.transform.position += Vector3.right * 0.06f; goblin.Pose(Dt); }
        Assert.AreEqual(1f, goblin.Motion.WalkWeight, 1e-4f, "Fully walking after the blend time");
        Assert.AreEqual(CreatureMotion.PoseKind.Walk, goblin.Motion.CurrentPose);

        for (int i = 0; i < 3; i++) goblin.Pose(Dt); // Stopped
        Assert.Greater(goblin.Motion.WalkWeight, 0f, "Stopping eases out too");
        Assert.AreEqual(CreatureMotion.PoseKind.Idle, goblin.Motion.CurrentPose);
    }

    [Test]
    public void Idle_Breathes()
    {
        Goblin goblin = ModeledGoblin();
        Transform body = Pivot(goblin, CreatureModel.BodyPivot);
        float min = float.MaxValue, max = float.MinValue;
        for (int i = 0; i < 250; i++) // 5 s: more than a breath at 0.25 Hz
        {
            goblin.Pose(Dt);
            min = Mathf.Min(min, body.localScale.y);
            max = Mathf.Max(max, body.localScale.y);
        }
        Assert.Greater(max, 1.01f);
        Assert.Less(min, 0.99f);
    }

    // --- Work and rest poses ---

    [Test]
    public void DigAim_ReadyRaiseStrikeRecover_OnTheOldTiming()
    {
        // #100: the chop aims the pick through points (was a fixed-axis roll); the phase timing is unchanged.
        MotionTuning t = MotionTuning.Imp();
        Assert.AreEqual(0.25f, t.digRaiseSeconds);
        Assert.AreEqual(0.1f, t.digStrikeSeconds);
        Assert.AreEqual(0.25f, t.digRecoverSeconds);

        Assert.AreEqual(t.digReadyPoint, CreatureMotion.DigAimPoint(0f, t), "Starts ready");
        AssertNear(t.digRaisePoint, CreatureMotion.DigAimPoint(t.digRaiseSeconds - 1e-4f, t), "Wound up at the end of the raise");
        AssertNear(t.digStrikePoint, CreatureMotion.DigAimPoint(t.digRaiseSeconds + t.digStrikeSeconds - 1e-4f, t), "Struck onto the tile");
        float period = t.digRaiseSeconds + t.digStrikeSeconds + t.digRecoverSeconds;
        AssertNear(t.digReadyPoint, CreatureMotion.DigAimPoint(period - 1e-4f, t), "Back to ready");
        AssertNear(CreatureMotion.DigAimPoint(0.1f, t), CreatureMotion.DigAimPoint(0.1f + period, t), "Loops");

        // Always in the plane toward the work, and the strike sits on the tile ahead at ground level.
        for (float c = 0f; c < period; c += 0.01f) Assert.AreEqual(0f, CreatureMotion.DigAimPoint(c, t).x, 1e-6f);
        Assert.Less(t.digStrikePoint.z, -0.5f, "On the tile a step ahead");
        Assert.Less(t.digStrikePoint.y, 0.1f, "At ground level");
        Assert.Greater(t.digRaisePoint.y, 0.9f, "Wound up overhead");
    }

    private static void AssertNear(Vector3 expected, Vector3 actual, string because) =>
        Assert.Less(Vector3.Distance(expected, actual), 2e-3f, $"{because}: expected {expected}, was {actual}");

    [Test]
    public void DigPose_ThePickStrikesTheTarget_AtAnyHeading()
    {
        // Mirrors #93's build-aim test: an imp facing a tile in each direction (its dig/rearm target via the
        // FaceTarget feed) — at the bottom of the strike the shoulder→pick-head line lands on that tile; at the
        // wind-up it's raised toward it, never out to the side.
        MotionTuning t = MotionTuning.Imp();
        foreach (Vector2Int offset in new[] { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) })
        {
            creatureGo = new GameObject("TestImp");
            var imp = creatureGo.AddComponent<Imp>();
            imp.Board = board;
            imp.Renderer = boardRenderer;
            imp.PlaceOnTile(new Vector2Int(24, 16));
            imp.CreateModel();
            Vector3 target = boardRenderer.GetTileCenterWorldPosition(24 + offset.x, 16 + offset.y);
            Vector3 toTarget = target - imp.transform.position;
            toTarget.y = 0f;

            float period = t.digRaiseSeconds + t.digStrikeSeconds + t.digRecoverSeconds;
            Ray strike = DigPickRay(imp, target, 2f * period + t.digRaiseSeconds + t.digStrikeSeconds); // Bottom of a strike
            Assert.Less(strike.direction.y, 0f, $"{offset}: striking down");
            float s = (0.05f - strike.origin.y) / strike.direction.y;
            Vector3 hit = strike.origin + strike.direction * s;
            Assert.Less(new Vector2(hit.x - target.x, hit.z - target.z).magnitude, 0.15f, $"{offset}: lands on the target tile's centre, hit {hit}");

            // On to the end of the next wind-up: through the recover, then (almost) the whole raise.
            Ray raise = DigPickRay(imp, target, t.digRecoverSeconds + t.digRaiseSeconds - Dt);
            Vector3 flat = raise.direction;
            flat.y = 0f;
            Assert.Greater(raise.direction.y, 0.5f, $"{offset}: wound up high");
            Assert.Greater(Vector3.Dot(flat.normalized, -toTarget.normalized), 0.5f, $"{offset}: wound up back over the shoulder, in line with the target");

            Object.DestroyImmediate(creatureGo);
        }
    }

    // Steps a digging imp facing target for the given seconds; returns the pick's world ray (shoulder → pick head).
    private static Ray DigPickRay(Imp imp, Vector3 target, float seconds)
    {
        for (float c = 0f; c < seconds - 1e-4f; c += Dt)
            imp.Motion.Step(Dt, new CreatureMotion.Flags { Digging = true, FaceTarget = target });
        Transform arm = CreatureModel.FindPivot(imp.Model, CreatureModel.ArmRightPivot);
        return new Ray(arm.position, arm.Find("PickHead").position - arm.position);
    }

    [Test]
    public void Sleeping_LiesDownOnTheCot_AndStandsBackUp()
    {
        Goblin goblin = ModeledGoblin();
        var ai = creatureGo.AddComponent<GoblinAI>();
        var t = new MotionTuning();

        // Drive the motion directly with the sleep flag (GoblinAI.IsSleeping is the source in game).
        CreatureMotion m = goblin.Motion;
        for (int i = 0; i < 20; i++) m.Step(Dt, new CreatureMotion.Flags { Sleeping = true });
        Assert.AreEqual(1f, m.SleepWeight, 1e-4f);
        Assert.AreEqual(CreatureMotion.PoseKind.Sleep, m.CurrentPose);
        Assert.AreEqual(t.sleepRollDegrees, goblin.Model.transform.localEulerAngles.z, 0.1f, "Lying down");
        Assert.AreEqual(t.sleepOffset.y, goblin.Model.transform.localPosition.y, 1e-3f, "Resting at cot height");

        for (int i = 0; i < 20; i++) m.Step(Dt, new CreatureMotion.Flags());
        Assert.AreEqual(0f, m.SleepWeight, 1e-4f, "Woke");
        Assert.AreEqual(0f, Quaternion.Angle(Quaternion.identity, goblin.Model.transform.localRotation), 0.1f, "Standing again");
    }

    [Test]
    public void Lunge_StepsInTowardTheTarget_AndRecovers()
    {
        var t = new MotionTuning();
        Assert.AreEqual(0f, CreatureMotion.LungeAmount(-1f, t), "Not lunging");
        Assert.AreEqual(1f, CreatureMotion.LungeAmount(t.lungeOutSeconds, t), 1e-4f, "Fully out");
        Assert.AreEqual(0f, CreatureMotion.LungeAmount(t.lungeOutSeconds + t.lungeRecoverSeconds + 0.01f, t), "Recovered");

        Goblin goblin = ModeledGoblin();
        goblin.Pose(Dt);
        goblin.NotifyAttack(goblin.transform.position + Vector3.right * 2f);
        for (int i = 0; i < 6; i++) goblin.Pose(Dt); // 0.12 s: peak
        Assert.Greater(goblin.Model.transform.localPosition.x, 0.1f, "Stepped in toward the target (+X)");
        for (int i = 0; i < 15; i++) goblin.Pose(Dt);
        Assert.AreEqual(0f, goblin.Model.transform.localPosition.x, 1e-3f, "Back in place");
    }

    [Test]
    public void Eating_DipsTheHead()
    {
        var t = new MotionTuning();
        Assert.AreEqual(1f, CreatureMotion.EatDipAmount(t.eatDipSeconds * 0.5f, t), 1e-4f, "Deepest mid-dip");
        Assert.AreEqual(0f, CreatureMotion.EatDipAmount(t.eatDipSeconds, t), "Done");

        Goblin goblin = ModeledGoblin();
        goblin.NotifyEat();
        for (int i = 0; i < 10; i++) goblin.Pose(Dt); // 0.2 s: mid-dip
        float pitch = goblin.Model.transform.Find(CreatureModel.HeadPivot).localEulerAngles.x;
        if (pitch > 180f) pitch -= 360f;
        Assert.Less(pitch, -t.eatDipDegrees * 0.8f, "Head dipped toward the food (forward, −X pitch)");
    }

    // --- Death: tip over, then deactivate ---

    [Test]
    public void Death_TipsOver_ThenDeactivates_IsDeadFromTheStart()
    {
        Goblin goblin = ModeledGoblin();
        goblin.StarvationSecondsToDie = 0.1f;
        goblin.Hunger = 0f;
        float tip = new MotionTuning().tipOverSeconds;

        for (int i = 0; i < 6 && !goblin.IsDead; i++) { goblin.Tick(Dt); goblin.Pose(Dt); }
        Assert.IsTrue(goblin.IsDead, "Logically dead at once");
        Assert.IsTrue(creatureGo.activeSelf, "Still showing: the tip-over plays first");

        float tipped = 0f;
        while (tipped < tip * 0.5f) { goblin.Pose(Dt); tipped += Dt; }
        Assert.IsTrue(creatureGo.activeSelf, "Mid-fall: still active");
        float midAngle = Quaternion.Angle(Quaternion.identity, goblin.Model.transform.localRotation);
        Assert.Greater(midAngle, 5f, "Falling");

        while (tipped < tip + Dt) { goblin.Pose(Dt); tipped += Dt; }
        Assert.IsFalse(creatureGo.activeSelf, "Deactivated when the fall ends");
        Assert.Greater(Quaternion.Angle(Quaternion.identity, goblin.Model.transform.localRotation), midAngle, "Fell over");
    }

    [Test]
    public void DyingAsleep_StaysLyingDown_NoPopUpright()
    {
        Goblin goblin = ModeledGoblin();
        CreatureMotion m = goblin.Motion;
        for (int i = 0; i < 20; i++) m.Step(Dt, new CreatureMotion.Flags { Sleeping = true });
        Quaternion lying = goblin.Model.transform.localRotation;

        m.BeginDeath();
        for (int i = 0; i < 10; i++)
        {
            m.Step(Dt, new CreatureMotion.Flags { Dead = true });
            Assert.AreEqual(0f, Quaternion.Angle(lying, goblin.Model.transform.localRotation), 0.5f, "Never stands up to fall");
        }
    }

    [Test]
    public void BareCreature_WithoutAModel_DeactivatesAtOnce_AsBefore()
    {
        creatureGo = new GameObject("BareGoblin");
        var goblin = creatureGo.AddComponent<Goblin>();
        goblin.Board = board;
        goblin.LogStats = false;
        goblin.StarvationSecondsToDie = 0.01f;
        goblin.Hunger = 0f;
        goblin.Tick(Dt);
        Assert.IsTrue(goblin.IsDead);
        Assert.IsFalse(creatureGo.activeSelf, "No model, no tip-over: deactivated in the same step");
        Assert.IsNull(goblin.Motion);
        Assert.DoesNotThrow(() => goblin.Pose(Dt), "Posing a bare creature no-ops");
    }

    [Test]
    public void Hero_TipsOverOnDeath()
    {
        creatureGo = new GameObject("TestHero");
        var health = creatureGo.AddComponent<Health>();
        health.MaxHealth = 5;
        var hero = creatureGo.AddComponent<Hero>();
        hero.Board = board;
        hero.Renderer = boardRenderer;
        hero.Health = health;
        hero.PlaceOnTile(new Vector2Int(24, 16));
        hero.CreateModel();

        health.TakeDamage(5);
        hero.Tick(Dt);
        hero.Pose(Dt);
        Assert.IsTrue(hero.IsDead);
        Assert.IsTrue(creatureGo.activeSelf, "Tipping");
        for (int i = 0; i < 30; i++) hero.Pose(Dt);
        Assert.IsFalse(creatureGo.activeSelf, "Gone after the fall");
    }

    [Test]
    public void MissingPivots_PoseSafely()
    {
        // A model built without pivots (e.g. a prop recipe) still poses its root without throwing.
        creatureGo = new GameObject("Owner");
        GameObject flat = CreatureModel.Build(creatureGo.transform, "Flat", PropModel.SpikeTrapPlateRecipe(0.8f, Color.gray), 0.2f);
        var m = new CreatureMotion(creatureGo.transform, flat, new MotionTuning());
        Assert.DoesNotThrow(() =>
        {
            for (int i = 0; i < 10; i++) { creatureGo.transform.position += Vector3.right * 0.05f; m.Step(Dt, new CreatureMotion.Flags { Digging = true }); }
            m.NotifyAttack(Vector3.zero);
            m.NotifyEat();
            m.BeginDeath();
            m.Step(Dt, new CreatureMotion.Flags { Dead = true });
        });
    }
}
