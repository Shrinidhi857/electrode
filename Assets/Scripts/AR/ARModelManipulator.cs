using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Unity.XR.CoreUtils;
using ContactorAR.Breaker;
using ContactorAR.Wiring;
using ContactorAR.SelectionSystem;
using ContactorAR.UndoSystem;
using ContactorAR.Interaction;

namespace ContactorAR.AR
{
    public enum PlacementMode
    {
        FreeMovable,
        HangingAtCoordinates,
        Locked
    }

    /// <summary>
    /// Handles AR touch gestures, 3D positioning, and native AR anchoring for contactor / breaker models:
    /// - Native AR Anchors: Uses ARAnchorManager to register physical anchors with ARCore/ARKit,
    ///   parenting the model under the anchor so SLAM corrections eliminate real-world drift.
    /// - Centralized Selection: only the active selected model responds to gestures (no cross-model interference).
    /// - 1-Finger Drag: moves the model horizontally along the world XZ plane at constant Y with 1:1 finger tracking.
    /// - 2-Finger Rotate: rotates around the Y axis via twist angle (X and Z euler angles forced to 0).
    /// - 2-Finger Pinch: scales only the selected model within calibrated min/max bounds.
    /// - Tracking State: pauses updates and manages visibility when AR tracking state is limited/lost.
    /// </summary>
    [SelectionBase]
    public class ARModelManipulator : MonoBehaviour
    {
        [Header("Placement & Hanging")]
        [SerializeField] private PlacementMode placementMode = PlacementMode.FreeMovable;

        [Tooltip("Specific world coordinate where the contactor is hung / pinned.")]
        [SerializeField] private Vector3 hangingTargetCoordinate = Vector3.zero;

        [Tooltip("Target rotation when hung.")]
        [SerializeField] private Vector3 hangingTargetEuler = new Vector3(0f, 180f, 0f);

        [Tooltip("Speed when smoothly moving to hanging coordinates.")]
        [SerializeField] private float hangingLerpSpeed = 8.0f;

        [Header("Pinch-to-Scale Settings")]
        [Tooltip("Smallest allowed scale as a multiplier of the model's initial spawn scale.")]
        [SerializeField] private float minScaleFactor = 0.2f;
        [Tooltip("Largest allowed scale as a multiplier of the model's initial spawn scale.")]
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
        private float minScaleAbs;
        private float maxScaleAbs;
        private bool isInteractingWithModel = false;
        private Vector2 lastScreenPos;
        private Vector3 dragPlaneOffset = Vector3.zero;
        private float dragInitialY = 0f;

        // Sub-elements being interacted with
        private MCBPivotLever activeLever;
        private WireConnectorEnd activeWireEnd;

        // Touch tracking
        private float previousPinchDistance = 0f;

        // ── Selection highlight (scale pulse when selected) ──────────
        private Coroutine selectionPulseCo;
        private bool isPulseActive = false;

        // Native AR Anchoring (AR Foundation 6)
        private ARAnchor currentAnchor;
        private ARAnchorManager anchorManager;
        private ARPlaneManager planeManager;
        private ARRaycastManager raycastManager;
        private bool wasTracking = true;
        private Renderer[] cachedRenderers;

        public PlacementMode Mode => placementMode;
        public Vector3 HangingCoordinate => hangingTargetCoordinate;
        public bool IsHanging => placementMode == PlacementMode.HangingAtCoordinates;
        public bool IsSelected => Selected == this;
        public ARAnchor CurrentAnchor => currentAnchor;

        public event Action<PlacementMode> OnPlacementModeChanged;
        public event Action<Vector3> OnHangingCoordinateUpdated;

        private void Awake()
        {
            mainCamera = Camera.main;
            initialScale = transform.localScale;
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
            RemoveARAnchor();
            All.Remove(this);
            if (Selected == this)
            {
                Select(All.Count > 0 ? All[0] : null);
            }
        }

        private void OnDestroy()
        {
            RemoveARAnchor();
            if (All.Contains(this)) All.Remove(this);
            if (Selected == this) Select(null);
        }

        private void Start()
        {
            EnsureGlobalServices();
            EnsureCollider();
            RefreshScaleBounds();
        }

        private void EnsureGlobalServices()
        {
            if (SelectionManager.Instance == null)
            {
                GameObject sm = new GameObject("SelectionManager");
                sm.AddComponent<SelectionManager>();
            }
            if (UndoService.Instance == null)
            {
                GameObject us = new GameObject("UndoService");
                us.AddComponent<UndoService>();
            }
            if (ModelRotationController.Instance == null)
            {
                GameObject rc = new GameObject("ModelRotationController");
                rc.AddComponent<ModelRotationController>();
            }
        }

        /// <summary>
        /// Recalculates absolute min/max scale from current localScale and serialized scale factor fields.
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

            // Stop pulse on previously selected model
            if (Selected != null) Selected.StopSelectionPulse();

            Selected = target;
            OnSelectionChanged?.Invoke(Selected);

            // Start selection pulse on newly selected model
            if (Selected != null) Selected.StartSelectionPulse();

            Debug.Log($"[ARModelManipulator] Active selected model: {(Selected != null ? Selected.gameObject.name : "None")}");
        }

        // ── Selection Highlight: quick scale pulse when selected ─────
        private void StartSelectionPulse()
        {
            if (selectionPulseCo != null) StopCoroutine(selectionPulseCo);
            selectionPulseCo = StartCoroutine(SelectionPulseRoutine());
        }

        private void StopSelectionPulse()
        {
            if (selectionPulseCo != null) { StopCoroutine(selectionPulseCo); selectionPulseCo = null; }
            isPulseActive = false;
            // Restore exact scale (avoid drift from interrupted lerp)
            transform.localScale = initialScale != Vector3.zero ? initialScale : transform.localScale;
        }

        private System.Collections.IEnumerator SelectionPulseRoutine()
        {
            isPulseActive = true;
            Vector3 baseScale = transform.localScale;
            Vector3 bigScale  = baseScale * 1.08f;   // 8% pop-up
            float dur = 0.18f;

            // Scale up
            float t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                transform.localScale = Vector3.Lerp(baseScale, bigScale, t / dur);
                yield return null;
            }
            // Scale back
            t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                transform.localScale = Vector3.Lerp(bigScale, baseScale, t / dur);
                yield return null;
            }
            transform.localScale = baseScale;
            isPulseActive = false;
            selectionPulseCo = null;
        }

        private void EnsureCollider()
        {
            BoxCollider bc = GetComponent<BoxCollider>();
            Renderer[] rends = GetComponentsInChildren<Renderer>();

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
                bc.size = new Vector3(
                    Mathf.Max(minLocalX, Mathf.Abs(bc.size.x)),
                    Mathf.Max(minLocalY, Mathf.Abs(bc.size.y)),
                    Mathf.Max(minLocalZ, Mathf.Abs(bc.size.z))
                );
            }

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

            // Handle native AR anchor tracking state changes (hides/resumes model during VIO tracking loss)
            if (currentAnchor != null)
            {
                bool isTracking = currentAnchor.trackingState == TrackingState.Tracking;
                if (wasTracking != isTracking)
                {
                    wasTracking = isTracking;
                    SetRenderersVisible(isTracking);
                }
            }

            // Check if user clicked/tapped to select a model or wire in the scene
            CheckTapSelection();

            // Only the currently SELECTED model executes gestures
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
                    AnchorAtCurrentPose();
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

                // Priority 1: Check if wire was tapped
                foreach (var hit in hits)
                {
                    CylindricalWire wire = hit.collider.GetComponentInParent<CylindricalWire>();
                    if (wire != null)
                    {
                        SelectionManager.Select(wire);
                        return;
                    }
                }

                // Priority 2: Check if model was tapped
                foreach (var hit in hits)
                {
                    ARModelManipulator hitManip = hit.collider.GetComponentInParent<ARModelManipulator>();
                    if (hitManip != null)
                    {
                        SelectionManager.Select(hitManip);
                        if (hitManip != Selected)
                        {
                            Select(hitManip);
                        }
                        return;
                    }
                }
            }
        }

        #region Coordinate Hanging & Locking API
        /// <summary>
        /// Locks the breaker firmly in place at its current position with a native ARAnchor.
        /// </summary>
        public void LockCurrentPosition()
        {
            placementMode = PlacementMode.Locked;
            AnchorAtCurrentPose();
            OnPlacementModeChanged?.Invoke(placementMode);
            Debug.Log($"[ARModelManipulator] {gameObject.name} locked and anchored at: {transform.position}");
        }

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

        public void ReleaseToFreeMovable()
        {
            RemoveARAnchor();
            placementMode = PlacementMode.FreeMovable;
            OnPlacementModeChanged?.Invoke(placementMode);
            Debug.Log($"[ARModelManipulator] {gameObject.name} released to FreeMovable mode.");
        }

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
        /// Rotates the model around the world Y axis, locking X and Z to 0.
        /// </summary>
        public void RotateY(float degrees)
        {
            transform.Rotate(Vector3.up, degrees, Space.World);
            Vector3 euler = transform.eulerAngles;
            euler.x = 0f;
            euler.z = 0f;
            transform.eulerAngles = euler;
            hangingTargetEuler = transform.eulerAngles;
        }

        private void LateUpdate()
        {
            // Lock X and Z rotation strictly to 0 every frame
            Vector3 euler = transform.eulerAngles;
            if (!Mathf.Approximately(euler.x, 0f) || !Mathf.Approximately(euler.z, 0f))
            {
                euler.x = 0f;
                euler.z = 0f;
                transform.eulerAngles = euler;
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

            // 1-Finger Touch: SELECT only — no accidental drag
            // Model moves only when explicitly in 2-finger pan mode (see below).
            if (touchCount == 1)
            {
                Touch touch = Input.GetTouch(0);

                // Lever and wire interactions still work with 1 finger
                if (touch.phase == TouchPhase.Began)
                {
                    StartTouchInteraction(touch.position, selectOnly: true);
                }
                else if (touch.phase == TouchPhase.Moved || touch.phase == TouchPhase.Stationary)
                {
                    if (activeWireEnd != null || activeLever != null)
                        UpdateTouchInteraction(touch.position);
                }
                else if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                {
                    EndAnyDrag();
                }
            }
            // 2-Finger Touch: Rotate, Pinch-Scale, or PAN (move) the model
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
                    // Begin 2-finger pan drag (like a 1-finger drag but gated to 2 fingers)
                    if (placementMode == PlacementMode.FreeMovable)
                        Begin2FingerPan(t0.position, t1.position);
                }
                else if (t0.phase == TouchPhase.Moved || t1.phase == TouchPhase.Moved)
                {
                    float deltaPinch = currentPinchDist - previousPinchDistance;

                    Vector2 prevDir = (t0.position - t0.deltaPosition) - (t1.position - t1.deltaPosition);
                    Vector2 currentDir = t0.position - t1.position;
                    float twistAngle = Vector2.SignedAngle(prevDir, currentDir);

                    float avgDeltaX = (t0.deltaPosition.x + t1.deltaPosition.x) * 0.5f;
                    float avgDeltaY = (t0.deltaPosition.y + t1.deltaPosition.y) * 0.5f;
                    bool sameDirectionY = (t0.deltaPosition.y * t1.deltaPosition.y > 0);
                    bool sameDirectionX = (t0.deltaPosition.x * t1.deltaPosition.x > 0);

                    // Priority 1: Pinch-to-scale (fingers moving apart/together clearly)
                    if (Mathf.Abs(deltaPinch) > 3.0f)
                    {
                        ApplyPinchScale(deltaPinch * pinchSensitivity);
                    }

                    // Priority 2: Rotation (finger twist > 1.8°)
                    if (Mathf.Abs(twistAngle) > 1.8f)
                    {
                        RotateY(-twistAngle * rotationSensitivity * 1.5f);
                    }
                    else if (Mathf.Abs(avgDeltaX) > 3.0f && Mathf.Abs(avgDeltaX) > Mathf.Abs(avgDeltaY) * 1.5f && Mathf.Abs(deltaPinch) < 4.0f && sameDirectionX)
                    {
                        RotateY(-avgDeltaX * rotationSensitivity * 0.4f);
                    }
                    // Priority 3: 2-finger parallel PAN — both fingers moving same direction (move model)
                    else if (Mathf.Abs(deltaPinch) < 6.0f && placementMode == PlacementMode.FreeMovable)
                    {
                        // Both fingers same direction = translate the model on the XZ plane
                        if ((sameDirectionX || sameDirectionY) && (Mathf.Abs(avgDeltaX) > 2.0f || Mathf.Abs(avgDeltaY) > 2.0f))
                        {
                            Vector2 midScreen = (t0.position + t1.position) * 0.5f;
                            Update2FingerPan(midScreen);
                        }
                    }

                    // Depth (Z) push: both fingers move up/down in parallel with no pinch
                    if (sameDirectionY && Mathf.Abs(avgDeltaY) > 5.0f && Mathf.Abs(deltaPinch) < 4.0f && !sameDirectionX)
                    {
                        transform.position += mainCamera.transform.forward * (avgDeltaY * 0.003f);
                    }

                    previousPinchDistance = currentPinchDist;
                }
                else if (t0.phase == TouchPhase.Ended || t1.phase == TouchPhase.Ended)
                {
                    EndAnyDrag(); // Re-anchor after 2-finger pan ends
                }
            }
        }

        // ── 2-Finger Pan helpers ─────────────────────────────────────
        private Vector2 panMidScreenPrev;
        private bool panActive = false;
        private Vector3 panPlaneOffset = Vector3.zero;

        private void Begin2FingerPan(Vector2 p0, Vector2 p1)
        {
            panMidScreenPrev = (p0 + p1) * 0.5f;
            panActive = true;
            dragInitialY = transform.position.y;

            if (mainCamera != null)
            {
                Ray ray = mainCamera.ScreenPointToRay(panMidScreenPrev);
                Plane hPlane = new Plane(Vector3.up, new Vector3(0f, dragInitialY, 0f));
                if (hPlane.Raycast(ray, out float enter) && enter > 0.05f)
                {
                    panPlaneOffset = transform.position - ray.GetPoint(enter);
                    panPlaneOffset.y = 0f;
                }
                else
                {
                    panPlaneOffset = Vector3.zero;
                }
            }

            // Detach anchor during move so we don't fight SLAM
            RemoveARAnchor();
            isInteractingWithModel = true;
        }

        private void Update2FingerPan(Vector2 midScreen)
        {
            if (!panActive || mainCamera == null) return;

            Ray ray = mainCamera.ScreenPointToRay(midScreen);
            Vector3 newPos;

            // Try AR plane first, then fall back to horizontal plane at model's Y
            List<ARRaycastHit> planeHits = new List<ARRaycastHit>();
            if (raycastManager != null && raycastManager.Raycast(ray, planeHits, TrackableType.PlaneWithinPolygon | TrackableType.PlaneWithinBounds) && planeHits.Count > 0)
            {
                newPos = planeHits[0].pose.position + panPlaneOffset;
                dragInitialY = newPos.y;
            }
            else
            {
                Plane hPlane = new Plane(Vector3.up, new Vector3(0f, dragInitialY, 0f));
                if (hPlane.Raycast(ray, out float enter) && enter > 0.05f && enter < 15f)
                {
                    newPos = ray.GetPoint(enter) + panPlaneOffset;
                }
                else
                {
                    Vector2 screenDelta = midScreen - panMidScreenPrev;
                    Vector3 camRight = mainCamera.transform.right;
                    Vector3 camFwd = Vector3.ProjectOnPlane(mainCamera.transform.forward, Vector3.up).normalized;
                    newPos = transform.position + (camRight * screenDelta.x + camFwd * screenDelta.y) * 0.002f;
                }
            }

            newPos.y = dragInitialY;

            if (ModelDragController.SnapEnabled)
            {
                float step = 0.018f;
                newPos.x = Mathf.Round(newPos.x / step) * step;
                newPos.z = Mathf.Round(newPos.z / step) * step;
            }

            transform.position = newPos;
            hangingTargetCoordinate = transform.position;
            panMidScreenPrev = midScreen;
        }
        #endregion

        #region Mouse Emulation (Unity Editor)
        private void HandleMouseInput()
        {
            Vector3 mousePos = Input.mousePosition;

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

            if (Input.GetMouseButton(1))
            {
                float rotX = Input.GetAxis("Mouse X") * 4.0f;
                RotateY(-rotX);
            }

            // Mouse pan: Middle-click or Shift+Left-drag moves model on XZ plane
            if (Input.GetMouseButtonDown(2) || (Input.GetKey(KeyCode.LeftShift) && Input.GetMouseButtonDown(0)))
            {
                if (placementMode == PlacementMode.FreeMovable)
                    Begin2FingerPan(mousePos, mousePos);
            }
            else if (Input.GetMouseButton(2) || (Input.GetKey(KeyCode.LeftShift) && Input.GetMouseButton(0)))
            {
                if (placementMode == PlacementMode.FreeMovable && panActive)
                    Update2FingerPan(mousePos);
            }
            else if (Input.GetMouseButtonUp(2) || (Input.GetKey(KeyCode.LeftShift) && Input.GetMouseButtonUp(0)))
            {
                EndAnyDrag();
            }
            else if (Input.GetMouseButtonDown(0))
            {
                StartTouchInteraction(mousePos, selectOnly: true);
            }
            else if (Input.GetMouseButton(0))
            {
                if (activeWireEnd != null || activeLever != null)
                    UpdateTouchInteraction(mousePos);
            }
            else if (Input.GetMouseButtonUp(0))
            {
                EndAnyDrag();
            }
        }
        #endregion

        #region Interaction Resolution
        private void StartTouchInteraction(Vector3 screenPos, bool selectOnly = false)
        {
            lastScreenPos = screenPos;

            if (UnityEngine.EventSystems.EventSystem.current != null)
            {
                if (Input.touchCount > 0 && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject(Input.GetTouch(0).fingerId))
                    return;
#if UNITY_EDITOR || UNITY_STANDALONE
                if (UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
                    return;
#endif
            }

            Ray ray = mainCamera.ScreenPointToRay(screenPos);
            RaycastHit[] hits = Physics.RaycastAll(ray, 50f, interactableMask);

            WireConnectorEnd connectedWireEndHit = null;
            bool hitModelOrLever = false;

            foreach (var hit in hits)
            {
                WireConnectorEnd wireEnd = hit.collider.GetComponent<WireConnectorEnd>();
                if (wireEnd != null)
                {
                    if (!wireEnd.IsConnected && !selectOnly)
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

            foreach (var hit in hits)
            {
                ARModelManipulator otherManip = hit.collider.GetComponentInParent<ARModelManipulator>();
                if (otherManip != null && otherManip != this)
                {
                    Select(otherManip);
                    return;
                }
            }

            if (connectedWireEndHit != null && !hitModelOrLever)
            {
                activeWireEnd = connectedWireEndHit;
                activeWireEnd.StartDragging(screenPos);
                return;
            }

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
                    // 1-finger touch on model selects it without dragging
                    if (selectOnly)
                    {
                        Select(this);
                        return;
                    }

                    if (placementMode != PlacementMode.FreeMovable)
                    {
                        ReleaseToFreeMovable();
                    }
                    RemoveARAnchor(); // Detach from anchor during drag

                    isInteractingWithModel = true;
                    dragInitialY = transform.position.y;
                    Plane horizontalPlane = new Plane(Vector3.up, new Vector3(0f, dragInitialY, 0f));

                    if (horizontalPlane.Raycast(ray, out float enter) && enter > 0.05f && enter < 15.0f && Mathf.Abs(ray.direction.y) > 0.08f)
                    {
                        dragPlaneOffset = transform.position - ray.GetPoint(enter);
                        dragPlaneOffset.y = 0f;
                    }
                    else
                    {
                        float dist = Vector3.Distance(mainCamera.transform.position, transform.position);
                        Vector3 hitPt = ray.GetPoint(dist);
                        hitPt.y = dragInitialY;
                        dragPlaneOffset = transform.position - hitPt;
                        dragPlaneOffset.y = 0f;
                    }
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
                Vector3 newPos;

                // Priority 1: Direct AR plane surface tracking (wall, door, table, desk)
                List<ARRaycastHit> planeHits = new List<ARRaycastHit>();
                if (raycastManager != null && raycastManager.Raycast(ray, planeHits, TrackableType.PlaneWithinPolygon | TrackableType.PlaneWithinBounds) && planeHits.Count > 0)
                {
                    newPos = planeHits[0].pose.position;
                    dragInitialY = newPos.y;
                }
                else
                {
                    // Fallback: horizontal plane raycast
                    Plane horizontalPlane = new Plane(Vector3.up, new Vector3(0f, dragInitialY, 0f));
                    if (horizontalPlane.Raycast(ray, out float enter) && enter > 0.05f && enter < 15.0f && Mathf.Abs(ray.direction.y) > 0.08f)
                    {
                        newPos = ray.GetPoint(enter) + dragPlaneOffset;
                    }
                    else
                    {
                        float dist = Vector3.Distance(mainCamera.transform.position, transform.position);
                        newPos = ray.GetPoint(dist) + dragPlaneOffset;
                    }
                    newPos.y = dragInitialY;
                }

                if (ModelDragController.SnapEnabled)
                {
                    float step = 0.018f;
                    newPos.x = Mathf.Round(newPos.x / step) * step;
                    newPos.z = Mathf.Round(newPos.z / step) * step;
                }

                transform.position = newPos;
                hangingTargetCoordinate = transform.position;

                Vector3 euler = transform.eulerAngles;
                euler.x = 0f;
                euler.z = 0f;
                transform.eulerAngles = euler;
                hangingTargetEuler = euler;
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

            if (isInteractingWithModel || panActive)
            {
                isInteractingWithModel = false;
                panActive = false;
                hangingTargetCoordinate = transform.position;
                hangingTargetEuler = transform.eulerAngles;
                AnchorAtCurrentPose();
                Debug.Log($"[ARModelManipulator] {gameObject.name} moved and anchored at: {transform.position}");
            }
        }

        private void ApplyPinchScale(float scaleDelta)
        {
            Vector3 current = transform.localScale;
            float factor = 1.0f + scaleDelta;
            float newScaleX = Mathf.Clamp(current.x * factor, minScaleAbs, maxScaleAbs);
            if (Mathf.Approximately(current.x, 0f)) return;
            float ratio = newScaleX / current.x;
            transform.localScale = current * ratio;
        }
        #endregion

        #region Native AR Anchor Lifecycle (Prevents Real-World Drift)
        private Coroutine anchorWaitCo;

        /// <summary>
        /// Rigidly anchors this model to the real world using native ARAnchor (ARCore/ARKit).
        /// If ARSession is not tracking yet, automatically awaits tracking establishing before attaching.
        /// Searches for detected planes across all surface angles (horizontal tables/floors AND vertical walls/doors).
        /// If placed on or near a detected ARPlane, attaches to that plane's anchor via AttachAnchor.
        /// Otherwise, registers a free-space anchor with the XRAnchorSubsystem via TryAddAnchorAsync.
        /// The model is parented to the resulting ARAnchor so SLAM corrections keep it pinned to the real world.
        /// </summary>
        public async void AnchorAtCurrentPose()
        {
#if !UNITY_EDITOR
            EnsureAnchorServices();

            // Guard: If ARSession is not actively tracking, queue anchoring until tracking begins
            if (ARSession.state != ARSessionState.SessionTracking)
            {
                Debug.Log($"[ARModelManipulator] ARSession is {ARSession.state}. Queuing anchor for {gameObject.name} once tracking establishes.");
                if (anchorWaitCo != null) StopCoroutine(anchorWaitCo);
                anchorWaitCo = StartCoroutine(WaitForTrackingAndAnchor());
                return;
            }

            if (anchorManager == null || !anchorManager.enabled)
            {
                Debug.LogWarning($"[ARModelManipulator] Cannot anchor {gameObject.name}: ARAnchorManager not available.");
                return;
            }

            RemoveARAnchor();

            Pose currentPose = new Pose(transform.position, transform.rotation);

            // 1. Check if placed directly on or near a detected plane (horizontal OR vertical wall/door)
            ARPlane hitPlane = FindPlaneNearModel(currentPose.position);
            if (hitPlane != null)
            {
                try
                {
                    currentAnchor = anchorManager.AttachAnchor(hitPlane, currentPose);
                    if (currentAnchor != null)
                    {
                        AttachToAnchor(currentAnchor);
                        Debug.Log($"[ARModelManipulator] {gameObject.name} attached to plane anchor on {hitPlane.trackableId} ({hitPlane.alignment}) at {currentPose.position}");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ARModelManipulator] Plane AttachAnchor failed: {ex.Message}. Falling back to TryAddAnchorAsync.");
                }
            }

            // 2. Free-space native anchor via XRAnchorSubsystem
            try
            {
                var result = await anchorManager.TryAddAnchorAsync(currentPose);
                if (result.status.IsSuccess() && result.value != null)
                {
                    currentAnchor = result.value;
                    AttachToAnchor(currentAnchor);
                    Debug.Log($"[ARModelManipulator] {gameObject.name} registered with native ARAnchor at {currentPose.position}");
                }
                else
                {
                    Debug.LogWarning($"[ARModelManipulator] TryAddAnchorAsync returned {result.status} for {gameObject.name}. Retaining world transform.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ARModelManipulator] Exception creating native anchor: {ex.Message}");
            }
#else
            Debug.Log($"[ARModelManipulator] (Editor Mode) Simulated anchor for {gameObject.name} at {transform.position}");
#endif
        }

        private System.Collections.IEnumerator WaitForTrackingAndAnchor()
        {
            float timeout = 10f;
            float elapsed = 0f;
            while (ARSession.state != ARSessionState.SessionTracking && elapsed < timeout)
            {
                yield return new WaitForSeconds(0.25f);
                elapsed += 0.25f;
            }

            anchorWaitCo = null;
            if (ARSession.state == ARSessionState.SessionTracking)
            {
                AnchorAtCurrentPose();
            }
        }

        private void AttachToAnchor(ARAnchor anchor)
        {
            if (anchor == null) return;
            transform.SetParent(anchor.transform, true);
        }

        public void RemoveARAnchor()
        {
            if (currentAnchor != null)
            {
                transform.SetParent(null, true);

#if !UNITY_EDITOR
                if (anchorManager != null && anchorManager.enabled)
                {
                    anchorManager.TryRemoveAnchor(currentAnchor);
                }
                else
                {
                    Destroy(currentAnchor.gameObject);
                }
#endif
                currentAnchor = null;
            }
        }

        /// <summary>
        /// Multi-axis surface detector that finds the best matching ARPlane under or behind the model.
        /// Raycasts from the camera, downwards, and forward to hit horizontal floors/tables AND vertical walls/doors.
        /// </summary>
        private ARPlane FindPlaneNearModel(Vector3 modelPos)
        {
            if (planeManager == null) planeManager = FindAnyObjectByType<ARPlaneManager>();
            if (planeManager == null) return null;
            if (raycastManager == null) raycastManager = FindAnyObjectByType<ARRaycastManager>();
            if (raycastManager == null) return null;

            List<ARRaycastHit> hits = new List<ARRaycastHit>();
            TrackableType planeFilter = TrackableType.PlaneWithinPolygon | TrackableType.PlaneWithinBounds;

            // 1. Ray from camera towards model (best for both horizontal floors and vertical walls/doors)
            if (mainCamera != null)
            {
                Vector3 camToModel = (modelPos - mainCamera.transform.position).normalized;
                Ray camRay = new Ray(mainCamera.transform.position, camToModel);
                if (raycastManager.Raycast(camRay, hits, planeFilter) && hits.Count > 0)
                {
                    return planeManager.GetPlane(hits[0].trackableId);
                }
            }

            // 2. Downward ray (for horizontal tables, desks, floors)
            Ray downRay = new Ray(modelPos + Vector3.up * 0.3f, Vector3.down);
            if (raycastManager.Raycast(downRay, hits, planeFilter) && hits.Count > 0)
            {
                return planeManager.GetPlane(hits[0].trackableId);
            }

            // 3. Forward ray into surface behind model (for vertical walls, switchboards, doors)
            if (mainCamera != null)
            {
                Ray fwdRay = new Ray(modelPos - mainCamera.transform.forward * 0.2f, mainCamera.transform.forward);
                if (raycastManager.Raycast(fwdRay, hits, planeFilter) && hits.Count > 0)
                {
                    return planeManager.GetPlane(hits[0].trackableId);
                }
            }

            // 4. Proximity check against all tracked AR planes within 0.4m
            ARPlane closestPlane = null;
            float closestDist = 0.40f;
            foreach (var plane in planeManager.trackables)
            {
                if (plane.trackingState == TrackingState.Tracking)
                {
                    float dist = Vector3.Distance(modelPos, plane.center);
                    if (dist < closestDist)
                    {
                        closestDist = dist;
                        closestPlane = plane;
                    }
                }
            }

            return closestPlane;
        }

        private void EnsureAnchorServices()
        {
            if (anchorManager == null) anchorManager = FindAnyObjectByType<ARAnchorManager>();
            if (planeManager == null) planeManager = FindAnyObjectByType<ARPlaneManager>();
            if (raycastManager == null) raycastManager = FindAnyObjectByType<ARRaycastManager>();

            if (anchorManager == null)
            {
                XROrigin origin = FindAnyObjectByType<XROrigin>();
                if (origin != null)
                {
                    anchorManager = origin.GetComponent<ARAnchorManager>() ?? origin.gameObject.AddComponent<ARAnchorManager>();
                }
            }
        }

        private void SetRenderersVisible(bool visible)
        {
            if (cachedRenderers == null || cachedRenderers.Length == 0)
                cachedRenderers = GetComponentsInChildren<Renderer>(true);

            for (int i = 0; i < cachedRenderers.Length; i++)
            {
                if (cachedRenderers[i] != null && cachedRenderers[i].GetComponentInParent<Canvas>() == null)
                {
                    cachedRenderers[i].enabled = visible;
                }
            }
        }
        #endregion
    }
}
