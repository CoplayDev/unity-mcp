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
    /// MiniMax 2D image provider via the synchronous <c>/v1/image_generation</c> endpoint. Text→image
    /// submits a prompt; image→image attaches a <c>subject_reference</c> (a hosted URL or an inline
    /// base64 data URI for a local image_path). The image is returned inline (base64) or as a URL the
    /// job manager downloads, so all work happens in <see cref="SubmitAsync"/> and
    /// <see cref="PollAsync"/> returns the captured result immediately. One adapter instance handles a
    /// single job (the job manager captures it for submit+poll).
    /// </summary>
    public sealed class MiniMaxImageAdapter : IImageProviderAdapter
    {
        private const string Endpoint = "https://api.minimax.io/v1/image_generation";
        private const string Host = "api.minimax.io";
        // internal so the model catalog references it directly (single source of truth, drift-guarded).
        internal const string DefaultModel = "image-01";

        public string Id => "minimax";

        private byte[] _inlineData;
        private string _downloadUrl;
        private string _error;

        public async Task<string> SubmitAsync(ImageGenRequest req, string apiKey, IHttpTransport http, CancellationToken ct)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));
            if (http == null) throw new ArgumentNullException(nameof(http));

            string model = string.IsNullOrEmpty(req.Model) ? DefaultModel : req.Model;

            var body = new JObject
            {
                ["model"] = model,
                ["prompt"] = req.Prompt ?? string.Empty
            };

            // image→image: attach the reference image as a subject_reference entry. image_file accepts
            // a hosted URL or an inline base64 data URI (for a local image_path). Plain text→image
            // sends prompt only.
            bool image = string.Equals(req.Mode, "image", StringComparison.OrdinalIgnoreCase)
                         && (!string.IsNullOrEmpty(req.ImageUrl) || !string.IsNullOrEmpty(req.ImagePath));
            if (image)
            {
                string imageRef = !string.IsNullOrEmpty(req.ImageUrl) ? req.ImageUrl : LocalImage.ToDataUri(req.ImagePath);
                body["subject_reference"] = new JArray(
                    new JObject { ["type"] = "character", ["image_file"] = imageRef });
            }

            // Forward explicit output dimensions for text→image only; the provider accepts a
            // {width,height} pair. (image→image derives size from the subject reference.)
            if (!image && req.Width > 0 && req.Height > 0)
            {
                body["width"] = req.Width;
                body["height"] = req.Height;
            }

            ProviderHttp.RequireHost(Endpoint, Host, apiKey, "minimax submit");

            var spec = new HttpRequestSpec
            {
                Method = "POST",
                Url = Endpoint,
                ContentType = "application/json",
                Body = Encoding.UTF8.GetBytes(body.ToString(Formatting.None))
            };
            spec.Headers["Authorization"] = "Bearer " + apiKey;

            HttpResult res = await http.SendAsync(spec, ct);
            JObject json = ParseOk(res, apiKey);

            // Prefer a URL result (default response_format=url); fall back to inline base64.
            string url = ExtractImageUrl(json);
            if (!string.IsNullOrEmpty(url))
            {
                _downloadUrl = url;
            }
            else
            {
                string b64 = ExtractImageBase64(json);
                if (!string.IsNullOrEmpty(b64))
                {
                    try { _inlineData = Convert.FromBase64String(b64); }
                    catch { _error = "MiniMax returned an image payload that was not valid base64."; }
                }
                else
                {
                    _error = "MiniMax returned no image. The selected model may not support image output.";
                }
            }
            return "ready";
        }

        public Task<ProviderPollResult> PollAsync(string providerJobId, string apiKey, IHttpTransport http, CancellationToken ct)
        {
            var result = new ProviderPollResult { Progress = 1f };
            if (!string.IsNullOrEmpty(_error) || (_inlineData == null && string.IsNullOrEmpty(_downloadUrl)))
            {
                result.State = ProviderPollState.Failed;
                result.Error = _error ?? "MiniMax produced no image.";
            }
            else
            {
                result.State = ProviderPollState.Succeeded;
                result.InlineData = _inlineData;
                result.DownloadUrl = _downloadUrl;
            }
            return Task.FromResult(result);
        }

        private static string ExtractImageUrl(JObject json)
            => (json["data"]?["image_urls"] as JArray)?[0]?.ToString();

        private static string ExtractImageBase64(JObject json)
            => (json["data"]?["image_base64"] as JArray)?[0]?.ToString();

        private static JObject ParseOk(HttpResult res, string apiKey)
        {
            string text = ProviderHttp.BodyText(res);

            JObject json = null;
            if (!string.IsNullOrEmpty(text))
            {
                try { json = JObject.Parse(text); } catch { /* non-JSON */ }
            }

            bool ok = res?.Ok == true;
            // MiniMax signals failure with a non-zero base_resp.status_code even on HTTP 200.
            int status = json?["base_resp"]?["status_code"]?.Type == JTokenType.Integer
                ? (int)json["base_resp"]["status_code"]
                : 0;
            if (!ok || status != 0)
            {
                string detail = json?["base_resp"]?["status_msg"]?.ToString()
                                ?? json?["error"]?["message"]?.ToString()
                                ?? json?["error"]?.ToString()
                                ?? ProviderHttp.Truncate(text);
                throw new Exception(SecretRedactor.Scrub(
                    $"MiniMax request failed (status={res?.Status}, base_resp.status_code={status}): {detail}", apiKey));
            }
            return json ?? new JObject();
        }
    }
}
