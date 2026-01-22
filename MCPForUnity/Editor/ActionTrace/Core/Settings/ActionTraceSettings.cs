using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.ActionTrace.Core.Presets;

namespace MCPForUnity.Editor.ActionTrace.Core.Settings
{
    /// <summary>
    /// Layered settings for event filtering.
    /// Controls which events are recorded at the capture layer.
    /// </summary>
    [Serializable]
    public sealed class FilteringSettings
    {
        [Range(0f, 1f)]
        [Tooltip("Minimum importance threshold (0.0-1.0). Events below this value will not be recorded. 0.0=all, 0.4=medium+, 0.7=high+")]
        public float MinImportanceForRecording = 0.4f;

        [Tooltip("Bypass importance filter - Disabled for now to avoid excessive data volume")]
        public bool BypassImportanceFilter = false;

        [Tooltip("List of disabled event types. Empty means all enabled.")]
        public string[] DisabledEventTypes = Array.Empty<string>();
    }

    /// <summary>
    /// Layered settings for event merging and aggregation.
    /// Controls how high-frequency events are combined.
    /// </summary>
    [Serializable]
    public sealed class MergingSettings
    {
        [Tooltip("Enable event merging. High-frequency events will be merged within the time window.")]
        public bool EnableEventMerging = true;

        [Range(0, 5000)]
        [Tooltip("Event merging time window (0-5000ms). High-frequency events within this window are merged.")]
        public int MergeWindowMs = 100;

        [Range(100, 10000)]
        [Tooltip("Transaction aggregation time window (100-10000ms). Events within this window are grouped into the same logical transaction.")]
        public int TransactionWindowMs = 2000;
    }

    /// <summary>
    /// Layered settings for storage and memory management.
    /// Controls event store size and dehydration behavior.
    /// </summary>
    [Serializable]
    public sealed class StorageSettings
    {
        [Range(100, 5000)]
        [Tooltip("Soft limit: target event count (100-5000). ContextMappings = MaxEvents × 2 (e.g., 1000→2000, 5000→10000).")]
        public int MaxEvents = 800;

        [Range(10, 1000)]
        [Tooltip("Number of hot events (10-1000) to retain with full payload. Events are kept in sequence order; the first N events (oldest) have full payload, while events beyond this limit are dehydrated (Payload=null).")]
        public int HotEventCount = 150;
    }

    // SamplingSettings is deprecated. Sampling is now configured via SamplingConfig
    // with hardcoded values to avoid runtime configuration complexity.
    //
    // [Serializable]
    // public sealed class SamplingSettings
    // {
    //     public bool EnableSampling = true;
    //     public float SamplingImportanceThreshold = 0.3f;
    //     public int HierarchySamplingMs = 1000;
    //     public int SelectionSamplingMs = 500;
    //     public int PropertySamplingMs = 200;
    // }

    /// <summary>
    /// Persistent settings for the ActionTrace system.
    /// Organized into logical layers for better clarity and maintainability.
    /// </summary>
    [CreateAssetMenu(fileName = "ActionTraceSettings", menuName = "ActionTrace/Settings")]
    public sealed class ActionTraceSettings : ScriptableObject
    {
        private const string SettingsPath = "Assets/ActionTraceSettings.asset";

        private static ActionTraceSettings _instance;

        // ========== Layered Settings ==========

        [Header("Event Filtering")]
        [Tooltip("Controls which events are recorded based on importance and type")]
        public FilteringSettings Filtering = new();

        [Header("Event Merging")]
        [Tooltip("Controls how high-frequency events are combined")]
        public MergingSettings Merging = new();

        [Header("Storage & Memory")]
        [Tooltip("Controls event storage limits and memory management")]
        public StorageSettings Storage = new();

        // Sampling is configured via SamplingConfig with hardcoded values
        // To customize, modify SamplingConfig.InitializeStrategies() directly
        // public SamplingSettings Sampling = new();

        // ========== Runtime State ==========

        [NonSerialized]
        private string _currentPresetName = "Standard";

        [NonSerialized]
        private bool _isDirty;

        // ========== Singleton Access ==========

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
        /// Currently active preset name.
        /// </summary>
        public string CurrentPresetName => _currentPresetName;

        /// <summary>
        /// Whether settings have unsaved changes.
        /// </summary>
        public bool IsDirty => _isDirty;

        // ========== Preset Management ==========

        /// <summary>
        /// Apply a preset configuration to this settings instance.
        /// </summary>
        public void ApplyPreset(ActionTracePreset preset)
        {
            if (preset == null) return;

            Filtering.MinImportanceForRecording = preset.MinImportance;
            Storage.MaxEvents = preset.MaxEvents;
            Storage.HotEventCount = preset.HotEventCount;
            Merging.EnableEventMerging = preset.EnableEventMerging;
            Merging.MergeWindowMs = preset.MergeWindowMs;
            Merging.TransactionWindowMs = preset.TransactionWindowMs;

            _currentPresetName = preset.Name;
            MarkDirty();
            Save();

            McpLog.Info($"[ActionTraceSettings] Applied preset: {preset.Name}");
        }

        /// <summary>
        /// Get all available presets.
        /// </summary>
        public static List<ActionTracePreset> GetPresets() => ActionTracePreset.AllPresets;

        /// <summary>
        /// Find preset by name.
        /// </summary>
        public static ActionTracePreset FindPreset(string name)
        {
            return ActionTracePreset.AllPresets.Find(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        // ========== Persistence ==========

        /// <summary>
        /// Reloads settings from disk.
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
            // Apply Standard preset by default
            settings.ApplyPreset(ActionTracePreset.Standard);
            AssetDatabase.CreateAsset(settings, SettingsPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            McpLog.Info($"[ActionTraceSettings] Created new settings at {SettingsPath}");
            return settings;
        }

        /// <summary>
        /// Saves the current settings to disk.
        /// </summary>
        public void Save()
        {
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
            _isDirty = false;
        }

        /// <summary>
        /// Mark settings as dirty (unsaved changes).
        /// </summary>
        public void MarkDirty()
        {
            _isDirty = true;
        }

        /// <summary>
        /// Shows the settings inspector window.
        /// </summary>
        public static void ShowSettingsWindow()
        {
            Selection.activeObject = Instance;
            EditorApplication.ExecuteMenuItem("Window/General/Inspector");
        }

        /// <summary>
        /// Validates settings and returns any issues.
        /// </summary>
        public List<string> Validate()
        {
            var issues = new List<string>();

            // Note: MinImportanceForRecording, MergeWindowMs, TransactionWindowMs, HotEventCount
            // are now constrained by Range attributes in Inspector.

            // Dynamic validation: HotEventCount should not exceed MaxEvents (runtime check)
            if (Storage.HotEventCount > Storage.MaxEvents)
                issues.Add("HotEventCount should not exceed MaxEvents");

            return issues;
        }

        /// <summary>
        /// Get estimated memory usage in bytes.
        /// </summary>
        public long GetEstimatedMemoryUsage()
        {
            // Approximate: each event ~300 bytes when hydrated, ~100 bytes when dehydrated
            int hotEvents = Storage.HotEventCount;
            int coldEvents = Storage.MaxEvents - Storage.HotEventCount;
            return (long)(hotEvents * 300 + coldEvents * 100);
        }

        /// <summary>
        /// Get estimated memory usage as human-readable string.
        /// </summary>
        public string GetEstimatedMemoryUsageString()
        {
            long bytes = GetEstimatedMemoryUsage();
            return bytes < 1024 ? $"{bytes} B"
                : bytes < 1024 * 1024 ? $"{bytes / 1024} KB"
                : $"{bytes / (1024 * 1024)} MB";
        }
    }
}
