using UnityEngine;
using UnityEngine.InputSystem;

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

    // New Input System actions
    private InputAction wasdAction;
    private InputAction arrowsAction;
    private InputAction middleDragAction;
    private InputAction scrollAction;
    private InputAction mousePositionAction;

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
            cam.transparencySortMode = TransparencySortMode.CustomAxis;
            cam.transparencySortAxis = new Vector3(0f, 1f, 0f);
            cam.farClipPlane = Mathf.Max(cam.farClipPlane, 100f);
        }

        CreateInputActions();
    }

    private void OnEnable()
    {
        CreateInputActions();
        wasdAction?.Enable();
        arrowsAction?.Enable();
        middleDragAction?.Enable();
        scrollAction?.Enable();
        mousePositionAction?.Enable();
    }

    private void OnDisable()
    {
        wasdAction?.Disable();
        arrowsAction?.Disable();
        middleDragAction?.Disable();
        scrollAction?.Disable();
        mousePositionAction?.Disable();
    }

    private void OnDestroy()
    {
        wasdAction?.Dispose();
        arrowsAction?.Dispose();
        middleDragAction?.Dispose();
        scrollAction?.Dispose();
        mousePositionAction?.Dispose();
    }

    private void CreateInputActions()
    {
        if (wasdAction != null) return;

        // WASD composite
        wasdAction = new InputAction("PanWASD", InputActionType.Value);
        wasdAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w")
            .With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a")
            .With("Right", "<Keyboard>/d");

        // Arrow keys composite
        arrowsAction = new InputAction("PanArrows", InputActionType.Value);
        arrowsAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/upArrow")
            .With("Down", "<Keyboard>/downArrow")
            .With("Left", "<Keyboard>/leftArrow")
            .With("Right", "<Keyboard>/rightArrow");

        // Middle mouse drag
        middleDragAction = new InputAction("MiddleDrag", InputActionType.Button, "<Mouse>/middleButton");

        // Mouse wheel scroll
        scrollAction = new InputAction("ZoomScroll", InputActionType.Value, "<Mouse>/scroll");

        // Mouse pointer position
        mousePositionAction = new InputAction("MousePosition", InputActionType.Value, "<Mouse>/position");
    }

    private void Start()
    {
        cam ??= GetComponent<Camera>();

        cam.orthographic = true;
        cam.transparencySortMode = TransparencySortMode.CustomAxis;
        cam.transparencySortAxis = new Vector3(0f, 1f, 0f);

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

        // When looking at ground (z=0) with pitchAngle around X axis:
        // Visible ground height = (2 * orthoSize) / cos(pitchAngle)
        // Visible ground width = (2 * orthoSize) * aspect
        // To frame the board at 16:9:
        // orthoSize for board height: (Height/2) * cos(pitchAngle)
        // orthoSize for board width: (Width/2) / targetAspect
        // Taking max frames the full board at 16:9 aspect.
        const float targetAspect = 16f / 9f;
        float rad = pitchAngle * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad);

        float sizeForWidth = dungeonBoard.Width * 0.5f / targetAspect;
        float sizeForHeight = dungeonBoard.Height * 0.5f * cos;

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
        Vector2 scroll = Vector2.zero;
        if (scrollAction != null)
        {
            scroll = scrollAction.ReadValue<Vector2>();
        }

        if (scroll == Vector2.zero && Mouse.current != null)
        {
            scroll = Mouse.current.scroll.ReadValue();
        }

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
        bool middlePressedThisFrame = false;
        bool middleHeld = false;
        bool middleReleased = false;

        if (middleDragAction != null)
        {
            middlePressedThisFrame = middleDragAction.WasPressedThisFrame();
            middleHeld = middleDragAction.IsPressed();
            middleReleased = middleDragAction.WasReleasedThisFrame();
        }

        if (Mouse.current != null)
        {
            if (Mouse.current.middleButton.wasPressedThisFrame) middlePressedThisFrame = true;
            if (Mouse.current.middleButton.isPressed) middleHeld = true;
            if (Mouse.current.middleButton.wasReleasedThisFrame) middleReleased = true;
        }

        Vector2 mousePos = Vector2.zero;
        if (mousePositionAction != null)
        {
            mousePos = mousePositionAction.ReadValue<Vector2>();
        }
        if (mousePos == Vector2.zero && Mouse.current != null)
        {
            mousePos = Mouse.current.position.ReadValue();
        }

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
                float unitsPerPixelY = unitsPerPixelX / Mathf.Cos(pitchAngle * Mathf.Deg2Rad);

                Vector3 groundDelta = new Vector3(-mouseDelta.x * unitsPerPixelX, -mouseDelta.y * unitsPerPixelY, 0f);
                targetGroundPos += groundDelta;
                currentGroundPos += groundDelta;
            }
        }
    }

    private void HandleKeyboardPan()
    {
        if (isDragging) return;

        Vector2 moveInput = Vector2.zero;
        if (wasdAction != null) moveInput += wasdAction.ReadValue<Vector2>();
        if (arrowsAction != null) moveInput += arrowsAction.ReadValue<Vector2>();

        // Direct Keyboard fallback
        if (moveInput == Vector2.zero && Keyboard.current != null)
        {
            var kb = Keyboard.current;
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) moveInput.y += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) moveInput.y -= 1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) moveInput.x -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) moveInput.x += 1f;
            if (moveInput.sqrMagnitude > 1f) moveInput.Normalize();
        }

        if (moveInput.sqrMagnitude > 0.001f)
        {
            // Move in ground plane: camera-relative X and ground-projected forward (Y in ground plane)
            Vector3 moveDir = new Vector3(moveInput.x, moveInput.y, 0f);
            targetGroundPos += moveDir * (panSpeed * Time.unscaledDeltaTime); // Pans while paused, same feel at 2×
        }

        currentGroundPos = Vector3.Lerp(currentGroundPos, targetGroundPos, Time.unscaledDeltaTime * panDamping);
    }

    public void UpdateCameraTransform()
    {
        cam ??= GetComponent<Camera>();
        if (cam == null) return;

        // Orthographic, pitched pitchAngle degrees around X axis
        transform.rotation = Quaternion.Euler(pitchAngle, 0f, 0f);

        // Keep camera height/distance fixed
        float rad = pitchAngle * Mathf.Deg2Rad;
        float tan = Mathf.Tan(rad);

        float camZ = -cameraDistance;
        float camY = currentGroundPos.y - camZ * tan;
        float camX = currentGroundPos.x;

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
        float minY = -boardHeight * 0.5f;
        float maxY = minY + boardHeight;

        // Raycast the 4 screen corners onto the z=0 plane to get the visible ground rect
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
        float visMinY = Mathf.Min(Mathf.Min(pBL.y, pBR.y), Mathf.Min(pTL.y, pTR.y));
        float visMaxY = Mathf.Max(Mathf.Max(pBL.y, pBR.y), Mathf.Max(pTL.y, pTR.y));

        float visibleWidth = visMaxX - visMinX;
        float visibleHeight = visMaxY - visMinY;

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

        // Y axis clamping: if view is larger than board on an axis, center that axis on the board (0)
        if (visibleHeight >= boardHeight)
        {
            targetGroundPos.y = 0f;
            currentGroundPos.y = 0f;
        }
        else
        {
            float halfH = visibleHeight * 0.5f;
            targetGroundPos.y = Mathf.Clamp(targetGroundPos.y, minY + halfH, maxY - halfH);
            currentGroundPos.y = Mathf.Clamp(currentGroundPos.y, minY + halfH, maxY - halfH);
        }
    }

    public bool RaycastGround(Ray ray, out Vector3 hitPoint)
    {
        hitPoint = Vector3.zero;
        if (Mathf.Abs(ray.direction.z) < 1e-5f)
            return false;

        float t = -ray.origin.z / ray.direction.z;
        hitPoint = ray.origin + ray.direction * t;
        hitPoint.z = 0f;
        return true;
    }

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
