using System;
using UnityEngine;
using ContactorAR.Breaker;

namespace ContactorAR.Wiring
{
    public enum TerminalType
    {
        Input,
        Output
    }

    /// <summary>
    /// Attached to each contactor terminal (term_in_1..4, term_out_1..4).
    /// Provides magnetic snap targeting for wire ends, snap indicators, 
    /// and acts as an electrical connection node in the circuit simulation.
    /// </summary>
    [SelectionBase]
    public class TerminalPoint : MonoBehaviour
    {
        [Header("Terminal Identity")]
        [SerializeField] private string terminalLabel = "IN 1";
        [SerializeField] private int poleIndex = 0;
        [SerializeField] private TerminalType terminalType = TerminalType.Input;
        [SerializeField] private ContactorController parentContactor;

        [Header("Snap Configuration")]
        [Tooltip("Max distance within which a wire end snaps to this terminal (tight 2.5cm to prevent cross-pole jumping).")]
        [SerializeField] private float snapRadius = 0.025f;

        [Tooltip("Specific local offset for wire tip attachment if needed.")]
        [SerializeField] private Vector3 snapAnchorOffset = Vector3.zero;

        [Header("Electrical State")]
        [SerializeField] private bool isEnergized = false;
        [SerializeField] private float voltage = 0.0f;
        [SerializeField] private string phaseTag = "";

        [Header("Connected Wire")]
        [SerializeField] private WireConnectorEnd connectedEnd;

        // Visual snap target highlight
        private GameObject snapHighlightObject;
        private bool isHighlighted = false;

        public string TerminalLabel => terminalLabel;
        public int PoleIndex => poleIndex;
        public TerminalType Type => terminalType;
        public ContactorController ParentContactor => parentContactor;
        public float SnapRadius => snapRadius;
        public bool IsOccupied => connectedEnd != null;
        public WireConnectorEnd ConnectedEnd => connectedEnd;
        public bool IsEnergized => isEnergized;
        public float Voltage => voltage;
        public string PhaseTag => phaseTag;

        public Vector3 SnapPosition => transform.TransformPoint(snapAnchorOffset);

        /// <summary>
        /// Outward pipeline exit direction pointing away from contactor body for Lucid pipeline routing.
        /// </summary>
        public Vector3 OutwardDirection
        {
            get
            {
                if (parentContactor != null)
                {
                    Vector3 diff = (SnapPosition - parentContactor.transform.position);
                    diff.z = 0; // lateral/vertical routing plane
                    if (diff.sqrMagnitude > 0.001f) return diff.normalized;
                }
                return terminalType == TerminalType.Input ? transform.up : -transform.up;
            }
        }

        public event Action<TerminalPoint, WireConnectorEnd> OnWireConnected;
        public event Action<TerminalPoint, WireConnectorEnd> OnWireDisconnected;
        public event Action<TerminalPoint> OnElectricalStateChanged;

        private void Awake()
        {
            if (parentContactor == null)
            {
                parentContactor = GetComponentInParent<ContactorController>();
            }

            EnsureCollider();
            CreateSnapHighlight();
        }

        public void Initialize(ContactorController contactor, int index, TerminalType type, string label)
        {
            parentContactor = contactor;
            poleIndex = index;
            terminalType = type;
            terminalLabel = label;
        }

        private void EnsureCollider()
        {
            Collider col = GetComponent<Collider>();
            if (col == null)
            {
                SphereCollider sc = gameObject.AddComponent<SphereCollider>();
                sc.radius = Mathf.Max(snapRadius * 0.8f, 0.02f);
                sc.isTrigger = true;
            }
        }

        private void CreateSnapHighlight()
        {
            if (snapHighlightObject != null) return;

            snapHighlightObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            snapHighlightObject.name = "SnapHighlight";
            snapHighlightObject.transform.SetParent(transform, false);
            snapHighlightObject.transform.localPosition = snapAnchorOffset;
            snapHighlightObject.transform.localScale = Vector3.one * Mathf.Max(snapRadius * 1.0f, 0.025f);

            // Remove collider
            Destroy(snapHighlightObject.GetComponent<Collider>());

            // Transparent luminous highlight material
            MeshRenderer mr = snapHighlightObject.GetComponent<MeshRenderer>();
            Material mat = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"));
            mat.color = new Color(0.2f, 0.8f, 1.0f, 0.35f);
            mr.material = mat;

            snapHighlightObject.SetActive(false);
        }

        /// <summary>
        /// Show or hide magnetic snap hint when user hovers a wire connector near this terminal.
        /// </summary>
        public void SetHighlight(bool active)
        {
            if (isHighlighted == active) return;
            isHighlighted = active;
            if (snapHighlightObject != null)
            {
                snapHighlightObject.SetActive(active && !IsOccupied);
            }
        }

        /// <summary>
        /// Connect a wire connector end to this terminal.
        /// </summary>
        public bool Connect(WireConnectorEnd wireEnd)
        {
            if (wireEnd == null) return false;
            if (connectedEnd != null && connectedEnd != wireEnd)
            {
                return false; // Already occupied
            }

            connectedEnd = wireEnd;
            SetHighlight(false);
            OnWireConnected?.Invoke(this, wireEnd);
            return true;
        }

        /// <summary>
        /// Disconnect a wire connector end from this terminal.
        /// </summary>
        public void Disconnect(WireConnectorEnd wireEnd)
        {
            if (connectedEnd == wireEnd)
            {
                connectedEnd = null;
                SetElectricalState(false, 0f, "");
                OnWireDisconnected?.Invoke(this, wireEnd);
            }
        }

        /// <summary>
        /// Clears any connected wire end and resets electrical state (useful when duplicating/cloning).
        /// </summary>
        public void ResetConnection()
        {
            connectedEnd = null;
            SetElectricalState(false, 0f, "");
            SetHighlight(false);
        }

        /// <summary>
        /// Sets voltage and energization status from the circuit simulation.
        /// </summary>
        public void SetElectricalState(bool energized, float volt, string phase)
        {
            bool changed = (isEnergized != energized) || !Mathf.Approximately(voltage, volt) || (phaseTag != phase);
            isEnergized = energized;
            voltage = volt;
            phaseTag = phase;

            if (changed)
            {
                OnElectricalStateChanged?.Invoke(this);
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = isEnergized ? Color.red : Color.cyan;
            Gizmos.DrawWireSphere(SnapPosition, snapRadius);
        }
    }
}
