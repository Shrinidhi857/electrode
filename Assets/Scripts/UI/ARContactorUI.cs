using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ContactorAR.Breaker;
using ContactorAR.Wiring;
using ContactorAR.Simulation;
using ContactorAR.AR;

namespace ContactorAR.UI
{
    /// <summary>
    /// Interactive AR on-screen controls and telemetry dashboard for the contactor/breaker.
    /// Provides dedicated buttons for ON, OFF, TRIP, coordinate hanging, wire spawning,
    /// power simulation toggle, and AR telemetry readouts.
    /// </summary>
    public class ARContactorUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private ContactorController contactor;
        [SerializeField] private ARModelManipulator manipulator;
        [SerializeField] private PowerCircuitSimulator simulator;
        [SerializeField] private WireSpawner wireSpawner;

        [Header("UI Status Elements")]
        [SerializeField] private TextMeshProUGUI statusBannerText;
        [SerializeField] private TextMeshProUGUI angleReadoutText;
        [SerializeField] private TextMeshProUGUI powerTelemetryText;

        [Header("UI Control Buttons")]
        [SerializeField] private Button buttonTurnOn;
        [SerializeField] private Button buttonTurnOff;
        [SerializeField] private Button buttonTrip;
        [SerializeField] private Button buttonTogglePower;
        [SerializeField] private Button buttonHangCoordinates;
        [SerializeField] private Button buttonSpawnWire;
        [SerializeField] private Button buttonClearWires;
        [SerializeField] private Button buttonToggleLabels;

        [Header("Button Text Displays")]
        [SerializeField] private TextMeshProUGUI powerButtonLabel;
        [SerializeField] private TextMeshProUGUI hangButtonLabel;

        private bool areLabelsVisible = true;

        private void Awake()
        {
            FindSceneReferencesIfNull();
            BuildRuntimeUIIfEmpty();
        }

        private void Start()
        {
            BindButtonEvents();
            UpdateAllTelemetry();
        }

        private void Update()
        {
            UpdateAllTelemetry();
        }

        public void FindSceneReferencesIfNull()
        {
            if (contactor == null) contactor = FindFirstObjectByType<ContactorController>();
            if (manipulator == null) manipulator = FindFirstObjectByType<ARModelManipulator>();
            if (simulator == null) simulator = FindFirstObjectByType<PowerCircuitSimulator>();
            if (wireSpawner == null) wireSpawner = FindFirstObjectByType<WireSpawner>();
        }

        private void BindButtonEvents()
        {
            if (buttonTurnOn != null)
                buttonTurnOn.onClick.AddListener(OnTurnOnClicked);

            if (buttonTurnOff != null)
                buttonTurnOff.onClick.AddListener(OnTurnOffClicked);

            if (buttonTrip != null)
                buttonTrip.onClick.AddListener(OnTripClicked);

            if (buttonTogglePower != null)
                buttonTogglePower.onClick.AddListener(OnTogglePowerClicked);

            if (buttonHangCoordinates != null)
                buttonHangCoordinates.onClick.AddListener(OnHangCoordinatesClicked);

            if (buttonSpawnWire != null)
                buttonSpawnWire.onClick.AddListener(OnSpawnWireClicked);

            if (buttonClearWires != null)
                buttonClearWires.onClick.AddListener(OnClearWiresClicked);

            if (buttonToggleLabels != null)
                buttonToggleLabels.onClick.AddListener(OnToggleLabelsClicked);
        }

        #region Button Actions
        public void OnTurnOnClicked()
        {
            if (contactor != null) contactor.SwitchOn();
        }

        public void OnTurnOffClicked()
        {
            if (contactor != null) contactor.SwitchOff();
        }

        public void OnTripClicked()
        {
            if (contactor != null) contactor.TriggerOverloadTrip("Manual Test Trip");
        }

        public void OnTogglePowerClicked()
        {
            if (simulator != null)
            {
                simulator.ToggleMainsSupply();
                UpdateAllTelemetry();
            }
        }

        public void OnHangCoordinatesClicked()
        {
            if (manipulator != null)
            {
                manipulator.ToggleHanging();
                UpdateAllTelemetry();
            }
        }

        public void OnSpawnWireClicked()
        {
            if (wireSpawner != null)
            {
                wireSpawner.SpawnNextWire();
            }
        }

        public void OnClearWiresClicked()
        {
            if (wireSpawner != null)
            {
                wireSpawner.ClearAllWires();
            }
        }

        public void OnToggleLabelsClicked()
        {
            areLabelsVisible = !areLabelsVisible;
            TerminalWorldLabel[] labels = FindObjectsByType<TerminalWorldLabel>(FindObjectsSortMode.None);
            for (int i = 0; i < labels.Length; i++)
            {
                labels[i].SetVisible(areLabelsVisible);
            }
        }
        #endregion

        #region Telemetry Updates
        private void UpdateAllTelemetry()
        {
            // 1. Lever & Breaker Status
            if (contactor != null && contactor.Lever != null)
            {
                float angle = contactor.Lever.CurrentAngle;
                BreakerState state = contactor.Lever.CurrentState;

                if (angleReadoutText != null)
                {
                    angleReadoutText.text = $"LEVER: {angle:F1}° [{state.ToString().ToUpper()}]";
                }

                if (statusBannerText != null)
                {
                    switch (state)
                    {
                        case BreakerState.On:
                            statusBannerText.text = "BREAKER: CLOSED / ON (68.0°)";
                            statusBannerText.color = new Color(0.2f, 0.9f, 0.3f);
                            break;
                        case BreakerState.Tripped:
                            statusBannerText.text = "BREAKER: TRIPPED (33.3°)";
                            statusBannerText.color = new Color(1.0f, 0.6f, 0.1f);
                            break;
                        case BreakerState.Off:
                        default:
                            statusBannerText.text = "BREAKER: OPEN / OFF (-1.4°)";
                            statusBannerText.color = new Color(0.9f, 0.25f, 0.25f);
                            break;
                    }
                }
            }

            // 2. Power Telemetry
            if (simulator != null && powerTelemetryText != null)
            {
                string powerState = simulator.MainsSupplyEnabled ? "230V / 400V (ACTIVE)" : "0V (OFFLINE)";
                string fault = simulator.IsShortCircuitFault ? " | FAULT: SHORT-CIRCUIT!" : "";
                powerTelemetryText.text = $"POWER: {powerState} | LOAD: {simulator.TotalLoadCurrent:F1}A{fault}";
                powerTelemetryText.color = simulator.IsShortCircuitFault ? Color.red : (simulator.MainsSupplyEnabled ? Color.yellow : Color.gray);
            }

            // 3. Power Button Label
            if (powerButtonLabel != null && simulator != null)
            {
                powerButtonLabel.text = simulator.MainsSupplyEnabled ? "MAINS: ON" : "MAINS: OFF";
            }

            // 4. Hang Button Label
            if (hangButtonLabel != null && manipulator != null)
            {
                hangButtonLabel.text = manipulator.IsHanging ? "UNPIN / FREE MOVE" : "HANG AT COORD";
            }
        }
        #endregion

        #region Dynamic UI Generation (Zero manual UI hierarchy needed)
        private void BuildRuntimeUIIfEmpty()
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                canvas = gameObject.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                gameObject.AddComponent<GraphicRaycaster>();
            }

            if (buttonTurnOn != null && statusBannerText != null) return; // Already constructed

            // --- Top Status Panel ---
            GameObject topPanel = CreateUIPanel("TopStatusPanel", transform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -20), new Vector2(-60, 120));
            Image topImg = topPanel.GetComponent<Image>();
            topImg.color = new Color(0.06f, 0.09f, 0.15f, 0.88f);

            statusBannerText = CreateUIText("StatusBanner", topPanel.transform, "BREAKER: OFF (-1.4°)", 26, TextAlignmentOptions.Center, new Vector2(0, 20), new Vector2(600, 38));
            statusBannerText.fontStyle = FontStyles.Bold;
            statusBannerText.color = new Color(0.9f, 0.25f, 0.25f);

            angleReadoutText = CreateUIText("AngleReadout", topPanel.transform, "LEVER: -1.4° [OFF]", 20, TextAlignmentOptions.Center, new Vector2(0, -12), new Vector2(500, 30));
            angleReadoutText.color = new Color(0.75f, 0.85f, 0.95f);

            powerTelemetryText = CreateUIText("PowerTelemetry", topPanel.transform, "POWER: 230V / 400V (ACTIVE)", 18, TextAlignmentOptions.Center, new Vector2(0, -40), new Vector2(650, 28));
            powerTelemetryText.color = Color.yellow;

            // --- Bottom Controls Panel ---
            GameObject bottomPanel = CreateUIPanel("BottomControlPanel", transform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 30), new Vector2(-60, 140));
            Image botImg = bottomPanel.GetComponent<Image>();
            botImg.color = new Color(0.06f, 0.09f, 0.15f, 0.88f);

            // Row 1: Breaker & Power Controls
            buttonTurnOn = CreateUIButton("BtnOn", bottomPanel.transform, "ON (68°)", new Color(0.15f, 0.65f, 0.25f), new Vector2(-360, 25), new Vector2(150, 50));
            buttonTurnOff = CreateUIButton("BtnOff", bottomPanel.transform, "OFF (-1.4°)", new Color(0.75f, 0.2f, 0.2f), new Vector2(-190, 25), new Vector2(150, 50));
            buttonTrip = CreateUIButton("BtnTrip", bottomPanel.transform, "TEST TRIP", new Color(0.85f, 0.55f, 0.1f), new Vector2(-20, 25), new Vector2(150, 50));
            
            buttonTogglePower = CreateUIButton("BtnPower", bottomPanel.transform, "MAINS: ON", new Color(0.2f, 0.45f, 0.75f), new Vector2(150, 25), new Vector2(150, 50));
            powerButtonLabel = buttonTogglePower.GetComponentInChildren<TextMeshProUGUI>();

            buttonHangCoordinates = CreateUIButton("BtnHang", bottomPanel.transform, "HANG AT COORD", new Color(0.45f, 0.25f, 0.75f), new Vector2(320, 25), new Vector2(170, 50));
            hangButtonLabel = buttonHangCoordinates.GetComponentInChildren<TextMeshProUGUI>();

            // Row 2: Wiring & View Controls
            buttonSpawnWire = CreateUIButton("BtnSpawnWire", bottomPanel.transform, "+ SPAWN WIRE", new Color(0.2f, 0.55f, 0.65f), new Vector2(-260, -32), new Vector2(160, 42));
            buttonClearWires = CreateUIButton("BtnClearWires", bottomPanel.transform, "CLEAR WIRES", new Color(0.4f, 0.4f, 0.45f), new Vector2(-80, -32), new Vector2(160, 42));
            buttonToggleLabels = CreateUIButton("BtnLabels", bottomPanel.transform, "TOGGLE LABELS", new Color(0.3f, 0.35f, 0.5f), new Vector2(100, -32), new Vector2(160, 42));
        }

        private GameObject CreateUIPanel(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 sizeDelta)
        {
            GameObject panel = new GameObject(name);
            panel.transform.SetParent(parent, false);
            RectTransform rect = panel.AddComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = sizeDelta;
            Image img = panel.AddComponent<Image>();
            return panel;
        }

        private TextMeshProUGUI CreateUIText(string name, Transform parent, string text, float size, TextAlignmentOptions align, Vector2 pos, Vector2 sizeDelta)
        {
            GameObject txtObj = new GameObject(name);
            txtObj.transform.SetParent(parent, false);
            RectTransform rect = txtObj.AddComponent<RectTransform>();
            rect.anchoredPosition = pos;
            rect.sizeDelta = sizeDelta;
            TextMeshProUGUI tmp = txtObj.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.alignment = align;
            return tmp;
        }

        private Button CreateUIButton(string name, Transform parent, string label, Color color, Vector2 pos, Vector2 sizeDelta)
        {
            GameObject btnObj = new GameObject(name);
            btnObj.transform.SetParent(parent, false);
            RectTransform rect = btnObj.AddComponent<RectTransform>();
            rect.anchoredPosition = pos;
            rect.sizeDelta = sizeDelta;

            Image img = btnObj.AddComponent<Image>();
            img.color = color;

            Button btn = btnObj.AddComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.normalColor = color;
            cb.highlightedColor = color * 1.25f;
            cb.pressedColor = color * 0.8f;
            btn.colors = cb;

            TextMeshProUGUI tmp = CreateUIText("Text", btnObj.transform, label, 18, TextAlignmentOptions.Center, Vector2.zero, sizeDelta);
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = Color.white;

            return btn;
        }
        #endregion
    }
}
