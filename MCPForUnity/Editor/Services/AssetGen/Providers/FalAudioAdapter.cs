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
        private const string QueueHost = "queue.fal.run";
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
            ProviderHttp.RequireHost(url, QueueHost, apiKey, "fal submit");

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
            // The response_url is provider-controlled; refuse to later attach the key to any host
            // other than the fal queue.
            ProviderHttp.RequireHost(responseUrl, QueueHost, apiKey, "fal submit response_url");
            return responseUrl;
        }

        // Duration is catalog-driven: the model's ModelEntry names the request key (seconds_total /
        // duration) and the clamp bounds. A duration-controllable endpoint (e.g. CassetteAI Music)
        // always sends a duration >= 1 — a prompt-only body is rejected with fal 422
        // "duration Field required" — while a non-duration model (Lyria) or an unknown model stays
        // prompt-only.
        private static JObject BuildBody(string model, AudioGenRequest req)
        {
            var body = new JObject { ["prompt"] = req.Prompt ?? string.Empty };

            ModelEntry entry = AssetGenModelCatalog.Find(model);
            if (entry != null && !string.IsNullOrEmpty(entry.DurationField))
            {
                float dur = req.Duration > 0f ? req.Duration : entry.DefaultDurationSeconds;
                float floor = Math.Max(1f, entry.MinDurationSeconds);
                dur = Math.Min(Math.Max(dur, floor), entry.MaxDurationSeconds);
                // Floor (not round) so we never exceed the requested duration, then enforce >= 1.
                int seconds = Math.Max(1, (int)Math.Floor(dur));
                body[entry.DurationField] = seconds;
            }
            return body;
        }

        public async Task<ProviderPollResult> PollAsync(string providerJobId, string apiKey, IHttpTransport http, CancellationToken ct)
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
