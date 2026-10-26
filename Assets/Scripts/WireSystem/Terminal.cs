using System;
using System.Collections.Generic;
using UnityEngine;

namespace WireSystem
{
    public enum TerminalType
    {
        Input,
        Output
    }

    /// <summary>
    /// Represents an electrical connection terminal on a 3D component (e.g. breaker screw terminals).
    /// Provides magnetic snap targeting, anchor coordinates, exit routing normals, and connection validation.
    /// </summary>
    [RequireComponent(typeof(SphereCollider))]
    [SelectionBase]
    public class Terminal : MonoBehaviour
    {
        [Header("Identity")]
        [Tooltip("Unique identifier for this terminal (e.g. IN_1, OUT_2).")]
        [SerializeField] private string terminalId = "term_in_1";

        [Tooltip("Electrical port type: Input (top) or Output (bottom).")]
        [SerializeField] private TerminalType terminalType = TerminalType.Input;

        [Header("Snap Geometry")]
        [Tooltip("Distance in world meters within which a dragged wire endpoint snaps to this terminal.")]
        [SerializeField] private float snapRadius = 0.04f;

        [Tooltip("Local-space axis along which wires exit (e.g. (0,1,0) for top terminals, (0,-1,0) for bottom).")]
        [SerializeField] private Vector3 exitDirection = Vector3.up;

        [Tooltip("Exact contact point the wire attaches to. Defaults to this transform if null.")]
        [SerializeField] private Transform anchor;

        [Tooltip("Maximum number of wires allowed on this terminal.")]
        [SerializeField] private int maxConnections = 1;

        [Header("Visual Feedback")]
        [Tooltip("Optional visual ring or sphere used to highlight snap state.")]
        [SerializeField] private Renderer highlightRenderer;
        [SerializeField] private Color normalColor = new Color(0.3f, 0.7f, 1f, 0.4f);
        [SerializeField] private Color highlightColor = new Color(0.1f, 1f, 0.4f, 1f);
        [SerializeField] private Color errorColor = new Color(1f, 0.2f, 0.2f, 1f);

        // Connected wires
        private readonly List<Wire> connectedWires = new List<Wire>();
        private SphereCollider triggerCollider;
        private Material highlightMatInstance;
        private Vector3 initialHighlightScale;
        private Coroutine errorFlashCoroutine;

        public string TerminalId => terminalId;
        public TerminalType Type => terminalType;
        public float SnapRadius => snapRadius;
        public Vector3 ExitDirection => exitDirection;
        public int MaxConnections => maxConnections;
        public IReadOnlyList<Wire> ConnectedWires => connectedWires;
        public bool IsFull => connectedWires.Count >= maxConnections;

        public Vector3 AnchorPosition => anchor != null ? anchor.position : transform.position;
        public Vector3 WorldExitDirection => transform.TransformDirection(exitDirection).normalized;

        public event Action<Terminal, Wire> OnConnected;
        public event Action<Terminal, Wire> OnDisconnected;

        private void Awake()
        {
            if (anchor == null) anchor = transform;
            EnsureTriggerCollider();
            EnsureHighlightVisual();
        }

        private void Reset()
        {
            if (string.IsNullOrEmpty(terminalId)) terminalId = gameObject.name;
            EnsureTriggerCollider();
            EnsureHighlightVisual();
        }

        public void Setup(string id, TerminalType type, Vector3 localExit, float radius = 0.04f, int maxConns = 1)
        {
            terminalId = id;
            terminalType = type;
            exitDirection = localExit;
            snapRadius = radius;
            maxConnections = maxConns;
            EnsureTriggerCollider();
            EnsureHighlightVisual();
        }

        private void EnsureTriggerCollider()
        {
            if (triggerCollider == null) triggerCollider = GetComponent<SphereCollider>();
            if (triggerCollider == null) triggerCollider = gameObject.AddComponent<SphereCollider>();

            triggerCollider.isTrigger = true;
            // Configure local sphere radius roughly matching snap radius
            float lossyScale = Mathf.Max(transform.lossyScale.x, 0.001f);
            triggerCollider.radius = (snapRadius * 0.75f) / lossyScale;

            // Assign Terminal layer (Layer 6 if registered, otherwise Default)
            int termLayer = LayerMask.NameToLayer("Terminal");
            if (termLayer != -1) gameObject.layer = termLayer;
        }

        private void EnsureHighlightVisual()
        {
            if (highlightRenderer != null) return;

            Transform existing = transform.Find("SnapHighlightVisual");
            if (existing != null)
            {
                highlightRenderer = existing.GetComponent<Renderer>();
                if (highlightRenderer != null)
                {
                    highlightRenderer.gameObject.SetActive(false);
                    return;
                }
            }

            // Create a small subtle highlight indicator ring/sphere around the screw
            GameObject visualObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            visualObj.name = "SnapHighlightVisual";
            visualObj.transform.SetParent(anchor != null ? anchor : transform, false);
            visualObj.transform.localPosition = Vector3.zero;
            visualObj.transform.localScale = Vector3.one * 0.012f;

            // Remove primitive collider so it does not interfere
            Collider col = visualObj.GetComponent<Collider>();
            if (col != null) DestroyImmediate(col);

            highlightRenderer = visualObj.GetComponent<Renderer>();
            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit")
                              ?? Shader.Find("Sprites/Default")
                              ?? Shader.Find("Unlit/Color");

            if (unlitShader != null)
            {
                highlightMatInstance = new Material(unlitShader);
                highlightMatInstance.color = normalColor;
                highlightRenderer.sharedMaterial = highlightMatInstance;
            }

            visualObj.SetActive(false);
        }

        #region Connection Validation & State
        /// <summary>
        /// Validates whether a proposed wire connection to this terminal is permissible.
        /// </summary>
        public bool CanAcceptConnection(Wire wire, Terminal otherEnd, WireSettings settings, out string rejectionReason)
        {
            rejectionReason = string.Empty;

            if (settings != null)
            {
                if (settings.preventSelfConnection && otherEnd != null && otherEnd == this)
                {
                    rejectionReason = "Cannot connect terminal to itself.";
                    return false;
                }

                if (settings.inputMustConnectToOutput && otherEnd != null && otherEnd.terminalType == this.terminalType)
                {
                    rejectionReason = $"Cannot connect {terminalType} to another {terminalType}.";
                    return false;
                }
            }

            // Check duplicate connection
            if (settings != null && settings.preventDuplicateConnection && otherEnd != null)
            {
                foreach (Wire existing in connectedWires)
                {
                    if (existing != wire && (existing.StartTerminal == otherEnd || existing.EndTerminal == otherEnd))
                    {
                        rejectionReason = "A wire already connects these terminals.";
                        return false;
                    }
                }
            }

            // Check connection capacity (if this wire is already counted, it is allowed)
            if (IsFull && !connectedWires.Contains(wire))
            {
                rejectionReason = $"Terminal {terminalId} has reached maximum connection limit ({maxConnections}).";
                return false;
            }

            return true;
        }

        public void AddWire(Wire wire)
        {
            if (wire == null || connectedWires.Contains(wire)) return;
            connectedWires.Add(wire);
            OnConnected?.Invoke(this, wire);
        }

        public void RemoveWire(Wire wire)
        {
            if (wire == null) return;
            if (connectedWires.Remove(wire))
            {
                OnDisconnected?.Invoke(this, wire);
            }
        }
        #endregion

        #region Visual Feedback
        /// <summary>
        /// Highlights the terminal when an endpoint hovers within snap distance.
        /// </summary>
        public void Highlight(bool active, bool isError = false)
        {
            if (highlightRenderer == null) return;

            if (!active)
            {
                highlightRenderer.gameObject.SetActive(false);
                return;
            }

            highlightRenderer.gameObject.SetActive(true);
            if (highlightMatInstance == null && highlightRenderer.sharedMaterial != null)
            {
                highlightMatInstance = highlightRenderer.material;
            }

            if (highlightMatInstance != null)
            {
                highlightMatInstance.color = isError ? errorColor : highlightColor;
            }

            // Subtle pulsing scale feedback
            highlightRenderer.transform.localScale = Vector3.one * (isError ? 0.016f : 0.014f);
        }

        /// <summary>
        /// Flashes red when a drop or snap attempt is rejected by connection rules.
        /// </summary>
        public void FlashRejected()
        {
            if (errorFlashCoroutine != null) StopCoroutine(errorFlashCoroutine);
            errorFlashCoroutine = StartCoroutine(DoErrorFlash());
        }

        private System.Collections.IEnumerator DoErrorFlash()
        {
            Highlight(true, true);
            yield return new WaitForSeconds(0.35f);
            Highlight(false, false);
            errorFlashCoroutine = null;
        }
        #endregion

        #region Gizmos
        private void OnDrawGizmosSelected()
        {
            // Draw magnetic snap perimeter
            Gizmos.color = new Color(0.1f, 0.85f, 1f, 0.4f);
            Vector3 center = AnchorPosition;
            Gizmos.DrawWireSphere(center, snapRadius);

            // Draw Exit Direction Arrow
            Gizmos.color = (terminalType == TerminalType.Input) ? Color.cyan : Color.yellow;
            Vector3 worldDir = WorldExitDirection;
            Vector3 tip = center + worldDir * (snapRadius * 1.5f);
            Gizmos.DrawLine(center, tip);
            Gizmos.DrawSphere(tip, snapRadius * 0.12f);
        }
        #endregion
    }
}
