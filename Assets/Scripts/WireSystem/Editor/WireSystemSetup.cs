#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WireSystem.Editor
{
    /// <summary>
    /// Editor automation utility for the Lucidchart-style Wire System.
    /// Accessible via Tools > Wire System in the Unity Editor menu.
    /// </summary>
    public static class WireSystemSetup
    {
        [MenuItem("Tools/Wire System/1. Setup TerminalPorts on Breaker", false, 1)]
        public static void SetupTerminals()
        {
            GameObject breaker = GameObject.Find("3_pole_mcb_final");
            if (breaker == null)
            {
                var candidates = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None);
                foreach (var c in candidates)
                {
                    if (c.name.Contains("3_pole_mcb") || c.name.Contains("Breaker") || c.name.Contains("Contactor"))
                    {
                        breaker = c.gameObject;
                        break;
                    }
                }
            }

            // Fallback: load from Assets
            if (breaker == null)
            {
                GameObject assetObj = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/3_pole_mcb_final.fbx");
                if (assetObj != null)
                {
                    breaker = (GameObject)PrefabUtility.InstantiatePrefab(assetObj);
                    breaker.name = "3_pole_mcb_final";
                    Undo.RegisterCreatedObjectUndo(breaker, "Instantiate 3_pole_mcb_final");
                }
            }

            if (breaker == null)
            {
                EditorUtility.DisplayDialog("Setup TerminalPorts",
                    "Could not find '3_pole_mcb_final' in the active scene or Assets.\nPlease verify Assets/3_pole_mcb_final.fbx exists.", "OK");
                return;
            }

            int configuredCount = 0;

            // Configure input terminals (term_in_1..4) -> Up
            for (int i = 1; i <= 4; i++)
            {
                string termName = $"term_in_{i}";
                Transform t = FindDeepChild(breaker.transform, termName);
                if (t != null)
                {
                    ConfigureTerminalPort(t.gameObject, $"IN_{i}", ExitDirection.Up);
                    configuredCount++;
                }
                else
                {
                    Debug.LogWarning($"[WireSystemSetup] Could not locate {termName} under {breaker.name}");
                }
            }

            // Configure output terminals (term_out_1..4) -> Down
            for (int i = 1; i <= 4; i++)
            {
                string termName = $"term_out_{i}";
                Transform t = FindDeepChild(breaker.transform, termName);
                if (t != null)
                {
                    ConfigureTerminalPort(t.gameObject, $"OUT_{i}", ExitDirection.Down);
                    configuredCount++;
                }
                else
                {
                    Debug.LogWarning($"[WireSystemSetup] Could not locate {termName} under {breaker.name}");
                }
            }

            EditorUtility.SetDirty(breaker);
            Debug.Log($"[WireSystemSetup] Successfully configured {configuredCount} TerminalPorts on {breaker.name}!");
            EditorUtility.DisplayDialog("Setup Complete",
                $"Configured {configuredCount} TerminalPorts on '{breaker.name}'.\nTerminals set to maxWires=1 with Up/Down exit directions.", "OK");
        }

        [MenuItem("Tools/Wire System/1b. Setup TerminalPorts on Contactor", false, 2)]
        public static void SetupContactorTerminals()
        {
            GameObject contactor = GameObject.Find("3_pole_contactor_final");
            if (contactor == null)
            {
                var candidates = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None);
                foreach (var c in candidates)
                {
                    if (c.name.Contains("3_pole_contactor") || (c.name.Contains("Contactor") && !c.name.Contains("Canvas")))
                    {
                        contactor = c.gameObject;
                        break;
                    }
                }
            }

            if (contactor == null)
            {
                GameObject assetObj = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/3_pole_contactor_final.fbx");
                if (assetObj != null)
                {
                    contactor = (GameObject)PrefabUtility.InstantiatePrefab(assetObj);
                    contactor.name = "3_pole_contactor_final";
                    Undo.RegisterCreatedObjectUndo(contactor, "Instantiate 3_pole_contactor_final");
                }
            }

            if (contactor == null)
            {
                EditorUtility.DisplayDialog("Setup TerminalPorts",
                    "Could not find '3_pole_contactor_final' in the active scene or Assets.\nPlease verify Assets/3_pole_contactor_final.fbx exists.", "OK");
                return;
            }

            int configuredCount = 0;
            (string name, string id, ExitDirection dir)[] terms = {
                ("term_in_1",  "IN_1",    ExitDirection.Up),
                ("term_in_2",  "IN_2",    ExitDirection.Up),
                ("term_in_3",  "IN_3",    ExitDirection.Up),
                ("term_out_1", "OUT_1",   ExitDirection.Down),
                ("term_out_2", "OUT_2",   ExitDirection.Down),
                ("term_out_3", "OUT_3",   ExitDirection.Down),
                ("term_in_A1", "COIL_A1", ExitDirection.Up),
                ("term_in_A2", "COIL_A2", ExitDirection.Down),
                ("term_no_u",  "NO_U",    ExitDirection.Up),
                ("term_no_d",  "NO_D",    ExitDirection.Down),
                ("term_nc_u",  "NC_U",    ExitDirection.Up),
                ("term_nc_d",  "NC_D",    ExitDirection.Down)
            };

            foreach (var t in terms)
            {
                Transform tr = FindDeepChild(contactor.transform, t.name);
                if (tr != null)
                {
                    ConfigureTerminalPort(tr.gameObject, t.id, t.dir);
                    configuredCount++;
                }
                else
                {
                    Debug.LogWarning($"[WireSystemSetup] Could not locate {t.name} under {contactor.name}");
                }
            }

            EditorUtility.SetDirty(contactor);
            Debug.Log($"[WireSystemSetup] Successfully configured {configuredCount} TerminalPorts on {contactor.name}!");
            EditorUtility.DisplayDialog("Setup Complete",
                $"Configured {configuredCount} TerminalPorts on '{contactor.name}'.\nAll 12 terminals ready for orthogonal wiring.", "OK");
        }

        private static void ConfigureTerminalPort(GameObject go, string id, ExitDirection exitDir)
        {
            Undo.RecordObject(go, "Setup TerminalPort");

            TerminalPort port = go.GetComponent<TerminalPort>();
            if (port == null) port = Undo.AddComponent<TerminalPort>(go);

            port.terminalId = id;
            port.maxWires = 1;
            port.LocalExitDirection = exitDir;

            EditorUtility.SetDirty(port);
        }

        [MenuItem("Tools/Wire System/2. Create or Locate Wire Material", false, 2)]
        public static Material CreateOrLocateWireMaterial()
        {
            string resourcesDir = "Assets/Resources";
            string assetPath = "Assets/Resources/WireMaterial.mat";

            if (!Directory.Exists(resourcesDir))
            {
                Directory.CreateDirectory(resourcesDir);
                AssetDatabase.Refresh();
            }

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (mat == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                             ?? Shader.Find("Sprites/Default")
                             ?? Shader.Find("Unlit/Color");

                if (shader != null)
                {
                    mat = new Material(shader);
                    mat.color = new Color(0.92f, 0.22f, 0.22f, 1f);
                    AssetDatabase.CreateAsset(mat, assetPath);
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                    Debug.Log($"[WireSystemSetup] Created new WireMaterial at {assetPath}");
                }
            }

            return mat;
        }

        [MenuItem("Tools/Wire System/3. Setup Complete Wire System in Scene", false, 3)]
        public static void SetupCompleteWireSystem()
        {
            // 1. Setup terminals on breaker
            SetupTerminals();

            // 2. Material
            Material wireMat = CreateOrLocateWireMaterial();

            // 3. Ensure WireManager GameObject in scene
            GameObject managerGo = GameObject.Find("WireManager");
            if (managerGo == null)
            {
                managerGo = new GameObject("WireManager");
                Undo.RegisterCreatedObjectUndo(managerGo, "Create WireManager");
            }

            // Ensure WireManager bridge component
            WireManager manager = managerGo.GetComponent<WireManager>();
            if (manager == null) manager = Undo.AddComponent<WireManager>(managerGo);

            // Ensure WireDragController
            WireDragController dragCtrl = managerGo.GetComponent<WireDragController>();
            if (dragCtrl == null) dragCtrl = Undo.AddComponent<WireDragController>(managerGo);

            // Assign fields via SerializedObject
            SerializedObject so = new SerializedObject(dragCtrl);
            SerializedProperty matProp = so.FindProperty("wireMaterial");
            if (matProp != null && wireMat != null)
            {
                matProp.objectReferenceValue = wireMat;
            }

            Camera mainCam = Camera.main;
            SerializedProperty camProp = so.FindProperty("targetCamera");
            if (camProp != null && mainCam != null)
            {
                camProp.objectReferenceValue = mainCam;
            }
            so.ApplyModifiedProperties();

            // Ensure WireInputController compatibility stub is present
            WireInputController inputBridge = managerGo.GetComponent<WireInputController>();
            if (inputBridge == null) inputBridge = Undo.AddComponent<WireInputController>(managerGo);

            EditorUtility.SetDirty(managerGo);
            Selection.activeGameObject = managerGo;

            Debug.Log("[WireSystemSetup] Complete Wire System configured successfully in scene!");
            EditorUtility.DisplayDialog("Wire System Ready",
                "Lucidchart Wire System successfully configured in scene!\n\n" +
                "• 8 TerminalPorts configured on breaker\n" +
                "• WireMaterial created in Assets/Resources/WireMaterial.mat\n" +
                "• WireManager + WireDragController configured in hierarchy\n\n" +
                "Enter Play Mode and click/drag between terminals to test!", "Let's Go!");
        }

        [MenuItem("Tools/Wire System/Clear All Active Wires in Scene", false, 20)]
        public static void ClearAllWiresInScene()
        {
            Wire[] wires = Object.FindObjectsByType<Wire>(FindObjectsSortMode.None);
            foreach (var w in wires)
            {
                if (w != null) Object.DestroyImmediate(w.gameObject);
            }
            Debug.Log("[WireSystemSetup] All wires cleared from scene.");
        }

        private static Transform FindDeepChild(Transform parent, string childName)
        {
            if (parent.name == childName) return parent;

            for (int i = 0; i < parent.childCount; i++)
            {
                Transform found = FindDeepChild(parent.GetChild(i), childName);
                if (found != null) return found;
            }
            return null;
        }
    }
}
#endif
