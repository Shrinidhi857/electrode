using UnityEngine;

namespace WireSystem
{
    public enum TerminalType
    {
        Input,
        Output
    }

    /// <summary>
    /// Backward-compatibility terminal class inheriting from TerminalPort.
    /// </summary>
    public class Terminal : TerminalPort
    {
        [SerializeField] private TerminalType terminalType = TerminalType.Input;
        public TerminalType Type => terminalType;
    }
}
