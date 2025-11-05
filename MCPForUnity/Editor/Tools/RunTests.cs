using System;
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
    [McpForUnityTool("run_tests")]
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
                return Response.Error(parseError);
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

            var testService = MCPServiceLocator.Tests;
            Task<TestRunResult> runTask;
            try
            {
                runTask = testService.RunTestsAsync(parsedMode.Value);
            }
            catch (Exception ex)
            {
                return Response.Error($"Failed to start test run: {ex.Message}");
            }

            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds));
            var completed = await Task.WhenAny(runTask, timeoutTask).ConfigureAwait(true);

            if (completed != runTask)
            {
                return Response.Error($"Test run timed out after {timeoutSeconds} seconds");
            }

            var result = await runTask.ConfigureAwait(true);

            string message =
                $"{parsedMode.Value} tests completed: {result.Passed}/{result.Total} passed, {result.Failed} failed, {result.Skipped} skipped";

            var data = result.ToSerializable(parsedMode.Value.ToString());
            return Response.Success(message, data);
        }
    }
}
