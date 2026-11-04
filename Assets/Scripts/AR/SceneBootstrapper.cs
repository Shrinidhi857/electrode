using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ContactorAR.UI;

namespace ContactorAR.AR
{
    /// <summary>
    /// Auto-injects itself into every scene load and:
    ///   1. Disables all legacy AR Foundation template UI panels (Coaching UI, Object Menu, etc.)
    ///   2. Ensures the ARContactorUI has a proper ScreenSpaceOverlay Canvas to render into.
    ///   3. Ensures EventSystem exists.
    /// Uses [RuntimeInitializeOnLoadMethod] so no scene reference is needed.
    /// </summary>
    [DefaultExecutionOrder(-200)]  // Run before ARContactorUI.Awake()
    public class SceneBootstrapper : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoInject()
        {
            // Schedule injection after scene objects are created
            // Use AfterSceneLoad so Awake of scene objects has run, 
            // but we still get to run before first Update.
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInjectAfterLoad()
        {
            // Don't add if already in the scene
            if (FindAnyObjectByType<SceneBootstrapper>() != null) return;

            GameObject go = new GameObject("[SceneBootstrapper]");
            go.AddComponent<SceneBootstrapper>();
            DontDestroyOnLoad(go);
            Debug.Log("[SceneBootstrapper] Auto-injected.");
        }
        // Names of root/child GameObjects from the old AR Foundation template to disable
        private static readonly string[] LegacyUINames = new[]
        {
            "Coaching UI",
            "Object Menu",
            "Object Menu Animator",
            "Object Spawner",
            "Hints Button",
            "Debug Menu Toggle",
            "Debug Plane Toggle",
            "Remove Objects Button",
            "Create Button",
            "Cancel Button",
            "Options Button",
            "Options Modal",
            "Spawn Object",
            "Delete Button",
        };

        private void Awake()
        {
            DisableLegacyUI();
            EnsureARAnchorManager();
            EnsureARContactorCanvas();
        }

        private void EnsureARAnchorManager()
        {
            Unity.XR.CoreUtils.XROrigin origin = FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
            if (origin != null)
            {
                var anchorMgr = origin.GetComponent<UnityEngine.XR.ARFoundation.ARAnchorManager>();
                if (anchorMgr == null)
                {
                    origin.gameObject.AddComponent<UnityEngine.XR.ARFoundation.ARAnchorManager>();
                    Debug.Log("[SceneBootstrapper] Attached ARAnchorManager to XROrigin.");
                }
            }
        }

        private void DisableLegacyUI()
        {
            // Search all GameObjects (including inactive) and disable legacy ones
            var allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
            foreach (var go in allObjects)
            {
                // Only process scene objects (not prefab assets)
                if (go.scene.name == null || go.scene.name == "") continue;

                string goName = go.name;
                foreach (string legacy in LegacyUINames)
                {
                    if (goName == legacy)
                    {
                        go.SetActive(false);
                        Debug.Log($"[SceneBootstrapper] Disabled legacy UI: {goName}");
                        break;
                    }
                }
            }
        }

        private void EnsureARContactorCanvas()
        {
            // Find the ARContactorUI component in the scene
            ARContactorUI uiComponent = FindAnyObjectByType<ARContactorUI>(FindObjectsInactive.Include);
            if (uiComponent == null)
            {
                Debug.LogWarning("[SceneBootstrapper] ARContactorUI not found in scene. Creating one.");
                CreateFreshUI();
                return;
            }

            GameObject uiGO = uiComponent.gameObject;

            // Check if this GameObject already has a Canvas component
            Canvas existingCanvas = uiGO.GetComponent<Canvas>();
            if (existingCanvas == null)
            {
                // Check if it has a Canvas in its parent hierarchy
                Canvas parentCanvas = uiGO.GetComponentInParent<Canvas>();
                if (parentCanvas != null)
                {
                    // Already under a Canvas — just ensure it's configured correctly
                    ConfigureCanvas(parentCanvas);
                    Debug.Log($"[SceneBootstrapper] ARContactorUI is under canvas: {parentCanvas.gameObject.name}");
                    return;
                }

                // No Canvas found anywhere — create a new Canvas root and reparent
                Debug.Log("[SceneBootstrapper] ARContactorUI has no Canvas. Creating Canvas root.");
                GameObject canvasGO = CreateCanvasRoot("ARContactorUI_Canvas");
                uiComponent.transform.SetParent(canvasGO.transform, false);

                // Reset RectTransform so it fills the Canvas
                RectTransform rt = uiGO.GetComponent<RectTransform>();
                if (rt == null) rt = uiGO.AddComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
            }
            else
            {
                // Already has a Canvas — configure it
                ConfigureCanvas(existingCanvas);
                Debug.Log($"[SceneBootstrapper] ARContactorUI has its own Canvas on {uiGO.name}");
            }
        }

        private void CreateFreshUI()
        {
            GameObject canvasGO = CreateCanvasRoot("ARContactorUI_Canvas");
            GameObject uiGO = new GameObject("ARContactorUI");
            uiGO.transform.SetParent(canvasGO.transform, false);
            RectTransform rt = uiGO.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            uiGO.AddComponent<ARContactorUI>();
        }

        private GameObject CreateCanvasRoot(string name)
        {
            GameObject canvasGO = new GameObject(name);
            Canvas canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100; // On top of everything

            CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            canvasGO.AddComponent<GraphicRaycaster>();

            ConfigureCanvas(canvas);
            return canvasGO;
        }

        private static void ConfigureCanvas(Canvas canvas)
        {
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            if (canvas.sortingOrder < 100) canvas.sortingOrder = 100;
        }
    }
}
