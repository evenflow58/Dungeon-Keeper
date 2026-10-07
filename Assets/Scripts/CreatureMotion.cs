using UnityEngine;

/// <summary>Every motion tunable for one creature, serialized on its component (ticket #87).</summary>
[System.Serializable]
public class MotionTuning
{
    [Header("Walk")]
    public float strideLength = 0.35f;     // Tiles of travel per full cycle (two steps): feet track the ground
    public float swingDegrees = 28f;       // Boot swing at the ankles
    public float armSwingDegrees = 24f;    // Arms counter-swing against the same-side boot
    public float bobHeight = 0.03f;        // Tiles, twice per cycle
    public float leanDegrees = 5f;         // Body leans into the direction of travel
    public float headBobDegrees = 2f;      // Head counter-bob
    public float fullWalkSpeed = 1f;       // Tiles/s at which the walk pose is fully weighted

    [Header("Idle")]
    public float breathingRate = 0.25f;    // Hz
    public float breathingAmount = 0.02f;  // Body pivot scale, ±fraction
    public float headSwayDegrees = 1.5f;

    [Header("Blending")]
    public float poseBlendSeconds = 0.2f;  // Walk / sleep / dig weights ease over this

    [Header("Death")]
    public float tipOverSeconds = 0.35f;   // The fall; the GameObject deactivates when it ends
    public float tipOverDegrees = 85f;
    public float tipOverLift = 0.12f;      // Tiles: a fallen body lies on the floor, not through it

    [Header("Fight")]
    public float lungeOutSeconds = 0.12f;
    public float lungeRecoverSeconds = 0.18f;
    public float lungeDistance = 0.15f;    // Tiles toward the target
    public float lungeDegrees = 12f;       // Body pitch into the strike

    [Header("Eat")]
    public float eatDipSeconds = 0.4f;
    public float eatDipDegrees = 20f;

    [Header("Dig")]
    public float digRaiseSeconds = 0.25f;
    public float digStrikeSeconds = 0.1f;
    public float digRecoverSeconds = 0.25f;
    public float digRaiseDegrees = 110f;   // Pickaxe arm up and out
    public float digStrikeDegrees = -15f;  // Follow-through below rest
    public float digBodyDegrees = 6f;      // Body pitch on the strike

    [Header("Sleep")]
    public float sleepRollDegrees = 80f;   // Lies down head-toward the pillow (−X)
    public Vector3 sleepOffset = new Vector3(0.38f, 0.3f, 0f); // Model offset while lying on the cot
    public float sleepBreathingFactor = 0.5f; // Slower breathing asleep

    public static MotionTuning Goblin() => new MotionTuning();
    public static MotionTuning Imp() => new MotionTuning { strideLength = 0.28f, bobHeight = 0.025f };
    public static MotionTuning Hero() => new MotionTuning { strideLength = 0.4f, swingDegrees = 25f, armSwingDegrees = 18f, bobHeight = 0.025f };
}

/// <summary>
/// The procedural pose driver for one creature's code-built model (ticket #87). It poses the model's named pivots
/// (CreatureModel's ankles, shoulders, neck, body) and the model root from state the sim already exposes, stepped
/// on the creature's own game-time step so pause and 2× apply. Presentation only: it never moves the creature.
///   Walk — phase advances with distance actually traveled (no skating at any speed); boots swing, arms counter-
///          swing, body bobs and leans into the direction of travel, head counter-bobs.
///   Idle — breathing and a slight head sway when nothing else owns the frame.
///   Dig, Sleep, Eat, Lunge, Death — see their tunables in MotionTuning.
/// A creature without a model (bare-staged in tests) has no driver; posing then no-ops at the call site.
/// </summary>
public class CreatureMotion
{
    public enum PoseKind { Idle, Walk, Dig, Sleep, Dead }

    /// <summary>What the creature is doing this step (presentation inputs; derived from sim state).</summary>
    public struct Flags
    {
        public bool Digging;
        public bool Sleeping;
        public bool Dead;
    }

    private readonly Transform owner;
    private readonly Transform model;
    private readonly Transform body, head, bootLeft, bootRight, armLeft, armRight;
    private readonly Vector3 bodyRestScale;
    private readonly MotionTuning t;

    private Vector3 lastPosition;
    private Vector3 moveDirection = Vector3.back; // Toward the camera until it first moves
    private float digClock;
    private float lungeClock = -1f;
    private Vector3 lungeDirection;
    private float eatClock = -1f;
    private Quaternion deathStartRotation;
    private Vector3 deathStartPosition;

    public float Phase { get; private set; }
    public float WalkWeight { get; private set; }
    public float SleepWeight { get; private set; }
    public float DigWeight { get; private set; }
    public float Clock { get; private set; }
    public float DeathElapsed { get; private set; }
    public bool Dying { get; private set; }
    public PoseKind CurrentPose { get; private set; }

    /// <summary>True once the death tip-over has played out: the owner may deactivate.</summary>
    public bool DeathComplete => Dying && DeathElapsed >= t.tipOverSeconds;

    public Transform ModelRoot => model;

    public CreatureMotion(Transform owner, GameObject modelObject, MotionTuning tuning)
    {
        this.owner = owner;
        model = modelObject.transform;
        t = tuning ?? new MotionTuning();
        body = CreatureModel.FindPivot(modelObject, CreatureModel.BodyPivot);
        head = CreatureModel.FindPivot(modelObject, CreatureModel.HeadPivot);
        bootLeft = CreatureModel.FindPivot(modelObject, CreatureModel.BootLeftPivot);
        bootRight = CreatureModel.FindPivot(modelObject, CreatureModel.BootRightPivot);
        armLeft = CreatureModel.FindPivot(modelObject, CreatureModel.ArmLeftPivot);
        armRight = CreatureModel.FindPivot(modelObject, CreatureModel.ArmRightPivot);
        bodyRestScale = body != null ? body.localScale : Vector3.one;
        lastPosition = owner.position;
    }

    // ---- notifications (presentation only) ----

    /// <summary>An attack landed this moment: lunge toward the target.</summary>
    public void NotifyAttack(Vector3 targetPosition)
    {
        Vector3 d = targetPosition - owner.position;
        d.y = 0f;
        lungeDirection = d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.back;
        lungeClock = 0f;
    }

    /// <summary>Food was eaten this moment: dip the head.</summary>
    public void NotifyEat() => eatClock = 0f;

    /// <summary>Death: start the tip-over from whatever pose the model is in now.</summary>
    public void BeginDeath()
    {
        if (Dying) return;
        Dying = true;
        DeathElapsed = 0f;
        deathStartRotation = model.localRotation;
        deathStartPosition = model.localPosition;
    }

    // ---- the step ----

    /// <summary>
    /// Advances the pose by deltaTime (game time: 0 while paused, so the model freezes mid-stride) and applies it.
    /// The walk reads the owner's actual displacement since the last step.
    /// </summary>
    public void Step(float deltaTime, Flags flags)
    {
        Vector3 delta = owner.position - lastPosition;
        delta.y = 0f;
        lastPosition = owner.position;
        if (deltaTime <= 0f) return; // Paused: frozen exactly where it was

        float distance = delta.magnitude;
        if (distance > 1e-5f) moveDirection = delta / distance;
        CurrentPose = SelectPose(flags.Dead || Dying, flags.Sleeping, flags.Digging, distance > 1e-5f);

        if (Dying)
        {
            StepDeath(deltaTime);
            return;
        }

        Clock += deltaTime;
        Phase = AdvancePhase(Phase, distance, t.strideLength);
        float rate = t.poseBlendSeconds > 0f ? deltaTime / t.poseBlendSeconds : 1f;
        float speed = distance / deltaTime;
        WalkWeight = Mathf.MoveTowards(WalkWeight, flags.Sleeping ? 0f : Mathf.Clamp01(speed / Mathf.Max(1e-4f, t.fullWalkSpeed)), rate);
        SleepWeight = Mathf.MoveTowards(SleepWeight, flags.Sleeping ? 1f : 0f, rate);
        DigWeight = Mathf.MoveTowards(DigWeight, flags.Digging ? 1f : 0f, rate);
        if (flags.Digging) digClock += deltaTime;
        if (lungeClock >= 0f) lungeClock += deltaTime;
        if (eatClock >= 0f) eatClock += deltaTime;

        Apply();
    }

    private void Apply()
    {
        float w = WalkWeight;
        Vector3 axis = Vector3.Cross(Vector3.up, moveDirection); // Limbs swing and the body leans along travel

        // Walk: boots in opposite phase, arms counter-swing against the same-side boot.
        SetRotation(bootLeft, Quaternion.AngleAxis(BootSwing(Phase, t.swingDegrees, w, true), axis));
        SetRotation(bootRight, Quaternion.AngleAxis(BootSwing(Phase, t.swingDegrees, w, false), axis));
        Quaternion armLeftPose = Quaternion.AngleAxis(-BootSwing(Phase, t.armSwingDegrees, w, true), axis);
        Quaternion armRightPose = Quaternion.AngleAxis(-BootSwing(Phase, t.armSwingDegrees, w, false), axis);

        // Dig: the right (pickaxe) arm chops in a loop; the body pitches into each strike.
        float digArm = DigArmAngle(digClock, t) * DigWeight;
        if (DigWeight > 0f) armRightPose = Quaternion.Slerp(armRightPose, Quaternion.Euler(0f, 0f, digArm), DigWeight);
        SetRotation(armLeft, armLeftPose);
        SetRotation(armRight, armRightPose);

        // Idle breathing (slower asleep); it fades out while walking.
        float breathRate = t.breathingRate * Mathf.Lerp(1f, t.sleepBreathingFactor, SleepWeight);
        float breath = Mathf.Sin(Clock * breathRate * 2f * Mathf.PI) * (1f - w);
        if (body != null) body.localScale = new Vector3(bodyRestScale.x, bodyRestScale.y * (1f + t.breathingAmount * breath), bodyRestScale.z);

        // Body: lean into travel, pitch on dig strikes and lunges.
        float lunge = LungeAmount(lungeClock, t);
        float strike = DigWeight * DigStrikeAmount(digClock, t);
        Quaternion bodyPose = Quaternion.AngleAxis(t.leanDegrees * w + t.lungeDegrees * lunge, LeanAxis(axis, lunge))
                            * Quaternion.Euler(-t.digBodyDegrees * strike, 0f, 0f);
        SetRotation(body, bodyPose);

        // Head: counter-bob on the walk, a slight sway at rest, a dip when eating.
        float headPitch = t.headBobDegrees * Mathf.Sin(2f * Phase) * w - t.eatDipDegrees * EatDipAmount(eatClock, t);
        float headRoll = t.headSwayDegrees * Mathf.Sin(Clock * t.breathingRate * Mathf.PI) * (1f - w);
        SetRotation(head, Quaternion.Euler(headPitch, 0f, headRoll));

        // Model root: walk bob, lunge step-in, and the lie-down on a cot.
        Vector3 offset = Vector3.up * (t.bobHeight * Mathf.Abs(Mathf.Sin(Phase)) * w)
                       + lungeDirection * (t.lungeDistance * lunge)
                       + t.sleepOffset * SleepWeight;
        model.localPosition = offset;
        model.localRotation = Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(0f, 0f, t.sleepRollDegrees), SleepWeight);
    }

    // Lunges pitch toward the target; otherwise the body leans along travel.
    private Vector3 LeanAxis(Vector3 travelAxis, float lunge) =>
        lunge > 0f ? Vector3.Cross(Vector3.up, lungeDirection) : travelAxis;

    private void StepDeath(float deltaTime)
    {
        DeathElapsed += deltaTime;
        float p = t.tipOverSeconds > 0f ? Mathf.Clamp01(DeathElapsed / t.tipOverSeconds) : 1f;
        float eased = p * p; // Accelerates like a fall

        // Already lying (died asleep): stay down, no pop upright. Otherwise topple onto its right side.
        bool lying = SleepWeight > 0.5f;
        Quaternion target = lying ? deathStartRotation : Quaternion.Euler(0f, 0f, -t.tipOverDegrees);
        Vector3 targetPosition = lying ? deathStartPosition : new Vector3(0f, t.tipOverLift, 0f);
        model.localRotation = Quaternion.Slerp(deathStartRotation, target, eased);
        model.localPosition = Vector3.Lerp(deathStartPosition, targetPosition, eased);
    }

    private static void SetRotation(Transform pivot, Quaternion rotation)
    {
        if (pivot != null) pivot.localRotation = rotation;
    }

    // ---- pure math (EditMode-tested) ----

    /// <summary>Walk phase after traveling distance: one full cycle (2π) per strideLength — distance, not time.</summary>
    public static float AdvancePhase(float phase, float distance, float strideLength)
    {
        if (strideLength <= 0f) return phase;
        float next = phase + distance / strideLength * 2f * Mathf.PI;
        return next % (2f * Mathf.PI);
    }

    /// <summary>A boot's swing angle at a phase: the left leads, the right is half a cycle behind.</summary>
    public static float BootSwing(float phase, float swingDegrees, float weight, bool left) =>
        swingDegrees * Mathf.Sin(left ? phase : phase + Mathf.PI) * weight;

    /// <summary>Which pose owns the frame: death over sleep over dig over walk over idle.</summary>
    public static PoseKind SelectPose(bool dead, bool sleeping, bool digging, bool moving) =>
        dead ? PoseKind.Dead : sleeping ? PoseKind.Sleep : digging ? PoseKind.Dig : moving ? PoseKind.Walk : PoseKind.Idle;

    /// <summary>The pickaxe arm's roll at a point in the dig loop: raise (eased), strike, recover (eased).</summary>
    public static float DigArmAngle(float clock, MotionTuning t)
    {
        float period = t.digRaiseSeconds + t.digStrikeSeconds + t.digRecoverSeconds;
        if (period <= 0f) return 0f;
        float u = clock % period;
        if (u < t.digRaiseSeconds) return Mathf.Lerp(0f, t.digRaiseDegrees, Mathf.SmoothStep(0f, 1f, u / t.digRaiseSeconds));
        u -= t.digRaiseSeconds;
        if (u < t.digStrikeSeconds) return Mathf.Lerp(t.digRaiseDegrees, t.digStrikeDegrees, u / t.digStrikeSeconds);
        u -= t.digStrikeSeconds;
        return Mathf.Lerp(t.digStrikeDegrees, 0f, Mathf.SmoothStep(0f, 1f, u / Mathf.Max(1e-4f, t.digRecoverSeconds)));
    }

    // 1 at the moment of each strike, easing off through the recovery; 0 while raising.
    private static float DigStrikeAmount(float clock, MotionTuning t)
    {
        float period = t.digRaiseSeconds + t.digStrikeSeconds + t.digRecoverSeconds;
        if (period <= 0f) return 0f;
        float u = clock % period - t.digRaiseSeconds;
        if (u < 0f) return 0f;
        if (u < t.digStrikeSeconds) return u / t.digStrikeSeconds;
        return 1f - Mathf.Clamp01((u - t.digStrikeSeconds) / Mathf.Max(1e-4f, t.digRecoverSeconds));
    }

    /// <summary>Lunge extent 0..1 at a time since the attack: out quickly, recover; 0 when not lunging.</summary>
    public static float LungeAmount(float clock, MotionTuning t)
    {
        if (clock < 0f) return 0f;
        if (clock < t.lungeOutSeconds) return clock / t.lungeOutSeconds;
        float back = (clock - t.lungeOutSeconds) / Mathf.Max(1e-4f, t.lungeRecoverSeconds);
        return back >= 1f ? 0f : 1f - back;
    }

    /// <summary>Eating head-dip extent 0..1: a smooth bump over eatDipSeconds; 0 when not eating.</summary>
    public static float EatDipAmount(float clock, MotionTuning t)
    {
        if (clock < 0f || clock >= t.eatDipSeconds) return 0f;
        return Mathf.Sin(clock / t.eatDipSeconds * Mathf.PI);
    }
}
