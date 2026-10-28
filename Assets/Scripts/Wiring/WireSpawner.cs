using System.Collections.Generic;
using UnityEngine;

namespace ContactorAR.Wiring
{
    [System.Serializable]
    public struct WireColorPreset
    {
        public string name;
        public Color color;
        public string phase;
    }

    /// <summary>
    /// Spawns and manages cylindrical wires in the AR scene.
    /// Supports preset electrical phase colorings (L1, L2, L3, Neutral, Earth)
    /// and clean resets.
    /// </summary>
    public class WireSpawner : MonoBehaviour
    {
        [Header("Spawn Settings")]
        [Tooltip("Spawn offset in front of camera or contactor.")]
        [SerializeField] private Vector3 spawnOffset = new Vector3(0f, -0.15f, 0.4f);

        [Header("Color Presets")]
        [SerializeField] private List<WireColorPreset> presets = new List<WireColorPreset>
        {
            new WireColorPreset { name = "Phase L1 (Red)", color = new Color(0.9f, 0.15f, 0.15f, 1f), phase = "L1" },
            new WireColorPreset { name = "Phase L2 (Yellow)", color = new Color(0.95f, 0.85f, 0.1f, 1f), phase = "L2" },
            new WireColorPreset { name = "Phase L3 (Blue)", color = new Color(0.15f, 0.45f, 0.95f, 1f), phase = "L3" },
            new WireColorPreset { name = "Neutral N (Black)", color = new Color(0.15f, 0.15f, 0.18f, 1f), phase = "N" }
        };

        private List<CylindricalWire> spawnedWires = new List<CylindricalWire>();
        private int currentPresetIndex = 0;

        public List<CylindricalWire> SpawnedWires => spawnedWires;

        /// <summary>
        /// Spawns a new wire using the current color preset.
        /// </summary>
        public CylindricalWire SpawnNextWire()
        {
            WireColorPreset preset = presets[currentPresetIndex % presets.Count];
            currentPresetIndex++;
            return SpawnWire(preset.color, preset.phase, preset.name);
        }

        public CylindricalWire SpawnWire(Color color, string phase, string wireName = "CylindricalWire")
        {
            Camera cam = Camera.main;
            Vector3 spawnOrigin = cam != null 
                ? cam.transform.position + cam.transform.forward * spawnOffset.z + cam.transform.up * spawnOffset.y + cam.transform.right * spawnOffset.x
                : transform.position + spawnOffset;

            GameObject wireObj = new GameObject(wireName);
            wireObj.transform.position = spawnOrigin;

            // Keep wire under this WireSpawner/CircuitSimulator root object at scale (1,1,1)
            // NEVER parent to the contactor directly to avoid inheriting 0.008x model scale
            wireObj.transform.SetParent(transform, true);

            CylindricalWire wire = wireObj.AddComponent<CylindricalWire>();
            wire.EnsureConnectorEnds();

            // If an unoccupied terminal exists, auto-attach EndA to it for instant snap workflow
            TerminalPoint openTerminal = null;
            TerminalPoint[] allTerminals = FindObjectsByType<TerminalPoint>(FindObjectsSortMode.None);
            for (int i = 0; i < allTerminals.Length; i++)
            {
                if (!allTerminals[i].IsOccupied)
                {
                    openTerminal = allTerminals[i];
                    break;
                }
            }

            if (openTerminal != null)
            {
                wire.EndA.SnapToTerminal(openTerminal);
                // Position EndB slightly outward along pipeline exit direction ready to drag
                wire.EndB.transform.position = openTerminal.SnapPosition + openTerminal.OutwardDirection * 0.12f + Vector3.forward * 0.05f;
            }
            else
            {
                Vector3 spread = cam != null ? cam.transform.right * 0.12f : Vector3.right * 0.12f;
                wire.EndA.transform.position = spawnOrigin - spread;
                wire.EndB.transform.position = spawnOrigin + spread;
            }

            wire.Setup(wire.EndA, wire.EndB, color, phase);
            spawnedWires.Add(wire);

            Debug.Log($"[WireSpawner] Spawned new wire: {wireName} ({phase})");
            return wire;
        }

        /// <summary>
        /// Removes all spawned wires from the scene.
        /// </summary>
        public void ClearAllWires()
        {
            for (int i = 0; i < spawnedWires.Count; i++)
            {
                if (spawnedWires[i] != null)
                {
                    Destroy(spawnedWires[i].gameObject);
                }
            }
            spawnedWires.Clear();
            currentPresetIndex = 0;
            Debug.Log("[WireSpawner] All wires cleared.");
        }

        /// <summary>
        /// Quick preset helper: Spawns 4 wires ready for 4-pole connections.
        /// </summary>
        public void SpawnFull4PoleSet()
        {
            ClearAllWires();
            for (int i = 0; i < presets.Count; i++)
            {
                SpawnWire(presets[i].color, presets[i].phase, presets[i].name);
            }
        }
    }
}
