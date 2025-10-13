using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services;
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

            var service = MCPServiceLocator.Tests;
            var result = await service.GetTestsAsync(mode: null).ConfigureAwait(true);
            var tests = result is List<Dictionary<string, string>> list
                ? list
                : new List<Dictionary<string, string>>(result);

            if (tests == null)
            {
                return Response.Error("Failed to retrieve tests");
            }

            string message = $"Retrieved {tests.Count} tests";

            return Response.Success(message, tests);
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
            string modeStr = @params["mode"]?.ToString();
            if (string.IsNullOrEmpty(modeStr))
            {
                return Response.Error("'mode' parameter is required");
            }

            if (!ModeParser.TryParse(modeStr, out var parsedMode, out var parseError))
            {
                return Response.Error(parseError);
            }

            McpLog.Info($"[GetTestsForMode] Retrieving tests for mode: {parsedMode.Value}");

            var service = MCPServiceLocator.Tests;
            var result = await service.GetTestsAsync(parsedMode).ConfigureAwait(true);
            var tests = result is List<Dictionary<string, string>> list
                ? list
                : new List<Dictionary<string, string>>(result);

            if (tests == null)
            {
                return Response.Error("Failed to retrieve tests");
            }

            string message = $"Retrieved {tests.Count} {parsedMode.Value} tests";
            return Response.Success(message, tests);
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

            if (modeStr.Equals("edit", StringComparison.OrdinalIgnoreCase))
            {
                mode = TestMode.EditMode;
                return true;
            }

            if (modeStr.Equals("play", StringComparison.OrdinalIgnoreCase))
            {
                mode = TestMode.PlayMode;
                return true;
            }

            error = $"Unknown test mode: '{modeStr}'. Use 'edit' or 'play'";
            return false;
        }
    }
}
