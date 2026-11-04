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

            // Target breaker GameObject: either this GameObject or loaded from Assets if missing
            GameObject breakerObj = gameObject;
            bool isSelfBreaker = GetComponent<ContactorController>() != null || 
                                 GetComponent<MCBPivotLever>() != null || 
                                 gameObject.name.ToLower().Contains("mcb") || 
                                 gameObject.name.ToLower().Contains("breaker");

            if (!isSelfBreaker)
            {
                ContactorController existing = FindAnyObjectByType<ContactorController>();
                if (existing != null)
                {
                    breakerObj = existing.gameObject;
                    log.AppendLine($"[OK] Found existing breaker in scene: {breakerObj.name}");
                }
                else
                {
                    // Load from Assets!
                    breakerObj = ModelAssetLoader.EnsureMCBInScene();
                    log.AppendLine($"[OK] Loaded MCB from Assets: {breakerObj.name}");
                }
            }

            // 1. Ensure ContactorController
            ContactorController contactor = breakerObj.GetComponent<ContactorController>();
            if (contactor == null)
            {
                contactor = breakerObj.AddComponent<ContactorController>();
                log.AppendLine("[OK] ContactorController added.");
            }
            else log.AppendLine("[OK] ContactorController found.");

            // 2. Ensure MCBPivotLever with 3_pole_mcb_final settings (-160° ON / -70° OFF on X axis)
            MCBPivotLever lever = breakerObj.GetComponent<MCBPivotLever>();
            if (lever == null)
            {
                lever = breakerObj.AddComponent<MCBPivotLever>();
                log.AppendLine("[OK] MCBPivotLever added.");
            }
            else log.AppendLine("[OK] MCBPivotLever found.");

            Transform pivotTrans = ModelAssetLoader.FindDeepChild(breakerObj.transform, "mcb_pivot");
            if (pivotTrans != null)
            {
                lever.Configure(pivotTrans, ModelAssetLoader.MCB_ON_ANGLE, ModelAssetLoader.MCB_OFF_ANGLE, ModelAssetLoader.MCB_TRIP_ANGLE, RotationAxis.X);
                log.AppendLine($"[OK] MCBPivotLever calibrated: ON={ModelAssetLoader.MCB_ON_ANGLE}°, OFF={ModelAssetLoader.MCB_OFF_ANGLE}°, TRIP={ModelAssetLoader.MCB_TRIP_ANGLE}° on X axis.");
            }

            // 3. Ensure AR Manipulator
            AR.ARModelManipulator arManip = breakerObj.GetComponent<AR.ARModelManipulator>();
            if (arManip == null)
            {
                arManip = breakerObj.AddComponent<AR.ARModelManipulator>();
                log.AppendLine("[OK] ARModelManipulator added.");
            }
            else log.AppendLine("[OK] ARModelManipulator found.");

            // 4. Configure terminals & actuator based on model type
            bool isContactor = breakerObj.name.ToLower().Contains("contactor") || breakerObj.GetComponent<ContactorArmatureController>() != null;
            if (isContactor)
            {
                ModelAssetLoader.ConfigureContactorInstance(breakerObj, applyDefaultScale: false); // scale applied in step 8
                log.AppendLine($"[OK] Configured as Contactor: 12 terminals, CJX2S-090018 armature plunger (ON=0.0126, OFF=0.0069).");
            }
            else
            {
                ModelAssetLoader.ConfigureMCBInstance(breakerObj, applyDefaultScale: false);        // scale applied in step 8
                contactor.AutoDiscoverTerminalsIfEmpty();
                log.AppendLine($"[OK] Configured as MCB Breaker: 8 terminals (term_in_1..4, term_out_1..4), mcb_pivot.");
            }

            // Also check for any standalone contactor in the scene
            ContactorArmatureController[] allContactors = FindObjectsByType<ContactorArmatureController>(FindObjectsSortMode.None);
            foreach (var ct in allContactors)
            {
                if (ct.gameObject != breakerObj)
                {
                    ModelAssetLoader.ConfigureContactorInstance(ct.gameObject);
                    log.AppendLine($"[OK] Configured additional contactor in scene: {ct.name}");
                }
            }

            // 5. Ensure ElectricalEffectsManager
            Simulation.ElectricalEffectsManager effects = breakerObj.GetComponent<Simulation.ElectricalEffectsManager>();
            if (effects == null)
            {
                effects = breakerObj.AddComponent<Simulation.ElectricalEffectsManager>();
                log.AppendLine("[OK] ElectricalEffectsManager added.");
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

            // 7. Validate distance from camera (keep model 1-2 feet / ~0.50m directly in front).
            //    Only reposition if the model is clearly out-of-range (> 3m) — avoids fighting with
            //    ARModelManipulator when the user has manually placed the model within a reasonable range.
            Camera cam = Camera.main;
            if (cam != null && breakerObj != null)
            {
                float dist = Vector3.Distance(breakerObj.transform.position, cam.transform.position);
                bool tooFar   = dist > 3.0f;                              // clearly not in AR view
                bool atOrigin = breakerObj.transform.position == Vector3.zero; // never been positioned
                if (tooFar || atOrigin)
                {
                    Vector3 calibratedPos = cam.transform.position + cam.transform.forward * ModelAssetLoader.SPAWN_DISTANCE;
                    breakerObj.transform.position = calibratedPos;
                    breakerObj.transform.rotation = Quaternion.LookRotation(-cam.transform.forward, Vector3.up);
                    log.AppendLine($"[CALIBRATED] Model repositioned to {ModelAssetLoader.SPAWN_DISTANCE:F2}m in front of camera (was {dist:F2}m away).");
                }
                else
                {
                    log.AppendLine($"[OK] Model already in valid range ({dist:F2}m from camera) — position preserved.");
                }
            }

            // 8. Validate scale
            if (breakerObj != null)
            {
                float targetScale = isContactor ? ModelAssetLoader.CONTACTOR_DEFAULT_SCALE : ModelAssetLoader.MCB_DEFAULT_SCALE;
                breakerObj.transform.localScale = Vector3.one * targetScale;
                log.AppendLine($"[OK] Scale calibrated: {breakerObj.transform.localScale}");

                // Notify ARModelManipulator of the final scale so pinch-to-scale bounds are correct
                AR.ARModelManipulator manip2 = breakerObj.GetComponent<AR.ARModelManipulator>();
                if (manip2 != null) manip2.RefreshScaleBounds();
            }

            // 9. Start in FreeMovable mode and register native anchor to prevent drift.
            if (arManip != null)
            {
                arManip.ReleaseToFreeMovable();
                arManip.AnchorAtCurrentPose();
                log.AppendLine("[OK] Breaker started in FreeMovable mode with native AR anchor.");
            }

            log.AppendLine("=== Bootstrap Complete ===");

            string logStr = log.ToString();
            Debug.Log(logStr);

            if (debugLogText != null)
            {
                debugLogText.text = logStr;
            }
        }
    }
}
