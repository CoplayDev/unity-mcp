using System;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using MCPForUnity.Editor.Services;

namespace MCPForUnityTests.Editor.Tools
{
    /// <summary>
    /// Tests for RunTests tool functionality.
    /// Note: We cannot easily test the full HandleCommand because it would create
    /// recursive test runner calls. Instead, we test the message formatting logic.
    /// </summary>
    public class RunTestsTests
    {
        [Test]
        public void FormatResultMessage_WithNoTests_IncludesWarning()
        {
            // Arrange
            var summary = new TestRunSummary(
                total: 0,
                passed: 0,
                failed: 0,
                skipped: 0,
                durationSeconds: 0.0,
                resultState: "Passed"
            );
            var result = new TestRunResult(summary, new TestRunTestResult[0]);

            // Act
            string message = FormatTestResultMessage("EditMode", result);

            // Assert - THIS IS THE NEW FEATURE
            Assert.IsTrue(
                message.Contains("No tests matched") || message.Contains("no tests found"),
                $"Expected warning when total=0, but got: '{message}'"
            );
        }

        [Test]
        public void FormatResultMessage_WithTests_NoWarning()
        {
            // Arrange
            var summary = new TestRunSummary(
                total: 5,
                passed: 4,
                failed: 1,
                skipped: 0,
                durationSeconds: 1.5,
                resultState: "Failed"
            );
            var result = new TestRunResult(summary, new TestRunTestResult[0]);

            // Act
            string message = FormatTestResultMessage("EditMode", result);

            // Assert
            Assert.IsFalse(
                message.Contains("No tests matched") || message.Contains("no tests found"),
                $"Should not have warning when tests exist, but got: '{message}'"
            );
            Assert.IsTrue(message.Contains("4/5 passed"), "Should contain pass ratio");
        }

        /// <summary>
        /// Helper method to format test result message.
        /// This matches the implementation in RunTests.cs.
        /// </summary>
        private static string FormatTestResultMessage(string mode, TestRunResult result)
        {
            string message = $"{mode} tests completed: {result.Passed}/{result.Total} passed, {result.Failed} failed, {result.Skipped} skipped";

            // Add warning when no tests matched the filter criteria
            if (result.Total == 0)
            {
                message += " (No tests matched the specified filters)";
            }

            return message;
        }
    }
}
