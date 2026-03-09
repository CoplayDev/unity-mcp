using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using UnityEngine;

namespace MCPForUnity.Editor.Tools
{
    [McpForUnityTool("manage_packages", AutoRegister = false, Group = "core", RequiresPolling = true, PollAction = "status")]
    public static class ManagePackages
    {
        // Pending async requests keyed by job ID
        private static readonly Dictionary<string, Request> PendingRequests = new();

        // Pending list/search requests keyed by job ID
        private static readonly Dictionary<string, ListRequest> PendingListRequests = new();
        private static readonly Dictionary<string, SearchRequest> PendingSearchRequests = new();

        public static object HandleCommand(JObject @params)
        {
            if (@params == null)
                return new ErrorResponse("Parameters cannot be null.");

            var p = new ToolParams(@params);

            var actionResult = p.GetRequired("action");
            if (!actionResult.IsSuccess)
                return new ErrorResponse(actionResult.ErrorMessage);

            string action = actionResult.Value.ToLowerInvariant();

            switch (action)
            {
                case "add_package":
                    return AddPackage(p);
                case "remove_package":
                    return RemovePackage(p);
                case "status":
                    return GetStatus(p);
                case "list_packages":
                    return ListPackages(p);
                case "search_packages":
                    return SearchPackages(p);
                case "get_package_info":
                    return GetPackageInfo(p);
                case "list_registries":
                    return ListRegistries();
                case "add_registry":
                    return AddRegistry(p);
                case "remove_registry":
                    return RemoveRegistry(p);
                case "embed_package":
                    return EmbedPackage(p);
                case "resolve_packages":
                    return ResolvePackages();
                case "ping":
                    return Ping();
                default:
                    return new ErrorResponse(
                        $"Unknown action: '{action}'. Supported actions: add_package, remove_package, status, list_packages, search_packages, get_package_info, list_registries, add_registry, remove_registry, embed_package, resolve_packages, ping.");
            }
        }

        // === add_package ===
        private static object AddPackage(ToolParams p)
        {
            var packageResult = p.GetRequired("package", "'package' parameter is required for add_package.");
            if (!packageResult.IsSuccess)
                return new ErrorResponse(packageResult.ErrorMessage);

            string package = packageResult.Value;

            var (isValid, warning) = ValidatePackageIdentifier(package);
            if (!isValid)
                return new ErrorResponse(warning);

            try
            {
                var request = Client.Add(package);
                string jobId = PackageJobManager.StartJob("add", package);
                PendingRequests[jobId] = request;

                RegisterCompletionCallback(jobId, request);

                string message = $"Package installation started for '{package}'. Use status action to check progress.";
                if (warning != null)
                    message = $"WARNING: {warning}\n{message}";

                return new PendingResponse(
                    message,
                    pollIntervalSeconds: 3.0,
                    data: new { job_id = jobId, operation = "add", package_ = package, warning }
                );
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Failed to start package installation: {e.Message}");
            }
        }

        // === remove_package ===
        private static object RemovePackage(ToolParams p)
        {
            var packageResult = p.GetRequired("package", "'package' parameter is required for remove_package.");
            if (!packageResult.IsSuccess)
                return new ErrorResponse(packageResult.ErrorMessage);

            string package = packageResult.Value;
            bool force = p.GetBool("force");

            // Check for dependents before removing
            if (!force)
            {
                var dependents = GetDependentPackages(package);
                if (dependents.Length > 0)
                {
                    string depList = string.Join(", ", dependents);
                    return new ErrorResponse(
                        $"Cannot remove '{package}': {dependents.Length} installed package(s) depend on it: {depList}. " +
                        "Set force=true to remove anyway.");
                }
            }

            try
            {
                var request = Client.Remove(package);
                string jobId = PackageJobManager.StartJob("remove", package);
                PendingRequests[jobId] = request;

                RegisterCompletionCallback(jobId, request);

                return new PendingResponse(
                    $"Package removal started for '{package}'. Use status action to check progress.",
                    pollIntervalSeconds: 3.0,
                    data: new { job_id = jobId, operation = "remove", package_ = package }
                );
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Failed to start package removal: {e.Message}");
            }
        }

        // === status ===
        private static object GetStatus(ToolParams p)
        {
            string jobId = p.Get("job_id");

            // Check pending list/search requests first
            if (!string.IsNullOrEmpty(jobId))
            {
                if (PendingListRequests.TryGetValue(jobId, out var listReq))
                    return CheckListRequest(jobId, listReq);

                if (PendingSearchRequests.TryGetValue(jobId, out var searchReq))
                    return CheckSearchRequest(jobId, searchReq);
            }

            PackageJob job;
            if (string.IsNullOrEmpty(jobId))
            {
                job = PackageJobManager.GetLatestJob();
                if (job == null)
                    return new SuccessResponse("No package jobs found.");
            }
            else
            {
                job = PackageJobManager.GetJob(jobId);
                if (job == null)
                    return new ErrorResponse($"No job found with ID '{jobId}'.");
            }

            // If job is still running, check in-memory request
            if (job.Status == PackageJobStatus.Running && PendingRequests.TryGetValue(job.JobId, out var req))
            {
                if (req.IsCompleted)
                {
                    FinalizeRequest(job.JobId, req);
                    job = PackageJobManager.GetJob(job.JobId);
                }
            }

            var serialized = PackageJobManager.ToSerializable(job);
            string message = job.Status switch
            {
                PackageJobStatus.Running => $"Job {job.JobId} is still running ({job.Operation} '{job.Package}').",
                PackageJobStatus.Succeeded => $"Job {job.JobId} succeeded ({job.Operation} '{job.Package}').",
                PackageJobStatus.Failed => $"Job {job.JobId} failed ({job.Operation} '{job.Package}'): {job.Error}",
                _ => $"Job {job.JobId}: {job.Status}"
            };

            if (job.Status == PackageJobStatus.Running)
            {
                return new PendingResponse(
                    message,
                    pollIntervalSeconds: 3.0,
                    data: serialized
                );
            }

            return new SuccessResponse(message, serialized);
        }

        // === list_packages ===
        private static object ListPackages(ToolParams p)
        {
            try
            {
                var request = Client.List();
                string jobId = Guid.NewGuid().ToString("N");
                PendingListRequests[jobId] = request;

                // Try immediate completion for fast responses
                if (request.IsCompleted)
                    return CheckListRequest(jobId, request);

                return new PendingResponse(
                    "Listing installed packages...",
                    pollIntervalSeconds: 1.0,
                    data: new { job_id = jobId, operation = "list_packages" }
                );
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Failed to list packages: {e.Message}");
            }
        }

        private static object CheckListRequest(string jobId, ListRequest request)
        {
            if (!request.IsCompleted)
            {
                return new PendingResponse(
                    "Listing installed packages...",
                    pollIntervalSeconds: 1.0,
                    data: new { job_id = jobId, operation = "list_packages" }
                );
            }

            PendingListRequests.Remove(jobId);

            if (request.Status == StatusCode.Failure)
                return new ErrorResponse($"Failed to list packages: {request.Error?.message ?? "Unknown error"}");

            var packages = request.Result
                .Select(pkg => new
                {
                    name = pkg.name,
                    version = pkg.version,
                    display_name = pkg.displayName,
                    source = pkg.source.ToString()
                })
                .ToArray();

            return new SuccessResponse(
                $"Found {packages.Length} installed package(s).",
                new { packages, count = packages.Length }
            );
        }

        // === search_packages ===
        private static object SearchPackages(ToolParams p)
        {
            var queryResult = p.GetRequired("query", "'query' parameter is required for search_packages.");
            if (!queryResult.IsSuccess)
                return new ErrorResponse(queryResult.ErrorMessage);

            try
            {
                var request = Client.Search(queryResult.Value);
                string jobId = Guid.NewGuid().ToString("N");
                PendingSearchRequests[jobId] = request;

                if (request.IsCompleted)
                    return CheckSearchRequest(jobId, request);

                return new PendingResponse(
                    $"Searching packages for '{queryResult.Value}'...",
                    pollIntervalSeconds: 1.0,
                    data: new { job_id = jobId, operation = "search_packages", query = queryResult.Value }
                );
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Failed to search packages: {e.Message}");
            }
        }

        private static object CheckSearchRequest(string jobId, SearchRequest request)
        {
            if (!request.IsCompleted)
            {
                return new PendingResponse(
                    "Searching packages...",
                    pollIntervalSeconds: 1.0,
                    data: new { job_id = jobId, operation = "search_packages" }
                );
            }

            PendingSearchRequests.Remove(jobId);

            if (request.Status == StatusCode.Failure)
                return new ErrorResponse($"Package search failed: {request.Error?.message ?? "Unknown error"}");

            var packages = request.Result
                .Select(pkg => new
                {
                    name = pkg.name,
                    version = pkg.version,
                    display_name = pkg.displayName,
                    description = TruncateDescription(pkg.description)
                })
                .ToArray();

            return new SuccessResponse(
                $"Found {packages.Length} matching package(s).",
                new { packages, count = packages.Length }
            );
        }

        // === get_package_info ===
        private static object GetPackageInfo(ToolParams p)
        {
            var packageResult = p.GetRequired("package", "'package' parameter is required for get_package_info.");
            if (!packageResult.IsSuccess)
                return new ErrorResponse(packageResult.ErrorMessage);

            string package = packageResult.Value;

            try
            {
                var allPackages = PackageInfo.GetAllRegisteredPackages();
                var info = allPackages.FirstOrDefault(pkg =>
                    string.Equals(pkg.name, package, StringComparison.OrdinalIgnoreCase));

                if (info == null)
                    return new ErrorResponse($"Package '{package}' is not installed.");

                var dependencies = info.dependencies
                    .Select(d => new { name = d.name, version = d.version })
                    .ToArray();

                return new SuccessResponse(
                    $"Package '{info.displayName}' ({info.name}@{info.version}).",
                    new
                    {
                        name = info.name,
                        version = info.version,
                        display_name = info.displayName,
                        description = info.description,
                        source = info.source.ToString(),
                        resolved_path = info.resolvedPath,
                        author = info.author?.name,
                        dependencies,
                        dependency_count = dependencies.Length
                    }
                );
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Failed to get package info: {e.Message}");
            }
        }

        // === list_registries ===
        private static object ListRegistries()
        {
            try
            {
                string manifestPath = GetManifestPath();
                if (!File.Exists(manifestPath))
                    return new ErrorResponse("Packages/manifest.json not found.");

                var manifest = JObject.Parse(File.ReadAllText(manifestPath));
                var registries = manifest["scopedRegistries"] as JArray ?? new JArray();

                var result = registries.Select(r => new
                {
                    name = r["name"]?.ToString(),
                    url = r["url"]?.ToString(),
                    scopes = (r["scopes"] as JArray)?.Select(s => s.ToString()).ToArray() ?? Array.Empty<string>()
                }).ToArray();

                return new SuccessResponse(
                    $"Found {result.Length} scoped registr(y/ies).",
                    new { registries = result, count = result.Length }
                );
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Failed to read registries: {e.Message}");
            }
        }

        // === add_registry ===
        private static object AddRegistry(ToolParams p)
        {
            var nameResult = p.GetRequired("name", "'name' parameter is required for add_registry.");
            if (!nameResult.IsSuccess)
                return new ErrorResponse(nameResult.ErrorMessage);

            var urlResult = p.GetRequired("url", "'url' parameter is required for add_registry.");
            if (!urlResult.IsSuccess)
                return new ErrorResponse(urlResult.ErrorMessage);

            string[] scopes = p.GetStringArray("scopes");
            if (scopes == null || scopes.Length == 0)
                return new ErrorResponse("'scopes' parameter is required (array of scope strings).");

            try
            {
                string manifestPath = GetManifestPath();
                if (!File.Exists(manifestPath))
                    return new ErrorResponse("Packages/manifest.json not found.");

                var manifest = JObject.Parse(File.ReadAllText(manifestPath));
                var registries = manifest["scopedRegistries"] as JArray;
                if (registries == null)
                {
                    registries = new JArray();
                    manifest["scopedRegistries"] = registries;
                }

                // Check for duplicate
                foreach (var reg in registries)
                {
                    if (string.Equals(reg["name"]?.ToString(), nameResult.Value, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(reg["url"]?.ToString(), urlResult.Value, StringComparison.OrdinalIgnoreCase))
                    {
                        return new ErrorResponse($"A registry with name '{nameResult.Value}' or URL '{urlResult.Value}' already exists.");
                    }
                }

                var newRegistry = new JObject
                {
                    ["name"] = nameResult.Value,
                    ["url"] = urlResult.Value,
                    ["scopes"] = new JArray(scopes)
                };
                registries.Add(newRegistry);

                File.WriteAllText(manifestPath, manifest.ToString(Formatting.Indented));
                Client.Resolve();

                return new SuccessResponse(
                    $"Added scoped registry '{nameResult.Value}'.",
                    new
                    {
                        name = nameResult.Value,
                        url = urlResult.Value,
                        scopes
                    }
                );
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Failed to add registry: {e.Message}");
            }
        }

        // === remove_registry ===
        private static object RemoveRegistry(ToolParams p)
        {
            string name = p.Get("name");
            string url = p.Get("url");

            if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(url))
                return new ErrorResponse("Either 'name' or 'url' parameter is required for remove_registry.");

            try
            {
                string manifestPath = GetManifestPath();
                if (!File.Exists(manifestPath))
                    return new ErrorResponse("Packages/manifest.json not found.");

                var manifest = JObject.Parse(File.ReadAllText(manifestPath));
                var registries = manifest["scopedRegistries"] as JArray;
                if (registries == null || registries.Count == 0)
                    return new ErrorResponse("No scoped registries configured.");

                JToken toRemove = null;
                foreach (var reg in registries)
                {
                    bool nameMatch = !string.IsNullOrEmpty(name)
                        && string.Equals(reg["name"]?.ToString(), name, StringComparison.OrdinalIgnoreCase);
                    bool urlMatch = !string.IsNullOrEmpty(url)
                        && string.Equals(reg["url"]?.ToString(), url, StringComparison.OrdinalIgnoreCase);

                    if (nameMatch || urlMatch)
                    {
                        toRemove = reg;
                        break;
                    }
                }

                if (toRemove == null)
                {
                    string identifier = !string.IsNullOrEmpty(name) ? $"name '{name}'" : $"URL '{url}'";
                    return new ErrorResponse($"No registry found matching {identifier}.");
                }

                string removedName = toRemove["name"]?.ToString();
                registries.Remove(toRemove);

                if (registries.Count == 0)
                    manifest.Remove("scopedRegistries");

                File.WriteAllText(manifestPath, manifest.ToString(Formatting.Indented));
                Client.Resolve();

                return new SuccessResponse($"Removed scoped registry '{removedName}'.");
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Failed to remove registry: {e.Message}");
            }
        }

        // === embed_package ===
        private static object EmbedPackage(ToolParams p)
        {
            var packageResult = p.GetRequired("package", "'package' parameter is required for embed_package.");
            if (!packageResult.IsSuccess)
                return new ErrorResponse(packageResult.ErrorMessage);

            try
            {
                var request = Client.Embed(packageResult.Value);
                string jobId = PackageJobManager.StartJob("embed", packageResult.Value);
                PendingRequests[jobId] = request;

                RegisterCompletionCallback(jobId, request);

                return new PendingResponse(
                    $"Embedding package '{packageResult.Value}'. Use status action to check progress.",
                    pollIntervalSeconds: 3.0,
                    data: new { job_id = jobId, operation = "embed", package_ = packageResult.Value }
                );
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Failed to embed package: {e.Message}");
            }
        }

        // === resolve_packages ===
        private static object ResolvePackages()
        {
            try
            {
                Client.Resolve();
                return new SuccessResponse("Package resolution triggered. Unity will re-resolve all packages.");
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Failed to trigger package resolution: {e.Message}");
            }
        }

        // === ping ===
        private static object Ping()
        {
            try
            {
                var allPackages = PackageInfo.GetAllRegisteredPackages();
                return new SuccessResponse(
                    "Package manager is available.",
                    new
                    {
                        unity_version = Application.unityVersion,
                        installed_package_count = allPackages.Length,
                        is_compiling = EditorApplication.isCompiling,
                        is_updating = EditorApplication.isUpdating
                    }
                );
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Package manager check failed: {e.Message}");
            }
        }

        // --- Helpers ---

        private static void RegisterCompletionCallback(string jobId, Request request)
        {
            void CheckCompletion()
            {
                if (!PendingRequests.ContainsKey(jobId))
                {
                    EditorApplication.update -= CheckCompletion;
                    return;
                }

                if (!request.IsCompleted)
                    return;

                EditorApplication.update -= CheckCompletion;
                FinalizeRequest(jobId, request);
            }

            EditorApplication.update += CheckCompletion;
        }

        private static void FinalizeRequest(string jobId, Request request)
        {
            PendingRequests.Remove(jobId);

            if (request.Status == StatusCode.Failure)
            {
                PackageJobManager.CompleteJob(jobId, false,
                    error: request.Error?.message ?? "Unknown package manager error");
                return;
            }

            string version = null;
            string name = null;

            if (request is AddRequest addReq && addReq.Result != null)
            {
                version = addReq.Result.version;
                name = addReq.Result.name;
            }
            else if (request is EmbedRequest embedReq && embedReq.Result != null)
            {
                version = embedReq.Result.version;
                name = embedReq.Result.name;
            }

            PackageJobManager.CompleteJob(jobId, true, version: version, name: name);
        }

        private static string GetManifestPath()
        {
            return Path.Combine(Application.dataPath, "..", "Packages", "manifest.json");
        }

        private static string TruncateDescription(string description, int maxLength = 200)
        {
            if (string.IsNullOrEmpty(description) || description.Length <= maxLength)
                return description;

            return description.Substring(0, maxLength) + "...";
        }

        private static (bool isValid, string warning) ValidatePackageIdentifier(string package)
        {
            if (string.IsNullOrWhiteSpace(package))
                return (false, "Package identifier cannot be empty.");

            // Git URLs — allow but warn
            if (package.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                package.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                package.StartsWith("git://", StringComparison.OrdinalIgnoreCase) ||
                package.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase) ||
                package.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            {
                return (true,
                    $"Installing from git URL. Ensure this is a trusted source — git packages execute code on import.");
            }

            // File paths — allow but warn
            if (package.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                return (true,
                    $"Installing from local path. Ensure this path contains trusted package code.");
            }

            // Normal package ID: strip version suffix for validation
            string name = package.Contains('@') ? package.Substring(0, package.IndexOf('@')) : package;
            if (!Regex.IsMatch(name, @"^[a-zA-Z][a-zA-Z0-9._-]*(\.[a-zA-Z0-9._-]+)+$"))
            {
                return (false,
                    $"'{package}' is not a valid package identifier. Expected format: com.company.package or com.company.package@version.");
            }

            return (true, null);
        }

        private static string[] GetDependentPackages(string packageName)
        {
            try
            {
                string name = PackageJobManager.ExtractPackageName(packageName);

                var allPackages = PackageInfo.GetAllRegisteredPackages();
                return allPackages
                    .Where(pkg => pkg.dependencies.Any(d =>
                        string.Equals(d.name, name, StringComparison.OrdinalIgnoreCase)))
                    .Select(pkg => pkg.name)
                    .ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }
    }
}
