using System;
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

        public async Task<string> SearchAsync(string query, string categories, bool downloadable, int? count, string cursor, string apiKey, IHttpTransport http, CancellationToken ct)
        {
            if (http == null) throw new ArgumentNullException(nameof(http));
            string url = SearchEndpoint + "?type=models&downloadable=" + (downloadable ? "true" : "false")
                         + "&q=" + Uri.EscapeDataString(query ?? string.Empty);
            if (!string.IsNullOrEmpty(categories)) url += "&categories=" + Uri.EscapeDataString(categories);
            if (count.HasValue) url += "&count=" + count.Value;
            if (!string.IsNullOrEmpty(cursor)) url += "&cursor=" + Uri.EscapeDataString(cursor);
            var spec = new HttpRequestSpec { Method = "GET", Url = url };
            spec.Headers["Authorization"] = "Token " + apiKey;
            HttpResult res = await http.SendAsync(spec, ct);
            // The raw response carries pagination (`cursors.next` / `next`) for the caller to page.
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
                    $"Sketchfab download returned no gltf url for '{uid}': {ProviderHttp.Truncate(res?.Text)}", apiKey));
            }
            return url;
        }

        private static string RawOk(HttpResult res, string apiKey, string phase)
        {
            string text = ProviderHttp.BodyText(res);

            bool ok = res?.Ok == true;
            if (!ok)
                throw new Exception(SecretRedactor.Scrub($"Sketchfab {phase} failed (status={res?.Status}): {ProviderHttp.Truncate(text)}", apiKey));
            return text ?? string.Empty;
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
                throw new Exception(SecretRedactor.Scrub($"Sketchfab {phase} failed (status={res?.Status}): {detail}", apiKey));
            }
            return json ?? new JObject();
        }
    }
}
