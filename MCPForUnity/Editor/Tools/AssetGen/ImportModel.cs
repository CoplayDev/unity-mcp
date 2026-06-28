using System;
using System.Collections.Generic;
using System.Threading;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Security;
using MCPForUnity.Editor.Services.AssetGen;
using MCPForUnity.Editor.Services.AssetGen.Http;
using MCPForUnity.Editor.Services.AssetGen.Providers;
using Newtonsoft.Json.Linq;

namespace MCPForUnity.Editor.Tools.AssetGen
{
    /// <summary>
    /// 3D marketplace import (Sketchfab). Search/preview are quick read-only calls run inline;
    /// import downloads the model archive and unpacks it into the project as a long-running job
    /// (returns a job_id; the client polls the `status` action). The provider key is read from
    /// the secure store on the C# side and never transits the bridge.
    /// </summary>
    [McpForUnityTool("import_model", AutoRegister = false, Group = "asset_gen", RequiresPolling = true, PollAction = "status", MaxPollSeconds = 300)]
    public static class ImportModel
    {
        private const string Provider = "sketchfab";

        // Test seam for the inline search/preview calls (import routes through the job manager's
        // own transport seam). Defaults to the production UnityWebRequest transport.
        internal static IHttpTransport TransportOverrideForTests;

        public static object HandleCommand(JObject @params)
        {
            if (@params == null) return new ErrorResponse("Parameters cannot be null.");
            var p = new ToolParams(@params);
            string action = (p.Get("action") ?? string.Empty).ToLowerInvariant();
            try
            {
                switch (action)
                {
                    case "search": return Search(p);
                    case "preview": return Preview(p);
                    case "import": return Import(p);
                    case "status": return Status(p);
                    case "cancel": return Cancel(p);
                    case "list_providers": return ListProviders();
                    case "": return new ErrorResponse("'action' parameter is required.");
                    default:
                        return new ErrorResponse($"Unknown action: '{action}'. Supported: search, preview, import, status, cancel, list_providers.");
                }
            }
            catch (NotSupportedException nse)
            {
                return new ErrorResponse(nse.Message);
            }
            catch (Exception e)
            {
                return new ErrorResponse(SecretRedactor.Scrub(e.Message));
            }
        }

        private static IHttpTransport Transport() => TransportOverrideForTests ?? new UnityWebRequestTransport();

        private static object Search(ToolParams p)
        {
            string query = p.Get("query");
            if (string.IsNullOrWhiteSpace(query)) return new ErrorResponse("'query' is required for search.");
            if (!SecureKeyStore.Current.TryGet(Provider, out string key) || string.IsNullOrEmpty(key))
                return KeyError();

            IMarketplaceProviderAdapter adapter = AssetGenProviders.Marketplace(Provider);
            string results = adapter.SearchAsync(query, key, Transport(), CancellationToken.None).GetAwaiter().GetResult();
            return new SuccessResponse($"Search results for '{query}'.",
                new { provider = Provider, results = ParseOrRaw(results) });
        }

        private static object Preview(ToolParams p)
        {
            string uid = p.Get("uid");
            if (string.IsNullOrWhiteSpace(uid)) return new ErrorResponse("'uid' is required for preview.");
            if (!SecureKeyStore.Current.TryGet(Provider, out string key) || string.IsNullOrEmpty(key))
                return KeyError();

            IMarketplaceProviderAdapter adapter = AssetGenProviders.Marketplace(Provider);
            string preview = adapter.PreviewAsync(uid, key, Transport(), CancellationToken.None).GetAwaiter().GetResult();
            return new SuccessResponse($"Preview for '{uid}'.",
                new { provider = Provider, uid, preview = ParseOrRaw(preview) });
        }

        private static object Import(ToolParams p)
        {
            string uid = p.Get("uid");
            if (string.IsNullOrWhiteSpace(uid)) return new ErrorResponse("'uid' is required for import.");
            AssetGenProviders.Marketplace(Provider); // throws NotSupportedException for unimplemented providers

            float targetSize = p.GetFloat("targetSize", 1f) ?? 1f;
            string name = p.Get("name");
            string outputFolder = p.Get("outputFolder");

            AssetGenJob job = AssetGenJobManager.StartMarketplaceImport(uid, targetSize, name, outputFolder);
            if (job.State == AssetGenJobState.Failed)
                return new ErrorResponse(job.Error ?? "Failed to start import.");

            return new PendingResponse(
                $"Sketchfab import started for '{uid}'. Poll the status action with this job_id.",
                pollIntervalSeconds: 3.0,
                data: new { job_id = job.JobId, provider = Provider, status = "pending" });
        }

        private static object Status(ToolParams p)
        {
            string jobId = p.Get("job_id");
            if (string.IsNullOrEmpty(jobId)) return new ErrorResponse("'job_id' is required for status.");
            AssetGenJob job = AssetGenJobManager.GetJob(jobId);
            if (job == null) return new ErrorResponse($"No job found with ID '{jobId}'.");

            switch (job.State)
            {
                case AssetGenJobState.Done:
                    return new SuccessResponse(
                        $"Import complete: {job.AssetPath}",
                        new { state = "done", asset_path = job.AssetPath, asset_guid = job.AssetGuid, progress = 1f });
                case AssetGenJobState.Failed:
                    return new ErrorResponse(job.Error ?? "Import failed.", new { state = "failed" });
                case AssetGenJobState.Canceled:
                    return new SuccessResponse("Import canceled.", new { state = "canceled" });
                default:
                    return new PendingResponse(
                        $"Import {job.State.ToString().ToLowerInvariant()} ({job.Progress:P0}).",
                        pollIntervalSeconds: 3.0,
                        data: new { job_id = job.JobId, state = job.State.ToString().ToLowerInvariant(), progress = job.Progress });
            }
        }

        private static object Cancel(ToolParams p)
        {
            string jobId = p.Get("job_id");
            if (string.IsNullOrEmpty(jobId)) return new ErrorResponse("'job_id' is required for cancel.");
            return AssetGenJobManager.Cancel(jobId)
                ? new SuccessResponse($"Cancel requested for job '{jobId}'.")
                : new ErrorResponse($"No cancelable job found with ID '{jobId}'.");
        }

        private static object ListProviders()
        {
            var list = new List<object>();
            foreach (ProviderInfo info in AssetGenProviders.List())
            {
                if (info.Kind != "marketplace") continue;
                list.Add(new { id = info.Id, kind = info.Kind, configured = info.Configured, capabilities = info.Capabilities });
            }
            return new SuccessResponse($"{list.Count} provider(s).", new { providers = list });
        }

        private static object KeyError()
            => new ErrorResponse(
                $"No API key configured for '{Provider}'. Add it in the MCP for Unity → Asset Generation tab " +
                $"(or set MCPFORUNITY_{Provider.ToUpperInvariant()}_API_KEY).");

        private static object ParseOrRaw(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try { return JToken.Parse(json); } catch { return json; }
        }
    }
}
