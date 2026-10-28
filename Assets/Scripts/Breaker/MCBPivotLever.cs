using System;
using UnityEngine;
using UnityEngine.Events;

namespace ContactorAR.Breaker
{
    public enum BreakerState
    {
        Off,
        On,
        Tripped
    }

    public enum RotationAxis
    {
        X,
        Y,
        Z
    }

    /// <summary>
    /// Controls the physical lever/pivot (mcb_pivot) of the breaker/contactor.
    /// Supports exact angle rotation between -1.4° (OFF) and 68.0° (ON), 
    /// smooth transitions, trip mechanism, touch/drag manipulation, and event reporting.
    /// </summary>
    [SelectionBase]
    public class MCBPivotLever : MonoBehaviour
    {
        [Header("Pivot Configuration")]
        [Tooltip("The Transform that physically rotates (e.g. mcb_pivot). Defaults to this transform if null.")]
        [SerializeField] private Transform pivotTransform;

        [Tooltip("Local rotation axis to rotate around.")]
        [SerializeField] private RotationAxis rotationAxis = RotationAxis.X;

        [Tooltip("Invert rotation direction if model pivot is oriented oppositely.")]
        [SerializeField] private bool invertAxis = false;

        [Header("Angle Limits (Degrees)")]
        [Tooltip("Angle corresponding to OFF state (-70.0 deg).")]
        [SerializeField] private float offAngle = -70.0f;

        [Tooltip("Angle corresponding to ON state (-160.0 deg).")]
        [SerializeField] private float onAngle = -160.0f;

        [Tooltip("Angle corresponding to TRIPPED state (middle position -115.0 deg).")]
        [SerializeField] private float tripAngle = -115.0f;

        [Header("Motion Settings")]
        [Tooltip("Speed when smoothly moving to a commanded state.")]
        [SerializeField] private float transitionSpeed = 350.0f;

        [Tooltip("Audio source to play click/clack/trip sounds.")]
        [SerializeField] private AudioSource audioSource;

        [Header("State")]
        [SerializeField] private BreakerState currentState = BreakerState.Off;
        [SerializeField] private float currentAngle = -70.0f;

        [Header("Events")]
        public UnityEvent<BreakerState> OnStateChanged;
        public UnityEvent<float, float> OnAngleChanged; // currentAngle, normalized (0 = off, 1 = on)

        private float targetAngle = -70.0f;
        private bool isAnimating = false;
        private bool isDragging = false;
        private Vector3 dragStartHit;
        private float dragStartAngle;
        private Camera mainCamera;
        private float baseEulerX = 0f;
        private float baseEulerY = 0f;
        private float baseEulerZ = 0f;
        private bool initialized = false;

        public float OffAngle => offAngle;
        public float OnAngle => onAngle;
        public float TripAngle => tripAngle;
        public float CurrentAngle => currentAngle;
        public BreakerState CurrentState => currentState;
        public bool IsOn => currentState == BreakerState.On;
        public bool IsTripped => currentState == BreakerState.Tripped;

        /// <summary>
        /// Programmatically configure the lever parameters.
        /// </summary>
        public void Configure(Transform pivot, float on, float off, float trip, RotationAxis axis = RotationAxis.X)
        {
            pivotTransform = pivot;
            onAngle = on;
            offAngle = off;
            tripAngle = trip;
            rotationAxis = axis;

            if (pivotTransform != null)
            {
                Vector3 euler = pivotTransform.localEulerAngles;
                baseEulerX = euler.x;
                baseEulerY = euler.y;
                baseEulerZ = euler.z;
            }

            initialized = true;
            currentAngle = offAngle;
            targetAngle = offAngle;
            ApplyAngle(currentAngle);
        }

        private void Awake()
        {
            InitIfNeeded();
        }

        private void InitIfNeeded()
        {
            if (initialized) return;
            initialized = true;

            if (pivotTransform == null)
            {
                pivotTransform = FindDeepChild(transform, "mcb_pivot");
                if (pivotTransform == null) pivotTransform = transform;
            }

            if (pivotTransform != null)
            {
                Vector3 euler = pivotTransform.localEulerAngles;
                baseEulerX = euler.x;
                baseEulerY = euler.y;
                baseEulerZ = euler.z;
            }

            currentAngle = offAngle;
            targetAngle = offAngle;
            ApplyAngle(currentAngle);

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

        private static Transform FindDeepChild(Transform parent, string childName)
        {
            if (parent == null || string.IsNullOrEmpty(childName)) return null;
            foreach (Transform child in parent)
            {
                if (child.name.Equals(childName, StringComparison.OrdinalIgnoreCase))
                    return child;
                Transform found = FindDeepChild(child, childName);
                if (found != null) return found;
            }
            return null;
        }

        private void Start()
        {
            mainCamera = Camera.main;
            SetStateInstant(currentState);
        }

        private void Update()
        {
            if (isAnimating)
            {
                currentAngle = Mathf.MoveTowards(currentAngle, targetAngle, transitionSpeed * Time.deltaTime);
                ApplyAngle(currentAngle);

                float normalized = Mathf.InverseLerp(offAngle, onAngle, currentAngle);
                OnAngleChanged?.Invoke(currentAngle, normalized);

                if (Mathf.Approximately(currentAngle, targetAngle))
                {
                    currentAngle = targetAngle;
                    ApplyAngle(currentAngle);
                    isAnimating = false;
                }
            }
        }

        /// <summary>
        /// Command the breaker to turn ON (rotates to 68 degrees).
        /// </summary>
        public void TurnOn()
        {
            SetTargetState(BreakerState.On, onAngle);
        }

        /// <summary>
        /// Command the breaker to turn OFF (rotates to -1.4 degrees).
        /// </summary>
        public void TurnOff()
        {
            SetTargetState(BreakerState.Off, offAngle);
        }

        /// <summary>
        /// Command the breaker to TRIP (rotates to trip angle, e.g. 33.3 degrees).
        /// </summary>
        public void Trip()
        {
            SetTargetState(BreakerState.Tripped, tripAngle);
        }

        /// <summary>
        /// Toggles between ON and OFF states.
        /// </summary>
        public void Toggle()
        {
            if (currentState == BreakerState.On)
            {
                TurnOff();
            }
            else
            {
                TurnOn();
            }
        }

        private void SetTargetState(BreakerState newState, float angle)
        {
            targetAngle = angle;
            isAnimating = true;

            if (currentState != newState)
            {
                currentState = newState;
                PlaySoundEffect(newState);
                OnStateChanged?.Invoke(currentState);
            }
        }

        public void SetStateInstant(BreakerState state)
        {
            currentState = state;
            isAnimating = false;
            switch (state)
            {
                case BreakerState.On:
                    currentAngle = onAngle;
                    break;
                case BreakerState.Tripped:
                    currentAngle = tripAngle;
                    break;
                case BreakerState.Off:
                default:
                    currentAngle = offAngle;
                    break;
            }
            targetAngle = currentAngle;
            ApplyAngle(currentAngle);

            float normalized = Mathf.InverseLerp(offAngle, onAngle, currentAngle);
            OnAngleChanged?.Invoke(currentAngle, normalized);
            OnStateChanged?.Invoke(currentState);
        }

        /// <summary>
        /// Sets a custom normalized position (0.0 = OFF, 1.0 = ON).
        /// </summary>
        public void SetNormalizedAngle(float t)
        {
            t = Mathf.Clamp01(t);
            float angle = Mathf.Lerp(offAngle, onAngle, t);
            SetAngleManual(angle);
        }

        public void SetAngleManual(float angle)
        {
            float min = Mathf.Min(offAngle, onAngle);
            float max = Mathf.Max(offAngle, onAngle);
            currentAngle = Mathf.Clamp(angle, min, max);
            isAnimating = false;
            ApplyAngle(currentAngle);

            float normalized = Mathf.InverseLerp(offAngle, onAngle, currentAngle);
            OnAngleChanged?.Invoke(currentAngle, normalized);

            // Determine closest state
            BreakerState resolvedState = currentState;
            if (Mathf.Abs(currentAngle - onAngle) < 5.0f)
            {
                resolvedState = BreakerState.On;
            }
            else if (Mathf.Abs(currentAngle - offAngle) < 5.0f)
            {
                resolvedState = BreakerState.Off;
            }
            else if (Mathf.Abs(currentAngle - tripAngle) < 10.0f)
            {
                resolvedState = BreakerState.Tripped;
            }

            if (resolvedState != currentState)
            {
                currentState = resolvedState;
                OnStateChanged?.Invoke(currentState);
            }
        }

        private void ApplyAngle(float angle)
        {
            if (pivotTransform == null) return;

            float actualAngle = invertAxis ? -angle : angle;
            switch (rotationAxis)
            {
                case RotationAxis.X:
                    pivotTransform.localRotation = Quaternion.Euler(actualAngle, baseEulerY, baseEulerZ);
                    break;
                case RotationAxis.Y:
                    pivotTransform.localRotation = Quaternion.Euler(baseEulerX, actualAngle, baseEulerZ);
                    break;
                case RotationAxis.Z:
                    pivotTransform.localRotation = Quaternion.Euler(baseEulerX, baseEulerY, actualAngle);
                    break;
            }
        }

        private void PlaySoundEffect(BreakerState state)
        {
            if (audioSource == null) return;

            // Generate procedural click tone if no audio clip is assigned
            audioSource.pitch = state switch
            {
                BreakerState.On => 1.2f,
                BreakerState.Off => 0.9f,
                BreakerState.Tripped => 1.5f,
                _ => 1.0f
            };
            audioSource.PlayOneShot(GetDefaultClickClip(state));
        }

        private static AudioClip defaultClickClip;
        private static AudioClip defaultTripClip;

        private AudioClip GetDefaultClickClip(BreakerState state)
        {
            if (state == BreakerState.Tripped)
            {
                if (defaultTripClip == null)
                    defaultTripClip = CreateSyntheticAudioClip("TripSnap", 220, 0.12f);
                return defaultTripClip;
            }

            if (defaultClickClip == null)
                defaultClickClip = CreateSyntheticAudioClip("ClickSnap", 380, 0.08f);
            return defaultClickClip;
        }

        private static AudioClip CreateSyntheticAudioClip(string name, float freq, float duration)
        {
            int sampleRate = 44100;
            int sampleCount = Mathf.CeilToInt(sampleRate * duration);
            float[] samples = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                float t = (float)i / sampleRate;
                float envelope = Mathf.Exp(-t * 35.0f); // snappy transient decay
                samples[i] = Mathf.Sin(2 * Mathf.PI * freq * t) * envelope * 0.8f;
                // Add some snap noise
                samples[i] += (UnityEngine.Random.value * 2f - 1f) * envelope * 0.3f;
            }

            AudioClip clip = AudioClip.Create(name, sampleCount, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        #region Touch / Drag Interaction
        /// <summary>
        /// Called when touch/pinch/mouse starts interacting with the lever.
        /// </summary>
        public void StartDragging(Vector3 worldPoint)
        {
            isDragging = true;
            isAnimating = false;
            dragStartHit = worldPoint;
            dragStartAngle = currentAngle;
        }

        public void DragLever(Vector3 currentWorldPoint)
        {
            if (!isDragging) return;

            // Project movement onto lever plane
            Vector3 delta = currentWorldPoint - dragStartHit;
            float dragDistance = Vector3.Dot(delta, transform.up);
            // Convert vertical drag distance to angle (dragging UP moves towards ON)
            float sign = (onAngle > offAngle) ? 1.0f : -1.0f;
            float angleDelta = dragDistance * 300.0f * sign;
            SetAngleManual(dragStartAngle + angleDelta);
        }

        public void EndDragging()
        {
            if (!isDragging) return;
            isDragging = false;

            // Snap to closest valid state upon release
            float distToOn = Mathf.Abs(currentAngle - onAngle);
            float distToOff = Mathf.Abs(currentAngle - offAngle);
            float distToTrip = Mathf.Abs(currentAngle - tripAngle);

            if (distToOn < distToOff && distToOn < distToTrip)
            {
                TurnOn();
            }
            else if (distToTrip < distToOff)
            {
                Trip();
            }
            else
            {
                TurnOff();
            }
        }
        #endregion
    }
}
