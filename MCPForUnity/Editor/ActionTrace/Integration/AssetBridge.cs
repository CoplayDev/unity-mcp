using System;
using UnityEditor;
using MCPForUnity.Editor.Tools;
using MCPForUnity.Editor.Helpers;
using System.Collections.Generic;

namespace MCPForUnity.Editor.ActionTrace.Integration
{
    /// <summary>
    /// Low-coupling bridge between ManageAsset and ActionTrace systems.
    ///
    /// This class subscribes to ManageAsset's events and forwards them to ActionTraceEventEmitter.
    /// The bridge pattern ensures:
    /// - ManageAsset has no direct dependency on ActionTrace
    /// - ActionTrace can be enabled/disabled without affecting ManageAsset
    /// - Single point of integration for easy maintenance
    ///
    /// Location: ActionTrace/Integration/ (separate folder for cross-system bridges)
    /// </summary>
    [InitializeOnLoad]
    internal static class ManageAssetBridge
    {
        static ManageAssetBridge()
        {
            // Subscribe to ManageAsset events
            // Events can only be subscribed to; null checks are not needed for subscription
            ManageAsset.OnAssetModified += OnAssetModifiedHandler;
            ManageAsset.OnAssetCreated += OnAssetCreatedHandler;
            ManageAsset.OnAssetDeleted += OnAssetDeletedHandler;
        }

        /// <summary>
        /// Forward asset modification events to ActionTrace.
        /// </summary>
        private static void OnAssetModifiedHandler(string assetPath, string assetType, IReadOnlyDictionary<string, object> changes)
        {
            try
            {
                Capture.ActionTraceEventEmitter.EmitAssetModified(assetPath, assetType, changes);
            }
            catch (Exception ex)
            {
                McpLog.Warn($"[ManageAssetBridge] Failed to record asset modification: {ex.Message}");
            }
        }

        /// <summary>
        /// Forward asset creation events to ActionTrace.
        /// </summary>
        private static void OnAssetCreatedHandler(string assetPath, string assetType)
        {
            try
            {
                Capture.ActionTraceEventEmitter.EmitAssetCreated(assetPath, assetType);
            }
            catch (Exception ex)
            {
                McpLog.Warn($"[ManageAssetBridge] Failed to record asset creation: {ex.Message}");
            }
        }

        /// <summary>
        /// Forward asset deletion events to ActionTrace.
        /// </summary>
        private static void OnAssetDeletedHandler(string assetPath, string assetType)
        {
            try
            {
                Capture.ActionTraceEventEmitter.EmitAssetDeleted(assetPath, assetType);
            }
            catch (Exception ex)
            {
                McpLog.Warn($"[ManageAssetBridge] Failed to record asset deletion: {ex.Message}");
            }
        }

        /// <summary>
        /// Unsubscribe from all events (useful for testing or cleanup).
        /// </summary>
        internal static void Disconnect()
        {
            ManageAsset.OnAssetModified -= OnAssetModifiedHandler;
            ManageAsset.OnAssetCreated -= OnAssetCreatedHandler;
            ManageAsset.OnAssetDeleted -= OnAssetDeletedHandler;
        }
    }
}
