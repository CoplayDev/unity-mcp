using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using MCPForUnity.Editor.Dependencies;
using MCPForUnity.Editor.Setup;
using MCPForUnity.Editor.Installation;
using MCPForUnity.Editor.Dependencies.Models;

namespace MCPForUnity.Tests.Integration
{
    [TestFixture]
    public class AssetStoreComplianceIntegrationTests
    {
        private string _originalSetupState;
        private const string SETUP_STATE_KEY = "MCPForUnity.SetupState";

        [SetUp]
        public void SetUp()
        {
            // Save original setup state
            _originalSetupState = EditorPrefs.GetString(SETUP_STATE_KEY, "");
            
            // Clear setup state for testing
            EditorPrefs.DeleteKey(SETUP_STATE_KEY);
        }

        [TearDown]
        public void TearDown()
        {
            // Restore original setup state
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
        public void EndToEndWorkflow_FreshInstall_ShowsSetupWizard()
        {
            // This test simulates a fresh install scenario
            
            // Arrange - Fresh state
            var setupState = SetupWizard.GetSetupState();
            Assert.IsFalse(setupState.HasCompletedSetup, "Should start with fresh state");

            // Act - Check if setup should be shown
            var shouldShow = setupState.ShouldShowSetup("3.4.0");

            // Assert
            Assert.IsTrue(shouldShow, "Setup wizard should be shown on fresh install");
        }

        [Test]
        public void EndToEndWorkflow_DependencyCheck_Integration()
        {
            // This test verifies the integration between dependency checking and setup wizard
            
            // Act
            var dependencyResult = DependencyManager.CheckAllDependencies();
            
            // Assert
            Assert.IsNotNull(dependencyResult, "Dependency check should return result");
            Assert.IsNotNull(dependencyResult.Dependencies, "Should have dependencies list");
            Assert.GreaterOrEqual(dependencyResult.Dependencies.Count, 3, "Should check core dependencies");
            
            // Verify core dependencies are checked
            var dependencyNames = dependencyResult.Dependencies.Select(d => d.Name).ToList();
            Assert.Contains("Python", dependencyNames, "Should check Python");
            Assert.Contains("UV Package Manager", dependencyNames, "Should check UV");
            Assert.Contains("MCP Server", dependencyNames, "Should check MCP Server");
        }

        [Test]
        public void EndToEndWorkflow_SetupCompletion_PersistsState()
        {
            // This test verifies the complete setup workflow
            
            // Arrange
            var initialState = SetupWizard.GetSetupState();
            Assert.IsFalse(initialState.HasCompletedSetup, "Should start incomplete");

            // Act - Complete setup
            SetupWizard.MarkSetupCompleted();
            SetupWizard.SaveSetupState();

            // Simulate Unity restart by clearing cached state
            EditorPrefs.DeleteKey(SETUP_STATE_KEY);
            var newState = SetupWizard.GetSetupState();

            // Assert
            Assert.IsTrue(newState.HasCompletedSetup, "Setup completion should persist");
            Assert.IsFalse(newState.ShouldShowSetup("3.4.0"), "Should not show setup after completion");
        }

        [Test]
        public void AssetStoreCompliance_NoBundledDependencies()
        {
            // This test verifies Asset Store compliance by ensuring no bundled dependencies
            
            // Check that the installation orchestrator doesn't automatically install
            // Python or UV (Asset Store compliance requirement)
            
            var orchestrator = new InstallationOrchestrator();
            var dependencies = new List<DependencyStatus>
            {
                new DependencyStatus { Name = "Python", IsRequired = true, IsAvailable = false },
                new DependencyStatus { Name = "UV Package Manager", IsRequired = true, IsAvailable = false }
            };

            bool installationCompleted = false;
            bool installationSucceeded = false;
            string installationMessage = "";

            orchestrator.OnInstallationComplete += (success, message) =>
            {
                installationCompleted = true;
                installationSucceeded = success;
                installationMessage = message;
            };

            // Act
            orchestrator.StartInstallation(dependencies);

            // Wait for completion
            var timeout = DateTime.Now.AddSeconds(10);
            while (!installationCompleted && DateTime.Now < timeout)
            {
                System.Threading.Thread.Sleep(100);
            }

            // Assert
            Assert.IsTrue(installationCompleted, "Installation should complete");
            Assert.IsFalse(installationSucceeded, "Installation should fail (Asset Store compliance)");
            Assert.IsTrue(installationMessage.Contains("Failed"), "Should indicate failure");
        }

        [Test]
        public void AssetStoreCompliance_MCPServerInstallation_Allowed()
        {
            // This test verifies that MCP Server installation is allowed (not bundled, but auto-installable)
            
            var orchestrator = new InstallationOrchestrator();
            var dependencies = new List<DependencyStatus>
            {
                new DependencyStatus { Name = "MCP Server", IsRequired = false, IsAvailable = false }
            };

            bool installationCompleted = false;
            bool installationSucceeded = false;

            orchestrator.OnInstallationComplete += (success, message) =>
            {
                installationCompleted = true;
                installationSucceeded = success;
            };

            // Act
            orchestrator.StartInstallation(dependencies);

            // Wait for completion
            var timeout = DateTime.Now.AddSeconds(10);
            while (!installationCompleted && DateTime.Now < timeout)
            {
                System.Threading.Thread.Sleep(100);
            }

            // Assert
            Assert.IsTrue(installationCompleted, "Installation should complete");
            // Note: Success depends on whether ServerInstaller.EnsureServerInstalled() works
            // The important thing is that it attempts installation (doesn't fail due to compliance)
        }

        [Test]
        public void CrossPlatformCompatibility_PlatformDetection()
        {
            // This test verifies cross-platform compatibility
            
            // Act
            var detector = DependencyManager.GetCurrentPlatformDetector();
            
            // Assert
            Assert.IsNotNull(detector, "Should detect current platform");
            Assert.IsTrue(detector.CanDetect, "Detector should be able to detect on current platform");
            Assert.IsNotEmpty(detector.PlatformName, "Platform name should not be empty");
            
            // Verify platform-specific URLs are provided
            var pythonUrl = detector.GetPythonInstallUrl();
            var uvUrl = detector.GetUVInstallUrl();
            
            Assert.IsNotNull(pythonUrl, "Python install URL should be provided");
            Assert.IsNotNull(uvUrl, "UV install URL should be provided");
            Assert.IsTrue(pythonUrl.StartsWith("http"), "Python URL should be valid");
            Assert.IsTrue(uvUrl.StartsWith("http"), "UV URL should be valid");
        }

        [Test]
        public void UserExperience_SetupWizardFlow()
        {
            // This test verifies the user experience flow
            
            // Scenario 1: First time user
            var state = SetupWizard.GetSetupState();
            Assert.IsTrue(state.ShouldShowSetup("3.4.0"), "First time user should see setup");
            
            // Scenario 2: User attempts setup
            state.RecordSetupAttempt();
            Assert.AreEqual(1, state.SetupAttempts, "Setup attempt should be recorded");
            
            // Scenario 3: User completes setup
            SetupWizard.MarkSetupCompleted();
            state = SetupWizard.GetSetupState();
            Assert.IsTrue(state.HasCompletedSetup, "Setup should be marked complete");
            Assert.IsFalse(state.ShouldShowSetup("3.4.0"), "Should not show setup after completion");
            
            // Scenario 4: Package upgrade
            Assert.IsTrue(state.ShouldShowSetup("4.0.0"), "Should show setup after major version upgrade");
        }

        [Test]
        public void ErrorHandling_GracefulDegradation()
        {
            // This test verifies that the system handles errors gracefully
            
            // Test dependency manager error handling
            Assert.DoesNotThrow(() => DependencyManager.CheckAllDependencies(), 
                "Dependency check should not throw exceptions");
            
            Assert.DoesNotThrow(() => DependencyManager.IsSystemReady(), 
                "System ready check should not throw exceptions");
            
            Assert.DoesNotThrow(() => DependencyManager.GetMissingDependenciesSummary(), 
                "Missing dependencies summary should not throw exceptions");
            
            // Test setup wizard error handling
            Assert.DoesNotThrow(() => SetupWizard.GetSetupState(), 
                "Get setup state should not throw exceptions");
            
            Assert.DoesNotThrow(() => SetupWizard.SaveSetupState(), 
                "Save setup state should not throw exceptions");
        }

        [Test]
        public void MenuIntegration_MenuItemsAccessible()
        {
            // This test verifies that menu items are accessible and functional
            
            // Test that menu methods can be called without exceptions
            Assert.DoesNotThrow(() => SetupWizard.ShowSetupWizardManual(), 
                "Manual setup wizard should be callable");
            
            Assert.DoesNotThrow(() => SetupWizard.ResetAndShowSetup(), 
                "Reset and show setup should be callable");
            
            Assert.DoesNotThrow(() => SetupWizard.CheckDependencies(), 
                "Check dependencies should be callable");
        }

        [Test]
        public void PerformanceConsiderations_LazyLoading()
        {
            // This test verifies that the system uses lazy loading and doesn't impact Unity startup
            
            var startTime = DateTime.Now;
            
            // These operations should be fast (lazy loading)
            var detector = DependencyManager.GetCurrentPlatformDetector();
            var state = SetupWizard.GetSetupState();
            
            var elapsed = DateTime.Now - startTime;
            
            // Assert
            Assert.IsNotNull(detector, "Platform detector should be available");
            Assert.IsNotNull(state, "Setup state should be available");
            Assert.IsTrue(elapsed.TotalMilliseconds < 1000, "Operations should be fast (< 1 second)");
        }

        [Test]
        public void StateManagement_Persistence()
        {
            // This test verifies that state management works correctly across sessions
            
            // Set up initial state
            var state = SetupWizard.GetSetupState();
            state.HasCompletedSetup = true;
            state.SetupVersion = "3.4.0";
            state.SetupAttempts = 3;
            state.PreferredInstallMode = "manual";
            
            SetupWizard.SaveSetupState();
            
            // Simulate Unity restart by clearing cached state
            EditorPrefs.DeleteKey(SETUP_STATE_KEY);
            
            // Load state again
            var loadedState = SetupWizard.GetSetupState();
            
            // Assert
            Assert.IsTrue(loadedState.HasCompletedSetup, "Completion status should persist");
            Assert.AreEqual("3.4.0", loadedState.SetupVersion, "Version should persist");
            Assert.AreEqual(3, loadedState.SetupAttempts, "Attempts should persist");
            Assert.AreEqual("manual", loadedState.PreferredInstallMode, "Install mode should persist");
        }
    }
}