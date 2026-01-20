using System;
using UnityEngine;
using UnityEditor;
using MCPForUnity.Editor.ActionTrace.Core.Presets;

namespace MCPForUnity.Editor.ActionTrace.Core.Settings
{
    /// <summary>
    /// Custom editor for ActionTraceSettings with clean, native-style layout.
    /// </summary>
    [CustomEditor(typeof(ActionTraceSettings))]
    public sealed class ActionTraceSettingsEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var settings = (ActionTraceSettings)target;

            serializedObject.Update();

            // Header with preset quick-apply
            DrawHeader(settings);

            EditorGUILayout.Space(4);

            // Draw all nested settings with children expanded
            DrawProperty(nameof(ActionTraceSettings.Filtering), "Filtering", "Controls which events are recorded");
            DrawProperty(nameof(ActionTraceSettings.Merging), "Merging", "Controls event merging and transaction grouping");
            DrawProperty(nameof(ActionTraceSettings.Storage), "Storage", "Controls event storage and memory");
            DrawProperty(nameof(ActionTraceSettings.Sampling), "Sampling", "Controls high-frequency event sampling");

            // Apply changes
            if (serializedObject.ApplyModifiedProperties())
            {
                settings.MarkDirty();
                settings.Save();
            }
        }

        private void DrawHeader(ActionTraceSettings settings)
        {
            // Title row with status
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Label("ActionTrace Settings", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();

                var issues = settings.Validate();
                if (issues.Count == 0)
                {
                    GUILayout.Label($"Est. Memory: {settings.GetEstimatedMemoryUsageString()}", EditorStyles.miniLabel);
                }
                else
                {
                    GUI.contentColor = Color.yellow;
                    GUILayout.Label($"⚠ {issues.Count} issue(s)", EditorStyles.miniLabel);
                    GUI.contentColor = Color.white;
                }
            }

            // Preset selector
            EditorGUILayout.Space(2);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Preset", GUILayout.Width(45));
                using (new EditorGUI.ChangeCheckScope())
                {
                    var presetNames = ActionTracePreset.AllPresets.ConvertAll(p => p.Name);
                    var currentIndex = presetNames.FindIndex(n => n == settings.CurrentPresetName);
                    var newIndex = EditorGUILayout.Popup(currentIndex, presetNames.ToArray());

                    if (newIndex != currentIndex && newIndex >= 0)
                    {
                        settings.ApplyPreset(ActionTracePreset.AllPresets[newIndex]);
                    }
                }
            }

            // Current preset description
            var currentPreset = ActionTracePreset.AllPresets.Find(p => p.Name == settings.CurrentPresetName);
            if (currentPreset != null)
            {
                EditorGUILayout.HelpBox(currentPreset.Description, MessageType.Info);
            }
        }

        private void DrawProperty(string propertyName, string displayName, string tooltip)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property == null) return;

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField(displayName, EditorStyles.boldLabel);

            // Expand nested property with all children visible
            EditorGUILayout.PropertyField(property, new GUIContent(tooltip), includeChildren: true);
        }
    }
}
