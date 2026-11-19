using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ContactorAR.Breaker;
using ContactorAR.Wiring;

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
        [Tooltip("Distance in front of the camera at which a new model is spawned (1-2 feet = ~0.5m).")]
        [SerializeField] private float spawnDistance = 0.50f;

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

            // 2. Load directly from Assets via ModelAssetLoader
            if (sourceGO == null)
            {
                string query = !string.IsNullOrEmpty(displayName) ? displayName.ToLowerInvariant() : "";
                if (query.Contains("mcb") || query.Contains("breaker") || index == 0)
                {
                    sourceGO = ModelAssetLoader.LoadMCBModel();
                    if (string.IsNullOrEmpty(displayName)) displayName = "MCB";
                }
                else if (query.Contains("contactor") || index == 1)
                {
                    sourceGO = ModelAssetLoader.LoadContactorModel();
                    if (string.IsNullOrEmpty(displayName)) displayName = "Contactor";
                }
                else if (query.Contains("motor"))
                {
                    sourceGO = ModelAssetLoader.LoadMotorModel();
                    if (string.IsNullOrEmpty(displayName)) displayName = "Motor";
                }
            }

            // 3. Fallback: Find existing contactor/breaker in scene to clone
            if (sourceGO == null)
            {
                // First try ContactorController
                ContactorController cc = FindAnyObjectByType<ContactorController>();
                if (cc != null)
                {
                    sourceGO = cc.transform.root.gameObject;
                    if (string.IsNullOrEmpty(displayName)) displayName = "Contactor";
                }
                else
                {
                    // Second try any object with TerminalPort
                    TerminalPort anyPort = FindAnyObjectByType<TerminalPort>();
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
                                ?? FindSceneObjectByName("3_pole_mcb")
                                ?? FindSceneObjectByName("Four-Pole");
                    }
                }
            }

            if (sourceGO == null)
            {
                Debug.LogWarning($"[ComponentSpawner] No prefab or scene object found to spawn '{displayName}'.");
                return null;
            }

            // Determine spawn position & rotation: always 1-2 feet directly in front of camera, facing user
            Vector3 spawnPos = GetSpawnPosition(sourceGO);
            Quaternion spawnRot = (mainCam != null)
                ? Quaternion.LookRotation(-mainCam.transform.forward, Vector3.up)
                : Quaternion.Euler(0, 180, 0);

            int cloneNumber = liveModels.Count + 1;
            GameObject newInstance = Instantiate(sourceGO, spawnPos, spawnRot);
            newInstance.name = $"{displayName ?? "Component"}_{cloneNumber}";
            // Position/rotation already set by Instantiate above — do not re-assign to avoid double-offset.

            // Configure MCB instance if loaded from raw asset
            if (displayName != null && (displayName.Contains("MCB") || displayName.Contains("Breaker") || newInstance.name.ToLowerInvariant().Contains("mcb")))
            {
                ModelAssetLoader.ConfigureMCBInstance(newInstance);
            }
            // Configure Contactor instance if loaded from raw asset
            else if (displayName != null && (displayName.Contains("Contactor") || displayName.Contains("contactor") || newInstance.name.ToLowerInvariant().Contains("contactor")))
            {
                ModelAssetLoader.ConfigureContactorInstance(newInstance);
            }

            // Ensure the model has ARModelManipulator for repositioning
            ARModelManipulator manip = newInstance.GetComponent<ARModelManipulator>();
            if (manip == null) manip = newInstance.AddComponent<ARModelManipulator>();
            manip.ReleaseToFreeMovable();
            manip.AnchorAtCurrentPose(); // Register native anchor at spawn pose
            manip.RefreshScaleBounds(); // Recalibrate pinch-to-scale after scale was set by Configure*()

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

        public void UnregisterModel(GameObject modelGO)
        {
            if (modelGO == null) return;
            for (int i = liveModels.Count - 1; i >= 0; i--)
            {
                if (liveModels[i] == null || liveModels[i].GameObject == modelGO || liveModels[i].GameObject == null)
                {
                    liveModels.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Permanently removes a model instance, cleans up its attached wires and labels,
        /// and records the action with UndoService.
        /// </summary>
        public void DeleteModel(ARModelManipulator manipulator)
        {
            if (manipulator == null) return;
            GameObject go = manipulator.gameObject;

            // 1. Clean up and remove all wires attached to any of this model's terminals
            WireSpawner wireSpawner = FindFirstObjectByType<WireSpawner>();
            ContactorAR.Wiring.TerminalPoint[] terminals = go.GetComponentsInChildren<ContactorAR.Wiring.TerminalPoint>(true);
            for (int i = 0; i < terminals.Length; i++)
            {
                var term = terminals[i];
                if (term != null && term.ConnectedEnd != null)
                {
                    CylindricalWire wire = term.ConnectedEnd.ParentWire;
                    if (wire != null)
                    {
                        if (wireSpawner != null) wireSpawner.RemoveWire(wire);
                        else Destroy(wire.gameObject);
                    }
                }
            }

            // 2. Remove floating labels
            ContactorAR.UI.TerminalWorldLabel[] labels = go.GetComponentsInChildren<ContactorAR.UI.TerminalWorldLabel>(true);
            for (int i = 0; i < labels.Length; i++)
            {
                if (labels[i] != null) Destroy(labels[i].gameObject);
            }

            // 3. Unregister
            UnregisterModel(go);

            // 4. Clear selection if this model was selected
            if (ARModelManipulator.Selected == manipulator)
            {
                ARModelManipulator.Select(null);
            }

            // 5. Record undo and deactivate/destroy
            ContactorAR.UndoSystem.UndoService.RecordAction(new ContactorAR.UndoSystem.DeleteObjectUndoAction(go));
            go.SetActive(false);
            Destroy(go, 6.0f); // Destroy after 6s (safe after 5s undo window)
        }

        private Vector3 GetSpawnPosition(GameObject sourceGO = null)
        {
            if (mainCam == null) mainCam = Camera.main;

            // Priority 1: If we already have models live in the scene, place the new one side-by-side to the right (9cm DIN rail spacing)
            if (liveModels.Count > 0 && liveModels[liveModels.Count - 1].GameObject != null)
            {
                Transform refTrans = liveModels[liveModels.Count - 1].GameObject.transform;
                Vector3 toRef = refTrans.position - (mainCam != null ? mainCam.transform.position : Vector3.zero);
                if (mainCam != null && Vector3.Dot(mainCam.transform.forward, toRef.normalized) > 0.1f && toRef.magnitude < 3f)
                {
                    Vector3 rightDir = mainCam.transform.right;
                    return refTrans.position + rightDir * 0.09f;
                }
            }

            // Priority 2: Try forward raycast against detected AR surfaces (table, desk, or wall/door)
            if (mainCam != null)
            {
                UnityEngine.XR.ARFoundation.ARRaycastManager rayMgr = FindAnyObjectByType<UnityEngine.XR.ARFoundation.ARRaycastManager>();
                if (rayMgr != null)
                {
                    Ray fwdRay = mainCam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
                    List<UnityEngine.XR.ARFoundation.ARRaycastHit> hits = new List<UnityEngine.XR.ARFoundation.ARRaycastHit>();
                    if (rayMgr.Raycast(fwdRay, hits, UnityEngine.XR.ARSubsystems.TrackableType.PlaneWithinPolygon | UnityEngine.XR.ARSubsystems.TrackableType.PlaneWithinBounds) && hits.Count > 0)
                    {
                        return hits[0].pose.position;
                    }
                }

                // Priority 3: Comfortable eye-level distance in front of camera
                Vector3 camFwd = mainCam.transform.forward;
                camFwd.y = 0;
                if (camFwd.sqrMagnitude < 0.01f) camFwd = mainCam.transform.forward;
                camFwd.Normalize();
                return mainCam.transform.position + camFwd * 0.70f;
            }

            return new Vector3(0f, 0f, 0.70f);
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
