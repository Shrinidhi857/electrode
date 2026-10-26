using System;
using System.Collections.Generic;
using UnityEngine;
using ContactorAR.Breaker;
using ContactorAR.Wiring;
using ContactorAR.Simulation;
using ContactorAR.AR;

namespace ContactorAR.Simulation
{
    [System.Serializable]
    public class ContactorTelemetryData
    {
        public float Timestamp;
        public string BreakerState;
        public float LeverAngleDeg;
        public bool MainsActive;
        public float TotalCurrentA;
        public bool ShortCircuitFault;
        public int WiresConnected;
        public int TerminalsEnergized;
    }

    /// <summary>
    /// Unified simulation manager that ties together:
    /// - Electrical circuit evaluation loop
    /// - Telemetry logging
    /// - Contactor lifecycle coordination
    /// - Wire connection integrity checks
    /// - Safety interlocks (double-check trip logic)
    /// </summary>
    public class SimulationManager : MonoBehaviour
    {
        [Header("Scene References")]
        [SerializeField] private ContactorController contactor;
        [SerializeField] private PowerCircuitSimulator circuitSimulator;
        [SerializeField] private WireSpawner wireSpawner;

        [Header("Logging")]
        [SerializeField] private bool enableTelemetryLog = true;
        [SerializeField] private float logIntervalSeconds = 2.0f;
        private float logTimer = 0f;

        [Header("Safety Interlocks")]
        [Tooltip("Prevent switching ON if short circuit fault persists.")]
        [SerializeField] private bool preventOnIfFaulted = true;

        [Header("Runtime Telemetry (Live)")]
        [SerializeField] private ContactorTelemetryData lastTelemetry;

        public ContactorTelemetryData LastTelemetry => lastTelemetry;

        public event Action<ContactorTelemetryData> OnTelemetryLog;

        private void Awake()
        {
            FindReferences();
        }

        private void FindReferences()
        {
            if (contactor == null) contactor = FindFirstObjectByType<ContactorController>();
            if (circuitSimulator == null) circuitSimulator = FindFirstObjectByType<PowerCircuitSimulator>();
            if (wireSpawner == null) wireSpawner = FindFirstObjectByType<WireSpawner>();
        }

        private void OnEnable()
        {
            if (contactor != null && contactor.Lever != null)
            {
                contactor.Lever.OnStateChanged.AddListener(OnBreakerStateChange);
            }
        }

        private void OnDisable()
        {
            if (contactor != null && contactor.Lever != null)
            {
                contactor.Lever.OnStateChanged.RemoveListener(OnBreakerStateChange);
            }
        }

        private void Update()
        {
            EnforceInterlocks();

            if (enableTelemetryLog)
            {
                logTimer += Time.deltaTime;
                if (logTimer >= logIntervalSeconds)
                {
                    logTimer = 0f;
                    CaptureTelemetry();
                }
            }
        }

        private void EnforceInterlocks()
        {
            if (circuitSimulator == null || contactor == null) return;

            // Safety interlock: auto-trip if fault AND breaker ON
            if (preventOnIfFaulted && circuitSimulator.IsShortCircuitFault && contactor.IsClosed)
            {
                contactor.TriggerOverloadTrip("Interlock: Short circuit detected while ON");
            }
        }

        private void OnBreakerStateChange(BreakerState state)
        {
            CaptureTelemetry();
        }

        private void CaptureTelemetry()
        {
            if (contactor == null || contactor.Lever == null) return;

            int wiresConnected = 0;
            int energizedTerminals = 0;

            if (wireSpawner != null)
            {
                for (int i = 0; i < wireSpawner.SpawnedWires.Count; i++)
                {
                    CylindricalWire w = wireSpawner.SpawnedWires[i];
                    if (w != null && w.EndA != null && w.EndB != null)
                    {
                        if (w.EndA.IsConnected || w.EndB.IsConnected)
                            wiresConnected++;
                    }
                }
            }

            TerminalPoint[] terminals = FindObjectsByType<TerminalPoint>(FindObjectsSortMode.None);
            for (int i = 0; i < terminals.Length; i++)
            {
                if (terminals[i].IsEnergized) energizedTerminals++;
            }

            lastTelemetry = new ContactorTelemetryData
            {
                Timestamp = Time.time,
                BreakerState = contactor.State.ToString(),
                LeverAngleDeg = contactor.Lever.CurrentAngle,
                MainsActive = circuitSimulator != null ? circuitSimulator.MainsSupplyEnabled : false,
                TotalCurrentA = circuitSimulator != null ? circuitSimulator.TotalLoadCurrent : 0f,
                ShortCircuitFault = circuitSimulator != null ? circuitSimulator.IsShortCircuitFault : false,
                WiresConnected = wiresConnected,
                TerminalsEnergized = energizedTerminals
            };

            OnTelemetryLog?.Invoke(lastTelemetry);

            if (enableTelemetryLog)
            {
                Debug.Log($"[SimulationManager] T={lastTelemetry.Timestamp:F1}s | State={lastTelemetry.BreakerState} | Angle={lastTelemetry.LeverAngleDeg:F1}° | Mains={lastTelemetry.MainsActive} | I={lastTelemetry.TotalCurrentA:F1}A | SC={lastTelemetry.ShortCircuitFault} | Wires={lastTelemetry.WiresConnected} | EnergizedPins={lastTelemetry.TerminalsEnergized}");
            }
        }

        /// <summary>
        /// Performs a wiring integrity check and logs warnings for floating (unconnected) wire ends.
        /// </summary>
        public void PerformWiringCheck()
        {
            if (wireSpawner == null) return;

            bool allConnected = true;
            for (int i = 0; i < wireSpawner.SpawnedWires.Count; i++)
            {
                CylindricalWire w = wireSpawner.SpawnedWires[i];
                if (w == null) continue;

                bool aFree = w.EndA != null && !w.EndA.IsConnected;
                bool bFree = w.EndB != null && !w.EndB.IsConnected;

                if (aFree || bFree)
                {
                    Debug.LogWarning($"[SimulationManager] Wire '{w.PhaseName}' has floating connector(s). Connect to terminal(s) to complete circuit.");
                    allConnected = false;
                }
            }

            if (allConnected && wireSpawner.SpawnedWires.Count > 0)
            {
                Debug.Log("[SimulationManager] All wires properly connected!");
            }
        }
    }
}
