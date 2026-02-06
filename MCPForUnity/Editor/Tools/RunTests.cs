using System;
using System.Linq;
using System.Threading.Tasks;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Resources.Tests;
using MCPForUnity.Editor.Services;
using Newtonsoft.Json.Linq;
using UnityEditor.TestTools.TestRunner.Api;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Starts a Unity Test Runner run asynchronously and returns a job id immediately.
    /// Use get_test_job(job_id) to poll status/results.
    /// </summary>
    [McpForUnityTool("run_tests", AutoRegister = false)]
    public static class RunTests
    {
        public static Task<object> HandleCommand(JObject @params)
        {
            try
            {
                // Check for clear_stuck action first
                if (ParamCoercion.CoerceBool(@params?["clear_stuck"], false))
                {
                    bool wasCleared = TestJobManager.ClearStuckJob();
                    return Task.FromResult<object>(new SuccessResponse(
                        wasCleared ? "Stuck job cleared." : "No running job to clear.",
                        new { cleared = wasCleared }
                    ));
                }

                string modeStr = @params?["mode"]?.ToString();
                if (string.IsNullOrWhiteSpace(modeStr))
                {
                    modeStr = "EditMode";
                }

                if (!ModeParser.TryParse(modeStr, out var parsedMode, out var parseError))
                {
                    return Task.FromResult<object>(new ErrorResponse(parseError));
                }

                bool includeDetails = ParamCoercion.CoerceBool(@params?["includeDetails"] ?? @params?["include_details"], false);
                bool includeFailedTests = ParamCoercion.CoerceBool(@params?["includeFailedTests"] ?? @params?["include_failed_tests"], false);

                var filterOptions = GetFilterOptions(@params);
                string jobId = TestJobManager.StartJob(parsedMode.Value, filterOptions);

                return Task.FromResult<object>(new SuccessResponse("Test job started.", new
                {
                    job_id = jobId,
                    status = "running",
                    mode = parsedMode.Value.ToString(),
                    include_details = includeDetails,
                    include_failed_tests = includeFailedTests
                }));
            }
            catch (Exception ex)
            {
                // Normalize the already-running case to a stable error token.
                if (ex.Message != null && ex.Message.IndexOf("already in progress", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return Task.FromResult<object>(new ErrorResponse("tests_running", new { reason = "tests_running", retry_after_ms = 5000 }));
                }
                return Task.FromResult<object>(new ErrorResponse($"Failed to start test job: {ex.Message}"));
            }
        }

        private static TestFilterOptions GetFilterOptions(JObject @params)
        {
            if (@params == null)
            {
                return null;
            }

            string[] ParseStringArray(string camelKey, string snakeKey = null)
            {
                var token = @params[camelKey] ?? (snakeKey != null ? @params[snakeKey] : null);
                if (token == null) return null;
                if (token.Type == JTokenType.String)
                {
                    var value = token.ToString();
                    if (string.IsNullOrWhiteSpace(value)) return null;
                    // Handle stringified JSON arrays (e.g. "[\"name1\", \"name2\"]")
                    var trimmed = value.Trim();
                    if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                    {
                        try
                        {
                            var parsed = JArray.Parse(trimmed);
                            var values = parsed.Values<string>()
                                .Where(s => !string.IsNullOrWhiteSpace(s))
                                .ToArray();
                            return values.Length > 0 ? values : null;
                        }
                        catch { /* not a valid JSON array, treat as plain string */ }
                    }
                    return new[] { value };
                }
                if (token.Type == JTokenType.Array)
                {
                    var array = token as JArray;
                    if (array == null || array.Count == 0) return null;
                    // Handle double-serialized arrays: MCP bridge may send ["[\"name1\"]"]
                    // where the inner string is a stringified JSON array
                    if (array.Count == 1 && array[0].Type == JTokenType.String)
                    {
                        var inner = array[0].ToString().Trim();
                        if (inner.StartsWith("[") && inner.EndsWith("]"))
                        {
                            try
                            {
                                array = JArray.Parse(inner);
                            }
                            catch { /* use original array */ }
                        }
                    }
                    // Handle nested arrays: [[name1, name2]]
                    else if (array.Count == 1 && array[0].Type == JTokenType.Array)
                    {
                        array = array[0] as JArray ?? array;
                    }
                    var values = array
                        .Values<string>()
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .ToArray();
                    return values.Length > 0 ? values : null;
                }
                return null;
            }

            var testNames = ParseStringArray("testNames", "test_names");
            var groupNames = ParseStringArray("groupNames", "group_names");
            var categoryNames = ParseStringArray("categoryNames", "category_names");
            var assemblyNames = ParseStringArray("assemblyNames", "assembly_names");

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
    }
}
