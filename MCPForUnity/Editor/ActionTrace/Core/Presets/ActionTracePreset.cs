using System;
using System.Collections.Generic;
using UnityEngine;

namespace MCPForUnity.Editor.ActionTrace.Core.Presets
{
    /// <summary>
    /// Preset configurations for ActionTrace settings.
    /// Each preset provides a balanced configuration for specific use cases.
    /// </summary>
    [Serializable]
    public sealed class ActionTracePreset
    {
        public string Name;
        public string Description;
        public float MinImportance;
        public int MaxEvents;
        public int HotEventCount;
        public bool EnableEventMerging;
        public int MergeWindowMs;
        public int TransactionWindowMs;

        public static readonly ActionTracePreset DebugAll = new()
        {
            Name = "Debug (All Events)",
            Description = "Record all events for debugging and complete traceability. Higher memory usage.",
            MinImportance = 0.0f,
            MaxEvents = 2000,
            HotEventCount = 400,
            EnableEventMerging = false,
            MergeWindowMs = 0,
            TransactionWindowMs = 5000
        };

        public static readonly ActionTracePreset Standard = new()
        {
            Name = "Standard",
            Description = "Standard configuration balancing performance and traceability. Suitable for daily development.",
            MinImportance = 0.4f,
            MaxEvents = 800,
            HotEventCount = 150,
            EnableEventMerging = true,
            MergeWindowMs = 100,
            TransactionWindowMs = 2000
        };

        public static readonly ActionTracePreset Lean = new()
        {
            Name = "Lean (Minimal)",
            Description = "Minimal configuration, only records high importance events. Lowest memory usage.",
            MinImportance = 0.7f,
            MaxEvents = 300,
            HotEventCount = 50,
            EnableEventMerging = true,
            MergeWindowMs = 50,
            TransactionWindowMs = 1000
        };

        public static readonly ActionTracePreset AIFocused = new()
        {
            Name = "AI Assistant",
            Description = "AI assistant optimized configuration. Focuses on asset changes and build events.",
            MinImportance = 0.5f,
            MaxEvents = 1000,
            HotEventCount = 200,
            EnableEventMerging = true,
            MergeWindowMs = 100,
            TransactionWindowMs = 3000
        };

        public static readonly ActionTracePreset Realtime = new()
        {
            Name = "Realtime",
            Description = "Realtime collaboration configuration. Minimal latency, high-frequency event sampling.",
            MinImportance = 0.3f,
            MaxEvents = 600,
            HotEventCount = 100,
            EnableEventMerging = true,
            MergeWindowMs = 50,
            TransactionWindowMs = 1500
        };

        public static readonly ActionTracePreset Performance = new()
        {
            Name = "Performance",
            Description = "Performance-first configuration. Minimal memory overhead, only critical events.",
            MinImportance = 0.6f,
            MaxEvents = 200,
            HotEventCount = 30,
            EnableEventMerging = true,
            MergeWindowMs = 50,
            TransactionWindowMs = 1000
        };

        public static readonly List<ActionTracePreset> AllPresets = new()
        {
            DebugAll, Standard, Lean, AIFocused, Realtime, Performance
        };
    }
}
