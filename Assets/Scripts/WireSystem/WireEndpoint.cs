using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace WireSystem
{
    /// <summary>
    /// Represents an interactive draggable endpoint handle at either end of a Wire (Start or End).
    /// Features magnetic snapping to Terminals with hysteresis, visual feedback,
    /// a small visible bead with an enlarged touch collider, and lerped snap attachment.
    /// </summary>
    [RequireComponent(typeof(SphereCollider))]
    public class WireEndpoint : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Wire parentWire;
        [SerializeField] private bool isStartEndpoint = true;

        [Header("State")]
        [SerializeField] private Terminal attachedTerminal;
        [SerializeField] private bool isDragging = false;
        [SerializeField] private bool isSnapped = false;

        private SphereCollider interactionCollider;
        private Renderer visualRenderer;
        private Material matInstance;
        private Vector3 currentWorldPosition;
        private Vector3 exitNormal = Vector3.up;
        private Terminal candidateTerminal;
        private Coroutine snapLerpCoroutine;

        public Wire ParentWire => parentWire;
        public bool IsStartEndpoint => isStartEndpoint;
        public Terminal AttachedTerminal => attachedTerminal;
        public bool IsConnected => attachedTerminal != null;
        public bool IsDragging => isDragging;
        public bool IsSnapped => isSnapped;
        public Vector3 Position => currentWorldPosition;
        public Vector3 ExitNormal => exitNormal;

        public event Action<WireEndpoint, Terminal> OnSnappedToTerminal;
        public event Action<WireEndpoint, Terminal> OnUnsnappedFromTerminal;

        private void Awake()
        {
            currentWorldPosition = transform.position;
            EnsureComponents();
        }

        public void Initialize(Wire wire, bool isStart, WireSettings settings)
        {
            parentWire = wire;
            isStartEndpoint = isStart;
            EnsureComponents();
            ApplySettings(settings);
        }

        private void EnsureComponents()
        {
            if (interactionCollider == null) interactionCollider = GetComponent<SphereCollider>();
            if (interactionCollider == null) interactionCollider = gameObject.AddComponent<SphereCollider>();

            interactionCollider.isTrigger = true;

            // Assign WireHandle layer (Layer 7 if registered)
            int handleLayer = LayerMask.NameToLayer("WireHandle");
            if (handleLayer != -1) gameObject.layer = handleLayer;

            // Find or create visible sphere
            Transform visual = transform.Find("VisualSphere");
            if (visual == null)
            {
                GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.name = "VisualSphere";
                sphere.transform.SetParent(transform, false);
                sphere.transform.localPosition = Vector3.zero;

                // Remove primitive collider
                Collider c = sphere.GetComponent<Collider>();
                if (c != null) DestroyImmediate(c);

                visual = sphere.transform;
            }

            visualRenderer = visual.GetComponent<Renderer>();
            if (visualRenderer != null)
            {
                Shader unlit = Shader.Find("Universal Render Pipeline/Unlit")
                            ?? Shader.Find("Sprites/Default")
                            ?? Shader.Find("Unlit/Color");
                if (unlit != null)
                {
                    matInstance = new Material(unlit);
                    visualRenderer.sharedMaterial = matInstance;
                }
            }
        }

        public void ApplySettings(WireSettings settings)
        {
            if (settings == null) return;

            // Configure visible sphere (small: ~0.004m radius = 0.008m diameter)
            Transform visual = transform.Find("VisualSphere");
            if (visual != null)
            {
                visual.localScale = Vector3.one * (settings.handleVisibleRadius * 2.0f);
            }

            // Configure generous touch interaction collider (~0.018m radius = 0.036m diameter)
            if (interactionCollider != null)
            {
                float parentScale = Mathf.Max(transform.lossyScale.x, 0.001f);
                interactionCollider.radius = settings.handleColliderRadius / parentScale;
            }
        }

        public void SetVisualColor(Color color, float alpha = 1.0f)
        {
            if (matInstance != null)
            {
                Color c = color;
                c.a = alpha;
                matInstance.color = c;
            }
        }

        #region Drag & Snap Handling
        public void StartDragging()
        {
            isDragging = true;
            if (attachedTerminal != null)
            {
                Terminal prev = attachedTerminal;
                attachedTerminal = null;
                isSnapped = false;
                prev.RemoveWire(parentWire);
                prev.Highlight(false);
                OnUnsnappedFromTerminal?.Invoke(this, prev);
            }

            // Visual scale up slightly when selected/dragged
            Transform visual = transform.Find("VisualSphere");
            if (visual != null) visual.localScale *= 1.4f;
        }

        public void StopDragging()
        {
            isDragging = false;

            // Reset visual scale
            if (parentWire != null && parentWire.Settings != null)
            {
                Transform visual = transform.Find("VisualSphere");
                if (visual != null)
                {
                    visual.localScale = Vector3.one * (parentWire.Settings.handleVisibleRadius * 2.0f);
                }
            }

            if (candidateTerminal != null)
            {
                candidateTerminal.Highlight(false);
            }
        }

        public void MoveDirect(Vector3 worldPos)
        {
            currentWorldPosition = worldPos;
            transform.position = worldPos;
        }

        /// <summary>
        /// Updates position and scans nearby terminals for magnetic snapping with hysteresis.
        /// </summary>
        public void UpdateDragPosition(Vector3 rawWorldPos, Terminal otherEndTerminal, WireSettings settings)
        {
            if (!isDragging) return;

            float snapInDist = settings != null ? settings.snapInRadius : 0.04f;
            float snapOutDist = snapInDist * (settings != null ? settings.snapOutRadiusMultiplier : 1.35f);

            // 1. If currently snapped to a candidate, check snap-out hysteresis
            if (isSnapped && candidateTerminal != null)
            {
                float distFromSnap = Vector3.Distance(rawWorldPos, candidateTerminal.AnchorPosition);
                if (distFromSnap > snapOutDist)
                {
                    // Break free from magnetic lock
                    isSnapped = false;
                    candidateTerminal.Highlight(false);
                    candidateTerminal = null;
                    if (snapLerpCoroutine != null) StopCoroutine(snapLerpCoroutine);
                }
                else
                {
                    // Maintain snap lock at terminal anchor
                    currentWorldPosition = candidateTerminal.AnchorPosition;
                    transform.position = currentWorldPosition;
                    exitNormal = candidateTerminal.WorldExitDirection;
                    return;
                }
            }

            // 2. Scan for candidate terminals within snap-in radius
            Terminal bestCandidate = FindCandidateTerminal(rawWorldPos, otherEndTerminal, snapInDist, settings);

            if (bestCandidate != null)
            {
                if (candidateTerminal != bestCandidate)
                {
                    if (candidateTerminal != null) candidateTerminal.Highlight(false);
                    candidateTerminal = bestCandidate;
                    candidateTerminal.Highlight(true);

                    // Trigger haptic snap feedback
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
                    Handheld.Vibrate();
#endif
                }

                isSnapped = true;
                exitNormal = candidateTerminal.WorldExitDirection;

                // Animate quick smooth lerp to anchor
                if (snapLerpCoroutine != null) StopCoroutine(snapLerpCoroutine);
                float dur = settings != null ? settings.snapLerpDuration : 0.08f;
                snapLerpCoroutine = StartCoroutine(LerpToPosition(candidateTerminal.AnchorPosition, dur));
            }
            else
            {
                // Free floating tracking finger
                isSnapped = false;
                if (candidateTerminal != null)
                {
                    candidateTerminal.Highlight(false);
                    candidateTerminal = null;
                }

                currentWorldPosition = rawWorldPos;
                transform.position = currentWorldPosition;
                exitNormal = (otherEndTerminal != null)
                    ? (otherEndTerminal.AnchorPosition - currentWorldPosition).normalized
                    : Vector3.up;
            }
        }

        private IEnumerator LerpToPosition(Vector3 targetPos, float duration)
        {
            Vector3 start = currentWorldPosition;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                currentWorldPosition = Vector3.Lerp(start, targetPos, t);
                transform.position = currentWorldPosition;
                yield return null;
            }

            currentWorldPosition = targetPos;
            transform.position = targetPos;
            snapLerpCoroutine = null;
        }

        /// <summary>
        /// Commits the connection if currently snapped to candidateTerminal.
        /// Returns true if successfully connected, false if dropped in empty space or rejected.
        /// </summary>
        public bool CommitConnection(Terminal otherEndTerminal, WireSettings settings)
        {
            if (isSnapped && candidateTerminal != null)
            {
                if (candidateTerminal.CanAcceptConnection(parentWire, otherEndTerminal, settings, out string reason))
                {
                    AttachToTerminal(candidateTerminal);
                    return true;
                }
                else
                {
                    candidateTerminal.FlashRejected();
                    candidateTerminal = null;
                    isSnapped = false;
                    return false;
                }
            }

            return false;
        }

        public void AttachToTerminal(Terminal terminal)
        {
            if (terminal == null) return;

            attachedTerminal = terminal;
            isSnapped = true;
            candidateTerminal = null;
            currentWorldPosition = terminal.AnchorPosition;
            transform.position = currentWorldPosition;
            exitNormal = terminal.WorldExitDirection;

            terminal.AddWire(parentWire);
            terminal.Highlight(false);
            OnSnappedToTerminal?.Invoke(this, terminal);
        }

        public void DetachFromTerminal()
        {
            if (attachedTerminal != null)
            {
                Terminal prev = attachedTerminal;
                attachedTerminal = null;
                isSnapped = false;
                prev.RemoveWire(parentWire);
                prev.Highlight(false);
                OnUnsnappedFromTerminal?.Invoke(this, prev);
            }
        }

        public void Detach() => DetachFromTerminal();

        private Terminal FindCandidateTerminal(Vector3 queryPos, Terminal otherEnd, float maxDist, WireSettings settings)
        {
            IEnumerable<Terminal> all = (WireManager.Instance != null && WireManager.Instance.CachedTerminals.Count > 0)
                ? (IEnumerable<Terminal>)WireManager.Instance.CachedTerminals
                : FindObjectsByType<Terminal>(FindObjectsInactive.Exclude);

            Terminal best = null;
            float bestDistSqr = maxDist * maxDist;

            foreach (Terminal t in all)
            {
                if (t == null) continue;

                if (!t.CanAcceptConnection(parentWire, otherEnd, settings, out _))
                {
                    continue;
                }

                float distSqr = (t.AnchorPosition - queryPos).sqrMagnitude;
                if (distSqr <= bestDistSqr)
                {
                    bestDistSqr = distSqr;
                    best = t;
                }
            }

            return best;
        }
        #endregion
    }
}
