using UnityEngine;

namespace WireSystem
{
    /// <summary>
    /// Compatibility bridge forwarding IsDragging to WireDragController.
    /// Used by ARPlacementManager and ARModelManipulator to prevent touch interaction conflicts during wire dragging.
    /// </summary>
    public class WireInputController : MonoBehaviour
    {
        public static bool IsDragging => WireDragController.IsDragging;
    }
}
