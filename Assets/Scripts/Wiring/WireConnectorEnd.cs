using System;
using UnityEngine;

namespace ContactorAR.Wiring
{
    /// <summary>
    /// Represents the spherical connector terminal at each end of the cylindrical wire.
    /// Supports pinch/drag manipulation, terminal magnetic snapping, and electrical link.
    /// </summary>
    [RequireComponent(typeof(SphereCollider))]
    [SelectionBase]
    public class WireConnectorEnd : MonoBehaviour
    {
        [Header("Parent Wire")]
        [SerializeField] private CylindricalWire parentWire;

        [Header("Snapping")]
        [Tooltip("Currently snapped terminal point, if connected.")]
        [SerializeField] private TerminalPoint snappedTerminal;

        [Tooltip("Snap distance threshold (kept tight at 2.5cm so wires don't jump to adjacent models).")]
        [SerializeField] private float snapThreshold = 0.025f;

        [Tooltip("Minimum screen drag distance in pixels before an attached wire detaches.")]
        [SerializeField] private float detachScreenThresholdPx = 25.0f;

        [Tooltip("Smooth lerp speed when snapping.")]
        [SerializeField] private float snapLerpSpeed = 16.0f;

        [Header("Visual")]
        [SerializeField] private MeshRenderer sphereRenderer;
        [SerializeField] private Color defaultColor = new Color(0.75f, 0.75f, 0.8f);
        [SerializeField] private Color snappedColor = new Color(0.2f, 0.85f, 0.4f);
        [SerializeField] private Color energizedColor = new Color(1.0f, 0.85f, 0.1f);

        [Header("Interaction State")]
        [SerializeField] private bool isDragging = false;

        private Camera mainCamera;
        private Plane dragPlane;
        private Vector3 dragOffset;
        private float dragDistance;
        private TerminalPoint closestCandidate;
        private Material sphereMatInstance;
        // Tracks latest input position so Update() can drive the drag each frame
        private Vector3 currentDragScreenPos;
        private Vector3 dragStartScreenPos;
        private bool isDetachedFromTerminal = false;

        public CylindricalWire ParentWire => parentWire;
        public TerminalPoint SnappedTerminal => snappedTerminal;
        public bool IsConnected => snappedTerminal != null;
        public bool IsDragging => isDragging;

        public event Action<WireConnectorEnd, TerminalPoint> OnSnapped;
        public event Action<WireConnectorEnd> OnUnsnapped;

        private void Awake()
        {
            mainCamera = Camera.main;

            if (parentWire == null)
            {
                parentWire = GetComponentInParent<CylindricalWire>();
            }

            if (sphereRenderer == null)
            {
                sphereRenderer = GetComponent<MeshRenderer>();
            }

            if (sphereRenderer != null)
            {
                sphereMatInstance = sphereRenderer.material;
            }

            // Ensure collider is set up
            SphereCollider sc = GetComponent<SphereCollider>();
            if (sc != null)
            {
                sc.isTrigger = false;
            }
        }

        public void SetParentWire(CylindricalWire wire)
        {
            parentWire = wire;
        }

        private void Update()
        {
            if (mainCamera == null)
            {
                mainCamera = Camera.main;
            }

            if (isDragging)
            {
                // Poll latest input position each frame and feed into drag logic
                PollCurrentInputPosition();
                DragToScreenPosition(currentDragScreenPos);
                ScanForNearbyTerminals();
            }
            else if (snappedTerminal != null)
            {
                // Firm, zero-drift lock to terminal's snap point (moves perfectly with hanging/moving contactor)
                transform.position = snappedTerminal.SnapPosition;
                transform.rotation = snappedTerminal.transform.rotation;
            }

            UpdateVisualColor();
        }

        /// <summary>
        /// Reads the current mouse or touch screen position into currentDragScreenPos.
        /// Called every frame while dragging so Update() always has a fresh position.
        /// </summary>
        private void PollCurrentInputPosition()
        {
#if UNITY_EDITOR || UNITY_STANDALONE
            currentDragScreenPos = Input.mousePosition;
#else
            if (Input.touchCount > 0)
            {
                currentDragScreenPos = Input.GetTouch(0).position;
            }
#endif
        }

        #region Dragging & Manipulation
        public void StartDragging(Vector3 touchPosition)
        {
            if (mainCamera == null) mainCamera = Camera.main;
            if (mainCamera == null) return;

            isDragging = true;
            dragStartScreenPos = touchPosition;

            // If already connected, do NOT unsnap yet!
            // Require a deliberate pull (detachScreenThresholdPx) before disconnecting.
            isDetachedFromTerminal = (snappedTerminal == null);

            // Lock interaction depth to current distance from camera, clamped safely
            dragDistance = Mathf.Clamp(Vector3.Distance(mainCamera.transform.position, transform.position), 0.35f, 4.0f);
        }

        public void DragToScreenPosition(Vector3 touchPosition, float pinchDistanceDelta = 0f)
        {
            if (!isDragging || mainCamera == null) return;

            // If still attached to a terminal, check if user deliberately pulled past detach threshold
            if (!isDetachedFromTerminal && snappedTerminal != null)
            {
                float screenMoved = Vector2.Distance(new Vector2(touchPosition.x, touchPosition.y), new Vector2(dragStartScreenPos.x, dragStartScreenPos.y));
                if (screenMoved >= detachScreenThresholdPx)
                {
                    Unsnap();
                    isDetachedFromTerminal = true;
                    dragDistance = Mathf.Clamp(Vector3.Distance(mainCamera.transform.position, transform.position), 0.35f, 4.0f);
                }
                else
                {
                    // Still held within threshold -> keep firmly locked to current terminal
                    transform.position = snappedTerminal.SnapPosition;
                    transform.rotation = snappedTerminal.transform.rotation;
                    return;
                }
            }

            // Optional pinch depth adjustment
            if (Mathf.Abs(pinchDistanceDelta) > 0.001f)
            {
                dragDistance = Mathf.Clamp(dragDistance + pinchDistanceDelta * 0.05f, 0.3f, 4.0f);
            }

            Ray ray = mainCamera.ScreenPointToRay(touchPosition);
            
            // Dynamic forward plane facing current camera pose - NEVER shoot behind camera!
            Plane currentPlane = new Plane(-mainCamera.transform.forward, mainCamera.transform.position + mainCamera.transform.forward * dragDistance);
            if (currentPlane.Raycast(ray, out float enter) && enter > 0.05f)
            {
                transform.position = ray.GetPoint(enter);
            }
            else
            {
                // Rock-solid fallback: Always keep strictly in front of camera
                transform.position = ray.GetPoint(dragDistance);
            }
        }

        public void EndDragging()
        {
            if (!isDragging) return;
            isDragging = false;

            // If never detached from original terminal, stay securely connected
            if (!isDetachedFromTerminal && snappedTerminal != null)
            {
                transform.position = snappedTerminal.SnapPosition;
                transform.rotation = snappedTerminal.transform.rotation;
                return;
            }

            // If a candidate terminal was found during drag, perform snap!
            if (closestCandidate != null && !closestCandidate.IsOccupied)
            {
                SnapToTerminal(closestCandidate);
            }

            if (closestCandidate != null)
            {
                closestCandidate.SetHighlight(false);
                closestCandidate = null;
            }

            if (parentWire != null)
            {
                parentWire.ValidateConnectionState();
            }
        }
        #endregion

        #region Terminal Snapping
        private void ScanForNearbyTerminals()
        {
            // Do NOT scan or seek other terminals if wire is currently attached and not detached!
            if (snappedTerminal != null && !isDetachedFromTerminal)
            {
                if (closestCandidate != null)
                {
                    closestCandidate.SetHighlight(false);
                    closestCandidate = null;
                }
                return;
            }

            TerminalPoint[] terminals = FindObjectsByType<TerminalPoint>(FindObjectsSortMode.None);
            TerminalPoint best = null;
            float bestDist = snapThreshold; // 0.025m = 2.5cm

            for (int i = 0; i < terminals.Length; i++)
            {
                TerminalPoint term = terminals[i];
                if (term.IsOccupied && term != snappedTerminal) continue;

                float dist = Vector3.Distance(transform.position, term.SnapPosition);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = term;
                }
            }

            if (closestCandidate != best)
            {
                if (closestCandidate != null) closestCandidate.SetHighlight(false);
                closestCandidate = best;
                if (closestCandidate != null) closestCandidate.SetHighlight(true);
            }
        }

        public void SnapToTerminal(TerminalPoint terminal)
        {
            if (terminal == null) return;

            if (terminal.Connect(this))
            {
                snappedTerminal = terminal;
                transform.position = terminal.SnapPosition;
                transform.rotation = terminal.transform.rotation;

                // Keep sphere under parentWire (scale 1,1,1) — DO NOT parent to terminal.transform
                // which has scale 0.008 (MCB) and would blow up this sphere by 125x!
                if (parentWire != null)
                {
                    transform.SetParent(parentWire.transform, true);
                    float sphereRadius = parentWire.WireRadius * 1.5f;
                    transform.localScale = Vector3.one * (sphereRadius * 2.0f);
                }

                OnSnapped?.Invoke(this, terminal);

                if (parentWire != null)
                {
                    parentWire.NotifyConnectionChanged();
                }
            }
        }

        public void Unsnap()
        {
            if (snappedTerminal != null)
            {
                TerminalPoint prev = snappedTerminal;
                snappedTerminal = null;

                if (parentWire != null)
                {
                    transform.SetParent(parentWire.transform, true);
                    float sphereRadius = parentWire.WireRadius * 1.5f;
                    transform.localScale = Vector3.one * (sphereRadius * 2.0f);
                }

                prev.Disconnect(this);
                OnUnsnapped?.Invoke(this);

                if (parentWire != null)
                {
                    parentWire.NotifyConnectionChanged();
                }
            }
        }
        #endregion

        private void UpdateVisualColor()
        {
            if (sphereMatInstance == null) return;

            Color target = defaultColor;
            if (parentWire != null && parentWire.IsEnergized)
            {
                target = energizedColor;
            }
            else if (IsConnected)
            {
                target = snappedColor;
            }
            else if (isDragging)
            {
                target = Color.white;
            }

            sphereMatInstance.color = target;
            sphereMatInstance.SetColor("_BaseColor", target);
            if (parentWire != null && parentWire.IsEnergized)
            {
                sphereMatInstance.SetColor("_EmissionColor", target * 1.5f);
                sphereMatInstance.EnableKeyword("_EMISSION");
            }
            else
            {
                sphereMatInstance.DisableKeyword("_EMISSION");
            }
        }

        private void OnDestroy()
        {
            if (closestCandidate != null)
            {
                closestCandidate.SetHighlight(false);
            }
            Unsnap();
        }
    }
}
