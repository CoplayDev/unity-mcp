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
    /// 3D model generation (Tripo/Meshy). Generation is triggered here (never from
    /// the GUI); the C# side reads the provider key from the secure store and runs the job.
    /// Long-running: returns a job_id immediately and the client polls the `status` action.
    /// </summary>
    [McpForUnityTool("generate_model", AutoRegister = false, Group = "asset_gen", RequiresPolling = true, PollAction = "status", MaxPollSeconds = 300)]
    public static class GenerateModel
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
            string provider = (p.Get("provider", "tripo") ?? "tripo").ToLowerInvariant();
            AssetGenProviders.Model(provider); // throws NotSupportedException for unimplemented providers

            if (!SecureKeyStore.Current.Has(provider))
                return new ErrorResponse(AssetGenProviders.MissingKeyMessage(provider));

            var req = new ModelGenRequest
            {
                Provider = provider,
                Mode = (p.Get("mode", "text") ?? "text").ToLowerInvariant(),
                Prompt = p.Get("prompt"),
                ImagePath = p.Get("imagePath"),
                ImageUrl = p.Get("imageUrl"),
                Format = (p.Get("format", "glb") ?? "glb").ToLowerInvariant(),
                TargetSize = p.GetFloat("targetSize", 1f) ?? 1f,
                Texture = p.GetBool("texture", true),
                Tier = p.Get("tier"),
                Name = p.Get("name"),
                OutputFolder = p.Get("outputFolder"),
            };
            if (!NormalizeOutputFolder(req.OutputFolder, out req.OutputFolder, out string outputErr))
                return new ErrorResponse(outputErr);

            if (req.Mode == "text" && string.IsNullOrWhiteSpace(req.Prompt))
                return new ErrorResponse("'prompt' is required for text mode.");
            if (req.Mode == "image" && string.IsNullOrWhiteSpace(req.ImageUrl))
            {
                if (string.IsNullOrWhiteSpace(req.ImagePath))
                    return new ErrorResponse("image mode requires 'image_url' or 'image_path'.");
                if (provider == "tripo")
                    return new ErrorResponse("Tripo image input requires a hosted 'image_url'; local 'image_path' is not supported for Tripo (use Meshy for local-image→3D).");
                if (!LocalImage.ResolveExisting(req.ImagePath, out string absImg, out string imgErr))
                    return new ErrorResponse(imgErr);
                req.ImagePath = absImg;
            }

            AssetGenJob job = AssetGenJobManager.StartModelGeneration(req);
            if (job.State == AssetGenJobState.Failed)
                return new ErrorResponse(job.Error ?? "Failed to start generation.");

            return new PendingResponse(
                $"3D generation started with '{provider}'. Poll the status action with this job_id.",
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
                        $"Generation complete: {job.AssetPath}",
                        new { state = "done", asset_path = job.AssetPath, asset_guid = job.AssetGuid, progress = 1f });
                case AssetGenJobState.Failed:
                    return new ErrorResponse(job.Error ?? "Generation failed.", new { state = "failed" });
                case AssetGenJobState.Canceled:
                    return new SuccessResponse("Generation canceled.", new { state = "canceled" });
                default:
                    return new PendingResponse(
                        $"Generation {job.State.ToString().ToLowerInvariant()} ({job.Progress:P0}).",
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
                list.Add(new { id = info.Id, kind = info.Kind, configured = info.Configured, capabilities = info.Capabilities });
            return new SuccessResponse($"{list.Count} provider(s).", new { providers = list });
        }
    }
}
