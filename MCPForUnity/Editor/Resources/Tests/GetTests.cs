using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEditor.TestTools.TestRunner.Api;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnity.Editor.Resources.Tests
{
    /// <summary>
    /// Provides access to Unity tests from the Test Framework.
    /// This is a read-only resource that can be queried by MCP clients.
    /// </summary>
    [McpForUnityResource("get_tests")]
    public static class GetTests
    {
        public static object HandleCommand(JObject @params)
        {
            try
            {
                string modeStr = @params?["mode"]?.ToString();
                TestMode? parsedMode = null;

                if (!string.IsNullOrWhiteSpace(modeStr))
                {
                    if (!ModeParser.TryParse(modeStr, out parsedMode, out var error))
                    {
                        return Response.Error(error);
                    }
                }

                McpLog.Info(
                    parsedMode.HasValue
                        ? $"[GetTests] Retrieving tests for mode: {parsedMode.Value}"
                        : "[GetTests] Retrieving tests for all modes",
                    always: false
                );

                var tests = TestCollector.GetTests(parsedMode);
                string message = parsedMode.HasValue
                    ? $"Retrieved {tests.Count} {parsedMode.Value} tests"
                    : $"Retrieved {tests.Count} tests";

                return Response.Success(message, tests);
            }
            catch (Exception e)
            {
                McpLog.Error($"[GetTests] Error retrieving tests: {e.Message}");
                return Response.Error($"Error retrieving tests: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Provides access to Unity tests for a specific mode (EditMode or PlayMode).
    /// This is a read-only resource that can be queried by MCP clients.
    /// </summary>
    [McpForUnityResource("get_tests_for_mode")]
    public static class GetTestsForMode
    {
        public static object HandleCommand(JObject @params)
        {
            try
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

                McpLog.Info($"[GetTestsForMode] Retrieving tests for mode: {parsedMode.Value}", always: false);

                var tests = TestCollector.GetTests(parsedMode);
                string message = $"Retrieved {tests.Count} {parsedMode.Value} tests";
                return Response.Success(message, tests);
            }
            catch (Exception e)
            {
                McpLog.Error($"[GetTestsForMode] Error retrieving tests: {e.Message}");
                return Response.Error($"Error retrieving tests: {e.Message}");
            }
        }
    }

    internal static class TestCollector
    {
        private static readonly TestMode[] AllModes = { TestMode.EditMode, TestMode.PlayMode };

        internal static List<Dictionary<string, string>> GetTests(TestMode? filterMode)
        {
            var modesToQuery = filterMode.HasValue ? new[] { filterMode.Value } : AllModes;
            var tests = new List<Dictionary<string, string>>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            var api = ScriptableObject.CreateInstance<TestRunnerApi>();

            try
            {
                foreach (var mode in modesToQuery)
                {
                    var filter = new Filter { testMode = mode };
                    api.RetrieveTestList(filter, root =>
                    {
                        if (root == null)
                        {
                            return;
                        }

                        CollectFromNode(root, mode, tests, seen, new List<string>());
                    });
                }
            }
            finally
            {
                if (api != null)
                {
                    ScriptableObject.DestroyImmediate(api);
                }
            }

            return tests;
        }

        private static void CollectFromNode(
            ITestAdaptor node,
            TestMode mode,
            List<Dictionary<string, string>> output,
            HashSet<string> seen,
            List<string> path
        )
        {
            if (node == null)
            {
                return;
            }

            bool hasName = !string.IsNullOrEmpty(node.Name);
            if (hasName)
            {
                path.Add(node.Name);
            }

            bool hasChildren = node.HasChildren && node.Children != null;

            if (!hasChildren)
            {
                string fullName = string.IsNullOrEmpty(node.FullName) ? node.Name ?? string.Empty : node.FullName;
                string key = $"{mode}:{fullName}";

                if (!string.IsNullOrEmpty(fullName) && seen.Add(key))
                {
                    string computedPath = path.Count > 0 ? string.Join("/", path) : fullName;
                    output.Add(new Dictionary<string, string>
                    {
                        ["name"] = node.Name ?? fullName,
                        ["full_name"] = fullName,
                        ["path"] = computedPath,
                        ["mode"] = mode.ToString(),
                    });
                }
            }
            else
            {
                foreach (var child in node.Children)
                {
                    CollectFromNode(child, mode, output, seen, path);
                }
            }

            if (hasName)
            {
                path.RemoveAt(path.Count - 1);
            }
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

            if (modeStr.Equals("edit", StringComparison.OrdinalIgnoreCase) ||
                modeStr.Equals("editmode", StringComparison.OrdinalIgnoreCase) ||
                modeStr.Equals("EditMode", StringComparison.Ordinal))
            {
                mode = TestMode.EditMode;
                return true;
            }

            if (modeStr.Equals("play", StringComparison.OrdinalIgnoreCase) ||
                modeStr.Equals("playmode", StringComparison.OrdinalIgnoreCase) ||
                modeStr.Equals("PlayMode", StringComparison.Ordinal))
            {
                mode = TestMode.PlayMode;
                return true;
            }

            error = $"Unknown test mode: '{modeStr}'. Use 'EditMode' or 'PlayMode'";
            return false;
        }
    }
}
