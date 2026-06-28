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
        private const string DefaultModel = "fal-ai/flux/dev";

        public string Id => "fal";

        public async Task<string> SubmitAsync(ImageGenRequest req, string apiKey, IHttpTransport http, CancellationToken ct)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));
            if (http == null) throw new ArgumentNullException(nameof(http));

            string model = string.IsNullOrEmpty(req.Model) ? DefaultModel : req.Model;
            var body = new JObject { ["prompt"] = req.Prompt ?? string.Empty, ["num_images"] = 1 };
            if (string.Equals(req.Mode, "image", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(req.ImageUrl))
                body["image_url"] = req.ImageUrl;

            var spec = new HttpRequestSpec
            {
                Method = "POST",
                Url = QueueBase + model,
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
                    throw new Exception(SecretRedactor.Scrub("fal submit returned no request_id: " + Truncate(res?.Text), apiKey));
                responseUrl = QueueBase + model + "/requests/" + requestId;
            }
            return responseUrl;
        }

        public async Task<ProviderPollResult> PollAsync(string providerJobId, string apiKey, IHttpTransport http, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(providerJobId)) throw new ArgumentNullException(nameof(providerJobId));
            string responseUrl = providerJobId;

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
                    result.State = ProviderPollState.Running;
                    return result;
            }

            // Completed: fetch the result payload and extract the first image URL.
            var resultSpec = new HttpRequestSpec { Method = "GET", Url = responseUrl };
            resultSpec.Headers["Authorization"] = "Key " + apiKey;
            HttpResult resultRes = await http.SendAsync(resultSpec, ct);
            JObject resultJson = ParseOk(resultRes, apiKey, "result");

            result.Progress = 1f;
            result.DownloadUrl = ExtractImageUrl(resultJson);
            if (string.IsNullOrEmpty(result.DownloadUrl))
            {
                result.State = ProviderPollState.Failed;
                result.Error = "fal completed but no image URL was present in the result.";
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
            string text = res?.Text;
            if (string.IsNullOrEmpty(text) && res?.Body != null) text = Encoding.UTF8.GetString(res.Body);

            JObject json = null;
            if (!string.IsNullOrEmpty(text))
            {
                try { json = JObject.Parse(text); } catch { /* non-JSON */ }
            }

            bool ok = res != null && (res.IsSuccess || (res.Status >= 200 && res.Status < 300));
            if (!ok)
            {
                string detail = json?["detail"]?.ToString() ?? json?["error"]?.ToString() ?? Truncate(text);
                throw new Exception(SecretRedactor.Scrub($"fal {phase} failed (status={res?.Status}): {detail}", apiKey));
            }
            return json ?? new JObject();
        }

        private static string Truncate(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Length <= 500 ? s : s.Substring(0, 500) + "…";
        }
    }
}
