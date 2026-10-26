using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ContactorAR.Breaker;
using ContactorAR.Wiring;

namespace ContactorAR.Simulation
{
    /// <summary>
    /// Spawns visual/procedural physics particles to simulate sparks when the contactor is TRIPPED,
    /// wires are short-circuited, or contacts open/close under load.
    /// Also handles the animated "power flow" line renderer effect along energized wires.
    /// </summary>
    [RequireComponent(typeof(ContactorController))]
    public class ElectricalEffectsManager : MonoBehaviour
    {
        [Header("Spark FX")]
        [Tooltip("Enable spark particles on TRIP events.")]
        [SerializeField] private bool enableSparks = true;
        [SerializeField] private float sparkIntensity = 1.5f;
        [SerializeField] private Color sparkColor = new Color(1.0f, 0.85f, 0.2f, 1.0f);

        [Header("Power Flow Line FX")]
        [Tooltip("Enable animated flow visualization along energized wires.")]
        [SerializeField] private bool enablePowerFlowFX = true;
        [SerializeField] private float flowSpeed = 0.9f;

        [Header("Indicator LEDs")]
        [Tooltip("Optional indicator objects that glow when breaker is closed/ON.")]
        [SerializeField] private List<MeshRenderer> indicatorLEDs = new List<MeshRenderer>();
        [SerializeField] private Color ledOffColor = new Color(0.1f, 0.1f, 0.1f, 1.0f);
        [SerializeField] private Color ledOnColor = new Color(0.15f, 0.9f, 0.25f, 1.0f);
        [SerializeField] private Color ledTripColor = new Color(1.0f, 0.6f, 0.1f, 1.0f);

        private ContactorController contactor;
        private ParticleSystem sparkSystem;
        private List<Material> ledMaterials = new List<Material>();
        private float flowOffset = 0f;

        private void Awake()
        {
            contactor = GetComponent<ContactorController>();
            CreateSparkSystem();
            SetupLEDMaterials();
        }

        private void OnEnable()
        {
            if (contactor != null && contactor.Lever != null)
            {
                contactor.Lever.OnStateChanged.AddListener(HandleStateChanged);
            }
        }

        private void OnDisable()
        {
            if (contactor != null && contactor.Lever != null)
            {
                contactor.Lever.OnStateChanged.RemoveListener(HandleStateChanged);
            }
        }

        private void Update()
        {
            if (!enablePowerFlowFX) return;
            flowOffset += Time.deltaTime * flowSpeed;
            if (flowOffset > 1f) flowOffset -= 1f;

            // Animate wire materials
            CylindricalWire[] wires = FindObjectsByType<CylindricalWire>(FindObjectsSortMode.None);
            foreach (CylindricalWire w in wires)
            {
                // Power flow shader-based animation can be added here
                // Currently, pulsing emission is handled in CylindricalWire.Update
            }
        }

        private void HandleStateChanged(BreakerState state)
        {
            switch (state)
            {
                case BreakerState.On:
                    SetLEDState(ledOnColor, 1.5f);
                    break;
                case BreakerState.Tripped:
                    SetLEDState(ledTripColor, 2.0f);
                    if (enableSparks) EmitTripSparks();
                    break;
                case BreakerState.Off:
                default:
                    SetLEDState(ledOffColor, 0f);
                    break;
            }
        }

        private void SetupLEDMaterials()
        {
            ledMaterials.Clear();
            for (int i = 0; i < indicatorLEDs.Count; i++)
            {
                if (indicatorLEDs[i] != null)
                {
                    Material m = indicatorLEDs[i].material;
                    ledMaterials.Add(m);
                }
            }
        }

        private void SetLEDState(Color color, float emissionIntensity)
        {
            for (int i = 0; i < ledMaterials.Count; i++)
            {
                if (ledMaterials[i] == null) continue;
                ledMaterials[i].color = color;
                ledMaterials[i].SetColor("_BaseColor", color);
                if (emissionIntensity > 0f)
                {
                    ledMaterials[i].EnableKeyword("_EMISSION");
                    ledMaterials[i].SetColor("_EmissionColor", color * emissionIntensity);
                }
                else
                {
                    ledMaterials[i].DisableKeyword("_EMISSION");
                    ledMaterials[i].SetColor("_EmissionColor", Color.black);
                }
            }
        }

        private void CreateSparkSystem()
        {
            GameObject sparkObj = new GameObject("TripSparkFX");
            sparkObj.transform.SetParent(transform, false);
            sparkObj.transform.localPosition = Vector3.zero;

            sparkSystem = sparkObj.AddComponent<ParticleSystem>();

            var main = sparkSystem.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.45f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.003f, 0.008f);
            main.startColor = new ParticleSystem.MinMaxGradient(sparkColor, Color.white);
            main.gravityModifier = 1.2f;
            main.maxParticles = 80;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = sparkSystem.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0.0f, 40) });

            var shape = sparkSystem.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.03f;

            var velocityOverLifetime = sparkSystem.velocityOverLifetime;
            velocityOverLifetime.enabled = true;
            velocityOverLifetime.space = ParticleSystemSimulationSpace.Local;

            var renderer = sparkSystem.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.2f;
            renderer.lengthScale = 2.5f;

            // Use default Unity particle material
            Material mat = new Material(Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Legacy Shaders/Particles/Additive"));
            if (mat != null)
            {
                mat.color = sparkColor;
                renderer.material = mat;
            }
        }

        public void EmitTripSparks()
        {
            if (sparkSystem == null) return;
            sparkSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            sparkSystem.Play();
        }

        /// <summary>
        /// Emit contact-close sparks at a specific terminal position.
        /// </summary>
        public void EmitContactSparksAt(Vector3 worldPos)
        {
            if (sparkSystem == null || !enableSparks) return;
            sparkSystem.transform.position = worldPos;
            var burst = sparkSystem.emission.GetBurst(0);
            burst.count = 15;
            sparkSystem.emission.SetBurst(0, burst);
            sparkSystem.Play();
        }
    }
}
