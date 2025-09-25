using MCPForUnity.Editor.Dependencies;
using MCPForUnity.Editor.Dependencies.Models;
using UnityEngine;

namespace MCPForUnity.Editor.Dependencies
{
    /// <summary>
    /// Simple test class for dependency management functionality
    /// This can be expanded into proper unit tests later
    /// </summary>
    public static class DependencyManagerTests
    {
        /// <summary>
        /// Test basic dependency detection functionality
        /// </summary>
        [UnityEditor.MenuItem("Window/MCP for Unity/Run Dependency Tests", priority = 100)]
        public static void RunBasicTests()
        {
            Debug.Log("<b><color=#2EA3FF>MCP-FOR-UNITY</color></b>: Running Dependency Manager Tests...");
            
            try
            {
                // Test 1: Platform detector availability
                var detector = DependencyManager.GetCurrentPlatformDetector();
                Debug.Log($"✓ Platform detector found: {detector.PlatformName}");
                
                // Test 2: Dependency check
                var result = DependencyManager.CheckAllDependencies();
                Debug.Log($"✓ Dependency check completed. System ready: {result.IsSystemReady}");
                
                // Test 3: Individual dependency checks
                bool pythonAvailable = DependencyManager.IsDependencyAvailable("python");
                bool uvAvailable = DependencyManager.IsDependencyAvailable("uv");
                bool serverAvailable = DependencyManager.IsDependencyAvailable("mcpserver");
                
                Debug.Log($"✓ Python available: {pythonAvailable}");
                Debug.Log($"✓ UV available: {uvAvailable}");
                Debug.Log($"✓ MCP Server available: {serverAvailable}");
                
                // Test 4: Installation recommendations
                var recommendations = DependencyManager.GetInstallationRecommendations();
                Debug.Log($"✓ Installation recommendations generated ({recommendations.Length} characters)");
                
                // Test 5: Setup state management
                var setupState = Setup.SetupWizard.GetSetupState();
                Debug.Log($"✓ Setup state loaded. Completed: {setupState.HasCompletedSetup}");
                
                // Test 6: Diagnostics
                var diagnostics = DependencyManager.GetDependencyDiagnostics();
                Debug.Log($"✓ Diagnostics generated ({diagnostics.Length} characters)");
                
                Debug.Log("<b><color=#2EA3FF>MCP-FOR-UNITY</color></b>: All tests completed successfully!");
                
                // Show detailed results
                Debug.Log($"<b>Detailed Dependency Status:</b>\n{diagnostics}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"<b><color=#FF6B6B>MCP-FOR-UNITY</color></b>: Test failed: {ex.Message}\n{ex.StackTrace}");
            }
        }
        
        /// <summary>
        /// Test setup wizard functionality
        /// </summary>
        [UnityEditor.MenuItem("Window/MCP for Unity/Test Setup Wizard", priority = 101)]
        public static void TestSetupWizard()
        {
            Debug.Log("<b><color=#2EA3FF>MCP-FOR-UNITY</color></b>: Testing Setup Wizard...");
            
            try
            {
                // Force show setup wizard for testing
                Setup.SetupWizard.ShowSetupWizard();
                Debug.Log("✓ Setup wizard opened successfully");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"<b><color=#FF6B6B>MCP-FOR-UNITY</color></b>: Setup wizard test failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Reset setup state for testing
        /// </summary>
        [UnityEditor.MenuItem("Window/MCP for Unity/Reset Setup State (Test)", priority = 102)]
        public static void ResetSetupStateForTesting()
        {
            Debug.Log("<b><color=#2EA3FF>MCP-FOR-UNITY</color></b>: Resetting setup state for testing...");
            
            try
            {
                Setup.SetupWizard.ResetSetupState();
                Debug.Log("✓ Setup state reset successfully");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"<b><color=#FF6B6B>MCP-FOR-UNITY</color></b>: Setup state reset failed: {ex.Message}");
            }
        }
    }
}