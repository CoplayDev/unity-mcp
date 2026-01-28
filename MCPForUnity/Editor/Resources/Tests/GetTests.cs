using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services;
using Newtonsoft.Json.Linq;
using UnityEditor.TestTools.TestRunner.Api;

namespace MCPForUnity.Editor.Resources.Tests
{
    /// <summary>
    /// Provides access to Unity tests from the Test Framework with pagination and filtering support.
    /// This is a read-only resource that can be queried by MCP clients.
    ///
    /// Parameters:
    /// - mode (optional): Filter by "EditMode" or "PlayMode"
    /// - filter (optional): Filter test names by pattern (case-insensitive contains)
    /// - page_size (optional): Number of tests per page (default: 50, max: 200)
    /// - cursor (optional): 0-based cursor for pagination
    /// - page_number (optional): 1-based page number (converted to cursor)
    /// </summary>
    [McpForUnityResource("get_tests")]
    public static class GetTests
    {
        private const int DEFAULT_PAGE_SIZE = 50;
        private const int MAX_PAGE_SIZE = 200;

        public static async Task<object> HandleCommand(JObject @params)
        {
            // Parse mode filter
            TestMode? modeFilter = null;
            string modeStr = @params?["mode"]?.ToString();
            if (!string.IsNullOrEmpty(modeStr))
            {
                if (!ModeParser.TryParse(modeStr, out modeFilter, out var parseError))
                {
                    return new ErrorResponse(parseError);
                }
            }

            // Parse name filter
            string nameFilter = @params?["filter"]?.ToString();

            McpLog.Info($"[GetTests] Retrieving tests (mode={modeFilter?.ToString() ?? "all"}, filter={nameFilter ?? "none"})");

            IReadOnlyList<Dictionary<string, string>> allTests;
            try
            {
                allTests = await MCPServiceLocator.Tests.GetTestsAsync(modeFilter).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                McpLog.Error($"[GetTests] Error retrieving tests: {ex.Message}\n{ex.StackTrace}");
                return new ErrorResponse("Failed to retrieve tests");
            }

            // Apply name filter if provided and convert to List for pagination
            List<Dictionary<string, string>> filteredTests;
            if (!string.IsNullOrEmpty(nameFilter))
            {
                filteredTests = allTests
                    .Where(t =>
                        (t.ContainsKey("name") && t["name"].IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (t.ContainsKey("full_name") && t["full_name"].IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                    )
                    .ToList();
            }
            else
            {
                filteredTests = allTests.ToList();
            }

            // Clamp page_size before parsing pagination to ensure cursor is computed correctly
            int requestedPageSize = ParamCoercion.CoerceInt(
                @params?["page_size"] ?? @params?["pageSize"],
                DEFAULT_PAGE_SIZE
            );
            int clampedPageSize = System.Math.Min(requestedPageSize, MAX_PAGE_SIZE);
            if (clampedPageSize <= 0) clampedPageSize = DEFAULT_PAGE_SIZE;

            // Create modified params with clamped page_size for cursor calculation
            var paginationParams = new JObject(@params);
            paginationParams["page_size"] = clampedPageSize;

            // Parse pagination with clamped page size
            var pagination = PaginationRequest.FromParams(paginationParams, DEFAULT_PAGE_SIZE);

            // Create paginated response
            var response = PaginationResponse<Dictionary<string, string>>.Create(filteredTests, pagination);

            string message = !string.IsNullOrEmpty(nameFilter)
                ? $"Retrieved {response.Items.Count} of {response.TotalCount} tests matching '{nameFilter}' (cursor {response.Cursor})"
                : $"Retrieved {response.Items.Count} of {response.TotalCount} tests (cursor {response.Cursor})";

            return new SuccessResponse(message, response);
        }
    }

    /// <summary>
    /// DEPRECATED: Use get_tests with mode parameter instead.
    /// Provides access to Unity tests for a specific mode (EditMode or PlayMode).
    /// This is a read-only resource that can be queried by MCP clients.
    ///
    /// Parameters:
    /// - mode (required): "EditMode" or "PlayMode"
    /// - filter (optional): Filter test names by pattern (case-insensitive contains)
    /// - page_size (optional): Number of tests per page (default: 50, max: 200)
    /// - cursor (optional): 0-based cursor for pagination
    /// </summary>
    [McpForUnityResource("get_tests_for_mode")]
    public static class GetTestsForMode
    {
        private const int DEFAULT_PAGE_SIZE = 50;
        private const int MAX_PAGE_SIZE = 200;

        public static async Task<object> HandleCommand(JObject @params)
        {
            string modeStr = @params?["mode"]?.ToString();
            if (string.IsNullOrEmpty(modeStr))
            {
                return new ErrorResponse("'mode' parameter is required");
            }

            if (!ModeParser.TryParse(modeStr, out var parsedMode, out var parseError))
            {
                return new ErrorResponse(parseError);
            }

            // Parse name filter
            string nameFilter = @params?["filter"]?.ToString();

            McpLog.Info($"[GetTestsForMode] Retrieving tests for mode: {parsedMode.Value} (filter={nameFilter ?? "none"})");

            IReadOnlyList<Dictionary<string, string>> allTests;
            try
            {
                allTests = await MCPServiceLocator.Tests.GetTestsAsync(parsedMode).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                McpLog.Error($"[GetTestsForMode] Error retrieving tests: {ex.Message}\n{ex.StackTrace}");
                return new ErrorResponse("Failed to retrieve tests");
            }

            // Apply name filter if provided and convert to List for pagination
            List<Dictionary<string, string>> filteredTests;
            if (!string.IsNullOrEmpty(nameFilter))
            {
                filteredTests = allTests
                    .Where(t =>
                        (t.ContainsKey("name") && t["name"].IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (t.ContainsKey("full_name") && t["full_name"].IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                    )
                    .ToList();
            }
            else
            {
                filteredTests = allTests.ToList();
            }

            // Clamp page_size before parsing pagination to ensure cursor is computed correctly
            int requestedPageSize = ParamCoercion.CoerceInt(
                @params?["page_size"] ?? @params?["pageSize"],
                DEFAULT_PAGE_SIZE
            );
            int clampedPageSize = System.Math.Min(requestedPageSize, MAX_PAGE_SIZE);
            if (clampedPageSize <= 0) clampedPageSize = DEFAULT_PAGE_SIZE;

            // Create modified params with clamped page_size for cursor calculation
            var paginationParams = new JObject(@params);
            paginationParams["page_size"] = clampedPageSize;

            // Parse pagination with clamped page size
            var pagination = PaginationRequest.FromParams(paginationParams, DEFAULT_PAGE_SIZE);

            // Create paginated response
            var response = PaginationResponse<Dictionary<string, string>>.Create(filteredTests, pagination);

            string message = nameFilter != null
                ? $"Retrieved {response.Items.Count} of {response.TotalCount} {parsedMode.Value} tests matching '{nameFilter}'"
                : $"Retrieved {response.Items.Count} of {response.TotalCount} {parsedMode.Value} tests";

            return new SuccessResponse(message, response);
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
