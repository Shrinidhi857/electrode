using System;
using UnityEngine;
using ContactorAR.AR;
using ContactorAR.SelectionSystem;

namespace ContactorAR.Interaction
{
    /// <summary>
    /// Restricts all model rotation to Vector3.up (world Y axis) ONLY.
    /// Locks X and Z euler angles strictly to 0 at all times.
    /// Supports two-finger twist gestures, quick 90° buttons, and 15° angle snapping.
    /// </summary>
    public class ModelRotationController : MonoBehaviour
    {
        public static ModelRotationController Instance { get; private set; }

        [Header("Settings")]
        [Tooltip("If true, snaps Y rotation to nearest 15-degree step on gesture end.")]
        [SerializeField] private bool snapTo15Degrees = true;

        [Tooltip("Sensitivity multiplier for two-finger twist.")]
        [SerializeField] private float twistSensitivity = 1.6f;

        private float previousTwistAngle = 0f;
        private bool isTwisting = false;

        public static bool SnapTo15Degrees
        {
            get => Instance != null && Instance.snapTo15Degrees;
            set { if (Instance != null) Instance.snapTo15Degrees = value; }
        }

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else if (Instance != this) Destroy(this);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void LateUpdate()
        {
            // Global safeguard: clamp all active models to upright Y rotation (0 X, 0 Z) every frame
            var all = ARModelManipulator.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] == null) continue;
                Vector3 euler = all[i].transform.eulerAngles;
                if (!Mathf.Approximately(euler.x, 0f) || !Mathf.Approximately(euler.z, 0f))
                {
                    all[i].transform.eulerAngles = new Vector3(0f, euler.y, 0f);
                }
            }
        }

        /// <summary>
        /// Rotates the target model around world Y by the given delta degrees, locking X and Z to 0.
        /// </summary>
        public static void RotateModelY(ARModelManipulator target, float degrees)
        {
            if (target == null) return;

            target.transform.Rotate(Vector3.up, degrees, Space.World);

            Vector3 euler = target.transform.eulerAngles;
            euler.x = 0f;
            euler.z = 0f;
            target.transform.eulerAngles = euler;
        }

        /// <summary>
        /// Snaps target's Y rotation to the nearest 15° or 90° increment.
        /// </summary>
        public static void SnapRotation(ARModelManipulator target, float step = 15f)
        {
            if (target == null) return;
            Vector3 euler = target.transform.eulerAngles;
            euler.y = Mathf.Round(euler.y / step) * step;
            euler.x = 0f;
            euler.z = 0f;
            target.transform.eulerAngles = euler;
        }
    }
}
