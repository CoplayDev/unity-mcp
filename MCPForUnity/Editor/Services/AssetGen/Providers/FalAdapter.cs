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
    /// fal.ai image provider via the queue API. Submits to queue.fal.run/{model} (auth header
    /// "Authorization: Key &lt;key&gt;"), polls the request's status_url, then fetches the result and
    /// returns the first image URL for the job manager to download.
    /// </summary>
    public sealed class FalAdapter : IImageProviderAdapter
    {
        private const string QueueBase = "https://queue.fal.run/";
        private const string QueueHost = "queue.fal.run";
        // Bundled bootstrap default. The shared live catalog may remove it or offer newer models.
        // internal so the model catalog references it directly (single source of truth, drift-guarded).
        internal const string DefaultModel = "fal-ai/flux-2";

        public string Id => "fal";

        public async Task<string> SubmitAsync(ImageGenRequest req, string apiKey, IHttpTransport http, CancellationToken ct)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));
            if (http == null) throw new ArgumentNullException(nameof(http));

            string model = string.IsNullOrEmpty(req.Model) ? DefaultModel : req.Model;
            bool image = string.Equals(req.Mode, "image", StringComparison.OrdinalIgnoreCase)
                         && (!string.IsNullOrEmpty(req.ImageUrl) || !string.IsNullOrEmpty(req.ImagePath));

            ModelEntry entry = req.CatalogEntry ?? AssetGenModelCatalog.Find(model);
            var body = new JObject();
            if (entry == null || entry.PromptField != null) body[entry?.PromptField ?? "prompt"] = req.Prompt ?? string.Empty;
            if (entry == null || (image ? entry.EditSupportsNumImages : entry.SupportsNumImages)) body["num_images"] = 1;
            string outputFormat = image ? entry?.EditOutputFormat : entry?.OutputFormat;
            if (outputFormat != null) body["output_format"] = outputFormat;
            string url;
            if (image)
            {
                // image→image / editing lives on the model's /edit endpoint and takes an image_urls
                // array; each entry accepts a hosted URL or an inline base64 data URI (local image_path).
                if (entry?.FromRefresh == true && string.IsNullOrEmpty(entry.EditModelId))
                    throw new Exception($"Model '{model}' has no verified image editing endpoint.");
                url = QueueBase + (entry?.EditModelId ?? model + "/edit");
                string imageRef = !string.IsNullOrEmpty(req.ImageUrl) ? req.ImageUrl : LocalImage.ToDataUri(req.ImagePath);
                body[entry?.ImageInputField ?? "image_urls"] = entry?.ImageInputIsArray != false ? (JToken)new JArray(imageRef) : (JToken)imageRef;
            }
            else
            {
                url = QueueBase + model;
            }
            // Forward explicit output dimensions for text→image only; fal's image_size accepts a
            // {width,height} object. (/edit derives size from the source image and may reject it.
            // FLUX has no transparency param — transparent backgrounds aren't a generation-time option.)
            if (!image && req.Width > 0 && req.Height > 0 && entry?.SupportsImageSize == false)
                throw new Exception($"Model '{model}' does not support width/height image_size parameters.");
            if (!image && req.Width > 0 && req.Height > 0)
                body["image_size"] = new JObject { ["width"] = req.Width, ["height"] = req.Height };

            return await SubmitQueueAsync(body, url.Substring(QueueBase.Length), apiKey, http, ct);
        }

        internal static async Task<string> SubmitQueueAsync(JObject body, string model, string apiKey, IHttpTransport http, CancellationToken ct)
        {
            if (!FalModelSchema.SafeId(model)) throw new InvalidOperationException("Invalid fal model ID.");
            string url = QueueBase + model;

            ProviderHttp.RequireHost(url, QueueHost, apiKey, "fal submit");

            var spec = new HttpRequestSpec
            {
                Method = "POST",
                Url = url,
                ContentType = "application/json",
                Body = Encoding.UTF8.GetBytes(body.ToString(Formatting.None))
            };
            spec.Headers["Authorization"] = "Key " + apiKey;

            HttpResult res = await http.SendAsync(spec, ct);
            JObject json = ParseOk(res, apiKey, "submit");

            // Prefer response_url; fall back to building it from request_id.
            string responseUrl = json["response_url"]?.ToString();
            if (string.IsNullOrEmpty(responseUrl))
            {
                string requestId = json["request_id"]?.ToString();
                if (string.IsNullOrEmpty(requestId))
                    throw new Exception(SecretRedactor.Scrub("fal submit returned no request_id: " + ProviderHttp.Truncate(res?.Text), apiKey));
                // Queue request URLs are namespaced by owner/app without the action sub-path,
                // so build from the base model id (not `url`, which may end in /edit).
                responseUrl = QueueBase + string.Join("/", model.Split('/'), 0, 2) + "/requests/" + Uri.EscapeDataString(requestId);
            }
            // The response_url is provider-controlled; refuse to later attach the key to any host
            // other than the fal queue.
            ProviderHttp.RequireHost(responseUrl, QueueHost, apiKey, "fal submit response_url");
            return responseUrl;
        }

        public Task<ProviderPollResult> PollAsync(string providerJobId, string apiKey, IHttpTransport http, CancellationToken ct)
            => PollQueueAsync(providerJobId, apiKey, http, ct, json =>
            {
                var file = json["images"]?[0] ?? json["image"];
                return new ProviderPollResult
                {
                    DownloadUrl = ExtractImageUrl(json),
                    ResultExt = ImageResultFormat.FromMetadata((string)file?["content_type"], (string)file?["url"]),
                };
            });

        internal static async Task<ProviderPollResult> PollQueueAsync(string providerJobId, string apiKey, IHttpTransport http, CancellationToken ct, Func<JObject, ProviderPollResult> extract)
        {
            if (string.IsNullOrEmpty(providerJobId)) throw new ArgumentNullException(nameof(providerJobId));
            string responseUrl = providerJobId;
            // providerJobId is provider-supplied (the submit-time response_url). Re-validate before
            // attaching the key so a poisoned URL can never exfiltrate it.
            ProviderHttp.RequireHost(responseUrl, QueueHost, apiKey, "fal poll");

            var statusSpec = new HttpRequestSpec { Method = "GET", Url = responseUrl + "/status" };
            statusSpec.Headers["Authorization"] = "Key " + apiKey;
            HttpResult statusRes = await http.SendAsync(statusSpec, ct);
            JObject statusJson = ParseOk(statusRes, apiKey, "status");

            string status = (statusJson["status"]?.ToString() ?? string.Empty).ToUpperInvariant();
            var result = new ProviderPollResult();
            switch (status)
            {
                case "COMPLETED":
                case "OK":
                    result.State = ProviderPollState.Succeeded;
                    break;
                case "IN_PROGRESS":
                    result.State = ProviderPollState.Running;
                    return result;
                case "IN_QUEUE":
                    result.State = ProviderPollState.Queued;
                    return result;
                case "ERROR":
                case "FAILED":
                    result.State = ProviderPollState.Failed;
                    result.Error = SecretRedactor.Scrub(statusJson["error"]?.ToString() ?? "fal task failed.", apiKey);
                    return result;
                default:
                    // An unmapped terminal status would otherwise poll until the 600s job timeout —
                    // fail fast instead.
                    result.State = ProviderPollState.Failed;
                    result.Error = SecretRedactor.Scrub($"fal returned an unexpected status '{status}'.", apiKey);
                    return result;
            }

            // Completed: fetch the result payload and extract the first image URL.
            var resultSpec = new HttpRequestSpec { Method = "GET", Url = responseUrl };
            resultSpec.Headers["Authorization"] = "Key " + apiKey;
            HttpResult resultRes = await http.SendAsync(resultSpec, ct);
            JObject resultJson = ParseOk(resultRes, apiKey, "result");

            result = extract(resultJson);
            result.State = ProviderPollState.Succeeded;
            result.Progress = 1f;
            if (string.IsNullOrEmpty(result.DownloadUrl))
            {
                result.State = ProviderPollState.Failed;
                result.Error = "fal completed but no result URL was present in the result.";
            }
            return result;
        }

        private static string ExtractImageUrl(JObject result)
        {
            JToken images = result["images"];
            if (images is JArray arr && arr.Count > 0)
            {
                string u = arr[0]?["url"]?.ToString();
                if (!string.IsNullOrEmpty(u)) return u;
            }
            string single = result["image"]?["url"]?.ToString();
            return string.IsNullOrEmpty(single) ? null : single;
        }

        private static JObject ParseOk(HttpResult res, string apiKey, string phase)
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
                string detail = json?["detail"]?.ToString() ?? json?["error"]?.ToString() ?? ProviderHttp.Truncate(text);
                throw new Exception(SecretRedactor.Scrub($"fal {phase} failed (status={res?.Status}): {detail}", apiKey));
            }
            return json ?? new JObject();
        }
    }
}
