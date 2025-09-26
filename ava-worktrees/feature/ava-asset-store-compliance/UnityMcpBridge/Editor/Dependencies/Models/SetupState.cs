using System;
using UnityEngine;

namespace MCPForUnity.Editor.Dependencies.Models
{
    /// <summary>
    /// Persistent state for the setup wizard to avoid repeated prompts
    /// </summary>
    [Serializable]
    public class SetupState
    {
        /// <summary>
        /// Whether the user has completed the initial setup wizard
        /// </summary>
        public bool HasCompletedSetup { get; set; }

        /// <summary>
        /// Whether the user has dismissed the setup wizard permanently
        /// </summary>
        public bool HasDismissedSetup { get; set; }

        /// <summary>
        /// Last time dependencies were checked
        /// </summary>
        public string LastDependencyCheck { get; set; }

        /// <summary>
        /// Version of the package when setup was last completed
        /// </summary>
        public string SetupVersion { get; set; }

        /// <summary>
        /// Whether to show the setup wizard on next domain reload
        /// </summary>
        public bool ShowSetupOnReload { get; set; }

        /// <summary>
        /// User's preferred installation mode (automatic/manual)
        /// </summary>
        public string PreferredInstallMode { get; set; }

        /// <summary>
        /// Number of times setup has been attempted
        /// </summary>
        public int SetupAttempts { get; set; }

        /// <summary>
        /// Last error encountered during setup
        /// </summary>
        public string LastSetupError { get; set; }

        public SetupState()
        {
            HasCompletedSetup = false;
            HasDismissedSetup = false;
            ShowSetupOnReload = false;
            PreferredInstallMode = "automatic";
            SetupAttempts = 0;
        }

        /// <summary>
        /// Check if setup should be shown based on current state
        /// </summary>
        public bool ShouldShowSetup(string currentVersion)
        {
            // Don't show if user has permanently dismissed
            if (HasDismissedSetup)
                return false;

            // Show if never completed setup
            if (!HasCompletedSetup)
                return true;

            // Show if package version has changed significantly
            if (!string.IsNullOrEmpty(currentVersion) && SetupVersion != currentVersion)
                return true;

            // Show if explicitly requested
            if (ShowSetupOnReload)
                return true;

            return false;
        }

        /// <summary>
        /// Mark setup as completed for the current version
        /// </summary>
        public void MarkSetupCompleted(string version)
        {
            HasCompletedSetup = true;
            SetupVersion = version;
            ShowSetupOnReload = false;
            LastSetupError = null;
        }

        /// <summary>
        /// Mark setup as dismissed permanently
        /// </summary>
        public void MarkSetupDismissed()
        {
            HasDismissedSetup = true;
            ShowSetupOnReload = false;
        }

        /// <summary>
        /// Record a setup attempt with optional error
        /// </summary>
        public void RecordSetupAttempt(string error = null)
        {
            SetupAttempts++;
            LastSetupError = error;
        }

        /// <summary>
        /// Reset setup state (for debugging or re-setup)
        /// </summary>
        public void Reset()
        {
            HasCompletedSetup = false;
            HasDismissedSetup = false;
            ShowSetupOnReload = false;
            SetupAttempts = 0;
            LastSetupError = null;
            LastDependencyCheck = null;
        }
    }
}