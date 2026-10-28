using System;
using UnityEngine;
using ContactorAR.Breaker;
using ContactorAR.Wiring;
using ContactorAR.UI;
using ContactorAR.AR;
using ContactorAR.Simulation;
using WireSystem;

namespace ContactorAR.Breaker
{
    /// <summary>
    /// Central asset loader and configurator that loads 3D electrical models from Assets
    /// into the scene at runtime and in the Editor.
    /// Handles:
    /// - 3_pole_mcb_final (new 4-pole/3-pole MCB breaker)
    /// - 3_pole_contactor_final (contactor)
    /// - 3_phase_Motor_final (motor)
    ///
    /// Configures all 8 terminals (term_in_1..4, term_out_1..4),
    /// mcb_pivot (-160° ON / -70° OFF on X axis), AR manipulator, colliders, and UI labels.
    /// </summary>
    public static class ModelAssetLoader
    {
        public const string MCB_MODEL_NAME = "3_pole_mcb_final";
        public const string CONTACTOR_MODEL_NAME = "3_pole_contactor_final";
        public const string MOTOR_MODEL_NAME = "3_phase_Motor_final";

        public const float MCB_ON_ANGLE = -160.0f;
        public const float MCB_OFF_ANGLE = -70.0f;
        public const float MCB_TRIP_ANGLE = -115.0f;

        public const float MCB_DEFAULT_SCALE = 0.008f;       // Scaled 125x down so 7.3m FBX becomes 5.8cm x 6.6cm x 6.1cm
        public const float CONTACTOR_DEFAULT_SCALE = 0.8f;   // 0.8 * 4.5cm = 3.6cm x 6.8cm x 5.9cm (matches MCB panel height)
        public const float SPAWN_DISTANCE = 0.50f;          // 0.50m (~1.64 feet) away from camera (within requested 1-2 feet)

        /// <summary>
        /// Loads the MCB model GameObject from Assets (Resources or AssetDatabase in Editor).
        /// </summary>
        public static GameObject LoadMCBModel()
        {
            return LoadModelFromAssets(MCB_MODEL_NAME);
        }

        /// <summary>
        /// Loads the Contactor model GameObject from Assets.
        /// </summary>
        public static GameObject LoadContactorModel()
        {
            return LoadModelFromAssets(CONTACTOR_MODEL_NAME);
        }

        /// <summary>
        /// Loads the Motor model GameObject from Assets.
        /// </summary>
        public static GameObject LoadMotorModel()
        {
            return LoadModelFromAssets(MOTOR_MODEL_NAME);
        }

        /// <summary>
        /// Loads a GameObject asset by name from Assets/Resources or AssetDatabase.
        /// </summary>
        public static GameObject LoadModelFromAssets(string modelName)
        {
            if (string.IsNullOrEmpty(modelName)) return null;

            // 1. Try Resources.Load (supported at runtime on all platforms including Android)
            GameObject go = Resources.Load<GameObject>("Models/" + modelName);
            if (go != null) return go;

            go = Resources.Load<GameObject>(modelName);
            if (go != null) return go;

            // Try generic names
            if (modelName.Contains("mcb") || modelName.Contains("breaker"))
            {
                go = Resources.Load<GameObject>("Models/MCB") ?? Resources.Load<GameObject>("MCB");
                if (go != null) return go;
            }
            else if (modelName.Contains("contactor"))
            {
                go = Resources.Load<GameObject>("Models/Contactor") ?? Resources.Load<GameObject>("Contactor");
                if (go != null) return go;
            }

#if UNITY_EDITOR
            // 2. Editor AssetDatabase fallback (loads directly from Assets folder)
            string[] searchPaths = new string[]
            {
                $"Assets/{modelName}.fbx",
                $"Assets/Models/{modelName}.fbx",
                $"Assets/Resources/Models/{modelName}.prefab",
                $"Assets/Resources/{modelName}.prefab",
                $"Assets/{modelName}.prefab"
            };

            foreach (string p in searchPaths)
            {
                go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(p);
                if (go != null) return go;
            }

            // Fallback: search by filter in AssetDatabase
            string[] guids = UnityEditor.AssetDatabase.FindAssets($"{modelName} t:GameObject");
            if (guids != null && guids.Length > 0)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go != null) return go;
            }
#endif

            return null;
        }

        /// <summary>
        /// Instantiates the new MCB model (3_pole_mcb_final) from Assets into the scene,
        /// configures all 8 terminals and mcb_pivot (-160 up on / -70 off down), and returns the instance.
        /// </summary>
        public static GameObject InstantiateMCB(Vector3 worldPosition, Quaternion worldRotation, Transform parent = null)
        {
            GameObject prefab = LoadMCBModel();
            if (prefab == null)
            {
                Debug.LogWarning($"[ModelAssetLoader] Could not load MCB model '{MCB_MODEL_NAME}' from Assets!");
                return null;
            }

            GameObject instance = UnityEngine.Object.Instantiate(prefab, worldPosition, worldRotation, parent);
            instance.name = MCB_MODEL_NAME;
            ConfigureMCBInstance(instance);
            return instance;
        }

        /// <summary>
        /// Ensures an MCB model is present in the scene. If none is found, loads and instantiates
        /// 3_pole_mcb_final from Assets.
        /// </summary>
        public static GameObject EnsureMCBInScene(Vector3? spawnPosition = null, Quaternion? spawnRotation = null)
        {
            // First check if already in scene
            ContactorController existing = UnityEngine.Object.FindAnyObjectByType<ContactorController>();
            if (existing != null)
            {
                ConfigureMCBInstance(existing.gameObject);
                return existing.gameObject;
            }

            // Check by name
            GameObject[] all = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            foreach (GameObject g in all)
            {
                if (g.name.Contains("mcb") || g.name.Contains("Breaker") || g.name.Contains("3_pole_mcb"))
                {
                    ConfigureMCBInstance(g);
                    return g;
                }
            }

            // Not found in scene: load from Assets
            Camera cam = Camera.main;
            Vector3 pos = spawnPosition ?? (cam != null ? cam.transform.position + cam.transform.forward * SPAWN_DISTANCE : new Vector3(0, 0, SPAWN_DISTANCE));
            Quaternion rot = spawnRotation ?? (cam != null ? Quaternion.LookRotation(-cam.transform.forward, Vector3.up) : Quaternion.Euler(0, 180, 0));

            return InstantiateMCB(pos, rot);
        }

        /// <summary>
        /// Automatically configures an MCB model instance with all required components:
        /// - Sets compact AR model scale (MCB_DEFAULT_SCALE = 0.35f)
        /// - ContactorController & 4 poles
        /// - MCBPivotLever on mcb_pivot (-160° ON / -70° OFF on X axis)
        /// - 8 Terminals: term_in_1..4 (Up) and term_out_1..4 (Down)
        /// - TerminalPoint, TerminalPort, SphereCollider, TerminalWorldLabel on each terminal
        /// - ARModelManipulator
        /// - ElectricalEffectsManager
        /// - BoxCollider on root & pivot
        /// </summary>
        public static void ConfigureMCBInstance(GameObject instance, bool applyDefaultScale = true)
        {
            if (instance == null) return;

            // 0. Set compact AR scale (only when explicitly requested, e.g. on first instantiation)
            if (applyDefaultScale)
                instance.transform.localScale = Vector3.one * MCB_DEFAULT_SCALE;

            // 1. ContactorController
            ContactorController contactor = instance.GetComponent<ContactorController>();
            if (contactor == null) contactor = instance.AddComponent<ContactorController>();

            // 2. Locate mcb_pivot
            Transform pivotTrans = FindDeepChild(instance.transform, "mcb_pivot");
            if (pivotTrans != null)
            {
                BoxCollider pivotCol = pivotTrans.GetComponent<BoxCollider>();
                if (pivotCol == null)
                {
                    pivotCol = pivotTrans.gameObject.AddComponent<BoxCollider>();
                    pivotCol.size = new Vector3(0.12f, 0.08f, 0.1f);
                }

                MCBPivotLever lever = instance.GetComponent<MCBPivotLever>();
                if (lever == null) lever = instance.AddComponent<MCBPivotLever>();

                lever.Configure(pivotTrans, MCB_ON_ANGLE, MCB_OFF_ANGLE, MCB_TRIP_ANGLE, RotationAxis.X);
            }

            // 3. Ensure BoxCollider on root for AR gestures/raycasting
            BoxCollider rootCol = instance.GetComponent<BoxCollider>();
            if (rootCol == null)
            {
                rootCol = instance.AddComponent<BoxCollider>();
                Renderer[] rends = instance.GetComponentsInChildren<Renderer>();
                if (rends.Length > 0)
                {
                    Bounds bounds = rends[0].bounds;
                    for (int i = 1; i < rends.Length; i++) bounds.Encapsulate(rends[i].bounds);
                    rootCol.center = instance.transform.InverseTransformPoint(bounds.center);
                    Vector3 lossy = instance.transform.lossyScale;
                    rootCol.size = new Vector3(
                        Mathf.Abs(bounds.size.x / Mathf.Max(0.0001f, Mathf.Abs(lossy.x))),
                        Mathf.Abs(bounds.size.y / Mathf.Max(0.0001f, Mathf.Abs(lossy.y))),
                        Mathf.Abs(bounds.size.z / Mathf.Max(0.0001f, Mathf.Abs(lossy.z)))
                    );
                }
            }

            // 4. ARModelManipulator
            ARModelManipulator manip = instance.GetComponent<ARModelManipulator>();
            if (manip == null) manip = instance.AddComponent<ARModelManipulator>();

            // 5. ElectricalEffectsManager
            ElectricalEffectsManager effects = instance.GetComponent<ElectricalEffectsManager>();
            if (effects == null) effects = instance.AddComponent<ElectricalEffectsManager>();

            // 6. Setup 8 Terminals (term_in_1..4 and term_out_1..4)
            string[] inTerms = { "term_in_1", "term_in_2", "term_in_3", "term_in_4" };
            string[] outTerms = { "term_out_1", "term_out_2", "term_out_3", "term_out_4" };
            string[] phases = { "L1", "L2", "L3", "N" };

            for (int i = 0; i < 4; i++)
            {
                ConfigureTerminal(instance.transform, inTerms[i], i, ContactorAR.Wiring.TerminalType.Input, $"IN {i + 1} ({phases[i]})", contactor, ExitDirection.Up);
                ConfigureTerminal(instance.transform, outTerms[i], i, ContactorAR.Wiring.TerminalType.Output, $"OUT {i + 1} ({phases[i]})", contactor, ExitDirection.Down);
            }

            contactor.AutoDiscoverTerminalsIfEmpty();
        }

        private static void ConfigureTerminal(Transform root, string childName, int index, ContactorAR.Wiring.TerminalType type, string label, ContactorController contactor, ExitDirection exitDir)
        {
            Transform termTrans = FindDeepChild(root, childName);
            if (termTrans == null) return;

            // SphereCollider for touch/wire snapping
            SphereCollider sc = termTrans.GetComponent<SphereCollider>();
            if (sc == null)
            {
                sc = termTrans.gameObject.AddComponent<SphereCollider>();
                sc.radius = 0.04f;
                sc.isTrigger = true;
            }

            // TerminalPoint for circuit simulator
            TerminalPoint tp = termTrans.GetComponent<TerminalPoint>();
            if (tp == null) tp = termTrans.gameObject.AddComponent<TerminalPoint>();
            tp.Initialize(contactor, index, type, label);

            // TerminalPort for orthogonal wire system
            TerminalPort port = termTrans.GetComponent<TerminalPort>();
            if (port == null) port = termTrans.gameObject.AddComponent<TerminalPort>();
            port.terminalId = type == ContactorAR.Wiring.TerminalType.Input ? $"IN_{index + 1}" : $"OUT_{index + 1}";
            port.maxWires = 1;
            port.LocalExitDirection = exitDir;

            // Floating AR World Label
            Transform labelChild = termTrans.Find("TerminalLabel");
            if (labelChild == null)
            {
                GameObject labelObj = new GameObject("TerminalLabel");
                labelObj.transform.SetParent(termTrans, false);
                TerminalWorldLabel worldLabel = labelObj.AddComponent<TerminalWorldLabel>();
                worldLabel.Setup(tp);
            }
        }

        /// <summary>
        /// Instantiates the 3-pole contactor from Assets, configures all 12 terminals,
        /// the CJX2S-090018 armature plunger, and returns the instance.
        /// </summary>
        public static GameObject InstantiateContactor(Vector3 worldPosition, Quaternion worldRotation, Transform parent = null)
        {
            GameObject prefab = LoadContactorModel();
            if (prefab == null)
            {
                Debug.LogWarning($"[ModelAssetLoader] Could not load Contactor model '{CONTACTOR_MODEL_NAME}' from Assets!");
                return null;
            }

            GameObject instance = UnityEngine.Object.Instantiate(prefab, worldPosition, worldRotation, parent);
            instance.name = CONTACTOR_MODEL_NAME;
            ConfigureContactorInstance(instance);
            return instance;
        }

        /// <summary>
        /// Fully configures a 3_pole_contactor_final instance:
        /// - ContactorArmatureController (moves CJX2S-090018: ON=y0.0126 / OFF=y0.0069)
        /// - ContactorController (for circuit simulator integration)
        /// - All 12 terminals wired up with TerminalPoint, TerminalPort, SphereCollider, TerminalWorldLabel
        /// - ARModelManipulator, ElectricalEffectsManager, BoxCollider on root
        /// </summary>
        public static void ConfigureContactorInstance(GameObject instance, bool applyDefaultScale = true)
        {
            if (instance == null) return;

            // 0. Set compact AR scale (only when explicitly requested)
            if (applyDefaultScale)
                instance.transform.localScale = Vector3.one * CONTACTOR_DEFAULT_SCALE;

            // 1. ContactorController (circuit sim backbone)
            ContactorController contactor = instance.GetComponent<ContactorController>();
            if (contactor == null) contactor = instance.AddComponent<ContactorController>();

            // 2. ContactorArmatureController (plunger physics + contact conductivity)
            ContactorArmatureController armCtrl = instance.GetComponent<ContactorArmatureController>();
            if (armCtrl == null) armCtrl = instance.AddComponent<ContactorArmatureController>();

            // Find and wire the armature transform
            Transform armatureTrans = FindDeepChild(instance.transform, ContactorArmatureController.ARMATURE_NODE_NAME);
            if (armatureTrans != null)
                armCtrl.Configure(armatureTrans, ContactorArmatureController.RELEASED_Y, ContactorArmatureController.ENERGIZED_Y);

            // 3. Root BoxCollider for AR gestures / raycasting
            BoxCollider rootCol = instance.GetComponent<BoxCollider>();
            if (rootCol == null)
            {
                rootCol = instance.AddComponent<BoxCollider>();
                Renderer[] rends = instance.GetComponentsInChildren<Renderer>();
                if (rends.Length > 0)
                {
                    Bounds b = rends[0].bounds;
                    for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                    rootCol.center = instance.transform.InverseTransformPoint(b.center);
                    Vector3 lossy = instance.transform.lossyScale;
                    rootCol.size = new Vector3(
                        Mathf.Abs(b.size.x / Mathf.Max(0.0001f, Mathf.Abs(lossy.x))),
                        Mathf.Abs(b.size.y / Mathf.Max(0.0001f, Mathf.Abs(lossy.y))),
                        Mathf.Abs(b.size.z / Mathf.Max(0.0001f, Mathf.Abs(lossy.z)))
                    );
                }
            }

            // 4. ARModelManipulator
            ARModelManipulator manip = instance.GetComponent<ARModelManipulator>();
            if (manip == null) manip = instance.AddComponent<ARModelManipulator>();

            // 5. ElectricalEffectsManager
            ElectricalEffectsManager effects = instance.GetComponent<ElectricalEffectsManager>();
            if (effects == null) effects = instance.AddComponent<ElectricalEffectsManager>();

            // 6. All 12 terminals
            // Main power in/out (3 poles)
            ConfigureTerminal(instance.transform, "term_in_1",  0, ContactorAR.Wiring.TerminalType.Input,  "IN L1",   contactor, ExitDirection.Up,   "IN_1");
            ConfigureTerminal(instance.transform, "term_in_2",  1, ContactorAR.Wiring.TerminalType.Input,  "IN L2",   contactor, ExitDirection.Up,   "IN_2");
            ConfigureTerminal(instance.transform, "term_in_3",  2, ContactorAR.Wiring.TerminalType.Input,  "IN L3",   contactor, ExitDirection.Up,   "IN_3");
            ConfigureTerminal(instance.transform, "term_out_1", 0, ContactorAR.Wiring.TerminalType.Output, "OUT L1",  contactor, ExitDirection.Down, "OUT_1");
            ConfigureTerminal(instance.transform, "term_out_2", 1, ContactorAR.Wiring.TerminalType.Output, "OUT L2",  contactor, ExitDirection.Down, "OUT_2");
            ConfigureTerminal(instance.transform, "term_out_3", 2, ContactorAR.Wiring.TerminalType.Output, "OUT L3",  contactor, ExitDirection.Down, "OUT_3");

            // Coil supply A1 / A2
            ConfigureTerminal(instance.transform, "term_in_A1", 3, ContactorAR.Wiring.TerminalType.Input,  "COIL A1", contactor, ExitDirection.Up,   "COIL_A1");
            ConfigureTerminal(instance.transform, "term_in_A2", 4, ContactorAR.Wiring.TerminalType.Input,  "COIL A2", contactor, ExitDirection.Down, "COIL_A2");

            // Auxiliary NO (13-14): closes when coil energized
            ConfigureTerminal(instance.transform, "term_no_u",  5, ContactorAR.Wiring.TerminalType.Input,  "NO 13",   contactor, ExitDirection.Up,   "NO_U");
            ConfigureTerminal(instance.transform, "term_no_d",  5, ContactorAR.Wiring.TerminalType.Output, "NO 14",   contactor, ExitDirection.Down, "NO_D");

            // Auxiliary NC (21-22): opens when coil energized
            ConfigureTerminal(instance.transform, "term_nc_u",  6, ContactorAR.Wiring.TerminalType.Input,  "NC 21",   contactor, ExitDirection.Up,   "NC_U");
            ConfigureTerminal(instance.transform, "term_nc_d",  6, ContactorAR.Wiring.TerminalType.Output, "NC 22",   contactor, ExitDirection.Down, "NC_D");

            // 7. Let armature controller discover all TerminalPoint components
            armCtrl.DiscoverContactorTerminals();

            contactor.AutoDiscoverTerminalsIfEmpty();
        }

        private static void ConfigureTerminal(Transform root, string childName, int index,
            ContactorAR.Wiring.TerminalType type, string label,
            ContactorController contactor, ExitDirection exitDir, string portId)
        {
            Transform termTrans = FindDeepChild(root, childName);
            if (termTrans == null)
            {
                Debug.LogWarning($"[ModelAssetLoader] Terminal '{childName}' not found under '{root.name}'.");
                return;
            }

            // SphereCollider for touch / wire snapping
            SphereCollider sc = termTrans.GetComponent<SphereCollider>();
            if (sc == null)
            {
                sc = termTrans.gameObject.AddComponent<SphereCollider>();
                sc.radius    = 0.04f;
                sc.isTrigger = true;
            }

            // TerminalPoint (electrical sim node)
            TerminalPoint tp = termTrans.GetComponent<TerminalPoint>();
            if (tp == null) tp = termTrans.gameObject.AddComponent<TerminalPoint>();
            tp.Initialize(contactor, index, type, label);

            // TerminalPort (orthogonal wire router)
            TerminalPort port = termTrans.GetComponent<TerminalPort>();
            if (port == null) port = termTrans.gameObject.AddComponent<TerminalPort>();
            port.terminalId      = portId;
            port.maxWires        = 1;
            port.LocalExitDirection = exitDir;

            // Floating AR world label
            if (termTrans.Find("TerminalLabel") == null)
            {
                GameObject labelObj = new GameObject("TerminalLabel");
                labelObj.transform.SetParent(termTrans, false);
                TerminalWorldLabel wl = labelObj.AddComponent<TerminalWorldLabel>();
                wl.Setup(tp);
            }
        }

        public static Transform FindDeepChild(Transform parent, string childName)
        {
            if (parent == null || string.IsNullOrEmpty(childName)) return null;

            foreach (Transform child in parent)
            {
                if (child.name.Equals(childName, StringComparison.OrdinalIgnoreCase))
                    return child;
                Transform found = FindDeepChild(child, childName);
                if (found != null) return found;
            }
            return null;
        }
    }
}
