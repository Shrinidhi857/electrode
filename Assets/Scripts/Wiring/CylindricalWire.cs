using System;
using UnityEngine;

namespace ContactorAR.Wiring
{
    /// <summary>
    /// Renders a dynamic, realistic 3D cylindrical wire between two spherical connector ends (EndA and EndB).
    /// Features procedural 3D tube geometry with catenary sag, URP lighting,
    /// live power flow emission effects, and touch/pinch manipulation.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [SelectionBase]
    public class CylindricalWire : MonoBehaviour
    {
        [Header("Connector Ends (Spheres)")]
        [SerializeField] private WireConnectorEnd endA;
        [SerializeField] private WireConnectorEnd endB;

        [Header("Cylindrical Geometry")]
        [Tooltip("Outer radius of the cylindrical wire.")]
        [Range(0.005f, 0.08f)]
        [SerializeField] private float wireRadius = 0.018f;

        [Tooltip("Number of radial sides forming the cylinder cross-section.")]
        [Range(6, 16)]
        [SerializeField] private int radialSegments = 10;

        [Tooltip("Number of subdivisions along the wire curve.")]
        [Range(8, 36)]
        [SerializeField] private int curveSegments = 20;

        [Tooltip("Natural sag factor simulating cable weight.")]
        [Range(0.0f, 0.5f)]
        [SerializeField] private float sagAmount = 0.12f;

        [Header("Wire Aesthetics")]
        [SerializeField] private Color wireColor = new Color(0.85f, 0.15f, 0.15f, 1.0f); // Default Red (L1)
        [SerializeField] private Color energizedGlowColor = new Color(1.0f, 0.9f, 0.2f, 1.0f);
        [SerializeField] private string phaseName = "Phase L1";

        [Header("Electrical State")]
        [SerializeField] private bool isEnergized = false;
        [SerializeField] private float voltage = 0.0f;

        public WireConnectorEnd EndA => endA;
        public WireConnectorEnd EndB => endB;
        public Color WireColor => wireColor;
        public bool IsEnergized => isEnergized;
        public float Voltage => voltage;
        public string PhaseName => phaseName;

        public event Action OnConnectionChanged;

        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private Mesh proceduralMesh;
        private Material wireMaterial;
        private Vector3[] curvePoints;

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
        }

        private void LateUpdate()
        {
            if (endA == null || endB == null) return;

            // Rebuild wire geometry when ends move
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
            wireMaterial.SetFloat("_Smoothness", 0.65f);
            meshRenderer.material = wireMaterial;
        }

        public void EnsureConnectorEnds()
        {
            if (endA == null)
            {
                endA = CreateConnectorSphere("EndA", transform.position - transform.right * 0.4f);
            }
            if (endB == null)
            {
                endB = CreateConnectorSphere("EndB", transform.position + transform.right * 0.4f);
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
            sphere.transform.localScale = Vector3.one * (wireRadius * 3.2f);

            SphereCollider sc = sphere.GetComponent<SphereCollider>();
            sc.radius = 0.65f; // Easy touch grab

            WireConnectorEnd conn = sphere.AddComponent<WireConnectorEnd>();
            return conn;
        }

        public void SetElectricalState(bool energized, float volt)
        {
            isEnergized = energized;
            voltage = volt;

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

            // Subtle electric AC pulsing effect (50Hz wave emulation)
            float pulse = 1.4f + Mathf.Sin(Time.time * 12.0f) * 0.4f;
            wireMaterial.SetColor("_EmissionColor", energizedGlowColor * pulse);
        }

        public void NotifyConnectionChanged()
        {
            OnConnectionChanged?.Invoke();
        }

        #region Procedural 3D Cylinder Mesh Generation
        public void RebuildWireMesh()
        {
            if (endA == null || endB == null) return;

            Vector3 pA = endA.transform.position;
            Vector3 pB = endB.transform.position;
            float span = Vector3.Distance(pA, pB);

            // Compute curve spline points with realistic gravity catenary droop
            float sag = Mathf.Clamp(span * sagAmount, 0.01f, 0.35f);
            Vector3 midPoint = (pA + pB) * 0.5f + Vector3.down * sag;

            for (int i = 0; i <= curveSegments; i++)
            {
                float t = (float)i / curveSegments;
                // Quadratic bezier through drooping midpoint
                Vector3 worldPt = (1 - t) * (1 - t) * pA + 2 * (1 - t) * t * midPoint + t * t * pB;
                curvePoints[i] = transform.InverseTransformPoint(worldPt);
            }

            // Generate 3D Tube Mesh vertices, normals, and UVs
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

                Vector3 up = Vector3.up;
                if (Mathf.Abs(Vector3.Dot(forward, up)) > 0.9f) up = Vector3.right;
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
        #endregion

        private void OnDestroy()
        {
            if (proceduralMesh != null) Destroy(proceduralMesh);
            if (wireMaterial != null) Destroy(wireMaterial);
        }
    }
}
