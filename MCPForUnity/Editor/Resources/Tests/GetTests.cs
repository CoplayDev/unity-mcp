using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
        public static async Task<object> HandleCommand(JObject @params)
        {
            McpLog.Info("[GetTests] Retrieving tests for all modes");

            var tests = await TestCollector.GetTestsAsync(filterMode: null).ConfigureAwait(true);

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

            McpLog.Info($"[GetTestsForMode] Retrieving tests for mode: {parsedMode.Value}", always: false);

            var tests = await TestCollector.GetTestsAsync(parsedMode).ConfigureAwait(true);

            if (tests == null)
            {
                return Response.Error("Failed to retrieve tests");
            }

            string message = $"Retrieved {tests.Count} {parsedMode.Value} tests";
            return Response.Success(message, tests);
        }
    }

    internal static class TestCollector
    {
        private static readonly TestMode[] AllModes = { TestMode.EditMode, TestMode.PlayMode };

        /// <summary>
        /// Retrieves tests asynchronously by awaiting Unity's TestRunnerApi callback.
        /// </summary>
        internal static async Task<List<Dictionary<string, string>>> GetTestsAsync(TestMode? filterMode)
        {
            var modesToQuery = filterMode.HasValue ? new[] { filterMode.Value } : AllModes;
            var tests = new List<Dictionary<string, string>>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            var api = ScriptableObject.CreateInstance<TestRunnerApi>();

            try
            {
                foreach (var mode in modesToQuery)
                {
                    var root = await RetrieveTestRootAsync(api, mode).ConfigureAwait(true);
                    if (root != null)
                    {
                        CollectFromNode(root, mode, tests, seen, new List<string>());
                    }
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

        private static async Task<ITestAdaptor> RetrieveTestRootAsync(TestRunnerApi api, TestMode mode)
        {
            var tcs = new TaskCompletionSource<ITestAdaptor>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool callbackInvoked = false;

            api.RetrieveTestList(mode, root =>
            {
                callbackInvoked = true;
                tcs.TrySetResult(root);
            });

            int framesRemaining = 100;
            while (!callbackInvoked && framesRemaining-- > 0)
            {
                await WaitForNextEditorFrame().ConfigureAwait(true);
            }

            if (!callbackInvoked)
            {
                McpLog.Warn($"[TestCollector] Timeout waiting for test retrieval callback for {mode}");
                return null;
            }

            try
            {
                return await tcs.Task.ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                McpLog.Error($"[TestCollector] Error retrieving tests for {mode}: {ex.Message}\n{ex.StackTrace}");
                return null;
            }
        }

        private static Task WaitForNextEditorFrame()
        {
            var frameTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            EditorApplication.delayCall += () => frameTcs.TrySetResult(true);
            return frameTcs.Task;
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
                    output.Add(new Dictionary<string, string>
                    {
                        ["name"] = node.Name ?? fullName,
                        ["full_name"] = fullName,
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
