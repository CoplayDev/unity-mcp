using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services.AssetGen;
using MCPForUnity.Editor.Services.AssetGen.Providers;

namespace MCPForUnity.Editor.Tools.AssetGen
{
    /// <summary>
    /// Shared action handlers for the generate_* asset tools (audio / image / model). The three tools
    /// differ only in their generate step and a kind label; <c>status</c>, <c>cancel</c>, and
    /// <c>list_providers</c> are identical modulo that label, so they live here once.
    /// </summary>
    internal static class AssetGenToolHelpers
    {
        /// <summary>
        /// Poll a job by id and map its state to a client response. <paramref name="kindLabel"/>
        /// prefixes the human-readable message (e.g. "Audio", "Image", "3D model").
        /// </summary>
        public static object Status(ToolParams p, string kindLabel, double pollIntervalSeconds)
        {
            string jobId = p.Get("job_id");
            if (string.IsNullOrEmpty(jobId)) return new ErrorResponse("'job_id' is required for status.");
            AssetGenJob job = AssetGenJobManager.GetJob(jobId);
            if (job == null) return new ErrorResponse($"No job found with ID '{jobId}'.");

            switch (job.State)
            {
                case AssetGenJobState.Done:
                    return new SuccessResponse(
                        $"{kindLabel} generated: {job.AssetPath}",
                        new { state = "done", asset_path = job.AssetPath, asset_guid = job.AssetGuid, progress = 1f });
                case AssetGenJobState.Failed:
                    return new ErrorResponse(job.Error ?? "Generation failed.", new { state = "failed" });
                case AssetGenJobState.Canceled:
                    return new SuccessResponse("Generation canceled.", new { state = "canceled" });
                default:
                    return new PendingResponse(
                        $"{kindLabel} {job.State.ToString().ToLowerInvariant()} ({job.Progress:P0}).",
                        pollIntervalSeconds: pollIntervalSeconds,
                        data: new { job_id = job.JobId, state = job.State.ToString().ToLowerInvariant(), progress = job.Progress });
            }
        }

        /// <summary>Request cancellation of a job by id.</summary>
        public static object Cancel(ToolParams p)
        {
            string jobId = p.Get("job_id");
            if (string.IsNullOrEmpty(jobId)) return new ErrorResponse("'job_id' is required for cancel.");
            return AssetGenJobManager.Cancel(jobId)
                ? new SuccessResponse($"Cancel requested for job '{jobId}'.")
                : new ErrorResponse($"No cancelable job found with ID '{jobId}'.");
        }

        /// <summary>List the configured providers for a given kind (audio / image / model).</summary>
        public static object ListProviders(string kind)
        {
            var list = new List<object>();
            foreach (ProviderInfo info in AssetGenProviders.List())
            {
                if (info.Kind != kind) continue;
                list.Add(new { id = info.Id, kind = info.Kind, configured = info.Configured, capabilities = info.Capabilities });
            }
            return new SuccessResponse($"{list.Count} {kind} provider(s).", new { providers = list });
        }

        /// <summary>Return the same model snapshot as the panel; refreshes run without blocking.</summary>
        public static object ListModels(ToolParams p, string kind, bool forceRefresh = false)
        {
            string provider = p.Get("provider")?.ToLowerInvariant();
            var providers = AssetGenProviders.List().Where(info => info.Kind == kind && (string.IsNullOrEmpty(provider) || info.Id == provider)).ToList();
            if (providers.Count == 0) return new ErrorResponse($"Unknown {kind} provider '{provider}'.");
            if (forceRefresh && !providers.Any(info => info.Id == "fal" || info.Id == "openrouter" && kind == "image"))
                return new ErrorResponse("Live refresh supports fal and OpenRouter images. Tripo and Meshy use bundled models.");
            string search = p.Get("search") ?? "";
            string mode = p.Get("mode");
            int limit = p.GetInt("limit", 50) ?? 50, offset = p.GetInt("offset", 0) ?? 0;
            if (limit < 1 || limit > 200 || offset < 0) return new ErrorResponse("Use limit=1..200 and offset>=0.");
            var models = new List<object>();
            var catalogs = new List<object>();
            foreach (var info in providers)
            {
                bool fal = info.Id == "fal", router = info.Id == "openrouter" && kind == "image";
                if (fal) _ = FalModelCatalog.RefreshAsync(kind, forceRefresh);
                if (router) _ = OpenRouterModelCatalog.RefreshAsync(forceRefresh);
                foreach (var model in AssetGenModelCatalog.ForProvider(info.Id, kind))
                {
                    var modes = model.Modes ?? (kind == "audio" ? new[] { "text" } : new[] { "text", "image" });
                    if (!string.IsNullOrEmpty(mode) && !modes.Contains(mode)) continue;
                    if ((model.Id + " " + model.Label + " " + model.UseCase).IndexOf(search, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                    models.Add(new
                    {
                        id = model.Id, label = model.Label, provider = model.Provider, kind = model.Kind,
                        use_case = model.UseCase, verified_at = model.VerifiedAt,
                        status = model.VerifiedAt != null ? "verified" : model.FromRefresh ? "discovered" : "unverified",
                        capabilities = modes,
                        max_duration_seconds = model.MaxDurationSeconds, license_type = model.LicenseType,
                    });
                }
                catalogs.Add(new
                {
                    provider = info.Id, source = fal ? FalModelCatalog.Source(kind) : router ? OpenRouterModelCatalog.Source : "bundled",
                    last_checked = fal ? FalModelCatalog.VerifiedAt(kind) : router ? OpenRouterModelCatalog.CheckedAt : null,
                    stale = fal ? FalModelCatalog.IsStale(kind) : !router || OpenRouterModelCatalog.IsStale,
                    refreshing = fal ? FalModelCatalog.IsRefreshing(kind) : router && OpenRouterModelCatalog.IsRefreshing,
                    refresh_error = fal ? FalModelCatalog.LastError(kind) : router ? OpenRouterModelCatalog.LastError : null,
                });
            }
            return new SuccessResponse("Model catalog. Repeat list_models while refreshing. Discovered models require compatibility verification; live endpoints are rechecked before generation.",
                new { models = models.Skip(offset).Take(limit).ToList(), total = models.Count, offset, limit, has_more = offset < models.Count - limit, catalogs });
        }
    }
}
