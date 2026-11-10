using System;
using System.Threading;
using System.Threading.Tasks;
using MCPForUnity.Editor.Services.AssetGen.Http;
using Newtonsoft.Json.Linq;

namespace MCPForUnity.Editor.Services.AssetGen.Providers
{
    public sealed class FalModelAdapter : IModelProviderAdapter
    {
        internal const string DefaultModel = "fal-ai/hunyuan3d-v3/text-to-3d";
        private ModelEntry profile;
        public string Id => "fal";

        public Task<string> SubmitAsync(ModelGenRequest req, string apiKey, IHttpTransport http, CancellationToken ct)
        {
            profile = req.CatalogEntry;
            if (profile == null || profile.VerifiedAt == null) throw new InvalidOperationException("Choose a verified fal 3D model.");
            if (!string.Equals(req.Format ?? "glb", "glb", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("fal 3D generation currently supports GLB output. Choose format='glb'.");
            if (!string.IsNullOrEmpty(req.Tier)) throw new InvalidOperationException("This fal adapter uses the endpoint's default tier.");
            if (!req.Texture && profile.TextureField == null)
                throw new InvalidOperationException("This model does not expose a texture toggle. Choose a model that supports it.");
            var body = new JObject();
            if (profile.PromptField != null) body[profile.PromptField] = req.Prompt ?? "";
            if (profile.TextureField != null) body[profile.TextureField] = req.Texture;
            if (req.Mode == "image")
            {
                string image = !string.IsNullOrEmpty(req.ImageUrl) ? req.ImageUrl : LocalImage.ToDataUri(req.ImagePath);
                body[profile.ImageInputField] = profile.ImageInputIsArray ? (JToken)new JArray(image) : image;
            }
            return FalAdapter.SubmitQueueAsync(body, profile.Id, apiKey, http, ct);
        }

        public Task<ProviderPollResult> PollAsync(string providerJobId, string apiKey, IHttpTransport http, CancellationToken ct)
            => FalAdapter.PollQueueAsync(providerJobId, apiKey, http, ct, json => new ProviderPollResult
            {
                DownloadUrl = (string)json.SelectToken(profile.ModelOutputField + ".url"), ResultExt = "glb",
            });
    }
}
