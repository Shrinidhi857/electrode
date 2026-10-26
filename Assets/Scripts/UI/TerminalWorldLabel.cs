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
        [Header("Target Terminal")]
        [SerializeField] private TerminalPoint terminal;

        [Header("Floating Offset")]
        [SerializeField] private Vector3 floatOffset = new Vector3(0f, 0.08f, 0f);

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
            UpdateLabelText();
        }

        private void Awake()
        {
            mainCamera = Camera.main;
            if (terminal == null) terminal = GetComponentInParent<TerminalPoint>();
            EnsureCanvasAndUI();
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

            // Billboard effect: Face AR camera
            transform.rotation = Quaternion.LookRotation(transform.position - mainCamera.transform.position);

            if (terminal != null)
            {
                transform.position = terminal.SnapPosition + floatOffset;
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
            if (worldCanvas != null) return;

            // Setup World Space Canvas
            worldCanvas = gameObject.GetComponent<Canvas>();
            if (worldCanvas == null) worldCanvas = gameObject.AddComponent<Canvas>();
            worldCanvas.renderMode = RenderMode.WorldSpace;

            RectTransform rect = worldCanvas.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(160, 60);
            rect.localScale = Vector3.one * 0.0018f; // Scaled down for world space

            // Background panel
            GameObject bgObj = new GameObject("BgPanel");
            bgObj.transform.SetParent(transform, false);
            backgroundPlate = bgObj.AddComponent<Image>();
            backgroundPlate.color = new Color(0.08f, 0.12f, 0.18f, 0.85f);
            RectTransform bgRect = bgObj.GetComponent<RectTransform>();
            bgRect.sizeDelta = new Vector2(160, 60);

            // Title Text
            GameObject titleObj = new GameObject("TitleText");
            titleObj.transform.SetParent(bgObj.transform, false);
            titleText = titleObj.AddComponent<TextMeshProUGUI>();
            titleText.fontSize = 18;
            titleText.alignment = TextAlignmentOptions.Center;
            titleText.fontStyle = FontStyles.Bold;
            titleText.text = terminal != null ? terminal.TerminalLabel : "TERMINAL";
            RectTransform tRect = titleObj.GetComponent<RectTransform>();
            tRect.sizeDelta = new Vector2(150, 24);
            tRect.anchoredPosition = new Vector2(0, 12);

            // Status Text
            GameObject statusObj = new GameObject("StatusText");
            statusObj.transform.SetParent(bgObj.transform, false);
            statusText = statusObj.AddComponent<TextMeshProUGUI>();
            statusText.fontSize = 14;
            statusText.alignment = TextAlignmentOptions.Center;
            statusText.text = "0V";
            RectTransform sRect = statusObj.GetComponent<RectTransform>();
            sRect.sizeDelta = new Vector2(150, 20);
            sRect.anchoredPosition = new Vector2(0, -12);
        }

        public void SetVisible(bool visible)
        {
            gameObject.SetActive(visible);
        }
    }
}
