using System;
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
    /// Status / cancel / list_providers are shared across the generate_* tools via
    /// <see cref="AssetGenToolHelpers"/>.
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
                    case "status": return AssetGenToolHelpers.Status(p, "3D model", 3.0);
                    case "cancel": return AssetGenToolHelpers.Cancel(p);
                    case "list_providers": return AssetGenToolHelpers.ListProviders("model");
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

            // Empty -> GUI-selected model -> catalog default. Null still reaches the adapter's own
            // default (Tripo ModelVersion / Meshy meshy-6).
            string model = AssetGenModelCatalog.ResolveModel("model", provider, p.Get("model"));

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
                Model = model,
                Name = p.Get("name"),
                OutputFolder = p.Get("outputFolder"),
            };
            if (!AssetGenPaths.NormalizeOutputFolder(req.OutputFolder, out req.OutputFolder, out string outputErr))
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
    }
}
