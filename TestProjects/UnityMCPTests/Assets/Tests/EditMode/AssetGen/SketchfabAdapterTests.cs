using System.Text;
using System.Threading;
using MCPForUnity.Editor.Services.AssetGen.Http;
using MCPForUnity.Editor.Services.AssetGen.Providers;
using NUnit.Framework;

namespace MCPForUnityTests.Editor.AssetGen
{
    /// <summary>
    /// Exercises the Sketchfab marketplace adapter against a <see cref="FakeHttpTransport"/>.
    /// Asserts the "Token" auth scheme and that the download endpoint's gltf.url is parsed into
    /// the archive URL the job manager will fetch. The secret is never asserted beyond the scheme.
    /// </summary>
    public class SketchfabAdapterTests
    {
        private static HttpResult Json(string json, int status = 200)
            => new HttpResult
            {
                Status = status,
                IsSuccess = status >= 200 && status < 300,
                Text = json,
                Body = Encoding.UTF8.GetBytes(json)
            };

        [Test]
        public void Search_GetsSearchEndpoint_WithTokenHeader_ReturnsRawJson()
        {
            var http = new FakeHttpTransport { Handler = _ => Json("{\"results\":[{\"uid\":\"abc\"}]}") };
            var adapter = new SketchfabAdapter();

            string raw = adapter.SearchAsync("castle", null, true, null, null, "sfk_secret", http, CancellationToken.None).GetAwaiter().GetResult();

            StringAssert.Contains("\"uid\":\"abc\"", raw);
            HttpRequestSpec rec = http.RecordedRequests[0];
            Assert.AreEqual("GET", rec.Method);
            StringAssert.Contains("/v3/search", rec.Url);
            StringAssert.Contains("downloadable=true", rec.Url);
            StringAssert.Contains("q=castle", rec.Url);
            Assert.IsTrue(rec.Headers.ContainsKey("Authorization"));
            StringAssert.StartsWith("Token ", rec.Headers["Authorization"]);
        }

        [Test]
        public void Search_ForwardsCategoriesCountCursorAndDownloadableFlag()
        {
            var http = new FakeHttpTransport { Handler = _ => Json("{\"results\":[],\"cursors\":{\"next\":\"2\"}}") };
            var adapter = new SketchfabAdapter();

            adapter.SearchAsync("castle", "architecture", false, 12, "2", "sfk_secret", http, CancellationToken.None)
                .GetAwaiter().GetResult();

            string url = http.RecordedRequests[0].Url;
            StringAssert.Contains("categories=architecture", url);
            StringAssert.Contains("count=12", url);
            StringAssert.Contains("cursor=2", url);
            StringAssert.Contains("downloadable=false", url);
        }

        [Test]
        public void ResolveDownloadUrl_ParsesGltfUrl()
        {
            var http = new FakeHttpTransport
            {
                Handler = _ => Json("{\"gltf\":{\"url\":\"https://dl.sketchfab.com/models/abc/file.zip?sig=x\"}}")
            };
            var adapter = new SketchfabAdapter();

            string url = adapter.ResolveDownloadUrlAsync("abc", "sfk_secret", http, CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.AreEqual("https://dl.sketchfab.com/models/abc/file.zip?sig=x", url);
            HttpRequestSpec rec = http.RecordedRequests[0];
            StringAssert.Contains("/v3/models/abc/download", rec.Url);
            StringAssert.StartsWith("Token ", rec.Headers["Authorization"]);
        }

        [Test]
        public void ResolveDownloadUrl_MissingGltf_Throws()
        {
            var http = new FakeHttpTransport { Handler = _ => Json("{\"usdz\":{\"url\":\"https://x/y.usdz\"}}") };
            var adapter = new SketchfabAdapter();

            Assert.Throws<System.Exception>(() =>
                adapter.ResolveDownloadUrlAsync("abc", "sfk_secret", http, CancellationToken.None).GetAwaiter().GetResult());
        }
    }
}
