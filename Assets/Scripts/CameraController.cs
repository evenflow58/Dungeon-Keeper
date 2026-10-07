using UnityEngine;

[RequireComponent(typeof(Camera))]
public class CameraController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private DungeonBoard dungeonBoard;

    [Header("Camera Tuning")]
    [SerializeField] private float panSpeed = 20f;
    [SerializeField] private float zoomSpeed = 2f;
    [SerializeField] private float pitchAngle = 45f;

    [Header("Damping")]
    [SerializeField] private float panDamping = 12f;
    [SerializeField] private float zoomDamping = 12f;

    [Header("Distance")]
    [SerializeField] private float cameraDistance = 25f;

    private Camera cam;
    private float baseOrthographicSize;
    private float minOrthographicSize;
    private float maxOrthographicSize;
    private float targetOrthographicSize;

    private Vector3 currentGroundPos = Vector3.zero;
    private Vector3 targetGroundPos = Vector3.zero;

    private Vector2 lastMouseScreenPos;
    private bool isDragging;

    // Bindings come from InputConfig (rebindable); this component owns its own set's enable/disable.
    private InputConfig.Actions input;

    // Public properties
    public DungeonBoard Board { get => dungeonBoard; set => dungeonBoard = value; }
    public float PanSpeed { get => panSpeed; set => panSpeed = value; }
    public float ZoomSpeed { get => zoomSpeed; set => zoomSpeed = value; }
    public float PitchAngle { get => pitchAngle; set => pitchAngle = value; }
    public float BaseOrthographicSize => baseOrthographicSize;
    public float MinOrthographicSize => minOrthographicSize;
    public float MaxOrthographicSize => maxOrthographicSize;
    public Vector3 CurrentGroundPos => currentGroundPos;
    public Vector3 TargetGroundPos => targetGroundPos;

    private void Awake()
    {
        cam = GetComponent<Camera>() ?? Camera.main;

        if (cam != null)
        {
            cam.orthographic = true;
            cam.transparencySortMode = TransparencySortMode.Default; // Sort by view depth: real depth replaced the 2D axis
            cam.farClipPlane = Mathf.Max(cam.farClipPlane, 100f);
        }

        CreateInputActions();
    }

    private void OnEnable()
    {
        CreateInputActions();
        input.Enable();
    }

    private void OnDisable()
    {
        input?.Disable();
    }

    private void OnDestroy()
    {
        input?.Dispose();
    }

    private void CreateInputActions()
    {
        input ??= InputConfig.CreateActions();
    }

    private void Start()
    {
        cam ??= GetComponent<Camera>();

        cam.orthographic = true;
        cam.transparencySortMode = TransparencySortMode.Default;

        ComputeBaseOrthographicSize();
        cam.orthographicSize = baseOrthographicSize;
        targetOrthographicSize = baseOrthographicSize;

        currentGroundPos = Vector3.zero;
        targetGroundPos = Vector3.zero;

        UpdateCameraTransform();
        ClampCamera();
        UpdateCameraTransform();
    }

    public void ComputeBaseOrthographicSize()
    {
        if (dungeonBoard == null) return;

        // Looking down at the ground (y = 0, the XZ plane) pitchAngle degrees below the horizon:
        // Visible ground depth (Z) = (2 * orthoSize) / sin(pitchAngle)
        // Visible ground width (X) = (2 * orthoSize) * aspect
        // To frame the board at 16:9:
        // orthoSize for board depth: (Height/2) * sin(pitchAngle)
        // orthoSize for board width: (Width/2) / targetAspect
        // Taking max frames the full board at 16:9 aspect. (At 45° this matches the old XY presentation exactly.)
        const float targetAspect = 16f / 9f;
        float rad = pitchAngle * Mathf.Deg2Rad;
        float sin = Mathf.Sin(rad);

        float sizeForWidth = dungeonBoard.Width * 0.5f / targetAspect;
        float sizeForHeight = dungeonBoard.Height * 0.5f * sin;

        baseOrthographicSize = Mathf.Max(sizeForWidth, sizeForHeight);
        minOrthographicSize = baseOrthographicSize * 0.5f;
        maxOrthographicSize = baseOrthographicSize * 2.0f;
    }

    private void LateUpdate()
    {
        if (cam == null) return;

        HandleZoom();
        HandleMiddleMouseDrag();
        HandleKeyboardPan();

        UpdateCameraTransform();
        ClampCamera();
        UpdateCameraTransform();
    }

    private void HandleZoom()
    {
        Vector2 scroll = input?.Zoom.ReadValue<Vector2>() ?? Vector2.zero;

        if (Mathf.Abs(scroll.y) > 0.01f)
        {
            float scrollTicks = scroll.y / 120f;

            // Exponential / percentage zoom is responsive and intuitive across all zoom levels
            float zoomFactor = Mathf.Pow(1.25f, -scrollTicks * (zoomSpeed * 0.75f));
            targetOrthographicSize = Mathf.Clamp(targetOrthographicSize * zoomFactor, minOrthographicSize, maxOrthographicSize);
        }

        cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, targetOrthographicSize, Time.unscaledDeltaTime * zoomDamping); // View chrome: unaffected by game speed
    }

    private void HandleMiddleMouseDrag()
    {
        if (input == null) return;

        bool middlePressedThisFrame = input.PanDrag.WasPressedThisFrame();
        bool middleHeld = input.PanDrag.IsPressed();
        bool middleReleased = input.PanDrag.WasReleasedThisFrame();
        Vector2 mousePos = input.PointerPosition.ReadValue<Vector2>();

        if (middlePressedThisFrame)
        {
            lastMouseScreenPos = mousePos;
            isDragging = true;
        }

        if (middleReleased)
        {
            isDragging = false;
        }

        if (isDragging && middleHeld)
        {
            Vector2 mouseDelta = mousePos - lastMouseScreenPos;
            lastMouseScreenPos = mousePos;

            if (mouseDelta.sqrMagnitude > 0.0001f && cam.pixelHeight > 0)
            {
                // Frame-to-frame delta: ground movement 1:1 with mouse cursor movement
                float unitsPerPixelX = (cam.orthographicSize * 2f) / cam.pixelHeight;
                float unitsPerPixelZ = unitsPerPixelX / Mathf.Sin(pitchAngle * Mathf.Deg2Rad); // Screen up = ground +Z, foreshortened

                Vector3 groundDelta = new Vector3(-mouseDelta.x * unitsPerPixelX, 0f, -mouseDelta.y * unitsPerPixelZ);
                targetGroundPos += groundDelta;
                currentGroundPos += groundDelta;
            }
        }
    }

    private void HandleKeyboardPan()
    {
        if (isDragging) return;

        // One Pan action holds both the WASD and arrow-key composites (InputConfig).
        Vector2 moveInput = input?.Pan.ReadValue<Vector2>() ?? Vector2.zero;

        if (moveInput.sqrMagnitude > 0.001f)
        {
            // Move in the ground plane: camera-relative X and ground-projected forward (+Z)
            Vector3 moveDir = new Vector3(moveInput.x, 0f, moveInput.y);
            targetGroundPos += moveDir * (panSpeed * Time.unscaledDeltaTime); // Pans while paused, same feel at 2×
        }

        currentGroundPos = Vector3.Lerp(currentGroundPos, targetGroundPos, Time.unscaledDeltaTime * panDamping);
    }

    public void UpdateCameraTransform()
    {
        cam ??= GetComponent<Camera>();
        if (cam == null) return;

        // Orthographic, pitched pitchAngle degrees down from the horizon, looking toward +Z
        transform.rotation = Quaternion.Euler(pitchAngle, 0f, 0f);

        // Keep camera height/distance fixed: cameraDistance back from the ground point, raised so it looks at it
        float rad = pitchAngle * Mathf.Deg2Rad;
        float tan = Mathf.Tan(rad);

        float camX = currentGroundPos.x;
        float camY = cameraDistance * tan;
        float camZ = currentGroundPos.z - cameraDistance;

        transform.position = new Vector3(camX, camY, camZ);
    }

    public void ClampCamera()
    {
        if (cam == null) return;
        if (dungeonBoard == null) return;

        float boardWidth = dungeonBoard.Width;
        float boardHeight = dungeonBoard.Height;
        float minX = -boardWidth * 0.5f;
        float maxX = minX + boardWidth;
        float minZ = -boardHeight * 0.5f; // Board rows run along Z on the ground plane
        float maxZ = minZ + boardHeight;

        // Raycast the 4 screen corners onto the ground plane (y = 0) to get the visible ground rect
        Ray rayBL = cam.ViewportPointToRay(new Vector3(0f, 0f, 0f));
        Ray rayBR = cam.ViewportPointToRay(new Vector3(1f, 0f, 0f));
        Ray rayTL = cam.ViewportPointToRay(new Vector3(0f, 1f, 0f));
        Ray rayTR = cam.ViewportPointToRay(new Vector3(1f, 1f, 0f));

        if (!RaycastGround(rayBL, out Vector3 pBL) ||
            !RaycastGround(rayBR, out Vector3 pBR) ||
            !RaycastGround(rayTL, out Vector3 pTL) ||
            !RaycastGround(rayTR, out Vector3 pTR))
        {
            return;
        }

        float visMinX = Mathf.Min(Mathf.Min(pBL.x, pBR.x), Mathf.Min(pTL.x, pTR.x));
        float visMaxX = Mathf.Max(Mathf.Max(pBL.x, pBR.x), Mathf.Max(pTL.x, pTR.x));
        float visMinZ = Mathf.Min(Mathf.Min(pBL.z, pBR.z), Mathf.Min(pTL.z, pTR.z));
        float visMaxZ = Mathf.Max(Mathf.Max(pBL.z, pBR.z), Mathf.Max(pTL.z, pTR.z));

        float visibleWidth = visMaxX - visMinX;
        float visibleHeight = visMaxZ - visMinZ;

        // X axis clamping: if view is larger than board on an axis, center that axis on the board (0)
        if (visibleWidth >= boardWidth)
        {
            targetGroundPos.x = 0f;
            currentGroundPos.x = 0f;
        }
        else
        {
            float halfW = visibleWidth * 0.5f;
            targetGroundPos.x = Mathf.Clamp(targetGroundPos.x, minX + halfW, maxX - halfW);
            currentGroundPos.x = Mathf.Clamp(currentGroundPos.x, minX + halfW, maxX - halfW);
        }

        // Z axis clamping: if view is larger than board on an axis, center that axis on the board (0)
        if (visibleHeight >= boardHeight)
        {
            targetGroundPos.z = 0f;
            currentGroundPos.z = 0f;
        }
        else
        {
            float halfH = visibleHeight * 0.5f;
            targetGroundPos.z = Mathf.Clamp(targetGroundPos.z, minZ + halfH, maxZ - halfH);
            currentGroundPos.z = Mathf.Clamp(currentGroundPos.z, minZ + halfH, maxZ - halfH);
        }
    }

    /// <summary>The ground-plane (y = 0) hit of a ray; the same math as TileHover.RaycastGroundPlane.</summary>
    public bool RaycastGround(Ray ray, out Vector3 hitPoint) => TileHover.RaycastGroundPlane(ray, out hitPoint);

    public void SetTargetGroundPos(Vector3 pos)
    {
        targetGroundPos = pos;
        currentGroundPos = pos;
        UpdateCameraTransform();
        ClampCamera();
        UpdateCameraTransform();
    }

    private void OnValidate()
    {
        pitchAngle = Mathf.Clamp(pitchAngle, 5f, 85f);
        cam ??= GetComponent<Camera>();
        if (cam != null && !Application.isPlaying)
        {
            ComputeBaseOrthographicSize();
            cam.orthographic = true;
            cam.orthographicSize = baseOrthographicSize;
            targetOrthographicSize = baseOrthographicSize;
            UpdateCameraTransform();
            ClampCamera();
            UpdateCameraTransform();
        }
    }
}
