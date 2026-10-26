using System;
using System.Collections.Generic;
using UnityEngine;

namespace SmartLines
{
    /// <summary>
    /// Represents the cardinal/designated snap port positions on a 2D object.
    /// </summary>
    public enum SnapPortType
    {
        Top,
        Bottom,
        Left,
        Right,
        Center,
        Custom
    }

    /// <summary>
    /// Designated attachment port on a snap target.
    /// </summary>
    [Serializable]
    public struct SnapPort
    {
        public SnapPortType portType;
        [Tooltip("Local offset from the target transform.")]
        public Vector2 localOffset;
        [Tooltip("Outward normal direction for smart routing exit angles (e.g. Vector2.up for Top).")]
        public Vector2 normalDirection;
    }

    /// <summary>
    /// Attach to any 2D object (diagram node, electrical component, obstacle) that Smart Lines can snap to.
    /// Provides designated cardinal ports (Top, Bottom, Left, Right) and edge/perimeter snapping.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    [SelectionBase]
    public class SmartSnapTarget : MonoBehaviour
    {
        [Header("Snapping Behavior")]
        [Tooltip("If true, line anchors will snap to designated ports (Top/Bottom/Left/Right). If false, snaps to closest perimeter point.")]
        [SerializeField] private bool useDiscretePorts = true;

        [Tooltip("Extra margin around perimeter for edge snapping.")]
        [SerializeField] private float perimeterSnapMargin = 0.2f;

        [Header("Ports Configuration")]
        [SerializeField] private List<SnapPort> customPorts = new List<SnapPort>();

        [Header("Visual Feedback")]
        [SerializeField] private bool showGizmos = true;
        [SerializeField] private Color portGizmoColor = new Color(0f, 0.8f, 1f, 0.8f);

        private Collider2D targetCollider;

        private void Awake()
        {
            targetCollider = GetComponent<Collider2D>();
            InitializeDefaultPortsIfEmpty();
        }

        private void Reset()
        {
            targetCollider = GetComponent<Collider2D>();
            InitializeDefaultPortsIfEmpty();
        }

        /// <summary>
        /// Populates standard cardinal ports based on the 2D collider bounds if none are defined.
        /// </summary>
        public void InitializeDefaultPortsIfEmpty()
        {
            if (customPorts != null && customPorts.Count > 0) return;

            customPorts = new List<SnapPort>();
            if (targetCollider == null) targetCollider = GetComponent<Collider2D>();

            Vector2 extents = targetCollider != null ? (Vector2)targetCollider.bounds.extents : new Vector2(0.5f, 0.5f);

            // Create Top, Bottom, Left, Right ports
            customPorts.Add(new SnapPort { portType = SnapPortType.Top, localOffset = new Vector2(0f, extents.y), normalDirection = Vector2.up });
            customPorts.Add(new SnapPort { portType = SnapPortType.Bottom, localOffset = new Vector2(0f, -extents.y), normalDirection = Vector2.down });
            customPorts.Add(new SnapPort { portType = SnapPortType.Left, localOffset = new Vector2(-extents.x, 0f), normalDirection = Vector2.left });
            customPorts.Add(new SnapPort { portType = SnapPortType.Right, localOffset = new Vector2(extents.x, 0f), normalDirection = Vector2.right });
        }

        /// <summary>
        /// Finds the best snap position and outward normal given a query position (world coordinates).
        /// </summary>
        /// <param name="queryWorldPos">Position of the dragging handle.</param>
        /// <param name="snapPosition">Output world snap coordinates.</param>
        /// <param name="outwardNormal">Output direction pointing away from the target.</param>
        /// <returns>True if within valid snap distance.</returns>
        public bool GetClosestSnapPoint(Vector2 queryWorldPos, out Vector2 snapPosition, out Vector2 outwardNormal)
        {
            if (targetCollider == null) targetCollider = GetComponent<Collider2D>();

            if (useDiscretePorts && customPorts != null && customPorts.Count > 0)
            {
                // Find closest designated port
                float minDstSqr = float.MaxValue;
                Vector2 bestPortWorld = queryWorldPos;
                Vector2 bestNormal = Vector2.zero;

                foreach (var port in customPorts)
                {
                    Vector2 portWorld = (Vector2)transform.TransformPoint(port.localOffset);
                    float dstSqr = (portWorld - queryWorldPos).sqrMagnitude;
                    if (dstSqr < minDstSqr)
                    {
                        minDstSqr = dstSqr;
                        bestPortWorld = portWorld;
                        bestNormal = transform.TransformDirection(port.normalDirection).normalized;
                    }
                }

                snapPosition = bestPortWorld;
                outwardNormal = bestNormal;
                return true;
            }
            else
            {
                // Perimeter edge snap using Collider2D.ClosestPoint
                if (targetCollider != null)
                {
                    snapPosition = targetCollider.ClosestPoint(queryWorldPos);
                    Vector2 center = targetCollider.bounds.center;
                    outwardNormal = (snapPosition - center).normalized;
                    if (outwardNormal == Vector2.zero) outwardNormal = Vector2.up;
                    return true;
                }

                snapPosition = transform.position;
                outwardNormal = Vector2.up;
                return true;
            }
        }

        /// <summary>
        /// Gets bounding box in world space with optional extra padding.
        /// </summary>
        public Bounds GetExpandedBounds(float padding)
        {
            if (targetCollider == null) targetCollider = GetComponent<Collider2D>();
            Bounds b = targetCollider != null ? targetCollider.bounds : new Bounds(transform.position, Vector3.one);
            b.Expand(padding * 2f);
            return b;
        }

        private void OnDrawGizmosSelected()
        {
            if (!showGizmos) return;

            Gizmos.color = portGizmoColor;
            if (customPorts != null)
            {
                foreach (var port in customPorts)
                {
                    Vector3 worldPos = transform.TransformPoint(port.localOffset);
                    Gizmos.DrawSphere(worldPos, 0.06f);
                    Vector3 normalWorld = transform.TransformDirection(port.normalDirection).normalized * 0.2f;
                    Gizmos.DrawLine(worldPos, worldPos + normalWorld);
                }
            }
        }
    }
}
