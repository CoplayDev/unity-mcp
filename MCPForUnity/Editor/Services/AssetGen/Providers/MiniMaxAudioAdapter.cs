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
    /// MiniMax audio provider for background-music generation via the (synchronous)
    /// <c>/v1/music_generation</c> endpoint. The prompt drives an instrumental track; the endpoint
    /// returns the result inline in a single response (no task id / poll endpoint exists), so the
    /// work happens in <see cref="SubmitAsync"/> and <see cref="PollAsync"/> returns it immediately —
    /// the same shape as the OpenRouter image adapter. One adapter instance handles a single job.
    ///
    /// The region is selected via <c>MCPFORUNITY_MINIMAX_REGION</c> (<c>global</c> — default — or
    /// <c>cn</c>) so both the global (<c>api.minimax.io</c>) and China (<c>api.minimaxi.com</c>)
    /// hosts are reachable; the key is only ever attached to the resolved region host.
    /// </summary>
    public sealed class MiniMaxAudioAdapter : IAudioProviderAdapter
    {
        internal const string GlobalEndpoint = "https://api.minimax.io/v1/music_generation";
        internal const string ChinaEndpoint = "https://api.minimaxi.com/v1/music_generation";
        private const string GlobalHost = "api.minimax.io";
        private const string ChinaHost = "api.minimaxi.com";
        internal const string RegionEnvVar = "MCPFORUNITY_MINIMAX_REGION";

        // Default background-music model. internal so the catalog references it directly (single
        // source of truth, drift-guarded).
        internal const string DefaultModel = "music-3.0";

        // We request mp3 (an import-allowed extension) and pin the result extension to it, so a
        // hosted result URL without a clean extension still imports correctly.
        private const string AudioFormat = "mp3";

        public string Id => "minimax";

        // Set once in SubmitAsync (synchronous provider), read back in PollAsync.
        private byte[] _inlineData;
        private string _downloadUrl;
        private string _resultExt;
        private string _error;

        public async Task<string> SubmitAsync(AudioGenRequest req, string apiKey, IHttpTransport http, CancellationToken ct)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));
            if (http == null) throw new ArgumentNullException(nameof(http));

            string model = string.IsNullOrEmpty(req.Model) ? DefaultModel : req.Model;
            ResolveRegion(out string endpoint, out string host);
            // The endpoint is adapter-constructed, but validate the host before attaching the key so
            // a mis-set region can never send credentials to an unexpected host.
            ProviderHttp.RequireHost(endpoint, host, apiKey, "minimax submit");

            var spec = new HttpRequestSpec
            {
                Method = "POST",
                Url = endpoint,
                ContentType = "application/json",
                Body = Encoding.UTF8.GetBytes(BuildBody(model, req).ToString(Formatting.None))
            };
            spec.Headers["Authorization"] = "Bearer " + apiKey;

            HttpResult res = await http.SendAsync(spec, ct);
            JObject json = ParseOk(res, apiKey);

            // base_resp.status_code == 0 means the request itself succeeded; anything else is an
            // application-level error (auth, quota, invalid params).
            int statusCode = AsInt(json["base_resp"]?["status_code"], -1);
            if (statusCode != 0)
            {
                string msg = json["base_resp"]?["status_msg"]?.ToString();
                _error = SecretRedactor.Scrub(
                    $"MiniMax music generation failed (status_code={statusCode}): {msg ?? "unknown error"}", apiKey);
                return "ready";
            }

            JToken data = json["data"];
            // data.status: 1 = in_progress, 2 = completed. The endpoint is synchronous, so a
            // non-completed status has no poll target to recover from — surface it as a failure.
            int genStatus = AsInt(data?["status"], -1);
            if (genStatus != 2)
            {
                _error = $"MiniMax music generation did not complete (status={genStatus}).";
                return "ready";
            }

            string audio = data?["audio"]?.ToString();
            if (string.IsNullOrEmpty(audio))
            {
                _error = "MiniMax completed but returned no audio.";
                return "ready";
            }

            // output_format may be a hosted URL or a hex-encoded audio payload; handle both.
            if (audio.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || audio.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                _downloadUrl = audio;
            }
            else
            {
                byte[] bytes = TryDecodeHex(audio);
                if (bytes == null || bytes.Length == 0)
                {
                    _error = "MiniMax returned an unrecognized audio payload.";
                    return "ready";
                }
                _inlineData = bytes;
            }
            _resultExt = AudioFormat;
            return "ready";
        }

        public Task<ProviderPollResult> PollAsync(string providerJobId, string apiKey, IHttpTransport http, CancellationToken ct)
        {
            var result = new ProviderPollResult { Progress = 1f };
            if (!string.IsNullOrEmpty(_error) || (_inlineData == null && string.IsNullOrEmpty(_downloadUrl)))
            {
                result.State = ProviderPollState.Failed;
                result.Error = _error ?? "MiniMax produced no audio.";
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

        // Prompt-driven instrumental background music. Duration is not a request parameter for this
        // endpoint (the model produces a full track), so req.Duration is intentionally unused.
        private static JObject BuildBody(string model, AudioGenRequest req)
        {
            return new JObject
            {
                ["model"] = model,
                ["prompt"] = req.Prompt ?? string.Empty,
                ["is_instrumental"] = true,
                ["output_format"] = "url",
                ["audio_setting"] = new JObject
                {
                    ["sample_rate"] = 44100,
                    ["bitrate"] = 256000,
                    ["format"] = AudioFormat
                }
            };
        }

        private static void ResolveRegion(out string endpoint, out string host)
        {
            string region = (Environment.GetEnvironmentVariable(RegionEnvVar) ?? string.Empty).Trim().ToLowerInvariant();
            if (region == "cn" || region == "cn_zh" || region == "china")
            {
                endpoint = ChinaEndpoint;
                host = ChinaHost;
            }
            else
            {
                endpoint = GlobalEndpoint;
                host = GlobalHost;
            }
        }

        private static int AsInt(JToken token, int fallback)
        {
            if (token == null || token.Type == JTokenType.Null) return fallback;
            if (token.Type == JTokenType.Integer) return (int)token;
            return int.TryParse(token.ToString(), out int v) ? v : fallback;
        }

        private static byte[] TryDecodeHex(string hex)
        {
            if (string.IsNullOrEmpty(hex) || (hex.Length % 2) != 0) return null;
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                int hi = HexVal(hex[i * 2]);
                int lo = HexVal(hex[i * 2 + 1]);
                if (hi < 0 || lo < 0) return null;
                bytes[i] = (byte)((hi << 4) | lo);
            }
            return bytes;
        }

        private static int HexVal(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
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
                string detail = json?["base_resp"]?["status_msg"]?.ToString()
                                ?? json?["error"]?.ToString()
                                ?? ProviderHttp.Truncate(text);
                throw new Exception(SecretRedactor.Scrub($"MiniMax request failed (status={res?.Status}): {detail}", apiKey));
            }
            return json ?? new JObject();
        }
    }
}
