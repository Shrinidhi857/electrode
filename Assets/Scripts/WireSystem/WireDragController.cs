using UnityEngine;

/// <summary>
/// Controls interactive wire creation and routing via screen-space touch or mouse gestures.
/// Placed on a WireManager object in the scene.
/// </summary>
public class WireDragController : MonoBehaviour
{
    [Header("Camera")]
    [Tooltip("Camera used for screen-to-world projections. Defaults to Camera.main if null.")]
    [SerializeField] private Camera targetCamera;

    [Header("Wire Visual Configuration")]
    [Tooltip("Material assigned to wire cylinders and spheres.")]
    [SerializeField] private Material wireMaterial;

    [Tooltip("Color applied to the newly created wires.")]
    [SerializeField] private Color wireColor = new Color(0.92f, 0.22f, 0.22f, 1f);

    [Header("Screen Space Interaction (Pixels)")]
    [Tooltip("Maximum screen-space distance in pixels from a terminal to initiate dragging.")]
    [SerializeField] private float pickRadiusPx = 60f;

    [Tooltip("Maximum screen-space distance in pixels to snap onto a target terminal.")]
    [SerializeField] private float snapRadiusPx = 50f;

    [Header("Wire Geometry (Meters)")]
    [Tooltip("Radius of the cylindrical wire in world meters (~1.6mm radius = 3.2mm diameter).")]
    [SerializeField] private float wireRadius = 0.0016f;

    [Tooltip("Radius of the end spheres in world meters (~2.5mm radius = 5mm diameter).")]
    [SerializeField] private float endSphereRadius = 0.0025f;

    [Tooltip("Length of the straight stub leaving each terminal in world meters (~1.2cm).")]
    [SerializeField] private float stub = 0.012f;

    // Static flag exposed to AR manipulator/placement scripts to prevent conflicting gestures
    public static bool IsDragging { get; private set; } = false;

    // Runtime state
    private Wire activeWire;
    private TerminalPort startTerminal;
    private TerminalPort snappedTerminal;
    private bool isDraggingWire = false;

    private void Awake()
    {
        // Enforce realistic electrical wiring dimensions (proportional to 5.8cm MCB / 4.5cm Contactor)
        if (wireRadius > 0.01f || wireRadius < 0.0005f) wireRadius = 0.0016f;
        if (endSphereRadius > 0.015f || endSphereRadius < 0.001f) endSphereRadius = 0.0025f;
        if (stub > 0.05f || stub < 0.005f) stub = 0.012f;
    }

    private void Start()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }
    }

    private void Update()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }
        if (targetCamera == null) return;

        Vector2 screenPos;
        bool isDown = false;
        bool isHeld = false;
        bool isUp = false;

        // Input: Input.GetTouch(0) first, falling back to mouse for Editor testing
        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);
            screenPos = touch.position;
            isDown = (touch.phase == TouchPhase.Began);
            isHeld = (touch.phase == TouchPhase.Moved || touch.phase == TouchPhase.Stationary);
            isUp = (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled);
        }
        else
        {
            screenPos = Input.mousePosition;
            isDown = Input.GetMouseButtonDown(0);
            isHeld = Input.GetMouseButton(0);
            isUp = Input.GetMouseButtonUp(0);
        }

        if (isDown)
        {
            HandleTouchDown(screenPos);
        }
        else if (isHeld && isDraggingWire)
        {
            HandleTouchDrag(screenPos);
        }
        else if (isUp && isDraggingWire)
        {
            HandleTouchUp(screenPos);
        }
    }

    private void HandleTouchDown(Vector2 screenPos)
    {
        TerminalPort picked = FindNearestTerminal(screenPos, pickRadiusPx, null);
        if (picked != null && picked.CanAccept)
        {
            startTerminal = picked;
            snappedTerminal = null;
            isDraggingWire = true;
            IsDragging = true;

            // Instantiate wire GameObject at scene root with identity transform
            GameObject wireObj = new GameObject("Wire");
            wireObj.transform.SetParent(null);
            wireObj.transform.position = Vector3.zero;
            wireObj.transform.rotation = Quaternion.identity;
            wireObj.transform.localScale = Vector3.one;

            activeWire = wireObj.AddComponent<Wire>();
            activeWire.ConfigureVisuals(wireMaterial, wireColor, wireRadius, endSphereRadius, stub);
            activeWire.ShowFree(startTerminal, startTerminal.Position);
        }
    }

    private void HandleTouchDrag(Vector2 screenPos)
    {
        if (activeWire == null || startTerminal == null) return;

        // Check if another valid terminal (not startTerminal, and can accept) is within snapRadiusPx
        TerminalPort candidate = FindNearestTerminal(screenPos, snapRadiusPx, startTerminal);
        if (candidate != null && candidate.CanAccept)
        {
            snappedTerminal = candidate;
            activeWire.ShowSnapped(startTerminal, snappedTerminal);
        }
        else
        {
            snappedTerminal = null;

            // Project finger onto a plane through start terminal facing the camera
            Vector3 planeNormal = -targetCamera.transform.forward;
            Plane plane = new Plane(planeNormal, startTerminal.Position);
            Ray ray = targetCamera.ScreenPointToRay(screenPos);

            if (plane.Raycast(ray, out float enter))
            {
                Vector3 worldPos = ray.GetPoint(enter);
                activeWire.ShowFree(startTerminal, worldPos);
            }
        }
    }

    private void HandleTouchUp(Vector2 screenPos)
    {
        if (activeWire != null)
        {
            // If valid snapped terminal exists, commit connection
            if (snappedTerminal != null && snappedTerminal != startTerminal && snappedTerminal.CanAccept)
            {
                activeWire.Connect(startTerminal, snappedTerminal);
            }
            else
            {
                // Released in empty space -> destroy preview wire
                Destroy(activeWire.gameObject);
            }
        }

        activeWire = null;
        startTerminal = null;
        snappedTerminal = null;
        isDraggingWire = false;
        IsDragging = false;
    }

    /// <summary>
    /// Finds the nearest TerminalPort within screen-space pixel distance, ignoring terminals behind camera.
    /// </summary>
    private TerminalPort FindNearestTerminal(Vector2 screenPos, float maxRadiusPx, TerminalPort excludeTerminal)
    {
        TerminalPort nearest = null;
        float minDistance = maxRadiusPx;

        var all = TerminalPort.All;
        for (int i = 0; i < all.Count; i++)
        {
            TerminalPort port = all[i];
            if (port == null || port == excludeTerminal) continue;

            Vector3 screenPt = targetCamera.WorldToScreenPoint(port.Position);

            // Ignore points behind the camera
            if (screenPt.z <= 0.01f) continue;

            float distPx = Vector2.Distance(new Vector2(screenPt.x, screenPt.y), screenPos);
            if (distPx <= minDistance)
            {
                minDistance = distPx;
                nearest = port;
            }
        }

        return nearest;
    }

    private void OnDisable()
    {
        if (isDraggingWire && activeWire != null)
        {
            Destroy(activeWire.gameObject);
            activeWire = null;
        }
        isDraggingWire = false;
        IsDragging = false;
    }
}
