using System;
using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Security;
using MCPForUnity.Editor.Services.AssetGen;
using MCPForUnity.Editor.Services.AssetGen.Providers;
using Newtonsoft.Json.Linq;

namespace MCPForUnity.Editor.Tools.AssetGen
{
    /// <summary>
    /// Audio generation (SFX / music) via fal.ai. Triggered here (never from the GUI); the C# side
    /// reads the fal key from the secure store and runs the job. Returns a job_id immediately; the
    /// client polls the `status` action. When `model` is omitted it falls back to the model selected
    /// in the Asset Generation tab, then the catalog default.
    /// </summary>
    [McpForUnityTool("generate_audio", AutoRegister = false, Group = "asset_gen", RequiresPolling = true, PollAction = "status", MaxPollSeconds = 600)]
    public static class GenerateAudio
    {
        public static object HandleCommand(JObject @params)
        {
            if (@params == null) return new ErrorResponse("Parameters cannot be null.");
            var p = new ToolParams(@params);
            string action = (p.Get("action") ?? string.Empty).ToLowerInvariant();
            try
            {
                switch (action)
                {
                    case "generate": return Generate(p);
                    case "status": return Status(p);
                    case "cancel": return Cancel(p);
                    case "list_providers": return ListProviders();
                    case "": return new ErrorResponse("'action' parameter is required.");
                    default:
                        return new ErrorResponse($"Unknown action: '{action}'. Supported: generate, status, cancel, list_providers.");
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

        private static object Generate(ToolParams p)
        {
            string provider = (p.Get("provider", "fal") ?? "fal").ToLowerInvariant();
            AssetGenProviders.Audio(provider); // throws NotSupportedException for unknown providers

            if (!SecureKeyStore.Current.Has(provider))
                return new ErrorResponse(AssetGenProviders.MissingKeyMessage(provider));

            string prompt = p.Get("prompt");
            if (string.IsNullOrWhiteSpace(prompt))
                return new ErrorResponse("'prompt' is required for audio generation.");

            // Empty -> GUI-selected model -> catalog default. A null model reaches the adapter's own
            // default; a resolved id is passed through verbatim (the catalog default equals the
            // adapter constant, so an omitted model is a no-op either way).
            string model = AssetGenModelCatalog.ResolveModel("audio", provider, p.Get("model"));

            var req = new AudioGenRequest
            {
                Provider = provider,
                Model = model,
                Prompt = prompt,
                Duration = p.GetFloat("duration", 0f) ?? 0f,
                Name = p.Get("name"),
                OutputFolder = p.Get("outputFolder"),
            };
            if (!NormalizeOutputFolder(req.OutputFolder, out req.OutputFolder, out string outputErr))
                return new ErrorResponse(outputErr);

            AssetGenJob job = AssetGenJobManager.StartAudioGeneration(req);
            if (job.State == AssetGenJobState.Failed)
                return new ErrorResponse(job.Error ?? "Failed to start generation.");

            return new PendingResponse(
                $"Audio generation started with '{provider}'. Poll the status action with this job_id.",
                pollIntervalSeconds: 3.0,
                data: new { job_id = job.JobId, provider, status = "pending" });
        }

        private static bool NormalizeOutputFolder(string outputFolder, out string normalized, out string error)
        {
            normalized = outputFolder;
            error = null;
            if (string.IsNullOrWhiteSpace(outputFolder)) return true;
            if (AssetGenPaths.TryGetAssetsFolder(outputFolder, out normalized)) return true;
            error = "'output_folder' must resolve under the project's Assets folder.";
            return false;
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
                        $"Audio generated: {job.AssetPath}",
                        new { state = "done", asset_path = job.AssetPath, asset_guid = job.AssetGuid, progress = 1f });
                case AssetGenJobState.Failed:
                    return new ErrorResponse(job.Error ?? "Generation failed.", new { state = "failed" });
                case AssetGenJobState.Canceled:
                    return new SuccessResponse("Generation canceled.", new { state = "canceled" });
                default:
                    return new PendingResponse(
                        $"Audio {job.State.ToString().ToLowerInvariant()} ({job.Progress:P0}).",
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
                if (info.Kind != "audio") continue;
                list.Add(new { id = info.Id, kind = info.Kind, configured = info.Configured, capabilities = info.Capabilities });
            }
            return new SuccessResponse($"{list.Count} audio provider(s).", new { providers = list });
        }
    }
}
