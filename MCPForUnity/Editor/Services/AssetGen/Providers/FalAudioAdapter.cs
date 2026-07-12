using System;
using System.IO;
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
    /// fal.ai audio provider via the queue API. One adapter fronts every v1 audio model
    /// (stable-audio-25, cassetteai/*, lyria2); the model id in <see cref="AudioGenRequest.Model"/>
    /// selects the endpoint. Submits to queue.fal.run/{model} (auth header
    /// "Authorization: Key &lt;key&gt;"), polls status, then returns the result audio URL for the job
    /// manager to download. Reuses the single existing "fal" secure key.
    /// </summary>
    public sealed class FalAudioAdapter : IAudioProviderAdapter
    {
        private const string QueueBase = "https://queue.fal.run/";
        // Stable Audio 2.5: music + SFX in one model, up to ~190s. The catalog default.
        // internal so the model catalog references it directly (single source of truth, drift-guarded).
        internal const string DefaultModel = "fal-ai/stable-audio-25/text-to-audio";

        public string Id => "fal";

        public async Task<string> SubmitAsync(AudioGenRequest req, string apiKey, IHttpTransport http, CancellationToken ct)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));
            if (http == null) throw new ArgumentNullException(nameof(http));

            string model = string.IsNullOrEmpty(req.Model) ? DefaultModel : req.Model;
            string url = QueueBase + model;

            var spec = new HttpRequestSpec
            {
                Method = "POST",
                Url = url,
                ContentType = "application/json",
                Body = Encoding.UTF8.GetBytes(BuildBody(model, req).ToString(Formatting.None))
            };
            spec.Headers["Authorization"] = "Key " + apiKey;

            HttpResult res = await http.SendAsync(spec, ct);
            JObject json = ParseOk(res, apiKey, "submit");

            string responseUrl = json["response_url"]?.ToString();
            if (string.IsNullOrEmpty(responseUrl))
            {
                string requestId = json["request_id"]?.ToString();
                if (string.IsNullOrEmpty(requestId))
                    throw new Exception(SecretRedactor.Scrub("fal submit returned no request_id: " + ProviderHttp.Truncate(res?.Text), apiKey));
                responseUrl = QueueBase + model + "/requests/" + requestId;
            }
            return responseUrl;
        }

        // Only Stable Audio and CassetteAI SFX have a confirmed duration knob. CassetteAI Music and
        // Lyria 2 ship prompt-only until their body schemas are verified against a live response.
        private static JObject BuildBody(string model, AudioGenRequest req)
        {
            var body = new JObject { ["prompt"] = req.Prompt ?? string.Empty };
            if (req.Duration > 0f)
            {
                if (model.IndexOf("stable-audio-25", StringComparison.OrdinalIgnoreCase) >= 0)
                    body["seconds_total"] = (int)Math.Min(req.Duration, 190f);
                else if (model.IndexOf("sound-effects", StringComparison.OrdinalIgnoreCase) >= 0)
                    body["duration"] = (int)Math.Min(req.Duration, 30f);
            }
            return body;
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

            var resultSpec = new HttpRequestSpec { Method = "GET", Url = responseUrl };
            resultSpec.Headers["Authorization"] = "Key " + apiKey;
            HttpResult resultRes = await http.SendAsync(resultSpec, ct);
            JObject resultJson = ParseOk(resultRes, apiKey, "result");

            string audioUrl = ExtractAudioUrl(resultJson);
            if (string.IsNullOrEmpty(audioUrl))
            {
                result.State = ProviderPollState.Failed;
                result.Error = "fal completed but no audio URL was present in the result.";
                return result;
            }
            result.Progress = 1f;
            result.DownloadUrl = audioUrl;
            // CassetteAI/Lyria return mp3, Stable Audio wav — derive the ext from the result URL.
            result.ResultExt = ExtractExt(audioUrl);
            return result;
        }

        private static string ExtractAudioUrl(JObject result)
        {
            string u = result["audio"]?["url"]?.ToString();
            if (!string.IsNullOrEmpty(u)) return u;
            u = result["audio_file"]?["url"]?.ToString();
            if (!string.IsNullOrEmpty(u)) return u;
            u = result["audio_url"]?.ToString();
            return string.IsNullOrEmpty(u) ? null : u;
        }

        private static string ExtractExt(string url)
        {
            try
            {
                string ext = Path.GetExtension(new Uri(url).AbsolutePath).TrimStart('.').ToLowerInvariant();
                return string.IsNullOrEmpty(ext) ? "wav" : ext;
            }
            catch { return "wav"; }
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
