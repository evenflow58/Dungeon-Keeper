using NUnit.Framework;
using UnityEngine;

public class CameraControllerTests
{
    private GameObject cameraObject;
    private GameObject boardObject;
    private Camera cam;
    private CameraController controller;

    [SetUp]
    public void SetUp()
    {
        boardObject = new GameObject("TestDungeonBoard");
        var board = boardObject.AddComponent<DungeonBoard>();
        board.InitializeBoard();

        cameraObject = new GameObject("TestCamera");
        cam = cameraObject.AddComponent<Camera>();
        cam.orthographic = true;
        controller = cameraObject.AddComponent<CameraController>();
        controller.Board = board;
    }

    [TearDown]
    public void TearDown()
    {
        if (cameraObject != null)
            Object.DestroyImmediate(cameraObject);
        if (boardObject != null)
            Object.DestroyImmediate(boardObject);
    }

    [Test]
    public void ComputeBaseOrthographicSize_At45DegreePitch_Equals13Point5()
    {
        controller.PitchAngle = 45f;
        controller.ComputeBaseOrthographicSize();

        Assert.AreEqual(13.5f, controller.BaseOrthographicSize, 0.0001f,
            "Base orthographic size at 45° pitch to frame 48x32 board at 16:9 should be 13.5");
    }

    [Test]
    public void ClampCamera_CentersCamera_WhenVisibleRectExceedsBoard()
    {
        controller.PitchAngle = 45f;
        controller.ComputeBaseOrthographicSize();

        // Visible ground dimensions on the XZ plane:
        // width (X) = 2 * size * aspect
        // depth (Z) = (2 * size) / sin(pitch)
        // With size = 15 and aspect = 2.0:
        // visible width = 60 > 48 (BoardWidth)
        // visible height ≈ 42.4 > 32 (BoardHeight)
        cam.orthographicSize = 15f;
        cam.aspect = 2.0f;
        controller.UpdateCameraTransform();

        // Attempt to move target away from center
        controller.SetTargetGroundPos(new Vector3(10f, 0f, 10f));
        controller.ClampCamera();

        Assert.AreEqual(0f, controller.TargetGroundPos.x, 0.001f,
            "Target X should be centered at 0 when visible width exceeds board width");
        Assert.AreEqual(0f, controller.TargetGroundPos.z, 0.001f,
            "Target Z should be centered at 0 when visible depth exceeds board height");
        Assert.AreEqual(0f, controller.CurrentGroundPos.x, 0.001f,
            "Current X should be centered at 0 when visible width exceeds board width");
        Assert.AreEqual(0f, controller.CurrentGroundPos.z, 0.001f,
            "Current Z should be centered at 0 when visible depth exceeds board height");
    }

    [Test]
    public void ClampCamera_CentersOnlyExceedingAxis_WhenOnlyOneAxisExceedsBoard()
    {
        controller.PitchAngle = 45f;
        controller.ComputeBaseOrthographicSize();

        // With size = 8 and aspect = 4.0:
        // visible width = 2 * 8 * 4 = 64 > 48 (exceeds board on X)
        // visible depth = 16 / sin(45°) ≈ 22.63 < 32 (fits within board on Z, halfH ≈ 11.31, allowed Z in [-4.69 .. 4.69])
        cam.orthographicSize = 8f;
        cam.aspect = 4.0f;
        controller.UpdateCameraTransform();

        // Move target to (10, 0, 3)
        controller.SetTargetGroundPos(new Vector3(10f, 0f, 3f));
        controller.ClampCamera();

        Assert.AreEqual(0f, controller.TargetGroundPos.x, 0.001f,
            "X should be centered to 0 because visible width (64) exceeds board width (48)");
        Assert.AreEqual(3f, controller.TargetGroundPos.z, 0.01f,
            "Z should NOT be forced to 0 because visible depth (22.63) fits inside board height (32)");
    }

    [Test]
    public void UpdateCameraTransform_LooksAtTheGroundPoint()
    {
        controller.PitchAngle = 45f;
        controller.ComputeBaseOrthographicSize();
        cam.orthographicSize = 6f;
        cam.aspect = 16f / 9f;
        controller.SetTargetGroundPos(new Vector3(3f, 0f, -2f));

        // The view's center ray lands on the ground point; the camera sits above and behind it (-Z).
        Assert.IsTrue(controller.RaycastGround(new Ray(cameraObject.transform.position, cameraObject.transform.forward), out Vector3 hit));
        Assert.AreEqual(3f, hit.x, 1e-3f);
        Assert.AreEqual(-2f, hit.z, 1e-3f);
        Assert.Greater(cameraObject.transform.position.y, 0f, "Above the ground");
        Assert.Less(cameraObject.transform.position.z, -2f, "Behind the ground point");
    }

    [Test]
    public void ZoomLimits_ScaleFromTheBoardFramingSize()
    {
        controller.PitchAngle = 45f;
        controller.ComputeBaseOrthographicSize();
        Assert.AreEqual(13.5f * 0.3f, controller.MinOrthographicSize, 1e-4f, "Default closest zoom: 0.3x the framing size");
        Assert.AreEqual(13.5f * 2f, controller.MaxOrthographicSize, 1e-4f, "Default farthest zoom: 2x the framing size");

        controller.MinZoomScale = 0.25f;
        controller.ComputeBaseOrthographicSize();
        Assert.AreEqual(13.5f * 0.25f, controller.MinOrthographicSize, 1e-4f, "Tunable");
    }

    // --- Zoom (#88): notch-correct, multiplicative, snappy ---

    private const float Min = 6.75f, Max = 27f, Step = 0.76f;

    [Test]
    public void NotchesFromScroll_OneNotchIsOneNotch_InEitherScrollMode()
    {
        // Input System 1.20 default (UniformAcrossAllPlatforms): ±1 per wheel notch. The old code divided by 120,
        // so a real notch moved the zoom 1/120th of a step: the "far too slow" bug.
        Assert.AreEqual(1f, CameraController.NotchesFromScroll(1f, true));
        Assert.AreEqual(-1f, CameraController.NotchesFromScroll(-1f, true));
        Assert.AreEqual(1f, CameraController.NotchesFromScroll(120f, false), 1e-6f, "Legacy platform range: 120 per notch");
    }

    [Test]
    public void OneNotch_ScalesTheTargetByTheStep_InAndOut()
    {
        Assert.AreEqual(13.5f * Step, CameraController.NextTargetSize(13.5f, 1f, Step, Min, Max), 1e-4f, "In");
        Assert.AreEqual(13.5f / Step, CameraController.NextTargetSize(13.5f, -1f, Step, Min, Max), 1e-4f, "Out");
        float twice = CameraController.NextTargetSize(CameraController.NextTargetSize(13.5f, 1f, Step, Min, Max), 1f, Step, Min, Max);
        Assert.AreEqual(13.5f * Step * Step, twice, 1e-4f, "Notches compound");
    }

    [Test]
    public void MaxToMin_InAboutFiveNotches_AndClamps()
    {
        float size = Max;
        int notches = 0;
        while (size > Min + 0.5f && notches < 50) { size = CameraController.NextTargetSize(size, 1f, Step, Min, Max); notches++; }
        Assert.That(notches, Is.InRange(4, 6), "Most of the range in 4-6 notches");

        Assert.AreEqual(Min, CameraController.NextTargetSize(Min, 3f, Step, Min, Max), "Clamped at min");
        Assert.AreEqual(Max, CameraController.NextTargetSize(Max, -3f, Step, Min, Max), "Clamped at max");
    }

    [Test]
    public void NearMin_SingleNotchesAreSmallAndPredictable()
    {
        float a = CameraController.NextTargetSize(10f, 1f, Step, Min, Max);
        Assert.AreEqual(10f * Step, a, 1e-4f, "Exactly one step, still above min");
        Assert.Less(Mathf.Abs(10f - a), 2.5f, "Fine-grained close in: about 2.4 units for one notch");
        Assert.Greater(Mathf.Abs(Max - CameraController.NextTargetSize(Max, 1f, Step, Min, Max)), 5f, "Big strides far out");
    }

    [Test]
    public void Settle_LandsInUnderAQuarterSecond_WithoutOvershoot_AtAnyFrameRate()
    {
        foreach (float dt in new[] { 1f / 30f, 1f / 60f, 1f / 144f })
        {
            float size = 27f, target = 6.75f, t = 0f, prev = size;
            while (size != target && t < 1f)
            {
                size = CameraController.SettleSize(size, target, 22f, dt);
                Assert.LessOrEqual(size, prev, $"Monotonic toward the target (dt {dt})");
                Assert.GreaterOrEqual(size, target, $"Never overshoots (dt {dt})");
                prev = size;
                t += dt;
            }
            Assert.AreEqual(target, size, $"Lands exactly (dt {dt})");
            Assert.Less(t, 0.4f, $"Snaps on, no long tail (dt {dt})");
            // 99% of the way there within the quarter second.
            float at25 = 27f;
            for (float u = 0f; u < 0.25f - 1e-6f; u += dt) at25 = CameraController.SettleSize(at25, target, 22f, dt);
            Assert.Less(Mathf.Abs(at25 - target), 0.01f * (27f - target) + 1e-3f, $"Within 1% by 0.25 s (dt {dt})");
        }
    }

    [Test]
    public void Settle_PausedClock_DoesNothing_ButUnscaledTimeKeepsItWorkingWhilePaused()
    {
        // The controller feeds Time.unscaledDeltaTime (never 0 while the game is paused); a zero step changes nothing.
        Assert.AreEqual(20f, CameraController.SettleSize(20f, 10f, 22f, 0f));
    }
}
