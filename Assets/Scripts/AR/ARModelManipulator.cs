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
    /// Handles AR touch gestures and 3D positioning for contactor / breaker models:
    /// - Centralized Selection: only the active selected model responds to gestures (no cross-model interference).
    /// - 1-Finger Drag: moves the model horizontally (left/right) and vertically (up/down) in the camera's view plane.
    /// - 2-Finger Rotate: rotates around the Y axis via twist angle OR horizontal two-finger swipe.
    /// - 2-Finger Pinch: scales only the selected model.
    /// - 2-Finger Vertical Drag: pushes/pulls the model in 3D depth (Z axis).
    /// - Programmatic Rotate: RotateY(degrees) for quick 90° button rotations.
    /// </summary>
    [SelectionBase]
    public class ARModelManipulator : MonoBehaviour
    {
        [Header("Placement & Hanging")]
        [SerializeField] private PlacementMode placementMode = PlacementMode.FreeMovable;

        [Tooltip("Specific world coordinate where the contactor is hung / pinned. Initialized to Vector3.zero so HangAtCoordinate() must be called explicitly — never drifts to a stale (0,0,1.2) default.")]
        [SerializeField] private Vector3 hangingTargetCoordinate = Vector3.zero;

        [Tooltip("Target rotation when hung.")]
        [SerializeField] private Vector3 hangingTargetEuler = new Vector3(0f, 180f, 0f);

        [Tooltip("Speed when smoothly moving to hanging coordinates.")]
        [SerializeField] private float hangingLerpSpeed = 8.0f;

        [Header("Pinch-to-Scale Settings")]
        [Tooltip("Smallest allowed scale as a multiplier of the model's initial spawn scale (e.g. 0.2 = 20% of original size).")]
        [SerializeField] private float minScaleFactor = 0.2f;
        [Tooltip("Largest allowed scale as a multiplier of the model's initial spawn scale (e.g. 10 = 10x original size).")]
        [SerializeField] private float maxScaleFactor = 10.0f;
        [SerializeField] private float pinchSensitivity = 0.008f;

        [Header("Rotation Settings")]
        [SerializeField] private float rotationSensitivity = 0.6f;

        [Header("Layer / Raycast Mask")]
        [SerializeField] private LayerMask interactableMask = ~0;

        // ──────────── Global Registry & Single Selection ────────────
        public static readonly List<ARModelManipulator> All = new List<ARModelManipulator>();
        public static ARModelManipulator Selected { get; private set; }
        public static event Action<ARModelManipulator> OnSelectionChanged;

        private Camera mainCamera;
        private Vector3 initialScale;
        private float minScaleAbs;   // initialScale.magnitude * minScaleFactor
        private float maxScaleAbs;   // initialScale.magnitude * maxScaleFactor
        private bool isInteractingWithModel = false;
        private Vector2 lastScreenPos;

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
            // Derive absolute min/max scale bounds from the model's actual spawn scale.
            // This works correctly whether the model is 0.008 (MCB) or 0.8 (Contactor).
            float baseSize = Mathf.Max(initialScale.x, 0.0001f);
            minScaleAbs = baseSize * minScaleFactor;
            maxScaleAbs = baseSize * maxScaleFactor;
            hangingTargetCoordinate = transform.position;
            hangingTargetEuler = transform.eulerAngles;
            EnsureCollider();
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
            // Refresh scale bounds in Start() because Bootstrap (DefaultExecutionOrder=-100) may
            // have changed localScale between Awake and Start. Capture the final intended spawn scale.
            RefreshScaleBounds();
        }

        /// <summary>
        /// Recalculates absolute min/max scale from current localScale and serialized scale factor fields.
        /// Call any time localScale is externally set (e.g. by Bootstrap or ComponentSpawner).
        /// </summary>
        public void RefreshScaleBounds()
        {
            initialScale = transform.localScale;
            float baseSize = Mathf.Max(initialScale.x, 0.0001f);
            minScaleAbs = baseSize * minScaleFactor;
            maxScaleAbs = baseSize * maxScaleFactor;
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

        /// <summary>
        /// Ensures the model has a valid BoxCollider in the correct local-space size.
        /// The MCB model has localScale=0.008 so local collider units are 125x world-space —
        /// a 1m local cube becomes 0.008m (8mm) world, which is correct for a 5.8cm model.
        /// Minimum size enforces 0.01m world-space = 1.25 local at scale 0.008.
        /// </summary>
        private void EnsureCollider()
        {
            BoxCollider bc = GetComponent<BoxCollider>();
            Renderer[] rends = GetComponentsInChildren<Renderer>();

            // Compute a safe minimum: 0.01m world minimum in local space
            Vector3 lossy = transform.lossyScale;
            float minLocalX = Mathf.Max(0.01f / Mathf.Max(0.0001f, Mathf.Abs(lossy.x)), 0.1f);
            float minLocalY = Mathf.Max(0.01f / Mathf.Max(0.0001f, Mathf.Abs(lossy.y)), 0.1f);
            float minLocalZ = Mathf.Max(0.01f / Mathf.Max(0.0001f, Mathf.Abs(lossy.z)), 0.1f);

            if (bc == null)
            {
                bc = gameObject.AddComponent<BoxCollider>();
                if (rends.Length > 0)
                {
                    Bounds bounds = rends[0].bounds;
                    for (int i = 1; i < rends.Length; i++)
                    {
                        if (rends[i].GetComponentInParent<Canvas>() != null) continue;
                        bounds.Encapsulate(rends[i].bounds);
                    }
                    bc.center = transform.InverseTransformPoint(bounds.center);
                    bc.size = new Vector3(
                        Mathf.Max(minLocalX, Mathf.Abs(bounds.size.x / Mathf.Max(0.001f, Mathf.Abs(lossy.x)))),
                        Mathf.Max(minLocalY, Mathf.Abs(bounds.size.y / Mathf.Max(0.001f, Mathf.Abs(lossy.y)))),
                        Mathf.Max(minLocalZ, Mathf.Abs(bounds.size.z / Mathf.Max(0.001f, Mathf.Abs(lossy.z))))
                    );
                }
                else
                {
                    bc.center = Vector3.zero;
                    bc.size = new Vector3(minLocalX * 10f, minLocalY * 10f, minLocalZ * 10f);
                }
            }
            else
            {
                // Ensure existing collider dimensions are strictly positive in local space
                bc.size = new Vector3(
                    Mathf.Max(minLocalX, Mathf.Abs(bc.size.x)),
                    Mathf.Max(minLocalY, Mathf.Abs(bc.size.y)),
                    Mathf.Max(minLocalZ, Mathf.Abs(bc.size.z))
                );
            }

            // Also ensure mesh colliders on child renderers (excluding UI/wires) for precise touch targeting
            foreach (Renderer r in rends)
            {
                if (r.GetComponentInParent<Canvas>() != null) continue;
                if (r.GetComponent<Collider>() == null && r is MeshRenderer mr)
                {
                    MeshFilter mf = mr.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null && mf.sharedMesh.isReadable)
                    {
                        MeshCollider mc = mr.gameObject.AddComponent<MeshCollider>();
                        mc.convex = true;
                    }
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
                // Do not switch selection if tapped on UI
                if (UnityEngine.EventSystems.EventSystem.current != null)
                {
                    if (Input.touchCount > 0 && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject(Input.GetTouch(0).fingerId))
                        return;
#if UNITY_EDITOR || UNITY_STANDALONE
                    if (UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
                        return;
#endif
                }

                Ray ray = mainCamera.ScreenPointToRay(tapPos);
                RaycastHit[] hits = Physics.RaycastAll(ray, 50f, interactableMask);
                foreach (var hit in hits)
                {
                    ARModelManipulator hitManip = hit.collider.GetComponentInParent<ARModelManipulator>();
                    if (hitManip != null && hitManip != Selected)
                    {
                        Select(hitManip);
                        break;
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
        /// Hangs the contactor directly in front of the AR camera at calibrated eye-level (1-2 feet / ~0.50m).
        /// </summary>
        public void HangAtCameraEyeLevel(float distance = ModelAssetLoader.SPAWN_DISTANCE, float heightOffset = 0.0f)
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

        /// <summary>
        /// Rotates the model around the world Y axis by the given degrees.
        /// </summary>
        public void RotateY(float degrees)
        {
            transform.Rotate(Vector3.up, degrees, Space.World);
            Debug.Log($"[ARModelManipulator] Rotated {gameObject.name} by {degrees:F1}°");
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

            // 1-Finger Touch: Drag model horizontally (left/right) & vertically (up/down) in 3D camera view plane
            if (touchCount == 1)
            {
                Touch touch = Input.GetTouch(0);

                if (touch.phase == TouchPhase.Began)
                {
                    StartTouchInteraction(touch.position);
                }
                else if (touch.phase == TouchPhase.Moved || touch.phase == TouchPhase.Stationary)
                {
                    if (isInteractingWithModel || activeWireEnd != null || activeLever != null)
                    {
                        UpdateTouchInteraction(touch.position);
                    }
                    else
                    {
                        StartTouchInteraction(touch.position);
                    }
                }
                else if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                {
                    EndAnyDrag();
                }
            }
            // 2-Finger Touch: Rotate, Pinch Scale, or Depth Move
            else if (touchCount == 2)
            {
                if (isInteractingWithModel)
                {
                    EndAnyDrag();
                }

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

                    // 1. Twist angle (angular rotation)
                    Vector2 prevDir = (t0.position - t0.deltaPosition) - (t1.position - t1.deltaPosition);
                    Vector2 currentDir = t0.position - t1.position;
                    float twistAngle = Vector2.SignedAngle(prevDir, currentDir);

                    // 2. Average delta horizontal & vertical across both fingers
                    float avgDeltaX = (t0.deltaPosition.x + t1.deltaPosition.x) * 0.5f;
                    float avgDeltaY = (t0.deltaPosition.y + t1.deltaPosition.y) * 0.5f;

                    bool sameDirectionY = (t0.deltaPosition.y * t1.deltaPosition.y > 0);

                    // Rotation: trigger if fingers twist OR if both fingers swipe horizontally together
                    if (Mathf.Abs(twistAngle) > 0.35f)
                    {
                        RotateY(-twistAngle * rotationSensitivity * 2.5f);
                    }
                    else if (Mathf.Abs(avgDeltaX) > 1.8f && Mathf.Abs(avgDeltaX) > Mathf.Abs(avgDeltaY) * 1.4f)
                    {
                        RotateY(-avgDeltaX * rotationSensitivity * 0.5f);
                    }
                    // Pinch-to-scale
                    else if (Mathf.Abs(deltaPinch) > 2.5f)
                    {
                        ApplyPinchScale(deltaPinch * pinchSensitivity);
                    }
                    // Depth (Z axis) movement: two fingers dragging vertically in parallel in the same direction
                    else if (sameDirectionY && Mathf.Abs(avgDeltaY) > 4.0f && placementMode == PlacementMode.FreeMovable)
                    {
                        transform.position += mainCamera.transform.forward * (avgDeltaY * 0.003f);
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
                    transform.position += mainCamera.transform.forward * (scroll * 1.5f);
                }
            }

            // Right click drag to rotate
            if (Input.GetMouseButton(1))
            {
                float rotX = Input.GetAxis("Mouse X") * 4.0f;
                RotateY(-rotX);
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
            lastScreenPos = screenPos;

            // 1. Ignore interaction if touching over UI element
            if (UnityEngine.EventSystems.EventSystem.current != null)
            {
                if (Input.touchCount > 0 && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject(Input.GetTouch(0).fingerId))
                    return;
#if UNITY_EDITOR || UNITY_STANDALONE
                if (UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
                    return;
#endif
            }

            // 2. Perform raycast into scene for wire ends, levers, or other models
            Ray ray = mainCamera.ScreenPointToRay(screenPos);
            RaycastHit[] hits = Physics.RaycastAll(ray, 50f, interactableMask);

            // Priority 1: Free (unconnected) wire connector ends
            WireConnectorEnd connectedWireEndHit = null;
            bool hitModelOrLever = false;

            foreach (var hit in hits)
            {
                WireConnectorEnd wireEnd = hit.collider.GetComponent<WireConnectorEnd>();
                if (wireEnd != null)
                {
                    if (!wireEnd.IsConnected)
                    {
                        activeWireEnd = wireEnd;
                        activeWireEnd.StartDragging(screenPos);
                        return;
                    }
                    else if (connectedWireEndHit == null)
                    {
                        connectedWireEndHit = wireEnd;
                    }
                }

                if (hit.collider.GetComponentInParent<MCBPivotLever>() != null || 
                    (hit.collider.GetComponentInParent<ARModelManipulator>() != null && hit.collider.GetComponent<WireConnectorEnd>() == null))
                {
                    hitModelOrLever = true;
                }
            }

            // Priority 2: MCB pivot lever
            foreach (var hit in hits)
            {
                MCBPivotLever lever = hit.collider.GetComponentInParent<MCBPivotLever>();
                if (lever != null)
                {
                    activeLever = lever;
                    activeLever.StartDragging(hit.point);
                    return;
                }
            }

            // Priority 3: Check if another model was tapped to switch selection
            foreach (var hit in hits)
            {
                ARModelManipulator otherManip = hit.collider.GetComponentInParent<ARModelManipulator>();
                if (otherManip != null && otherManip != this)
                {
                    Select(otherManip);
                    return;
                }
            }

            // Priority 4: Connected wire end (only if specifically tapped and not touching model body)
            if (connectedWireEndHit != null && !hitModelOrLever)
            {
                activeWireEnd = connectedWireEndHit;
                activeWireEnd.StartDragging(screenPos);
                return;
            }

            // Priority 5: 3D Movement of THIS selected model
            // Start dragging if: (a) the touch actually hit this model's colliders, OR
            // (b) the model is selected and the touch is close to model centre on-screen
            // (handles cases where the BoxCollider is undersized after import)
            if (placementMode == PlacementMode.FreeMovable)
            {
                bool hitThisModel = false;
                foreach (var hit in hits)
                {
                    if (hit.collider.GetComponentInParent<ARModelManipulator>() == this &&
                        hit.collider.GetComponent<WireConnectorEnd>() == null)
                    {
                        hitThisModel = true;
                        break;
                    }
                }

                // Fallback: allow drag when tapping within 180px of the projected model centre
                if (!hitThisModel && mainCamera != null)
                {
                    Vector3 modelScreenPos = mainCamera.WorldToScreenPoint(transform.position);
                    if (modelScreenPos.z > 0f)
                    {
                        float screenDist = Vector2.Distance(new Vector2(screenPos.x, screenPos.y),
                                                            new Vector2(modelScreenPos.x, modelScreenPos.y));
                        hitThisModel = screenDist < 180f;
                    }
                }

                if (hitThisModel)
                {
                    isInteractingWithModel = true;
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
                Vector2 deltaScreen = (Vector2)screenPos - lastScreenPos;
                lastScreenPos = screenPos;

                if (deltaScreen.sqrMagnitude > 0.01f)
                {
                    float distance = Mathf.Clamp(Vector3.Distance(mainCamera.transform.position, transform.position), 0.35f, 6.0f);
                    float fovRad = mainCamera.fieldOfView * 0.5f * Mathf.Deg2Rad;
                    float worldPerPixel = (2.0f * distance * Mathf.Tan(fovRad)) / Mathf.Max(100, Screen.height);

                    // Translate along camera right (horizontal left/right) and camera up (vertical up/down)
                    Vector3 moveDelta = (mainCamera.transform.right * deltaScreen.x + mainCamera.transform.up * deltaScreen.y) * worldPerPixel;
                    transform.position += moveDelta;

                    // Immediately keep hangingTargetCoordinate in sync with the new position
                    hangingTargetCoordinate = transform.position;
                    hangingTargetEuler = transform.eulerAngles;
                }
            }
            else
            {
                lastScreenPos = screenPos;
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

            if (isInteractingWithModel)
            {
                isInteractingWithModel = false;
                // Firmly lock the final position coordinates so it stays in place
                hangingTargetCoordinate = transform.position;
                hangingTargetEuler = transform.eulerAngles;
                Debug.Log($"[ARModelManipulator] {gameObject.name} moved and locked at: {transform.position}");
            }
        }

        private void ApplyPinchScale(float scaleDelta)
        {
            Vector3 current = transform.localScale;
            float factor = 1.0f + scaleDelta;
            // Scale by factor but clamp to relative min/max — works for any initial scale (0.008 MCB or 0.8 Contactor)
            float newScaleX = Mathf.Clamp(current.x * factor, minScaleAbs, maxScaleAbs);
            if (Mathf.Approximately(current.x, 0f)) return;
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
