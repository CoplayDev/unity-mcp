using System;
using System.Collections.Generic;
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
    /// Tencent Hunyuan 3D model provider. The provider key is a JSON blob carrying the Tencent
    /// Cloud <c>secretId</c>/<c>secretKey</c> pair; each request is signed with TC3-HMAC-SHA256
    /// (see <see cref="TencentCloud3Signer"/>). Submit mints a JobId; polling maps Tencent's job
    /// status and, on completion, returns the result archive URL (.zip). Secrets never log.
    /// </summary>
    public sealed class HunyuanAdapter : IModelProviderAdapter
    {
        private const string Endpoint = "https://ai3d.tencentcloudapi.com/";
        private const string Host = "ai3d.tencentcloudapi.com";
        private const string Service = "ai3d";
        private const string Version = "2025-05-13";
        private const string SubmitAction = "SubmitHunyuanTo3DJob";
        private const string QueryAction = "QueryHunyuanTo3DJob";

        public string Id => "hunyuan";

        public async Task<string> SubmitAsync(ModelGenRequest req, string apiKey, IHttpTransport http, CancellationToken ct)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));
            if (http == null) throw new ArgumentNullException(nameof(http));

            ParseCredentials(apiKey, out string secretId, out string secretKey);

            bool image = string.Equals(req.Mode, "image", StringComparison.OrdinalIgnoreCase)
                         && !string.IsNullOrEmpty(req.ImageUrl);
            JObject body = image
                ? new JObject { ["ImageUrl"] = req.ImageUrl }
                : new JObject { ["Prompt"] = req.Prompt ?? string.Empty };

            JObject json = await SendSigned(SubmitAction, body, secretId, secretKey, http, ct);

            string jobId = json["Response"]?["JobId"]?.ToString();
            if (string.IsNullOrEmpty(jobId))
                throw new Exception(SecretRedactor.Scrub("Hunyuan submit returned no JobId: " + Truncate(json?.ToString()), secretKey));
            return jobId;
        }

        public async Task<ProviderPollResult> PollAsync(string providerJobId, string apiKey, IHttpTransport http, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(providerJobId)) throw new ArgumentNullException(nameof(providerJobId));
            if (http == null) throw new ArgumentNullException(nameof(http));

            ParseCredentials(apiKey, out string secretId, out string secretKey);

            var body = new JObject { ["JobId"] = providerJobId };
            JObject json = await SendSigned(QueryAction, body, secretId, secretKey, http, ct);
            JObject response = json["Response"] as JObject ?? new JObject();

            string status = (response["Status"]?.ToString() ?? string.Empty).ToUpperInvariant();
            var result = new ProviderPollResult();
            switch (status)
            {
                case "DONE":
                    result.State = ProviderPollState.Succeeded;
                    result.Progress = 1f;
                    result.ResultExt = "zip";
                    result.DownloadUrl = (response["ResultFile3Ds"] as JArray)?[0]?["Url"]?.ToString();
                    if (string.IsNullOrEmpty(result.DownloadUrl))
                    {
                        result.State = ProviderPollState.Failed;
                        result.Error = "Hunyuan reported DONE but no result file URL was present.";
                    }
                    break;
                case "RUN":
                    result.State = ProviderPollState.Running;
                    break;
                case "WAIT":
                    result.State = ProviderPollState.Queued;
                    break;
                case "FAIL":
                    result.State = ProviderPollState.Failed;
                    result.Error = SecretRedactor.Scrub(
                        response["ErrorMessage"]?.ToString()
                        ?? response["Error"]?["Message"]?.ToString()
                        ?? "Hunyuan task failed.", secretKey);
                    break;
                default:
                    result.State = ProviderPollState.Queued;
                    break;
            }
            return result;
        }

        private static async Task<JObject> SendSigned(string action, JObject body, string secretId, string secretKey, IHttpTransport http, CancellationToken ct)
        {
            string payload = body.ToString(Formatting.None);
            Dictionary<string, string> headers = TencentCloud3Signer.SignedHeaders(
                Service, Host, action, Version, payload, secretId, secretKey, DateTimeOffset.UtcNow);

            var spec = new HttpRequestSpec
            {
                Method = "POST",
                Url = Endpoint,
                ContentType = "application/json; charset=utf-8",
                Body = Encoding.UTF8.GetBytes(payload)
            };
            foreach (var kv in headers)
            {
                // Content-Type is set via ContentType; UnityWebRequest forbids a manual Host header.
                if (string.Equals(kv.Key, "Content-Type", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(kv.Key, "Host", StringComparison.OrdinalIgnoreCase)) continue;
                spec.Headers[kv.Key] = kv.Value;
            }

            HttpResult res = await http.SendAsync(spec, ct);
            JObject json = Parse(res);

            string apiError = json?["Response"]?["Error"]?["Message"]?.ToString();
            bool httpOk = res != null && (res.IsSuccess || (res.Status >= 200 && res.Status < 300));
            if (!httpOk || !string.IsNullOrEmpty(apiError))
            {
                throw new Exception(SecretRedactor.Scrub(
                    $"Hunyuan {action} failed (status={res?.Status}): {apiError ?? Truncate(res?.Text)}", secretKey));
            }
            return json;
        }

        private static void ParseCredentials(string apiKey, out string secretId, out string secretKey)
        {
            secretId = null;
            secretKey = null;
            if (!string.IsNullOrEmpty(apiKey))
            {
                try
                {
                    JObject creds = JObject.Parse(apiKey);
                    secretId = creds["secretId"]?.ToString() ?? creds["SecretId"]?.ToString();
                    secretKey = creds["secretKey"]?.ToString() ?? creds["SecretKey"]?.ToString();
                }
                catch { /* handled below */ }
            }
            if (string.IsNullOrEmpty(secretId) || string.IsNullOrEmpty(secretKey))
                throw new Exception("Hunyuan key must be a JSON object: {\"secretId\":\"...\",\"secretKey\":\"...\"}.");
        }

        private static JObject Parse(HttpResult res)
        {
            string text = res?.Text;
            if (string.IsNullOrEmpty(text) && res?.Body != null) text = Encoding.UTF8.GetString(res.Body);
            if (string.IsNullOrEmpty(text)) return new JObject();
            try { return JObject.Parse(text); } catch { return new JObject(); }
        }

        private static string Truncate(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Length <= 500 ? s : s.Substring(0, 500) + "…";
        }
    }
}
