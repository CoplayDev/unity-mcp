using System;
using System.Linq;
using NUnit.Framework;
using MCPForUnity.Editor.Dependencies.Models;

namespace MCPForUnity.Tests.Dependencies
{
    [TestFixture]
    public class DependencyModelsTests
    {
        [Test]
        public void DependencyStatus_DefaultConstructor_SetsCorrectDefaults()
        {
            // Act
            var status = new DependencyStatus();
            
            // Assert
            Assert.IsNull(status.Name, "Name should be null by default");
            Assert.IsFalse(status.IsAvailable, "IsAvailable should be false by default");
            Assert.IsFalse(status.IsRequired, "IsRequired should be false by default");
            Assert.IsNull(status.Version, "Version should be null by default");
            Assert.IsNull(status.Path, "Path should be null by default");
            Assert.IsNull(status.Details, "Details should be null by default");
            Assert.IsNull(status.ErrorMessage, "ErrorMessage should be null by default");
        }

        [Test]
        public void DependencyStatus_ParameterizedConstructor_SetsCorrectValues()
        {
            // Arrange
            var name = "Test Dependency";
            var isAvailable = true;
            var isRequired = true;
            var version = "1.0.0";
            var path = "/test/path";
            var details = "Test details";
            
            // Act
            var status = new DependencyStatus
            {
                Name = name,
                IsAvailable = isAvailable,
                IsRequired = isRequired,
                Version = version,
                Path = path,
                Details = details
            };
            
            // Assert
            Assert.AreEqual(name, status.Name, "Name should be set correctly");
            Assert.AreEqual(isAvailable, status.IsAvailable, "IsAvailable should be set correctly");
            Assert.AreEqual(isRequired, status.IsRequired, "IsRequired should be set correctly");
            Assert.AreEqual(version, status.Version, "Version should be set correctly");
            Assert.AreEqual(path, status.Path, "Path should be set correctly");
            Assert.AreEqual(details, status.Details, "Details should be set correctly");
        }

        [Test]
        public void DependencyCheckResult_DefaultConstructor_InitializesCollections()
        {
            // Act
            var result = new DependencyCheckResult();
            
            // Assert
            Assert.IsNotNull(result.Dependencies, "Dependencies should be initialized");
            Assert.IsNotNull(result.RecommendedActions, "RecommendedActions should be initialized");
            Assert.AreEqual(0, result.Dependencies.Count, "Dependencies should be empty initially");
            Assert.AreEqual(0, result.RecommendedActions.Count, "RecommendedActions should be empty initially");
            Assert.IsFalse(result.IsSystemReady, "IsSystemReady should be false by default");
            Assert.IsTrue(result.CheckedAt <= DateTime.UtcNow, "CheckedAt should be set to current time or earlier");
        }

        [Test]
        public void DependencyCheckResult_AllRequiredAvailable_ReturnsCorrectValue()
        {
            // Arrange
            var result = new DependencyCheckResult();
            result.Dependencies.Add(new DependencyStatus { Name = "Required1", IsRequired = true, IsAvailable = true });
            result.Dependencies.Add(new DependencyStatus { Name = "Required2", IsRequired = true, IsAvailable = true });
            result.Dependencies.Add(new DependencyStatus { Name = "Optional1", IsRequired = false, IsAvailable = false });
            
            // Act & Assert
            Assert.IsTrue(result.AllRequiredAvailable, "AllRequiredAvailable should be true when all required dependencies are available");
        }

        [Test]
        public void DependencyCheckResult_AllRequiredAvailable_ReturnsFalse_WhenRequiredMissing()
        {
            // Arrange
            var result = new DependencyCheckResult();
            result.Dependencies.Add(new DependencyStatus { Name = "Required1", IsRequired = true, IsAvailable = true });
            result.Dependencies.Add(new DependencyStatus { Name = "Required2", IsRequired = true, IsAvailable = false });
            
            // Act & Assert
            Assert.IsFalse(result.AllRequiredAvailable, "AllRequiredAvailable should be false when required dependencies are missing");
        }

        [Test]
        public void DependencyCheckResult_HasMissingOptional_ReturnsCorrectValue()
        {
            // Arrange
            var result = new DependencyCheckResult();
            result.Dependencies.Add(new DependencyStatus { Name = "Required1", IsRequired = true, IsAvailable = true });
            result.Dependencies.Add(new DependencyStatus { Name = "Optional1", IsRequired = false, IsAvailable = false });
            
            // Act & Assert
            Assert.IsTrue(result.HasMissingOptional, "HasMissingOptional should be true when optional dependencies are missing");
        }

        [Test]
        public void DependencyCheckResult_GetMissingDependencies_ReturnsCorrectList()
        {
            // Arrange
            var result = new DependencyCheckResult();
            var available = new DependencyStatus { Name = "Available", IsAvailable = true };
            var missing1 = new DependencyStatus { Name = "Missing1", IsAvailable = false };
            var missing2 = new DependencyStatus { Name = "Missing2", IsAvailable = false };
            
            result.Dependencies.Add(available);
            result.Dependencies.Add(missing1);
            result.Dependencies.Add(missing2);
            
            // Act
            var missing = result.GetMissingDependencies();
            
            // Assert
            Assert.AreEqual(2, missing.Count, "Should return 2 missing dependencies");
            Assert.IsTrue(missing.Any(d => d.Name == "Missing1"), "Should include Missing1");
            Assert.IsTrue(missing.Any(d => d.Name == "Missing2"), "Should include Missing2");
            Assert.IsFalse(missing.Any(d => d.Name == "Available"), "Should not include available dependency");
        }

        [Test]
        public void DependencyCheckResult_GetMissingRequired_ReturnsCorrectList()
        {
            // Arrange
            var result = new DependencyCheckResult();
            var availableRequired = new DependencyStatus { Name = "AvailableRequired", IsRequired = true, IsAvailable = true };
            var missingRequired = new DependencyStatus { Name = "MissingRequired", IsRequired = true, IsAvailable = false };
            var missingOptional = new DependencyStatus { Name = "MissingOptional", IsRequired = false, IsAvailable = false };
            
            result.Dependencies.Add(availableRequired);
            result.Dependencies.Add(missingRequired);
            result.Dependencies.Add(missingOptional);
            
            // Act
            var missingRequired_result = result.GetMissingRequired();
            
            // Assert
            Assert.AreEqual(1, missingRequired_result.Count, "Should return 1 missing required dependency");
            Assert.AreEqual("MissingRequired", missingRequired_result[0].Name, "Should return the missing required dependency");
        }

        [Test]
        public void DependencyCheckResult_GenerateSummary_AllAvailable()
        {
            // Arrange
            var result = new DependencyCheckResult();
            result.Dependencies.Add(new DependencyStatus { Name = "Dep1", IsRequired = true, IsAvailable = true });
            result.Dependencies.Add(new DependencyStatus { Name = "Dep2", IsRequired = false, IsAvailable = true });
            
            // Act
            result.GenerateSummary();
            
            // Assert
            Assert.IsTrue(result.IsSystemReady, "System should be ready when all dependencies are available");
            Assert.IsTrue(result.Summary.Contains("All dependencies are available"), "Summary should indicate all dependencies are available");
        }

        [Test]
        public void DependencyCheckResult_GenerateSummary_MissingOptional()
        {
            // Arrange
            var result = new DependencyCheckResult();
            result.Dependencies.Add(new DependencyStatus { Name = "Required", IsRequired = true, IsAvailable = true });
            result.Dependencies.Add(new DependencyStatus { Name = "Optional", IsRequired = false, IsAvailable = false });
            
            // Act
            result.GenerateSummary();
            
            // Assert
            Assert.IsTrue(result.IsSystemReady, "System should be ready when only optional dependencies are missing");
            Assert.IsTrue(result.Summary.Contains("System is ready"), "Summary should indicate system is ready");
            Assert.IsTrue(result.Summary.Contains("optional"), "Summary should mention optional dependencies");
        }

        [Test]
        public void DependencyCheckResult_GenerateSummary_MissingRequired()
        {
            // Arrange
            var result = new DependencyCheckResult();
            result.Dependencies.Add(new DependencyStatus { Name = "Required1", IsRequired = true, IsAvailable = true });
            result.Dependencies.Add(new DependencyStatus { Name = "Required2", IsRequired = true, IsAvailable = false });
            
            // Act
            result.GenerateSummary();
            
            // Assert
            Assert.IsFalse(result.IsSystemReady, "System should not be ready when required dependencies are missing");
            Assert.IsTrue(result.Summary.Contains("System is not ready"), "Summary should indicate system is not ready");
            Assert.IsTrue(result.Summary.Contains("required"), "Summary should mention required dependencies");
        }

        [Test]
        public void SetupState_DefaultConstructor_SetsCorrectDefaults()
        {
            // Act
            var state = new SetupState();
            
            // Assert
            Assert.IsFalse(state.HasCompletedSetup, "HasCompletedSetup should be false by default");
            Assert.IsFalse(state.HasDismissedSetup, "HasDismissedSetup should be false by default");
            Assert.IsFalse(state.ShowSetupOnReload, "ShowSetupOnReload should be false by default");
            Assert.AreEqual("automatic", state.PreferredInstallMode, "PreferredInstallMode should be 'automatic' by default");
            Assert.AreEqual(0, state.SetupAttempts, "SetupAttempts should be 0 by default");
        }

        [Test]
        public void SetupState_ShouldShowSetup_ReturnsFalse_WhenDismissed()
        {
            // Arrange
            var state = new SetupState();
            state.HasDismissedSetup = true;
            
            // Act & Assert
            Assert.IsFalse(state.ShouldShowSetup("1.0.0"), "Should not show setup when dismissed");
        }

        [Test]
        public void SetupState_ShouldShowSetup_ReturnsTrue_WhenNotCompleted()
        {
            // Arrange
            var state = new SetupState();
            state.HasCompletedSetup = false;
            
            // Act & Assert
            Assert.IsTrue(state.ShouldShowSetup("1.0.0"), "Should show setup when not completed");
        }

        [Test]
        public void SetupState_ShouldShowSetup_ReturnsTrue_WhenVersionChanged()
        {
            // Arrange
            var state = new SetupState();
            state.HasCompletedSetup = true;
            state.SetupVersion = "1.0.0";
            
            // Act & Assert
            Assert.IsTrue(state.ShouldShowSetup("2.0.0"), "Should show setup when version changed");
        }

        [Test]
        public void SetupState_ShouldShowSetup_ReturnsFalse_WhenCompletedSameVersion()
        {
            // Arrange
            var state = new SetupState();
            state.HasCompletedSetup = true;
            state.SetupVersion = "1.0.0";
            
            // Act & Assert
            Assert.IsFalse(state.ShouldShowSetup("1.0.0"), "Should not show setup when completed for same version");
        }

        [Test]
        public void SetupState_MarkSetupCompleted_SetsCorrectValues()
        {
            // Arrange
            var state = new SetupState();
            var version = "1.0.0";
            
            // Act
            state.MarkSetupCompleted(version);
            
            // Assert
            Assert.IsTrue(state.HasCompletedSetup, "HasCompletedSetup should be true");
            Assert.AreEqual(version, state.SetupVersion, "SetupVersion should be set");
            Assert.IsFalse(state.ShowSetupOnReload, "ShowSetupOnReload should be false");
            Assert.IsNull(state.LastSetupError, "LastSetupError should be null");
        }

        [Test]
        public void SetupState_MarkSetupDismissed_SetsCorrectValues()
        {
            // Arrange
            var state = new SetupState();
            
            // Act
            state.MarkSetupDismissed();
            
            // Assert
            Assert.IsTrue(state.HasDismissedSetup, "HasDismissedSetup should be true");
            Assert.IsFalse(state.ShowSetupOnReload, "ShowSetupOnReload should be false");
        }

        [Test]
        public void SetupState_RecordSetupAttempt_IncrementsCounter()
        {
            // Arrange
            var state = new SetupState();
            var error = "Test error";
            
            // Act
            state.RecordSetupAttempt(error);
            
            // Assert
            Assert.AreEqual(1, state.SetupAttempts, "SetupAttempts should be incremented");
            Assert.AreEqual(error, state.LastSetupError, "LastSetupError should be set");
        }

        [Test]
        public void SetupState_Reset_ClearsAllValues()
        {
            // Arrange
            var state = new SetupState();
            state.HasCompletedSetup = true;
            state.HasDismissedSetup = true;
            state.ShowSetupOnReload = true;
            state.SetupAttempts = 5;
            state.LastSetupError = "Error";
            state.LastDependencyCheck = "2023-01-01";
            
            // Act
            state.Reset();
            
            // Assert
            Assert.IsFalse(state.HasCompletedSetup, "HasCompletedSetup should be reset");
            Assert.IsFalse(state.HasDismissedSetup, "HasDismissedSetup should be reset");
            Assert.IsFalse(state.ShowSetupOnReload, "ShowSetupOnReload should be reset");
            Assert.AreEqual(0, state.SetupAttempts, "SetupAttempts should be reset");
            Assert.IsNull(state.LastSetupError, "LastSetupError should be reset");
            Assert.IsNull(state.LastDependencyCheck, "LastDependencyCheck should be reset");
        }
    }
}