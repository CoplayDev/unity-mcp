using System;
using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// MCP tool for Unity Package Manager.
    /// Actions: list, get_info, add, remove, search.
    /// </summary>
    [McpForUnityTool("manage_packages", AutoRegister = true)]
    public static class ManagePackages
    {
        public static object HandleCommand(JObject @params)
        {
            if (@params == null)
                return new ErrorResponse("Parameters cannot be null.");

            var p = new ToolParams(@params);
            var actionResult = p.GetRequired("action");
            if (!actionResult.IsSuccess)
                return new ErrorResponse(actionResult.ErrorMessage);

            string action = actionResult.Value.ToLowerInvariant();

            try
            {
                return action switch
                {
                    "list"     => ListPackages(p),
                    "get_info" => GetPackageInfo(p),
                    "add"      => AddPackage(p),
                    "remove"   => RemovePackage(p),
                    "search"   => SearchPackages(p),
                    _ => new ErrorResponse(
                        $"Unknown action: '{action}'. Supported: list, get_info, add, remove, search")
                };
            }
            catch (Exception e)
            {
                McpLog.Error($"[ManagePackages] Action '{action}' failed: {e}");
                return new ErrorResponse($"Internal error: {e.Message}");
            }
        }

        #region Package Operations

        private static object ListPackages(ToolParams p)
        {
            bool includeBuiltIn = p.GetBool("include_built_in", false);

            var request = Client.List(includeBuiltIn);
            WaitForRequest(request);

            if (request.Status == StatusCode.Failure)
                return new ErrorResponse($"Failed to list packages: {request.Error?.message}");

            var allItems = new List<Dictionary<string, object>>();
            foreach (var pkg in request.Result)
            {
                allItems.Add(new Dictionary<string, object>
                {
                    ["name"] = pkg.name,
                    ["display_name"] = pkg.displayName,
                    ["version"] = pkg.version,
                    ["source"] = pkg.source.ToString(),
                    ["status"] = pkg.status.ToString(),
                });
            }

            return new SuccessResponse($"Found {allItems.Count} package(s).", new Dictionary<string, object>
            {
                ["total_count"] = allItems.Count,
                ["packages"] = allItems,
            });
        }

        private static object GetPackageInfo(ToolParams p)
        {
            var nameResult = p.GetRequired("package_name");
            if (!nameResult.IsSuccess)
                return new ErrorResponse(nameResult.ErrorMessage);

            // List and find
            var request = Client.List(true);
            WaitForRequest(request);

            if (request.Status == StatusCode.Failure)
                return new ErrorResponse($"Failed to list packages: {request.Error?.message}");

            var pkg = request.Result.FirstOrDefault(
                x => string.Equals(x.name, nameResult.Value, StringComparison.OrdinalIgnoreCase));

            if (pkg == null)
                return new ErrorResponse($"Package '{nameResult.Value}' not found.");

            var deps = new List<string>();
            if (pkg.dependencies != null)
            {
                foreach (var dep in pkg.dependencies)
                    deps.Add($"{dep.name}@{dep.version}");
            }

            return new SuccessResponse($"Package '{pkg.displayName}'.", new Dictionary<string, object>
            {
                ["name"] = pkg.name,
                ["display_name"] = pkg.displayName,
                ["version"] = pkg.version,
                ["description"] = pkg.description,
                ["source"] = pkg.source.ToString(),
                ["status"] = pkg.status.ToString(),
                ["resolved_path"] = pkg.resolvedPath,
                ["dependencies"] = deps,
            });
        }

        private static object AddPackage(ToolParams p)
        {
            var idResult = p.GetRequired("package_id");
            if (!idResult.IsSuccess)
                return new ErrorResponse(idResult.ErrorMessage);

            var request = Client.Add(idResult.Value);
            WaitForRequest(request);

            if (request.Status == StatusCode.Failure)
                return new ErrorResponse($"Failed to add package: {request.Error?.message}");

            var pkg = request.Result;
            return new SuccessResponse($"Package '{pkg.displayName}' ({pkg.version}) installed.", new Dictionary<string, object>
            {
                ["name"] = pkg.name,
                ["version"] = pkg.version,
            });
        }

        private static object RemovePackage(ToolParams p)
        {
            var nameResult = p.GetRequired("package_name");
            if (!nameResult.IsSuccess)
                return new ErrorResponse(nameResult.ErrorMessage);

            var request = Client.Remove(nameResult.Value);
            WaitForRequest(request);

            if (request.Status == StatusCode.Failure)
                return new ErrorResponse($"Failed to remove package: {request.Error?.message}");

            return new SuccessResponse($"Package '{nameResult.Value}' removed.");
        }

        private static object SearchPackages(ToolParams p)
        {
            var queryResult = p.GetRequired("query");
            if (!queryResult.IsSuccess)
                return new ErrorResponse(queryResult.ErrorMessage);

            var request = Client.SearchAll(queryResult.Value);
            WaitForRequest(request);

            if (request.Status == StatusCode.Failure)
                return new ErrorResponse($"Search failed: {request.Error?.message}");

            var results = new List<Dictionary<string, object>>();
            foreach (var pkg in request.Result)
            {
                results.Add(new Dictionary<string, object>
                {
                    ["name"] = pkg.name,
                    ["display_name"] = pkg.displayName,
                    ["version"] = pkg.version,
                    ["description"] = pkg.description?.Substring(0, Math.Min(pkg.description.Length, 200)),
                });
            }

            return new SuccessResponse($"Found {results.Count} package(s) matching '{queryResult.Value}'.", new Dictionary<string, object>
            {
                ["total_count"] = results.Count,
                ["packages"] = results,
            });
        }

        #endregion

        #region Helpers

        private static void WaitForRequest<T>(T request) where T : Request
        {
            // Spin-wait for the async request (runs on Editor main thread)
            while (!request.IsCompleted)
            {
                System.Threading.Thread.Sleep(10);
            }
        }

        #endregion
    }
}
