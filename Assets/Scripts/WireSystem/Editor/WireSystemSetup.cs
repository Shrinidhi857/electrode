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
            GameObject breaker = GameObject.Find("Four-Pole-Breaker-final");
            if (breaker == null)
            {
                var candidates = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None);
                foreach (var c in candidates)
                {
                    if (c.name.Contains("Breaker") || c.name.Contains("Contactor"))
                    {
                        breaker = c.gameObject;
                        break;
                    }
                }
            }

            if (breaker == null)
            {
                EditorUtility.DisplayDialog("Setup TerminalPorts",
                    "Could not find 'Four-Pole-Breaker-final' in the active scene.\nPlease open SampleScene.", "OK");
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
