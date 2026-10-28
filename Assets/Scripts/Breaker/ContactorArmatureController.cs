using System;
using UnityEngine;
using UnityEngine.Events;
using ContactorAR.Wiring;
using WireSystem;

namespace ContactorAR.Breaker
{
    /// <summary>
    /// Controls the electromagnetic coil and moving armature plunger (CJX2S-090018)
    /// of the 3-Pole Industrial Contactor (3_pole_contactor_final).
    ///
    /// Real Contactor Mechanics:
    /// - Coil ON (Energized): Armature moves to y = 0.0126 (drawn inward by magnetic field)
    ///   - Main power contacts 1-2, 3-4, 5-6 CLOSE
    ///   - Auxiliary NO contact 13-14 CLOSES
    ///   - Auxiliary NC contact 21-22 OPENS
    /// - Coil OFF (Released): Armature returns to y = 0.0069 (pushed outward by return springs)
    ///   - Main power contacts 1-2, 3-4, 5-6 OPEN
    ///   - Auxiliary NO contact 13-14 OPENS
    ///   - Auxiliary NC contact 21-22 CLOSES
    ///
    /// Also supports:
    /// - Manual plunger press (touching/clicking the red plunger in AR or Editor)
    /// - Automatic coil excitation when voltage is present between A1 and A2 terminals
    /// - Realistic magnetic solenoid actuation click/clack sound
    /// </summary>
    [SelectionBase]
    public class ContactorArmatureController : MonoBehaviour
    {
        public const string ARMATURE_NODE_NAME = "CJX2S-090018";
        public const float RELEASED_Y = 0.0069f;
        public const float ENERGIZED_Y = 0.0126f;

        [Header("Armature Plunger (CJX2S-090018)")]
        [Tooltip("The moving coil armature part (CJX2S-090018).")]
        [SerializeField] private Transform armatureTransform;

        [Tooltip("Y position when coil is de-energized / released (0.0069).")]
        [SerializeField] private float releasedY = RELEASED_Y;

        [Tooltip("Y position when coil is energized / pulled in (0.0126).")]
        [SerializeField] private float energizedY = ENERGIZED_Y;

        [Tooltip("Actuation speed (m/s). Electromagnetic contactors snap in under 40ms.")]
        [SerializeField] private float actuationSpeed = 0.15f;

        [Header("Coil Status")]
        [SerializeField] private bool isEnergized = false;
        [SerializeField] private bool manualOverrideHeld = false;
        [SerializeField] private float currentY = RELEASED_Y;

        [Header("Audio")]
        [SerializeField] private AudioSource audioSource;

        [Header("Events")]
        public UnityEvent<bool> OnCoilStateChanged;

        private float targetY = RELEASED_Y;
        private float initialLocalX = 0f;
        private float initialLocalZ = 0f;
        private bool isMoving = false;

        // Terminal references
        private TerminalPoint termA1;
        private TerminalPoint termA2;
        private TerminalPoint termIn1, termOut1;
        private TerminalPoint termIn2, termOut2;
        private TerminalPoint termIn3, termOut3;
        private TerminalPoint termNoU, termNoD;
        private TerminalPoint termNcU, termNcD;

        public bool IsEnergized => isEnergized || manualOverrideHeld;
        public float ReleasedY => releasedY;
        public float EnergizedY => energizedY;
        public float CurrentY => currentY;
        public Transform ArmatureTransform => armatureTransform;

        private void Awake()
        {
            InitializeArmature();
            InitializeAudio();
        }

        private void Start()
        {
            DiscoverContactorTerminals();
            SetCoilStateInstant(isEnergized);
        }

        private void Update()
        {
            if (isMoving)
            {
                currentY = Mathf.MoveTowards(currentY, targetY, actuationSpeed * Time.deltaTime);
                ApplyArmaturePosition(currentY);

                if (Mathf.Approximately(currentY, targetY))
                {
                    currentY = targetY;
                    ApplyArmaturePosition(currentY);
                    isMoving = false;
                }
            }

            CheckCoilElectricalSupply();
        }

        public void InitializeArmature()
        {
            if (armatureTransform == null)
            {
                armatureTransform = ModelAssetLoader.FindDeepChild(transform, ARMATURE_NODE_NAME);
            }

            if (armatureTransform != null)
            {
                Vector3 pos = armatureTransform.localPosition;
                initialLocalX = pos.x;
                initialLocalZ = pos.z;

                // Ensure a BoxCollider exists on the plunger so user can touch/click it
                BoxCollider col = armatureTransform.GetComponent<BoxCollider>();
                if (col == null)
                {
                    col = armatureTransform.gameObject.AddComponent<BoxCollider>();
                    col.size = new Vector3(0.025f, 0.015f, 0.035f);
                }

                currentY = isEnergized ? energizedY : releasedY;
                targetY = currentY;
                ApplyArmaturePosition(currentY);
            }
        }

        private void InitializeAudio()
        {
            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
                if (audioSource == null)
                {
                    audioSource = gameObject.AddComponent<AudioSource>();
                    audioSource.playOnAwake = false;
                    audioSource.spatialBlend = 0.8f;
                }
            }
        }

        public void Configure(Transform armature, float relY = RELEASED_Y, float energY = ENERGIZED_Y)
        {
            armatureTransform = armature;
            releasedY = relY;
            energizedY = energY;
            InitializeArmature();
        }

        /// <summary>
        /// Command the contactor coil to ENERGIZE (snaps CJX2S-090018 inward to y = 0.0126).
        /// Closes main poles and auxiliary NO contacts; opens NC contacts.
        /// </summary>
        public void Energize()
        {
            SetCoilState(true);
        }

        /// <summary>
        /// Command the contactor coil to RELEASE (moves CJX2S-090018 outward to y = 0.0069).
        /// Opens main poles and auxiliary NO contacts; closes NC contacts.
        /// </summary>
        public void Release()
        {
            SetCoilState(false);
        }

        /// <summary>
        /// Toggles contactor coil state.
        /// </summary>
        public void Toggle()
        {
            SetCoilState(!isEnergized);
        }

        public void SetCoilState(bool energized)
        {
            if (isEnergized == energized && !manualOverrideHeld) return;

            isEnergized = energized;
            targetY = isEnergized ? energizedY : releasedY;
            isMoving = true;

            PlaySolenoidSound(isEnergized);
            UpdateContactConductivity(isEnergized);
            OnCoilStateChanged?.Invoke(isEnergized);
        }

        public void SetCoilStateInstant(bool energized)
        {
            isEnergized = energized;
            isMoving = false;
            currentY = isEnergized ? energizedY : releasedY;
            targetY = currentY;
            ApplyArmaturePosition(currentY);
            UpdateContactConductivity(isEnergized);
            OnCoilStateChanged?.Invoke(isEnergized);
        }

        /// <summary>
        /// Simulates manual mechanical press of the red plunger.
        /// </summary>
        public void SetManualOverride(bool held)
        {
            manualOverrideHeld = held;
            bool active = isEnergized || manualOverrideHeld;
            targetY = active ? energizedY : releasedY;
            isMoving = true;
            UpdateContactConductivity(active);
            OnCoilStateChanged?.Invoke(active);
        }

        private void ApplyArmaturePosition(float y)
        {
            if (armatureTransform != null)
            {
                armatureTransform.localPosition = new Vector3(initialLocalX, y, initialLocalZ);
            }
        }

        /// <summary>
        /// Updates the conductivity of all main power contacts, NO aux, and NC aux.
        /// </summary>
        private void UpdateContactConductivity(bool coilActive)
        {
            // Main 3 Poles: Closed when coil is active, Open when released
            SetPoleConductivity(termIn1, termOut1, coilActive);
            SetPoleConductivity(termIn2, termOut2, coilActive);
            SetPoleConductivity(termIn3, termOut3, coilActive);

            // Auxiliary NO (13-14): Closed when coil active, Open when released
            SetPoleConductivity(termNoU, termNoD, coilActive);

            // Auxiliary NC (21-22): Open when coil active, Closed when released
            SetPoleConductivity(termNcU, termNcD, !coilActive);
        }

        private void SetPoleConductivity(TerminalPoint inTerm, TerminalPoint outTerm, bool conductive)
        {
            if (inTerm == null || outTerm == null) return;

            if (conductive)
            {
                // Pass through: out mirrors in's electrical state
                if (inTerm.IsEnergized)
                    outTerm.SetElectricalState(true, inTerm.Voltage, inTerm.PhaseTag);
                else if (outTerm.IsEnergized)
                    inTerm.SetElectricalState(true, outTerm.Voltage, outTerm.PhaseTag);
                // else both stay as-is (waiting for external circuit update)
            }
            else
            {
                // Open circuit: neither side can pass through this contact pair
                // We only clear if the source of that voltage is THIS contact pair
                // (don't clear externally supplied voltages on input terminals)
                if (!outTerm.IsEnergized) return; // already off
                // Only clear output if it seems to be fed through this contact (not directly wired)
                if (outTerm.ConnectedEnd == null)
                    outTerm.SetElectricalState(false, 0f, "");
            }
        }

        /// <summary>
        /// Checks if electrical voltage is present on coil terminals A1 and A2 via wired circuit.
        /// </summary>
        private void CheckCoilElectricalSupply()
        {
            if (termA1 != null && termA2 != null)
            {
                // If A1 has live voltage (>= 110V) and A2 is connected (neutral/ground reference)
                bool coilElectricallyPowered = termA1.IsEnergized && Mathf.Abs(termA1.Voltage - termA2.Voltage) > 100f;
                if (coilElectricallyPowered != isEnergized && !manualOverrideHeld)
                {
                    SetCoilState(coilElectricallyPowered);
                }
            }
        }

        /// <summary>
        /// Finds all 12 contactor terminals by name under this GameObject.
        /// </summary>
        public void DiscoverContactorTerminals()
        {
            termIn1  = FindTerminal("term_in_1");
            termIn2  = FindTerminal("term_in_2");
            termIn3  = FindTerminal("term_in_3");
            termOut1 = FindTerminal("term_out_1");
            termOut2 = FindTerminal("term_out_2");
            termOut3 = FindTerminal("term_out_3");

            termA1   = FindTerminal("term_in_A1");
            termA2   = FindTerminal("term_in_A2");

            termNoU  = FindTerminal("term_no_u");
            termNoD  = FindTerminal("term_no_d");
            termNcU  = FindTerminal("term_nc_u");
            termNcD  = FindTerminal("term_nc_d");
        }

        private TerminalPoint FindTerminal(string nodeName)
        {
            Transform t = ModelAssetLoader.FindDeepChild(transform, nodeName);
            return t != null ? t.GetComponent<TerminalPoint>() : null;
        }

        private void PlaySolenoidSound(bool energized)
        {
            if (audioSource == null) return;

            audioSource.pitch = energized ? 1.0f : 0.85f;
            audioSource.PlayOneShot(GetContactorClickClip(energized));
        }

        private static AudioClip contactorClackClip;
        private static AudioClip contactorReleaseClip;

        private AudioClip GetContactorClickClip(bool energized)
        {
            if (energized)
            {
                if (contactorClackClip == null)
                    contactorClackClip = CreateSyntheticAudioClip("ContactorPullInClack", 180, 0.09f, 0.9f);
                return contactorClackClip;
            }
            else
            {
                if (contactorReleaseClip == null)
                    contactorReleaseClip = CreateSyntheticAudioClip("ContactorReleaseSnap", 280, 0.06f, 0.6f);
                return contactorReleaseClip;
            }
        }

        private static AudioClip CreateSyntheticAudioClip(string name, float freq, float duration, float strength)
        {
            int sampleRate = 44100;
            int sampleCount = Mathf.CeilToInt(sampleRate * duration);
            float[] samples = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                float t = (float)i / sampleRate;
                float envelope = Mathf.Exp(-t * 40.0f);
                samples[i] = Mathf.Sin(2 * Mathf.PI * freq * t) * envelope * strength;
                samples[i] += (UnityEngine.Random.value * 2f - 1f) * envelope * 0.4f * strength;
            }

            AudioClip clip = AudioClip.Create(name, sampleCount, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}

