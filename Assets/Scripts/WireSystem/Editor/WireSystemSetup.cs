#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WireSystem.Editor
{
    /// <summary>
    /// Editor automation utility for the Wire System.
    /// Provides one-click setup for breaker terminals, WireSettings asset creation,
    /// Wire prefab creation, and WireManager hierarchy setup.
    /// Accessible via Tools > Wire System in Unity Editor.
    /// </summary>
    public static class WireSystemSetup
    {
        private const string TERMINAL_LAYER_NAME = "Terminal";
        private const string HANDLE_LAYER_NAME = "WireHandle";

        [MenuItem("Tools/Wire System/1. Setup Terminals on Breaker", false, 1)]
        public static void SetupTerminals()
        {
            EnsureLayersExist();

            GameObject breaker = GameObject.Find("Four-Pole-Breaker-final");
            if (breaker == null)
            {
                // Fallback search across active scene
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
                EditorUtility.DisplayDialog("Setup Terminals", 
                    "Could not find 'Four-Pole-Breaker-final' in the current scene.\nPlease open SampleScene.", "OK");
                return;
            }

            int configuredCount = 0;
            int termLayer = LayerMask.NameToLayer(TERMINAL_LAYER_NAME);

            // Configure input terminals (term_in_1..4)
            for (int i = 1; i <= 4; i++)
            {
                string termName = $"term_in_{i}";
                Transform t = FindDeepChild(breaker.transform, termName);
                if (t != null)
                {
                    ConfigureTerminal(t.gameObject, $"IN_{i}", TerminalType.Input, Vector3.up, termLayer);
                    configuredCount++;
                }
                else
                {
                    Debug.LogWarning($"[WireSystemSetup] Could not locate {termName} under {breaker.name}");
                }
            }

            // Configure output terminals (term_out_1..4)
            for (int i = 1; i <= 4; i++)
            {
                string termName = $"term_out_{i}";
                Transform t = FindDeepChild(breaker.transform, termName);
                if (t != null)
                {
                    ConfigureTerminal(t.gameObject, $"OUT_{i}", TerminalType.Output, Vector3.down, termLayer);
                    configuredCount++;
                }
                else
                {
                    Debug.LogWarning($"[WireSystemSetup] Could not locate {termName} under {breaker.name}");
                }
            }

            EditorUtility.SetDirty(breaker);
            Debug.Log($"[WireSystemSetup] Successfully configured {configuredCount} terminals on {breaker.name}!");
            EditorUtility.DisplayDialog("Setup Complete", 
                $"Configured {configuredCount} terminals on '{breaker.name}'.\nTerminals assigned to layer '{TERMINAL_LAYER_NAME}' with trigger colliders and exit directions.", "OK");
        }

        private static void ConfigureTerminal(GameObject go, string id, TerminalType type, Vector3 exitDir, int layerIndex)
        {
            Undo.RecordObject(go, "Setup Terminal");

            if (layerIndex != -1)
            {
                go.layer = layerIndex;
            }

            // Ensure SphereCollider is trigger
            SphereCollider col = go.GetComponent<SphereCollider>();
            if (col == null) col = Undo.AddComponent<SphereCollider>(go);
            col.isTrigger = true;
            col.radius = 0.02f; // ~2cm local radius

            // Ensure Terminal component
            Terminal term = go.GetComponent<Terminal>();
            if (term == null) term = Undo.AddComponent<Terminal>(go);

            term.Setup(id, type, exitDir, 0.04f, 1);
            EditorUtility.SetDirty(term);
        }

        [MenuItem("Tools/Wire System/2. Create or Locate WireSettings Asset", false, 2)]
        public static WireSettings CreateOrLocateSettings()
        {
            string resourcesDir = "Assets/Resources";
            string assetPath = "Assets/Resources/WireSettings.asset";

            if (!Directory.Exists(resourcesDir))
            {
                Directory.CreateDirectory(resourcesDir);
                AssetDatabase.Refresh();
            }

            WireSettings settings = AssetDatabase.LoadAssetAtPath<WireSettings>(assetPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<WireSettings>();
                AssetDatabase.CreateAsset(settings, assetPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log($"[WireSystemSetup] Created new WireSettings asset at {assetPath}");
            }
            else
            {
                Debug.Log($"[WireSystemSetup] Found existing WireSettings asset at {assetPath}");
            }

            Selection.activeObject = settings;
            return settings;
        }

        [MenuItem("Tools/Wire System/3. Setup Complete Wire System in Scene", false, 3)]
        public static void SetupCompleteWireSystem()
        {
            EnsureLayersExist();
            WireSettings settings = CreateOrLocateSettings();

            // 1. Setup terminals
            SetupTerminals();

            // 2. Ensure WireManager GameObject in scene
            WireManager manager = Object.FindFirstObjectByType<WireManager>();
            if (manager == null)
            {
                GameObject managerGo = new GameObject("WireManager");
                Undo.RegisterCreatedObjectUndo(managerGo, "Create WireManager");
                manager = managerGo.AddComponent<WireManager>();
            }

            // 3. Ensure WireInputController on WireManager
            WireInputController inputCtrl = manager.GetComponent<WireInputController>();
            if (inputCtrl == null)
            {
                inputCtrl = Undo.AddComponent<WireInputController>(manager.gameObject);
            }

            // 4. Ensure AudioSource on WireManager
            AudioSource audio = manager.GetComponent<AudioSource>();
            if (audio == null)
            {
                audio = Undo.AddComponent<AudioSource>(manager.gameObject);
                audio.playOnAwake = false;
            }

            // Set references
            manager.SetSettings(settings);

            GameObject breaker = GameObject.Find("Four-Pole-Breaker-final");
            if (breaker != null)
            {
                // Set reference model root via reflection or serialized property
                SerializedObject so = new SerializedObject(manager);
                SerializedProperty prop = so.FindProperty("referenceModelRoot");
                if (prop != null)
                {
                    prop.objectReferenceValue = breaker.transform;
                    so.ApplyModifiedProperties();
                }
            }

            EditorUtility.SetDirty(manager);
            Selection.activeGameObject = manager.gameObject;

            Debug.Log("[WireSystemSetup] Complete Wire System configured successfully in scene!");
            EditorUtility.DisplayDialog("Wire System Ready", 
                "Wire System successfully installed in SampleScene!\n\n" +
                "• 8 Terminals configured with trigger colliders and exit normals\n" +
                "• WireSettings asset created in Assets/Resources/WireSettings.asset\n" +
                "• WireManager + WireInputController active in hierarchy\n\n" +
                "You can now test in Play Mode (Left Click & Drag between terminals)!", "Let's Go!");
        }

        [MenuItem("Tools/Wire System/Clear All Active Wires in Scene", false, 20)]
        public static void ClearAllWiresInScene()
        {
            if (WireManager.Instance != null)
            {
                WireManager.Instance.ClearAll();
            }
            else
            {
                Wire[] wires = Object.FindObjectsByType<Wire>(FindObjectsSortMode.None);
                foreach (var w in wires)
                {
                    if (w != null) Object.DestroyImmediate(w.gameObject);
                }
            }
            Debug.Log("[WireSystemSetup] All wires cleared from scene.");
        }

        private static void EnsureLayersExist()
        {
            SerializedObject tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");

            EnsureLayer(layers, 6, TERMINAL_LAYER_NAME);
            EnsureLayer(layers, 7, HANDLE_LAYER_NAME);

            tagManager.ApplyModifiedProperties();
        }

        private static void EnsureLayer(SerializedProperty layers, int index, string layerName)
        {
            if (index < 0 || index >= layers.arraySize) return;
            SerializedProperty element = layers.GetArrayElementAtIndex(index);
            if (string.IsNullOrEmpty(element.stringValue))
            {
                element.stringValue = layerName;
            }
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
