using System;
using System.Collections.Generic;
using UnityEngine;

namespace WireSystem
{
    [Serializable]
    public class WireConnectionData
    {
        public string startTerminalId;
        public string endTerminalId;
        public int colorIndex;
    }

    [Serializable]
    public class WireSaveContainer
    {
        public List<WireConnectionData> connections = new List<WireConnectionData>();
    }

    /// <summary>
    /// Central manager and singleton registry for the interactive wire system.
    /// Handles wire lifecycle, palette distribution, static connection events,
    /// and JSON persistence.
    /// </summary>
    public class WireManager : MonoBehaviour
    {
        public static WireManager Instance { get; private set; }

        public static event Action<Terminal, Terminal, Wire> OnWireConnected;
        public static event Action<Terminal, Terminal, Wire> OnWireDisconnected;

        [Header("Configuration")]
        [SerializeField] private WireSettings settings;
        [SerializeField] private GameObject wirePrefab;
        [SerializeField] private Transform referenceModelRoot;

        private readonly List<Wire> activeWires = new List<Wire>();
        private readonly Dictionary<string, Terminal> terminalRegistry = new Dictionary<string, Terminal>();
        private int colorPaletteIndex = 0;

        public WireSettings Settings => settings;
        public IReadOnlyList<Wire> ActiveWires => activeWires;
        public IReadOnlyCollection<Terminal> CachedTerminals => terminalRegistry.Values;
        public Transform ReferenceModelRoot => referenceModelRoot;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            EnsureSettings();
            ResolveReferenceModel();
            RefreshTerminalRegistry();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void SetSettings(WireSettings newSettings)
        {
            settings = newSettings;
            foreach (var w in activeWires)
            {
                if (w != null) w.ApplyStyling();
            }
        }

        private void EnsureSettings()
        {
            if (settings == null)
            {
                settings = Resources.Load<WireSettings>("WireSettings");
                if (settings == null)
                {
                    settings = ScriptableObject.CreateInstance<WireSettings>();
                }
            }
        }

        private void ResolveReferenceModel()
        {
            if (referenceModelRoot != null) return;
            GameObject breaker = GameObject.Find("Four-Pole-Breaker-final");
            if (breaker != null) referenceModelRoot = breaker.transform;
        }

        public void RefreshTerminalRegistry()
        {
            terminalRegistry.Clear();
            Terminal[] terminals = FindObjectsByType<Terminal>(FindObjectsSortMode.None);
            foreach (var t in terminals)
            {
                if (t != null && !string.IsNullOrEmpty(t.TerminalId))
                {
                    terminalRegistry[t.TerminalId] = t;
                }
            }
        }

        public Terminal GetTerminalById(string terminalId)
        {
            if (string.IsNullOrEmpty(terminalId)) return null;
            if (terminalRegistry.TryGetValue(terminalId, out Terminal term))
            {
                return term;
            }

            // Fallback scan
            RefreshTerminalRegistry();
            terminalRegistry.TryGetValue(terminalId, out term);
            return term;
        }

        public List<Wire> GetWiresOnTerminal(Terminal t)
        {
            List<Wire> result = new List<Wire>();
            if (t == null) return result;

            for (int i = 0; i < activeWires.Count; i++)
            {
                Wire w = activeWires[i];
                if (w != null && (w.StartTerminal == t || w.EndTerminal == t))
                {
                    result.Add(w);
                }
            }
            return result;
        }

        /// <summary>
        /// Creates a new interactive wire starting at startTerminal.
        /// The end endpoint is left free for dragging.
        /// </summary>
        public Wire CreateWire(Terminal startTerminal)
        {
            if (startTerminal == null) return null;

            EnsureSettings();
            ResolveReferenceModel();

            Color assignedColor = GetNextWireColor();
            int lane = CalculateLaneIndex(startTerminal);

            GameObject wireObj;
            if (wirePrefab != null)
            {
                wireObj = Instantiate(wirePrefab, transform);
            }
            else
            {
                wireObj = new GameObject("Wire_" + (activeWires.Count + 1));
                wireObj.transform.SetParent(transform, false);
            }

            Wire wire = wireObj.GetComponent<Wire>();
            if (wire == null) wire = wireObj.AddComponent<Wire>();

            wire.Initialize(settings, assignedColor, referenceModelRoot, lane);

            // Lock start endpoint to startTerminal
            wire.StartEndpoint.AttachToTerminal(startTerminal);

            // Position end endpoint at start terminal initially
            wire.EndEndpoint.MoveDirect(startTerminal.AnchorPosition);

            activeWires.Add(wire);
            return wire;
        }

        public void NotifyWireConnected(Wire wire)
        {
            if (wire == null || !wire.IsConnected) return;

            Terminal a = wire.StartTerminal;
            Terminal b = wire.EndTerminal;

            wire.SetDraggingVisualState(false);
            wire.UpdatePath();

            OnWireConnected?.Invoke(a, b, wire);
        }

        public void NotifyWireDisconnected(Wire wire, Terminal detachedTerminal)
        {
            if (wire == null) return;

            Terminal remaining = (wire.StartTerminal == detachedTerminal) ? wire.EndTerminal : wire.StartTerminal;
            OnWireDisconnected?.Invoke(detachedTerminal, remaining, wire);
        }

        public void RemoveWire(Wire wire)
        {
            if (wire == null) return;

            Terminal a = wire.StartTerminal;
            Terminal b = wire.EndTerminal;

            if (wire.StartEndpoint != null) wire.StartEndpoint.Detach();
            if (wire.EndEndpoint != null) wire.EndEndpoint.Detach();

            if (a != null || b != null)
            {
                OnWireDisconnected?.Invoke(a, b, wire);
            }

            activeWires.Remove(wire);
            Destroy(wire.gameObject);
        }

        public void ClearAll()
        {
            for (int i = activeWires.Count - 1; i >= 0; i--)
            {
                if (activeWires[i] != null)
                {
                    RemoveWire(activeWires[i]);
                }
            }
            activeWires.Clear();
        }

        private Color GetNextWireColor()
        {
            if (settings != null && settings.wireColorPalette != null && settings.wireColorPalette.Length > 0)
            {
                Color c = settings.wireColorPalette[colorPaletteIndex % settings.wireColorPalette.Length];
                colorPaletteIndex++;
                return c;
            }
            return new Color(0.92f, 0.22f, 0.22f, 1f);
        }

        private int CalculateLaneIndex(Terminal start)
        {
            int count = 0;
            for (int i = 0; i < activeWires.Count; i++)
            {
                if (activeWires[i] != null && activeWires[i].StartTerminal == start)
                {
                    count++;
                }
            }
            return count;
        }

        #region JSON Save / Load
        public string SaveConnectionsToJson()
        {
            WireSaveContainer container = new WireSaveContainer();
            for (int i = 0; i < activeWires.Count; i++)
            {
                Wire w = activeWires[i];
                if (w != null && w.IsConnected)
                {
                    container.connections.Add(new WireConnectionData
                    {
                        startTerminalId = w.StartTerminal.TerminalId,
                        endTerminalId = w.EndTerminal.TerminalId,
                        colorIndex = i
                    });
                }
            }
            return JsonUtility.ToJson(container, true);
        }

        public bool LoadConnectionsFromJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return false;

            try
            {
                WireSaveContainer container = JsonUtility.FromJson<WireSaveContainer>(json);
                if (container == null || container.connections == null) return false;

                ClearAll();
                RefreshTerminalRegistry();

                foreach (var conn in container.connections)
                {
                    Terminal start = GetTerminalById(conn.startTerminalId);
                    Terminal end = GetTerminalById(conn.endTerminalId);

                    if (start != null && end != null)
                    {
                        Wire wire = CreateWire(start);
                        if (wire != null)
                        {
                            wire.EndEndpoint.AttachToTerminal(end);
                            NotifyWireConnected(wire);
                        }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[WireManager] Failed to load connections: {ex.Message}");
                return false;
            }
        }
        #endregion
    }
}
