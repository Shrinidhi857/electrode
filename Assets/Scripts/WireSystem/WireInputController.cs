using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace WireSystem
{
    /// <summary>
    /// Unified input handler for touch (mobile AR) and mouse (Unity Editor).
    /// Handles:
    /// - Single-finger / mouse drag initiation from Terminals to create new orthogonal wires
    /// - Dragging existing endpoint handles to re-attach or disconnect
    /// - Double-tap / long-press deletion
    /// - Screen-to-world camera drag plane calculation
    /// - Blocking AR model placement/manipulation via static IsDragging flag
    /// - Haptic and audio feedback on magnetic snap
    /// </summary>
    public class WireInputController : MonoBehaviour
    {
        public static bool IsDragging { get; private set; } = false;

        [Header("Layer Masks")]
        [Tooltip("Mask containing Terminal (Layer 6) and WireHandle (Layer 7).")]
        [SerializeField] private LayerMask raycastMask;

        [Header("Audio Feedback")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip snapClip;
        [SerializeField] private AudioClip releaseClip;

        [Header("Double Tap / Long Press Settings")]
        [SerializeField] private float doubleTapThreshold = 0.35f;
        [SerializeField] private float longPressThreshold = 0.65f;

        private Camera mainCamera;
        private Plane dragPlane;
        private WireEndpoint activeEndpoint;
        private Wire activeWire;
        private Terminal originalTerminalBeforeDrag;

        // Press tracking for tap / long-press
        private Vector2 pointerDownScreenPos;
        private float pointerDownTime;
        private float lastTapTime;
        private WireEndpoint lastTappedEndpoint;
        private bool hasMovedSignificantly = false;

        private void Awake()
        {
            if (audioSource == null) audioSource = GetComponent<AudioSource>();

            // Ensure layer mask covers Terminal and WireHandle if not configured
            if (raycastMask.value == 0)
            {
                int termLayer = LayerMask.NameToLayer("Terminal");
                int handleLayer = LayerMask.NameToLayer("WireHandle");
                int mask = 0;
                if (termLayer != -1) mask |= (1 << termLayer);
                if (handleLayer != -1) mask |= (1 << handleLayer);
                if (mask == 0) mask = ~0; // fallback all layers
                raycastMask = mask;
            }
        }

        private void Start()
        {
            mainCamera = Camera.main;
        }

        private void Update()
        {
            if (mainCamera == null) mainCamera = Camera.main;
            if (mainCamera == null) return;

#if UNITY_EDITOR || UNITY_STANDALONE
            HandleMouseInput();
#else
            HandleTouchInput();
#endif
        }

        #region Mobile Touch Input (Single Touch Only)
        private void HandleTouchInput()
        {
            int touchCount = Input.touchCount;

            // Multi-touch must NOT be intercepted; preserve pinch-scale / rotate AR gestures
            if (touchCount > 1)
            {
                if (IsDragging)
                {
                    CancelDrag();
                }
                return;
            }

            if (touchCount == 0) return;

            Touch touch = Input.GetTouch(0);

            // Block UI touches
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(touch.fingerId))
            {
                if (touch.phase == TouchPhase.Began && IsDragging)
                {
                    CancelDrag();
                }
                return;
            }

            switch (touch.phase)
            {
                case TouchPhase.Began:
                    OnPointerDown(touch.position);
                    break;

                case TouchPhase.Moved:
                case TouchPhase.Stationary:
                    OnPointerMove(touch.position);
                    break;

                case TouchPhase.Ended:
                case TouchPhase.Canceled:
                    OnPointerUp(touch.position);
                    break;
            }
        }
        #endregion

        #region Unity Editor Mouse Input
        private void HandleMouseInput()
        {
            Vector2 mousePos = Input.mousePosition;

            // Check if pointer is over UI
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                return;
            }

            if (Input.GetMouseButtonDown(0))
            {
                OnPointerDown(mousePos);
            }
            else if (Input.GetMouseButton(0))
            {
                OnPointerMove(mousePos);
            }
            else if (Input.GetMouseButtonUp(0))
            {
                OnPointerUp(mousePos);
            }
        }
        #endregion

        #region Pointer Interaction Lifecycle
        private void OnPointerDown(Vector2 screenPos)
        {
            pointerDownScreenPos = screenPos;
            pointerDownTime = Time.time;
            hasMovedSignificantly = false;

            Ray ray = mainCamera.ScreenPointToRay(screenPos);
            if (!Physics.Raycast(ray, out RaycastHit hit, 50f, raycastMask))
            {
                return;
            }

            // 1. Check if clicked an existing wire handle (WireEndpoint)
            WireEndpoint endpoint = hit.collider.GetComponentInParent<WireEndpoint>();
            if (endpoint != null && endpoint.ParentWire != null)
            {
                // Check double tap for quick deletion
                if (Time.time - lastTapTime < doubleTapThreshold && lastTappedEndpoint == endpoint)
                {
                    DeleteWire(endpoint.ParentWire);
                    lastTapTime = 0f;
                    lastTappedEndpoint = null;
                    return;
                }

                lastTapTime = Time.time;
                lastTappedEndpoint = endpoint;

                StartDraggingExistingEndpoint(endpoint);
                return;
            }

            // 2. Check if clicked a Terminal to initiate a new wire
            Terminal terminal = hit.collider.GetComponentInParent<Terminal>();
            if (terminal != null)
            {
                StartDraggingNewWire(terminal);
                return;
            }
        }

        private void OnPointerMove(Vector2 screenPos)
        {
            if (!IsDragging || activeEndpoint == null) return;

            if (Vector2.Distance(screenPos, pointerDownScreenPos) > 10f)
            {
                hasMovedSignificantly = true;
            }

            // Raycast against the camera-facing drag plane passing through the anchor
            Ray ray = mainCamera.ScreenPointToRay(screenPos);
            if (dragPlane.Raycast(ray, out float enter))
            {
                Vector3 worldPos = ray.GetPoint(enter);
                Terminal otherEnd = (activeWire != null && activeWire.StartEndpoint == activeEndpoint)
                    ? activeWire.EndTerminal
                    : (activeWire != null ? activeWire.StartTerminal : null);

                WireSettings s = WireManager.Instance != null ? WireManager.Instance.Settings : null;
                activeEndpoint.UpdateDragPosition(worldPos, otherEnd, s);
            }
        }

        private void OnPointerUp(Vector2 screenPos)
        {
            if (!IsDragging || activeEndpoint == null) return;

            // Long press check without moving -> trigger deletion
            if (!hasMovedSignificantly && (Time.time - pointerDownTime) >= longPressThreshold && activeWire != null)
            {
                DeleteWire(activeWire);
                EndDragState();
                return;
            }

            Terminal otherEnd = (activeWire != null && activeWire.StartEndpoint == activeEndpoint)
                ? activeWire.EndTerminal
                : (activeWire != null ? activeWire.StartTerminal : null);

            WireSettings s = WireManager.Instance != null ? WireManager.Instance.Settings : null;
            bool connected = activeEndpoint.CommitConnection(otherEnd, s);

            if (connected)
            {
                // Wire successfully attached!
                if (activeWire != null)
                {
                    WireManager.Instance?.NotifyWireConnected(activeWire);
                }
                PlaySound(snapClip);
            }
            else
            {
                // Dropped in empty space or rejected
                if (s != null && s.revertOnInvalidDrop && originalTerminalBeforeDrag != null)
                {
                    // Revert endpoint to previous terminal
                    activeEndpoint.AttachToTerminal(originalTerminalBeforeDrag);
                    if (activeWire != null)
                    {
                        WireManager.Instance?.NotifyWireConnected(activeWire);
                    }
                }
                else
                {
                    // A wire only exists if BOTH ends are connected to real terminals
                    if (activeWire != null)
                    {
                        WireManager.Instance?.RemoveWire(activeWire);
                    }
                    PlaySound(releaseClip);
                }
            }

            EndDragState();
        }

        private void CancelDrag()
        {
            if (!IsDragging) return;

            if (activeWire != null && !activeWire.IsConnected)
            {
                WireManager.Instance?.RemoveWire(activeWire);
            }

            EndDragState();
        }

        private void EndDragState()
        {
            if (activeEndpoint != null)
            {
                activeEndpoint.StopDragging();
                activeEndpoint = null;
            }

            activeWire = null;
            originalTerminalBeforeDrag = null;
            IsDragging = false;
        }

        private void StartDraggingNewWire(Terminal terminal)
        {
            if (terminal == null || terminal.IsFull)
            {
                terminal?.FlashRejected();
                return;
            }

            if (WireManager.Instance == null) return;

            Wire newWire = WireManager.Instance.CreateWire(terminal);
            if (newWire == null) return;

            activeWire = newWire;
            activeEndpoint = newWire.EndEndpoint;
            originalTerminalBeforeDrag = null;

            activeEndpoint.StartDragging();

            // Setup camera-facing drag plane through the terminal anchor
            Vector3 planeNormal = -mainCamera.transform.forward;
            dragPlane = new Plane(planeNormal, terminal.AnchorPosition);

            IsDragging = true;
        }

        private void StartDraggingExistingEndpoint(WireEndpoint endpoint)
        {
            if (endpoint == null) return;

            activeWire = endpoint.ParentWire;
            activeEndpoint = endpoint;
            originalTerminalBeforeDrag = endpoint.AttachedTerminal;

            if (activeWire != null && originalTerminalBeforeDrag != null)
            {
                WireManager.Instance?.NotifyWireDisconnected(activeWire, originalTerminalBeforeDrag);
            }

            activeEndpoint.StartDragging();

            // Setup camera-facing drag plane through current handle position
            Vector3 planeNormal = -mainCamera.transform.forward;
            dragPlane = new Plane(planeNormal, endpoint.Position);

            IsDragging = true;
        }

        private void DeleteWire(Wire wire)
        {
            if (wire != null && WireManager.Instance != null)
            {
                WireManager.Instance.RemoveWire(wire);
                PlaySound(releaseClip);
            }
        }

        private void PlaySound(AudioClip clip)
        {
            if (audioSource != null && clip != null)
            {
                audioSource.PlayOneShot(clip);
            }
        }
        #endregion
    }
}
