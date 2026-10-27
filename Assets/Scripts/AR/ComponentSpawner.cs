using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ContactorAR.Breaker;

namespace ContactorAR.AR
{
    /// <summary>
    /// Describes one spawnable component that appears in the sidebar browser.
    /// Attach to a prefab in Resources/Components/ or assign prefab directly.
    /// </summary>
    [System.Serializable]
    public class ComponentEntry
    {
        [Tooltip("Display name shown in the sidebar button.")]
        public string displayName = "Component";

        [Tooltip("Short one-line description shown under the name.")]
        public string description = "";

        [Tooltip("Icon shown in the sidebar block. Leave null to use auto-generated label.")]
        public Sprite icon;

        [Tooltip("Prefab to instantiate when the button is tapped.")]
        public GameObject prefab;

        [Tooltip("Accent color for this block in the sidebar.")]
        public Color accentColor = new Color(0.18f, 0.42f, 0.72f, 1f);
    }

    /// <summary>
    /// Manages the component library:
    ///   - Spawns new model instances in front of the camera when requested via the sidebar
    ///   - Stores all live model instances
    ///   - Allows long-press-to-select + drag-to-move + tap-to-place AR placement per model
    ///
    /// Add this to the WireManager / Scene Controller GameObject.
    /// Register <see cref="ComponentEntry"/> items in the Inspector.
    /// </summary>
    public class ComponentSpawner : MonoBehaviour
    {
        [Header("Component Library")]
        [Tooltip("List of spawnable components shown in the sidebar.")]
        [SerializeField] public List<ComponentEntry> components = new List<ComponentEntry>();

        [Header("Placement Settings")]
        [Tooltip("Distance in front of the camera at which a new model is spawned.")]
        [SerializeField] private float spawnDistance = 1.5f;

        // All live model instances tracked by this spawner
        private readonly List<SpawnedModel> liveModels = new List<SpawnedModel>();
        private Camera mainCam;

        private void Awake()
        {
            mainCam = Camera.main;
        }

        // ──────────────────────────────────────────────────────────
        //  Public API called from ARContactorUI sidebar
        // ──────────────────────────────────────────────────────────

        /// <summary>
        /// Spawns a new instance of the given ComponentEntry's prefab in front of the camera,
        /// or clones the existing contactor/breaker model loaded in the scene.
        /// The new model is placed cleanly (side-by-side on DIN rail or in front of camera)
        /// and starts with fresh, unconnected terminals.
        /// </summary>
        public GameObject SpawnComponent(int index, string preferredName = null)
        {
            if (mainCam == null) mainCam = Camera.main;

            GameObject sourceGO = null;
            string displayName = preferredName;

            // 1. Try from registered components list
            if (index >= 0 && index < components.Count && components[index] != null)
            {
                ComponentEntry entry = components[index];
                sourceGO = entry.prefab;
                if (string.IsNullOrEmpty(displayName)) displayName = entry.displayName;
            }

            // 2. Fallback: Find existing contactor/breaker in scene to clone
            if (sourceGO == null)
            {
                // First try ContactorController
                ContactorController cc = FindFirstObjectByType<ContactorController>();
                if (cc != null)
                {
                    sourceGO = cc.transform.root.gameObject;
                    if (string.IsNullOrEmpty(displayName)) displayName = "Contactor";
                }
                else
                {
                    // Second try any object with TerminalPort
                    TerminalPort anyPort = FindFirstObjectByType<TerminalPort>();
                    if (anyPort != null)
                    {
                        sourceGO = anyPort.transform.root.gameObject;
                        if (string.IsNullOrEmpty(displayName)) displayName = "Breaker";
                    }
                    else
                    {
                        // Third try name search
                        string query = !string.IsNullOrEmpty(displayName) ? displayName : "Breaker";
                        sourceGO = FindSceneObjectByName(query)
                                ?? FindSceneObjectByName("Contactor")
                                ?? FindSceneObjectByName("Four-Pole");
                    }
                }
            }

            if (sourceGO == null)
            {
                Debug.LogWarning($"[ComponentSpawner] No prefab or scene object found to spawn '{displayName}'.");
                return null;
            }

            // Determine spawn position & rotation
            Vector3 spawnPos = GetSpawnPosition(sourceGO);
            Quaternion spawnRot = sourceGO.transform.rotation;

            int cloneNumber = liveModels.Count + 1;
            GameObject newInstance = Instantiate(sourceGO, spawnPos, spawnRot);
            newInstance.name = $"{displayName ?? "Component"}_{cloneNumber}";

            // Ensure the model has ARModelManipulator for repositioning
            ARModelManipulator manip = newInstance.GetComponent<ARModelManipulator>();
            if (manip == null) manip = newInstance.AddComponent<ARModelManipulator>();
            manip.ReleaseToFreeMovable();

            // Reset wire connections and generate unique IDs for all terminals on the new instance
            TerminalPort[] ports = newInstance.GetComponentsInChildren<TerminalPort>(true);
            for (int i = 0; i < ports.Length; i++)
            {
                ports[i].wireCount = 0;
                ports[i].terminalId = $"{newInstance.name}_{ports[i].name}";
            }

            ContactorAR.Wiring.TerminalPoint[] points = newInstance.GetComponentsInChildren<ContactorAR.Wiring.TerminalPoint>(true);
            for (int i = 0; i < points.Length; i++)
            {
                points[i].ResetConnection();
            }

            ComponentEntry entryRef = (index >= 0 && index < components.Count)
                ? components[index]
                : new ComponentEntry { displayName = displayName ?? "Component" };

            ARModelManipulator.Select(manip);

            SpawnedModel sm = new SpawnedModel(newInstance, manip, entryRef);
            liveModels.Add(sm);

            Debug.Log($"[ComponentSpawner] Added component instance: '{newInstance.name}' at {spawnPos} (total: {liveModels.Count})");
            return newInstance;
        }

        /// <summary>Returns a snapshot list of all live spawned models.</summary>
        public IReadOnlyList<SpawnedModel> LiveModels => liveModels;

        private Vector3 GetSpawnPosition(GameObject sourceGO = null)
        {
            if (mainCam == null) mainCam = Camera.main;

            // If sourceGO is in front of the camera, place the new one side-by-side to the right (DIN rail style)
            if (sourceGO != null && mainCam != null)
            {
                Transform refTrans = liveModels.Count > 0 ? liveModels[liveModels.Count - 1].GameObject.transform : sourceGO.transform;
                Vector3 toRef = refTrans.position - mainCam.transform.position;
                if (Vector3.Dot(mainCam.transform.forward, toRef.normalized) > 0.1f && toRef.magnitude < 6f)
                {
                    Vector3 rightDir = refTrans.right;
                    return refTrans.position + rightDir * 0.32f;
                }
            }

            if (mainCam != null)
                return mainCam.transform.position + mainCam.transform.forward * spawnDistance;

            return sourceGO != null ? sourceGO.transform.position + Vector3.right * 0.32f : Vector3.zero;
        }

        /// <summary>
        /// Finds a root-level scene GameObject whose name contains the given query (case-insensitive).
        /// Used as fallback when no prefab is assigned in the ComponentEntry.
        /// </summary>
        private static GameObject FindSceneObjectByName(string query)
        {
            if (string.IsNullOrEmpty(query)) return null;
            string lq = query.ToLowerInvariant();

            GameObject[] all = FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            GameObject bestMatch = null;
            int bestDepth = int.MaxValue;
            foreach (GameObject go in all)
            {
                if (go == null) continue;
                // Exclude UI and camera GameObjects
                if (go.GetComponentInParent<Canvas>() != null) continue;
                if (go.GetComponent<Camera>() != null) continue;

                if (go.name.ToLowerInvariant().Contains(lq))
                {
                    int depth = 0;
                    Transform t = go.transform;
                    while (t.parent != null) { depth++; t = t.parent; }
                    if (depth < bestDepth)
                    {
                        bestDepth = depth;
                        bestMatch = go.transform.root.gameObject;
                    }
                }
            }
            return bestMatch;
        }

        // ──────────────────────────────────────────────────────────
        //  Inner data class
        // ──────────────────────────────────────────────────────────

        public class SpawnedModel
        {
            public GameObject GameObject { get; }
            public ARModelManipulator Manipulator { get; }
            public ComponentEntry Entry { get; }

            public SpawnedModel(GameObject go, ARModelManipulator manip, ComponentEntry entry)
            {
                GameObject = go;
                Manipulator = manip;
                Entry = entry;
            }
        }
    }
}
