using System;
using UnityEngine;

namespace SmartLines
{
    /// <summary>
    /// Draggable 2D anchor handle (Start or End) for Smart Lines.
    /// Supports mouse/touch dragging, elastic magnetic snapping to SmartSnapTarget perimeter/ports,
    /// and triggers line recalculation only when moved.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    [SelectionBase]
    public class SmartLineAnchor : MonoBehaviour
    {
        [Header("Snap Settings")]
        [Tooltip("LayerMask containing objects with SmartSnapTarget components.")]
        [SerializeField] private LayerMask snapTargetLayerMask = ~0;

        [Tooltip("Max world distance to magnetically snap to a target port or edge.")]
        [SerializeField] private float snapDistance = 0.5f;

        [Tooltip("Distance multiplier required to break away from an established snap (elastic feel).")]
        [SerializeField] private float breakAwayMultiplier = 1.3f;

        [Header("Visual Feedback")]
        [SerializeField] private SpriteRenderer handleRenderer;
        [SerializeField] private Color normalColor = new Color(0.2f, 0.6f, 1f, 1f);
        [SerializeField] private Color snappedColor = new Color(0.1f, 1f, 0.4f, 1f);
        [SerializeField] private Color draggingColor = new Color(1f, 0.8f, 0.2f, 1f);

        // State
        private bool isDragging;
        private Camera mainCamera;
        private Vector3 dragOffset;
        private SmartSnapTarget currentSnappedTarget;
        private Vector2 exitNormal = Vector2.right;

        public event Action OnAnchorPositionChanged;
        public event Action<SmartSnapTarget> OnTargetSnapped;
        public event Action OnTargetUnsnapped;

        public Vector2 Position => transform.position;
        public Vector2 ExitNormal => exitNormal;
        public bool IsSnapped => currentSnappedTarget != null;
        public SmartSnapTarget SnappedTarget => currentSnappedTarget;

        private void Awake()
        {
            mainCamera = Camera.main;
            if (handleRenderer == null) handleRenderer = GetComponent<SpriteRenderer>();
            UpdateVisual(normalColor);
        }

        private void Update()
        {
            if (mainCamera == null) mainCamera = Camera.main;

            // If snapped to a moving target, track it perfectly without drifting
            if (!isDragging && currentSnappedTarget != null)
            {
                if (currentSnappedTarget.GetClosestSnapPoint(transform.position, out Vector2 snapPt, out Vector2 normal))
                {
                    if ((Vector2)transform.position != snapPt)
                    {
                        transform.position = snapPt;
                        exitNormal = normal;
                        OnAnchorPositionChanged?.Invoke();
                    }
                }
            }
        }

        #region Mouse / Touch Dragging
        private void OnMouseDown()
        {
            if (mainCamera == null) mainCamera = Camera.main;
            Vector3 mouseWorld = GetMouseWorldPosition();
            dragOffset = transform.position - mouseWorld;
            isDragging = true;
            UpdateVisual(draggingColor);
        }

        private void OnMouseDrag()
        {
            if (!isDragging || mainCamera == null) return;

            Vector3 mouseWorld = GetMouseWorldPosition();
            Vector2 rawTargetPos = (Vector2)(mouseWorld + dragOffset);

            // Handle Snap Logic
            Vector2 finalPos = rawTargetPos;
            SmartSnapTarget candidate = FindNearbySnapTarget(rawTargetPos, out Vector2 snapPt, out Vector2 normal);

            if (candidate != null)
            {
                // Magnetically lock to snap position
                finalPos = snapPt;
                exitNormal = normal;

                if (currentSnappedTarget != candidate)
                {
                    currentSnappedTarget = candidate;
                    OnTargetSnapped?.Invoke(candidate);
                }
                UpdateVisual(snappedColor);
            }
            else
            {
                // Check breakaway condition if previously snapped
                if (currentSnappedTarget != null)
                {
                    float distToSnap = Vector2.Distance(rawTargetPos, transform.position);
                    if (distToSnap > snapDistance * breakAwayMultiplier)
                    {
                        currentSnappedTarget = null;
                        OnTargetUnsnapped?.Invoke();
                        UpdateVisual(draggingColor);
                    }
                    else
                    {
                        // Elastic hold within breakaway zone
                        finalPos = transform.position;
                    }
                }
                else
                {
                    UpdateVisual(draggingColor);
                }
            }

            if ((Vector2)transform.position != finalPos)
            {
                transform.position = finalPos;
                OnAnchorPositionChanged?.Invoke();
            }
        }

        private void OnMouseUp()
        {
            isDragging = false;
            UpdateVisual(currentSnappedTarget != null ? snappedColor : normalColor);
            OnAnchorPositionChanged?.Invoke();
        }
        #endregion

        /// <summary>
        /// Scans for candidate SmartSnapTargets within snap distance.
        /// </summary>
        private SmartSnapTarget FindNearbySnapTarget(Vector2 queryPos, out Vector2 snapPoint, out Vector2 outwardNormal)
        {
            snapPoint = queryPos;
            outwardNormal = Vector2.up;

            Collider2D[] colliders = Physics2D.OverlapCircleAll(queryPos, snapDistance, snapTargetLayerMask);
            SmartSnapTarget bestTarget = null;
            float bestDistanceSqr = float.MaxValue;

            foreach (var col in colliders)
            {
                if (col.gameObject == gameObject) continue;

                SmartSnapTarget target = col.GetComponent<SmartSnapTarget>();
                if (target == null) target = col.GetComponentInParent<SmartSnapTarget>();

                if (target != null)
                {
                    if (target.GetClosestSnapPoint(queryPos, out Vector2 pt, out Vector2 norm))
                    {
                        float dSqr = (pt - queryPos).sqrMagnitude;
                        if (dSqr <= snapDistance * snapDistance && dSqr < bestDistanceSqr)
                        {
                            bestDistanceSqr = dSqr;
                            bestTarget = target;
                            snapPoint = pt;
                            outwardNormal = norm;
                        }
                    }
                }
            }

            return bestTarget;
        }

        private Vector3 GetMouseWorldPosition()
        {
            Vector3 screenPos = Input.mousePosition;
            screenPos.z = -mainCamera.transform.position.z + transform.position.z;
            return mainCamera.ScreenToWorldPoint(screenPos);
        }

        private void UpdateVisual(Color color)
        {
            if (handleRenderer != null)
            {
                handleRenderer.color = color;
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.25f);
            Gizmos.DrawWireSphere(transform.position, snapDistance);
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(transform.position, (Vector2)transform.position + exitNormal * 0.3f);
        }
    }
}
