using System;
using System.Collections.Generic;
using UnityEngine;
using ContactorAR.Wiring;

namespace ContactorAR.Breaker
{
    [System.Serializable]
    public class BreakerPole
    {
        public string poleName = "Pole 1";
        public TerminalPoint inputTerminal;
        public TerminalPoint outputTerminal;
        [HideInInspector] public bool isClosed;
    }

    /// <summary>
    /// Master coordinator for the 4-Pole Contactor / Breaker.
    /// Manages the 4 internal electrical poles, synchronizes with the pivot lever,
    /// and controls indicator lights and visual feedback.
    /// </summary>
    [SelectionBase]
    public class ContactorController : MonoBehaviour
    {
        [Header("Components")]
        [Tooltip("The lever pivot component controlling -1.4 to 68 deg movement.")]
        [SerializeField] private MCBPivotLever lever;

        [Header("4-Pole Terminal Configuration")]
        [SerializeField] private List<BreakerPole> poles = new List<BreakerPole>();

        [Header("Visual Indicators")]
        [Tooltip("Optional indicator mesh or light that changes color with breaker status.")]
        [SerializeField] private MeshRenderer statusIndicatorRenderer;
        [SerializeField] private Color offSafeColor = new Color(0.1f, 0.8f, 0.2f, 1f); // Green
        [SerializeField] private Color onLiveColor = new Color(0.9f, 0.1f, 0.1f, 1f);   // Red
        [SerializeField] private Color tripAlertColor = new Color(1.0f, 0.6f, 0.0f, 1f); // Amber / Yellow

        [Header("Electrical Specs")]
        [Tooltip("Rated current in Amperes.")]
        [SerializeField] private float ratedCurrentAmps = 63f;

        [Tooltip("Rated voltage in Volts.")]
        [SerializeField] private float ratedVoltage = 400f;

        public MCBPivotLever Lever => lever;
        public List<BreakerPole> Poles => poles;
        public BreakerState State => lever != null ? lever.CurrentState : BreakerState.Off;
        public bool IsClosed => lever != null && lever.IsOn;
        public float RatedCurrent => ratedCurrentAmps;
        public float RatedVoltage => ratedVoltage;

        private Material indicatorMaterialInstance;

        private void Awake()
        {
            if (lever == null)
            {
                lever = GetComponentInChildren<MCBPivotLever>();
                if (lever == null)
                {
                    lever = gameObject.AddComponent<MCBPivotLever>();
                }
            }

            if (statusIndicatorRenderer != null)
            {
                indicatorMaterialInstance = statusIndicatorRenderer.material;
            }
        }

        private void OnEnable()
        {
            if (lever != null)
            {
                lever.OnStateChanged.AddListener(HandleStateChanged);
            }
        }

        private void OnDisable()
        {
            if (lever != null)
            {
                lever.OnStateChanged.RemoveListener(HandleStateChanged);
            }
        }

        private void Start()
        {
            AutoDiscoverTerminalsIfEmpty();
            UpdatePolesConductivity(lever.CurrentState == BreakerState.On);
            UpdateVisualIndicator(lever.CurrentState);
        }

        /// <summary>
        /// Automatically discovers term_in_1..4 and term_out_1..4 in children if not assigned.
        /// </summary>
        public void AutoDiscoverTerminalsIfEmpty()
        {
            if (poles == null) poles = new List<BreakerPole>();
            if (poles.Count > 0 && poles[0].inputTerminal != null) return;

            poles.Clear();
            string[] phaseNames = { "Pole 1 (L1)", "Pole 2 (L2)", "Pole 3 (L3)", "Pole 4 (N)" };

            for (int i = 1; i <= 4; i++)
            {
                Transform inTransform = FindDeepChild(transform, $"term_in_{i}");
                Transform outTransform = FindDeepChild(transform, $"term_out_{i}");

                TerminalPoint inPoint = null;
                TerminalPoint outPoint = null;

                if (inTransform != null)
                {
                    inPoint = inTransform.GetComponent<TerminalPoint>();
                    if (inPoint == null) inPoint = inTransform.gameObject.AddComponent<TerminalPoint>();
                    inPoint.Initialize(this, i - 1, TerminalType.Input, $"IN {i}");
                }

                if (outTransform != null)
                {
                    outPoint = outTransform.GetComponent<TerminalPoint>();
                    if (outPoint == null) outPoint = outTransform.gameObject.AddComponent<TerminalPoint>();
                    outPoint.Initialize(this, i - 1, TerminalType.Output, $"OUT {i}");
                }

                BreakerPole pole = new BreakerPole
                {
                    poleName = phaseNames[i - 1],
                    inputTerminal = inPoint,
                    outputTerminal = outPoint,
                    isClosed = false
                };
                poles.Add(pole);
            }
        }

        private Transform FindDeepChild(Transform parent, string childName)
        {
            foreach (Transform child in parent)
            {
                if (child.name.Equals(childName, StringComparison.OrdinalIgnoreCase))
                    return child;
                Transform found = FindDeepChild(child, childName);
                if (found != null) return found;
            }
            return null;
        }

        private void HandleStateChanged(BreakerState newState)
        {
            bool contactsClosed = (newState == BreakerState.On);
            UpdatePolesConductivity(contactsClosed);
            UpdateVisualIndicator(newState);
        }

        private void UpdatePolesConductivity(bool closed)
        {
            for (int i = 0; i < poles.Count; i++)
            {
                poles[i].isClosed = closed;
            }
        }

        private void UpdateVisualIndicator(BreakerState state)
        {
            if (indicatorMaterialInstance == null) return;

            Color targetColor = state switch
            {
                BreakerState.On => onLiveColor,
                BreakerState.Tripped => tripAlertColor,
                _ => offSafeColor
            };

            indicatorMaterialInstance.color = targetColor;
            indicatorMaterialInstance.SetColor("_BaseColor", targetColor);
            indicatorMaterialInstance.SetColor("_EmissionColor", targetColor * (state == BreakerState.On ? 1.5f : 0.5f));
            indicatorMaterialInstance.EnableKeyword("_EMISSION");
        }

        /// <summary>
        /// Check if a specific pole is conducting power.
        /// </summary>
        public bool IsPoleConducting(int poleIndex)
        {
            if (poleIndex < 0 || poleIndex >= poles.Count) return false;
            return poles[poleIndex].isClosed;
        }

        /// <summary>
        /// Find the opposite paired terminal on this breaker.
        /// </summary>
        public TerminalPoint GetPairedTerminal(TerminalPoint terminal)
        {
            for (int i = 0; i < poles.Count; i++)
            {
                if (poles[i].inputTerminal == terminal) return poles[i].outputTerminal;
                if (poles[i].outputTerminal == terminal) return poles[i].inputTerminal;
            }
            return null;
        }

        /// <summary>
        /// External trigger to trip breaker on overload / short-circuit.
        /// </summary>
        public void TriggerOverloadTrip(string reason = "Overload")
        {
            Debug.LogWarning($"[ContactorController] MCB Tripped! Reason: {reason}");
            if (lever != null)
            {
                lever.Trip();
            }
        }

        public void ToggleBreaker()
        {
            if (lever != null) lever.Toggle();
        }

        public void SwitchOn()
        {
            if (lever != null) lever.TurnOn();
        }

        public void SwitchOff()
        {
            if (lever != null) lever.TurnOff();
        }
    }
}
