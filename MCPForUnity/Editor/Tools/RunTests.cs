using System;
using System.Linq;
using System.Threading.Tasks;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Resources.Tests;
using MCPForUnity.Editor.Services;
using Newtonsoft.Json.Linq;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Executes Unity tests for a specified mode and returns detailed results.
    /// </summary>
    [McpForUnityTool("run_tests", AutoRegister = false)]
    public static class RunTests
    {
        private const int DefaultTimeoutSeconds = 600; // 10 minutes

        public static async Task<object> HandleCommand(JObject @params)
        {
            string modeStr = @params?["mode"]?.ToString();
            if (string.IsNullOrWhiteSpace(modeStr))
            {
                modeStr = "EditMode";
            }

            if (!ModeParser.TryParse(modeStr, out var parsedMode, out var parseError))
            {
                return new ErrorResponse(parseError);
            }

            int timeoutSeconds = DefaultTimeoutSeconds;
            try
            {
                var timeoutToken = @params?["timeoutSeconds"];
                if (timeoutToken != null && int.TryParse(timeoutToken.ToString(), out var parsedTimeout) && parsedTimeout > 0)
                {
                    timeoutSeconds = parsedTimeout;
                }
            }
            catch
            {
                // Preserve default timeout if parsing fails
            }

            var filterOptions = ParseFilterOptions(@params);

            var testService = MCPServiceLocator.Tests;
            Task<TestRunResult> runTask;
            try
            {
                runTask = testService.RunTestsAsync(parsedMode.Value, filterOptions);
            }
            catch (Exception ex)
            {
                return new ErrorResponse($"Failed to start test run: {ex.Message}");
            }

            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds));
            var completed = await Task.WhenAny(runTask, timeoutTask).ConfigureAwait(true);

            if (completed != runTask)
            {
                return new ErrorResponse($"Test run timed out after {timeoutSeconds} seconds");
            }

            var result = await runTask.ConfigureAwait(true);

            string message =
                $"{parsedMode.Value} tests completed: {result.Passed}/{result.Total} passed, {result.Failed} failed, {result.Skipped} skipped";

            var data = result.ToSerializable(parsedMode.Value.ToString());
            return new SuccessResponse(message, data);
        }

        private static TestFilterOptions ParseFilterOptions(JObject @params)
        {
            if (@params == null)
            {
                return null;
            }

            var testNames = ParseStringArray(@params, "testNames");
            var groupNames = ParseStringArray(@params, "groupNames");
            var categoryNames = ParseStringArray(@params, "categoryNames");
            var assemblyNames = ParseStringArray(@params, "assemblyNames");

            // Return null if no filters specified
            if (testNames == null && groupNames == null && categoryNames == null && assemblyNames == null)
            {
                return null;
            }

            return new TestFilterOptions
            {
                TestNames = testNames,
                GroupNames = groupNames,
                CategoryNames = categoryNames,
                AssemblyNames = assemblyNames
            };
        }

        private static string[] ParseStringArray(JObject @params, string key)
        {
            var token = @params[key];
            if (token == null)
            {
                return null;
            }

            if (token.Type == JTokenType.String)
            {
                var value = token.ToString();
                return string.IsNullOrWhiteSpace(value) ? null : new[] { value };
            }

            if (token.Type == JTokenType.Array)
            {
                var array = token as JArray;
                if (array == null || array.Count == 0)
                {
                    return null;
                }

                var values = array
                    .Where(t => t.Type == JTokenType.String)
                    .Select(t => t.ToString())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToArray();

                return values.Length > 0 ? values : null;
            }

            return null;
        }
    }
}
