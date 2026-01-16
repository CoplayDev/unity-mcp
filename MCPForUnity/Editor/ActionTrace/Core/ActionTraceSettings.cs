using System;
using UnityEngine;
using UnityEditor;

namespace MCPForUnity.Editor.ActionTrace.Core
{
    /// <summary>
    /// Persistent settings for the ActionTrace system.
    /// These settings control event filtering behavior at the store level,
    /// not just UI display - events below the threshold may not be recorded.
    /// </summary>
    public class ActionTraceSettings : ScriptableObject
    {
        private const string SettingsPath = "Assets/ActionTraceSettings.asset";

        private static ActionTraceSettings _instance;

        /// <summary>
        /// Gets or creates the singleton settings instance.
        /// </summary>
        public static ActionTraceSettings Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = LoadSettings();
                    if (_instance == null)
                    {
                        _instance = CreateSettings();
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// Minimum importance threshold for recording events.
        /// Events with importance below this value will be ignored at the store level.
        /// Range: 0.0 (all events) to 1.0 (only critical events)
        /// </summary>
        public float MinImportanceForRecording = 0.4f;

        /// <summary>
        /// Event merging time window in milliseconds.
        /// High-frequency events within this window will be merged.
        /// </summary>
        public int MergeWindowMs = 100;

        /// <summary>
        /// Maximum number of events to keep in the store.
        /// </summary>
        public int MaxEvents = 1000;

        /// <summary>
        /// Number of "hot" events to keep with full payload.
        /// Older events are dehydrated (payload = null).
        /// </summary>
        public int HotEventCount = 100;

        /// <summary>
        /// Enable event merging for high-frequency events.
        /// </summary>
        public bool EnableEventMerging = true;

        /// <summary>
        /// Event types to track (empty = all types).
        /// Specific event types can be disabled here.
        /// </summary>
        public string[] DisabledEventTypes = Array.Empty<string>();

        /// <summary>
        /// Time window for transaction aggregation in milliseconds.
        /// Events within this window are grouped into a single logical transaction.
        /// Used by TransactionAggregator when no ToolCallId information exists.
        /// </summary>
        public int TransactionWindowMs = 2000;

        /// <summary>
        /// Reloads settings from disk.
        /// Call this after manually modifying the settings asset.
        /// </summary>
        public static void Reload()
        {
            _instance = LoadSettings();
        }

        private static ActionTraceSettings LoadSettings()
        {
            return AssetDatabase.LoadAssetAtPath<ActionTraceSettings>(SettingsPath);
        }

        private static ActionTraceSettings CreateSettings()
        {
            var settings = CreateInstance<ActionTraceSettings>();
            AssetDatabase.CreateAsset(settings, SettingsPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[ActionTraceSettings] Created new settings at {SettingsPath}");
            return settings;
        }

        /// <summary>
        /// Saves the current settings to disk.
        /// </summary>
        public void Save()
        {
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
            Debug.Log("[ActionTraceSettings] Settings saved");
        }

        /// <summary>
        /// Shows the settings inspector window.
        /// </summary>
        public static void ShowSettingsWindow()
        {
            Selection.activeObject = Instance;
            EditorApplication.ExecuteMenuItem("Window/General/Inspector");
        }
    }

    /// <summary>
    /// Custom editor for ActionTraceSettings.
    /// Provides a clean UI for modifying ActionTrace settings.
    /// </summary>
    [CustomEditor(typeof(ActionTraceSettings))]
    public class ActionTraceSettingsEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            GUILayout.Label("ActionTrace Settings", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "These settings control event recording behavior at the store level.\n" +
                "Changes affect which events are captured, not just UI display.",
                MessageType.Info);
            EditorGUILayout.Space();

            SerializedObject so = serializedObject;
            so.Update();

            EditorGUILayout.LabelField("Event Filtering", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(so.FindProperty(nameof(ActionTraceSettings.MinImportanceForRecording)),
                new GUIContent("Min Importance for Recording",
                "Events below this importance will NOT be recorded. 0.0 = all events, 0.4 = medium+, 0.7 = high+"));

            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Event Merging", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(so.FindProperty(nameof(ActionTraceSettings.EnableEventMerging)),
                new GUIContent("Enable Event Merging", "Merge high-frequency events within time window"));
            EditorGUILayout.PropertyField(so.FindProperty(nameof(ActionTraceSettings.MergeWindowMs)),
                new GUIContent("Merge Window (ms)", "Time window for event merging"));

            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Storage Limits", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(so.FindProperty(nameof(ActionTraceSettings.MaxEvents)),
                new GUIContent("Max Events", "Maximum number of events to store"));
            EditorGUILayout.PropertyField(so.FindProperty(nameof(ActionTraceSettings.HotEventCount)),
                new GUIContent("Hot Events Count", "Keep full payload for latest N events"));

            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Transaction Aggregation", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(so.FindProperty(nameof(ActionTraceSettings.TransactionWindowMs)),
                new GUIContent("Transaction Window (ms)", "Time window for grouping events into logical transactions"));

            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Quick Presets", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("All Events (Debug)"))
            {
                SetImportance(0f);
            }
            if (GUILayout.Button("Low+"))
            {
                SetImportance(0f);
            }
            if (GUILayout.Button("Medium+"))
            {
                SetImportance(0.4f);
            }
            if (GUILayout.Button("High+"))
            {
                SetImportance(0.7f);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            so.ApplyModifiedProperties();

            if (GUI.changed)
            {
                (target as ActionTraceSettings)?.Save();
            }
        }

        private void SetImportance(float value)
        {
            SerializedProperty prop = serializedObject.FindProperty(nameof(ActionTraceSettings.MinImportanceForRecording));
            prop.floatValue = value;
            serializedObject.ApplyModifiedProperties();
            (target as ActionTraceSettings)?.Save();
        }
    }
}
