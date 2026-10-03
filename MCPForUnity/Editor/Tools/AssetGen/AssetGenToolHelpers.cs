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
            if (forceRefresh && !providers.Any(info => info.Id == "fal"))
                return new ErrorResponse("Live model refresh currently supports fal image and audio. Other providers use the bundled catalog.");
            var models = new List<object>();
            var catalogs = new List<object>();
            foreach (var info in providers)
            {
                bool live = info.Id == "fal" && (kind == "image" || kind == "audio");
                if (live) _ = FalModelCatalog.RefreshAsync(kind, forceRefresh);
                foreach (var model in AssetGenModelCatalog.ForProvider(info.Id, kind))
                    models.Add(new
                    {
                        id = model.Id, label = model.Label, provider = model.Provider, kind = model.Kind,
                        use_case = model.UseCase, verified_at = model.VerifiedAt,
                        status = model.FromRefresh ? "verified" : "unverified",
                        capabilities = kind == "model" ? info.Capabilities : kind == "image" && (!model.FromRefresh || !string.IsNullOrEmpty(model.EditModelId))
                            ? new[] { "text", "image" } : new[] { "text" },
                        max_duration_seconds = model.MaxDurationSeconds, license_type = model.LicenseType,
                    });
                catalogs.Add(new
                {
                    provider = info.Id, source = live ? FalModelCatalog.Source(kind) : "bundled",
                    last_verified = live ? FalModelCatalog.VerifiedAt(kind) : null,
                    stale = !live || FalModelCatalog.IsStale(kind),
                    refreshing = live && FalModelCatalog.IsRefreshing(kind),
                    refresh_error = live ? FalModelCatalog.LastError(kind) : null,
                });
            }
            return new SuccessResponse("Model catalog. If refreshing is true, call list_models again after the refresh completes. Bundled entries are unverified; fal endpoints are rechecked before generation.",
                new { models, catalogs });
        }
    }
}
