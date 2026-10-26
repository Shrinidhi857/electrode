using System;
using System.Collections.Generic;
using UnityEngine;

namespace ContactorAR.Wiring
{
    public enum WireRoutingStyle
    {
        PipelineElbow, // Lucidchart-style straight pipeline with neat 90-degree rounded elbows
        StraightLine,   // Direct rigid pipeline point A to B
        CatenarySag    // Soft sagging cable
    }

    /// <summary>
    /// Renders a sleek, thin 3D pipeline/conduit wire between two spherical snap-connector ends.
    /// Replicates Lucidchart-style orthogonal 90-degree pipeline routing,
    /// dynamic snap validation (wire only completes and exists when both ends attach to terminals),
    /// and live electrical glow simulation.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [SelectionBase]
    public class CylindricalWire : MonoBehaviour
    {
        [Header("Connector Ends (Spheres)")]
        [SerializeField] private WireConnectorEnd endA;
        [SerializeField] private WireConnectorEnd endB;

        [Header("Pipeline Routing Style")]
        [Tooltip("Routing mode: PipelineElbow (Lucid 90-degree straight conduits), StraightLine, or CatenarySag.")]
        [SerializeField] private WireRoutingStyle routingStyle = WireRoutingStyle.PipelineElbow;

        [Header("Cylindrical Geometry (Thin & Sleek)")]
        [Tooltip("Outer radius of the wire. Kept thin and realistic (3.5mm radius = 7mm diameter).")]
        [Range(0.0015f, 0.015f)]
        [SerializeField] private float wireRadius = 0.0035f;

        [Tooltip("Size multiplier for the spherical snap connector ends.")]
        [Range(1.8f, 4.0f)]
        [SerializeField] private float sphereScaleMultiplier = 2.6f;

        [Tooltip("Number of radial sides forming the cylinder cross-section.")]
        [Range(6, 16)]
        [SerializeField] private int radialSegments = 10;

        [Tooltip("Number of subdivisions along the wire curve.")]
        [Range(12, 48)]
        [SerializeField] private int curveSegments = 28;

        [Tooltip("Corner rounding radius for 90-degree pipeline elbows.")]
        [Range(0.005f, 0.04f)]
        [SerializeField] private float elbowCornerRadius = 0.016f;

        [Header("Wire Aesthetics")]
        [SerializeField] private Color wireColor = new Color(0.85f, 0.15f, 0.15f, 1.0f); // Default Red (L1)
        [SerializeField] private Color energizedGlowColor = new Color(1.0f, 0.9f, 0.2f, 1.0f);
        [SerializeField] private string phaseName = "Phase L1";

        [Header("Electrical & Connection State")]
        [SerializeField] private bool isEnergized = false;
        [SerializeField] private float voltage = 0.0f;

        public WireConnectorEnd EndA => endA;
        public WireConnectorEnd EndB => endB;
        public Color WireColor => wireColor;
        public bool IsEnergized => isEnergized;
        public float Voltage => voltage;
        public string PhaseName => phaseName;
        public float WireRadius => wireRadius;

        /// <summary>
        /// A wire is only complete and establishes circuit flow when BOTH ends are attached to actual terminals.
        /// </summary>
        public bool IsFullyConnected => (endA != null && endA.IsConnected && endB != null && endB.IsConnected);

        public event Action OnConnectionChanged;

        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private Mesh proceduralMesh;
        private Material wireMaterial;
        private Vector3[] curvePoints;
        private readonly List<Vector3> s_PipelineWaypoints = new List<Vector3>(16);

        private void Awake()
        {
            meshFilter = GetComponent<MeshFilter>();
            meshRenderer = GetComponent<MeshRenderer>();

            proceduralMesh = new Mesh { name = "CylindricalWireMesh" };
            meshFilter.sharedMesh = proceduralMesh;

            SetupMaterial();
            curvePoints = new Vector3[curveSegments + 1];
        }

        private void Start()
        {
            EnsureConnectorEnds();
            RebuildWireMesh();
            UpdateVisualAppearance();
        }

        private void LateUpdate()
        {
            if (endA == null || endB == null) return;

            // Rebuild wire geometry when either end moves
            if (endA.transform.hasChanged || endB.transform.hasChanged || transform.hasChanged)
            {
                RebuildWireMesh();
                endA.transform.hasChanged = false;
                endB.transform.hasChanged = false;
                transform.hasChanged = false;
            }

            UpdateEmissionPulse();
        }

        public void Setup(WireConnectorEnd a, WireConnectorEnd b, Color color, string phase)
        {
            endA = a;
            endB = b;
            wireColor = color;
            phaseName = phase;

            if (endA != null) endA.SetParentWire(this);
            if (endB != null) endB.SetParentWire(this);

            SetupMaterial();
            RebuildWireMesh();
            UpdateVisualAppearance();
        }

        private void SetupMaterial()
        {
            if (meshRenderer == null) meshRenderer = GetComponent<MeshRenderer>();

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") 
                         ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                         ?? Shader.Find("Standard");

            wireMaterial = new Material(shader);
            wireMaterial.color = wireColor;
            wireMaterial.SetColor("_BaseColor", wireColor);
            wireMaterial.SetFloat("_Smoothness", 0.7f);
            meshRenderer.material = wireMaterial;
        }

        public void EnsureConnectorEnds()
        {
            if (endA == null)
            {
                endA = CreateConnectorSphere("EndA", transform.position - transform.right * 0.15f);
            }
            if (endB == null)
            {
                endB = CreateConnectorSphere("EndB", transform.position + transform.right * 0.15f);
            }

            endA.SetParentWire(this);
            endB.SetParentWire(this);
        }

        private WireConnectorEnd CreateConnectorSphere(string name, Vector3 pos)
        {
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = name;
            sphere.transform.SetParent(transform, true);
            sphere.transform.position = pos;

            // Small, sleek metallic connector sphere
            float sphereRadius = Mathf.Max(wireRadius * sphereScaleMultiplier, 0.008f);
            sphere.transform.localScale = Vector3.one * (sphereRadius * 2.0f);

            // Generous touch collider so user can easily grab it with a finger
            SphereCollider sc = sphere.GetComponent<SphereCollider>();
            sc.radius = 1.6f;

            WireConnectorEnd conn = sphere.AddComponent<WireConnectorEnd>();
            return conn;
        }

        /// <summary>
        /// Validates connection state. If both ends are not connected and wire was dropped in empty space, cleans it up.
        /// </summary>
        public void ValidateConnectionState()
        {
            // If neither end is connected to any terminal, user abandoned it in mid-air -> remove it
            if ((endA == null || !endA.IsConnected) && (endB == null || !endB.IsConnected))
            {
                Debug.Log($"[CylindricalWire] Wire {name} dropped without any terminal connections. Auto-cleaning.");
                Destroy(gameObject);
                return;
            }

            UpdateVisualAppearance();
            OnConnectionChanged?.Invoke();
        }

        private void UpdateVisualAppearance()
        {
            if (wireMaterial == null) return;

            if (IsFullyConnected)
            {
                // Solid, full-opacity active circuit conduit
                Color c = wireColor;
                c.a = 1.0f;
                wireMaterial.color = c;
                wireMaterial.SetColor("_BaseColor", c);
            }
            else
            {
                // Semi-translucent draft state indicating it is still seeking its second terminal
                Color c = wireColor;
                c.a = 0.65f;
                wireMaterial.color = c;
                wireMaterial.SetColor("_BaseColor", c);
            }
        }

        public void SetElectricalState(bool energized, float volt)
        {
            // A wire can only be energized if both ends connect to actual terminals!
            isEnergized = energized && IsFullyConnected;
            voltage = isEnergized ? volt : 0.0f;

            if (wireMaterial != null)
            {
                if (isEnergized)
                {
                    wireMaterial.EnableKeyword("_EMISSION");
                    wireMaterial.SetColor("_EmissionColor", energizedGlowColor * 1.8f);
                }
                else
                {
                    wireMaterial.DisableKeyword("_EMISSION");
                    wireMaterial.SetColor("_EmissionColor", Color.black);
                }
            }
        }

        private void UpdateEmissionPulse()
        {
            if (!isEnergized || wireMaterial == null) return;

            // Electric pulse effect
            float pulse = 1.3f + Mathf.Sin(Time.time * 12.0f) * 0.35f;
            wireMaterial.SetColor("_EmissionColor", energizedGlowColor * pulse);
        }

        public void NotifyConnectionChanged()
        {
            ValidateConnectionState();
        }

        #region Procedural 3D Pipeline & Wire Mesh Generation
        public void RebuildWireMesh()
        {
            if (endA == null || endB == null) return;

            Vector3 pA = endA.transform.position;
            Vector3 pB = endB.transform.position;

            // Generate path waypoints based on selected routing mode
            switch (routingStyle)
            {
                case WireRoutingStyle.PipelineElbow:
                    BuildPipelineElbowPath(pA, pB);
                    break;
                case WireRoutingStyle.StraightLine:
                    BuildStraightLinePath(pA, pB);
                    break;
                case WireRoutingStyle.CatenarySag:
                default:
                    BuildCatenaryPath(pA, pB);
                    break;
            }

            // Generate 3D Tube Mesh vertices, normals, and UVs around curvePoints
            int ringVertexCount = radialSegments + 1;
            int totalVertices = (curveSegments + 1) * ringVertexCount;
            int totalIndices = curveSegments * radialSegments * 6;

            Vector3[] vertices = new Vector3[totalVertices];
            Vector3[] normals = new Vector3[totalVertices];
            Vector2[] uvs = new Vector2[totalVertices];
            int[] triangles = new int[totalIndices];

            int vertIdx = 0;
            int triIdx = 0;

            for (int i = 0; i <= curveSegments; i++)
            {
                Vector3 current = curvePoints[i];
                Vector3 forward;
                if (i == 0) forward = (curvePoints[1] - current).normalized;
                else if (i == curveSegments) forward = (current - curvePoints[i - 1]).normalized;
                else forward = (curvePoints[i + 1] - curvePoints[i - 1]).normalized;

                if (forward == Vector3.zero) forward = Vector3.forward;

                Vector3 up = Vector3.up;
                if (Mathf.Abs(Vector3.Dot(forward, up)) > 0.88f) up = Vector3.right;
                Vector3 right = Vector3.Cross(forward, up).normalized;
                up = Vector3.Cross(right, forward).normalized;

                float vCoord = (float)i / curveSegments;

                for (int r = 0; r <= radialSegments; r++)
                {
                    float angle = (float)r / radialSegments * Mathf.PI * 2.0f;
                    float cos = Mathf.Cos(angle);
                    float sin = Mathf.Sin(angle);

                    Vector3 normal = (right * cos + up * sin).normalized;
                    vertices[vertIdx] = current + normal * wireRadius;
                    normals[vertIdx] = normal;
                    uvs[vertIdx] = new Vector2((float)r / radialSegments, vCoord * 4.0f);
                    vertIdx++;
                }
            }

            for (int i = 0; i < curveSegments; i++)
            {
                int ringStart = i * ringVertexCount;
                int nextRingStart = (i + 1) * ringVertexCount;

                for (int r = 0; r < radialSegments; r++)
                {
                    int current = ringStart + r;
                    int next = current + 1;
                    int above = nextRingStart + r;
                    int aboveNext = above + 1;

                    triangles[triIdx++] = current;
                    triangles[triIdx++] = above;
                    triangles[triIdx++] = next;

                    triangles[triIdx++] = next;
                    triangles[triIdx++] = above;
                    triangles[triIdx++] = aboveNext;
                }
            }

            proceduralMesh.Clear();
            proceduralMesh.vertices = vertices;
            proceduralMesh.normals = normals;
            proceduralMesh.uv = uvs;
            proceduralMesh.triangles = triangles;
            proceduralMesh.RecalculateBounds();
        }

        /// <summary>
        /// Lucidchart-style straight pipeline connection with clean 90-degree rounded elbows.
        /// </summary>
        private void BuildPipelineElbowPath(Vector3 pA, Vector3 pB)
        {
            s_PipelineWaypoints.Clear();

            // 1. Determine outward exit normals
            Vector3 exitA = (endA != null && endA.SnappedTerminal != null)
                ? endA.SnappedTerminal.OutwardDirection
                : (pB - pA).normalized;

            Vector3 exitB = (endB != null && endB.SnappedTerminal != null)
                ? endB.SnappedTerminal.OutwardDirection
                : (pA - pB).normalized;

            float span = Vector3.Distance(pA, pB);
            float stub = Mathf.Clamp(span * 0.14f, 0.025f, 0.08f);

            Vector3 stubA = pA + exitA * stub;
            Vector3 stubB = pB + exitB * stub;

            s_PipelineWaypoints.Add(pA);
            s_PipelineWaypoints.Add(stubA);

            // 2. Generate 90-degree orthogonal intermediate pipe corners
            Vector3 mid = (stubA + stubB) * 0.5f;

            // Route through lateral plane or vertical column
            if (Mathf.Abs(stubA.y - stubB.y) > Mathf.Abs(stubA.x - stubB.x))
            {
                // Dominant vertical: route Out -> Horizontal step -> Vertical run -> In
                Vector3 c1 = new Vector3(stubA.x, mid.y, stubA.z);
                Vector3 c2 = new Vector3(stubB.x, mid.y, stubB.z);
                s_PipelineWaypoints.Add(c1);
                s_PipelineWaypoints.Add(c2);
            }
            else
            {
                // Dominant horizontal: route Out -> Vertical step -> Horizontal run -> In
                Vector3 c1 = new Vector3(mid.x, stubA.y, stubA.z);
                Vector3 c2 = new Vector3(mid.x, stubB.y, stubB.z);
                s_PipelineWaypoints.Add(c1);
                s_PipelineWaypoints.Add(c2);
            }

            s_PipelineWaypoints.Add(stubB);
            s_PipelineWaypoints.Add(pB);

            // 3. Smooth corners with pipeline fillets and sample into curvePoints
            SamplePolylineWithCornerFillets(s_PipelineWaypoints, elbowCornerRadius, curvePoints);
        }

        private void BuildStraightLinePath(Vector3 pA, Vector3 pB)
        {
            for (int i = 0; i <= curveSegments; i++)
            {
                float t = (float)i / curveSegments;
                Vector3 worldPt = Vector3.Lerp(pA, pB, t);
                curvePoints[i] = transform.InverseTransformPoint(worldPt);
            }
        }

        private void BuildCatenaryPath(Vector3 pA, Vector3 pB)
        {
            float span = Vector3.Distance(pA, pB);
            float sag = Mathf.Clamp(span * 0.08f, 0.005f, 0.15f);
            Vector3 midPoint = (pA + pB) * 0.5f + Vector3.down * sag;

            for (int i = 0; i <= curveSegments; i++)
            {
                float t = (float)i / curveSegments;
                Vector3 worldPt = (1 - t) * (1 - t) * pA + 2 * (1 - t) * t * midPoint + t * t * pB;
                curvePoints[i] = transform.InverseTransformPoint(worldPt);
            }
        }

        private void SamplePolylineWithCornerFillets(List<Vector3> waypoints, float radius, Vector3[] output)
        {
            if (waypoints.Count < 2) return;

            // Build total length along the polyline
            List<Vector3> densePoints = new List<Vector3>(64);
            densePoints.Add(waypoints[0]);

            for (int i = 1; i < waypoints.Count - 1; i++)
            {
                Vector3 pPrev = waypoints[i - 1];
                Vector3 pCurr = waypoints[i];
                Vector3 pNext = waypoints[i + 1];

                Vector3 d1 = (pPrev - pCurr).normalized;
                Vector3 d2 = (pNext - pCurr).normalized;

                float maxR = Mathf.Min(Vector3.Distance(pPrev, pCurr), Vector3.Distance(pNext, pCurr)) * 0.45f;
                float r = Mathf.Min(radius, maxR);

                Vector3 startFillet = pCurr + d1 * r;
                Vector3 endFillet = pCurr + d2 * r;

                densePoints.Add(startFillet);

                // Small quadratic fillet bend (3-4 sub points)
                for (int s = 1; s <= 4; s++)
                {
                    float t = s / 5.0f;
                    Vector3 filletPt = (1 - t) * (1 - t) * startFillet + 2 * (1 - t) * t * pCurr + t * t * endFillet;
                    densePoints.Add(filletPt);
                }

                densePoints.Add(endFillet);
            }

            densePoints.Add(waypoints[waypoints.Count - 1]);

            // Resample densePoints into curvePoints array
            float[] cumDist = new float[densePoints.Count];
            cumDist[0] = 0;
            for (int i = 1; i < densePoints.Count; i++)
            {
                cumDist[i] = cumDist[i - 1] + Vector3.Distance(densePoints[i - 1], densePoints[i]);
            }

            float totalLength = cumDist[cumDist.Length - 1];
            if (totalLength < 0.0001f) totalLength = 0.0001f;

            for (int i = 0; i <= curveSegments; i++)
            {
                float targetDist = ((float)i / curveSegments) * totalLength;

                // Find segment
                int seg = 0;
                while (seg < cumDist.Length - 2 && cumDist[seg + 1] < targetDist)
                {
                    seg++;
                }

                float segLen = cumDist[seg + 1] - cumDist[seg];
                float t = segLen > 0.0001f ? (targetDist - cumDist[seg]) / segLen : 0f;
                Vector3 worldPt = Vector3.Lerp(densePoints[seg], densePoints[seg + 1], t);
                output[i] = transform.InverseTransformPoint(worldPt);
            }
        }
        #endregion
    }
}
