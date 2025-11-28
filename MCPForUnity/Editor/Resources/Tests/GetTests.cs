using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services;
using Newtonsoft.Json.Linq;
using UnityEditor.TestTools.TestRunner.Api;

namespace MCPForUnity.Editor.Resources.Tests
{
    /// <summary>
    /// Provides access to Unity tests from the Test Framework.
    /// This is a read-only resource that can be queried by MCP clients.
    /// </summary>
    [McpForUnityResource("get_tests")]
    public static class GetTests
    {
        public static async Task<object> HandleCommand(JObject @params)
        {
            McpLog.Info("[GetTests] Retrieving tests for all modes");
            IReadOnlyList<Dictionary<string, string>> result;

            try
            {
                result = await MCPServiceLocator.Tests.GetTestsAsync(mode: null).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                McpLog.Error($"[GetTests] Error retrieving tests: {ex.Message}\n{ex.StackTrace}");
                return new ErrorResponse("Failed to retrieve tests");
            }

            string message = $"Retrieved {result.Count} tests";

            return new SuccessResponse(message, result);
        }
    }

    /// <summary>
    /// Provides access to Unity tests for a specific mode (EditMode or PlayMode).
    /// This is a read-only resource that can be queried by MCP clients.
    /// </summary>
    [McpForUnityResource("get_tests_for_mode")]
    public static class GetTestsForMode
    {
        public static async Task<object> HandleCommand(JObject @params)
        {
            IReadOnlyList<Dictionary<string, string>> result;
            string modeStr = @params["mode"]?.ToString();
            if (string.IsNullOrEmpty(modeStr))
            {
                return new ErrorResponse("'mode' parameter is required");
            }

            if (!ModeParser.TryParse(modeStr, out var parsedMode, out var parseError))
            {
                return new ErrorResponse(parseError);
            }

            McpLog.Info($"[GetTestsForMode] Retrieving tests for mode: {parsedMode.Value}");

            try
            {
                result = await MCPServiceLocator.Tests.GetTestsAsync(parsedMode).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                McpLog.Error($"[GetTestsForMode] Error retrieving tests: {ex.Message}\n{ex.StackTrace}");
                return new ErrorResponse("Failed to retrieve tests");
            }

            string message = $"Retrieved {result.Count} {parsedMode.Value} tests";
            return new SuccessResponse(message, result);
        }
    }

    internal static class ModeParser
    {
        internal static bool TryParse(string modeStr, out TestMode? mode, out string error)
        {
            error = null;
            mode = null;

            if (string.IsNullOrWhiteSpace(modeStr))
            {
                error = "'mode' parameter cannot be empty";
                return false;
            }

            if (modeStr.Equals("EditMode", StringComparison.OrdinalIgnoreCase))
            {
                mode = TestMode.EditMode;
                return true;
            }

            if (modeStr.Equals("PlayMode", StringComparison.OrdinalIgnoreCase))
            {
                mode = TestMode.PlayMode;
                return true;
            }

            error = $"Unknown test mode: '{modeStr}'. Use 'EditMode' or 'PlayMode'";
            return false;
        }
    }
}
