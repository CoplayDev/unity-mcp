// TestRunnerNoThrottle.cs
// Sets Unity Editor to "No Throttling" mode during test runs.
// This helps tests that don't trigger compilation run smoothly in the background.
// Note: Tests that trigger mid-run compilation may still stall due to OS-level throttling.

using System;
using System.Reflection;
using MCPForUnity.Editor.Helpers;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Automatically sets the editor to "No Throttling" mode during test runs.
    /// 
    /// This helps prevent background stalls for normal tests. However, tests that trigger
    /// script compilation mid-run may still stall because:
    /// - Internal Unity coroutine waits rely on editor ticks
    /// - OS-level throttling affects the main thread when Unity is backgrounded
    /// - No amount of internal nudging can overcome OS thread scheduling
    /// 
    /// The MCP workflow is unaffected because socket messages provide external stimulus
    /// that wakes Unity's main thread.
    /// </summary>
    [InitializeOnLoad]
    public static class TestRunnerNoThrottle
    {
        private const string ApplicationIdleTimeKey = "ApplicationIdleTime";
        private const string InteractionModeKey = "InteractionMode";

        // SessionState keys to persist across domain reload
        private const string SessionKey_TestRunActive = "TestRunnerNoThrottle_TestRunActive";
        private const string SessionKey_PrevIdleTime = "TestRunnerNoThrottle_PrevIdleTime";
        private const string SessionKey_PrevInteractionMode = "TestRunnerNoThrottle_PrevInteractionMode";
        private const string SessionKey_SettingsCaptured = "TestRunnerNoThrottle_SettingsCaptured";

        // Keep reference to avoid GC and set HideFlags to avoid serialization issues
        private static TestRunnerApi _api;

        static TestRunnerNoThrottle()
        {
            try
            {
                _api = ScriptableObject.CreateInstance<TestRunnerApi>();
                _api.hideFlags = HideFlags.HideAndDontSave;
                _api.RegisterCallbacks(new TestCallbacks());

                // Check if recovering from domain reload during an active test run
                if (IsTestRunActive())
                {
                    McpLog.Info("[TestRunnerNoThrottle] Recovered from domain reload - reapplying No Throttling.");
                    ApplyNoThrottling();
                }
            }
            catch (Exception e)
            {
                McpLog.Warn($"[TestRunnerNoThrottle] Failed to register callbacks: {e}");
            }
        }

        #region State Persistence

        private static bool IsTestRunActive() => SessionState.GetBool(SessionKey_TestRunActive, false);
        private static void SetTestRunActive(bool active) => SessionState.SetBool(SessionKey_TestRunActive, active);
        private static bool AreSettingsCaptured() => SessionState.GetBool(SessionKey_SettingsCaptured, false);
        private static void SetSettingsCaptured(bool captured) => SessionState.SetBool(SessionKey_SettingsCaptured, captured);
        private static int GetPrevIdleTime() => SessionState.GetInt(SessionKey_PrevIdleTime, 4);
        private static void SetPrevIdleTime(int value) => SessionState.SetInt(SessionKey_PrevIdleTime, value);
        private static int GetPrevInteractionMode() => SessionState.GetInt(SessionKey_PrevInteractionMode, 0);
        private static void SetPrevInteractionMode(int value) => SessionState.SetInt(SessionKey_PrevInteractionMode, value);

        #endregion

        /// <summary>
        /// Apply no-throttling preemptively before tests start.
        /// Call this before Execute() for PlayMode tests to ensure Unity isn't throttled
        /// during the Play mode transition (before RunStarted fires).
        /// </summary>
        public static void ApplyNoThrottlingPreemptive()
        {
            SetTestRunActive(true);
            ApplyNoThrottling();
        }

        private static void ApplyNoThrottling()
        {
            if (!AreSettingsCaptured())
            {
                SetPrevIdleTime(EditorPrefs.GetInt(ApplicationIdleTimeKey, 4));
                SetPrevInteractionMode(EditorPrefs.GetInt(InteractionModeKey, 0));
                SetSettingsCaptured(true);
            }

            // 0ms idle + InteractionMode=1 (No Throttling)
            EditorPrefs.SetInt(ApplicationIdleTimeKey, 0);
            EditorPrefs.SetInt(InteractionModeKey, 1);

            ForceEditorToApplyInteractionPrefs();
            McpLog.Info("[TestRunnerNoThrottle] Applied No Throttling for test run.");
        }

        private static void RestoreThrottling()
        {
            if (!AreSettingsCaptured()) return;

            EditorPrefs.SetInt(ApplicationIdleTimeKey, GetPrevIdleTime());
            EditorPrefs.SetInt(InteractionModeKey, GetPrevInteractionMode());
            ForceEditorToApplyInteractionPrefs();

            SetSettingsCaptured(false);
            SetTestRunActive(false);
            McpLog.Info("[TestRunnerNoThrottle] Restored Interaction Mode after test run.");
        }

        private static void ForceEditorToApplyInteractionPrefs()
        {
            try
            {
                var method = typeof(EditorApplication).GetMethod(
                    "UpdateInteractionModeSettings",
                    BindingFlags.Static | BindingFlags.NonPublic
                );
                method?.Invoke(null, null);
            }
            catch
            {
                // Ignore reflection errors
            }
        }

        private sealed class TestCallbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun)
            {
                SetTestRunActive(true);
                ApplyNoThrottling();
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                RestoreThrottling();
            }

            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }
        }
    }
}
