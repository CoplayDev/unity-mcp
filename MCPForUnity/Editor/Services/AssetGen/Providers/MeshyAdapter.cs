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
    /// Meshy model provider. Text→3D posts a "preview" task to the v2 text-to-3d endpoint;
    /// image→3D posts to the v1 image-to-3d endpoint. Both mint a task id (returned by the API
    /// in the <c>result</c> field). Polling reads the task, mapping Meshy's status enum and
    /// surfacing the model URL for the requested format on success. The bearer key is supplied
    /// per call and never logged; every error is run through <see cref="SecretRedactor"/>.
    /// </summary>
    public sealed class MeshyAdapter : IModelProviderAdapter
    {
        private const string TextEndpoint = "https://api.meshy.ai/openapi/v2/text-to-3d";
        private const string ImageEndpoint = "https://api.meshy.ai/openapi/v1/image-to-3d";

        public string Id => "meshy";

        // Stashed at submit so poll can pick the matching model_urls entry.
        private string _format = "glb";

        public async Task<string> SubmitAsync(ModelGenRequest req, string apiKey, IHttpTransport http, CancellationToken ct)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));
            if (http == null) throw new ArgumentNullException(nameof(http));

            _format = string.IsNullOrEmpty(req.Format) ? "glb" : req.Format.TrimStart('.').ToLowerInvariant();

            bool image = string.Equals(req.Mode, "image", StringComparison.OrdinalIgnoreCase)
                         && !string.IsNullOrEmpty(req.ImageUrl);

            JObject body;
            string url;
            if (image)
            {
                url = ImageEndpoint;
                body = new JObject { ["image_url"] = req.ImageUrl, ["ai_model"] = "meshy-6" };
            }
            else
            {
                url = TextEndpoint;
                body = new JObject
                {
                    ["mode"] = "preview",
                    ["prompt"] = req.Prompt ?? string.Empty,
                    ["ai_model"] = "meshy-6"
                };
            }

            var spec = new HttpRequestSpec
            {
                Method = "POST",
                Url = url,
                ContentType = "application/json",
                Body = Encoding.UTF8.GetBytes(body.ToString(Formatting.None))
            };
            spec.Headers["Authorization"] = "Bearer " + apiKey;

            HttpResult res = await http.SendAsync(spec, ct);
            JObject json = ParseOk(res, apiKey, "submit");

            string taskId = json["result"]?.ToString();
            if (string.IsNullOrEmpty(taskId))
                throw new Exception(SecretRedactor.Scrub("Meshy submit returned no task id: " + Truncate(res?.Text), apiKey));
            return taskId;
        }

        public async Task<ProviderPollResult> PollAsync(string providerJobId, string apiKey, IHttpTransport http, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(providerJobId)) throw new ArgumentNullException(nameof(providerJobId));
            if (http == null) throw new ArgumentNullException(nameof(http));

            var spec = new HttpRequestSpec { Method = "GET", Url = TextEndpoint + "/" + providerJobId };
            spec.Headers["Authorization"] = "Bearer " + apiKey;

            HttpResult res = await http.SendAsync(spec, ct);
            JObject json = ParseOk(res, apiKey, "poll");

            var result = new ProviderPollResult { State = MapState(json["status"]?.ToString()) };

            JToken prog = json["progress"];
            if (prog != null && prog.Type != JTokenType.Null)
                result.Progress = Mathf.Clamp01(prog.Value<float>() / 100f);

            if (result.State == ProviderPollState.Succeeded)
            {
                result.Progress = 1f;
                result.DownloadUrl = ExtractModelUrl(json["model_urls"] as JObject);
                if (string.IsNullOrEmpty(result.DownloadUrl))
                {
                    result.State = ProviderPollState.Failed;
                    result.Error = "Meshy reported success but no model URL was present in the response.";
                }
            }
            else if (result.State == ProviderPollState.Failed)
            {
                string err = json["task_error"]?["message"]?.ToString()
                             ?? json["message"]?.ToString()
                             ?? "Meshy task failed.";
                result.Error = SecretRedactor.Scrub(err, apiKey);
            }

            return result;
        }

        private string ExtractModelUrl(JObject urls)
        {
            if (urls == null) return null;
            string byFormat = urls[_format]?.ToString();
            if (!string.IsNullOrEmpty(byFormat)) return byFormat;
            string glb = urls["glb"]?.ToString();
            if (!string.IsNullOrEmpty(glb)) return glb;
            string fbx = urls["fbx"]?.ToString();
            return string.IsNullOrEmpty(fbx) ? null : fbx;
        }

        private static ProviderPollState MapState(string status)
        {
            switch ((status ?? string.Empty).ToUpperInvariant())
            {
                case "SUCCEEDED":
                    return ProviderPollState.Succeeded;
                case "FAILED":
                case "EXPIRED":
                    return ProviderPollState.Failed;
                case "IN_PROGRESS":
                    return ProviderPollState.Running;
                case "PENDING":
                default:
                    return ProviderPollState.Queued;
            }
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
                string detail = json?["message"]?.ToString() ?? json?["error"]?.ToString() ?? Truncate(text);
                throw new Exception(SecretRedactor.Scrub($"Meshy {phase} failed (status={res?.Status}): {detail}", apiKey));
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
