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
        Vector3 axis = Vector3.Cross(Vector3.up, Vector3.right); // Walking +X swings about this axis
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
    public void DigArm_RaisesStrikesAndRecovers()
    {
        var t = new MotionTuning();
        Assert.AreEqual(0f, CreatureMotion.DigArmAngle(0f, t), 1e-4f, "Starts at rest");
        Assert.AreEqual(t.digRaiseDegrees, CreatureMotion.DigArmAngle(t.digRaiseSeconds, t), 1e-3f, "Up at the end of the raise");
        Assert.AreEqual(t.digStrikeDegrees, CreatureMotion.DigArmAngle(t.digRaiseSeconds + t.digStrikeSeconds - 1e-4f, t), 0.5f, "Strike follows through");
        float period = t.digRaiseSeconds + t.digStrikeSeconds + t.digRecoverSeconds;
        Assert.AreEqual(0f, CreatureMotion.DigArmAngle(period - 1e-4f, t), 0.5f, "Back to rest");
        Assert.AreEqual(CreatureMotion.DigArmAngle(0.1f, t), CreatureMotion.DigArmAngle(0.1f + period, t), 1e-3f, "Loops");
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
