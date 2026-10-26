#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using ContactorAR.Breaker;
using ContactorAR.Wiring;
using ContactorAR.Simulation;
using ContactorAR.AR;
using ContactorAR.UI;

namespace ContactorAR.Editor
{
    /// <summary>
    /// Editor automation tool that automatically configures all colliders,
    /// pivot lever, terminals, power simulator, AR manipulator, and UI in the scene with 1 click.
    /// Accessible via: Tools -> Breaker & Contactor AR -> Setup Scene Automatically
    /// </summary>
    public static class ContactorSceneSetupTool
    {
        [MenuItem("Tools/Breaker & Contactor AR/Setup Scene Automatically", false, 1)]
        public static void SetupSceneAutomatically()
        {
            // 0. Disable default AR Template (UI canvas & Object Spawner) to stop tap interference
            DisableDefaultARTemplateInternal();

            // 1. Locate the Breaker object in the scene
            GameObject breakerObj = GameObject.Find("Four-Pole-Breaker-final");
            if (breakerObj == null)
            {
                // Try finding any object with 'breaker' or 'mcb' in its name
                GameObject[] all = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
                foreach (GameObject g in all)
                {
                    if (g.name.ToLower().Contains("breaker") || g.name.ToLower().Contains("contactor"))
                    {
                        breakerObj = g;
                        break;
                    }
                }
            }

            if (breakerObj == null)
            {
                EditorUtility.DisplayDialog("Breaker Not Found", 
                    "Could not find 'Four-Pole-Breaker-final' in the current scene. Please select it in the Hierarchy and run again.", "OK");
                return;
            }

            Undo.RegisterFullObjectHierarchyUndo(breakerObj, "Setup Contactor AR");

            // 2. Add or configure ContactorController
            ContactorController contactor = breakerObj.GetComponent<ContactorController>();
            if (contactor == null) contactor = breakerObj.AddComponent<ContactorController>();

            // 3. Find and configure mcb_pivot & lever
            Transform pivotTrans = FindDeepChild(breakerObj.transform, "mcb_pivot");
            if (pivotTrans != null)
            {
                // Ensure collider on pivot so user can touch/drag it directly
                BoxCollider pivotCol = pivotTrans.GetComponent<BoxCollider>();
                if (pivotCol == null)
                {
                    pivotCol = pivotTrans.gameObject.AddComponent<BoxCollider>();
                    pivotCol.size = new Vector3(0.12f, 0.08f, 0.1f);
                }

                MCBPivotLever lever = breakerObj.GetComponent<MCBPivotLever>();
                if (lever == null) lever = breakerObj.AddComponent<MCBPivotLever>();

                // Configure exact angles: -1.4 deg to 68.0 deg
                SerializedObject so = new SerializedObject(lever);
                so.FindProperty("pivotTransform").objectReferenceValue = pivotTrans;
                so.FindProperty("offAngle").floatValue = -1.4f;
                so.FindProperty("onAngle").floatValue = 68.0f;
                so.FindProperty("tripAngle").floatValue = 33.3f;
                so.FindProperty("rotationAxis").enumValueIndex = 0; // X axis
                so.ApplyModifiedProperties();
            }

            // 4. Ensure BoxCollider on Breaker Root for AR move/pinch interaction
            BoxCollider rootCol = breakerObj.GetComponent<BoxCollider>();
            if (rootCol == null)
            {
                rootCol = breakerObj.AddComponent<BoxCollider>();
                Bounds bounds = new Bounds(breakerObj.transform.position, Vector3.zero);
                Renderer[] rends = breakerObj.GetComponentsInChildren<Renderer>();
                if (rends.Length > 0)
                {
                    bounds = rends[0].bounds;
                    for (int i = 1; i < rends.Length; i++) bounds.Encapsulate(rends[i].bounds);
                    rootCol.center = breakerObj.transform.InverseTransformPoint(bounds.center);
                    rootCol.size = breakerObj.transform.InverseTransformVector(bounds.size);
                }
            }

            // 5. Add ARModelManipulator
            ARModelManipulator manipulator = breakerObj.GetComponent<ARModelManipulator>();
            if (manipulator == null) manipulator = breakerObj.AddComponent<ARModelManipulator>();

            // 6. Setup 8 Terminals and floating AR World Labels
            string[] inTerms = { "term_in_1", "term_in_2", "term_in_3", "term_in_4" };
            string[] outTerms = { "term_out_1", "term_out_2", "term_out_3", "term_out_4" };
            string[] phases = { "L1", "L2", "L3", "N" };

            for (int i = 0; i < 4; i++)
            {
                SetupTerminal(breakerObj.transform, inTerms[i], i, TerminalType.Input, $"IN {i + 1} ({phases[i]})", contactor);
                SetupTerminal(breakerObj.transform, outTerms[i], i, TerminalType.Output, $"OUT {i + 1} ({phases[i]})", contactor);
            }

            contactor.AutoDiscoverTerminalsIfEmpty();

            // 7. Add Circuit Simulator Manager
            GameObject simObj = GameObject.Find("CircuitSimulator");
            if (simObj == null)
            {
                simObj = new GameObject("CircuitSimulator");
                Undo.RegisterCreatedObjectUndo(simObj, "Create Circuit Simulator");
            }
            PowerCircuitSimulator sim = simObj.GetComponent<PowerCircuitSimulator>();
            if (sim == null) sim = simObj.AddComponent<PowerCircuitSimulator>();

            // 8. Add Wire Spawner
            WireSpawner spawner = simObj.GetComponent<WireSpawner>();
            if (spawner == null) spawner = simObj.AddComponent<WireSpawner>();

            // 9. Add AR UI Canvas
            GameObject uiObj = GameObject.Find("ContactorAR_Canvas");
            if (uiObj == null)
            {
                uiObj = new GameObject("ContactorAR_Canvas");
                Undo.RegisterCreatedObjectUndo(uiObj, "Create AR UI Canvas");
                ARContactorUI ui = uiObj.AddComponent<ARContactorUI>();
            }

            // 10. Mark scene dirty to save changes
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene()
            );

            Selection.activeGameObject = breakerObj;

            EditorUtility.DisplayDialog("Setup Successful!",
                "4-Pole Breaker / Contactor AR experience successfully configured!\n\n" +
                "Features Ready:\n" +
                "• Pivot Lever (-1.4° OFF to 68° ON / TRIP)\n" +
                "• AR Pinch-to-Scale, 1-Finger Drag, 2-Finger Twist\n" +
                "• Coordinate Hanging / Pinning Mode\n" +
                "• 8 Snap-Enabled Terminals (term_in_1..4, term_out_1..4)\n" +
                "• Floating AR World Labels with Live Voltage\n" +
                "• Cylindrical 3D Wires with Spherical Snap Connectors\n" +
                "• 4-Pole Circuit Power Simulation & Overload Trip\n" +
                "• Full On-Screen AR HUD Controls", "Awesome!");
        }

        private static void SetupTerminal(Transform root, string childName, int index, TerminalType type, string label, ContactorController contactor)
        {
            Transform termTrans = FindDeepChild(root, childName);
            if (termTrans == null) return;

            SphereCollider sc = termTrans.GetComponent<SphereCollider>();
            if (sc == null)
            {
                sc = termTrans.gameObject.AddComponent<SphereCollider>();
                sc.radius = 0.04f;
                sc.isTrigger = true;
            }

            TerminalPoint tp = termTrans.GetComponent<TerminalPoint>();
            if (tp == null) tp = termTrans.gameObject.AddComponent<TerminalPoint>();
            tp.Initialize(contactor, index, type, label);

            // Add AR Floating Label
            Transform labelChild = termTrans.Find("TerminalLabel");
            if (labelChild == null)
            {
                GameObject labelObj = new GameObject("TerminalLabel");
                labelObj.transform.SetParent(termTrans, false);
                TerminalWorldLabel worldLabel = labelObj.AddComponent<TerminalWorldLabel>();
                worldLabel.Setup(tp);
            }
        }

        [MenuItem("Tools/Breaker & Contactor AR/Disable Default AR Template (UI & Spawner)", false, 2)]
        public static void DisableDefaultARTemplateMenu()
        {
            int count = DisableDefaultARTemplateInternal();
            EditorUtility.DisplayDialog("Template Deactivated", 
                $"Disabled {count} default AR Template object(s) (UI Canvas, Coaching UI, Object Spawner).\n\n" +
                "The template will no longer intercept screen taps or spawn cubes/cylinders.", "OK");
        }

        [MenuItem("Tools/Breaker & Contactor AR/Delete Template Overlay (Cube, Pyramid, Spawner)", false, 3)]
        public static void DeleteDefaultARTemplateMenu()
        {
            string[] names = { "UI", "Object Spawner" };
            int count = 0;
            foreach (string n in names)
            {
                GameObject g = GameObject.Find(n);
                if (g != null)
                {
                    if (n == "UI")
                    {
                        if (g.transform.Find("Object Menu") == null && 
                            g.transform.Find("Coaching UI") == null &&
                            g.transform.Find("Greeting Prompt") == null)
                        {
                            continue;
                        }
                    }
                    Undo.DestroyObjectImmediate(g);
                    count++;
                    Debug.Log($"[ContactorAR Editor] Deleted template GameObject: {n}");
                }
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene()
            );

            EditorUtility.DisplayDialog("Template Removed", 
                $"Successfully removed {count} template object(s) from the scene.\n\n" +
                "The Cube/Pyramid/Arch overlay and Object Spawner are completely gone.", "OK");
        }

        private static int DisableDefaultARTemplateInternal()
        {
            int count = 0;
            string[] namesToDisable = { "Object Spawner", "UI" };
            foreach (string n in namesToDisable)
            {
                GameObject g = GameObject.Find(n);
                if (g != null)
                {
                    if (n == "UI")
                    {
                        // Verify it's template UI
                        if (g.transform.Find("Object Menu") == null && 
                            g.transform.Find("Coaching UI") == null && 
                            g.transform.Find("Greeting Prompt") == null)
                        {
                            continue;
                        }
                    }

                    Undo.RecordObject(g, "Disable Template Object");
                    g.SetActive(false);
                    count++;
                    Debug.Log($"[ContactorAR Editor] Deactivated template GameObject: {n}");
                }
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene()
            );

            return count;
        }

        private static Transform FindDeepChild(Transform parent, string childName)
        {
            foreach (Transform child in parent)
            {
                if (child.name.Equals(childName, System.StringComparison.OrdinalIgnoreCase))
                    return child;
                Transform found = FindDeepChild(child, childName);
                if (found != null) return found;
            }
            return null;
        }
    }
}
#endif
