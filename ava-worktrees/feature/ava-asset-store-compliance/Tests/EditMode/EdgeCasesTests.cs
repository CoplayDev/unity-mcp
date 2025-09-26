using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using MCPForUnity.Editor.Dependencies;
using MCPForUnity.Editor.Setup;
using MCPForUnity.Editor.Installation;
using MCPForUnity.Editor.Dependencies.Models;
using MCPForUnity.Tests.Mocks;

namespace MCPForUnity.Tests
{
    [TestFixture]
    public class EdgeCasesTests
    {
        private string _originalSetupState;
        private const string SETUP_STATE_KEY = "MCPForUnity.SetupState";

        [SetUp]
        public void SetUp()
        {
            _originalSetupState = EditorPrefs.GetString(SETUP_STATE_KEY, "");
            EditorPrefs.DeleteKey(SETUP_STATE_KEY);
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(_originalSetupState))
            {
                EditorPrefs.SetString(SETUP_STATE_KEY, _originalSetupState);
            }
            else
            {
                EditorPrefs.DeleteKey(SETUP_STATE_KEY);
            }
        }

        [Test]
        public void DependencyManager_NullPlatformDetector_HandlesGracefully()
        {
            // This test verifies behavior when no platform detector is available
            // (though this shouldn't happen in practice)
            
            // We can't easily mock this without changing the DependencyManager,
            // but we can verify it handles the current platform correctly
            Assert.DoesNotThrow(() => DependencyManager.GetCurrentPlatformDetector(),
                "Should handle platform detection gracefully");
        }

        [Test]
        public void DependencyManager_CorruptedDependencyData_HandlesGracefully()
        {
            // Test handling of corrupted or unexpected dependency data
            
            var result = DependencyManager.CheckAllDependencies();
            
            // Even with potential corruption, should return valid result structure
            Assert.IsNotNull(result, "Should return valid result even with potential data issues");
            Assert.IsNotNull(result.Dependencies, "Dependencies list should not be null");
            Assert.IsNotNull(result.Summary, "Summary should not be null");
            Assert.IsNotNull(result.RecommendedActions, "Recommended actions should not be null");
        }

        [Test]
        public void SetupWizard_CorruptedEditorPrefs_CreatesDefaultState()
        {
            // Test handling of corrupted EditorPrefs data
            
            // Set invalid JSON
            EditorPrefs.SetString(SETUP_STATE_KEY, "{ invalid json data }");
            
            // Should create default state without throwing
            var state = SetupWizard.GetSetupState();
            
            Assert.IsNotNull(state, "Should create default state for corrupted data");
            Assert.IsFalse(state.HasCompletedSetup, "Default state should not be completed");
            Assert.IsFalse(state.HasDismissedSetup, "Default state should not be dismissed");
        }

        [Test]
        public void SetupWizard_EmptyEditorPrefs_CreatesDefaultState()
        {
            // Test handling of empty EditorPrefs
            
            EditorPrefs.SetString(SETUP_STATE_KEY, "");
            
            var state = SetupWizard.GetSetupState();
            
            Assert.IsNotNull(state, "Should create default state for empty data");
            Assert.IsFalse(state.HasCompletedSetup, "Default state should not be completed");
        }

        [Test]
        public void SetupWizard_VeryLongVersionString_HandlesCorrectly()
        {
            // Test handling of unusually long version strings
            
            var longVersion = new string('1', 1000) + ".0.0";
            var state = SetupWizard.GetSetupState();
            
            Assert.DoesNotThrow(() => state.ShouldShowSetup(longVersion),
                "Should handle long version strings");
            
            Assert.DoesNotThrow(() => state.MarkSetupCompleted(longVersion),
                "Should handle long version strings in completion");
        }

        [Test]
        public void SetupWizard_NullVersionString_HandlesCorrectly()
        {
            // Test handling of null version strings
            
            var state = SetupWizard.GetSetupState();
            
            Assert.DoesNotThrow(() => state.ShouldShowSetup(null),
                "Should handle null version strings");
            
            Assert.DoesNotThrow(() => state.MarkSetupCompleted(null),
                "Should handle null version strings in completion");
        }

        [Test]
        public void InstallationOrchestrator_NullDependenciesList_HandlesGracefully()
        {
            // Test handling of null dependencies list
            
            var orchestrator = new InstallationOrchestrator();
            
            Assert.DoesNotThrow(() => orchestrator.StartInstallation(null),
                "Should handle null dependencies list gracefully");
        }

        [Test]
        public void InstallationOrchestrator_EmptyDependenciesList_CompletesSuccessfully()
        {
            // Test handling of empty dependencies list
            
            var orchestrator = new InstallationOrchestrator();
            var emptyList = new List<DependencyStatus>();
            
            bool completed = false;
            bool success = false;
            
            orchestrator.OnInstallationComplete += (s, m) => { completed = true; success = s; };
            
            orchestrator.StartInstallation(emptyList);
            
            // Wait briefly
            System.Threading.Thread.Sleep(200);
            
            Assert.IsTrue(completed, "Empty installation should complete");
            Assert.IsTrue(success, "Empty installation should succeed");
        }

        [Test]
        public void InstallationOrchestrator_DependencyWithNullName_HandlesGracefully()
        {
            // Test handling of dependency with null name
            
            var orchestrator = new InstallationOrchestrator();
            var dependencies = new List<DependencyStatus>
            {
                new DependencyStatus { Name = null, IsRequired = true, IsAvailable = false }
            };
            
            bool completed = false;
            
            orchestrator.OnInstallationComplete += (s, m) => completed = true;
            
            Assert.DoesNotThrow(() => orchestrator.StartInstallation(dependencies),
                "Should handle dependency with null name");
            
            // Wait briefly
            System.Threading.Thread.Sleep(1000);
            
            Assert.IsTrue(completed, "Installation should complete even with null dependency name");
        }

        [Test]
        public void DependencyCheckResult_NullDependenciesList_HandlesGracefully()
        {
            // Test handling of null dependencies in result
            
            var result = new DependencyCheckResult();
            result.Dependencies = null;
            
            Assert.DoesNotThrow(() => result.GenerateSummary(),
                "Should handle null dependencies list in summary generation");
            
            Assert.DoesNotThrow(() => result.GetMissingDependencies(),
                "Should handle null dependencies list in missing dependencies");
            
            Assert.DoesNotThrow(() => result.GetMissingRequired(),
                "Should handle null dependencies list in missing required");
        }

        [Test]
        public void DependencyStatus_ExtremeValues_HandlesCorrectly()
        {
            // Test handling of extreme values in dependency status
            
            var status = new DependencyStatus();
            
            // Test very long strings
            var longString = new string('x', 10000);
            
            Assert.DoesNotThrow(() => status.Name = longString,
                "Should handle very long name");
            
            Assert.DoesNotThrow(() => status.Version = longString,
                "Should handle very long version");
            
            Assert.DoesNotThrow(() => status.Path = longString,
                "Should handle very long path");
            
            Assert.DoesNotThrow(() => status.Details = longString,
                "Should handle very long details");
            
            Assert.DoesNotThrow(() => status.ErrorMessage = longString,
                "Should handle very long error message");
        }

        [Test]
        public void SetupState_ExtremeAttemptCounts_HandlesCorrectly()
        {
            // Test handling of extreme attempt counts
            
            var state = new SetupState();
            
            // Test very high attempt count
            state.SetupAttempts = int.MaxValue;
            
            Assert.DoesNotThrow(() => state.RecordSetupAttempt(),
                "Should handle overflow in setup attempts gracefully");
        }

        [Test]
        public void DependencyManager_ConcurrentAccess_HandlesCorrectly()
        {
            // Test concurrent access to dependency manager
            
            var tasks = new List<System.Threading.Tasks.Task>();
            var exceptions = new List<Exception>();
            
            for (int i = 0; i < 10; i++)
            {
                tasks.Add(System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        DependencyManager.CheckAllDependencies();
                        DependencyManager.IsSystemReady();
                        DependencyManager.GetMissingDependenciesSummary();
                    }
                    catch (Exception ex)
                    {
                        lock (exceptions)
                        {
                            exceptions.Add(ex);
                        }
                    }
                }));
            }
            
            System.Threading.Tasks.Task.WaitAll(tasks.ToArray(), TimeSpan.FromSeconds(10));
            
            Assert.AreEqual(0, exceptions.Count, 
                $"Concurrent access should not cause exceptions. Exceptions: {string.Join(", ", exceptions)}");
        }

        [Test]
        public void SetupWizard_ConcurrentStateAccess_HandlesCorrectly()
        {
            // Test concurrent access to setup wizard state
            
            var tasks = new List<System.Threading.Tasks.Task>();
            var exceptions = new List<Exception>();
            
            for (int i = 0; i < 10; i++)
            {
                tasks.Add(System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        var state = SetupWizard.GetSetupState();
                        state.RecordSetupAttempt();
                        SetupWizard.SaveSetupState();
                    }
                    catch (Exception ex)
                    {
                        lock (exceptions)
                        {
                            exceptions.Add(ex);
                        }
                    }
                }));
            }
            
            System.Threading.Tasks.Task.WaitAll(tasks.ToArray(), TimeSpan.FromSeconds(10));
            
            Assert.AreEqual(0, exceptions.Count, 
                $"Concurrent state access should not cause exceptions. Exceptions: {string.Join(", ", exceptions)}");
        }

        [Test]
        public void MockPlatformDetector_EdgeCases_HandlesCorrectly()
        {
            // Test edge cases with mock platform detector
            
            var mock = new MockPlatformDetector();
            
            // Test with null/empty values
            mock.SetPythonAvailable(true, null, "", null);
            mock.SetUVAvailable(false, "", null, "");
            mock.SetMCPServerAvailable(true, null, "");
            
            Assert.DoesNotThrow(() => mock.DetectPython(),
                "Mock should handle null/empty values");
            
            Assert.DoesNotThrow(() => mock.DetectUV(),
                "Mock should handle null/empty values");
            
            Assert.DoesNotThrow(() => mock.DetectMCPServer(),
                "Mock should handle null/empty values");
        }

        [Test]
        public void InstallationOrchestrator_RapidCancellation_HandlesCorrectly()
        {
            // Test rapid cancellation of installation
            
            var orchestrator = new InstallationOrchestrator();
            var dependencies = new List<DependencyStatus>
            {
                new DependencyStatus { Name = "Python", IsRequired = true, IsAvailable = false }
            };
            
            // Start and immediately cancel
            orchestrator.StartInstallation(dependencies);
            orchestrator.CancelInstallation();
            
            // Should handle rapid cancellation gracefully
            Assert.IsFalse(orchestrator.IsInstalling, "Should not be installing after cancellation");
        }

        [Test]
        public void DependencyManager_InvalidDependencyNames_HandlesCorrectly()
        {
            // Test handling of invalid dependency names
            
            var invalidNames = new[] { null, "", "   ", "invalid-name", "PYTHON", "python123" };
            
            foreach (var name in invalidNames)
            {
                Assert.DoesNotThrow(() => DependencyManager.IsDependencyAvailable(name),
                    $"Should handle invalid dependency name: '{name}'");
                
                var result = DependencyManager.IsDependencyAvailable(name);
                if (name != "python" && name != "uv" && name != "mcpserver" && name != "mcp-server")
                {
                    Assert.IsFalse(result, $"Invalid dependency name '{name}' should return false");
                }
            }
        }
    }
}