using UnityEngine;

namespace ContactorAR.AR
{
    /// <summary>
    /// Automatically detects and disables the default Unity Mobile AR Template GameObjects
    /// (the template 'UI' canvas, GoalManager coaching overlay, ARTemplateMenuManager, and Object Spawner)
    /// so they do not intercept touch inputs, spawn unwanted template cubes/cylinders, or block raycasts.
    /// Runs at highest priority before other scene components initialize.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    public class ARTemplateDisabler : MonoBehaviour
    {
        [Tooltip("If true, automatically disables the template GameObjects upon Awake.")]
        [SerializeField] private bool autoDisableOnAwake = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoDisableOnPlay()
        {
            DisableTemplateInterference();
        }

        private void Awake()
        {
            if (autoDisableOnAwake)
            {
                DisableTemplateInterference();
            }
        }

        /// <summary>
        /// Finds and deactivates all default template interference in the scene.
        /// </summary>
        public static void DisableTemplateInterference()
        {
            int disabledCount = 0;

            // 1. Disable by known template GameObject names
            string[] templateGoNames = new[] { "UI", "Object Spawner", "Object Menu", "Coaching UI", "Greeting Prompt", "Options Modal" };
            foreach (string goName in templateGoNames)
            {
                GameObject go = GameObject.Find(goName);
                if (go != null)
                {
                    if (goName == "UI")
                    {
                        bool isTemplateUI = go.transform.Find("Object Menu") != null
                                         || go.transform.Find("Coaching UI") != null
                                         || go.transform.Find("Greeting Prompt") != null;
                        if (!isTemplateUI) continue;
                    }

                    go.SetActive(false);
                    disabledCount++;
                    Debug.Log($"[ContactorAR] Disabled template GameObject: '{goName}' to prevent touch interference.");
                }
            }

            // 2. Disable template script components by type name
            MonoBehaviour[] allBehaviours = Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (MonoBehaviour mb in allBehaviours)
            {
                if (mb == null) continue;
                string typeName = mb.GetType().Name;

                if (typeName == "GoalManager" || typeName == "ARTemplateMenuManager" || typeName == "ObjectSpawner")
                {
                    mb.enabled = false;
                    if (mb.gameObject != null && mb.gameObject.activeSelf)
                    {
                        if (mb.gameObject.name == "UI" || mb.gameObject.name == "Object Spawner")
                        {
                            mb.gameObject.SetActive(false);
                            disabledCount++;
                        }
                    }
                    Debug.Log($"[ContactorAR] Disabled template script component: {typeName} on '{mb.gameObject.name}'.");
                }
            }

            // 3. Hide AR plane floor overlay visualizations completely.
            //    ARPlaneManager renders a plane prefab mesh on every detected surface.
            //    We disable all MeshRenderer/LineRenderer children on every ARPlane in the scene.
            DisableARPlaneVisualizations();

            if (disabledCount > 0)
            {
                Debug.Log($"[ContactorAR] Successfully deactivated default AR Template systems ({disabledCount} objects/components neutralized).");
            }
        }

        private void Update()
        {
            // Continuously suppress any plane visualizations that AR Foundation spawns dynamically
            DisableARPlaneVisualizations();
        }

        /// <summary>
        /// Iterates over all AR plane GameObjects in the scene and disables their
        /// MeshRenderer and LineRenderer components so the floor grid overlay is invisible.
        /// Also sets ARPlaneManager.planePrefab to null to prevent future planes from rendering.
        /// </summary>
        public static void DisableARPlaneVisualizations()
        {
            var pm = Object.FindFirstObjectByType<UnityEngine.XR.ARFoundation.ARPlaneManager>(FindObjectsInactive.Include);
            if (pm != null)
            {
                if (pm.planePrefab != null) pm.planePrefab = null;
                foreach (var plane in pm.trackables)
                {
                    DisableRenderersOnHierarchy(plane.gameObject);
                    if (plane.gameObject.activeSelf) plane.gameObject.SetActive(false);
                }
            }

            // Also catch any GameObjects whose name suggests they are plane visualizations
            string[] planeNames = { "ARPlane", "AR Feathered Plane", "PlaneVisualization", "PlaneDebugVisualizer", "DebugPlane", "DetectedPlane" };
            foreach (string pName in planeNames)
            {
                foreach (GameObject go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (go != null && go.name.Contains(pName))
                    {
                        DisableRenderersOnHierarchy(go);
                        if (go.activeSelf) go.SetActive(false);
                    }
                }
            }
        }

        private static void DisableRenderersOnHierarchy(GameObject go)
        {
            if (go == null) return;
            foreach (MeshRenderer mr in go.GetComponentsInChildren<MeshRenderer>(true))
                mr.enabled = false;
            foreach (LineRenderer lr in go.GetComponentsInChildren<LineRenderer>(true))
                lr.enabled = false;
        }
    }
}
