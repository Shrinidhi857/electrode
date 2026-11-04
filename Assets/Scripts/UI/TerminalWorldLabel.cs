using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ContactorAR.Wiring;

namespace ContactorAR.UI
{
    /// <summary>
    /// Displays an AR billboard floating label above each terminal,
    /// showing terminal name, live voltage, phase, and connection state.
    /// </summary>
    public class TerminalWorldLabel : MonoBehaviour
    {
        public const float WORLD_LABEL_SCALE = 0.00010f; // Ultra-compact AR readability so 18mm pole labels never overlap
        public static bool GlobalLabelsEnabled = false; // Hidden by default for clean AR view

        [Header("Target Terminal")]
        [SerializeField] private TerminalPoint terminal;

        [Header("Floating Offset")]
        [SerializeField] private Vector3 floatOffset = new Vector3(0f, 0.012f, 0f);

        [Header("UI Elements")]
        [SerializeField] private Canvas worldCanvas;
        [SerializeField] private TextMeshProUGUI titleText;
        [SerializeField] private TextMeshProUGUI statusText;
        [SerializeField] private Image backgroundPlate;

        private Camera mainCamera;

        public void Setup(TerminalPoint targetTerm)
        {
            terminal = targetTerm;
            EnsureCanvasAndUI();
            if (worldCanvas != null) worldCanvas.transform.localScale = Vector3.one * WORLD_LABEL_SCALE;
            UpdateLabelText();
        }

        private void Awake()
        {
            mainCamera = Camera.main;
            if (terminal == null) terminal = GetComponentInParent<TerminalPoint>();
            EnsureCanvasAndUI();
            if (worldCanvas != null) worldCanvas.transform.localScale = Vector3.one * WORLD_LABEL_SCALE;
        }

        private void OnEnable()
        {
            if (terminal != null)
            {
                terminal.OnElectricalStateChanged += HandleStateChanged;
                terminal.OnWireConnected += HandleWireChanged;
                terminal.OnWireDisconnected += HandleWireChanged;
            }
        }

        private void OnDisable()
        {
            if (terminal != null)
            {
                terminal.OnElectricalStateChanged -= HandleStateChanged;
                terminal.OnWireConnected -= HandleWireChanged;
                terminal.OnWireDisconnected -= HandleWireChanged;
            }
        }

        private void LateUpdate()
        {
            if (mainCamera == null) mainCamera = Camera.main;
            if (mainCamera == null) return;

            // Labels are only shown when explicitly enabled via bottom toolbar "Labels" toggle
            bool shouldBeVisible = GlobalLabelsEnabled;

            float distanceToCam = Vector3.Distance(transform.position, mainCamera.transform.position);
            if (distanceToCam > 2.0f)
            {
                shouldBeVisible = false; // Distance culling beyond 2.0 meters
            }

            if (worldCanvas != null)
            {
                worldCanvas.enabled = shouldBeVisible;
            }

            if (!shouldBeVisible) return;

            // Billboard effect: Face AR camera
            transform.rotation = Quaternion.LookRotation(transform.position - mainCamera.transform.position);

            // Dynamic distance scaling: keeps label small and crisp without overlapping neighbors
            float distScale = Mathf.Clamp(distanceToCam * 0.75f, 0.4f, 1.2f);
            if (worldCanvas != null)
            {
                worldCanvas.transform.localScale = Vector3.one * (WORLD_LABEL_SCALE * distScale);
            }

            if (terminal != null)
            {
                // Stagger vertical height between even and odd poles so adjacent terminal labels never overlap
                float staggerY = (terminal.PoleIndex % 2 == 1) ? 0.022f : 0.010f;
                transform.position = terminal.SnapPosition + new Vector3(floatOffset.x, staggerY, floatOffset.z);
            }
        }

        private void HandleStateChanged(TerminalPoint tp) => UpdateLabelText();
        private void HandleWireChanged(TerminalPoint tp, WireConnectorEnd we) => UpdateLabelText();

        public void UpdateLabelText()
        {
            if (terminal == null) return;

            if (titleText != null)
            {
                string phaseSuffix = !string.IsNullOrEmpty(terminal.PhaseTag) ? $" ({terminal.PhaseTag})" : "";
                titleText.text = $"{terminal.TerminalLabel}{phaseSuffix}";
            }

            if (statusText != null)
            {
                if (terminal.IsEnergized)
                {
                    statusText.text = $"{terminal.Voltage:F0}V LIVE";
                    statusText.color = new Color(1.0f, 0.85f, 0.1f); // Electric yellow
                    if (backgroundPlate != null) backgroundPlate.color = new Color(0.35f, 0.05f, 0.05f, 0.85f);
                }
                else
                {
                    statusText.text = terminal.IsOccupied ? "CONNECTED (0V)" : "OPEN";
                    statusText.color = new Color(0.7f, 0.85f, 0.95f);
                    if (backgroundPlate != null) backgroundPlate.color = new Color(0.08f, 0.12f, 0.18f, 0.8f);
                }
            }
        }

        private void EnsureCanvasAndUI()
        {
            if (worldCanvas != null)
            {
                // Ensure scale matches new 3-5x smaller compact scale
                worldCanvas.transform.localScale = Vector3.one * WORLD_LABEL_SCALE;
                return;
            }

            // Setup World Space Canvas
            worldCanvas = gameObject.GetComponent<Canvas>();
            if (worldCanvas == null) worldCanvas = gameObject.AddComponent<Canvas>();
            worldCanvas.renderMode = RenderMode.WorldSpace;

            RectTransform rect = worldCanvas.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(72, 28);
            rect.localScale = Vector3.one * WORLD_LABEL_SCALE;

            // Background panel
            GameObject bgObj = new GameObject("BgPanel");
            bgObj.transform.SetParent(transform, false);
            backgroundPlate = bgObj.AddComponent<Image>();
            backgroundPlate.color = new Color(0.08f, 0.12f, 0.18f, 0.85f);
            RectTransform bgRect = bgObj.GetComponent<RectTransform>();
            bgRect.sizeDelta = new Vector2(72, 28);

            // Title Text
            GameObject titleObj = new GameObject("TitleText");
            titleObj.transform.SetParent(bgObj.transform, false);
            titleText = titleObj.AddComponent<TextMeshProUGUI>();
            titleText.fontSize = 11;
            titleText.alignment = TextAlignmentOptions.Center;
            titleText.fontStyle = FontStyles.Bold;
            titleText.text = terminal != null ? terminal.TerminalLabel : "TERMINAL";
            RectTransform tRect = titleObj.GetComponent<RectTransform>();
            tRect.sizeDelta = new Vector2(68, 14);
            tRect.anchoredPosition = new Vector2(0, 6);

            // Status Text
            GameObject statusObj = new GameObject("StatusText");
            statusObj.transform.SetParent(bgObj.transform, false);
            statusText = statusObj.AddComponent<TextMeshProUGUI>();
            statusText.fontSize = 9;
            statusText.alignment = TextAlignmentOptions.Center;
            statusText.text = "0V";
            RectTransform sRect = statusObj.GetComponent<RectTransform>();
            sRect.sizeDelta = new Vector2(68, 12);
            sRect.anchoredPosition = new Vector2(0, -6);
        }

        public void SetVisible(bool visible)
        {
            GlobalLabelsEnabled = visible;
            if (worldCanvas == null) worldCanvas = GetComponent<Canvas>();
            if (worldCanvas != null)
            {
                worldCanvas.enabled = visible;
            }
            else
            {
                gameObject.SetActive(visible);
            }
        }
    }
}
