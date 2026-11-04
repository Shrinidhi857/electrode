using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using ContactorAR.AR;
using ContactorAR.SelectionSystem;

namespace ContactorAR.Interaction
{
    /// <summary>
    /// Handles 1:1 horizontal plane (world XZ) dragging for the selected model.
    /// Uses Plane(Vector3.up, model.position) raycasts from the screen touch point.
    /// Supports toggleable DIN rail / grid snap and AR plane height alignment.
    /// </summary>
    public class ModelDragController : MonoBehaviour
    {
        public static ModelDragController Instance { get; private set; }

        [Header("Plane & Height Settings")]
        [Tooltip("If true, snaps model Y to detected AR planes when available.")]
        [SerializeField] private bool useARPlaneHeight = false;

        [Header("Snap Settings")]
        [Tooltip("Toggleable grid snap for aligning models cleanly side-by-side on DIN rail.")]
        [SerializeField] private bool snapEnabled = true;

        [Tooltip("Grid snap spacing in world meters (e.g. 0.018m = 1.8cm standard DIN rail step).")]
        [SerializeField] private float snapGridSize = 0.018f;

        private Camera mainCamera;
        private ARModelManipulator activeTarget;
        private Plane dragPlane;
        private Vector3 dragPlaneOffset;
        private bool isDragging = false;
        private int activeFingerId = -1;

        public static bool IsDragging => Instance != null && Instance.isDragging;
        public static bool SnapEnabled
        {
            get => Instance != null && Instance.snapEnabled;
            set { if (Instance != null) Instance.snapEnabled = value; }
        }

        public static event Action<ARModelManipulator, Vector3> OnModelMoved;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else if (Instance != this) Destroy(this);
            mainCamera = Camera.main;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            // Model drag input is handled centrally by ARModelManipulator to prevent
            // dual-script race conditions and gesture conflicts (pinch, rotate, wire drag).
            // ModelDragController retains global snap configuration (SnapEnabled, snapGridSize).
        }

        private void HandleTouchInput()
        {
            if (Input.touchCount == 0)
            {
                if (isDragging) EndDrag();
                return;
            }

            Touch touch = Input.GetTouch(0);

            // Do not drag if multiple fingers are active (pinch/rotate takes over)
            if (Input.touchCount > 1)
            {
                if (isDragging) EndDrag();
                return;
            }

            if (touch.phase == TouchPhase.Began)
            {
                // Block if pointer is over UI
                if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(touch.fingerId))
                    return;

                TryStartDrag(touch.position, touch.fingerId);
            }
            else if ((touch.phase == TouchPhase.Moved || touch.phase == TouchPhase.Stationary) && isDragging && touch.fingerId == activeFingerId)
            {
                UpdateDrag(touch.position);
            }
            else if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
            {
                if (isDragging && touch.fingerId == activeFingerId)
                {
                    EndDrag();
                }
            }
        }

        private void HandleMouseInput()
        {
            if (Input.GetMouseButtonDown(0))
            {
                if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                    return;

                TryStartDrag(Input.mousePosition, -1);
            }
            else if (Input.GetMouseButton(0) && isDragging)
            {
                UpdateDrag(Input.mousePosition);
            }
            else if (Input.GetMouseButtonUp(0) && isDragging)
            {
                EndDrag();
            }
        }

        /// <summary>
        /// Attempts to initiate horizontal plane drag on the selected model or model under touch point.
        /// </summary>
        public bool TryStartDrag(Vector2 screenPos, int fingerId)
        {
            Ray ray = mainCamera.ScreenPointToRay(screenPos);
            RaycastHit[] hits = Physics.RaycastAll(ray, 50f);

            ARModelManipulator hitModel = null;
            foreach (var hit in hits)
            {
                // Exclude wire connectors so wire dragging takes precedence
                if (hit.collider.GetComponent<ContactorAR.Wiring.WireConnectorEnd>() != null) return false;
                if (hit.collider.GetComponentInParent<ContactorAR.Breaker.MCBPivotLever>() != null) return false;

                ARModelManipulator manip = hit.collider.GetComponentInParent<ARModelManipulator>();
                if (manip != null)
                {
                    hitModel = manip;
                    break;
                }
            }

            // Proximity fallback for touch forgiveness
            if (hitModel == null)
            {
                ARModelManipulator selected = SelectionManager.SelectedModel ?? ARModelManipulator.Selected;
                if (selected != null)
                {
                    Vector3 scPos = mainCamera.WorldToScreenPoint(selected.transform.position);
                    if (scPos.z > 0f && Vector2.Distance(screenPos, (Vector2)scPos) < 160f)
                    {
                        hitModel = selected;
                    }
                }
            }

            if (hitModel == null) return false;

            // Model must be selected and in FreeMovable mode
            if (hitModel.Mode != PlacementMode.FreeMovable) return false;

            SelectionManager.Select(hitModel);
            activeTarget = hitModel;
            activeFingerId = fingerId;

            // Define horizontal XZ plane through model's current world position
            dragPlane = new Plane(Vector3.up, activeTarget.transform.position);

            if (dragPlane.Raycast(ray, out float enter))
            {
                Vector3 hitPoint = ray.GetPoint(enter);
                dragPlaneOffset = activeTarget.transform.position - hitPoint;
                dragPlaneOffset.y = 0f; // strictly horizontal offset
                isDragging = true;
                return true;
            }

            return false;
        }

        private void UpdateDrag(Vector2 screenPos)
        {
            if (activeTarget == null)
            {
                EndDrag();
                return;
            }

            Ray ray = mainCamera.ScreenPointToRay(screenPos);
            if (dragPlane.Raycast(ray, out float enter))
            {
                Vector3 worldHit = ray.GetPoint(enter);
                Vector3 targetPos = worldHit + dragPlaneOffset;

                // Lock Y strictly to original horizontal plane height (constant Y)
                targetPos.y = activeTarget.transform.position.y;

                // Optional grid snap (e.g. 1.8cm DIN rail pitch)
                if (snapEnabled && snapGridSize > 0.001f)
                {
                    targetPos.x = Mathf.Round(targetPos.x / snapGridSize) * snapGridSize;
                    targetPos.z = Mathf.Round(targetPos.z / snapGridSize) * snapGridSize;
                }

                // 1:1 crisp responsive movement without jitter
                activeTarget.transform.position = targetPos;

                // Always enforce 0 X and Z euler angles
                Vector3 euler = activeTarget.transform.eulerAngles;
                euler.x = 0f;
                euler.z = 0f;
                activeTarget.transform.eulerAngles = euler;

                OnModelMoved?.Invoke(activeTarget, targetPos);
            }
        }

        public void EndDrag()
        {
            if (activeTarget != null)
            {
                // Enforce upright orientation on release
                Vector3 euler = activeTarget.transform.eulerAngles;
                euler.x = 0f;
                euler.z = 0f;
                activeTarget.transform.eulerAngles = euler;
            }

            isDragging = false;
            activeTarget = null;
            activeFingerId = -1;
        }
    }
}
