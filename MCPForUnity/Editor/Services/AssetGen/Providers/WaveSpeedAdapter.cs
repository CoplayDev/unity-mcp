using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MCPForUnity.Editor.Security;
using MCPForUnity.Editor.Services.AssetGen.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MCPForUnity.Editor.Services.AssetGen.Providers
{
    /// <summary>
    /// WaveSpeedAI image provider. Submits the model input to api.wavespeed.ai/api/v3/{model}
    /// ("Authorization: Bearer &lt;key&gt;"), then polls /api/v3/predictions/{id}/result and returns the
    /// first output URL. Every response is a {code, message, data} envelope; a non-200 code is an
    /// error even when the HTTP status is 200.
    /// </summary>
    public sealed class WaveSpeedAdapter : IImageProviderAdapter
    {
        private const string ApiBase = "https://api.wavespeed.ai/api/v3/";
        private const string ApiHost = "api.wavespeed.ai";
        // internal so the model catalog references them directly (single source of truth).
        internal const string DefaultModel = "bytedance/seedream-v5.5";
        internal const string DefaultEditModel = "bytedance/seedream-v5.5/edit";

        private static readonly Regex SafeModelId = new Regex(@"^[A-Za-z0-9][A-Za-z0-9._-]*(/[A-Za-z0-9][A-Za-z0-9._-]*)+$");
        private static readonly Regex SafePredictionId = new Regex(@"^[A-Za-z0-9_-]+$");

        public string Id => "wavespeed";

        public async Task<string> SubmitAsync(ImageGenRequest req, string apiKey, IHttpTransport http, CancellationToken ct)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));
            if (http == null) throw new ArgumentNullException(nameof(http));

            bool image = string.Equals(req.Mode, "image", StringComparison.OrdinalIgnoreCase)
                         && (!string.IsNullOrEmpty(req.ImageUrl) || !string.IsNullOrEmpty(req.ImagePath));
            string model = string.IsNullOrEmpty(req.Model) ? DefaultModel : req.Model;
            // The default text model has a sibling /edit endpoint; any other model id is used as given.
            if (image && model == DefaultModel) model = DefaultEditModel;
            if (!SafeModelId.IsMatch(model)) throw new InvalidOperationException($"Invalid WaveSpeedAI model ID '{model}'.");

            var body = new JObject { ["prompt"] = req.Prompt ?? string.Empty };
            if (image)
            {
                // Edit models take an images array; each entry is a hosted URL or a base64 data URI.
                string imageRef = !string.IsNullOrEmpty(req.ImageUrl) ? req.ImageUrl : LocalImage.ToDataUri(req.ImagePath);
                body["images"] = new JArray(imageRef);
            }
            else if (req.Width > 0 && req.Height > 0)
            {
                // Image models take an aspect ratio rather than pixel dimensions.
                int d = Gcd(req.Width, req.Height);
                body["aspect_ratio"] = (req.Width / d) + ":" + (req.Height / d);
            }

            string url = ApiBase + model;
            ProviderHttp.RequireHost(url, ApiHost, apiKey, "wavespeed submit");
            var spec = new HttpRequestSpec
            {
                Method = "POST",
                Url = url,
                ContentType = "application/json",
                Body = Encoding.UTF8.GetBytes(body.ToString(Formatting.None))
            };
            spec.Headers["Authorization"] = "Bearer " + apiKey;

            JObject data = ParseData(await http.SendAsync(spec, ct), apiKey, "submit");
            string id = data["id"]?.ToString();
            if (string.IsNullOrEmpty(id) || !SafePredictionId.IsMatch(id))
                throw new Exception(SecretRedactor.Scrub("wavespeed submit returned no valid prediction id: " + ProviderHttp.Truncate(data.ToString(Formatting.None)), apiKey));
            return id;
        }

        public async Task<ProviderPollResult> PollAsync(string providerJobId, string apiKey, IHttpTransport http, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(providerJobId)) throw new ArgumentNullException(nameof(providerJobId));
            if (!SafePredictionId.IsMatch(providerJobId)) throw new InvalidOperationException("Invalid WaveSpeedAI prediction ID.");

            var spec = new HttpRequestSpec { Method = "GET", Url = ApiBase + "predictions/" + providerJobId + "/result" };
            spec.Headers["Authorization"] = "Bearer " + apiKey;
            JObject data = ParseData(await http.SendAsync(spec, ct), apiKey, "poll");

            string status = (data["status"]?.ToString() ?? string.Empty).ToLowerInvariant();
            var result = new ProviderPollResult();
            switch (status)
            {
                case "created":
                    result.State = ProviderPollState.Queued;
                    return result;
                case "processing":
                    result.State = ProviderPollState.Running;
                    return result;
                case "failed":
                    result.State = ProviderPollState.Failed;
                    string error = data["error"]?.ToString();
                    result.Error = SecretRedactor.Scrub(string.IsNullOrEmpty(error) ? "wavespeed prediction failed." : error, apiKey);
                    return result;
                case "completed":
                    string outputUrl = (data["outputs"] as JArray)?.Count > 0 ? data["outputs"][0]?.ToString() : null;
                    if (string.IsNullOrEmpty(outputUrl))
                    {
                        result.State = ProviderPollState.Failed;
                        result.Error = "wavespeed completed but no output URL was present in the result.";
                        return result;
                    }
                    result.State = ProviderPollState.Succeeded;
                    result.Progress = 1f;
                    result.DownloadUrl = outputUrl;
                    result.ResultExt = ImageResultFormat.FromMetadata(null, outputUrl);
                    return result;
                default:
                    // An unmapped status would otherwise poll until the job timeout — fail fast instead.
                    result.State = ProviderPollState.Failed;
                    result.Error = SecretRedactor.Scrub($"wavespeed returned an unexpected status '{status}'.", apiKey);
                    return result;
            }
        }

        private static JObject ParseData(HttpResult res, string apiKey, string phase)
        {
            string text = ProviderHttp.BodyText(res);
            JObject json = null;
            if (!string.IsNullOrEmpty(text))
            {
                try { json = JObject.Parse(text); } catch { /* non-JSON */ }
            }

            int? code = json?["code"]?.Type == JTokenType.Integer ? json["code"].Value<int>() : (int?)null;
            if (res?.Ok != true || (code.HasValue && code.Value != 200))
            {
                string detail = json?["message"]?.ToString() ?? ProviderHttp.Truncate(text);
                throw new Exception(SecretRedactor.Scrub($"wavespeed {phase} failed (status={res?.Status}, code={code?.ToString() ?? "none"}): {detail}", apiKey));
            }
            if (!(json?["data"] is JObject data))
                throw new Exception(SecretRedactor.Scrub($"wavespeed {phase} returned no data: " + ProviderHttp.Truncate(text), apiKey));
            return data;
        }

        private static int Gcd(int a, int b) => b == 0 ? a : Gcd(b, a % b);
    }
}
