using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using ContactorAR.Breaker;
using ContactorAR.Simulation;
using ContactorAR.AR;

namespace ContactorAR.AR
{
    /// <summary>
    /// Manages the AR Foundation plane detection, contactor model placement,
    /// anchor pinning, AR session lifecycle, and AR -> world coordinate mapping.
    /// 
    /// Features:
    /// - AR plane detection (horizontal / wall detection for hanging)
    /// - Tap-to-place on detected surface
    /// - Anchored hanging at specific world coordinates with ARAnchor
    /// - AR camera calibration info
    /// - Graceful fallback when AR is unavailable (Unity Editor)
    /// </summary>
    [RequireComponent(typeof(ARRaycastManager))]
    public class ARPlacementManager : MonoBehaviour
    {
        [Header("AR Components")]
        [SerializeField] private ARRaycastManager arRaycastManager;
        [SerializeField] private ARPlaneManager arPlaneManager;
        [SerializeField] private ARAnchorManager arAnchorManager;

        [Header("Contactor Model")]
        [SerializeField] private GameObject contactorPrefab;
        [SerializeField] private ARModelManipulator spawnedManipulator;

        [Header("Placement Settings")]
        [Tooltip("Show a reticle ghost where the model will be placed.")]
        [SerializeField] private GameObject placementReticle;

        [Tooltip("Enable tap-to-place on first tap when model not yet placed.")]
        [SerializeField] private bool tapToPlace = true;

        [Tooltip("Preferred hanging height offset from AR plane surface.")]
        [SerializeField] private float hangingHeightAbovePlane = 0.65f;

        [Tooltip("Force hanging mode immediately on placement.")]
        [SerializeField] private bool autoHangOnPlacement = false;

        [Header("Status")]
        [SerializeField] private bool modelPlaced = false;
        [SerializeField] private bool arActive = false;

        private readonly List<ARRaycastHit> hitResults = new List<ARRaycastHit>();
        private ARAnchor currentAnchor;
        private Pose lastValidReticlePose;
        private bool reticleVisible = false;

        public bool IsModelPlaced => modelPlaced;
        public bool IsARActive => arActive;
        public ARModelManipulator ActiveManipulator => spawnedManipulator;

        public event Action<Vector3, Quaternion> OnModelPlaced;
        public event Action OnARSessionLost;
        public event Action OnARSessionReady;

        private void Awake()
        {
            if (arRaycastManager == null)
                arRaycastManager = GetComponent<ARRaycastManager>();

            if (contactorPrefab == null)
                contactorPrefab = ModelAssetLoader.LoadMCBModel();
        }

        private void Start()
        {
            // Hide reticle until valid surface found
            if (placementReticle != null)
                placementReticle.SetActive(false);

            // Suppress plane visualizations now and whenever new planes are detected
            SuppressPlaneVisuals();
            if (arPlaneManager != null)
                arPlaneManager.planesChanged += OnPlanesChanged;

#if UNITY_EDITOR
            arActive = false;
            if (!modelPlaced && contactorPrefab != null && tapToPlace)
            {
                PlaceModelAt(Vector3.forward * 0.50f, Quaternion.Euler(0, 180, 0));
            }
#else
            arActive = true;
#endif
        }

        private void OnDestroy()
        {
            if (arPlaneManager != null)
                arPlaneManager.planesChanged -= OnPlanesChanged;
        }

        private void OnPlanesChanged(ARPlanesChangedEventArgs args)
        {
            // Disable renderers on any newly added plane
            foreach (var plane in args.added)
            {
                DisablePlaneRenderers(plane.gameObject);
            }
        }

        private static void DisablePlaneRenderers(GameObject go)
        {
            if (go == null) return;
            foreach (MeshRenderer mr in go.GetComponentsInChildren<MeshRenderer>(true))
                mr.enabled = false;
            foreach (LineRenderer lr in go.GetComponentsInChildren<LineRenderer>(true))
                lr.enabled = false;
        }

        private void SuppressPlaneVisuals()
        {
            if (arPlaneManager == null) return;
            foreach (var plane in arPlaneManager.trackables)
            {
                DisablePlaneRenderers(plane.gameObject);
            }
        }

        private void Update()
        {
#if !UNITY_EDITOR
            UpdateARReticle();
            HandleARTouchInput();
#endif
        }

        #region AR Surface Scanning & Reticle
        private void UpdateARReticle()
        {
            if (modelPlaced) { HideReticle(); return; }

            if (arRaycastManager == null) return;

            Vector2 screenCenter = new Vector2(Screen.width / 2f, Screen.height / 2f);

            if (arRaycastManager.Raycast(screenCenter, hitResults, TrackableType.PlaneWithinPolygon | TrackableType.PlaneWithinBounds))
            {
                Pose hitPose = hitResults[0].pose;
                lastValidReticlePose = hitPose;
                ShowReticle(hitPose.position, hitPose.rotation);
            }
            else
            {
                HideReticle();
            }
        }

        private void ShowReticle(Vector3 position, Quaternion rotation)
        {
            if (placementReticle == null) return;
            reticleVisible = true;
            placementReticle.SetActive(true);
            placementReticle.transform.SetPositionAndRotation(position, rotation);
        }

        private void HideReticle()
        {
            if (!reticleVisible || placementReticle == null) return;
            reticleVisible = false;
            placementReticle.SetActive(false);
        }
        #endregion

        #region AR Touch Placement
        private void HandleARTouchInput()
        {
            if (WireSystem.WireInputController.IsDragging) return;
            if (Input.touchCount == 0) return;

            Touch touch = Input.GetTouch(0);
            if (touch.phase != TouchPhase.Began) return;

            // Don't intercept touches if already placed (let ARModelManipulator handle them)
            if (modelPlaced) return;

            if (!tapToPlace) return;

            if (arRaycastManager.Raycast(touch.position, hitResults, TrackableType.PlaneWithinPolygon | TrackableType.PlaneWithinBounds))
            {
                Pose hitPose = hitResults[0].pose;
                Vector3 placementPos = hitPose.position + Vector3.up * hangingHeightAbovePlane;
                PlaceModelAt(placementPos, Quaternion.Euler(0, Camera.main != null ? Camera.main.transform.eulerAngles.y + 180f : 0, 0));
            }
        }
        #endregion

        #region Model Placement & Anchoring
        public void PlaceModelAt(Vector3 worldPosition, Quaternion worldRotation)
        {
            if (contactorPrefab == null)
                contactorPrefab = ModelAssetLoader.LoadMCBModel();

            if (contactorPrefab == null)
            {
                Debug.LogWarning("[ARPlacementManager] No contactorPrefab assigned and could not load from Assets. Cannot place model.");
                return;
            }

            // Remove previous instance
            if (spawnedManipulator != null)
            {
                Destroy(spawnedManipulator.gameObject);
            }

            // Spawn contactor model
            GameObject instance = Instantiate(contactorPrefab, worldPosition, worldRotation);
            instance.name = ModelAssetLoader.MCB_MODEL_NAME;
            ModelAssetLoader.ConfigureMCBInstance(instance);
            spawnedManipulator = instance.GetComponent<ARModelManipulator>();
            if (spawnedManipulator == null)
                spawnedManipulator = instance.AddComponent<ARModelManipulator>();

            modelPlaced = true;
            HideReticle();

            // Optional: Auto-hang immediately
            if (autoHangOnPlacement && spawnedManipulator != null)
            {
                spawnedManipulator.HangAtCoordinate(worldPosition, worldRotation);
            }

            OnModelPlaced?.Invoke(worldPosition, worldRotation);
            Debug.Log($"[ARPlacementManager] Contactor placed at: {worldPosition}");

            // Create AR Anchor at placement surface if AR Anchor Manager available
            if (arAnchorManager != null && arPlaneManager != null && hitResults.Count > 0)
            {
                ARRaycastHit firstHit = hitResults[0];
                // Use ARPlaneManager to look up the plane from the trackable ID
                ARPlane plane = arPlaneManager.GetPlane(firstHit.trackableId);
                if (plane != null)
                {
                    // Modern AR Foundation anchor creation: add ARAnchor component at pose
                    GameObject anchorObj = new GameObject("ContactorAnchor");
                    anchorObj.transform.SetPositionAndRotation(firstHit.pose.position, firstHit.pose.rotation);
                    ARAnchor anchor = anchorObj.AddComponent<ARAnchor>();
                    if (anchor != null)
                    {
                        currentAnchor = anchor;
                        instance.transform.SetParent(anchor.transform, true);
                    }
                }
            }
        }

        /// <summary>
        /// Re-place contactor at specific AR-calibrated world coordinate (e.g., wall socket position).
        /// </summary>
        public void PinToWorldCoordinate(Vector3 worldCoord, Vector3 eulerAngles)
        {
            if (spawnedManipulator != null)
            {
                spawnedManipulator.HangAtCoordinate(worldCoord, Quaternion.Euler(eulerAngles));
            }
        }

        public void ResetPlacement()
        {
            if (spawnedManipulator != null)
            {
                Destroy(spawnedManipulator.gameObject);
                spawnedManipulator = null;
            }
            modelPlaced = false;
            Debug.Log("[ARPlacementManager] Placement reset. Tap to place again.");
        }
        #endregion
    }
}
