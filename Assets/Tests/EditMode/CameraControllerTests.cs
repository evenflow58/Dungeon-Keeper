using NUnit.Framework;
using UnityEngine;

public class CameraControllerTests
{
    private GameObject cameraObject;
    private Camera cam;
    private CameraController controller;

    [SetUp]
    public void SetUp()
    {
        cameraObject = new GameObject("TestCamera");
        cam = cameraObject.AddComponent<Camera>();
        cam.orthographic = true;
        controller = cameraObject.AddComponent<CameraController>();
    }

    [TearDown]
    public void TearDown()
    {
        if (cameraObject != null)
        {
            Object.DestroyImmediate(cameraObject);
        }
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

        // Visible ground dimensions:
        // width = 2 * size * aspect
        // height = (2 * size) / cos(pitch)
        // With size = 15 and aspect = 2.0:
        // visible width = 60 > 48 (BoardWidth)
        // visible height ≈ 42.4 > 32 (BoardHeight)
        cam.orthographicSize = 15f;
        cam.aspect = 2.0f;
        controller.UpdateCameraTransform();

        // Attempt to move target away from center
        controller.SetTargetGroundPos(new Vector3(10f, 10f, 0f));
        controller.ClampCamera();

        Assert.AreEqual(0f, controller.TargetGroundPos.x, 0.001f,
            "Target X should be centered at 0 when visible width exceeds board width");
        Assert.AreEqual(0f, controller.TargetGroundPos.y, 0.001f,
            "Target Y should be centered at 0 when visible height exceeds board height");
        Assert.AreEqual(0f, controller.CurrentGroundPos.x, 0.001f,
            "Current X should be centered at 0 when visible width exceeds board width");
        Assert.AreEqual(0f, controller.CurrentGroundPos.y, 0.001f,
            "Current Y should be centered at 0 when visible height exceeds board height");
    }

    [Test]
    public void ClampCamera_CentersOnlyExceedingAxis_WhenOnlyOneAxisExceedsBoard()
    {
        controller.PitchAngle = 45f;
        controller.ComputeBaseOrthographicSize();

        // With size = 8 and aspect = 4.0:
        // visible width = 2 * 8 * 4 = 64 > 48 (exceeds board on X)
        // visible height = 16 / cos(45°) ≈ 22.63 < 32 (fits within board on Y, halfH ≈ 11.31, allowed Y in [-4.69 .. 4.69])
        cam.orthographicSize = 8f;
        cam.aspect = 4.0f;
        controller.UpdateCameraTransform();

        // Move target to (10, 3, 0)
        controller.SetTargetGroundPos(new Vector3(10f, 3f, 0f));
        controller.ClampCamera();

        Assert.AreEqual(0f, controller.TargetGroundPos.x, 0.001f,
            "X should be centered to 0 because visible width (64) exceeds board width (48)");
        Assert.AreEqual(3f, controller.TargetGroundPos.y, 0.01f,
            "Y should NOT be forced to 0 because visible height (22.63) fits inside board height (32)");
    }
}
