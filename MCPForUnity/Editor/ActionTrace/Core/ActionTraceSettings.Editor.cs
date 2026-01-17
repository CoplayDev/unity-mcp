using System;
using UnityEngine;
using UnityEditor;

namespace MCPForUnity.Editor.ActionTrace.Core
{
    /// <summary>
    /// Custom editor for ActionTraceSettings with layered UI.
    /// </summary>
    [CustomEditor(typeof(ActionTraceSettings))]
    public sealed class ActionTraceSettingsEditor : UnityEditor.Editor
    {
        private Vector2 _scrollPos;
        private bool _showFiltering = true;
        private bool _showMerging = true;
        private bool _showStorage = true;
        private bool _showSampling = true;

        public override void OnInspectorGUI()
        {
            var settings = (ActionTraceSettings)target;

            serializedObject.Update();

            EditorGUILayout.Space(6);
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Label("ActionTrace Settings", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Validate", GUILayout.Width(80)))
                {
                    ValidateAndShowIssues(settings);
                }
            }

            EditorGUILayout.HelpBox(
                "Layered settings configuration. Changes affect event capture behavior, not just UI display.",
                MessageType.Info);
            EditorGUILayout.Space(4);

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            // Presets Section
            DrawPresetsSection(settings);
            EditorGUILayout.Space(8);

            // Layered Settings Sections
            DrawFilteringSection(settings);
            DrawMergingSection(settings);
            DrawStorageSection(settings);
            DrawSamplingSection(settings);

            EditorGUILayout.EndScrollView();

            // Apply changes only if properties were actually modified
            if (serializedObject.ApplyModifiedProperties())
            {
                settings.MarkDirty();
                settings.Save();
            }
        }

        private void DrawPresetsSection(ActionTraceSettings settings)
        {
            EditorGUILayout.LabelField("Preset Configuration", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                $"Current: {settings.CurrentPresetName} | Est. Memory: {settings.GetEstimatedMemoryUsageString()}",
                MessageType.None);

            using (new GUILayout.HorizontalScope())
            {
                foreach (var preset in ActionTracePreset.AllPresets)
                {
                    if (GUILayout.Button(preset.Name))
                    {
                        settings.ApplyPreset(preset);
                        GUI.changed = true;
                    }
                }
            }

            // Show description of current preset
            var currentPreset = ActionTracePreset.AllPresets.Find(p => p.Name == settings.CurrentPresetName);
            if (currentPreset != null)
            {
                EditorGUILayout.HelpBox(currentPreset.Description, MessageType.None);
            }
        }

        private void DrawFilteringSection(ActionTraceSettings settings)
        {
            _showFiltering = EditorGUILayout.Foldout(_showFiltering, "Event Filtering", true, EditorStyles.boldLabel);
            if (!_showFiltering) return;

            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(ActionTraceSettings.Filtering)),
                    new GUIContent("Filter Settings", "Controls which events are recorded"));

                EditorGUILayout.HelpBox(
                    "MinImportance: 0.0=all, 0.4=medium+, 0.7=high+\n" +
                    "Bypass: Skip filter, record all events",
                    MessageType.None);
            }
            EditorGUILayout.Space(4);
        }

        private void DrawMergingSection(ActionTraceSettings settings)
        {
            _showMerging = EditorGUILayout.Foldout(_showMerging, "Event Merging", true, EditorStyles.boldLabel);
            if (!_showMerging) return;

            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(ActionTraceSettings.Merging)),
                    new GUIContent("Merge Settings", "Controls high-frequency event merging and transaction aggregation"));

                EditorGUILayout.HelpBox(
                    "MergeWindow: Similar events within window are merged\n" +
                    "TransactionWindow: Events within window grouped into same transaction",
                    MessageType.None);
            }
            EditorGUILayout.Space(4);
        }

        private void DrawStorageSection(ActionTraceSettings settings)
        {
            _showStorage = EditorGUILayout.Foldout(_showStorage, "Storage Management", true, EditorStyles.boldLabel);
            if (!_showStorage) return;

            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(ActionTraceSettings.Storage)),
                    new GUIContent("Storage Settings", "Controls event storage and memory management"));

                EditorGUILayout.HelpBox(
                    $"MaxEvents: Soft limit (hard limit = MaxEvents × 2, range: 100-5000)\n" +
                    $"HotEventCount: Events kept in memory with full payload\n" +
                    $"ContextMappings: MaxEvents × 2 (e.g., 800→1600, 5000→10000)\n" +
                    $"Est. Memory: {settings.GetEstimatedMemoryUsageString()}",
                    MessageType.None);
            }
            EditorGUILayout.Space(4);
        }

        private void DrawSamplingSection(ActionTraceSettings settings)
        {
            _showSampling = EditorGUILayout.Foldout(_showSampling, "Sampling Configuration", true, EditorStyles.boldLabel);
            if (!_showSampling) return;

            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(ActionTraceSettings.Sampling)),
                    new GUIContent("Sampling Settings", "Controls sampling behavior for high-frequency events"));

                EditorGUILayout.HelpBox(
                    "Hierarchy: Hierarchy change event sampling interval\n" +
                    "Selection: Selection change event sampling interval\n" +
                    "Property: Property modification event sampling interval",
                    MessageType.None);
            }
            EditorGUILayout.Space(4);
        }

        private void ValidateAndShowIssues(ActionTraceSettings settings)
        {
            var issues = settings.Validate();
            if (issues.Count == 0)
            {
                EditorUtility.DisplayDialog("Validation Passed", "All settings are valid!", "OK");
            }
            else
            {
                var message = string.Join("\n", issues);
                EditorUtility.DisplayDialog("Validation Issues", $"Found the following issues:\n\n{message}", "OK");
            }
        }
    }
}
