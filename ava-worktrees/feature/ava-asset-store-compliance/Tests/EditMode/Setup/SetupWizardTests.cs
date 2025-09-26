using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using MCPForUnity.Editor.Setup;
using MCPForUnity.Editor.Dependencies.Models;
using MCPForUnity.Tests.Mocks;

namespace MCPForUnity.Tests.Setup
{
    [TestFixture]
    public class SetupWizardTests
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
        public void GetSetupState_ReturnsValidState()
        {
            // Act
            var state = SetupWizard.GetSetupState();
            
            // Assert
            Assert.IsNotNull(state, "Setup state should not be null");
            Assert.IsFalse(state.HasCompletedSetup, "Fresh state should not be completed");
            Assert.IsFalse(state.HasDismissedSetup, "Fresh state should not be dismissed");
        }

        [Test]
        public void SaveSetupState_PersistsState()
        {
            // Arrange
            var state = SetupWizard.GetSetupState();
            state.HasCompletedSetup = true;
            state.SetupVersion = "1.0.0";
            
            // Act
            SetupWizard.SaveSetupState();
            
            // Verify persistence by creating new instance
            EditorPrefs.DeleteKey(SETUP_STATE_KEY); // Clear cached state
            var loadedState = SetupWizard.GetSetupState();
            
            // Assert
            Assert.IsTrue(loadedState.HasCompletedSetup, "State should be persisted");
            Assert.AreEqual("1.0.0", loadedState.SetupVersion, "Version should be persisted");
        }

        [Test]
        public void MarkSetupCompleted_UpdatesState()
        {
            // Act
            SetupWizard.MarkSetupCompleted();
            
            // Assert
            var state = SetupWizard.GetSetupState();
            Assert.IsTrue(state.HasCompletedSetup, "Setup should be marked as completed");
            Assert.IsNotNull(state.SetupVersion, "Setup version should be set");
        }

        [Test]
        public void MarkSetupDismissed_UpdatesState()
        {
            // Act
            SetupWizard.MarkSetupDismissed();
            
            // Assert
            var state = SetupWizard.GetSetupState();
            Assert.IsTrue(state.HasDismissedSetup, "Setup should be marked as dismissed");
        }

        [Test]
        public void ResetSetupState_ClearsState()
        {
            // Arrange
            SetupWizard.MarkSetupCompleted();
            SetupWizard.MarkSetupDismissed();
            
            // Act
            SetupWizard.ResetSetupState();
            
            // Assert
            var state = SetupWizard.GetSetupState();
            Assert.IsFalse(state.HasCompletedSetup, "Setup completion should be reset");
            Assert.IsFalse(state.HasDismissedSetup, "Setup dismissal should be reset");
        }

        [Test]
        public void ShowSetupWizard_WithNullDependencyResult_ChecksDependencies()
        {
            // This test verifies that ShowSetupWizard handles null dependency results
            // by checking dependencies itself
            
            // Act & Assert (should not throw)
            Assert.DoesNotThrow(() => SetupWizard.ShowSetupWizard(null), 
                "ShowSetupWizard should handle null dependency result gracefully");
        }

        [Test]
        public void ShowSetupWizard_WithDependencyResult_RecordsAttempt()
        {
            // Arrange
            var dependencyResult = new DependencyCheckResult();
            dependencyResult.Dependencies.Add(new DependencyStatus 
            { 
                Name = "Python", 
                IsRequired = true, 
                IsAvailable = false 
            });
            dependencyResult.GenerateSummary();
            
            var initialAttempts = SetupWizard.GetSetupState().SetupAttempts;
            
            // Act
            SetupWizard.ShowSetupWizard(dependencyResult);
            
            // Assert
            var state = SetupWizard.GetSetupState();
            Assert.AreEqual(initialAttempts + 1, state.SetupAttempts, 
                "Setup attempts should be incremented");
        }

        [Test]
        public void SetupState_LoadingCorruptedData_CreatesDefaultState()
        {
            // Arrange - Set corrupted JSON data
            EditorPrefs.SetString(SETUP_STATE_KEY, "{ invalid json }");
            
            // Act
            var state = SetupWizard.GetSetupState();
            
            // Assert
            Assert.IsNotNull(state, "Should create default state when loading corrupted data");
            Assert.IsFalse(state.HasCompletedSetup, "Default state should not be completed");
        }

        [Test]
        public void SetupState_ShouldShowSetup_Logic()
        {
            // Test various scenarios for when setup should be shown
            var state = SetupWizard.GetSetupState();
            
            // Scenario 1: Fresh install
            Assert.IsTrue(state.ShouldShowSetup("1.0.0"), 
                "Should show setup on fresh install");
            
            // Scenario 2: After completion
            state.MarkSetupCompleted("1.0.0");
            Assert.IsFalse(state.ShouldShowSetup("1.0.0"), 
                "Should not show setup after completion for same version");
            
            // Scenario 3: Version upgrade
            Assert.IsTrue(state.ShouldShowSetup("2.0.0"), 
                "Should show setup after version upgrade");
            
            // Scenario 4: After dismissal
            state.MarkSetupDismissed();
            Assert.IsFalse(state.ShouldShowSetup("3.0.0"), 
                "Should not show setup after dismissal, even for new version");
        }

        [Test]
        public void SetupWizard_MenuItems_Exist()
        {
            // This test verifies that the menu items are properly registered
            // We can't easily test the actual menu functionality, but we can verify
            // the methods exist and are callable
            
            Assert.DoesNotThrow(() => SetupWizard.ShowSetupWizardManual(), 
                "Manual setup wizard menu item should be callable");
            
            Assert.DoesNotThrow(() => SetupWizard.ResetAndShowSetup(), 
                "Reset and show setup menu item should be callable");
            
            Assert.DoesNotThrow(() => SetupWizard.CheckDependencies(), 
                "Check dependencies menu item should be callable");
        }

        [Test]
        public void SetupWizard_BatchMode_Handling()
        {
            // Test that setup wizard respects batch mode settings
            // This is important for CI/CD environments
            
            var originalBatchMode = Application.isBatchMode;
            
            try
            {
                // We can't actually change batch mode in tests, but we can verify
                // the setup wizard handles the current mode gracefully
                Assert.DoesNotThrow(() => SetupWizard.GetSetupState(), 
                    "Setup wizard should handle batch mode gracefully");
            }
            finally
            {
                // Restore original state (though we can't actually change it)
            }
        }

        [Test]
        public void SetupWizard_ErrorHandling_InSaveLoad()
        {
            // Test error handling in save/load operations
            
            // This test verifies that the setup wizard handles errors gracefully
            // when saving or loading state
            
            Assert.DoesNotThrow(() => SetupWizard.SaveSetupState(), 
                "Save setup state should handle errors gracefully");
            
            Assert.DoesNotThrow(() => SetupWizard.GetSetupState(), 
                "Get setup state should handle errors gracefully");
        }

        [Test]
        public void SetupWizard_StateTransitions()
        {
            // Test various state transitions
            var state = SetupWizard.GetSetupState();
            
            // Initial state
            Assert.IsFalse(state.HasCompletedSetup);
            Assert.IsFalse(state.HasDismissedSetup);
            Assert.AreEqual(0, state.SetupAttempts);
            
            // Record attempt
            state.RecordSetupAttempt("Test error");
            Assert.AreEqual(1, state.SetupAttempts);
            Assert.AreEqual("Test error", state.LastSetupError);
            
            // Complete setup
            SetupWizard.MarkSetupCompleted();
            state = SetupWizard.GetSetupState();
            Assert.IsTrue(state.HasCompletedSetup);
            Assert.IsNull(state.LastSetupError);
            
            // Reset
            SetupWizard.ResetSetupState();
            state = SetupWizard.GetSetupState();
            Assert.IsFalse(state.HasCompletedSetup);
            Assert.AreEqual(0, state.SetupAttempts);
        }
    }
}