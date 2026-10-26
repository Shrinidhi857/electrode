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

            // 1. Ensure ContactorController
            ContactorController contactor = GetComponent<ContactorController>();
            if (contactor == null)
            {
                contactor = gameObject.AddComponent<ContactorController>();
                log.AppendLine("[OK] ContactorController added.");
            }
            else log.AppendLine("[OK] ContactorController found.");

            // 2. Ensure MCBPivotLever
            MCBPivotLever lever = GetComponent<MCBPivotLever>();
            if (lever == null)
            {
                lever = gameObject.AddComponent<MCBPivotLever>();
                log.AppendLine("[OK] MCBPivotLever added.");
            }
            else log.AppendLine("[OK] MCBPivotLever found.");

            // 3. Ensure AR Manipulator
            AR.ARModelManipulator arManip = GetComponent<AR.ARModelManipulator>();
            if (arManip == null)
            {
                arManip = gameObject.AddComponent<AR.ARModelManipulator>();
                log.AppendLine("[OK] ARModelManipulator added.");
            }
            else log.AppendLine("[OK] ARModelManipulator found.");

            // 4. Auto-discover terminals
            contactor.AutoDiscoverTerminalsIfEmpty();
            log.AppendLine($"[OK] Terminals discovered: {contactor.Poles.Count * 2} (8 total).");

            // 5. Ensure ElectricalEffectsManager
            Simulation.ElectricalEffectsManager effects = GetComponent<Simulation.ElectricalEffectsManager>();
            if (effects == null)
            {
                effects = gameObject.AddComponent<Simulation.ElectricalEffectsManager>();
                log.AppendLine("[OK] ElectricalEffectsManager added.");
            }

            // 6. Validate global managers
            Simulation.PowerCircuitSimulator sim = FindFirstObjectByType<Simulation.PowerCircuitSimulator>();
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
            float scaleX = transform.localScale.x;
            if (scaleX < 0.05f || scaleX > 200f)
            {
                log.AppendLine($"[WARN] Scale looks unusual: {transform.localScale}. Consider resetting to (1,1,1) base.");
            }
            else
            {
                log.AppendLine($"[OK] Scale: {transform.localScale}");
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
