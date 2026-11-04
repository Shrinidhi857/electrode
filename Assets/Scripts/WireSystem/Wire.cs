using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Represents a routable 3D wire connecting two TerminalPorts.
/// Generates orthogonal visual segments using pooled Unity primitives (cylinders and spheres)
/// with stripped colliders, handles live previewing, and re-routes automatically when terminals move in AR.
/// </summary>
public class Wire : MonoBehaviour
{
    [Header("Connected Terminals")]
    [SerializeField] private TerminalPort startTerminal;
    [SerializeField] private TerminalPort endTerminal;

    [Header("Visual Configuration")]
    [Tooltip("Material applied to all wire cylinders and spheres. Must be assigned in the Inspector.")]
    public Material wireMaterial;

    [Tooltip("Color tint applied to the wire.")]
    public Color wireColor = new Color(0.92f, 0.22f, 0.22f, 1f);

    [Tooltip("Radius of the cylindrical wire segments in world meters (~1.6mm radius = 3.2mm diameter).")]
    public float wireRadius = 0.0016f;

    [Tooltip("Radius of the end spheres sitting on terminals in world meters (~2.5mm radius = 5mm diameter).")]
    public float endSphereRadius = 0.0025f;

    [Tooltip("Straight stub distance leaving terminals before the first 90-degree turn in meters (~1.2cm).")]
    public float stub = 0.012f;

    // State
    private bool isConnected = false;
    private Vector3 lastStartPos;
    private Vector3 lastStartDir;
    private Vector3 lastEndPos;
    private Vector3 lastEndDir;

    // Primitives pooling
    private readonly List<GameObject> cylinderPool = new List<GameObject>();
    private readonly List<GameObject> cornerSpherePool = new List<GameObject>();
    private GameObject startSphere;
    private GameObject endSphere;
    private MaterialPropertyBlock propertyBlock;

    // Global registry & connection events
    public static readonly List<Wire> AllWires = new List<Wire>();
    public static event System.Action<TerminalPort, TerminalPort, Wire> OnWireConnected;
    public static event System.Action<TerminalPort, TerminalPort, Wire> OnWireDisconnected;

    public TerminalPort StartTerminal => startTerminal;
    public TerminalPort EndTerminal => endTerminal;
    public bool IsConnected => isConnected;

    private void Awake()
    {
        // Enforce identity transform at scene root
        transform.SetParent(null);
        transform.position = Vector3.zero;
        transform.rotation = Quaternion.identity;
        transform.localScale = Vector3.one;

        propertyBlock = new MaterialPropertyBlock();
    }

    private void OnEnable()
    {
        if (!AllWires.Contains(this))
        {
            AllWires.Add(this);
        }
    }

    private void OnDisable()
    {
        AllWires.Remove(this);
    }

    /// <summary>
    /// Configures the wire styling parameters.
    /// </summary>
    public void ConfigureVisuals(Material mat, Color col, float radius, float endRadius, float stubLen)
    {
        wireMaterial = mat;
        wireColor = col;
        wireRadius = Mathf.Clamp(radius, 0.0008f, 0.006f);
        endSphereRadius = Mathf.Clamp(endRadius, 0.0015f, 0.008f);
        stub = Mathf.Clamp(stubLen, 0.005f, 0.03f);

        ApplyPropertyBlockToAll();
    }

    /// <summary>
    /// Updates live preview during dragging towards a free world coordinate.
    /// </summary>
    public void ShowFree(TerminalPort from, Vector3 worldPoint)
    {
        if (from == null) return;

        startTerminal = from;
        endTerminal = null;
        isConnected = false;

        List<Vector3> points = WireRouter.Route(
            from.Position, from.ExitDir,
            worldPoint, Vector3.zero,
            stub,
            GetBodyBounds(from));
        UpdateVisuals(points);
    }

    /// <summary>
    /// Updates live preview when snapped to a target terminal candidate.
    /// </summary>
    public void ShowSnapped(TerminalPort from, TerminalPort to)
    {
        if (from == null || to == null) return;

        startTerminal = from;
        endTerminal = to;
        isConnected = false;

        List<Vector3> points = WireRouter.Route(
            from.Position, from.ExitDir,
            to.Position, to.ExitDir,
            stub,
            GetBodyBounds(from),
            GetBodyBounds(to));
        UpdateVisuals(points);
    }

    /// <summary>
    /// Commits the permanent connection between two terminals.
    /// </summary>
    public void Connect(TerminalPort from, TerminalPort to)
    {
        if (from == null || to == null) return;

        startTerminal = from;
        endTerminal = to;
        isConnected = true;

        startTerminal.wireCount++;
        endTerminal.wireCount++;

        lastStartPos = startTerminal.Position;
        lastStartDir = startTerminal.ExitDir;
        lastEndPos = endTerminal.Position;
        lastEndDir = endTerminal.ExitDir;

        List<Vector3> points = WireRouter.Route(
            startTerminal.Position, startTerminal.ExitDir,
            endTerminal.Position, endTerminal.ExitDir,
            stub,
            GetBodyBounds(startTerminal),
            GetBodyBounds(endTerminal));
        UpdateVisuals(points);

        OnWireConnected?.Invoke(startTerminal, endTerminal, this);
    }

    private void LateUpdate()
    {
        if (!isConnected) return;

        // If either terminal GameObject is destroyed, destroy this wire
        if (startTerminal == null || endTerminal == null)
        {
            Destroy(gameObject);
            return;
        }

        // Re-route automatically if either terminal moved or rotated (e.g. AR tracking or scene move)
        Vector3 curStartPos = startTerminal.Position;
        Vector3 curStartDir = startTerminal.ExitDir;
        Vector3 curEndPos = endTerminal.Position;
        Vector3 curEndDir = endTerminal.ExitDir;

        if (curStartPos != lastStartPos || curStartDir != lastStartDir ||
            curEndPos != lastEndPos || curEndDir != lastEndDir)
        {
            lastStartPos = curStartPos;
            lastStartDir = curStartDir;
            lastEndPos = curEndPos;
            lastEndDir = curEndDir;

            List<Vector3> points = WireRouter.Route(
                curStartPos, curStartDir,
                curEndPos, curEndDir,
                stub,
                GetBodyBounds(startTerminal),
                GetBodyBounds(endTerminal));
            UpdateVisuals(points);
        }
    }

    private void OnDestroy()
    {
        if (isConnected)
        {
            if (startTerminal != null)
            {
                startTerminal.wireCount = Mathf.Max(0, startTerminal.wireCount - 1);
            }
            if (endTerminal != null)
            {
                endTerminal.wireCount = Mathf.Max(0, endTerminal.wireCount - 1);
            }

            OnWireDisconnected?.Invoke(startTerminal, endTerminal, this);
        }
    }

    /// <summary>
    /// Returns the combined world-space renderer bounds of the parent component body
    /// that owns the terminal. Walks up the hierarchy to find the root model.
    /// Returns null if no renderers are found.
    /// </summary>
    private static Bounds? GetBodyBounds(TerminalPort terminal)
    {
        if (terminal == null) return null;

        // Walk up from the terminal to find the first ancestor with renderers
        // (the contactor/breaker root usually has the bulk of the geometry)
        Transform t = terminal.transform.parent;
        while (t != null)
        {
            Renderer[] rends = t.GetComponentsInChildren<Renderer>();
            if (rends != null && rends.Length > 0)
            {
                Bounds combined = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++)
                    combined.Encapsulate(rends[i].bounds);
                return combined;
            }
            t = t.parent;
        }
        return null;
    }

    /// <summary>
    /// Renders the wire waypoints using pooled cylinders and spheres.
    /// </summary>
    private void UpdateVisuals(List<Vector3> points)
    {
        if (points == null || points.Count < 2)
        {
            HideAllVisuals();
            return;
        }

        int pointCount = points.Count;
        int segmentCount = pointCount - 1;
        int cornerCount = Mathf.Max(0, pointCount - 2);

        // 1. Start sphere
        if (startSphere == null)
        {
            startSphere = CreatePrimitiveObject(PrimitiveType.Sphere, "StartSphere");
        }
        startSphere.SetActive(true);
        startSphere.transform.position = points[0];
        startSphere.transform.localScale = Vector3.one * (endSphereRadius * 2f);

        // 2. End sphere
        if (endSphere == null)
        {
            endSphere = CreatePrimitiveObject(PrimitiveType.Sphere, "EndSphere");
        }
        endSphere.SetActive(true);
        endSphere.transform.position = points[pointCount - 1];
        endSphere.transform.localScale = Vector3.one * (endSphereRadius * 2f);

        // 3. Corner spheres at bends
        while (cornerSpherePool.Count < cornerCount)
        {
            cornerSpherePool.Add(CreatePrimitiveObject(PrimitiveType.Sphere, $"Corner_{cornerSpherePool.Count}"));
        }
        for (int i = 0; i < cornerSpherePool.Count; i++)
        {
            if (i < cornerCount)
            {
                cornerSpherePool[i].SetActive(true);
                cornerSpherePool[i].transform.position = points[i + 1];
                cornerSpherePool[i].transform.localScale = Vector3.one * (wireRadius * 2f);
            }
            else
            {
                cornerSpherePool[i].SetActive(false);
            }
        }

        // 4. Cylinders per segment
        while (cylinderPool.Count < segmentCount)
        {
            cylinderPool.Add(CreatePrimitiveObject(PrimitiveType.Cylinder, $"Cylinder_{cylinderPool.Count}"));
        }
        for (int i = 0; i < cylinderPool.Count; i++)
        {
            if (i < segmentCount)
            {
                Vector3 pA = points[i];
                Vector3 pB = points[i + 1];
                float length = Vector3.Distance(pA, pB);

                if (length > 0.0001f)
                {
                    cylinderPool[i].SetActive(true);
                    cylinderPool[i].transform.position = (pA + pB) * 0.5f;
                    cylinderPool[i].transform.up = (pB - pA).normalized;
                    cylinderPool[i].transform.localScale = new Vector3(wireRadius * 2f, length * 0.5f, wireRadius * 2f);
                }
                else
                {
                    cylinderPool[i].SetActive(false);
                }
            }
            else
            {
                cylinderPool[i].SetActive(false);
            }
        }
    }

    private void HideAllVisuals()
    {
        if (startSphere != null) startSphere.SetActive(false);
        if (endSphere != null) endSphere.SetActive(false);
        for (int i = 0; i < cornerSpherePool.Count; i++)
        {
            if (cornerSpherePool[i] != null) cornerSpherePool[i].SetActive(false);
        }
        for (int i = 0; i < cylinderPool.Count; i++)
        {
            if (cylinderPool[i] != null) cylinderPool[i].SetActive(false);
        }
    }

    private GameObject CreatePrimitiveObject(PrimitiveType type, string primitiveName)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = primitiveName;

        // Primitives MUST not have colliders
        Collider col = go.GetComponent<Collider>();
        if (col != null)
        {
            if (Application.isPlaying)
            {
                Destroy(col);
            }
            else
            {
                DestroyImmediate(col);
            }
        }

        go.transform.SetParent(transform, false);

        MeshRenderer mr = go.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            if (wireMaterial != null)
            {
                mr.sharedMaterial = wireMaterial;
            }
            ApplyPropertyBlock(mr);
        }

        return go;
    }

    private void ApplyPropertyBlock(MeshRenderer mr)
    {
        if (mr == null) return;
        if (propertyBlock == null) propertyBlock = new MaterialPropertyBlock();

        mr.GetPropertyBlock(propertyBlock);
        propertyBlock.SetColor("_BaseColor", wireColor);
        propertyBlock.SetColor("_Color", wireColor);
        mr.SetPropertyBlock(propertyBlock);
    }

    private void ApplyPropertyBlockToAll()
    {
        if (startSphere != null) ApplyPropertyBlock(startSphere.GetComponent<MeshRenderer>());
        if (endSphere != null) ApplyPropertyBlock(endSphere.GetComponent<MeshRenderer>());
        for (int i = 0; i < cornerSpherePool.Count; i++)
        {
            if (cornerSpherePool[i] != null) ApplyPropertyBlock(cornerSpherePool[i].GetComponent<MeshRenderer>());
        }
        for (int i = 0; i < cylinderPool.Count; i++)
        {
            if (cylinderPool[i] != null) ApplyPropertyBlock(cylinderPool[i].GetComponent<MeshRenderer>());
        }
    }
}
