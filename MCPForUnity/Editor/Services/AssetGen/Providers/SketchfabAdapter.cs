using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MCPForUnity.Editor.Security;
using MCPForUnity.Editor.Services.AssetGen.Http;
using Newtonsoft.Json.Linq;

namespace MCPForUnity.Editor.Services.AssetGen.Providers
{
    /// <summary>
    /// Sketchfab marketplace provider. Search/preview are read-only GETs returning the raw API
    /// JSON to the caller; <see cref="ResolveDownloadUrlAsync"/> hits the model download endpoint
    /// and returns the signed glTF archive (.zip) URL the job manager will fetch. Auth uses the
    /// "Token &lt;key&gt;" scheme; the key is supplied per call and never logged.
    /// </summary>
    public sealed class SketchfabAdapter : IMarketplaceProviderAdapter
    {
        private const string SearchEndpoint = "https://api.sketchfab.com/v3/search";
        private const string ModelsEndpoint = "https://api.sketchfab.com/v3/models";

        public string Id => "sketchfab";

        public async Task<string> SearchAsync(string query, string apiKey, IHttpTransport http, CancellationToken ct)
        {
            if (http == null) throw new ArgumentNullException(nameof(http));
            string url = SearchEndpoint + "?type=models&downloadable=true&q=" + Uri.EscapeDataString(query ?? string.Empty);
            var spec = new HttpRequestSpec { Method = "GET", Url = url };
            spec.Headers["Authorization"] = "Token " + apiKey;
            HttpResult res = await http.SendAsync(spec, ct);
            return RawOk(res, apiKey, "search");
        }

        public async Task<string> PreviewAsync(string uid, string apiKey, IHttpTransport http, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(uid)) throw new ArgumentNullException(nameof(uid));
            if (http == null) throw new ArgumentNullException(nameof(http));
            var spec = new HttpRequestSpec { Method = "GET", Url = ModelsEndpoint + "/" + uid };
            spec.Headers["Authorization"] = "Token " + apiKey;
            HttpResult res = await http.SendAsync(spec, ct);
            return RawOk(res, apiKey, "preview");
        }

        public async Task<string> ResolveDownloadUrlAsync(string uid, string apiKey, IHttpTransport http, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(uid)) throw new ArgumentNullException(nameof(uid));
            if (http == null) throw new ArgumentNullException(nameof(http));
            var spec = new HttpRequestSpec { Method = "GET", Url = ModelsEndpoint + "/" + uid + "/download" };
            spec.Headers["Authorization"] = "Token " + apiKey;

            HttpResult res = await http.SendAsync(spec, ct);
            JObject json = ParseOk(res, apiKey, "download");

            string url = json["gltf"]?["url"]?.ToString();
            if (string.IsNullOrEmpty(url))
            {
                throw new Exception(SecretRedactor.Scrub(
                    $"Sketchfab download returned no gltf url for '{uid}': {Truncate(res?.Text)}", apiKey));
            }
            return url;
        }

        private static string RawOk(HttpResult res, string apiKey, string phase)
        {
            string text = res?.Text;
            if (string.IsNullOrEmpty(text) && res?.Body != null) text = Encoding.UTF8.GetString(res.Body);

            bool ok = res != null && (res.IsSuccess || (res.Status >= 200 && res.Status < 300));
            if (!ok)
                throw new Exception(SecretRedactor.Scrub($"Sketchfab {phase} failed (status={res?.Status}): {Truncate(text)}", apiKey));
            return text ?? string.Empty;
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
                throw new Exception(SecretRedactor.Scrub($"Sketchfab {phase} failed (status={res?.Status}): {detail}", apiKey));
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
