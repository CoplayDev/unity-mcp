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
    /// MiniMax music-cover adapter for the synchronous music-generation endpoint. A cover accepts
    /// one raw reference source (URL or base64 audio) and an optional preprocessed feature id, then
    /// exposes the returned URL or hex payload through the normal audio job pipeline.
    /// </summary>
    public sealed class MiniMaxAudioAdapter : IAudioProviderAdapter
    {
        internal const string GlobalEndpoint = "https://api.minimax.io/v1/music_generation";
        internal const string ChinaEndpoint = "https://api.minimaxi.com/v1/music_generation";
        internal const string RegionEnvVar = "MCPFORUNITY_MINIMAX_REGION";
        internal const string DefaultModel = "music-cover";

        private const string GlobalHost = "api.minimax.io";
        private const string ChinaHost = "api.minimaxi.com";
        private const string FreeModel = "music-cover-free";

        private byte[] _inlineData;
        private string _downloadUrl;
        private string _resultExt;
        private string _error;

        public string Id => "minimax";

        internal static bool IsCoverModel(string model)
            => string.Equals(model, DefaultModel, StringComparison.Ordinal)
               || string.Equals(model, FreeModel, StringComparison.Ordinal);

        public async Task<string> SubmitAsync(
            AudioGenRequest req,
            string apiKey,
            IHttpTransport http,
            CancellationToken ct)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));
            if (http == null) throw new ArgumentNullException(nameof(http));

            string model = string.IsNullOrWhiteSpace(req.Model) ? DefaultModel : req.Model;
            ValidateRequest(req, model);
            ResolveRegion(out string endpoint, out string host, out bool chinaRegion);
            ProviderHttp.RequireHost(endpoint, host, apiKey, "MiniMax cover submit");

            var spec = new HttpRequestSpec
            {
                Method = "POST",
                Url = endpoint,
                ContentType = "application/json",
                Body = Encoding.UTF8.GetBytes(BuildBody(req, model, chinaRegion).ToString(Formatting.None))
            };
            spec.Headers["Authorization"] = "Bearer " + apiKey;

            HttpResult response = await http.SendAsync(spec, ct);
            JObject json = ParseOk(response, apiKey);

            int statusCode = AsInt(json["base_resp"]?["status_code"], -1);
            if (statusCode != 0)
            {
                string statusMessage = json["base_resp"]?["status_msg"]?.ToString();
                _error = SecretRedactor.Scrub(
                    $"MiniMax music cover failed (status_code={statusCode}): {statusMessage ?? "unknown error"}",
                    apiKey);
                return "ready";
            }

            JToken data = json["data"];
            int generationStatus = AsInt(data?["status"], -1);
            if (generationStatus != 2)
            {
                _error = generationStatus == 1
                    ? "MiniMax music cover is still in progress, but the response included no query endpoint."
                    : $"MiniMax music cover returned an unexpected status ({generationStatus}).";
                return "ready";
            }

            string audio = data?["audio"]?.ToString();
            if (string.IsNullOrWhiteSpace(audio))
            {
                _error = "MiniMax music cover completed without audio data.";
                return "ready";
            }

            if (Uri.TryCreate(audio, UriKind.Absolute, out Uri audioUri)
                && (audioUri.Scheme == Uri.UriSchemeHttp || audioUri.Scheme == Uri.UriSchemeHttps))
            {
                _downloadUrl = audio;
            }
            else
            {
                _inlineData = TryDecodeHex(audio);
                if (_inlineData == null || _inlineData.Length == 0)
                {
                    _error = "MiniMax returned an unrecognized audio payload.";
                    return "ready";
                }
            }

            _resultExt = NormalizeAudioFormat(req.AudioFormat);
            return "ready";
        }

        public Task<ProviderPollResult> PollAsync(
            string providerJobId,
            string apiKey,
            IHttpTransport http,
            CancellationToken ct)
        {
            var result = new ProviderPollResult { Progress = 1f };
            if (!string.IsNullOrEmpty(_error) || (_inlineData == null && string.IsNullOrEmpty(_downloadUrl)))
            {
                result.State = ProviderPollState.Failed;
                result.Error = _error ?? "MiniMax produced no cover audio.";
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

        private static void ValidateRequest(AudioGenRequest req, string model)
        {
            if (!IsCoverModel(model))
                throw new NotSupportedException("MiniMax audio supports music-cover and music-cover-free.");

            int sourceCount = 0;
            if (!string.IsNullOrWhiteSpace(req.AudioUrl)) sourceCount++;
            if (!string.IsNullOrWhiteSpace(req.AudioBase64)) sourceCount++;
            if (sourceCount != 1)
                throw new ArgumentException("Provide exactly one cover source: audio_url or audio_base64.");

            NormalizeOutputFormat(req.OutputFormat);
            NormalizeAudioFormat(req.AudioFormat);
        }

        private static JObject BuildBody(AudioGenRequest req, string model, bool chinaRegion)
        {
            var body = new JObject
            {
                ["model"] = model,
                ["stream"] = false,
                ["output_format"] = NormalizeOutputFormat(req.OutputFormat),
                ["audio_setting"] = new JObject
                {
                    ["format"] = NormalizeAudioFormat(req.AudioFormat)
                }
            };

            if (!string.IsNullOrWhiteSpace(req.Prompt)) body["prompt"] = req.Prompt;
            if (!string.IsNullOrWhiteSpace(req.Lyrics)) body["lyrics"] = req.Lyrics;
            if (req.LyricsOptimizer.HasValue) body["lyrics_optimizer"] = req.LyricsOptimizer.Value;
            if (req.IsInstrumental.HasValue) body["is_instrumental"] = req.IsInstrumental.Value;
            if (!string.IsNullOrWhiteSpace(req.AudioUrl)) body["audio_url"] = req.AudioUrl;
            if (!string.IsNullOrWhiteSpace(req.AudioBase64)) body["audio_base64"] = req.AudioBase64;
            if (!string.IsNullOrWhiteSpace(req.CoverFeatureId)) body["cover_feature_id"] = req.CoverFeatureId;
            if (chinaRegion && req.AigcWatermark.HasValue) body["aigc_watermark"] = req.AigcWatermark.Value;
            return body;
        }

        private static string NormalizeOutputFormat(string value)
        {
            string normalized = string.IsNullOrWhiteSpace(value) ? "url" : value.Trim().ToLowerInvariant();
            if (normalized != "url" && normalized != "hex")
                throw new ArgumentException("output_format must be 'url' or 'hex'.");
            return normalized;
        }

        private static string NormalizeAudioFormat(string value)
        {
            string normalized = string.IsNullOrWhiteSpace(value) ? "mp3" : value.Trim().ToLowerInvariant();
            if (normalized != "mp3" && normalized != "wav" && normalized != "pcm")
                throw new ArgumentException("audio_format must be 'mp3', 'wav', or 'pcm'.");
            return normalized;
        }

        private static void ResolveRegion(out string endpoint, out string host, out bool chinaRegion)
        {
            string region = (Environment.GetEnvironmentVariable(RegionEnvVar) ?? string.Empty)
                .Trim().ToLowerInvariant();
            chinaRegion = region == "cn" || region == "cn_zh" || region == "china";
            endpoint = chinaRegion ? ChinaEndpoint : GlobalEndpoint;
            host = chinaRegion ? ChinaHost : GlobalHost;
        }

        private static int AsInt(JToken token, int fallback)
        {
            if (token == null || token.Type == JTokenType.Null) return fallback;
            if (token.Type == JTokenType.Integer) return (int)token;
            return int.TryParse(token.ToString(), out int parsed) ? parsed : fallback;
        }

        private static byte[] TryDecodeHex(string hex)
        {
            if (string.IsNullOrEmpty(hex) || (hex.Length % 2) != 0) return null;
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                int high = HexValue(hex[i * 2]);
                int low = HexValue(hex[i * 2 + 1]);
                if (high < 0 || low < 0) return null;
                bytes[i] = (byte)((high << 4) | low);
            }
            return bytes;
        }

        private static int HexValue(char value)
        {
            if (value >= '0' && value <= '9') return value - '0';
            if (value >= 'a' && value <= 'f') return value - 'a' + 10;
            if (value >= 'A' && value <= 'F') return value - 'A' + 10;
            return -1;
        }

        private static JObject ParseOk(HttpResult response, string apiKey)
        {
            string text = ProviderHttp.BodyText(response);
            JObject json = null;
            if (!string.IsNullOrEmpty(text))
            {
                try { json = JObject.Parse(text); }
                catch { /* handled below */ }
            }

            if (response?.Ok != true)
            {
                string detail = json?["base_resp"]?["status_msg"]?.ToString()
                                ?? json?["error"]?.ToString()
                                ?? ProviderHttp.Truncate(text);
                throw new Exception(SecretRedactor.Scrub(
                    $"MiniMax cover request failed (status={response?.Status}): {detail}", apiKey));
            }
            return json ?? new JObject();
        }
    }
}
