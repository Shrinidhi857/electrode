using System;
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
    /// Handles AR touch gestures for the breaker model:
    /// - Pinch to Scale
    /// - 1-Finger Drag to Move / Place
    /// - 2-Finger Twist to Rotate
    /// - Hanging / Pinning at specific coordinates (e.g., eye-level wall mounting, fixed world coordinates)
    /// - Editor mouse emulation for seamless testing without mobile deployment
    /// - Sub-element raycast filtering (allows dragging lever and wires without moving the whole model)
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

        public event Action<PlacementMode> OnPlacementModeChanged;
        public event Action<Vector3> OnHangingCoordinateUpdated;

        private void Awake()
        {
            mainCamera = Camera.main;
            initialScale = transform.localScale;
        }

        private void Start()
        {
            EnsureCollider();
        }

        private void EnsureCollider()
        {
            Collider col = GetComponent<Collider>();
            if (col == null)
            {
                BoxCollider bc = gameObject.AddComponent<BoxCollider>();
                // Auto compute bounding box from renderers
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
            if (mainCamera == null) mainCamera = Camera.main;
            if (mainCamera == null) return;

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
                    // Snap exactly to target coordinate and permanently lock
                    transform.position = hangingTargetCoordinate;
                    transform.rotation = Quaternion.Euler(hangingTargetEuler);
                    placementMode = PlacementMode.Locked;
                    ApplyARAnchor();
                    OnPlacementModeChanged?.Invoke(placementMode);
                }
                return;
            }

            if (placementMode == PlacementMode.Locked)
            {
                // In locked mode, only lever and wire interactions are processed
#if UNITY_EDITOR || UNITY_STANDALONE
                HandleMouseInput();
#else
                HandleTouchInput();
#endif
                return;
            }

#if UNITY_EDITOR || UNITY_STANDALONE
            HandleMouseInput();
#else
            HandleTouchInput();
#endif
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
            Debug.Log($"[ARModelManipulator] Breaker locked at current position: {transform.position}");
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
            Debug.Log($"[ARModelManipulator] Breaker hanging commanded to: {hangingTargetCoordinate}");
        }

        /// <summary>
        /// Hangs the contactor directly in front of the AR camera at calibrated eye-level (e.g. wall mounting).
        /// </summary>
        public void HangAtCameraEyeLevel(float distance = 1.2f, float heightOffset = 0.0f)
        {
            if (mainCamera == null) mainCamera = Camera.main;
            if (mainCamera == null) return;

            Vector3 camPos = mainCamera.transform.position;
            Vector3 camFwd = mainCamera.transform.forward;
            camFwd.y = 0; // Keep horizontal
            camFwd.Normalize();

            Vector3 hangPos = camPos + camFwd * distance + Vector3.up * heightOffset;
            Quaternion hangRot = Quaternion.LookRotation(-camFwd, Vector3.up);

            HangAtCoordinate(hangPos, hangRot);
        }

        /// <summary>
        /// Unpins/Releases the model so user can freely drag and pinch again.
        /// </summary>
        public void ReleaseToFreeMovable()
        {
            placementMode = PlacementMode.FreeMovable;
            OnPlacementModeChanged?.Invoke(placementMode);
            Debug.Log("[ARModelManipulator] Breaker released to free movable mode.");
        }

        /// <summary>
        /// Toggles between Hanging at coordinates and Free movable.
        /// </summary>
        public void ToggleHanging()
        {
            if (placementMode == PlacementMode.HangingAtCoordinates)
            {
                ReleaseToFreeMovable();
            }
            else
            {
                HangAtCameraEyeLevel(1.2f, 0.0f);
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

            // 1-Finger Touch Interaction (Drag Model, Drag Lever, or Drag Wire)
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
            // 2-Finger Touch Interaction (Pinch to Scale & Twist to Rotate)
            else if (touchCount == 2)
            {
                Touch t0 = Input.GetTouch(0);
                Touch t1 = Input.GetTouch(1);

                // Pinch to scale
                float currentPinchDist = Vector2.Distance(t0.position, t1.position);
                if (t0.phase == TouchPhase.Began || t1.phase == TouchPhase.Began)
                {
                    previousPinchDistance = currentPinchDist;
                }
                else if (t0.phase == TouchPhase.Moved || t1.phase == TouchPhase.Moved)
                {
                    float deltaPinch = currentPinchDist - previousPinchDistance;
                    ApplyPinchScale(deltaPinch * pinchSensitivity);
                    previousPinchDistance = currentPinchDist;

                    // Twist rotation
                    Vector2 prevDir = (t0.position - t0.deltaPosition) - (t1.position - t1.deltaPosition);
                    Vector2 currentDir = t0.position - t1.position;
                    float angle = Vector2.SignedAngle(prevDir, currentDir);
                    transform.Rotate(Vector3.up, -angle * rotationSensitivity, Space.World);
                }
            }
        }
        #endregion

        #region Mouse Emulation (Unity Editor)
        private void HandleMouseInput()
        {
            Vector3 mousePos = Input.mousePosition;

            // Scroll wheel pinch scale
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.01f)
            {
                ApplyPinchScale(scroll * 12.0f);
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
                // Priority 1: Check if clicked on a wire connector end
                WireConnectorEnd wireEnd = hit.collider.GetComponent<WireConnectorEnd>();
                if (wireEnd != null)
                {
                    activeWireEnd = wireEnd;
                    activeWireEnd.StartDragging(screenPos);
                    return;
                }

                // Priority 2: Check if clicked on the MCB pivot lever
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
                    // In Locked or Hanging mode, ignore body dragging completely so user can inspect or touch without accidental movement
                    if (placementMode == PlacementMode.Locked || placementMode == PlacementMode.HangingAtCoordinates)
                    {
                        return;
                    }

                    isInteractingWithModel = true;
                    dragDistance = Mathf.Clamp(Vector3.Distance(mainCamera.transform.position, transform.position), 0.35f, 4.0f);
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
                Plane currentPlane = new Plane(-mainCamera.transform.forward, mainCamera.transform.position + mainCamera.transform.forward * dragDistance);
                if (currentPlane.Raycast(ray, out float enter) && enter > 0.05f)
                {
                    transform.position = ray.GetPoint(enter);
                }
                else
                {
                    // Strictly keep in front of camera
                    transform.position = ray.GetPoint(dragDistance);
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
