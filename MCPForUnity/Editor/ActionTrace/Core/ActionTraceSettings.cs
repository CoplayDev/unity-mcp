using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnity.Editor.ActionTrace.Core
{
    /// <summary>
    /// Layered settings for event filtering.
    /// Controls which events are recorded at the capture layer.
    /// </summary>
    [Serializable]
    public sealed class FilteringSettings
    {
        [Tooltip("Minimum importance threshold. Events below this value will not be recorded. 0.0=all, 0.4=medium+, 0.7=high+")]
        public float MinImportanceForRecording = 0.4f;

        [Tooltip("Bypass importance filter - Disabled for now to avoid excessive data volume")]
        public bool BypassImportanceFilter = false;

        [Tooltip("List of disabled event types. Empty means all enabled.")]
        public string[] DisabledEventTypes = Array.Empty<string>();

        [Tooltip("(Future Feature,not used now) Enable emergency AI trigger. Critical events (score 10.0) will interrupt AI request attention.")]
        public bool EnableEmergencyAITrigger = true;
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

        [Tooltip("Event merging time window (milliseconds).")]
        public int MergeWindowMs = 100;

        [Tooltip("Transaction aggregation time window (milliseconds). Events within the window are grouped into the same logical transaction.")]
        public int TransactionWindowMs = 2000;
    }

    /// <summary>
    /// Layered settings for storage and memory management.
    /// Controls event store size and dehydration behavior.
    /// </summary>
    [Serializable]
    public sealed class StorageSettings
    {
        [Tooltip("Soft limit: target number of events to store. Actually allows 1.5x as buffer.")]
        public int MaxEvents = 800;

        [Tooltip("Number of hot events to retain with full payload. Older events will be dehydrated (Payload=null).")]
        public int HotEventCount = 150;

        [Tooltip("Minimum number of events to keep when auto-cleaning.")]
        public int MinKeepEvents = 100;

        [Tooltip("Enable cross-domain reload persistence.")]
        public bool EnablePersistence = true;

        [Tooltip("Auto-save interval in seconds. 0 = disable auto-save.")]
        public int AutoSaveIntervalSeconds = 30;
    }

    /// <summary>
    /// Layered settings for sampling and throttling.
    /// Controls how high-frequency events are sampled.
    /// </summary>
    [Serializable]
    public sealed class SamplingSettings
    {
        [Tooltip("Enable global sampling. Events below threshold will be sampled.")]
        public bool EnableSampling = true;

        [Tooltip("Sampling importance threshold. Events below this value may be sampled.")]
        public float SamplingImportanceThreshold = 0.3f;

        [Tooltip("HierarchyChanged event sampling interval (milliseconds).")]
        public int HierarchySamplingMs = 1000;

        [Tooltip("SelectionChanged event sampling interval (milliseconds).")]
        public int SelectionSamplingMs = 500;

        [Tooltip("PropertyModified event sampling interval (milliseconds).")]
        public int PropertySamplingMs = 200;
    }

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
        // Headers are rendered by ActionTraceSettingsEditor, not here.

        public FilteringSettings Filtering = new();
        public MergingSettings Merging = new();
        public StorageSettings Storage = new();
        public SamplingSettings Sampling = new();

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
            McpLog.Info("[ActionTraceSettings] Settings saved");
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

            if (Filtering.MinImportanceForRecording < 0 || Filtering.MinImportanceForRecording > 1)
                issues.Add("MinImportanceForRecording must be between 0 and 1");

            if (Storage.MaxEvents < 100)
                issues.Add("MaxEvents should be at least 100");

            if (Storage.HotEventCount > Storage.MaxEvents)
                issues.Add("HotEventCount should not exceed MaxEvents");

            if (Storage.HotEventCount < 10)
                issues.Add("HotEventCount should be at least 10");

            if (Merging.MergeWindowMs < 0)
                issues.Add("MergeWindowMs cannot be negative");

            if (Merging.TransactionWindowMs < 100)
                issues.Add("TransactionWindowMs should be at least 100ms");

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
