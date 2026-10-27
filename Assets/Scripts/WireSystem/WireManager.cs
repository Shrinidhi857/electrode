using System;
using System.Collections.Generic;
using UnityEngine;

namespace WireSystem
{
    /// <summary>
    /// Central manager and compatibility bridge for the wire system.
    /// Hosts the WireDragController, manages active wires, and relays events to PowerCircuitSimulator.
    /// </summary>
    [RequireComponent(typeof(WireDragController))]
    public class WireManager : MonoBehaviour
    {
        public static WireManager Instance { get; private set; }

        public static event Action<TerminalPort, TerminalPort, Wire> OnWireConnected;
        public static event Action<TerminalPort, TerminalPort, Wire> OnWireDisconnected;

        public IReadOnlyList<Wire> ActiveWires => Wire.AllWires;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (GetComponent<WireDragController>() == null)
            {
                gameObject.AddComponent<WireDragController>();
            }
        }

        private void OnEnable()
        {
            Wire.OnWireConnected += HandleWireConnected;
            Wire.OnWireDisconnected += HandleWireDisconnected;
        }

        private void OnDisable()
        {
            Wire.OnWireConnected -= HandleWireConnected;
            Wire.OnWireDisconnected -= HandleWireDisconnected;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void HandleWireConnected(TerminalPort a, TerminalPort b, Wire w)
        {
            OnWireConnected?.Invoke(a, b, w);
        }

        private void HandleWireDisconnected(TerminalPort a, TerminalPort b, Wire w)
        {
            OnWireDisconnected?.Invoke(a, b, w);
        }

        public void ClearAll()
        {
            for (int i = Wire.AllWires.Count - 1; i >= 0; i--)
            {
                if (Wire.AllWires[i] != null)
                {
                    Destroy(Wire.AllWires[i].gameObject);
                }
            }
        }
    }
}
