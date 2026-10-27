using System.Collections.Generic;
using UnityEngine;

public enum ExitDirection
{
    Up,
    Down,
    Left,
    Right,
    Forward,
    Back
}

/// <summary>
/// Attached to empty GameObjects representing contactor / breaker electrical terminals.
/// Manages terminal identity, connection capacity, world exit direction, and global registry.
/// </summary>
public class TerminalPort : MonoBehaviour
{
    [Header("Terminal Identity")]
    [Tooltip("Unique identifier for this terminal (e.g. IN_1, OUT_2).")]
    public string terminalId;

    [Header("Connection Limits")]
    [Tooltip("Maximum number of wires that can attach to this terminal.")]
    public int maxWires = 1;

    [Tooltip("Current count of connected wires.")]
    public int wireCount = 0;

    [Header("Routing Exit Normal")]
    [Tooltip("Local-space exit direction normal along which wires leave this terminal.")]
    [SerializeField] private ExitDirection exitDirection = ExitDirection.Up;

    /// <summary>
    /// Global registry of all currently active TerminalPorts in the scene.
    /// </summary>
    public static readonly List<TerminalPort> All = new List<TerminalPort>();

    public ExitDirection LocalExitDirection
    {
        get => exitDirection;
        set => exitDirection = value;
    }

    /// <summary>
    /// World position of the terminal contact point.
    /// </summary>
    public Vector3 Position => transform.position;

    /// <summary>
    /// Outward exit vector in world space, based on local exitDirection.
    /// </summary>
    public Vector3 ExitDir => transform.TransformDirection(GetLocalExitVector(exitDirection)).normalized;

    /// <summary>
    /// True if this terminal can accept another wire.
    /// </summary>
    public bool CanAccept => wireCount < maxWires;

    private void OnEnable()
    {
        if (!All.Contains(this))
        {
            All.Add(this);
        }
    }

    private void OnDisable()
    {
        All.Remove(this);
    }

    private void Reset()
    {
        if (string.IsNullOrEmpty(terminalId))
        {
            terminalId = gameObject.name;
        }
    }

    /// <summary>
    /// Converts the ExitDirection enum to a local unit vector.
    /// </summary>
    public static Vector3 GetLocalExitVector(ExitDirection dir)
    {
        switch (dir)
        {
            case ExitDirection.Up: return Vector3.up;
            case ExitDirection.Down: return Vector3.down;
            case ExitDirection.Left: return Vector3.left;
            case ExitDirection.Right: return Vector3.right;
            case ExitDirection.Forward: return Vector3.forward;
            case ExitDirection.Back: return Vector3.back;
            default: return Vector3.up;
        }
    }

    private void OnDrawGizmos()
    {
        Vector3 pos = Position;
        Vector3 dir = ExitDir;

        // Terminal contact point sphere
        Gizmos.color = CanAccept ? new Color(0.1f, 0.8f, 1f, 0.8f) : new Color(1f, 0.3f, 0.2f, 0.8f);
        Gizmos.DrawSphere(pos, 0.005f);

        // Exit direction vector line
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(pos, pos + dir * 0.03f);
        Gizmos.DrawSphere(pos + dir * 0.03f, 0.0015f);
    }
}
