using System.Collections.Generic;
using UnityEngine;

namespace WireSystem
{
    /// <summary>
    /// Represents an interactive 3D orthogonal electrical wire connecting two Terminals.
    /// Manages LineRenderer visual styling, endpoint handles, live LateUpdate re-routing,
    /// and connected vs dragging opacity transitions.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public class Wire : MonoBehaviour
    {
        [Header("Endpoints")]
        [SerializeField] private WireEndpoint startEndpoint;
        [SerializeField] private WireEndpoint endEndpoint;

        [Header("Configuration")]
        [SerializeField] private WireSettings settings;
        [SerializeField] private Color wireColor = new Color(0.92f, 0.22f, 0.22f, 1f);
        [SerializeField] private int laneIndex = 0;

        [Header("Reference Frame")]
        [Tooltip("Transform defining the local coordinate space for orthogonal routing (typically Four-Pole-Breaker-final).")]
        [SerializeField] private Transform modelRoot;

        private LineRenderer lineRenderer;
        private Material lineMatInstance;
        private readonly List<Vector3> pathBuffer = new List<Vector3>(32);
        private Camera cachedCamera;
        private bool isDraggingVisual = false;

        public WireEndpoint StartEndpoint => startEndpoint;
        public WireEndpoint EndEndpoint => endEndpoint;
        public Terminal StartTerminal => startEndpoint != null ? startEndpoint.AttachedTerminal : null;
        public Terminal EndTerminal => endEndpoint != null ? endEndpoint.AttachedTerminal : null;
        public bool IsConnected => startEndpoint != null && startEndpoint.IsConnected &&
                                   endEndpoint != null && endEndpoint.IsConnected;
        public bool IsDragging => (startEndpoint != null && startEndpoint.IsDragging) ||
                                  (endEndpoint != null && endEndpoint.IsDragging);
        public WireSettings Settings => settings;
        public Color WireColor => wireColor;
        public int LaneIndex
        {
            get => laneIndex;
            set => laneIndex = value;
        }

        public Transform ModelRoot
        {
            get => modelRoot;
            set => modelRoot = value;
        }

        private void Awake()
        {
            EnsureLineRenderer();
            cachedCamera = Camera.main;
        }

        private void Start()
        {
            if (cachedCamera == null) cachedCamera = Camera.main;
            ResolveModelRoot();
        }

        public void Initialize(WireSettings wireSettings, Color color, Transform referenceModelRoot, int lane = 0)
        {
            settings = wireSettings;
            wireColor = color;
            modelRoot = referenceModelRoot;
            laneIndex = lane;

            EnsureLineRenderer();
            EnsureEndpoints();

            if (startEndpoint != null) startEndpoint.Initialize(this, true, settings);
            if (endEndpoint != null) endEndpoint.Initialize(this, false, settings);

            ApplyStyling();
            UpdatePath();
        }

        private void EnsureLineRenderer()
        {
            if (lineRenderer == null) lineRenderer = GetComponent<LineRenderer>();
            if (lineRenderer == null) lineRenderer = gameObject.AddComponent<LineRenderer>();

            lineRenderer.useWorldSpace = true;
            lineRenderer.alignment = LineAlignment.View;
            lineRenderer.numCornerVertices = 4;
            lineRenderer.numCapVertices = 4;
            lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lineRenderer.receiveShadows = false;

            if (lineMatInstance == null)
            {
                Shader unlit = Shader.Find("Universal Render Pipeline/Unlit")
                            ?? Shader.Find("Sprites/Default")
                            ?? Shader.Find("Unlit/Color");

                if (unlit != null)
                {
                    lineMatInstance = new Material(unlit);
                    lineRenderer.sharedMaterial = lineMatInstance;
                }
            }
        }

        private void EnsureEndpoints()
        {
            if (startEndpoint == null)
            {
                Transform s = transform.Find("StartEndpoint");
                if (s == null)
                {
                    GameObject go = new GameObject("StartEndpoint");
                    go.transform.SetParent(transform, false);
                    s = go.transform;
                }
                startEndpoint = s.GetComponent<WireEndpoint>();
                if (startEndpoint == null) startEndpoint = s.gameObject.AddComponent<WireEndpoint>();
            }

            if (endEndpoint == null)
            {
                Transform e = transform.Find("EndEndpoint");
                if (e == null)
                {
                    GameObject go = new GameObject("EndEndpoint");
                    go.transform.SetParent(transform, false);
                    e = go.transform;
                }
                endEndpoint = e.GetComponent<WireEndpoint>();
                if (endEndpoint == null) endEndpoint = e.gameObject.AddComponent<WireEndpoint>();
            }
        }

        private void ResolveModelRoot()
        {
            if (modelRoot != null) return;

            // Try to find breaker root in scene
            GameObject breaker = GameObject.Find("Four-Pole-Breaker-final");
            if (breaker != null)
            {
                modelRoot = breaker.transform;
                return;
            }

            if (StartTerminal != null)
            {
                modelRoot = StartTerminal.transform.root;
            }
            else if (EndTerminal != null)
            {
                modelRoot = EndTerminal.transform.root;
            }
            else
            {
                modelRoot = transform;
            }
        }

        public void ApplyStyling()
        {
            if (lineRenderer == null) EnsureLineRenderer();

            float width = settings != null ? settings.lineWidth : 0.0025f;
            lineRenderer.startWidth = width;
            lineRenderer.endWidth = width;

            UpdateVisualColor();

            if (startEndpoint != null) startEndpoint.ApplySettings(settings);
            if (endEndpoint != null) endEndpoint.ApplySettings(settings);
        }

        public void SetDraggingVisualState(bool dragging)
        {
            if (isDraggingVisual == dragging) return;
            isDraggingVisual = dragging;
            UpdateVisualColor();
        }

        private void UpdateVisualColor()
        {
            float alpha = isDraggingVisual ? (settings != null ? settings.draggingOpacity : 0.55f) : 1.0f;
            Color c = wireColor;
            c.a = alpha;

            if (lineMatInstance != null)
            {
                lineMatInstance.color = c;
            }

            if (startEndpoint != null) startEndpoint.SetVisualColor(c, alpha);
            if (endEndpoint != null) endEndpoint.SetVisualColor(c, alpha);
        }

        private void LateUpdate()
        {
            if (startEndpoint == null || endEndpoint == null) return;

            if (settings != null && settings.constantScreenSpaceThickness)
            {
                UpdateConstantScreenSpaceWidth();
            }

            SetDraggingVisualState(IsDragging);
            UpdatePath();
        }

        private void UpdateConstantScreenSpaceWidth()
        {
            if (cachedCamera == null) cachedCamera = Camera.main;
            if (cachedCamera == null || lineRenderer == null) return;

            Vector3 midPoint = (startEndpoint.Position + endEndpoint.Position) * 0.5f;
            float dist = Vector3.Distance(cachedCamera.transform.position, midPoint);

            // Screen pixel width to world width at dist
            float screenWorldWidth = (settings.screenSpacePixelWidth / Screen.height) * (2f * dist * Mathf.Tan(cachedCamera.fieldOfView * 0.5f * Mathf.Deg2Rad));
            lineRenderer.startWidth = screenWorldWidth;
            lineRenderer.endWidth = screenWorldWidth;
        }

        /// <summary>
        /// Recalculates the orthogonal path waypoints and applies them to the LineRenderer.
        /// </summary>
        public void UpdatePath()
        {
            if (startEndpoint == null || endEndpoint == null) return;
            if (modelRoot == null) ResolveModelRoot();

            Vector3 startPos = startEndpoint.Position;
            Vector3 startExit = startEndpoint.ExitNormal;

            Vector3 endPos = endEndpoint.Position;
            Vector3 endExit = endEndpoint.ExitNormal;

            WireRouter.CalculateOrthogonalPath(
                startPos,
                startExit,
                endPos,
                endExit,
                modelRoot,
                settings,
                laneIndex,
                pathBuffer);

            if (lineRenderer != null)
            {
                lineRenderer.positionCount = pathBuffer.Count;
                for (int i = 0; i < pathBuffer.Count; i++)
                {
                    lineRenderer.SetPosition(i, pathBuffer[i]);
                }
            }
        }

        private void OnDestroy()
        {
            if (lineMatInstance != null)
            {
                Destroy(lineMatInstance);
            }
        }
    }
}
