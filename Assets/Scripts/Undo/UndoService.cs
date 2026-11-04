using System;
using System.Collections.Generic;
using UnityEngine;

namespace ContactorAR.UndoSystem
{
    public interface IUndoAction
    {
        string Description { get; }
        void Undo();
    }

    /// <summary>
    /// Lightweight Undo service maintaining an action stack for deletions and additions.
    /// Supports a 5-second undo notification window.
    /// </summary>
    public class UndoService : MonoBehaviour
    {
        public static UndoService Instance { get; private set; }

        private readonly Stack<IUndoAction> undoStack = new Stack<IUndoAction>();
        public static event Action<string> OnActionRecorded;
        public static event Action OnUndoExecuted;

        public static bool CanUndo => Instance != null && Instance.undoStack.Count > 0;

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
        /// Records an undoable action to the stack and emits notification.
        /// </summary>
        public static void RecordAction(IUndoAction action)
        {
            if (action == null) return;
            if (Instance == null)
            {
                GameObject go = new GameObject("UndoService");
                Instance = go.AddComponent<UndoService>();
            }

            Instance.undoStack.Push(action);
            OnActionRecorded?.Invoke(action.Description);
            Debug.Log($"[UndoService] Recorded: {action.Description} (stack size: {Instance.undoStack.Count})");
        }

        /// <summary>
        /// Performs the most recent undo action.
        /// </summary>
        public static bool PerformUndo()
        {
            if (Instance == null || Instance.undoStack.Count == 0) return false;

            IUndoAction action = Instance.undoStack.Pop();
            try
            {
                action.Undo();
                OnUndoExecuted?.Invoke();
                Debug.Log($"[UndoService] Undone: {action.Description}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UndoService] Error executing undo for {action.Description}: {ex.Message}");
                return false;
            }
        }

        public static void Clear()
        {
            if (Instance != null) Instance.undoStack.Clear();
        }
    }

    /// <summary>
    /// Undo action for a deleted GameObject (reactivates or restores it).
    /// </summary>
    public class DeleteObjectUndoAction : IUndoAction
    {
        private readonly GameObject targetObject;
        private readonly string objectName;

        public string Description => $"Deleted {objectName}";

        public DeleteObjectUndoAction(GameObject obj)
        {
            targetObject = obj;
            objectName = obj != null ? obj.name : "Object";
        }

        public void Undo()
        {
            if (targetObject != null)
            {
                targetObject.SetActive(true);
            }
        }
    }

    /// <summary>
    /// Undo action for an added GameObject (removes/deactivates it).
    /// </summary>
    public class AddObjectUndoAction : IUndoAction
    {
        private readonly GameObject targetObject;
        private readonly string objectName;

        public string Description => $"Added {objectName}";

        public AddObjectUndoAction(GameObject obj)
        {
            targetObject = obj;
            objectName = obj != null ? obj.name : "Object";
        }

        public void Undo()
        {
            if (targetObject != null)
            {
                targetObject.SetActive(false);
            }
        }
    }
}
