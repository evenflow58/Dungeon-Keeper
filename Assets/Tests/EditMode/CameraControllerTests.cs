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
}
