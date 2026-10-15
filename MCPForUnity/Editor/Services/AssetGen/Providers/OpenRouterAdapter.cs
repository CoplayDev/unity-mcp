using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MCPForUnity.Editor.Security;
using MCPForUnity.Editor.Services.AssetGen.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MCPForUnity.Editor.Services.AssetGen.Providers
{
    /// <summary>
    /// OpenRouter's synchronous Image API with verified per-endpoint capabilities.
    /// Legacy direct callers without a catalog profile retain the chat response contract. The
    /// image is returned inline, so the
    /// work happens in <see cref="SubmitAsync"/> and <see cref="PollAsync"/> returns it immediately.
    /// One adapter instance handles a single job (the job manager captures it for submit+poll).
    /// </summary>
    public sealed class OpenRouterAdapter : IImageProviderAdapter
    {
        private const string Endpoint = "https://openrouter.ai/api/v1/chat/completions";
        // internal so the model catalog references it directly (single source of truth).
        internal const string DefaultModel = "google/gemini-2.5-flash-image";

        public string Id => "openrouter";

        private byte[] _inlineData;
        private string _downloadUrl;
        private string _error;
        private string _resultExt;

        public async Task<string> SubmitAsync(ImageGenRequest req, string apiKey, IHttpTransport http, CancellationToken ct)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));
            if (http == null) throw new ArgumentNullException(nameof(http));
            _inlineData = null; _downloadUrl = null; _error = null; _resultExt = null;

            string model = string.IsNullOrEmpty(req.Model) ? DefaultModel : req.Model;

            // image->image: attach the reference image as an image_url content part alongside the
            // text prompt (OpenRouter content-array form). image_url.url takes an http(s) URL or a
            // base64 data URI. Plain text->image uses a string content.
            bool image = string.Equals(req.Mode, "image", StringComparison.OrdinalIgnoreCase)
                         && (!string.IsNullOrEmpty(req.ImageUrl) || !string.IsNullOrEmpty(req.ImagePath));
            // image_url.url accepts a hosted URL or an inline base64 data URI (for a local image_path).
            string imageRef = image
                ? (!string.IsNullOrEmpty(req.ImageUrl) ? req.ImageUrl : LocalImage.ToDataUri(req.ImagePath))
                : null;
            JToken content = image
                ? new JArray(
                    new JObject { ["type"] = "text", ["text"] = req.Prompt ?? string.Empty },
                    new JObject { ["type"] = "image_url", ["image_url"] = new JObject { ["url"] = imageRef } })
                : (JToken)(req.Prompt ?? string.Empty);

            var body = new JObject
            {
                ["model"] = model,
                ["modalities"] = new JArray("image", "text"),
                ["messages"] = new JArray(new JObject
                {
                    ["role"] = "user",
                    ["content"] = content
                })
            };
            bool imageApi = req.CatalogEntry?.RouterProviderTag != null;
            if (imageApi)
            {
                var profile = req.CatalogEntry;
                body = new JObject
                {
                    ["model"] = model, ["prompt"] = req.Prompt ?? "",
                    ["provider"] = new JObject { ["only"] = new JArray(profile.RouterProviderTag), ["allow_fallbacks"] = false },
                };
                if (image) body["input_references"] = new JArray(new JObject { ["type"] = "image_url", ["image_url"] = new JObject { ["url"] = imageRef } });
                if (profile.OutputFormat != null) body["output_format"] = profile.OutputFormat;
                if (req.Width > 0 && req.Height > 0)
                {
                    if (profile.RouterParameters?["size"] == null) throw new InvalidOperationException("This OpenRouter endpoint does not accept explicit pixel dimensions.");
                    body["size"] = req.Width + "x" + req.Height;
                }
            }

            var spec = new HttpRequestSpec
            {
                Method = "POST",
                Url = imageApi ? "https://openrouter.ai/api/v1/images" : Endpoint,
                ContentType = "application/json",
                Body = Encoding.UTF8.GetBytes(body.ToString(Formatting.None))
            };
            spec.Headers["Authorization"] = "Bearer " + apiKey;

            HttpResult res = await http.SendAsync(spec, ct);
            JObject json = ParseOk(res, apiKey);
            if (imageApi)
            {
                var file = json["data"]?[0];
                _resultExt = ImageResultFormat.FromMetadata((string)file?["media_type"]);
                try { _inlineData = Convert.FromBase64String((string)file?["b64_json"] ?? ""); }
                catch { _error = "OpenRouter returned invalid image bytes."; }
                if (_inlineData?.Length == 0) _error = "OpenRouter returned no image.";
                if (_resultExt == "svg" || _resultExt == "webp") _error = "This Unity importer requires PNG or JPEG output.";
                return "ready";
            }

            string url = ExtractImageUrl(json);
            if (string.IsNullOrEmpty(url))
            {
                _error = "OpenRouter returned no image. The selected model may not support image output.";
                return "ready";
            }

            if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                int comma = url.IndexOf("base64,", StringComparison.OrdinalIgnoreCase);
                if (comma < 0) { _error = "OpenRouter returned an unrecognized image payload."; return "ready"; }
                try { _inlineData = Convert.FromBase64String(url.Substring(comma + "base64,".Length)); }
                catch { _error = "OpenRouter image was not valid base64."; }
                _resultExt = ImageResultFormat.FromMetadata(url.Substring(5, Math.Max(0, url.IndexOf(';') - 5)));
            }
            else
            {
                _downloadUrl = url;
                _resultExt = ImageResultFormat.FromMetadata(null, url);
            }
            return "ready";
        }

        public Task<ProviderPollResult> PollAsync(string providerJobId, string apiKey, IHttpTransport http, CancellationToken ct)
        {
            var result = new ProviderPollResult { Progress = 1f };
            if (!string.IsNullOrEmpty(_error) || (_inlineData == null && string.IsNullOrEmpty(_downloadUrl)))
            {
                result.State = ProviderPollState.Failed;
                result.Error = _error ?? "OpenRouter produced no image.";
            }
            else
            {
                result.State = ProviderPollState.Succeeded;
                result.InlineData = _inlineData;
                result.DownloadUrl = _downloadUrl;
                result.ResultExt = _resultExt;
            }
            return Task.FromResult(result);
        }

        private static string ExtractImageUrl(JObject json)
        {
            JToken message = json["choices"]?[0]?["message"];
            if (message == null) return null;

            // Preferred: message.images[].image_url.url
            if (message["images"] is JArray imgs && imgs.Count > 0)
            {
                string u = imgs[0]?["image_url"]?["url"]?.ToString() ?? imgs[0]?["url"]?.ToString();
                if (!string.IsNullOrEmpty(u)) return u;
            }
            // Fallback: a content array with image_url parts
            if (message["content"] is JArray parts)
            {
                foreach (JToken part in parts)
                {
                    string u = part?["image_url"]?["url"]?.ToString();
                    if (!string.IsNullOrEmpty(u)) return u;
                }
            }
            return null;
        }

        private static JObject ParseOk(HttpResult res, string apiKey)
        {
            string text = ProviderHttp.BodyText(res);

            JObject json = null;
            if (!string.IsNullOrEmpty(text))
            {
                try { json = JObject.Parse(text); } catch { /* non-JSON */ }
            }

            bool ok = res?.Ok == true;
            if (!ok)
            {
                string detail = json?["error"]?["message"]?.ToString() ?? json?["error"]?.ToString()
                                ?? ProviderHttp.Truncate(text);
                throw new Exception(SecretRedactor.Scrub($"OpenRouter request failed (status={res?.Status}): {detail}", apiKey));
            }
            return json ?? new JObject();
        }
    }
}
