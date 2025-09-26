using System;
using MCPForUnity.Editor.Dependencies;
using MCPForUnity.Editor.Dependencies.Models;
using MCPForUnity.Editor.Helpers;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Setup
{
    /// <summary>
    /// Handles automatic triggering of the setup wizard based on dependency state
    /// </summary>
    [InitializeOnLoad]
    public static class SetupWizard
    {
        private const string SETUP_STATE_KEY = "MCPForUnity.SetupState";
        private const string PACKAGE_VERSION = "3.4.0"; // Should match package.json version
        
        private static SetupState _setupState;
        private static bool _hasCheckedThisSession = false;

        static SetupWizard()
        {
            // Skip in batch mode unless explicitly allowed
            if (Application.isBatchMode && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("UNITY_MCP_ALLOW_BATCH")))
            {
                return;
            }

            // Defer setup check until editor is ready
            EditorApplication.delayCall += CheckSetupNeeded;
        }

        /// <summary>
        /// Get the current setup state
        /// </summary>
        public static SetupState GetSetupState()
        {
            if (_setupState == null)
            {
                LoadSetupState();
            }
            return _setupState;
        }

        /// <summary>
        /// Save the current setup state
        /// </summary>
        public static void SaveSetupState()
        {
            if (_setupState != null)
            {
                try
                {
                    string json = JsonUtility.ToJson(_setupState, true);
                    EditorPrefs.SetString(SETUP_STATE_KEY, json);
                    McpLog.Info("Setup state saved", always: false);
                }
                catch (Exception ex)
                {
                    McpLog.Error($"Failed to save setup state: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Load setup state from EditorPrefs
        /// </summary>
        private static void LoadSetupState()
        {
            try
            {
                string json = EditorPrefs.GetString(SETUP_STATE_KEY, "");
                if (!string.IsNullOrEmpty(json))
                {
                    _setupState = JsonUtility.FromJson<SetupState>(json);
                }
            }
            catch (Exception ex)
            {
                McpLog.Warn($"Failed to load setup state: {ex.Message}");
            }

            // Create default state if loading failed
            if (_setupState == null)
            {
                _setupState = new SetupState();
            }
        }

        /// <summary>
        /// Check if setup wizard should be shown
        /// </summary>
        private static void CheckSetupNeeded()
        {
            // Only check once per session
            if (_hasCheckedThisSession)
                return;

            _hasCheckedThisSession = true;

            try
            {
                var setupState = GetSetupState();

                // Don't show setup if user has dismissed it or if already completed for this version
                if (!setupState.ShouldShowSetup(PACKAGE_VERSION))
                {
                    McpLog.Info("Setup wizard not needed - already completed or dismissed", always: false);
                    return;
                }

                // Check if dependencies are missing
                var dependencyResult = DependencyManager.CheckAllDependencies();
                if (dependencyResult.IsSystemReady)
                {
                    McpLog.Info("All dependencies available - marking setup as completed", always: false);
                    setupState.MarkSetupCompleted(PACKAGE_VERSION);
                    SaveSetupState();
                    return;
                }

                // Show setup wizard if dependencies are missing
                var missingRequired = dependencyResult.GetMissingRequired();
                if (missingRequired.Count > 0)
                {
                    McpLog.Info($"Missing required dependencies: {string.Join(", ", missingRequired.ConvertAll(d => d.Name))}");
                    
                    // Delay showing the wizard slightly to ensure Unity is fully loaded
                    EditorApplication.delayCall += () => ShowSetupWizard(dependencyResult);
                }
            }
            catch (Exception ex)
            {
                McpLog.Error($"Error checking setup requirements: {ex.Message}");
            }
        }

        /// <summary>
        /// Show the setup wizard window
        /// </summary>
        public static void ShowSetupWizard(DependencyCheckResult dependencyResult = null)
        {
            try
            {
                // If no dependency result provided, check now
                if (dependencyResult == null)
                {
                    dependencyResult = DependencyManager.CheckAllDependencies();
                }

                // Show the setup wizard window
                SetupWizardWindow.ShowWindow(dependencyResult);
                
                // Record that we've attempted setup
                var setupState = GetSetupState();
                setupState.RecordSetupAttempt();
                SaveSetupState();
            }
            catch (Exception ex)
            {
                McpLog.Error($"Error showing setup wizard: {ex.Message}");
            }
        }

        /// <summary>
        /// Mark setup as completed
        /// </summary>
        public static void MarkSetupCompleted()
        {
            try
            {
                var setupState = GetSetupState();
                setupState.MarkSetupCompleted(PACKAGE_VERSION);
                SaveSetupState();
                
                McpLog.Info("Setup marked as completed");
            }
            catch (Exception ex)
            {
                McpLog.Error($"Error marking setup as completed: {ex.Message}");
            }
        }

        /// <summary>
        /// Mark setup as dismissed
        /// </summary>
        public static void MarkSetupDismissed()
        {
            try
            {
                var setupState = GetSetupState();
                setupState.MarkSetupDismissed();
                SaveSetupState();
                
                McpLog.Info("Setup marked as dismissed");
            }
            catch (Exception ex)
            {
                McpLog.Error($"Error marking setup as dismissed: {ex.Message}");
            }
        }

        /// <summary>
        /// Reset setup state (for debugging or re-setup)
        /// </summary>
        public static void ResetSetupState()
        {
            try
            {
                var setupState = GetSetupState();
                setupState.Reset();
                SaveSetupState();
                
                McpLog.Info("Setup state reset");
            }
            catch (Exception ex)
            {
                McpLog.Error($"Error resetting setup state: {ex.Message}");
            }
        }

        /// <summary>
        /// Force show setup wizard (for manual invocation)
        /// </summary>
        [MenuItem("Window/MCP for Unity/Setup Wizard", priority = 1)]
        public static void ShowSetupWizardManual()
        {
            ShowSetupWizard();
        }

        /// <summary>
        /// Reset setup and show wizard again
        /// </summary>
        [MenuItem("Window/MCP for Unity/Reset Setup", priority = 2)]
        public static void ResetAndShowSetup()
        {
            ResetSetupState();
            _hasCheckedThisSession = false;
            ShowSetupWizard();
        }

        /// <summary>
        /// Check dependencies and show status
        /// </summary>
        [MenuItem("Window/MCP for Unity/Check Dependencies", priority = 3)]
        public static void CheckDependencies()
        {
            var result = DependencyManager.CheckAllDependencies();
            var diagnostics = DependencyManager.GetDependencyDiagnostics();
            
            Debug.Log($"<b><color=#2EA3FF>MCP-FOR-UNITY</color></b>: Dependency Check Results\n{diagnostics}");
            
            if (!result.IsSystemReady)
            {
                bool showWizard = EditorUtility.DisplayDialog(
                    "MCP for Unity - Dependencies",
                    $"System Status: {result.Summary}\n\nWould you like to open the Setup Wizard?",
                    "Open Setup Wizard",
                    "Close"
                );
                
                if (showWizard)
                {
                    ShowSetupWizard(result);
                }
            }
            else
            {
                EditorUtility.DisplayDialog(
                    "MCP for Unity - Dependencies",
                    "✓ All dependencies are available and ready!\n\nMCP for Unity is ready to use.",
                    "OK"
                );
            }
        }
    }
}