using System;
using System.Collections.Generic;
using UnityEngine;
using ContactorAR.AR;
using ContactorAR.Wiring;

namespace ContactorAR.SelectionSystem
{
    /// <summary>
    /// Centralized selection manager for models and wires.
    /// Handles single-selection, deselection, selection events, and raycast hit detection.
    /// </summary>
    public class SelectionManager : MonoBehaviour
    {
        public static SelectionManager Instance { get; private set; }

        public static event Action<ARModelManipulator> OnModelSelected;
        public static event Action<ARModelManipulator> OnModelDeselected;
        public static event Action<CylindricalWire> OnWireSelected;
        public static event Action<CylindricalWire> OnWireDeselected;
        public static event Action OnSelectionCleared;

        [Header("Status")]
        [SerializeField] private ARModelManipulator currentModel;
        [SerializeField] private CylindricalWire currentWire;

        public static ARModelManipulator SelectedModel => Instance != null ? Instance.currentModel : null;
        public static CylindricalWire SelectedWire => Instance != null ? Instance.currentWire : null;
        public static bool HasSelection => Instance != null && (Instance.currentModel != null || Instance.currentWire != null);

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else if (Instance != this)
            {
                Destroy(this);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Selects a 3D model, deselecting any previous model or wire.
        /// </summary>
        public static void Select(ARModelManipulator model)
        {
            if (Instance == null) return;
            if (Instance.currentModel == model && Instance.currentWire == null) return;

            DeselectWire();

            if (Instance.currentModel != null && Instance.currentModel != model)
            {
                ARModelManipulator prev = Instance.currentModel;
                Instance.currentModel = null;
                OnModelDeselected?.Invoke(prev);
            }

            Instance.currentModel = model;
            if (model != null)
            {
                ARModelManipulator.Select(model);
                OnModelSelected?.Invoke(model);
            }
            else
            {
                OnSelectionCleared?.Invoke();
            }
        }

        /// <summary>
        /// Selects a wire, deselecting any previous model or wire.
        /// </summary>
        public static void Select(CylindricalWire wire)
        {
            if (Instance == null) return;
            if (Instance.currentWire == wire && Instance.currentModel == null) return;

            DeselectModel();

            if (Instance.currentWire != null && Instance.currentWire != wire)
            {
                CylindricalWire prev = Instance.currentWire;
                Instance.currentWire = null;
                OnWireDeselected?.Invoke(prev);
            }

            Instance.currentWire = wire;
            if (wire != null)
            {
                OnWireSelected?.Invoke(wire);
            }
            else
            {
                OnSelectionCleared?.Invoke();
            }
        }

        public static void DeselectModel()
        {
            if (Instance != null && Instance.currentModel != null)
            {
                ARModelManipulator prev = Instance.currentModel;
                Instance.currentModel = null;
                OnModelDeselected?.Invoke(prev);
            }
        }

        public static void DeselectWire()
        {
            if (Instance != null && Instance.currentWire != null)
            {
                CylindricalWire prev = Instance.currentWire;
                Instance.currentWire = null;
                OnWireDeselected?.Invoke(prev);
            }
        }

        public static void ClearSelection()
        {
            DeselectModel();
            DeselectWire();
            OnSelectionCleared?.Invoke();
        }
    }
}
