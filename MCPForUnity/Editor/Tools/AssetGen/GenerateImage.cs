using System;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Security;
using MCPForUnity.Editor.Services.AssetGen;
using MCPForUnity.Editor.Services.AssetGen.Providers;
using Newtonsoft.Json.Linq;

namespace MCPForUnity.Editor.Tools.AssetGen
{
    /// <summary>
    /// 2D image generation via an aggregator (fal.ai / OpenRouter). Triggered here (never from the
    /// GUI); the C# side reads the provider key from the secure store and runs the job. Returns a
    /// job_id immediately; the client polls the `status` action. Status / cancel / list_providers are
    /// shared across the generate_* tools via <see cref="AssetGenToolHelpers"/>.
    /// </summary>
    [McpForUnityTool("generate_image", AutoRegister = false, Group = "asset_gen", RequiresPolling = true, PollAction = "status", MaxPollSeconds = 300)]
    public static class GenerateImage
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
                    case "remove_background":
                        return new ErrorResponse("remove_background is not implemented in this version.");
                    case "status": return AssetGenToolHelpers.Status(p, "Image", 2.0);
                    case "cancel": return AssetGenToolHelpers.Cancel(p);
                    case "list_providers": return AssetGenToolHelpers.ListProviders("image");
                    case "": return new ErrorResponse("'action' parameter is required.");
                    default:
                        return new ErrorResponse($"Unknown action: '{action}'. Supported: generate, remove_background, status, cancel, list_providers.");
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
            AssetGenProviders.Image(provider); // throws NotSupportedException for unknown providers

            if (!SecureKeyStore.Current.Has(provider))
                return new ErrorResponse(AssetGenProviders.MissingKeyMessage(provider));

            // Empty -> GUI-selected model -> catalog default. Null still reaches the adapter's own
            // default (no regression when nothing is selected anywhere).
            string model = AssetGenModelCatalog.ResolveModel("image", provider, p.Get("model"));

            var req = new ImageGenRequest
            {
                Provider = provider,
                Mode = (p.Get("mode", "text") ?? "text").ToLowerInvariant(),
                Prompt = p.Get("prompt"),
                ImagePath = p.Get("imagePath"),
                ImageUrl = p.Get("imageUrl"),
                Model = model,
                Transparent = p.GetBool("transparent", false),
                AsSprite = p.GetBool("asSprite", true),
                Width = p.GetInt("width", 0) ?? 0,
                Height = p.GetInt("height", 0) ?? 0,
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
                if (!LocalImage.ResolveExisting(req.ImagePath, out string absImg, out string imgErr))
                    return new ErrorResponse(imgErr);
                req.ImagePath = absImg;
            }

            AssetGenJob job = AssetGenJobManager.StartImageGeneration(req);
            if (job.State == AssetGenJobState.Failed)
                return new ErrorResponse(job.Error ?? "Failed to start generation.");

            return new PendingResponse(
                $"Image generation started with '{provider}'. Poll the status action with this job_id.",
                pollIntervalSeconds: 2.0,
                data: new { job_id = job.JobId, provider, status = "pending" });
        }
    }
}
