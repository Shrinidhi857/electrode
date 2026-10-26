using System.Collections.Generic;
using UnityEngine;

namespace SmartLines
{
    /// <summary>
    /// Line rendering controller that consumes paths computed by SmartLineRouter
    /// and displays them using a Unity LineRenderer.
    /// Replicates Lucidchart-style smart lines with sleek, customizable wire width.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    [ExecuteAlways]
    [SelectionBase]
    public class SmartLineRenderer : MonoBehaviour
    {
        [Header("Anchors")]
        [Tooltip("Starting draggable handle / anchor.")]
        [SerializeField] private SmartLineAnchor startAnchor;

        [Tooltip("Ending draggable handle / anchor.")]
        [SerializeField] private SmartLineAnchor endAnchor;

        [Header("Routing Configuration")]
        [Tooltip("Path routing style: Straight, Elbow (90-deg bends), or Curved (Bezier).")]
        [SerializeField] private LinePathMode pathMode = LinePathMode.Elbow;

        [Tooltip("Physics2D LayerMask for obstacles (colliders) the line should route around.")]
        [SerializeField] private LayerMask obstacleLayerMask = 1 << 0; // Default layer

        [Tooltip("Clearance distance (in world units) to keep between the wire and obstacle colliders.")]
        [Range(0.05f, 1.5f)]
        [SerializeField] private float obstaclePadding = 0.25f;

        [Tooltip("Segment resolution for Curved mode.")]
        [Range(8, 48)]
        [SerializeField] private int curveResolution = 24;

        [Header("Visual Appearance")]
        [Tooltip("Thickness of the wire. Kept sleek and slim for schematic readability.")]
        [Range(0.01f, 0.2f)]
        [SerializeField] private float lineWidth = 0.045f;

        [Tooltip("Base wire color.")]
        [SerializeField] private Color wireColor = new Color(0.15f, 0.6f, 1f, 1f);

        [Tooltip("Material used by the LineRenderer (URP Sprites/Default or Unlit).")]
        [SerializeField] private Material lineMaterial;

        private LineRenderer lineRenderer;
        private Vector2 lastStartPos;
        private Vector2 lastEndPos;
        private LinePathMode lastMode;
        private bool isDirty = true;

        public LinePathMode PathMode
        {
            get => pathMode;
            set { if (pathMode != value) { pathMode = value; SetDirty(); } }
        }

        public SmartLineAnchor StartAnchor
        {
            get => startAnchor;
            set { startAnchor = value; BindAnchors(); SetDirty(); }
        }

        public SmartLineAnchor EndAnchor
        {
            get => endAnchor;
            set { endAnchor = value; BindAnchors(); SetDirty(); }
        }

        public float LineWidth
        {
            get => lineWidth;
            set { lineWidth = value; ApplyLineStyle(); }
        }

        private void Awake()
        {
            EnsureLineRenderer();
            ApplyLineStyle();
            BindAnchors();
        }

        private void OnEnable()
        {
            EnsureLineRenderer();
            ApplyLineStyle();
            BindAnchors();
            SetDirty();
        }

        private void OnDisable()
        {
            UnbindAnchors();
        }

        private void OnValidate()
        {
            EnsureLineRenderer();
            ApplyLineStyle();
            SetDirty();
        }

        private void Start()
        {
            UpdatePathNow();
        }

        private void Update()
        {
            // Detect if anchors moved without triggering events (e.g. moved via inspector or external script)
            if (startAnchor != null && (Vector2)startAnchor.transform.position != lastStartPos)
            {
                isDirty = true;
            }
            if (endAnchor != null && (Vector2)endAnchor.transform.position != lastEndPos)
            {
                isDirty = true;
            }
            if (pathMode != lastMode)
            {
                isDirty = true;
            }

            if (isDirty)
            {
                UpdatePathNow();
            }
        }

        /// <summary>
        /// Marks the line dirty to recalculate during the next frame.
        /// </summary>
        public void SetDirty()
        {
            isDirty = true;
        }

        /// <summary>
        /// Immediately forces a recalculation and renders the updated path.
        /// </summary>
        [ContextMenu("Force Update Path")]
        public void UpdatePathNow()
        {
            isDirty = false;
            if (lineRenderer == null) EnsureLineRenderer();
            if (startAnchor == null || endAnchor == null)
            {
                lineRenderer.positionCount = 0;
                return;
            }

            Vector2 pStart = startAnchor.Position;
            Vector2 pEnd = endAnchor.Position;
            Vector2 normStart = startAnchor.ExitNormal;
            Vector2 normEnd = endAnchor.ExitNormal;

            lastStartPos = pStart;
            lastEndPos = pEnd;
            lastMode = pathMode;

            // Route through SmartLineRouter
            List<Vector2> points = SmartLineRouter.CalculatePath(
                pStart,
                pEnd,
                normStart,
                normEnd,
                pathMode,
                obstacleLayerMask,
                obstaclePadding,
                curveResolution
            );

            // Push to LineRenderer
            lineRenderer.positionCount = points.Count;
            for (int i = 0; i < points.Count; i++)
            {
                lineRenderer.SetPosition(i, new Vector3(points[i].x, points[i].y, transform.position.z));
            }
        }

        private void EnsureLineRenderer()
        {
            if (lineRenderer == null)
            {
                lineRenderer = GetComponent<LineRenderer>();
            }

            // Configure round joints and caps for clean Lucidchart aesthetic
            lineRenderer.numCornerVertices = 4;
            lineRenderer.numCapVertices = 4;
            lineRenderer.useWorldSpace = true;
            lineRenderer.alignment = LineAlignment.TransformZ;
        }

        private void ApplyLineStyle()
        {
            if (lineRenderer == null) return;

            lineRenderer.startWidth = lineWidth;
            lineRenderer.endWidth = lineWidth;
            lineRenderer.startColor = wireColor;
            lineRenderer.endColor = wireColor;

            if (lineMaterial != null)
            {
                lineRenderer.sharedMaterial = lineMaterial;
            }
            else if (lineRenderer.sharedMaterial == null)
            {
                // Fallback to standard URP sprite / unlit material
                Shader unlit = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") 
                            ?? Shader.Find("Sprites/Default") 
                            ?? Shader.Find("Unlit/Color");
                if (unlit != null)
                {
                    lineRenderer.sharedMaterial = new Material(unlit);
                }
            }
        }

        private void BindAnchors()
        {
            UnbindAnchors();
            if (startAnchor != null)
            {
                startAnchor.OnAnchorPositionChanged += HandleAnchorMoved;
            }
            if (endAnchor != null)
            {
                endAnchor.OnAnchorPositionChanged += HandleAnchorMoved;
            }
        }

        private void UnbindAnchors()
        {
            if (startAnchor != null)
            {
                startAnchor.OnAnchorPositionChanged -= HandleAnchorMoved;
            }
            if (endAnchor != null)
            {
                endAnchor.OnAnchorPositionChanged -= HandleAnchorMoved;
            }
        }

        private void HandleAnchorMoved()
        {
            SetDirty();
        }
    }
}
