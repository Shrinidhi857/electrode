using UnityEngine;

namespace WireSystem
{
    /// <summary>
    /// Global configuration and styling rules for the interactive wire system.
    /// ScriptableObject asset: Tools > Wire System > Create Wire Settings.
    /// </summary>
    [CreateAssetMenu(fileName = "WireSettings", menuName = "Wire System/Wire Settings")]
    public class WireSettings : ScriptableObject
    {
        [Header("Line Appearance")]
        [Tooltip("Thickness of the wire line in world meters. Kept sleek and thin (0.0025m = 2.5mm).")]
        [Range(0.001f, 0.015f)]
        public float lineWidth = 0.0025f;

        [Tooltip("If true, scales wire thickness with camera distance to preserve constant screen-space readability.")]
        public bool constantScreenSpaceThickness = false;

        [Tooltip("Target screen-space thickness in pixels when constantScreenSpaceThickness is enabled.")]
        public float screenSpacePixelWidth = 3.5f;

        [Tooltip("Palette of distinct wire colors assigned to new connections.")]
        public Color[] wireColorPalette = new Color[]
        {
            new Color(0.92f, 0.22f, 0.22f, 1f), // Phase L1 - Red
            new Color(0.95f, 0.85f, 0.15f, 1f), // Phase L2 - Yellow
            new Color(0.20f, 0.55f, 0.95f, 1f), // Phase L3 - Blue
            new Color(0.22f, 0.22f, 0.25f, 1f), // Neutral - Dark Grey
            new Color(0.25f, 0.85f, 0.35f, 1f)  // Earth - Green
        };

        [Tooltip("Opacity multiplier applied to the wire while it is being actively dragged / unconnected.")]
        [Range(0.2f, 0.9f)]
        public float draggingOpacity = 0.55f;

        [Header("Snap Configuration")]
        [Tooltip("Distance in world meters to magnetically jump onto a candidate terminal.")]
        public float snapInRadius = 0.04f;

        [Tooltip("Hysteresis multiplier. An attached endpoint must move past snapInRadius * multiplier to unsnap.")]
        public float snapOutRadiusMultiplier = 1.35f;

        [Tooltip("Time in seconds to smoothly animate magnetic snap onto terminal anchor.")]
        public float snapLerpDuration = 0.08f;

        [Header("Orthogonal Pipeline Routing")]
        [Tooltip("Distance in meters the wire travels straight out of a terminal before taking its first 90-degree turn.")]
        public float stubLength = 0.02f;

        [Tooltip("If true, rounds 90-degree elbows with small arc fillets. If false, leaves sharp corners.")]
        public bool roundCorners = false;

        [Tooltip("Radius of the rounded fillet when roundCorners is true.")]
        public float cornerRadius = 0.004f;

        [Tooltip("Lateral offset applied to parallel wires between the same terminal rows to prevent overlapping.")]
        public float laneOffset = 0.006f;

        [Header("Handles & Colliders")]
        [Tooltip("Visual radius of the small endpoint sphere sit on the terminal.")]
        public float handleVisibleRadius = 0.004f;

        [Tooltip("Touch/click interaction radius for the endpoint sphere (easy to hit on mobile screen).")]
        public float handleColliderRadius = 0.018f;

        [Header("Validation Rules")]
        [Tooltip("If true, endpoints cannot connect to the terminal they originated from.")]
        public bool preventSelfConnection = true;

        [Tooltip("If true, prevents multiple wires connecting the exact same pair of terminals.")]
        public bool preventDuplicateConnection = true;

        [Tooltip("If true, Input terminals may only connect to Output terminals, and vice-versa.")]
        public bool inputMustConnectToOutput = false;

        [Tooltip("If true, dropping an edited handle in empty space reverts to its previous terminal instead of deleting.")]
        public bool revertOnInvalidDrop = false;

        [Header("Material References")]
        [Tooltip("Unlit material assigned to LineRenderers (auto-resolved if null).")]
        public Material unlitLineMaterial;
    }
}
