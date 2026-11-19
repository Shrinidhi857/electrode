using System.Collections.Generic;
using UnityEngine;
using ContactorAR.Breaker;
using ContactorAR.Wiring;

namespace ContactorAR.Breaker
{
    /// <summary>
    /// Runtime bootstrap:  Ensures that all required components exist on the breaker prefab root,
    /// wires up event chains, validates scene references, and logs a complete feature summary.
    /// Attach this to the Four-Pole-Breaker root GameObject.
    /// </summary>
    [DefaultExecutionOrder(-100)] // Executes before all other ContactorAR scripts
    public class ContactorARBootstrapper : MonoBehaviour
    {
        [Header("Auto-Setup")]
        [Tooltip("Automatically add required components if missing at runtime.")]
        [SerializeField] private bool autoSetup = true;

        [Tooltip("Assign a status label where boot log is written.")]
        [SerializeField] private TMPro.TextMeshProUGUI debugLogText;

        private void Awake()
        {
            if (autoSetup)
            {
                RunBootstrap();
            }
        }

        private void RunBootstrap()
        {
            // 0. Disable interfering default AR Template (UI & Object Spawner)
            ContactorAR.AR.ARTemplateDisabler.DisableTemplateInterference();

            System.Text.StringBuilder log = new System.Text.StringBuilder();
            log.AppendLine("=== ContactorAR Bootstrap ===");

            // Target breaker GameObject: either this GameObject or existing in scene
            GameObject breakerObj = null;
            bool isSelfBreaker = GetComponent<ContactorController>() != null || 
                                 GetComponent<MCBPivotLever>() != null || 
                                 gameObject.name.ToLower().Contains("mcb") || 
                                 gameObject.name.ToLower().Contains("breaker");

            if (isSelfBreaker)
            {
                breakerObj = gameObject;
            }
            else
            {
                ContactorController existing = FindAnyObjectByType<ContactorController>();
                if (existing != null)
                {
                    breakerObj = existing.gameObject;
                    log.AppendLine($"[OK] Found existing breaker in scene: {breakerObj.name}");
                }
                else
                {
                    // Clean startup mode: do NOT spawn any model automatically!
                    // Camera view starts completely empty; models are imported on demand when user clicks sidebar buttons.
                    log.AppendLine("[OK] Clean startup mode active: Camera view opens with no models pre-loaded. Models import on button click.");
                }
            }

            // 1-5. If a breaker exists in scene, configure it
            AR.ARModelManipulator arManip = null;
            bool isContactor = false;

            if (breakerObj != null)
            {
                ContactorController contactor = breakerObj.GetComponent<ContactorController>();
                if (contactor == null)
                {
                    contactor = breakerObj.AddComponent<ContactorController>();
                    log.AppendLine("[OK] ContactorController added.");
                }

                MCBPivotLever lever = breakerObj.GetComponent<MCBPivotLever>();
                if (lever == null)
                {
                    lever = breakerObj.AddComponent<MCBPivotLever>();
                    log.AppendLine("[OK] MCBPivotLever added.");
                }

                Transform pivotTrans = ModelAssetLoader.FindDeepChild(breakerObj.transform, "mcb_pivot");
                if (pivotTrans != null)
                {
                    lever.Configure(pivotTrans, ModelAssetLoader.MCB_ON_ANGLE, ModelAssetLoader.MCB_OFF_ANGLE, ModelAssetLoader.MCB_TRIP_ANGLE, RotationAxis.X);
                }

                arManip = breakerObj.GetComponent<AR.ARModelManipulator>();
                if (arManip == null) arManip = breakerObj.AddComponent<AR.ARModelManipulator>();

                isContactor = breakerObj.name.ToLower().Contains("contactor") || breakerObj.GetComponent<ContactorArmatureController>() != null;
                if (isContactor)
                {
                    ModelAssetLoader.ConfigureContactorInstance(breakerObj, applyDefaultScale: false);
                }
                else
                {
                    ModelAssetLoader.ConfigureMCBInstance(breakerObj, applyDefaultScale: false);
                    contactor.AutoDiscoverTerminalsIfEmpty();
                }

                Simulation.ElectricalEffectsManager effects = breakerObj.GetComponent<Simulation.ElectricalEffectsManager>();
                if (effects == null) breakerObj.AddComponent<Simulation.ElectricalEffectsManager>();
            }

            // 6. Validate global managers
            Simulation.PowerCircuitSimulator sim = FindAnyObjectByType<Simulation.PowerCircuitSimulator>();
            if (sim == null)
            {
                GameObject simObj = new GameObject("CircuitSimulator");
                sim = simObj.AddComponent<Simulation.PowerCircuitSimulator>();
                simObj.AddComponent<WireSpawner>();
                simObj.AddComponent<Simulation.SimulationManager>();
                log.AppendLine("[OK] PowerCircuitSimulator auto-created.");
            }
            else log.AppendLine("[OK] PowerCircuitSimulator found.");

            // 7. Validate scale
            if (breakerObj != null)
            {
                float targetScale = isContactor ? ModelAssetLoader.CONTACTOR_DEFAULT_SCALE : ModelAssetLoader.MCB_DEFAULT_SCALE;
                breakerObj.transform.localScale = Vector3.one * targetScale;
                log.AppendLine($"[OK] Scale calibrated: {breakerObj.transform.localScale}");

                AR.ARModelManipulator manip2 = breakerObj.GetComponent<AR.ARModelManipulator>();
                if (manip2 != null) manip2.RefreshScaleBounds();
            }

            // 8. Start intelligent AR tracking-aware placement & native plane anchoring (if model exists)
            if (breakerObj != null)
            {
                StartCoroutine(StartPlacementWhenARReady(breakerObj, arManip));
            }

            log.AppendLine("=== Bootstrap Complete ===");

            string logStr = log.ToString();
            Debug.Log(logStr);

            if (debugLogText != null)
            {
                debugLogText.text = logStr;
            }
        }

        private System.Collections.IEnumerator StartPlacementWhenARReady(GameObject breakerObj, AR.ARModelManipulator arManip)
        {
#if !UNITY_EDITOR
            // Wait until ARCore tracking is fully active before placing and anchoring
            float timeout = 8.0f;
            float elapsed = 0f;
            while (UnityEngine.XR.ARFoundation.ARSession.state != UnityEngine.XR.ARFoundation.ARSessionState.SessionTracking && elapsed < timeout)
            {
                yield return new WaitForSeconds(0.2f);
                elapsed += 0.2f;
            }
#endif
            Camera cam = Camera.main;
            if (cam == null || breakerObj == null) yield break;

            UnityEngine.XR.ARFoundation.ARRaycastManager rayMgr = FindAnyObjectByType<UnityEngine.XR.ARFoundation.ARRaycastManager>();
            List<UnityEngine.XR.ARFoundation.ARRaycastHit> hits = new List<UnityEngine.XR.ARFoundation.ARRaycastHit>();
            bool placedOnSurface = false;

            // Try to place on the first detected surface (table, desk, or wall/door)
            if (rayMgr != null)
            {
                Ray fwdRay = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
                if (rayMgr.Raycast(fwdRay, hits, UnityEngine.XR.ARSubsystems.TrackableType.PlaneWithinPolygon | UnityEngine.XR.ARSubsystems.TrackableType.PlaneWithinBounds) && hits.Count > 0)
                {
                    breakerObj.transform.position = hits[0].pose.position;
                    breakerObj.transform.rotation = Quaternion.LookRotation(-cam.transform.forward, Vector3.up);
                    placedOnSurface = true;
                    Debug.Log($"[ContactorARBootstrapper] Placed breaker on detected AR surface at {hits[0].pose.position}");
                }
            }

            if (!placedOnSurface)
            {
                // Fallback to comfortable 0.70m in front of camera at eye level
                Vector3 camFwd = cam.transform.forward;
                camFwd.y = 0;
                if (camFwd.sqrMagnitude < 0.01f) camFwd = cam.transform.forward;
                camFwd.Normalize();

                Vector3 targetPos = cam.transform.position + camFwd * 0.70f;
                breakerObj.transform.position = targetPos;
                breakerObj.transform.rotation = Quaternion.LookRotation(-camFwd, Vector3.up);
            }

            if (arManip != null)
            {
                arManip.ReleaseToFreeMovable();
                arManip.AnchorAtCurrentPose();
                Debug.Log($"[ContactorARBootstrapper] Breaker anchored firmly with native ARAnchor.");
            }
        }
    }
}
