using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using ContactorAR.Breaker;
using ContactorAR.Wiring;
using ContactorAR.Simulation;
using ContactorAR.AR;
using ContactorAR.SelectionSystem;
using ContactorAR.UndoSystem;
using ContactorAR.Interaction;

namespace ContactorAR.UI
{
    /// <summary>
    /// Modern, mobile-first, dark glass-morphism portrait UI for the Contactor AR Simulator.
    /// Implements:
    /// 1. Top status bar: slim, single-line telemetry (breaker state chip, voltage, load) + expandable details.
    /// 2. Floating Action Button (FAB "+"): opens smooth slide-in left sidebar with component cards.
    /// 3. Bottom toolbar: exactly 4 primary buttons (Select/Move, Wire, Labels, More ⋯).
    /// 4. "More" bottom sheet: slide-up sheet for Mains supply, Test trip, Lock placement, DIN rail snap, Clear wires, Reset.
    /// 5. Contextual control bar: floating bar anchored near/above bottom toolbar for selected MCB, Contactor, or Wire.
    /// 6. Undo snackbar: 5-second notification with [UNDO] action.
    /// </summary>
    public class ARContactorUI : MonoBehaviour
    {
        // ──────────── Palette (Visual Design System) ────────────
        private static readonly Color BgDark       = new Color32(18, 21, 28, 225);   // #12151C at ~88% opacity
        private static readonly Color SurfaceDark  = new Color32(28, 33, 43, 245);   // #1C212B
        private static readonly Color SurfaceLight = new Color32(40, 48, 62, 255);
        private static readonly Color AccentBlue   = new Color32(59, 130, 246, 255); // #3B82F6
        private static readonly Color SuccessGreen = new Color32(34, 197, 94, 255);  // #22C55E
        private static readonly Color DangerRed    = new Color32(239, 68, 68, 255);  // #EF4444
        private static readonly Color WarningAmber = new Color32(245, 158, 11, 255); // #F59E0B
        private static readonly Color TextLight    = new Color32(229, 231, 235, 255);// #E5E7EB
        private static readonly Color TextMuted    = new Color32(156, 163, 175, 255);// #9CA3AF
        private static readonly Color BorderSubtle = new Color32(60, 70, 90, 80);

        [Header("Scene References")]
        [SerializeField] private ContactorController contactor;
        [SerializeField] private ARModelManipulator manipulator;
        [SerializeField] private PowerCircuitSimulator simulator;
        [SerializeField] private WireSpawner wireSpawner;
        [SerializeField] private ComponentSpawner componentSpawner;
        [SerializeField] private ContactorArmatureController armatureController;

        // UI Root & Safe Area
        private Canvas rootCanvas;
        private RectTransform safeAreaRect;

        // Top Status Bar
        private RectTransform topStatusBarRect;
        private Image breakerChipBg;
        private TextMeshProUGUI breakerChipText;
        private TextMeshProUGUI powerTelemetryText;
        private TextMeshProUGUI loadTelemetryText;
        private GameObject detailsDrawer;
        private TextMeshProUGUI detailsText;
        private bool isDetailsDrawerOpen = false;

        // FAB (+) and Left Sidebar
        private Button fabAddButton;
        private GameObject sidebarBackdrop;
        private RectTransform sidebarRect;
        private bool isSidebarOpen = false;
        private Coroutine sidebarAnimationCo;

        // Bottom Toolbar (4 primary buttons)
        private Button btnModeSelect;
        private Button btnModeWire;
        private Button btnToggleLabels;
        private Button btnMore;
        private Image btnModeSelectBg;
        private Image btnModeWireBg;
        private Image btnToggleLabelsBg;
        private TextMeshProUGUI btnModeSelectText;
        private TextMeshProUGUI btnModeWireText;
        private TextMeshProUGUI btnToggleLabelsText;

        // "More" Bottom Sheet
        private GameObject bottomSheetBackdrop;
        private RectTransform bottomSheetRect;
        private bool isBottomSheetOpen = false;
        private Coroutine bottomSheetAnimationCo;
        private TextMeshProUGUI btnSheetMainsText;
        private TextMeshProUGUI btnSheetSnapText;
        private TextMeshProUGUI btnSheetLockText;

        // Contextual Floating Bar
        private RectTransform contextBarRect;
        private TextMeshProUGUI contextTitleText;
        private GameObject contextMcbContainer;
        private GameObject contextContactorContainer;
        private GameObject contextWireContainer;
        private Button contextBtnMcbToggle;
        private TextMeshProUGUI contextBtnMcbToggleText;
        private Button contextBtnMcbTrip;
        private Button contextBtnRotate;
        private Button contextBtnDelete;
        private Button contextBtnWireDelete;
        private Button contextBtnCoilOn;
        private Button contextBtnCoilOff;
        private Button contextBtnCoilToggle;
        private Button contextBtnManualPress;
        private TextMeshProUGUI contextBtnManualPressText;
        private bool manualPressHeld = false;

        // Confirmation Modal
        private GameObject confirmModal;
        private TextMeshProUGUI confirmModalText;
        private Action onConfirmAction;

        // Undo Snackbar
        private RectTransform snackbarRect;
        private TextMeshProUGUI snackbarText;
        private Button snackbarUndoBtn;
        private Coroutine snackbarDismissCo;

        // Cached procedural sprites
        private static Sprite roundedSprite;
        private static Sprite circleSprite;

        // Modes
        public enum AppInteractionMode { SelectMove, Wire }
        private AppInteractionMode currentMode = AppInteractionMode.SelectMove;

        private void Awake()
        {
            EnsureProceduralSprites();
            EnsureCanvasSetup();
            FindSceneReferencesIfNull();
            BuildMobilePortraitUI();
        }

        /// <summary>
        /// Makes sure this component is inside a properly configured ScreenSpaceOverlay Canvas.
        /// If no Canvas exists, creates a dedicated ARContactorUI_Canvas root.
        /// </summary>
        private void EnsureCanvasSetup()
        {
            // Ensure EventSystem exists
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                GameObject esGO = new GameObject("EventSystem");
                esGO.AddComponent<EventSystem>();
                esGO.AddComponent<StandaloneInputModule>();
            }

            // Already have a Canvas somewhere in hierarchy?
            rootCanvas = GetComponentInParent<Canvas>();
            if (rootCanvas == null)
                rootCanvas = GetComponent<Canvas>();

            if (rootCanvas != null)
            {
                // Reconfigure existing canvas
                rootCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                if (rootCanvas.sortingOrder < 100) rootCanvas.sortingOrder = 100;
                CanvasScaler existingScaler = rootCanvas.GetComponent<CanvasScaler>();
                if (existingScaler != null)
                {
                    existingScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                    existingScaler.referenceResolution = new Vector2(1080, 1920);
                    existingScaler.matchWidthOrHeight = 0.5f;
                }
                return;
            }

            // No Canvas found — create a dedicated ScreenSpaceOverlay Canvas root
            Debug.Log("[ARContactorUI] Creating ARContactorUI_Canvas root.");
            GameObject canvasGO = new GameObject("ARContactorUI_Canvas");
            Canvas canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            canvasGO.AddComponent<GraphicRaycaster>();
            rootCanvas = canvas;
        }

        private void Start()
        {
            BindEventListeners();
            UpdateAllTelemetry();
            UpdateContextBarVisibility();
        }

        private void OnEnable()
        {
            SelectionManager.OnModelSelected += HandleModelSelected;
            SelectionManager.OnModelDeselected += HandleModelDeselected;
            SelectionManager.OnWireSelected += HandleWireSelected;
            SelectionManager.OnWireDeselected += HandleWireDeselected;
            SelectionManager.OnSelectionCleared += HandleSelectionCleared;
            UndoService.OnActionRecorded += HandleUndoActionRecorded;
        }

        private void OnDisable()
        {
            SelectionManager.OnModelSelected -= HandleModelSelected;
            SelectionManager.OnModelDeselected -= HandleModelDeselected;
            SelectionManager.OnWireSelected -= HandleWireSelected;
            SelectionManager.OnWireDeselected -= HandleWireDeselected;
            SelectionManager.OnSelectionCleared -= HandleSelectionCleared;
            UndoService.OnActionRecorded -= HandleUndoActionRecorded;
        }

        private void Update()
        {
            UpdateAllTelemetry();
            UpdateContextBarDynamicState();
            ApplySafeArea();
        }

        public void FindSceneReferencesIfNull()
        {
            if (contactor == null)         contactor         = FindAnyObjectByType<ContactorController>();
            if (manipulator == null)       manipulator       = FindAnyObjectByType<ARModelManipulator>();
            if (simulator == null)         simulator         = FindAnyObjectByType<PowerCircuitSimulator>();
            if (wireSpawner == null)       wireSpawner       = FindAnyObjectByType<WireSpawner>();
            if (componentSpawner == null)  componentSpawner  = FindAnyObjectByType<ComponentSpawner>();
            if (componentSpawner == null)  componentSpawner  = gameObject.AddComponent<ComponentSpawner>();
            if (armatureController == null) armatureController = FindAnyObjectByType<ContactorArmatureController>();

            if (SelectionManager.Instance == null)
            {
                GameObject sm = new GameObject("SelectionManager");
                sm.AddComponent<SelectionManager>();
            }
            if (UndoService.Instance == null)
            {
                GameObject us = new GameObject("UndoService");
                us.AddComponent<UndoService>();
            }
            if (ModelDragController.Instance == null)
            {
                GameObject dc = new GameObject("ModelDragController");
                dc.AddComponent<ModelDragController>();
            }
            if (ModelRotationController.Instance == null)
            {
                GameObject rc = new GameObject("ModelRotationController");
                rc.AddComponent<ModelRotationController>();
            }
        }

        #region Safe Area Handling
        private Rect lastSafeArea = Rect.zero;
        private void ApplySafeArea()
        {
            if (safeAreaRect == null) return;
            Rect safeArea = Screen.safeArea;
            if (safeArea == lastSafeArea) return;
            lastSafeArea = safeArea;

            Vector2 minAnchor = safeArea.position;
            Vector2 maxAnchor = minAnchor + safeArea.size;
            minAnchor.x /= Screen.width;
            minAnchor.y /= Screen.height;
            maxAnchor.x /= Screen.width;
            maxAnchor.y /= Screen.height;

            safeAreaRect.anchorMin = minAnchor;
            safeAreaRect.anchorMax = maxAnchor;
            safeAreaRect.offsetMin = Vector2.zero;
            safeAreaRect.offsetMax = Vector2.zero;
        }
        #endregion

        #region UI Construction
        private void BuildMobilePortraitUI()
        {
            Transform parentContainer = (rootCanvas != null) ? rootCanvas.transform : transform;

            // Remove any legacy runtime panels if re-running
            for (int i = parentContainer.childCount - 1; i >= 0; i--)
            {
                Destroy(parentContainer.GetChild(i).gameObject);
            }

            // Safe area container
            GameObject safeGO = new GameObject("SafeAreaContainer", typeof(RectTransform));
            safeGO.transform.SetParent(parentContainer, false);
            safeAreaRect = safeGO.GetComponent<RectTransform>();
            safeAreaRect.anchorMin = Vector2.zero;
            safeAreaRect.anchorMax = Vector2.one;
            safeAreaRect.offsetMin = Vector2.zero;
            safeAreaRect.offsetMax = Vector2.zero;
            ApplySafeArea();

            // Build individual modern UI layers
            BuildTopStatusBar(safeAreaRect);
            BuildDetailsDrawer(safeAreaRect);
            BuildContextualBar(safeAreaRect);
            BuildBottomToolbar(safeAreaRect);
            BuildFABAddButton(safeAreaRect);
            BuildLeftSidebar(safeAreaRect);
            BuildBottomSheet(safeAreaRect);
            BuildConfirmModal(safeAreaRect);
            BuildUndoSnackbar(safeAreaRect);
        }

        // 1. Top Status Bar (Slim, translucent, single row)
        private void BuildTopStatusBar(Transform parent)
        {
            GameObject barGO = CreateGlassPanel("TopStatusBar", parent,
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(1f, 1f),
                pivot: new Vector2(0.5f, 1f),
                pos: new Vector2(0f, -12f),
                sizeDelta: new Vector2(-40f, 56f),
                bgColor: BgDark, cornerRadius: 18);
            topStatusBarRect = barGO.GetComponent<RectTransform>();

            // Button to tap-to-expand details
            Button barBtn = barGO.AddComponent<Button>();
            barBtn.transition = Selectable.Transition.None;
            barBtn.onClick.AddListener(ToggleDetailsDrawer);

            HorizontalLayoutGroup hlg = barGO.AddComponent<HorizontalLayoutGroup>();
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.spacing = 16f;
            hlg.padding = new RectOffset(16, 16, 6, 6);
            hlg.childControlWidth = false;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;

            // Breaker State Chip (Pill badge)
            GameObject chipGO = CreateGlassPanel("BreakerChip", barGO.transform,
                anchorMin: new Vector2(0f, 0.5f), anchorMax: new Vector2(0f, 0.5f),
                pivot: new Vector2(0f, 0.5f),
                pos: Vector2.zero, sizeDelta: new Vector2(180f, 40f),
                bgColor: DangerRed, cornerRadius: 14);
            breakerChipBg = chipGO.GetComponent<Image>();
            breakerChipText = CreateText("ChipText", chipGO.transform, "● OFF", 17f, FontStyles.Bold, TextLight, TextAlignmentOptions.Center);
            RectTransform chipTextRect = breakerChipText.GetComponent<RectTransform>();
            chipTextRect.anchorMin = Vector2.zero; chipTextRect.anchorMax = Vector2.one;
            chipTextRect.offsetMin = Vector2.zero; chipTextRect.offsetMax = Vector2.zero;

            // Power Readout
            powerTelemetryText = CreateText("PowerTelemetry", barGO.transform, "230V / 400V", 18f, FontStyles.Bold, TextLight, TextAlignmentOptions.Center);
            LayoutElement lePower = powerTelemetryText.gameObject.AddComponent<LayoutElement>();
            lePower.preferredWidth = 240f;

            // Separator dot
            TextMeshProUGUI dot = CreateText("Dot", barGO.transform, "•", 16f, FontStyles.Normal, TextMuted, TextAlignmentOptions.Center);
            LayoutElement leDot = dot.gameObject.AddComponent<LayoutElement>();
            leDot.preferredWidth = 16f;

            // Load Readout
            loadTelemetryText = CreateText("LoadTelemetry", barGO.transform, "3.2A", 18f, FontStyles.Bold, WarningAmber, TextAlignmentOptions.Center);
            LayoutElement leLoad = loadTelemetryText.gameObject.AddComponent<LayoutElement>();
            leLoad.preferredWidth = 140f;

            // Expand icon hint
            TextMeshProUGUI expandIcon = CreateText("ExpandHint", barGO.transform, "▼", 14f, FontStyles.Normal, TextMuted, TextAlignmentOptions.Center);
            LayoutElement leExp = expandIcon.gameObject.AddComponent<LayoutElement>();
            leExp.preferredWidth = 30f;
        }

        // Details drawer that drops down below TopStatusBar
        private void BuildDetailsDrawer(Transform parent)
        {
            detailsDrawer = CreateGlassPanel("DetailsDrawer", parent,
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(1f, 1f),
                pivot: new Vector2(0.5f, 1f),
                pos: new Vector2(0f, -76f),
                sizeDelta: new Vector2(-40f, 120f),
                bgColor: SurfaceDark, cornerRadius: 18);
            detailsDrawer.SetActive(false);

            detailsText = CreateText("DetailsContent", detailsDrawer.transform,
                "LEVER: -70.0° [OFF]\nMAINS: 230V / 400V (3-PHASE ACTIVE)\nCONTACTOR: RELEASED | MOTOR: IDLE",
                16f, FontStyles.Normal, TextLight, TextAlignmentOptions.Left);
            RectTransform dtRect = detailsText.GetComponent<RectTransform>();
            dtRect.anchorMin = Vector2.zero; dtRect.anchorMax = Vector2.one;
            dtRect.offsetMin = new Vector2(20f, 10f); dtRect.offsetMax = new Vector2(-20f, -10f);
        }

        private void ToggleDetailsDrawer()
        {
            isDetailsDrawerOpen = !isDetailsDrawerOpen;
            if (detailsDrawer != null) detailsDrawer.SetActive(isDetailsDrawerOpen);
        }

        // 2. Floating Action Button (FAB "+") bottom-right
        private void BuildFABAddButton(Transform parent)
        {
            GameObject fabGO = CreateGlassPanel("FAB_AddButton", parent,
                anchorMin: new Vector2(1f, 0f), anchorMax: new Vector2(1f, 0f),
                pivot: new Vector2(1f, 0f),
                pos: new Vector2(-28f, 110f),
                sizeDelta: new Vector2(68f, 68f),
                bgColor: AccentBlue, cornerRadius: 34);

            fabAddButton = fabGO.AddComponent<Button>();
            fabAddButton.onClick.AddListener(OpenLeftSidebar);

            TextMeshProUGUI plusText = CreateText("PlusIcon", fabGO.transform, "+", 38f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center);
            RectTransform ptRect = plusText.GetComponent<RectTransform>();
            ptRect.anchorMin = Vector2.zero; ptRect.anchorMax = Vector2.one;
            ptRect.offsetMin = new Vector2(0f, 2f); ptRect.offsetMax = new Vector2(0f, 2f);
        }

        // Left Sidebar (Hidden by default, slides in 0.25s)
        private void BuildLeftSidebar(Transform parent)
        {
            // Dimmed backdrop
            sidebarBackdrop = new GameObject("SidebarBackdrop");
            sidebarBackdrop.transform.SetParent(parent, false);
            RectTransform bdRect = sidebarBackdrop.AddComponent<RectTransform>();
            bdRect.anchorMin = Vector2.zero; bdRect.anchorMax = Vector2.one;
            bdRect.offsetMin = Vector2.zero; bdRect.offsetMax = Vector2.zero;
            Image bdImg = sidebarBackdrop.AddComponent<Image>();
            bdImg.color = new Color(0f, 0f, 0f, 0.65f);
            Button bdBtn = sidebarBackdrop.AddComponent<Button>();
            bdBtn.transition = Selectable.Transition.None;
            bdBtn.onClick.AddListener(CloseLeftSidebar);
            sidebarBackdrop.SetActive(false);

            // Left Sidebar panel
            float sideW = 340f;
            GameObject sideGO = CreateGlassPanel("LeftSidebar", sidebarBackdrop.transform,
                anchorMin: new Vector2(0f, 0f), anchorMax: new Vector2(0f, 1f),
                pivot: new Vector2(0f, 0.5f),
                pos: new Vector2(-sideW, 0f),
                sizeDelta: new Vector2(sideW, 0f),
                bgColor: BgDark, cornerRadius: 0);
            sidebarRect = sideGO.GetComponent<RectTransform>();

            // Header row
            GameObject headerGO = new GameObject("HeaderRow");
            headerGO.transform.SetParent(sideGO.transform, false);
            RectTransform hrRect = headerGO.AddComponent<RectTransform>();
            hrRect.anchorMin = new Vector2(0f, 1f); hrRect.anchorMax = new Vector2(1f, 1f);
            hrRect.pivot = new Vector2(0.5f, 1f);
            hrRect.anchoredPosition = new Vector2(0f, -12f);
            hrRect.sizeDelta = new Vector2(-24f, 50f);

            TextMeshProUGUI headerTitle = CreateText("HeaderTitle", headerGO.transform, "COMPONENTS", 20f, FontStyles.Bold, TextLight, TextAlignmentOptions.MidlineLeft);
            RectTransform htRect = headerTitle.GetComponent<RectTransform>();
            htRect.anchorMin = new Vector2(0f, 0f); htRect.anchorMax = new Vector2(0.8f, 1f);
            htRect.offsetMin = Vector2.zero; htRect.offsetMax = Vector2.zero;

            // Close (X) button
            GameObject closeBtnGO = new GameObject("CloseBtn");
            closeBtnGO.transform.SetParent(headerGO.transform, false);
            RectTransform cbRect = closeBtnGO.AddComponent<RectTransform>();
            cbRect.anchorMin = new Vector2(0.82f, 0f); cbRect.anchorMax = new Vector2(1f, 1f);
            cbRect.offsetMin = Vector2.zero; cbRect.offsetMax = Vector2.zero;
            Button closeBtn = closeBtnGO.AddComponent<Button>();
            closeBtn.onClick.AddListener(CloseLeftSidebar);
            TextMeshProUGUI closeTxt = CreateText("X", closeBtnGO.transform, "✕", 24f, FontStyles.Bold, TextMuted, TextAlignmentOptions.Center);
            RectTransform ctxtRect = closeTxt.GetComponent<RectTransform>();
            ctxtRect.anchorMin = Vector2.zero; ctxtRect.anchorMax = Vector2.one;
            ctxtRect.offsetMin = Vector2.zero; ctxtRect.offsetMax = Vector2.zero;

            // Scroll container for component cards
            GameObject scrollGO = new GameObject("CardScroll");
            scrollGO.transform.SetParent(sideGO.transform, false);
            RectTransform scrRect = scrollGO.AddComponent<RectTransform>();
            scrRect.anchorMin = new Vector2(0f, 0f); scrRect.anchorMax = new Vector2(1f, 1f);
            scrRect.offsetMin = new Vector2(12f, 16f); scrRect.offsetMax = new Vector2(-12f, -74f);

            ScrollRect scroll = scrollGO.AddComponent<ScrollRect>();
            scroll.horizontal = false; scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            GameObject contentGO = new GameObject("Content");
            contentGO.transform.SetParent(scrollGO.transform, false);
            RectTransform cntRect = contentGO.AddComponent<RectTransform>();
            cntRect.anchorMin = new Vector2(0f, 1f); cntRect.anchorMax = new Vector2(1f, 1f);
            cntRect.pivot = new Vector2(0.5f, 1f);
            cntRect.anchoredPosition = Vector2.zero;
            cntRect.sizeDelta = new Vector2(0f, 0f);

            VerticalLayoutGroup vlg = contentGO.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.spacing = 10f;
            vlg.childControlWidth = true; vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;

            ContentSizeFitter csf = contentGO.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = cntRect;

            // Add the 5 component cards
            (string name, string desc, Color accent, int idx)[] items = {
                ("4-Pole MCB", "Main Circuit Breaker", AccentBlue, 0),
                ("Contactor", "3-Phase Motor Starter", SuccessGreen, 1),
                ("MCB 2P", "2-Pole Branch Breaker", WarningAmber, 2),
                ("Bus Bar", "Distribution Busbar", new Color32(168, 85, 247, 255), 3),
                ("DIN Rail", "Mounting Rail", new Color32(100, 116, 139, 255), 4)
            };

            for (int i = 0; i < items.Length; i++)
            {
                var it = items[i];
                BuildComponentCard(contentGO.transform, it.name, it.desc, it.accent, it.idx);
            }
        }

        private void BuildComponentCard(Transform parent, string title, string subtitle, Color accent, int componentIdx)
        {
            GameObject cardGO = CreateGlassPanel($"Card_{title}", parent,
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(1f, 1f),
                pivot: new Vector2(0.5f, 1f),
                pos: Vector2.zero, sizeDelta: new Vector2(0f, 76f),
                bgColor: SurfaceDark, cornerRadius: 16);

            LayoutElement le = cardGO.AddComponent<LayoutElement>();
            le.preferredHeight = 76f;
            le.minHeight = 76f;

            Button cardBtn = cardGO.AddComponent<Button>();
            cardBtn.onClick.AddListener(() => OnComponentCardClicked(componentIdx, title));

            // Accent stripe on the left
            GameObject stripe = CreateGlassPanel("Stripe", cardGO.transform,
                anchorMin: new Vector2(0f, 0f), anchorMax: new Vector2(0f, 1f),
                pivot: new Vector2(0f, 0.5f),
                pos: new Vector2(3f, 0f), sizeDelta: new Vector2(5f, -12f),
                bgColor: accent, cornerRadius: 3);

            // Title
            TextMeshProUGUI titleTxt = CreateText("Title", cardGO.transform, title, 17f, FontStyles.Bold, TextLight, TextAlignmentOptions.MidlineLeft);
            RectTransform trRect = titleTxt.GetComponent<RectTransform>();
            trRect.anchorMin = new Vector2(0f, 0.5f); trRect.anchorMax = new Vector2(1f, 1f);
            trRect.offsetMin = new Vector2(24f, 0f); trRect.offsetMax = new Vector2(-12f, 0f);

            // Subtitle
            TextMeshProUGUI subTxt = CreateText("Subtitle", cardGO.transform, subtitle, 12f, FontStyles.Normal, TextMuted, TextAlignmentOptions.MidlineLeft);
            RectTransform srRect = subTxt.GetComponent<RectTransform>();
            srRect.anchorMin = new Vector2(0f, 0f); trRect.anchorMax = new Vector2(1f, 0.5f);
            srRect.offsetMin = new Vector2(24f, 6f); srRect.offsetMax = new Vector2(-12f, 0f);
        }

        private void OnComponentCardClicked(int index, string componentName)
        {
            CloseLeftSidebar();
            if (componentSpawner == null) componentSpawner = FindAnyObjectByType<ComponentSpawner>();
            if (componentSpawner != null)
            {
                GameObject spawned = componentSpawner.SpawnComponent(index, componentName);
                if (spawned != null)
                {
                    ARModelManipulator manip = spawned.GetComponent<ARModelManipulator>();
                    if (manip != null)
                    {
                        SelectionManager.Select(manip);
                    }
                }
            }
        }

        private void OpenLeftSidebar()
        {
            if (isSidebarOpen) return;
            isSidebarOpen = true;
            sidebarBackdrop.SetActive(true);
            if (sidebarAnimationCo != null) StopCoroutine(sidebarAnimationCo);
            sidebarAnimationCo = StartCoroutine(AnimateRectX(sidebarRect, -340f, 0f, 0.25f));
        }

        private void CloseLeftSidebar()
        {
            if (!isSidebarOpen) return;
            isSidebarOpen = false;
            if (sidebarAnimationCo != null) StopCoroutine(sidebarAnimationCo);
            sidebarAnimationCo = StartCoroutine(AnimateRectX(sidebarRect, 0f, -340f, 0.25f, () => {
                sidebarBackdrop.SetActive(false);
            }));
        }

        // 3. Bottom Toolbar (ONLY 4 primary buttons: Select/Move, Wire, Labels, More ⋯)
        private void BuildBottomToolbar(Transform parent)
        {
            GameObject barGO = CreateGlassPanel("BottomToolbar", parent,
                anchorMin: new Vector2(0f, 0f), anchorMax: new Vector2(1f, 0f),
                pivot: new Vector2(0.5f, 0f),
                pos: new Vector2(0f, 16f),
                sizeDelta: new Vector2(-40f, 76f),
                bgColor: BgDark, cornerRadius: 22);

            HorizontalLayoutGroup hlg = barGO.AddComponent<HorizontalLayoutGroup>();
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.spacing = 10f;
            hlg.padding = new RectOffset(12, 12, 8, 8);
            hlg.childControlWidth = true; hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true; hlg.childForceExpandHeight = true;

            // 1. Select / Move
            btnModeSelect = CreateToolbarButton(barGO.transform, "Select", "✥", out btnModeSelectBg, out btnModeSelectText, OnToolbarSelectClicked);
            // 2. Wire
            btnModeWire = CreateToolbarButton(barGO.transform, "Wire", "☍", out btnModeWireBg, out btnModeWireText, OnToolbarWireClicked);
            // 3. Labels
            btnToggleLabels = CreateToolbarButton(barGO.transform, "Labels", "🏷", out btnToggleLabelsBg, out btnToggleLabelsText, OnToolbarLabelsClicked);
            // 4. More (⋯)
            btnMore = CreateToolbarButton(barGO.transform, "More", "⋯", out Image moreBg, out TextMeshProUGUI moreTxt, OnToolbarMoreClicked);

            UpdateToolbarVisuals();
        }

        private Button CreateToolbarButton(Transform parent, string label, string icon, out Image bgImage, out TextMeshProUGUI textComp, UnityEngine.Events.UnityAction action)
        {
            GameObject btnGO = CreateGlassPanel($"Btn_{label}", parent,
                anchorMin: Vector2.zero, anchorMax: Vector2.one,
                pivot: new Vector2(0.5f, 0.5f),
                pos: Vector2.zero, sizeDelta: Vector2.zero,
                bgColor: SurfaceDark, cornerRadius: 16);
            bgImage = btnGO.GetComponent<Image>();

            Button btn = btnGO.AddComponent<Button>();
            btn.onClick.AddListener(action);

            textComp = CreateText("Label", btnGO.transform, $"{icon}\n{label}", 14f, FontStyles.Bold, TextLight, TextAlignmentOptions.Center);
            RectTransform tr = textComp.GetComponent<RectTransform>();
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = Vector2.zero; tr.offsetMax = Vector2.zero;

            return btn;
        }

        private void UpdateToolbarVisuals()
        {
            if (btnModeSelectBg != null)
                btnModeSelectBg.color = (currentMode == AppInteractionMode.SelectMove) ? AccentBlue : SurfaceDark;
            if (btnModeWireBg != null)
                btnModeWireBg.color = (currentMode == AppInteractionMode.Wire) ? AccentBlue : SurfaceDark;
            if (btnToggleLabelsBg != null)
                btnToggleLabelsBg.color = TerminalWorldLabel.GlobalLabelsEnabled ? AccentBlue : SurfaceDark;
        }

        private void OnToolbarSelectClicked()
        {
            currentMode = AppInteractionMode.SelectMove;
            UpdateToolbarVisuals();
        }

        private void OnToolbarWireClicked()
        {
            currentMode = AppInteractionMode.Wire;
            UpdateToolbarVisuals();
            if (wireSpawner != null) wireSpawner.SpawnNextWire();
        }

        private void OnToolbarLabelsClicked()
        {
            TerminalWorldLabel.GlobalLabelsEnabled = !TerminalWorldLabel.GlobalLabelsEnabled;
            UpdateToolbarVisuals();

            // Refresh all scene labels
            TerminalWorldLabel[] labels = FindObjectsByType<TerminalWorldLabel>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < labels.Length; i++)
            {
                labels[i].SetVisible(TerminalWorldLabel.GlobalLabelsEnabled);
            }
        }

        private void OnToolbarMoreClicked()
        {
            OpenBottomSheet();
        }

        // 4. "More" Bottom Sheet
        private void BuildBottomSheet(Transform parent)
        {
            // Dimmed backdrop
            bottomSheetBackdrop = new GameObject("BottomSheetBackdrop");
            bottomSheetBackdrop.transform.SetParent(parent, false);
            RectTransform bdRect = bottomSheetBackdrop.AddComponent<RectTransform>();
            bdRect.anchorMin = Vector2.zero; bdRect.anchorMax = Vector2.one;
            bdRect.offsetMin = Vector2.zero; bdRect.offsetMax = Vector2.zero;
            Image bdImg = bottomSheetBackdrop.AddComponent<Image>();
            bdImg.color = new Color(0f, 0f, 0f, 0.65f);
            Button bdBtn = bottomSheetBackdrop.AddComponent<Button>();
            bdBtn.transition = Selectable.Transition.None;
            bdBtn.onClick.AddListener(CloseBottomSheet);
            bottomSheetBackdrop.SetActive(false);

            // Bottom sheet panel
            float sheetH = 430f;
            GameObject sheetGO = CreateGlassPanel("BottomSheet", bottomSheetBackdrop.transform,
                anchorMin: new Vector2(0f, 0f), anchorMax: new Vector2(1f, 0f),
                pivot: new Vector2(0.5f, 0f),
                pos: new Vector2(0f, -sheetH),
                sizeDelta: new Vector2(0f, sheetH),
                bgColor: BgDark, cornerRadius: 24);
            bottomSheetRect = sheetGO.GetComponent<RectTransform>();

            // Header
            GameObject hr = new GameObject("SheetHeader");
            hr.transform.SetParent(sheetGO.transform, false);
            RectTransform hrRect = hr.AddComponent<RectTransform>();
            hrRect.anchorMin = new Vector2(0f, 1f); hrRect.anchorMax = new Vector2(1f, 1f);
            hrRect.pivot = new Vector2(0.5f, 1f);
            hrRect.anchoredPosition = new Vector2(0f, -12f);
            hrRect.sizeDelta = new Vector2(-40f, 44f);

            TextMeshProUGUI titleTxt = CreateText("Title", hr.transform, "SCENE CONTROLS", 18f, FontStyles.Bold, TextLight, TextAlignmentOptions.MidlineLeft);
            RectTransform ttRect = titleTxt.GetComponent<RectTransform>();
            ttRect.anchorMin = new Vector2(0f, 0f); ttRect.anchorMax = new Vector2(0.85f, 1f);
            ttRect.offsetMin = Vector2.zero; ttRect.offsetMax = Vector2.zero;

            GameObject closeBtnGO = new GameObject("CloseBtn");
            closeBtnGO.transform.SetParent(hr.transform, false);
            RectTransform cbRect = closeBtnGO.AddComponent<RectTransform>();
            cbRect.anchorMin = new Vector2(0.85f, 0f); cbRect.anchorMax = new Vector2(1f, 1f);
            cbRect.offsetMin = Vector2.zero; cbRect.offsetMax = Vector2.zero;
            Button closeBtn = closeBtnGO.AddComponent<Button>();
            closeBtn.onClick.AddListener(CloseBottomSheet);
            TextMeshProUGUI closeTxt = CreateText("X", closeBtnGO.transform, "✕", 22f, FontStyles.Bold, TextMuted, TextAlignmentOptions.Center);
            RectTransform ctxtRect = closeTxt.GetComponent<RectTransform>();
            ctxtRect.anchorMin = Vector2.zero; ctxtRect.anchorMax = Vector2.one;
            ctxtRect.offsetMin = Vector2.zero; ctxtRect.offsetMax = Vector2.zero;

            // Grid container for sheet actions
            GameObject gridGO = new GameObject("ActionGrid");
            gridGO.transform.SetParent(sheetGO.transform, false);
            RectTransform grRect = gridGO.AddComponent<RectTransform>();
            grRect.anchorMin = new Vector2(0f, 0f); grRect.anchorMax = new Vector2(1f, 1f);
            grRect.offsetMin = new Vector2(24f, 20f); grRect.offsetMax = new Vector2(-24f, -66f);

            GridLayoutGroup glg = gridGO.AddComponent<GridLayoutGroup>();
            glg.cellSize = new Vector2(495f, 62f);
            glg.spacing = new Vector2(16f, 12f);
            glg.startCorner = GridLayoutGroup.Corner.UpperLeft;
            glg.startAxis = GridLayoutGroup.Axis.Horizontal;
            glg.childAlignment = TextAnchor.UpperCenter;

            // Sheet Buttons
            CreateSheetActionButton(gridGO.transform, "Mains Power", "MAINS: ON", out btnSheetMainsText, SurfaceDark, OnSheetToggleMainsClicked);
            CreateSheetActionButton(gridGO.transform, "Test Trip", "TEST TRIP", out _, WarningAmber, OnSheetTestTripClicked);
            CreateSheetActionButton(gridGO.transform, "Lock Placement", "LOCK: MOVABLE", out btnSheetLockText, SurfaceDark, OnSheetToggleLockClicked);
            CreateSheetActionButton(gridGO.transform, "Grid Snap", "GRID SNAP: ON", out btnSheetSnapText, SurfaceDark, OnSheetToggleSnapClicked);
            CreateSheetActionButton(gridGO.transform, "Clear Wires", "CLEAR ALL WIRES", out _, DangerRed, OnSheetClearWiresClicked);
            CreateSheetActionButton(gridGO.transform, "Reset Scene", "RESET SCENE", out _, new Color32(80, 40, 40, 255), OnSheetResetSceneClicked);
        }

        private Button CreateSheetActionButton(Transform parent, string name, string initialText, out TextMeshProUGUI textComp, Color bgColor, UnityEngine.Events.UnityAction action)
        {
            GameObject btnGO = CreateGlassPanel($"BtnSheet_{name}", parent,
                anchorMin: Vector2.zero, anchorMax: Vector2.one,
                pivot: new Vector2(0.5f, 0.5f),
                pos: Vector2.zero, sizeDelta: Vector2.zero,
                bgColor: bgColor, cornerRadius: 14);

            Button btn = btnGO.AddComponent<Button>();
            btn.onClick.AddListener(action);

            textComp = CreateText("Label", btnGO.transform, initialText, 16f, FontStyles.Bold, TextLight, TextAlignmentOptions.Center);
            RectTransform tr = textComp.GetComponent<RectTransform>();
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = Vector2.zero; tr.offsetMax = Vector2.zero;

            return btn;
        }

        private void OpenBottomSheet()
        {
            if (isBottomSheetOpen) return;
            isBottomSheetOpen = true;
            bottomSheetBackdrop.SetActive(true);
            UpdateBottomSheetTexts();
            if (bottomSheetAnimationCo != null) StopCoroutine(bottomSheetAnimationCo);
            bottomSheetAnimationCo = StartCoroutine(AnimateRectY(bottomSheetRect, -430f, 0f, 0.25f));
        }

        private void CloseBottomSheet()
        {
            if (!isBottomSheetOpen) return;
            isBottomSheetOpen = false;
            if (bottomSheetAnimationCo != null) StopCoroutine(bottomSheetAnimationCo);
            bottomSheetAnimationCo = StartCoroutine(AnimateRectY(bottomSheetRect, 0f, -430f, 0.25f, () => {
                bottomSheetBackdrop.SetActive(false);
            }));
        }

        private void UpdateBottomSheetTexts()
        {
            if (btnSheetMainsText != null && simulator != null)
            {
                btnSheetMainsText.text = simulator.MainsSupplyEnabled ? "MAINS: ON (230V)" : "MAINS: OFF (0V)";
            }
            if (btnSheetSnapText != null)
            {
                btnSheetSnapText.text = ModelDragController.SnapEnabled ? "GRID SNAP: ON (18mm)" : "GRID SNAP: OFF";
            }
            if (btnSheetLockText != null)
            {
                ARModelManipulator target = SelectionManager.SelectedModel ?? ARModelManipulator.Selected ?? manipulator;
                if (target != null)
                {
                    bool isLocked = target.Mode == PlacementMode.Locked || target.Mode == PlacementMode.HangingAtCoordinates;
                    btnSheetLockText.text = isLocked ? "POSITION: LOCKED" : "POSITION: MOVABLE";
                }
            }
        }

        private void OnSheetToggleMainsClicked()
        {
            if (simulator != null)
            {
                simulator.ToggleMainsSupply();
                UpdateBottomSheetTexts();
                UpdateAllTelemetry();
            }
        }

        private void OnSheetTestTripClicked()
        {
            if (contactor != null) contactor.TriggerOverloadTrip("Manual Test Trip");
            CloseBottomSheet();
        }

        private void OnSheetToggleLockClicked()
        {
            ARModelManipulator target = SelectionManager.SelectedModel ?? ARModelManipulator.Selected ?? manipulator;
            if (target != null)
            {
                target.ToggleMovable();
                UpdateBottomSheetTexts();
            }
        }

        private void OnSheetToggleSnapClicked()
        {
            ModelDragController.SnapEnabled = !ModelDragController.SnapEnabled;
            UpdateBottomSheetTexts();
        }

        private void OnSheetClearWiresClicked()
        {
            CloseBottomSheet();
            ShowConfirmDialog("Are you sure you want to clear all wires in the scene?", () => {
                if (wireSpawner != null) wireSpawner.ClearAllWires();
            });
        }

        private void OnSheetResetSceneClicked()
        {
            CloseBottomSheet();
            ShowConfirmDialog("Reset entire scene to initial components?", () => {
                UnityEngine.SceneManagement.SceneManager.LoadScene(
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
            });
        }

        // 5. Contextual Action Bar (Anchored directly above the bottom toolbar, disappears when nothing is selected)
        private void BuildContextualBar(Transform parent)
        {
            GameObject barGO = CreateGlassPanel("ContextualBar", parent,
                anchorMin: new Vector2(0f, 0f), anchorMax: new Vector2(1f, 0f),
                pivot: new Vector2(0.5f, 0f),
                pos: new Vector2(0f, 102f),
                sizeDelta: new Vector2(-40f, 68f),
                bgColor: SurfaceDark, cornerRadius: 20);
            contextBarRect = barGO.GetComponent<RectTransform>();

            // Title label (left side)
            contextTitleText = CreateText("ContextTitle", barGO.transform, "MCB", 15f, FontStyles.Bold, AccentBlue, TextAlignmentOptions.MidlineLeft);
            RectTransform ctr = contextTitleText.GetComponent<RectTransform>();
            ctr.anchorMin = new Vector2(0f, 0f); ctr.anchorMax = new Vector2(0.24f, 1f);
            ctr.offsetMin = new Vector2(16f, 0f); ctr.offsetMax = Vector2.zero;

            // MCB Sub-container
            contextMcbContainer = new GameObject("McbContainer");
            contextMcbContainer.transform.SetParent(barGO.transform, false);
            RectTransform mcbRect = contextMcbContainer.AddComponent<RectTransform>();
            mcbRect.anchorMin = new Vector2(0.25f, 0f); mcbRect.anchorMax = new Vector2(0.72f, 1f);
            mcbRect.offsetMin = Vector2.zero; mcbRect.offsetMax = Vector2.zero;
            HorizontalLayoutGroup mcbHlg = contextMcbContainer.AddComponent<HorizontalLayoutGroup>();
            mcbHlg.childAlignment = TextAnchor.MiddleCenter;
            mcbHlg.spacing = 8f;
            mcbHlg.childControlWidth = true; mcbHlg.childControlHeight = true;

            contextBtnMcbToggle = CreateContextButton(contextMcbContainer.transform, "Toggle", "SWITCH ON", out contextBtnMcbToggleText, SuccessGreen, OnContextMcbToggleClicked);
            contextBtnMcbTrip = CreateContextButton(contextMcbContainer.transform, "Trip", "TEST TRIP", out _, WarningAmber, OnSheetTestTripClicked);

            // Contactor Sub-container (compact segmented coil control)
            contextContactorContainer = new GameObject("ContactorContainer");
            contextContactorContainer.transform.SetParent(barGO.transform, false);
            RectTransform ctRect = contextContactorContainer.AddComponent<RectTransform>();
            ctRect.anchorMin = new Vector2(0.25f, 0f); ctRect.anchorMax = new Vector2(0.72f, 1f);
            ctRect.offsetMin = Vector2.zero; ctRect.offsetMax = Vector2.zero;
            HorizontalLayoutGroup ctHlg = contextContactorContainer.AddComponent<HorizontalLayoutGroup>();
            ctHlg.childAlignment = TextAnchor.MiddleCenter;
            ctHlg.spacing = 6f;
            ctHlg.childControlWidth = true; ctHlg.childControlHeight = true;

            contextBtnCoilOn = CreateContextButton(contextContactorContainer.transform, "CoilOn", "ON", out _, SuccessGreen, OnCoilOnClicked);
            contextBtnCoilOff = CreateContextButton(contextContactorContainer.transform, "CoilOff", "OFF", out _, DangerRed, OnCoilOffClicked);
            contextBtnCoilToggle = CreateContextButton(contextContactorContainer.transform, "Toggle", "TOGGLE", out _, AccentBlue, OnCoilToggleClicked);
            contextBtnManualPress = CreateContextButton(contextContactorContainer.transform, "Press", "PRESS", out contextBtnManualPressText, WarningAmber, OnManualPressClicked);

            // Wire Sub-container
            contextWireContainer = new GameObject("WireContainer");
            contextWireContainer.transform.SetParent(barGO.transform, false);
            RectTransform wrRect = contextWireContainer.AddComponent<RectTransform>();
            wrRect.anchorMin = new Vector2(0.25f, 0f); wrRect.anchorMax = new Vector2(0.72f, 1f);
            wrRect.offsetMin = Vector2.zero; wrRect.offsetMax = Vector2.zero;
            HorizontalLayoutGroup wrHlg = contextWireContainer.AddComponent<HorizontalLayoutGroup>();
            wrHlg.childAlignment = TextAnchor.MiddleCenter;
            wrHlg.spacing = 8f;
            wrHlg.childControlWidth = true; wrHlg.childControlHeight = true;

            contextBtnWireDelete = CreateContextButton(contextWireContainer.transform, "DeleteWire", "DELETE WIRE", out _, DangerRed, OnContextDeleteWireClicked);

            // Universal Right-side buttons: [ Rotate 90° ] and [ Delete 🗑 ]
            GameObject rightActions = new GameObject("RightActions");
            rightActions.transform.SetParent(barGO.transform, false);
            RectTransform raRect = rightActions.AddComponent<RectTransform>();
            raRect.anchorMin = new Vector2(0.73f, 0f); raRect.anchorMax = new Vector2(1f, 1f);
            raRect.offsetMin = Vector2.zero; raRect.offsetMax = new Vector2(-12f, 0f);
            HorizontalLayoutGroup raHlg = rightActions.AddComponent<HorizontalLayoutGroup>();
            raHlg.childAlignment = TextAnchor.MiddleRight;
            raHlg.spacing = 8f;
            raHlg.childControlWidth = false; raHlg.childControlHeight = true;

            contextBtnRotate = CreateContextButton(rightActions.transform, "Rotate", "90° ↻", out _, SurfaceLight, OnContextRotateClicked, 85f);
            contextBtnDelete = CreateContextButton(rightActions.transform, "Delete", "🗑", out _, DangerRed, OnContextDeleteModelClicked, 64f);

            contextBarRect.gameObject.SetActive(false); // Hidden by default
        }

        private Button CreateContextButton(Transform parent, string name, string text, out TextMeshProUGUI textComp, Color bgColor, UnityEngine.Events.UnityAction action, float fixedWidth = 0f)
        {
            GameObject btnGO = CreateGlassPanel($"BtnCtx_{name}", parent,
                anchorMin: Vector2.zero, anchorMax: Vector2.one,
                pivot: new Vector2(0.5f, 0.5f),
                pos: Vector2.zero, sizeDelta: Vector2.zero,
                bgColor: bgColor, cornerRadius: 14);

            if (fixedWidth > 0f)
            {
                LayoutElement le = btnGO.AddComponent<LayoutElement>();
                le.preferredWidth = fixedWidth;
            }

            Button btn = btnGO.AddComponent<Button>();
            btn.onClick.AddListener(action);

            textComp = CreateText("Label", btnGO.transform, text, 14f, FontStyles.Bold, TextLight, TextAlignmentOptions.Center);
            RectTransform tr = textComp.GetComponent<RectTransform>();
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = Vector2.zero; tr.offsetMax = Vector2.zero;

            return btn;
        }

        private void UpdateContextBarVisibility()
        {
            if (contextBarRect == null) return;

            ARModelManipulator model = SelectionManager.SelectedModel ?? ARModelManipulator.Selected;
            CylindricalWire wire = SelectionManager.SelectedWire;

            if (wire != null)
            {
                contextBarRect.gameObject.SetActive(true);
                contextTitleText.text = $"{wire.PhaseName}";
                contextMcbContainer.SetActive(false);
                contextContactorContainer.SetActive(false);
                contextWireContainer.SetActive(true);
                contextBtnRotate.gameObject.SetActive(false);
                contextBtnDelete.gameObject.SetActive(false);
            }
            else if (model != null)
            {
                contextBarRect.gameObject.SetActive(true);
                string mName = model.gameObject.name;
                contextBtnRotate.gameObject.SetActive(true);
                contextBtnDelete.gameObject.SetActive(true);

                if (mName.ToLowerInvariant().Contains("contactor"))
                {
                    contextTitleText.text = "CONTACTOR";
                    contextMcbContainer.SetActive(false);
                    contextContactorContainer.SetActive(true);
                    contextWireContainer.SetActive(false);
                }
                else
                {
                    contextTitleText.text = mName.Contains("2P") ? "MCB 2P" : "4-POLE MCB";
                    contextMcbContainer.SetActive(true);
                    contextContactorContainer.SetActive(false);
                    contextWireContainer.SetActive(false);
                }
            }
            else
            {
                contextBarRect.gameObject.SetActive(false);
            }
        }

        private void UpdateContextBarDynamicState()
        {
            if (contextBarRect == null || !contextBarRect.gameObject.activeSelf) return;

            if (contextBtnMcbToggleText != null && contactor != null && contactor.Lever != null)
            {
                bool isOn = contactor.Lever.CurrentState == BreakerState.On;
                contextBtnMcbToggleText.text = isOn ? "SWITCH OFF" : "SWITCH ON";
                Image bg = contextBtnMcbToggle.GetComponent<Image>();
                if (bg != null) bg.color = isOn ? DangerRed : SuccessGreen;
            }
        }

        private void OnContextMcbToggleClicked()
        {
            if (contactor == null) contactor = FindAnyObjectByType<ContactorController>();
            if (contactor != null)
            {
                if (contactor.Lever != null && contactor.Lever.CurrentState == BreakerState.On)
                    contactor.SwitchOff();
                else
                    contactor.SwitchOn();
                UpdateAllTelemetry();
            }
        }

        private void OnContextRotateClicked()
        {
            ARModelManipulator target = SelectionManager.SelectedModel ?? ARModelManipulator.Selected ?? manipulator;
            if (target != null)
            {
                target.RotateY(90f);
            }
        }

        private void OnContextDeleteModelClicked()
        {
            ARModelManipulator target = SelectionManager.SelectedModel ?? ARModelManipulator.Selected ?? manipulator;
            if (target == null) return;

            string objName = target.gameObject.name;
            if (componentSpawner == null) componentSpawner = FindAnyObjectByType<ComponentSpawner>();

            if (componentSpawner != null)
            {
                componentSpawner.DeleteModel(target);
            }
            else
            {
                UndoService.RecordAction(new DeleteObjectUndoAction(target.gameObject));
                target.gameObject.SetActive(false);
                Destroy(target.gameObject, 6.0f);
            }

            SelectionManager.ClearSelection();
            UpdateContextBarVisibility();
        }

        private void OnContextDeleteWireClicked()
        {
            CylindricalWire wire = SelectionManager.SelectedWire;
            if (wire == null) return;

            UndoService.RecordAction(new DeleteObjectUndoAction(wire.gameObject));
            wire.DeleteWire();
            SelectionManager.ClearSelection();
            UpdateContextBarVisibility();
        }

        private void OnCoilOnClicked()     { GetArmature()?.Energize(); UpdateAllTelemetry(); }
        private void OnCoilOffClicked()    { GetArmature()?.Release(); UpdateAllTelemetry(); }
        private void OnCoilToggleClicked() { GetArmature()?.Toggle(); UpdateAllTelemetry(); }
        private void OnManualPressClicked()
        {
            manualPressHeld = !manualPressHeld;
            GetArmature()?.SetManualOverride(manualPressHeld);
            if (contextBtnManualPressText != null)
                contextBtnManualPressText.text = manualPressHeld ? "RELEASE" : "PRESS";
            UpdateAllTelemetry();
        }

        private ContactorArmatureController GetArmature()
        {
            if (armatureController == null) armatureController = FindAnyObjectByType<ContactorArmatureController>();
            return armatureController;
        }

        // 6. Confirmation Modal
        private void BuildConfirmModal(Transform parent)
        {
            confirmModal = new GameObject("ConfirmModal");
            confirmModal.transform.SetParent(parent, false);
            RectTransform cmRect = confirmModal.AddComponent<RectTransform>();
            cmRect.anchorMin = Vector2.zero; cmRect.anchorMax = Vector2.one;
            cmRect.offsetMin = Vector2.zero; cmRect.offsetMax = Vector2.zero;
            Image bg = confirmModal.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.75f);

            GameObject boxGO = CreateGlassPanel("Box", confirmModal.transform,
                anchorMin: new Vector2(0.5f, 0.5f), anchorMax: new Vector2(0.5f, 0.5f),
                pivot: new Vector2(0.5f, 0.5f),
                pos: Vector2.zero, sizeDelta: new Vector2(620f, 240f),
                bgColor: SurfaceDark, cornerRadius: 20);

            confirmModalText = CreateText("Question", boxGO.transform, "Are you sure?", 18f, FontStyles.Bold, TextLight, TextAlignmentOptions.Center);
            RectTransform qRect = confirmModalText.GetComponent<RectTransform>();
            qRect.anchorMin = new Vector2(0f, 0.45f); qRect.anchorMax = new Vector2(1f, 1f);
            qRect.offsetMin = new Vector2(24f, 0f); qRect.offsetMax = new Vector2(-24f, -16f);

            // Cancel button
            Button cancelBtn = CreateContextButton(boxGO.transform, "Cancel", "CANCEL", out _, SurfaceLight, () => {
                confirmModal.SetActive(false);
            }, 180f);
            RectTransform cr = cancelBtn.GetComponent<RectTransform>();
            cr.anchorMin = new Vector2(0.1f, 0.12f); cr.anchorMax = new Vector2(0.46f, 0.42f);
            cr.offsetMin = Vector2.zero; cr.offsetMax = Vector2.zero;

            // Confirm button
            Button okBtn = CreateContextButton(boxGO.transform, "Confirm", "CONFIRM", out _, DangerRed, () => {
                confirmModal.SetActive(false);
                onConfirmAction?.Invoke();
            }, 180f);
            RectTransform okr = okBtn.GetComponent<RectTransform>();
            okr.anchorMin = new Vector2(0.54f, 0.12f); okr.anchorMax = new Vector2(0.9f, 0.42f);
            okr.offsetMin = Vector2.zero; okr.offsetMax = Vector2.zero;

            confirmModal.SetActive(false);
        }

        private void ShowConfirmDialog(string message, Action onConfirm)
        {
            if (confirmModalText != null) confirmModalText.text = message;
            onConfirmAction = onConfirm;
            if (confirmModal != null) confirmModal.SetActive(true);
        }

        // 7. Undo Snackbar (5s notification with [UNDO] button)
        private void BuildUndoSnackbar(Transform parent)
        {
            GameObject sbGO = CreateGlassPanel("UndoSnackbar", parent,
                anchorMin: new Vector2(0.5f, 0f), anchorMax: new Vector2(0.5f, 0f),
                pivot: new Vector2(0.5f, 0f),
                pos: new Vector2(0f, 185f),
                sizeDelta: new Vector2(680f, 54f),
                bgColor: SurfaceDark, cornerRadius: 18);
            snackbarRect = sbGO.GetComponent<RectTransform>();

            snackbarText = CreateText("Msg", sbGO.transform, "Item deleted", 15f, FontStyles.Normal, TextLight, TextAlignmentOptions.MidlineLeft);
            RectTransform stRect = snackbarText.GetComponent<RectTransform>();
            stRect.anchorMin = new Vector2(0f, 0f); stRect.anchorMax = new Vector2(0.7f, 1f);
            stRect.offsetMin = new Vector2(24f, 0f); stRect.offsetMax = Vector2.zero;

            snackbarUndoBtn = CreateContextButton(sbGO.transform, "UndoAction", "UNDO", out _, WarningAmber, OnSnackbarUndoClicked, 120f);
            RectTransform urRect = snackbarUndoBtn.GetComponent<RectTransform>();
            urRect.anchorMin = new Vector2(0.72f, 0.14f); urRect.anchorMax = new Vector2(0.96f, 0.86f);
            urRect.offsetMin = Vector2.zero; urRect.offsetMax = Vector2.zero;

            snackbarRect.gameObject.SetActive(false);
        }

        private void ShowSnackbar(string message)
        {
            if (snackbarRect == null) return;
            if (snackbarText != null) snackbarText.text = message;
            snackbarRect.gameObject.SetActive(true);

            if (snackbarDismissCo != null) StopCoroutine(snackbarDismissCo);
            snackbarDismissCo = StartCoroutine(DismissSnackbarAfterSeconds(5.0f));
        }

        private IEnumerator DismissSnackbarAfterSeconds(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            if (snackbarRect != null) snackbarRect.gameObject.SetActive(false);
        }

        private void OnSnackbarUndoClicked()
        {
            if (UndoService.PerformUndo())
            {
                if (snackbarRect != null) snackbarRect.gameObject.SetActive(false);
                UpdateContextBarVisibility();
            }
        }

        private void HandleUndoActionRecorded(string description)
        {
            ShowSnackbar($"{description}");
        }
        #endregion

        #region Telemetry & Event Handlers
        private void BindEventListeners()
        {
            // Empty hook for future callbacks
        }

        private void HandleModelSelected(ARModelManipulator m) => UpdateContextBarVisibility();
        private void HandleModelDeselected(ARModelManipulator m) => UpdateContextBarVisibility();
        private void HandleWireSelected(CylindricalWire w) => UpdateContextBarVisibility();
        private void HandleWireDeselected(CylindricalWire w) => UpdateContextBarVisibility();
        private void HandleSelectionCleared() => UpdateContextBarVisibility();

        private void UpdateAllTelemetry()
        {
            // 1. Lever & Breaker status chip
            if (contactor != null && contactor.Lever != null)
            {
                BreakerState state = contactor.Lever.CurrentState;
                float angle = contactor.Lever.CurrentAngle;

                if (breakerChipText != null)
                {
                    switch (state)
                    {
                        case BreakerState.On:
                            breakerChipText.text = $"● ON ({angle:F0}°)";
                            if (breakerChipBg != null) breakerChipBg.color = SuccessGreen;
                            break;
                        case BreakerState.Tripped:
                            breakerChipText.text = "● TRIP";
                            if (breakerChipBg != null) breakerChipBg.color = WarningAmber;
                            break;
                        default:
                            breakerChipText.text = $"● OFF ({angle:F0}°)";
                            if (breakerChipBg != null) breakerChipBg.color = DangerRed;
                            break;
                    }
                }
            }

            // 2. Power Telemetry
            if (simulator != null && powerTelemetryText != null)
            {
                powerTelemetryText.text = simulator.MainsSupplyEnabled ? "230V / 400V" : "0V (OFFLINE)";
                powerTelemetryText.color = simulator.IsShortCircuitFault ? DangerRed :
                                          (simulator.MainsSupplyEnabled ? TextLight : TextMuted);
            }

            // 3. Load Telemetry
            if (simulator != null && loadTelemetryText != null)
            {
                string fault = simulator.IsShortCircuitFault ? " SHORT!" : "";
                loadTelemetryText.text = $"{simulator.TotalLoadCurrent:F1}A{fault}";
                loadTelemetryText.color = simulator.IsShortCircuitFault ? DangerRed : WarningAmber;
            }

            // 4. Details drawer content
            if (detailsDrawer != null && detailsDrawer.activeSelf && detailsText != null)
            {
                float angle = (contactor != null && contactor.Lever != null) ? contactor.Lever.CurrentAngle : 0f;
                string state = (contactor != null && contactor.Lever != null) ? contactor.Lever.CurrentState.ToString().ToUpper() : "OFF";
                ContactorArmatureController arm = GetArmature();
                string armStr = (arm != null && arm.IsEnergized) ? "ENERGIZED" : "RELEASED";
                float armY = (arm != null) ? arm.CurrentY : 0f;

                detailsText.text = $"LEVER: {angle:F1}° [{state}]\n" +
                                   $"POWER: {(simulator != null && simulator.MainsSupplyEnabled ? "230V / 400V (ACTIVE)" : "0V (OFFLINE)")}\n" +
                                   $"CONTACTOR: {armStr} (y={armY:F4}m)";
            }
        }
        #endregion

        #region Helpers & Animation
        private static void EnsureProceduralSprites()
        {
            if (roundedSprite == null) roundedSprite = CreateRoundedRectSprite(64, 18);
            if (circleSprite == null)  circleSprite  = CreateRoundedRectSprite(64, 32);
        }

        private static Sprite CreateRoundedRectSprite(int size, int radius)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            Color32 transparent = new Color32(0, 0, 0, 0);
            Color32 white = new Color32(255, 255, 255, 255);
            Color32[] colors = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int cx = x < radius ? radius : (x >= size - radius ? size - radius - 1 : x);
                    int cy = y < radius ? radius : (y >= size - radius ? size - radius - 1 : y);
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy));
                    if (dist > radius)
                    {
                        float alpha = Mathf.Clamp01(radius + 1.2f - dist);
                        colors[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255));
                    }
                    else
                    {
                        colors[y * size + x] = white;
                    }
                }
            }
            tex.SetPixels32(colors);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        }

        private GameObject CreateGlassPanel(string name, Transform parent,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 pos, Vector2 sizeDelta, Color bgColor, int cornerRadius = 16)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform r = go.AddComponent<RectTransform>();
            r.anchorMin = anchorMin;
            r.anchorMax = anchorMax;
            r.pivot = pivot;
            r.anchoredPosition = pos;
            r.sizeDelta = sizeDelta;

            Image img = go.AddComponent<Image>();
            img.sprite = roundedSprite;
            img.type = Image.Type.Sliced;
            img.color = bgColor;

            return go;
        }

        private TextMeshProUGUI CreateText(string name, Transform parent, string text, float size,
            FontStyles style, Color color, TextAlignmentOptions align)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.fontStyle = style;
            tmp.color = color;
            tmp.alignment = align;
            return tmp;
        }

        private IEnumerator AnimateRectX(RectTransform target, float fromX, float toX, float duration, Action onComplete = null)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float easeT = 1f - Mathf.Pow(1f - t, 3f); // Ease-out cubic
                target.anchoredPosition = new Vector2(Mathf.Lerp(fromX, toX, easeT), target.anchoredPosition.y);
                yield return null;
            }
            target.anchoredPosition = new Vector2(toX, target.anchoredPosition.y);
            onComplete?.Invoke();
        }

        private IEnumerator AnimateRectY(RectTransform target, float fromY, float toY, float duration, Action onComplete = null)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float easeT = 1f - Mathf.Pow(1f - t, 3f); // Ease-out cubic
                target.anchoredPosition = new Vector2(target.anchoredPosition.x, Mathf.Lerp(fromY, toY, easeT));
                yield return null;
            }
            target.anchoredPosition = new Vector2(target.anchoredPosition.x, toY);
            onComplete?.Invoke();
        }
        #endregion
    }
}
