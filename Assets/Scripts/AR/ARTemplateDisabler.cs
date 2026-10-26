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
                // Look for both active and inactive objects
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

            // 2. Disable any components by type name across the scene (e.g., GoalManager, ARTemplateMenuManager, ObjectSpawner)
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
                        // Check if it's the template UI or spawner root
                        if (mb.gameObject.name == "UI" || mb.gameObject.name == "Object Spawner")
                        {
                            mb.gameObject.SetActive(false);
                            disabledCount++;
                        }
                    }
                    Debug.Log($"[ContactorAR] Disabled template script component: {typeName} on '{mb.gameObject.name}'.");
                }
            }

            if (disabledCount > 0)
            {
                Debug.Log($"[ContactorAR] Successfully deactivated default AR Template systems ({disabledCount} objects/components neutralized).");
            }
        }
    }
}
