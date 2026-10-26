using System;
using UnityEngine;
using ContactorAR.Breaker;

namespace ContactorAR.Breaker
{
    /// <summary>
    /// Handles physical push-button interactions on the contactor front panel.
    /// Each button has a press depth animation, tactile spring-back, 
    /// configurable trigger action (ON / OFF / TRIP / Reset), and event callbacks.
    /// </summary>
    [SelectionBase]
    public class ContactorButton : MonoBehaviour
    {
        public enum ButtonAction
        {
            TurnOn,
            TurnOff,
            TripTest,
            Reset,
            TogglePower
        }

        [Header("Configuration")]
        [SerializeField] private string buttonLabel = "ON";
        [SerializeField] private ButtonAction actionOnPress = ButtonAction.TurnOn;
        [SerializeField] private ContactorController targetContactor;

        [Header("Press Animation")]
        [Tooltip("How far the button presses in (local Z axis, meters).")]
        [SerializeField] private float pressDepth = 0.006f;
        [Tooltip("Speed of press and spring-back motion.")]
        [SerializeField] private float pressSpeed = 24.0f;

        [Header("Colors")]
        [SerializeField] private Color normalColor = new Color(0.2f, 0.6f, 0.95f);
        [SerializeField] private Color pressedColor = new Color(0.1f, 0.3f, 0.5f);
        [SerializeField] private Color activeGlowColor = new Color(0.3f, 0.95f, 0.4f);

        [Header("Interaction")]
        [SerializeField] private float touchHitRadius = 0.04f;
        [SerializeField] private bool requiresHold = false;
        [SerializeField] private float holdThreshold = 0.5f;

        public event Action OnButtonPressed;

        private bool isPressed = false;
        private Vector3 restLocalPos;
        private Vector3 pressedLocalPos;
        private MeshRenderer buttonRenderer;
        private Material buttonMatInstance;
        private float holdTimer = 0f;
        private bool actionFired = false;
        private Camera mainCamera;

        private void Awake()
        {
            mainCamera = Camera.main;
            restLocalPos = transform.localPosition;
            pressedLocalPos = restLocalPos + new Vector3(0f, 0f, -pressDepth);

            buttonRenderer = GetComponent<MeshRenderer>();
            if (buttonRenderer == null)
                buttonRenderer = GetComponentInChildren<MeshRenderer>();

            if (buttonRenderer != null)
            {
                buttonMatInstance = buttonRenderer.material;
                buttonMatInstance.color = normalColor;
                buttonMatInstance.SetColor("_BaseColor", normalColor);
            }

            if (targetContactor == null)
                targetContactor = GetComponentInParent<ContactorController>();

            EnsureCollider();
        }

        private void EnsureCollider()
        {
            if (GetComponent<Collider>() == null)
            {
                SphereCollider sc = gameObject.AddComponent<SphereCollider>();
                sc.radius = touchHitRadius;
                sc.isTrigger = false;
            }
        }

        private void Update()
        {
            // Smooth press animation
            Vector3 target = isPressed ? pressedLocalPos : restLocalPos;
            transform.localPosition = Vector3.Lerp(transform.localPosition, target, Time.deltaTime * pressSpeed);

            // Color update
            if (buttonMatInstance != null)
            {
                Color targetColor = isPressed ? pressedColor : normalColor;
                buttonMatInstance.color = Color.Lerp(buttonMatInstance.color, targetColor, Time.deltaTime * pressSpeed);
                buttonMatInstance.SetColor("_BaseColor", buttonMatInstance.color);
            }

            // Hold button logic
            if (isPressed && requiresHold)
            {
                holdTimer += Time.deltaTime;
                if (holdTimer >= holdThreshold && !actionFired)
                {
                    FireAction();
                    actionFired = true;
                }
            }

#if UNITY_EDITOR || UNITY_STANDALONE
            HandleMouseInput();
#else
            HandleTouchInput();
#endif
        }

        #region Collision-Based Interaction
        /// <summary>
        /// Trigger press from external AR raycast manager or touch handler.
        /// </summary>
        public void PressButton()
        {
            if (isPressed) return;
            isPressed = true;
            holdTimer = 0f;
            actionFired = false;

            if (!requiresHold)
            {
                FireAction();
                actionFired = true;
            }
        }

        public void ReleaseButton()
        {
            isPressed = false;
            holdTimer = 0f;
            // Spring back will happen via lerp in Update()
        }
        #endregion

        private void HandleMouseInput()
        {
            if (mainCamera == null) mainCamera = Camera.main;
            if (mainCamera == null) return;

            if (Input.GetMouseButtonDown(0))
            {
                Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
                if (Physics.Raycast(ray, out RaycastHit hit, 50f))
                {
                    if (hit.transform == transform || hit.transform.IsChildOf(transform))
                    {
                        PressButton();
                    }
                }
            }
            if (Input.GetMouseButtonUp(0))
            {
                ReleaseButton();
            }
        }

        private void HandleTouchInput()
        {
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch touch = Input.GetTouch(i);
                if (touch.phase == TouchPhase.Began)
                {
                    if (mainCamera == null) mainCamera = Camera.main;
                    if (mainCamera == null) continue;
                    Ray ray = mainCamera.ScreenPointToRay(touch.position);
                    if (Physics.Raycast(ray, out RaycastHit hit, 50f))
                    {
                        if (hit.transform == transform || hit.transform.IsChildOf(transform))
                            PressButton();
                    }
                }
                else if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                {
                    ReleaseButton();
                }
            }
        }

        private void FireAction()
        {
            if (targetContactor == null) return;

            switch (actionOnPress)
            {
                case ButtonAction.TurnOn:
                    targetContactor.SwitchOn();
                    break;
                case ButtonAction.TurnOff:
                    targetContactor.SwitchOff();
                    break;
                case ButtonAction.TripTest:
                    targetContactor.TriggerOverloadTrip("Test Trip Button");
                    break;
                case ButtonAction.Reset:
                    targetContactor.SwitchOff(); // Reset = move to OFF
                    break;
                case ButtonAction.TogglePower:
                    targetContactor.ToggleBreaker();
                    break;
            }

            FlashGlow();
            OnButtonPressed?.Invoke();
            Debug.Log($"[ContactorButton] Button '{buttonLabel}' fired action: {actionOnPress}");
        }

        private void FlashGlow()
        {
            if (buttonMatInstance == null) return;
            buttonMatInstance.EnableKeyword("_EMISSION");
            buttonMatInstance.SetColor("_EmissionColor", activeGlowColor * 2.5f);

            // Auto-fade emission using a native coroutine (no LeanTween required)
            StopAllCoroutines();
            StartCoroutine(FadeEmissionCoroutine(2.5f, 0f, 0.4f));
        }

        private System.Collections.IEnumerator FadeEmissionCoroutine(float fromIntensity, float toIntensity, float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float v = Mathf.Lerp(fromIntensity, toIntensity, t);
                if (buttonMatInstance != null)
                    buttonMatInstance.SetColor("_EmissionColor", activeGlowColor * v);
                yield return null;
            }
            if (buttonMatInstance != null)
            {
                buttonMatInstance.SetColor("_EmissionColor", Color.black);
                buttonMatInstance.DisableKeyword("_EMISSION");
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, touchHitRadius);
        }
    }
}
