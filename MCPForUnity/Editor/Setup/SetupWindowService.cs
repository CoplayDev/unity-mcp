using System;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Dependencies;
using MCPForUnity.Editor.Dependencies.Models;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Windows;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Setup
{
    /// <summary>
    /// Handles automatic triggering of the MCP setup window and exposes menu entry points
    /// </summary>
    [InitializeOnLoad]
    public static class SetupWindowService
    {
        private const string SETUP_COMPLETED_KEY = EditorPrefKeys.SetupCompleted;
        private const string SETUP_DISMISSED_KEY = EditorPrefKeys.SetupDismissed;

        // Use SessionState to persist "checked this editor session" across domain reloads.
        // SessionState survives assembly reloads within the same Editor session, which prevents
        // the setup window from reappearing after code reloads / playmode transitions.
        private const string SessionCheckedKey = "MCPForUnity.SetupWindowCheckedThisEditorSession";

        static SetupWindowService()
        {
            // Skip in batch mode
            if (Application.isBatchMode)
                return;

            // Show Setup Window on package import
            EditorApplication.delayCall += CheckSetupNeeded;
        }

        /// <summary>
        /// Check if Setup Window should be shown
        /// </summary>
        private static void CheckSetupNeeded()
        {
            // Ensure we only run once per Editor session (survives domain reloads).
            // This avoids showing the setup dialog repeatedly when scripts recompile or Play mode toggles.
            if (SessionState.GetBool(SessionCheckedKey, false))
                return;

            SessionState.SetBool(SessionCheckedKey, true);

            try
            {
                // Check if setup was already completed or dismissed in previous sessions
                bool setupCompleted = EditorPrefs.GetBool(SETUP_COMPLETED_KEY, false);
                bool setupDismissed = EditorPrefs.GetBool(SETUP_DISMISSED_KEY, false);

                // Only show Setup Window if it hasn't been completed or dismissed before
                if (!(setupCompleted || setupDismissed))
                {
                    McpLog.Info("Package imported - showing Setup Window", always: false);

                    var dependencyResult = DependencyManager.CheckAllDependencies();
                    EditorApplication.delayCall += () => ShowSetupWindow(dependencyResult);
                }
                else
                {
                    McpLog.Info(
                        "Setup Window skipped - previously completed or dismissed",
                        always: false
                    );
                }
            }
            catch (Exception ex)
            {
                McpLog.Error($"Error checking setup status: {ex.Message}");
            }
        }

        /// <summary>
        /// Show the setup window
        /// </summary>
        public static void ShowSetupWindow(DependencyCheckResult dependencyResult = null)
        {
            try
            {
                dependencyResult ??= DependencyManager.CheckAllDependencies();
                MCPSetupWindow.ShowWindow(dependencyResult);
            }
            catch (Exception ex)
            {
                McpLog.Error($"Error showing setup window: {ex.Message}");
            }
        }

        /// <summary>
        /// Mark setup as completed
        /// </summary>
        public static void MarkSetupCompleted()
        {
            EditorPrefs.SetBool(SETUP_COMPLETED_KEY, true);
            McpLog.Info("Setup marked as completed");
        }

        /// <summary>
        /// Mark setup as dismissed
        /// </summary>
        public static void MarkSetupDismissed()
        {
            EditorPrefs.SetBool(SETUP_DISMISSED_KEY, true);
            McpLog.Info("Setup marked as dismissed");
        }
    }
}
