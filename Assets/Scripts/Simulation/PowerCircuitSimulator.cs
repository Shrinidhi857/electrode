using System;
using System.Collections.Generic;
using UnityEngine;
using ContactorAR.Breaker;
using ContactorAR.Wiring;

namespace ContactorAR.Simulation
{
    /// <summary>
    /// Simulates electrical circuit propagation across the contactor poles and connected wires.
    /// Manages mains power supply injection, contact closure conduction, voltage drops,
    /// and short-circuit fault tripping.
    /// </summary>
    public class PowerCircuitSimulator : MonoBehaviour
    {
        [Header("Target Contactor")]
        [SerializeField] private ContactorController contactor;

        [Header("Mains Supply Settings")]
        [Tooltip("Is the main electrical feeder supply energized?")]
        [SerializeField] private bool mainsSupplyEnabled = true;

        [Tooltip("Supply voltage for single phase to neutral.")]
        [SerializeField] private float lineVoltage = 230.0f;

        [Tooltip("Supply voltage between phases.")]
        [SerializeField] private float phaseToPhaseVoltage = 400.0f;

        [Header("Fault Detection")]
        [Tooltip("Automatically trip breaker if short circuit is created.")]
        [SerializeField] private bool autoTripOnShortCircuit = true;

        [Header("Telemetry")]
        [SerializeField] private bool isShortCircuitFault = false;
        [SerializeField] private float totalLoadCurrent = 0.0f;

        public bool MainsSupplyEnabled => mainsSupplyEnabled;
        public float LineVoltage => lineVoltage;
        public float PhaseToPhaseVoltage => phaseToPhaseVoltage;
        public bool IsShortCircuitFault => isShortCircuitFault;
        public float TotalLoadCurrent => totalLoadCurrent;

        public event Action OnCircuitEvaluated;

        private void Awake()
        {
            if (contactor == null)
            {
                contactor = FindFirstObjectByType<ContactorController>();
            }
        }

        private void OnEnable()
        {
            if (contactor != null && contactor.Lever != null)
            {
                contactor.Lever.OnStateChanged.AddListener(OnBreakerStateChanged);
            }
        }

        private void OnDisable()
        {
            if (contactor != null && contactor.Lever != null)
            {
                contactor.Lever.OnStateChanged.RemoveListener(OnBreakerStateChanged);
            }
        }

        private void Start()
        {
            EvaluateCircuit();
        }

        private void Update()
        {
            // Evaluate circuit state every frame to respond instantly to wire connects/disconnects
            EvaluateCircuit();
        }

        private void OnBreakerStateChanged(BreakerState state)
        {
            EvaluateCircuit();
        }

        public void SetMainsSupply(bool enabled)
        {
            mainsSupplyEnabled = enabled;
            EvaluateCircuit();
        }

        public void ToggleMainsSupply()
        {
            SetMainsSupply(!mainsSupplyEnabled);
        }

        /// <summary>
        /// Evaluates the complete electrical network graph.
        /// </summary>
        public void EvaluateCircuit()
        {
            if (contactor == null) return;

            isShortCircuitFault = false;
            totalLoadCurrent = 0f;

            // 1. Reset all terminal points in the scene
            TerminalPoint[] allTerminals = FindObjectsByType<TerminalPoint>(FindObjectsSortMode.None);
            for (int i = 0; i < allTerminals.Length; i++)
            {
                allTerminals[i].SetElectricalState(false, 0f, "");
            }

            // 2. Reset all wires in the scene
            CylindricalWire[] allWires = FindObjectsByType<CylindricalWire>(FindObjectsSortMode.None);
            for (int i = 0; i < allWires.Length; i++)
            {
                allWires[i].SetElectricalState(false, 0f);
            }

            // 3. Inject Mains Power into Contactor Input Terminals (term_in_1..4)
            if (mainsSupplyEnabled)
            {
                string[] defaultPhases = { "L1", "L2", "L3", "N" };
                for (int i = 0; i < contactor.Poles.Count; i++)
                {
                    BreakerPole pole = contactor.Poles[i];
                    if (pole.inputTerminal != null)
                    {
                        float v = (i == 3) ? 0f : lineVoltage; // Neutral = 0V reference, Phases = 230V
                        pole.inputTerminal.SetElectricalState(true, v, defaultPhases[i % 4]);
                    }
                }
            }

            // 4. Propagate across closed breaker poles (from input to output terminals)
            bool breakerClosed = contactor.IsClosed;
            for (int i = 0; i < contactor.Poles.Count; i++)
            {
                BreakerPole pole = contactor.Poles[i];
                if (breakerClosed && pole.inputTerminal != null && pole.outputTerminal != null)
                {
                    if (pole.inputTerminal.IsEnergized)
                    {
                        pole.outputTerminal.SetElectricalState(
                            true,
                            pole.inputTerminal.Voltage,
                            pole.inputTerminal.PhaseTag
                        );
                    }
                }
            }

            // 5. Propagate power through fully connected wires (both ends connected to actual terminals)
            for (int i = 0; i < allWires.Length; i++)
            {
                CylindricalWire wire = allWires[i];
                if (wire == null || !wire.IsFullyConnected) continue;

                TerminalPoint termA = wire.EndA.SnappedTerminal;
                TerminalPoint termB = wire.EndB.SnappedTerminal;

                bool termAEnergized = termA != null && termA.IsEnergized;
                bool termBEnergized = termB != null && termB.IsEnergized;

                // Check for short circuit fault (e.g. wire connects Phase to Neutral directly)
                if (termA != null && termB != null)
                {
                    if (termA.PhaseTag.StartsWith("L") && termB.PhaseTag == "N" ||
                        termB.PhaseTag.StartsWith("L") && termA.PhaseTag == "N")
                    {
                        if (termAEnergized || termBEnergized)
                        {
                            isShortCircuitFault = true;
                        }
                    }
                }

                if (termAEnergized && termB != null && !termB.IsEnergized)
                {
                    termB.SetElectricalState(true, termA.Voltage, termA.PhaseTag);
                    wire.SetElectricalState(true, termA.Voltage);
                    totalLoadCurrent += 4.5f; // Simulated current draw
                }
                else if (termBEnergized && termA != null && !termA.IsEnergized)
                {
                    termA.SetElectricalState(true, termB.Voltage, termB.PhaseTag);
                    wire.SetElectricalState(true, termB.Voltage);
                    totalLoadCurrent += 4.5f;
                }
                else if (termAEnergized || termBEnergized)
                {
                    float maxV = Mathf.Max(termA != null ? termA.Voltage : 0f, termB != null ? termB.Voltage : 0f);
                    wire.SetElectricalState(true, maxV);
                    totalLoadCurrent += 3.2f;
                }
                else
                {
                    wire.SetElectricalState(false, 0f);
                }
            }

            // 6. Handle Short Circuit Fault Auto-Trip
            if (isShortCircuitFault && autoTripOnShortCircuit && contactor.IsClosed)
            {
                contactor.TriggerOverloadTrip("Short Circuit: Direct Phase-to-Neutral Contact!");
            }

            OnCircuitEvaluated?.Invoke();
        }
    }
}
