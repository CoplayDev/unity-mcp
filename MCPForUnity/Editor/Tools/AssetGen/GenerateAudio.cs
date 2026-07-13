using System;
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
    /// in the Asset Generation tab, then the catalog default. Status / cancel / list_providers are
    /// shared across the generate_* tools via <see cref="AssetGenToolHelpers"/>.
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
                    case "status": return AssetGenToolHelpers.Status(p, "Audio", 3.0);
                    case "cancel": return AssetGenToolHelpers.Cancel(p);
                    case "list_providers": return AssetGenToolHelpers.ListProviders("audio");
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
            if (!AssetGenPaths.NormalizeOutputFolder(req.OutputFolder, out req.OutputFolder, out string outputErr))
                return new ErrorResponse(outputErr);

            AssetGenJob job = AssetGenJobManager.StartAudioGeneration(req);
            if (job.State == AssetGenJobState.Failed)
                return new ErrorResponse(job.Error ?? "Failed to start generation.");

            return new PendingResponse(
                $"Audio generation started with '{provider}'. Poll the status action with this job_id.",
                pollIntervalSeconds: 3.0,
                data: new { job_id = job.JobId, provider, status = "pending" });
        }
    }
}
