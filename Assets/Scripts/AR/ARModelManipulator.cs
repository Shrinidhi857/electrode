using System;
using System.Collections.Generic;
using UnityEngine;
using ContactorAR.Breaker;
using ContactorAR.Wiring;

namespace ContactorAR.AR
{
    public enum PlacementMode
    {
        FreeMovable,
        HangingAtCoordinates,
        Locked
    }

    /// <summary>
    /// Handles AR touch gestures for contactor / breaker models:
    /// - Centralized selection: only the tapped/selected model responds to scale, rotate, and move gestures.
    /// - 1-Finger Drag to move across the view plane with zero jump (using hit point offset).
    /// - 2-Finger Pinch to scale ONLY the selected model.
    /// - 2-Finger Twist to rotate ONLY the selected model around Y axis.
    /// - 2-Finger Parallel Drag (or Mouse Scroll) to move closer / further in Z (depth in 3D).
    /// - Coordinate pinning / locking with ARAnchor when locked.
    /// </summary>
    [SelectionBase]
    public class ARModelManipulator : MonoBehaviour
    {
        [Header("Placement & Hanging")]
        [SerializeField] private PlacementMode placementMode = PlacementMode.FreeMovable;

        [Tooltip("Specific world coordinate where the contactor is hung / pinned.")]
        [SerializeField] private Vector3 hangingTargetCoordinate = new Vector3(0f, 0f, 1.2f);

        [Tooltip("Target rotation when hung.")]
        [SerializeField] private Vector3 hangingTargetEuler = new Vector3(0f, 180f, 0f);

        [Tooltip("Speed when smoothly moving to hanging coordinates.")]
        [SerializeField] private float hangingLerpSpeed = 8.0f;

        [Header("Pinch-to-Scale Settings")]
        [SerializeField] private float minScale = 0.5f;
        [SerializeField] private float maxScale = 30.0f;
        [SerializeField] private float pinchSensitivity = 0.005f;

        [Header("Rotation Settings")]
        [SerializeField] private float rotationSensitivity = 0.4f;

        [Header("Layer / Raycast Mask")]
        [SerializeField] private LayerMask interactableMask = ~0;

        // ──────────── Global Registry & Single Selection ────────────
        public static readonly List<ARModelManipulator> All = new List<ARModelManipulator>();
        public static ARModelManipulator Selected { get; private set; }
        public static event Action<ARModelManipulator> OnSelectionChanged;

        private Camera mainCamera;
        private Vector3 initialScale;
        private bool isInteractingWithModel = false;
        private Plane dragPlane;
        private Vector3 dragOffset;
        private float dragDistance;

        // Sub-elements being interacted with
        private MCBPivotLever activeLever;
        private WireConnectorEnd activeWireEnd;

        // Touch tracking
        private float previousPinchDistance = 0f;

        public PlacementMode Mode => placementMode;
        public Vector3 HangingCoordinate => hangingTargetCoordinate;
        public bool IsHanging => placementMode == PlacementMode.HangingAtCoordinates;
        public bool IsSelected => Selected == this;

        public event Action<PlacementMode> OnPlacementModeChanged;
        public event Action<Vector3> OnHangingCoordinateUpdated;

        private void Awake()
        {
            mainCamera = Camera.main;
            initialScale = transform.localScale;
        }

        private void OnEnable()
        {
            if (!All.Contains(this)) All.Add(this);
            if (Selected == null) Select(this);
        }

        private void OnDisable()
        {
            All.Remove(this);
            if (Selected == this)
            {
                Select(All.Count > 0 ? All[0] : null);
            }
        }

        private void Start()
        {
            EnsureCollider();
        }

        /// <summary>
        /// Selects this specific model manipulator as the active target for gestures and UI controls.
        /// </summary>
        public static void Select(ARModelManipulator target)
        {
            if (Selected == target) return;
            Selected = target;
            OnSelectionChanged?.Invoke(Selected);
            Debug.Log($"[ARModelManipulator] Active selected model: {(Selected != null ? Selected.gameObject.name : "None")}");
        }

        private void EnsureCollider()
        {
            Collider col = GetComponent<Collider>();
            if (col == null)
            {
                BoxCollider bc = gameObject.AddComponent<BoxCollider>();
                Bounds bounds = new Bounds(transform.position, Vector3.zero);
                Renderer[] rends = GetComponentsInChildren<Renderer>();
                if (rends.Length > 0)
                {
                    bounds = rends[0].bounds;
                    for (int i = 1; i < rends.Length; i++)
                    {
                        bounds.Encapsulate(rends[i].bounds);
                    }
                    bc.center = transform.InverseTransformPoint(bounds.center);
                    bc.size = transform.InverseTransformVector(bounds.size);
                }
                else
                {
                    bc.size = new Vector3(0.3f, 0.3f, 0.3f);
                }
            }
        }

        private void Update()
        {
            if (WireDragController.IsDragging || WireSystem.WireInputController.IsDragging) return;

            if (mainCamera == null) mainCamera = Camera.main;
            if (mainCamera == null) return;

            // Check if user clicked/tapped to select a different model in the scene
            CheckTapSelection();

            // CRITICAL: Only the currently SELECTED model executes gestures!
            // This prevents pinching or moving one model from affecting any other model in the scene.
            if (Selected != this)
            {
                return;
            }

            // Handle Hanging Mode smooth transition to locked position
            if (placementMode == PlacementMode.HangingAtCoordinates)
            {
                if (Vector3.Distance(transform.position, hangingTargetCoordinate) > 0.005f)
                {
                    transform.position = Vector3.Lerp(transform.position, hangingTargetCoordinate, Time.deltaTime * hangingLerpSpeed);
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(hangingTargetEuler), Time.deltaTime * hangingLerpSpeed);
                }
                else
                {
                    transform.position = hangingTargetCoordinate;
                    transform.rotation = Quaternion.Euler(hangingTargetEuler);
                    placementMode = PlacementMode.Locked;
                    ApplyARAnchor();
                    OnPlacementModeChanged?.Invoke(placementMode);
                }
                return;
            }

#if UNITY_EDITOR || UNITY_STANDALONE
            HandleMouseInput();
#else
            HandleTouchInput();
#endif
        }

        /// <summary>
        /// Checks if a touch or mouse click began on any model in the scene to switch selection.
        /// </summary>
        private void CheckTapSelection()
        {
            Vector3 tapPos = Vector3.zero;
            bool tapBegan = false;

            if (Input.touchCount > 0)
            {
                Touch t = Input.GetTouch(0);
                if (t.phase == TouchPhase.Began)
                {
                    tapPos = t.position;
                    tapBegan = true;
                }
            }
#if UNITY_EDITOR || UNITY_STANDALONE
            else if (Input.GetMouseButtonDown(0))
            {
                tapPos = Input.mousePosition;
                tapBegan = true;
            }
#endif

            if (tapBegan && mainCamera != null)
            {
                Ray ray = mainCamera.ScreenPointToRay(tapPos);
                if (Physics.Raycast(ray, out RaycastHit hit, 50f, interactableMask))
                {
                    ARModelManipulator hitManip = hit.collider.GetComponentInParent<ARModelManipulator>();
                    if (hitManip != null && hitManip != Selected)
                    {
                        Select(hitManip);
                    }
                }
            }
        }

        #region Coordinate Hanging & Locking API
        /// <summary>
        /// Locks the breaker firmly in place at its current position with an ARAnchor.
        /// </summary>
        public void LockCurrentPosition()
        {
            placementMode = PlacementMode.Locked;
            ApplyARAnchor();
            OnPlacementModeChanged?.Invoke(placementMode);
            Debug.Log($"[ARModelManipulator] {gameObject.name} locked at: {transform.position}");
        }

        /// <summary>
        /// Hangs the contactor at a specific world coordinate.
        /// </summary>
        public void HangAtCoordinate(Vector3 targetWorldPosition, Quaternion? targetRotation = null)
        {
            RemoveARAnchor();
            hangingTargetCoordinate = targetWorldPosition;
            if (targetRotation.HasValue)
            {
                hangingTargetEuler = targetRotation.Value.eulerAngles;
            }
            placementMode = PlacementMode.HangingAtCoordinates;
            OnPlacementModeChanged?.Invoke(placementMode);
            OnHangingCoordinateUpdated?.Invoke(hangingTargetCoordinate);
        }

        /// <summary>
        /// Hangs the contactor directly in front of the AR camera at calibrated eye-level.
        /// </summary>
        public void HangAtCameraEyeLevel(float distance = 1.2f, float heightOffset = 0.0f)
        {
            if (mainCamera == null) mainCamera = Camera.main;
            if (mainCamera == null) return;

            Vector3 camPos = mainCamera.transform.position;
            Vector3 camFwd = mainCamera.transform.forward;
            camFwd.y = 0;
            camFwd.Normalize();

            Vector3 hangPos = camPos + camFwd * distance + Vector3.up * heightOffset;
            Quaternion hangRot = Quaternion.LookRotation(-camFwd, Vector3.up);

            HangAtCoordinate(hangPos, hangRot);
        }

        /// <summary>
        /// Releases the model so user can freely drag in XYZ, pinch-to-scale, and twist-to-rotate.
        /// </summary>
        public void ReleaseToFreeMovable()
        {
            RemoveARAnchor();
            placementMode = PlacementMode.FreeMovable;
            OnPlacementModeChanged?.Invoke(placementMode);
            Debug.Log($"[ARModelManipulator] {gameObject.name} released to FreeMovable mode.");
        }

        /// <summary>
        /// Toggles between Locked and Free movable.
        /// </summary>
        public void ToggleMovable()
        {
            if (placementMode == PlacementMode.Locked || placementMode == PlacementMode.HangingAtCoordinates)
            {
                ReleaseToFreeMovable();
            }
            else
            {
                LockCurrentPosition();
            }
        }
        #endregion

        #region Touch Gestures (Mobile AR)
        private void HandleTouchInput()
        {
            int touchCount = Input.touchCount;

            if (touchCount == 0)
            {
                EndAnyDrag();
                return;
            }

            // 1-Finger Touch Interaction (Drag Model in view plane, Drag Lever, or Drag Wire)
            if (touchCount == 1)
            {
                Touch touch = Input.GetTouch(0);

                if (touch.phase == TouchPhase.Began)
                {
                    StartTouchInteraction(touch.position);
                }
                else if (touch.phase == TouchPhase.Moved || touch.phase == TouchPhase.Stationary)
                {
                    UpdateTouchInteraction(touch.position);
                }
                else if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                {
                    EndAnyDrag();
                }
            }
            // 2-Finger Touch Interaction (Pinch to Scale, Twist to Rotate, Parallel Drag for Z Depth)
            else if (touchCount == 2)
            {
                Touch t0 = Input.GetTouch(0);
                Touch t1 = Input.GetTouch(1);

                float currentPinchDist = Vector2.Distance(t0.position, t1.position);
                if (t0.phase == TouchPhase.Began || t1.phase == TouchPhase.Began)
                {
                    previousPinchDistance = currentPinchDist;
                }
                else if (t0.phase == TouchPhase.Moved || t1.phase == TouchPhase.Moved)
                {
                    float deltaPinch = currentPinchDist - previousPinchDistance;

                    // Twist rotation angle
                    Vector2 prevDir = (t0.position - t0.deltaPosition) - (t1.position - t1.deltaPosition);
                    Vector2 currentDir = t0.position - t1.position;
                    float angle = Vector2.SignedAngle(prevDir, currentDir);

                    // Check if both fingers move in parallel vertically -> adjust Z depth (closer/further)
                    float avgDeltaY = (t0.deltaPosition.y + t1.deltaPosition.y) * 0.5f;
                    bool isParallelZDrag = Mathf.Abs(deltaPinch) < 14f && Mathf.Abs(angle) < 8f && Mathf.Abs(avgDeltaY) > 2.5f;

                    if (isParallelZDrag && placementMode == PlacementMode.FreeMovable)
                    {
                        // Parallel 2-finger drag pushes model along camera Z (depth in 3D)
                        float zStep = avgDeltaY * 0.004f;
                        transform.position += mainCamera.transform.forward * zStep;
                        dragDistance = Mathf.Clamp(Vector3.Distance(mainCamera.transform.position, transform.position), 0.35f, 6.0f);
                        dragPlane = new Plane(-mainCamera.transform.forward, transform.position);
                    }
                    else
                    {
                        // Pinch to scale
                        if (Mathf.Abs(deltaPinch) > 1.5f)
                        {
                            ApplyPinchScale(deltaPinch * pinchSensitivity);
                        }

                        // Twist rotation around Y axis
                        if (Mathf.Abs(angle) > 0.5f)
                        {
                            transform.Rotate(Vector3.up, -angle * rotationSensitivity, Space.World);
                        }
                    }

                    previousPinchDistance = currentPinchDist;
                }
            }
        }
        #endregion

        #region Mouse Emulation (Unity Editor)
        private void HandleMouseInput()
        {
            Vector3 mousePos = Input.mousePosition;

            // Scroll wheel for distance in Z (or scale with Ctrl held)
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.01f)
            {
                if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                {
                    ApplyPinchScale(scroll * 12.0f);
                }
                else if (placementMode == PlacementMode.FreeMovable)
                {
                    // Scroll wheel moves in depth (Z plane)
                    float zStep = scroll * 1.5f;
                    transform.position += mainCamera.transform.forward * zStep;
                    dragDistance = Mathf.Clamp(Vector3.Distance(mainCamera.transform.position, transform.position), 0.35f, 8.0f);
                    dragPlane = new Plane(-mainCamera.transform.forward, transform.position);
                }
            }

            // Right click drag to rotate
            if (Input.GetMouseButton(1))
            {
                float rotX = Input.GetAxis("Mouse X") * 4.0f;
                transform.Rotate(Vector3.up, -rotX, Space.World);
            }

            // Left click interactions
            if (Input.GetMouseButtonDown(0))
            {
                StartTouchInteraction(mousePos);
            }
            else if (Input.GetMouseButton(0))
            {
                UpdateTouchInteraction(mousePos);
            }
            else if (Input.GetMouseButtonUp(0))
            {
                EndAnyDrag();
            }
        }
        #endregion

        #region Interaction Resolution
        private void StartTouchInteraction(Vector3 screenPos)
        {
            Ray ray = mainCamera.ScreenPointToRay(screenPos);
            if (Physics.Raycast(ray, out RaycastHit hit, 50f, interactableMask))
            {
                // Priority 1: Wire connector end
                WireConnectorEnd wireEnd = hit.collider.GetComponent<WireConnectorEnd>();
                if (wireEnd != null)
                {
                    activeWireEnd = wireEnd;
                    activeWireEnd.StartDragging(screenPos);
                    return;
                }

                // Priority 2: MCB pivot lever
                MCBPivotLever lever = hit.collider.GetComponentInParent<MCBPivotLever>();
                if (lever != null)
                {
                    activeLever = lever;
                    activeLever.StartDragging(hit.point);
                    return;
                }

                // Priority 3: Breaker model body clicked
                if (hit.transform == transform || hit.transform.IsChildOf(transform))
                {
                    if (placementMode == PlacementMode.Locked || placementMode == PlacementMode.HangingAtCoordinates)
                    {
                        return;
                    }

                    isInteractingWithModel = true;
                    dragDistance = Mathf.Clamp(Vector3.Distance(mainCamera.transform.position, transform.position), 0.35f, 6.0f);
                    dragPlane = new Plane(-mainCamera.transform.forward, hit.point);
                    // Store offset between transform.position and hit.point so the model does not jump upon touch
                    dragOffset = transform.position - hit.point;
                }
            }
        }

        private void UpdateTouchInteraction(Vector3 screenPos)
        {
            if (activeWireEnd != null)
            {
                activeWireEnd.DragToScreenPosition(screenPos);
                return;
            }

            if (activeLever != null)
            {
                Ray ray = mainCamera.ScreenPointToRay(screenPos);
                Plane leverPlane = new Plane(-mainCamera.transform.forward, activeLever.transform.position);
                if (leverPlane.Raycast(ray, out float enter) && enter > 0.05f)
                {
                    activeLever.DragLever(ray.GetPoint(enter));
                }
                return;
            }

            if (isInteractingWithModel && placementMode == PlacementMode.FreeMovable)
            {
                Ray ray = mainCamera.ScreenPointToRay(screenPos);
                if (dragPlane.Raycast(ray, out float enter) && enter > 0.05f)
                {
                    transform.position = ray.GetPoint(enter) + dragOffset;
                }
                else
                {
                    transform.position = ray.GetPoint(dragDistance) + dragOffset;
                }
            }
        }

        private void EndAnyDrag()
        {
            if (activeWireEnd != null)
            {
                activeWireEnd.EndDragging();
                activeWireEnd = null;
            }

            if (activeLever != null)
            {
                activeLever.EndDragging();
                activeLever = null;
            }

            isInteractingWithModel = false;
        }

        private void ApplyPinchScale(float scaleDelta)
        {
            Vector3 current = transform.localScale;
            float factor = 1.0f + scaleDelta;
            float newScaleX = Mathf.Clamp(current.x * factor, minScale, maxScale);
            float ratio = newScaleX / current.x;
            transform.localScale = current * ratio;
        }

        private void ApplyARAnchor()
        {
#if !UNITY_EDITOR
            if (GetComponent<UnityEngine.XR.ARFoundation.ARAnchor>() == null)
            {
                gameObject.AddComponent<UnityEngine.XR.ARFoundation.ARAnchor>();
            }
#endif
        }

        private void RemoveARAnchor()
        {
#if !UNITY_EDITOR
            var anchor = GetComponent<UnityEngine.XR.ARFoundation.ARAnchor>();
            if (anchor != null)
            {
                Destroy(anchor);
            }
#endif
        }
        #endregion
    }
}
