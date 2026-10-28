using System;
using System.Collections.Generic;
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
    /// Provides:
    ///   - Top status panel (telemetry readouts)
    ///   - Bottom control panel (larger buttons: ON/OFF/TRIP/POWER/LOCK/LABELS/WIRE controls)
    ///   - Left-side scrollable component browser sidebar (1.5 cm wide, 5 cm+ blocks per component)
    ///     that calls ComponentSpawner when a component block is tapped.
    /// </summary>
    public class ARContactorUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private ContactorController contactor;
        [SerializeField] private ARModelManipulator manipulator;
        [SerializeField] private PowerCircuitSimulator simulator;
        [SerializeField] private WireSpawner wireSpawner;
        [SerializeField] private ComponentSpawner componentSpawner;

        [Header("Contactor Coil")]
        [SerializeField] private ContactorArmatureController armatureController;
        private Button buttonCoilOn;
        private Button buttonCoilOff;
        private Button buttonCoilToggle;
        private Button buttonManualPress;
        private TextMeshProUGUI contactorStatusText;

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
        [SerializeField] private Button buttonRotateModel;

        [Header("Button Text Displays")]
        [SerializeField] private TextMeshProUGUI powerButtonLabel;
        [SerializeField] private TextMeshProUGUI hangButtonLabel;

        // Sidebar component blocks (dynamically generated)
        private readonly List<Button> sidebarButtons = new List<Button>();
        private bool areLabelsVisible = true;

        // ──────────── DPI helpers ────────────
        // Unity's CanvasScaler reference res is 1920×1080; we work in those virtual pixels.
        // To convert physical cm to virtual pixels we use: px = cm * (refPxPerInch / 2.54)
        // Assuming 160 px/inch reference DPI in Unity's default scaling.
        private const float REF_PPI = 160f;
        private const float CM_TO_PX = REF_PPI / 2.54f;

        // Physical sizes in reference-resolution pixels
        // Sidebar block: 1.5 cm wide × 5 cm tall
        private static readonly Vector2 SidebarBlockSize = new Vector2(1.5f * CM_TO_PX, 5.0f * CM_TO_PX);
        // Main buttons: 3 cm wide × 2 cm tall (much bigger than before)
        private static readonly Vector2 MainButtonSize  = new Vector2(3.5f * CM_TO_PX, 2.2f * CM_TO_PX);
        // Small action buttons: 3 cm wide × 1.8 cm tall
        private static readonly Vector2 SmallButtonSize = new Vector2(3.0f * CM_TO_PX, 1.8f * CM_TO_PX);

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
            if (contactor == null)         contactor         = FindFirstObjectByType<ContactorController>();
            if (manipulator == null)       manipulator       = FindFirstObjectByType<ARModelManipulator>();
            if (simulator == null)         simulator         = FindFirstObjectByType<PowerCircuitSimulator>();
            if (wireSpawner == null)       wireSpawner       = FindFirstObjectByType<WireSpawner>();
            if (componentSpawner == null)  componentSpawner  = FindFirstObjectByType<ComponentSpawner>();
            if (componentSpawner == null)  componentSpawner  = gameObject.AddComponent<ComponentSpawner>();
            if (armatureController == null) armatureController = FindFirstObjectByType<ContactorArmatureController>();
        }

        private void BindButtonEvents()
        {
            SafeBind(buttonTurnOn,          OnTurnOnClicked);
            SafeBind(buttonTurnOff,         OnTurnOffClicked);
            SafeBind(buttonTrip,            OnTripClicked);
            SafeBind(buttonTogglePower,     OnTogglePowerClicked);
            SafeBind(buttonHangCoordinates, OnHangCoordinatesClicked);
            SafeBind(buttonSpawnWire,       OnSpawnWireClicked);
            SafeBind(buttonClearWires,      OnClearWiresClicked);
            SafeBind(buttonToggleLabels,    OnToggleLabelsClicked);
            SafeBind(buttonRotateModel,     OnRotateClicked);

            // Contactor coil buttons
            SafeBind(buttonCoilOn,      OnCoilOnClicked);
            SafeBind(buttonCoilOff,     OnCoilOffClicked);
            SafeBind(buttonCoilToggle,  OnCoilToggleClicked);
            SafeBind(buttonManualPress, OnManualPressClicked);
        }

        private static void SafeBind(Button btn, UnityEngine.Events.UnityAction action)
        {
            if (btn != null) btn.onClick.AddListener(action);
        }

        #region Button Actions
        public void OnTurnOnClicked()
        {
            if (contactor == null) FindSceneReferencesIfNull();
            if (contactor != null) contactor.SwitchOn();
        }

        public void OnTurnOffClicked()
        {
            if (contactor == null) FindSceneReferencesIfNull();
            if (contactor != null) contactor.SwitchOff();
        }

        public void OnTripClicked()
        {
            if (contactor == null) FindSceneReferencesIfNull();
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
            ARModelManipulator target = ARModelManipulator.Selected ?? manipulator;
            if (target != null)
            {
                if (target.Mode == PlacementMode.Locked || target.Mode == PlacementMode.HangingAtCoordinates)
                    target.ReleaseToFreeMovable();
                else
                    target.LockCurrentPosition();
                UpdateAllTelemetry();
            }
        }

        public void OnSpawnWireClicked()
        {
            if (wireSpawner != null) wireSpawner.SpawnNextWire();
        }

        public void OnClearWiresClicked()
        {
            if (wireSpawner != null) wireSpawner.ClearAllWires();
        }

        public void OnToggleLabelsClicked()
        {
            areLabelsVisible = !areLabelsVisible;
            // IMPORTANT: must use FindObjectsInactive.Include so that labels that were hidden
            // (SetActive false) are still found and can be re-shown on the next toggle.
            TerminalWorldLabel[] labels = FindObjectsByType<TerminalWorldLabel>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < labels.Length; i++)
                labels[i].SetVisible(areLabelsVisible);
        }

        public void OnRotateClicked()
        {
            ARModelManipulator target = ARModelManipulator.Selected ?? manipulator;
            if (target != null)
            {
                target.RotateY(90f);
            }
        }

        private void OnSidebarComponentClicked(int index, string componentName)
        {
            if (componentSpawner == null)
            {
                componentSpawner = FindFirstObjectByType<ComponentSpawner>();
                if (componentSpawner == null)
                    componentSpawner = gameObject.AddComponent<ComponentSpawner>();
            }

            componentSpawner.SpawnComponent(index, componentName);
        }

        // ── Contactor coil actions ────────────────────────────────────
        private ContactorArmatureController GetArmature()
        {
            if (armatureController == null)
                armatureController = FindFirstObjectByType<ContactorArmatureController>();
            return armatureController;
        }

        public void OnCoilOnClicked()
        {
            GetArmature()?.Energize();
            UpdateContactorStatus();
        }

        public void OnCoilOffClicked()
        {
            GetArmature()?.Release();
            UpdateContactorStatus();
        }

        public void OnCoilToggleClicked()
        {
            GetArmature()?.Toggle();
            UpdateContactorStatus();
        }

        private bool _manualPressHeld = false;
        public void OnManualPressClicked()
        {
            _manualPressHeld = !_manualPressHeld;
            GetArmature()?.SetManualOverride(_manualPressHeld);
            if (buttonManualPress != null)
            {
                TextMeshProUGUI lbl = buttonManualPress.GetComponentInChildren<TextMeshProUGUI>();
                if (lbl != null) lbl.text = _manualPressHeld ? "RELEASE PLUNGER" : "MANUAL PRESS";
            }
            UpdateContactorStatus();
        }

        private void UpdateContactorStatus()
        {
            if (contactorStatusText == null) return;
            ContactorArmatureController arm = GetArmature();
            if (arm == null)
            {
                contactorStatusText.text  = "CONTACTOR: NOT FOUND";
                contactorStatusText.color = Color.gray;
                return;
            }
            if (arm.IsEnergized)
            {
                contactorStatusText.text  = $"CONTACTOR: ENERGIZED  y={arm.CurrentY:F4}m";
                contactorStatusText.color = new Color(0.2f, 0.9f, 0.3f);
            }
            else
            {
                contactorStatusText.text  = $"CONTACTOR: RELEASED   y={arm.CurrentY:F4}m";
                contactorStatusText.color = new Color(0.9f, 0.3f, 0.25f);
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
                    angleReadoutText.text = $"LEVER: {angle:F1}° [{state.ToString().ToUpper()}]";

                if (statusBannerText != null)
                {
                    switch (state)
                    {
                        case BreakerState.On:
                            statusBannerText.text  = $"BREAKER: CLOSED / ON ({contactor.Lever.OnAngle:F1}°)";
                            statusBannerText.color = new Color(0.2f, 0.9f, 0.3f);
                            break;
                        case BreakerState.Tripped:
                            statusBannerText.text  = $"BREAKER: TRIPPED ({contactor.Lever.TripAngle:F1}°)";
                            statusBannerText.color = new Color(1.0f, 0.6f, 0.1f);
                            break;
                        default:
                            statusBannerText.text  = $"BREAKER: OPEN / OFF ({contactor.Lever.OffAngle:F1}°)";
                            statusBannerText.color = new Color(0.9f, 0.25f, 0.25f);
                            break;
                    }
                }
            }

            // 2. Power Telemetry
            if (simulator != null && powerTelemetryText != null)
            {
                string powerState = simulator.MainsSupplyEnabled ? "230V / 400V (ACTIVE)" : "0V (OFFLINE)";
                string fault      = simulator.IsShortCircuitFault ? " | FAULT: SHORT-CIRCUIT!" : "";
                powerTelemetryText.text  = $"POWER: {powerState} | LOAD: {simulator.TotalLoadCurrent:F1}A{fault}";
                powerTelemetryText.color = simulator.IsShortCircuitFault ? Color.red :
                                          (simulator.MainsSupplyEnabled ? Color.yellow : Color.gray);
            }

            // 3. Power Button Label
            if (powerButtonLabel != null && simulator != null)
                powerButtonLabel.text = simulator.MainsSupplyEnabled ? "MAINS: ON" : "MAINS: OFF";

            // 4. Lock / Move Button Label
            ARModelManipulator target = ARModelManipulator.Selected ?? manipulator;
            if (hangButtonLabel != null && target != null)
            {
                bool isLocked = target.Mode == PlacementMode.Locked ||
                                target.Mode == PlacementMode.HangingAtCoordinates;
                string mName = target.gameObject.name;
                hangButtonLabel.text = isLocked ? $"MOVE: {mName}" : $"LOCK: {mName}";
            }

            // 5. Contactor status
            UpdateContactorStatus();
        }
        #endregion

        #region Dynamic UI Generation
        private void BuildRuntimeUIIfEmpty()
        {
            // Ensure we have a Canvas
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                canvas = gameObject.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 10;
                CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;
                gameObject.AddComponent<GraphicRaycaster>();
            }

            if (buttonTurnOn != null && statusBannerText != null) return; // Already constructed

            BuildTopPanel();
            BuildBottomPanel();
            BuildContactorPanel();
            BuildSidebarPanel();
        }

        // ── Top status panel ──────────────────────────────────────────
        private void BuildTopPanel()
        {
            float panelH = 130f;
            GameObject topPanel = CreatePanel("TopStatusPanel", transform,
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(1f, 1f),
                pivot: new Vector2(0.5f, 1f),
                anchoredPos: new Vector2(0f, 0f),
                sizeDelta: new Vector2(-SidebarBlockSize.x - 8f, panelH));
            SetPanelColor(topPanel, new Color(0.05f, 0.08f, 0.14f, 0.92f));

            statusBannerText = CreateText("StatusBanner", topPanel.transform,
                "BREAKER: OFF (-1.4°)", 30f, TextAlignmentOptions.Center,
                pos: new Vector2(0f, 30f), sizeDelta: new Vector2(700f, 46f));
            statusBannerText.fontStyle = FontStyles.Bold;
            statusBannerText.color = new Color(0.9f, 0.25f, 0.25f);

            angleReadoutText = CreateText("AngleReadout", topPanel.transform,
                "LEVER: -1.4° [OFF]", 22f, TextAlignmentOptions.Center,
                pos: new Vector2(0f, -6f), sizeDelta: new Vector2(600f, 34f));
            angleReadoutText.color = new Color(0.75f, 0.85f, 0.95f);

            powerTelemetryText = CreateText("PowerTelemetry", topPanel.transform,
                "POWER: 230V / 400V (ACTIVE)", 20f, TextAlignmentOptions.Center,
                pos: new Vector2(0f, -42f), sizeDelta: new Vector2(750f, 32f));
            powerTelemetryText.color = Color.yellow;
        }

        // ── Bottom control panel ──────────────────────────────────────
        private void BuildBottomPanel()
        {
            float panelH = 200f;
            GameObject bottomPanel = CreatePanel("BottomControlPanel", transform,
                anchorMin: new Vector2(0f, 0f), anchorMax: new Vector2(1f, 0f),
                pivot: new Vector2(0.5f, 0f),
                anchoredPos: new Vector2(0f, 0f),
                sizeDelta: new Vector2(-SidebarBlockSize.x - 8f, panelH));
            SetPanelColor(bottomPanel, new Color(0.05f, 0.08f, 0.14f, 0.92f));

            // Row 1 — Breaker & Power: ON / OFF / TRIP / MAINS / LOCK
            float row1Y  =  58f;
            float row2Y  = -52f;
            float btnW   = MainButtonSize.x;
            float btnH   = MainButtonSize.y;

            // Distribute 5 buttons across the panel
            float[] xPos = { -450f, -220f, 10f, 240f, 470f };

            buttonTurnOn = CreateButton("BtnOn",   bottomPanel.transform, "ON (68°)",
                new Color(0.12f, 0.62f, 0.22f), new Vector2(xPos[0], row1Y), new Vector2(btnW, btnH), 26f);

            buttonTurnOff = CreateButton("BtnOff", bottomPanel.transform, "OFF (-1.4°)",
                new Color(0.72f, 0.15f, 0.15f), new Vector2(xPos[1], row1Y), new Vector2(btnW, btnH), 26f);

            buttonTrip = CreateButton("BtnTrip",   bottomPanel.transform, "TEST TRIP",
                new Color(0.82f, 0.50f, 0.08f), new Vector2(xPos[2], row1Y), new Vector2(btnW, btnH), 26f);

            buttonTogglePower = CreateButton("BtnPower", bottomPanel.transform, "MAINS: ON",
                new Color(0.18f, 0.42f, 0.72f), new Vector2(xPos[3], row1Y), new Vector2(btnW, btnH), 26f);
            powerButtonLabel = buttonTogglePower.GetComponentInChildren<TextMeshProUGUI>();

            buttonHangCoordinates = CreateButton("BtnHang", bottomPanel.transform, "LOCK IN PLACE",
                new Color(0.42f, 0.22f, 0.72f), new Vector2(xPos[4], row1Y), new Vector2(btnW, btnH), 24f);
            hangButtonLabel = buttonHangCoordinates.GetComponentInChildren<TextMeshProUGUI>();

            // Row 2 — Wire & View controls
            float sW = SmallButtonSize.x;
            float sH = SmallButtonSize.y;

            buttonSpawnWire = CreateButton("BtnSpawnWire", bottomPanel.transform, "+ SPAWN WIRE",
                new Color(0.18f, 0.52f, 0.62f), new Vector2(-450f, row2Y), new Vector2(sW, sH), 22f);

            buttonClearWires = CreateButton("BtnClearWires", bottomPanel.transform, "CLEAR WIRES",
                new Color(0.35f, 0.35f, 0.42f), new Vector2(-150f, row2Y), new Vector2(sW, sH), 22f);

            buttonToggleLabels = CreateButton("BtnLabels", bottomPanel.transform, "TOGGLE LABELS",
                new Color(0.28f, 0.32f, 0.48f), new Vector2(150f, row2Y), new Vector2(sW, sH), 22f);

            buttonRotateModel = CreateButton("BtnRotate", bottomPanel.transform, "ROTATE 90°",
                new Color(0.38f, 0.28f, 0.58f), new Vector2(450f, row2Y), new Vector2(sW, sH), 22f);
        }

        // ── Contactor coil control panel (right-side, vertically stacked) ──
        private void BuildContactorPanel()
        {
            float panelW = 220f;
            float panelH = 320f;

            GameObject ctPanel = CreatePanel("ContactorPanel", transform,
                anchorMin: new Vector2(1f, 0.5f), anchorMax: new Vector2(1f, 0.5f),
                pivot: new Vector2(1f, 0.5f),
                anchoredPos: new Vector2(0f, 0f),
                sizeDelta: new Vector2(panelW, panelH));
            SetPanelColor(ctPanel, new Color(0.05f, 0.12f, 0.08f, 0.94f));

            // Header
            TextMeshProUGUI header = CreateText("ContactorHeader", ctPanel.transform,
                "CONTACTOR", 18f, TextAlignmentOptions.Center,
                new Vector2(0f, 138f), new Vector2(panelW - 8f, 26f));
            header.fontStyle = FontStyles.Bold | FontStyles.SmallCaps;
            header.color = new Color(0.3f, 1f, 0.45f);

            // Status line
            contactorStatusText = CreateText("ContactorStatus", ctPanel.transform,
                "RELEASED   y=0.0069m", 14f, TextAlignmentOptions.Center,
                new Vector2(0f, 110f), new Vector2(panelW - 8f, 24f));
            contactorStatusText.color = new Color(0.9f, 0.3f, 0.25f);

            float btnW = panelW - 16f;
            float btnH = 48f;
            float gap   = 6f;
            float startY = 72f;

            buttonCoilOn = CreateButton("BtnCoilOn", ctPanel.transform, "COIL ON",
                new Color(0.12f, 0.62f, 0.22f), new Vector2(0f, startY), new Vector2(btnW, btnH), 20f);

            buttonCoilOff = CreateButton("BtnCoilOff", ctPanel.transform, "COIL OFF",
                new Color(0.72f, 0.15f, 0.15f), new Vector2(0f, startY - (btnH + gap)), new Vector2(btnW, btnH), 20f);

            buttonCoilToggle = CreateButton("BtnCoilToggle", ctPanel.transform, "TOGGLE",
                new Color(0.18f, 0.42f, 0.72f), new Vector2(0f, startY - 2f * (btnH + gap)), new Vector2(btnW, btnH), 20f);

            buttonManualPress = CreateButton("BtnManualPress", ctPanel.transform, "MANUAL PRESS",
                new Color(0.55f, 0.35f, 0.08f), new Vector2(0f, startY - 3f * (btnH + gap)), new Vector2(btnW, btnH), 18f);

            // Re-bind newly created buttons
            SafeBind(buttonCoilOn,      OnCoilOnClicked);
            SafeBind(buttonCoilOff,     OnCoilOffClicked);
            SafeBind(buttonCoilToggle,  OnCoilToggleClicked);
            SafeBind(buttonManualPress, OnManualPressClicked);
        }

        // ── Left-side scrollable component browser sidebar ────────────
        private void BuildSidebarPanel()
        {
            float sideW = SidebarBlockSize.x + 16f; // small margin

            // Outer panel anchored to left edge, full height
            GameObject sidePanel = CreatePanel("SidebarPanel", transform,
                anchorMin: new Vector2(0f, 0f), anchorMax: new Vector2(0f, 1f),
                pivot: new Vector2(0f, 0.5f),
                anchoredPos: new Vector2(0f, 0f),
                sizeDelta: new Vector2(sideW, 0f));
            SetPanelColor(sidePanel, new Color(0.04f, 0.06f, 0.12f, 0.94f));

            // Scroll view
            GameObject scrollGO = new GameObject("SidebarScroll");
            scrollGO.transform.SetParent(sidePanel.transform, false);
            RectTransform scrollRect = scrollGO.AddComponent<RectTransform>();
            scrollRect.anchorMin = Vector2.zero;
            scrollRect.anchorMax = Vector2.one;
            scrollRect.offsetMin = new Vector2(4f,  4f);
            scrollRect.offsetMax = new Vector2(-4f, -4f);

            ScrollRect scroll = scrollGO.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical   = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.elasticity   = 0.1f;
            scroll.scrollSensitivity = 30f;

            // Viewport
            GameObject viewport = new GameObject("Viewport");
            viewport.transform.SetParent(scrollGO.transform, false);
            RectTransform vpRect = viewport.AddComponent<RectTransform>();
            vpRect.anchorMin = Vector2.zero;
            vpRect.anchorMax = Vector2.one;
            vpRect.offsetMin = Vector2.zero;
            vpRect.offsetMax = Vector2.zero;
            Image vpImg = viewport.AddComponent<Image>();
            vpImg.color = new Color(0f, 0f, 0f, 0.01f); // nearly transparent mask
            Mask mask = viewport.AddComponent<Mask>();
            mask.showMaskGraphic = false;
            scroll.viewport = vpRect;

            // Content container (vertical layout, grows downward)
            GameObject content = new GameObject("Content");
            content.transform.SetParent(viewport.transform, false);
            RectTransform contentRect = content.AddComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot     = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = Vector2.zero;

            VerticalLayoutGroup vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.spacing        = 6f;
            vlg.padding        = new RectOffset(4, 4, 8, 8);
            vlg.childControlWidth  = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;

            ContentSizeFitter csf = content.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.content = contentRect;

            // Header label
            CreateSidebarLabel(content.transform, "COMPONENTS");

            // Generate blocks for each registered component
            int count = componentSpawner != null ? componentSpawner.components.Count : 0;
            if (count == 0)
            {
                // Show placeholders so the sidebar isn't blank at startup
                AddDefaultComponentBlocks(content.transform);
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    ComponentEntry entry = componentSpawner.components[i];
                    AddComponentBlock(content.transform, i, entry.displayName, entry.description, entry.accentColor);
                }
            }
        }

        /// <summary>Adds a small styled header label inside the sidebar.</summary>
        private void CreateSidebarLabel(Transform parent, string text)
        {
            GameObject go = new GameObject("SidebarHeader");
            go.transform.SetParent(parent, false);
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 28f;
            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text      = text;
            tmp.fontSize  = 14f;
            tmp.fontStyle = FontStyles.Bold | FontStyles.SmallCaps;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color     = new Color(0.55f, 0.65f, 0.85f, 1f);
        }

        /// <summary>Fallback blocks when no ComponentSpawner entries exist at build time.</summary>
        private void AddDefaultComponentBlocks(Transform parent)
        {
            (string name, string desc, Color col)[] defaults = {
                ("4-Pole MCB",   "Circuit Breaker", new Color(0.18f, 0.42f, 0.72f)),
                ("Contactor",    "Motor Starter",   new Color(0.22f, 0.58f, 0.35f)),
                ("MCB 2P",       "2-Pole Breaker",  new Color(0.62f, 0.28f, 0.12f)),
                ("Bus Bar",      "Distribution",    new Color(0.55f, 0.18f, 0.55f)),
                ("DIN Rail",     "Mounting Rail",   new Color(0.35f, 0.35f, 0.42f)),
            };

            for (int i = 0; i < defaults.Length; i++)
            {
                var d = defaults[i];
                AddComponentBlock(parent, i, d.name, d.desc, d.col);
            }
        }

        /// <summary>Creates one component block button in the sidebar.</summary>
        private void AddComponentBlock(Transform parent, int index, string name, string desc, Color accent)
        {
            GameObject blockGO = new GameObject($"SidebarBlock_{name}");
            blockGO.transform.SetParent(parent, false);

            LayoutElement le = blockGO.AddComponent<LayoutElement>();
            le.preferredHeight  = SidebarBlockSize.y;
            le.minHeight        = SidebarBlockSize.y;

            Image bg = blockGO.AddComponent<Image>();
            bg.color = new Color(accent.r * 0.55f, accent.g * 0.55f, accent.b * 0.55f, 0.96f);

            Button btn = blockGO.AddComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.normalColor      = bg.color;
            cb.highlightedColor = accent * 1.25f;
            cb.pressedColor     = accent * 0.75f;
            cb.fadeDuration     = 0.08f;
            btn.colors = cb;

            int capturedIndex = index; // capture for lambda
            string capturedName = name;
            btn.onClick.AddListener(() => OnSidebarComponentClicked(capturedIndex, capturedName));
            sidebarButtons.Add(btn);

            // Accent stripe on the left
            GameObject stripe = new GameObject("Stripe");
            stripe.transform.SetParent(blockGO.transform, false);
            RectTransform sr = stripe.AddComponent<RectTransform>();
            sr.anchorMin = new Vector2(0f, 0f);
            sr.anchorMax = new Vector2(0f, 1f);
            sr.pivot     = new Vector2(0f, 0.5f);
            sr.anchoredPosition = Vector2.zero;
            sr.sizeDelta = new Vector2(5f, 0f);
            Image si = stripe.AddComponent<Image>();
            si.color = accent * 1.5f;

            // Component icon placeholder (circle)
            GameObject iconGO = new GameObject("Icon");
            iconGO.transform.SetParent(blockGO.transform, false);
            RectTransform ir = iconGO.AddComponent<RectTransform>();
            ir.anchorMin = new Vector2(0.5f, 1f);
            ir.anchorMax = new Vector2(0.5f, 1f);
            ir.pivot     = new Vector2(0.5f, 1f);
            ir.anchoredPosition = new Vector2(0f, -10f);
            ir.sizeDelta = new Vector2(SidebarBlockSize.x * 0.55f, SidebarBlockSize.x * 0.55f);
            Image iconImg = iconGO.AddComponent<Image>();
            iconImg.color = accent;

            // Name label
            GameObject nameLabelGO = new GameObject("NameLabel");
            nameLabelGO.transform.SetParent(blockGO.transform, false);
            RectTransform nlr = nameLabelGO.AddComponent<RectTransform>();
            nlr.anchorMin = new Vector2(0f, 0.5f);
            nlr.anchorMax = new Vector2(1f, 0.5f);
            nlr.pivot     = new Vector2(0.5f, 1f);
            nlr.anchoredPosition = new Vector2(0f, 0f);
            nlr.sizeDelta = new Vector2(0f, 30f);
            TextMeshProUGUI nameText = nameLabelGO.AddComponent<TextMeshProUGUI>();
            nameText.text      = name;
            nameText.fontSize  = 16f;
            nameText.fontStyle = FontStyles.Bold;
            nameText.alignment = TextAlignmentOptions.Center;
            nameText.color     = Color.white;

            // Description label
            if (!string.IsNullOrEmpty(desc))
            {
                GameObject descGO = new GameObject("DescLabel");
                descGO.transform.SetParent(blockGO.transform, false);
                RectTransform dr = descGO.AddComponent<RectTransform>();
                dr.anchorMin = new Vector2(0f, 0.5f);
                dr.anchorMax = new Vector2(1f, 0.5f);
                dr.pivot     = new Vector2(0.5f, 1f);
                dr.anchoredPosition = new Vector2(0f, -28f);
                dr.sizeDelta = new Vector2(0f, 22f);
                TextMeshProUGUI descText = descGO.AddComponent<TextMeshProUGUI>();
                descText.text      = desc;
                descText.fontSize  = 12f;
                descText.alignment = TextAlignmentOptions.Center;
                descText.color     = new Color(0.8f, 0.88f, 1f, 0.85f);
            }
        }
        #endregion

        #region UI Factory Helpers
        private GameObject CreatePanel(string name, Transform parent,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 anchoredPos, Vector2 sizeDelta)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform r = go.AddComponent<RectTransform>();
            r.anchorMin       = anchorMin;
            r.anchorMax       = anchorMax;
            r.pivot           = pivot;
            r.anchoredPosition = anchoredPos;
            r.sizeDelta       = sizeDelta;
            go.AddComponent<Image>();
            return go;
        }

        private static void SetPanelColor(GameObject panel, Color color)
        {
            Image img = panel.GetComponent<Image>();
            if (img != null) img.color = color;
        }

        private TextMeshProUGUI CreateText(string name, Transform parent,
            string text, float size, TextAlignmentOptions align,
            Vector2 pos, Vector2 sizeDelta)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform r = go.AddComponent<RectTransform>();
            r.anchoredPosition = pos;
            r.sizeDelta        = sizeDelta;
            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text      = text;
            tmp.fontSize  = size;
            tmp.alignment = align;
            return tmp;
        }

        private Button CreateButton(string name, Transform parent,
            string label, Color color, Vector2 pos, Vector2 size, float fontSize = 22f)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform r = go.AddComponent<RectTransform>();
            r.anchoredPosition = pos;
            r.sizeDelta        = size;

            // Rounded-look background
            Image img = go.AddComponent<Image>();
            img.color = color;

            Button btn = go.AddComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.normalColor      = color;
            cb.highlightedColor = color * 1.30f;
            cb.pressedColor     = color * 0.75f;
            cb.fadeDuration     = 0.08f;
            btn.colors = cb;

            // Child text
            TextMeshProUGUI tmp = CreateText("Text", go.transform, label, fontSize,
                TextAlignmentOptions.Center, Vector2.zero, size);
            tmp.fontStyle = FontStyles.Bold;
            tmp.color     = Color.white;

            return btn;
        }
        #endregion
    }
}
