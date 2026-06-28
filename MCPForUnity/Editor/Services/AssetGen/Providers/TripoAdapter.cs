using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MCPForUnity.Editor.Security;
using MCPForUnity.Editor.Services.AssetGen.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MCPForUnity.Editor.Services.AssetGen.Providers
{
    /// <summary>
    /// Tripo3D model provider. Submits text→3D / image→3D tasks to the OpenAPI task endpoint and
    /// polls for completion. The bearer key is supplied per call and never logged; every error
    /// message is run through <see cref="SecretRedactor"/> before it is surfaced.
    /// </summary>
    public sealed class TripoAdapter : IModelProviderAdapter
    {
        private const string TaskEndpoint = "https://api.tripo3d.ai/v2/openapi/task";
        private const string ModelVersion = "v2.5-20250123";

        public string Id => "tripo";

        public async Task<string> SubmitAsync(ModelGenRequest req, string apiKey, IHttpTransport http, CancellationToken ct)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));
            if (http == null) throw new ArgumentNullException(nameof(http));

            JObject body;
            bool image = string.Equals(req.Mode, "image", StringComparison.OrdinalIgnoreCase)
                         && !string.IsNullOrEmpty(req.ImageUrl);
            if (image)
            {
                body = new JObject
                {
                    ["type"] = "image_to_model",
                    ["file"] = new JObject
                    {
                        ["type"] = "url",
                        ["url"] = req.ImageUrl
                    }
                };
            }
            else
            {
                body = new JObject
                {
                    ["type"] = "text_to_model",
                    ["prompt"] = req.Prompt ?? string.Empty,
                    ["model_version"] = ModelVersion
                };
            }

            var spec = new HttpRequestSpec
            {
                Method = "POST",
                Url = TaskEndpoint,
                ContentType = "application/json",
                Body = Encoding.UTF8.GetBytes(body.ToString(Formatting.None))
            };
            spec.Headers["Authorization"] = "Bearer " + apiKey;

            HttpResult res = await http.SendAsync(spec, ct);
            JObject json = ParseAndValidate(res, apiKey, "submit");

            string taskId = json["data"]?["task_id"]?.ToString();
            if (string.IsNullOrEmpty(taskId))
            {
                throw new Exception(SecretRedactor.Scrub(
                    "Tripo submit returned no task_id: " + Truncate(res?.Text), apiKey));
            }
            return taskId;
        }

        public async Task<ProviderPollResult> PollAsync(string providerJobId, string apiKey, IHttpTransport http, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(providerJobId)) throw new ArgumentNullException(nameof(providerJobId));
            if (http == null) throw new ArgumentNullException(nameof(http));

            var spec = new HttpRequestSpec
            {
                Method = "GET",
                Url = TaskEndpoint + "/" + providerJobId
            };
            spec.Headers["Authorization"] = "Bearer " + apiKey;

            HttpResult res = await http.SendAsync(spec, ct);
            JObject json = ParseAndValidate(res, apiKey, "poll");
            JObject data = json["data"] as JObject ?? new JObject();

            var result = new ProviderPollResult { State = MapState(data["status"]?.ToString()) };

            JToken progressTok = data["progress"];
            if (progressTok != null && progressTok.Type != JTokenType.Null)
            {
                result.Progress = Mathf.Clamp01(progressTok.Value<float>() / 100f);
            }

            if (result.State == ProviderPollState.Succeeded)
            {
                result.Progress = 1f;
                result.DownloadUrl = ExtractDownloadUrl(data);
                if (string.IsNullOrEmpty(result.DownloadUrl))
                {
                    result.State = ProviderPollState.Failed;
                    result.Error = "Tripo reported success but no model URL was present in the response.";
                }
            }
            else if (result.State == ProviderPollState.Failed)
            {
                string err = data["error"]?.ToString()
                             ?? data["message"]?.ToString()
                             ?? json["message"]?.ToString()
                             ?? "Tripo task failed.";
                result.Error = SecretRedactor.Scrub(err, apiKey);
            }

            return result;
        }

        private static ProviderPollState MapState(string status)
        {
            switch ((status ?? string.Empty).ToLowerInvariant())
            {
                case "success":
                case "succeeded":
                    return ProviderPollState.Succeeded;
                case "failed":
                case "error":
                case "cancelled":
                case "canceled":
                case "banned":
                case "expired":
                    return ProviderPollState.Failed;
                case "running":
                case "processing":
                    return ProviderPollState.Running;
                default:
                    return ProviderPollState.Queued;
            }
        }

        /// <summary>
        /// Resolve the model download URL, defensive about Tripo's nested output shapes. Prefer a
        /// textured / PBR model; fall back to the base model. Accepts both the newer flat form
        /// (<c>output.pbr_model</c> = url string) and the older nested form
        /// (<c>result.pbr_model.url</c> = object with a url field).
        /// </summary>
        private static string ExtractDownloadUrl(JObject data)
        {
            JObject output = data["output"] as JObject;
            JObject resultObj = data["result"] as JObject;

            return UrlOf(output?["pbr_model"])
                   ?? UrlOf(resultObj?["pbr_model"])
                   ?? UrlOf(output?["model"])
                   ?? UrlOf(resultObj?["model"])
                   ?? UrlOf(output?["base_model"])
                   ?? UrlOf(resultObj?["base_model"]);
        }

        /// <summary>A field may be a plain URL string or an object carrying a "url" property.</summary>
        private static string UrlOf(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return null;
            if (token.Type == JTokenType.String)
            {
                string s = token.ToString();
                return string.IsNullOrEmpty(s) ? null : s;
            }
            if (token is JObject obj)
            {
                string u = obj["url"]?.ToString();
                return string.IsNullOrEmpty(u) ? null : u;
            }
            return null;
        }

        /// <summary>
        /// Validate transport success + Tripo's body-level <c>code</c> (0 == ok), parse the JSON,
        /// and throw a redacted exception otherwise.
        /// </summary>
        private static JObject ParseAndValidate(HttpResult res, string apiKey, string phase)
        {
            string text = res?.Text;
            if (string.IsNullOrEmpty(text) && res?.Body != null)
            {
                text = Encoding.UTF8.GetString(res.Body);
            }

            JObject json = null;
            if (!string.IsNullOrEmpty(text))
            {
                try { json = JObject.Parse(text); }
                catch { /* non-JSON body; handled below */ }
            }

            bool httpOk = res != null && (res.IsSuccess || (res.Status >= 200 && res.Status < 300));

            int code = 0;
            JToken codeTok = json?["code"];
            if (codeTok != null && codeTok.Type != JTokenType.Null)
            {
                try { code = codeTok.Value<int>(); } catch { code = -1; }
            }

            if (!httpOk || code != 0)
            {
                string detail = json?["message"]?.ToString()
                                ?? json?["error"]?.ToString()
                                ?? Truncate(text);
                throw new Exception(SecretRedactor.Scrub(
                    $"Tripo {phase} failed (status={res?.Status}, code={code}): {detail}", apiKey));
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
