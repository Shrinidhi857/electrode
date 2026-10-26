#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using ContactorAR.Breaker;

namespace ContactorAR.Editor
{
    /// <summary>
    /// Custom inspector for MCBPivotLever.
    /// Shows a live angle preview slider, quick action buttons (ON/OFF/TRIP),
    /// and annotated documentation directly in the Inspector.
    /// </summary>
    [CustomEditor(typeof(MCBPivotLever))]
    public class MCBPivotLeverEditor : UnityEditor.Editor
    {
        private float previewAngle = -1.4f;

        public override void OnInspectorGUI()
        {
            MCBPivotLever lever = (MCBPivotLever)target;
            DrawDefaultInspector();

            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("── Live Preview Controls ──", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Calibrated Range: OFF = -1.4° | TRIP = 33.3° | ON = 68.0°", MessageType.Info);

            // Angle slider preview
            float prev = previewAngle;
            previewAngle = EditorGUILayout.Slider("Preview Angle (°)", previewAngle, lever.OffAngle - 5f, lever.OnAngle + 5f);
            if (!Mathf.Approximately(prev, previewAngle) && Application.isPlaying)
            {
                lever.SetAngleManual(previewAngle);
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Quick Actions (Runtime Only)", EditorStyles.miniLabel);

            EditorGUI.BeginDisabledGroup(!Application.isPlaying);
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("◀ OFF (-1.4°)", GUILayout.Height(30)))
            {
                lever.TurnOff();
            }
            if (GUILayout.Button("⚡ TRIP (33.3°)", GUILayout.Height(30)))
            {
                lever.Trip();
            }
            if (GUILayout.Button("ON (68.0°) ▶", GUILayout.Height(30)))
            {
                lever.TurnOn();
            }

            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("↕ Toggle ON / OFF", GUILayout.Height(28)))
            {
                lever.Toggle();
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField($"Current Angle: {lever.CurrentAngle:F2}°  |  State: {lever.CurrentState}", EditorStyles.helpBox);

            EditorGUI.EndDisabledGroup();

            // Force repaint to show live values
            if (Application.isPlaying) Repaint();
        }
    }

    /// <summary>
    /// Custom inspector for ContactorButton with quick-press capability.
    /// </summary>
    [CustomEditor(typeof(ContactorButton))]
    public class ContactorButtonEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space(8);
            EditorGUI.BeginDisabledGroup(!Application.isPlaying);

            if (GUILayout.Button("Press Button (Runtime)", GUILayout.Height(30)))
            {
                ((ContactorButton)target).PressButton();
            }

            EditorGUI.EndDisabledGroup();
        }
    }

    /// <summary>
    /// Custom inspector for ContactorController providing macro buttons.
    /// </summary>
    [CustomEditor(typeof(ContactorController))]
    public class ContactorControllerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            ContactorController ctrl = (ContactorController)target;
            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("── Quick Contactor Actions ──", EditorStyles.boldLabel);

            EditorGUI.BeginDisabledGroup(!Application.isPlaying);
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("SWITCH ON", EditorStyles.miniButtonLeft))
                ctrl.SwitchOn();
            if (GUILayout.Button("SWITCH OFF", EditorStyles.miniButtonMid))
                ctrl.SwitchOff();
            if (GUILayout.Button("TRIP NOW", EditorStyles.miniButtonRight))
                ctrl.TriggerOverloadTrip("Inspector Test");

            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("Auto-Discover 8 Terminals"))
                ctrl.AutoDiscoverTerminalsIfEmpty();

            EditorGUI.EndDisabledGroup();

            if (Application.isPlaying)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField($"Contactor State: {ctrl.State}  |  IsClosed: {ctrl.IsClosed}", EditorStyles.helpBox);
                Repaint();
            }
        }
    }
}
#endif
