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
                ModelAssetLoader.ConfigureContactorInstance(breakerObj);
                log.AppendLine($"[OK] Configured as Contactor: 12 terminals, CJX2S-090018 armature plunger (ON=0.0126, OFF=0.0069).");
            }
            else
            {
                ModelAssetLoader.ConfigureMCBInstance(breakerObj);
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

            // 7. Validate distance from camera (keep model 1-2 feet / ~0.50m directly in front)
            Camera cam = Camera.main;
            if (cam != null && breakerObj != null)
            {
                float dist = Vector3.Distance(breakerObj.transform.position, cam.transform.position);
                if (dist > 1.2f || breakerObj.transform.position.z > 1.2f)
                {
                    Vector3 calibratedPos = cam.transform.position + cam.transform.forward * ModelAssetLoader.SPAWN_DISTANCE;
                    breakerObj.transform.position = calibratedPos;
                    breakerObj.transform.rotation = Quaternion.LookRotation(-cam.transform.forward, Vector3.up);
                    log.AppendLine($"[CALIBRATED] Model positioned 1-2 feet ({ModelAssetLoader.SPAWN_DISTANCE:F2}m) directly in front of camera.");
                }
            }

            // 8. Validate scale
            if (breakerObj != null)
            {
                float targetScale = isContactor ? ModelAssetLoader.CONTACTOR_DEFAULT_SCALE : ModelAssetLoader.MCB_DEFAULT_SCALE;
                breakerObj.transform.localScale = Vector3.one * targetScale;
                log.AppendLine($"[OK] Scale calibrated: {breakerObj.transform.localScale}");
            }

            // 9. Lock model in place to prevent drift
            if (arManip != null)
            {
                arManip.LockCurrentPosition();
                log.AppendLine("[OK] Breaker anchored and locked to prevent camera drift.");
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
