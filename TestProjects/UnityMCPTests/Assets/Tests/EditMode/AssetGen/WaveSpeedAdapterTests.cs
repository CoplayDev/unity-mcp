using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MCPForUnity.Editor.Services.AssetGen.Http;
using MCPForUnity.Editor.Services.AssetGen.Providers;
using NUnit.Framework;
using UnityEngine;

namespace MCPForUnityTests.Editor.AssetGen
{
    /// <summary>
    /// Exercises the WaveSpeedAI image adapter against a <see cref="FakeHttpTransport"/> — no network.
    /// Asserts request shaping (endpoint, Bearer scheme, body), envelope error handling (HTTP 200
    /// with a non-200 code) and poll-state mapping.
    /// </summary>
    public class WaveSpeedAdapterTests
    {
        private const string Key = "ws_secret_value";
        private readonly List<TimeSpan> _delays = new List<TimeSpan>();

        [SetUp]
        public void SetUp()
        {
            _delays.Clear();
            WaveSpeedAdapter.DelayOverrideForTests = (d, _) => { _delays.Add(d); return Task.CompletedTask; };
        }

        [TearDown]
        public void TearDown() => WaveSpeedAdapter.DelayOverrideForTests = null;

        private static string ProjectRoot()
        {
            string dp = Application.dataPath.Replace('\\', '/');
            return dp.Substring(0, dp.Length - "Assets".Length);
        }

        private static string WriteProjectFile(string rel, byte[] bytes)
        {
            string abs = Path.Combine(ProjectRoot(), rel).Replace('\\', '/');
            Directory.CreateDirectory(Path.GetDirectoryName(abs));
            File.WriteAllBytes(abs, bytes);
            return rel;
        }

        private static HttpResult Json(string json, int status = 200)
            => new HttpResult { Status = status, IsSuccess = status >= 200 && status < 300, Text = json };

        private static HttpResult Prediction(string status, string extra = "")
            => Json("{\"code\":200,\"message\":\"success\",\"data\":{\"id\":\"pred123\",\"status\":\"" + status + "\"" + extra + "}}");

        private static string Submit(ImageGenRequest req, FakeHttpTransport http)
            => new WaveSpeedAdapter().SubmitAsync(req, Key, http, CancellationToken.None).GetAwaiter().GetResult();

        private static ProviderPollResult Poll(FakeHttpTransport http)
            => new WaveSpeedAdapter().PollAsync("pred123", Key, http, CancellationToken.None).GetAwaiter().GetResult();

        [Test]
        public void Submit_Text_PostsDefaultModel_WithBearerHeader_AndReturnsPredictionId()
        {
            var http = new FakeHttpTransport { Handler = _ => Prediction("created", ",\"outputs\":[]") };

            string id = Submit(new ImageGenRequest { Provider = "wavespeed", Mode = "text", Prompt = "a brass lantern" }, http);

            Assert.AreEqual("pred123", id);
            HttpRequestSpec rec = http.RecordedRequests[0];
            Assert.AreEqual("POST", rec.Method);
            Assert.AreEqual("https://api.wavespeed.ai/api/v3/" + WaveSpeedAdapter.DefaultModel, rec.Url);
            Assert.AreEqual("Bearer " + Key, rec.Headers["Authorization"]);
            Assert.AreEqual("{\"prompt\":\"a brass lantern\"}", Encoding.UTF8.GetString(rec.Body));
        }

        [Test]
        public void Submit_Text_WithSize_SendsReducedAspectRatio_AndRequestModel()
        {
            var http = new FakeHttpTransport { Handler = _ => Prediction("created") };

            Submit(new ImageGenRequest { Mode = "text", Prompt = "x", Model = "vendor/other-model", Width = 1920, Height = 1080 }, http);

            HttpRequestSpec rec = http.RecordedRequests[0];
            StringAssert.EndsWith("/api/v3/vendor/other-model", rec.Url);
            StringAssert.Contains("\"aspect_ratio\":\"16:9\"", Encoding.UTF8.GetString(rec.Body));
        }

        [TestCase(2520, 1080, "21:9", "2k")]
        [TestCase(1080, 2520, "9:21", "2k")]
        [TestCase(1920, 1080, "16:9", "1.5k")]
        [TestCase(1024, 1024, "1:1", "1k")]
        [TestCase(1000, 700, "3:2", "1k")]
        public void Submit_Text_DefaultModel_SendsNearestSupportedRatio_AndResolution(int w, int h, string ratio, string resolution)
        {
            var http = new FakeHttpTransport { Handler = _ => Prediction("created") };

            Submit(new ImageGenRequest { Mode = "text", Prompt = "x", Width = w, Height = h }, http);

            string body = Encoding.UTF8.GetString(http.RecordedRequests[0].Body);
            StringAssert.Contains("\"aspect_ratio\":\"" + ratio + "\"", body);
            StringAssert.Contains("\"resolution\":\"" + resolution + "\"", body);
        }

        [Test]
        public void Submit_Text_OtherModel_SendsNoResolution()
        {
            var http = new FakeHttpTransport { Handler = _ => Prediction("created") };

            Submit(new ImageGenRequest { Mode = "text", Prompt = "x", Model = "vendor/other-model", Width = 1024, Height = 1024 }, http);

            StringAssert.DoesNotContain("resolution", Encoding.UTF8.GetString(http.RecordedRequests[0].Body));
        }

        [Test]
        public void Submit_ImageMode_DefaultModel_UsesEditEndpoint_WithImagesArray()
        {
            var http = new FakeHttpTransport { Handler = _ => Prediction("created") };

            Submit(new ImageGenRequest { Mode = "image", Prompt = "make it watercolor", ImageUrl = "https://ex.com/in.png" }, http);

            HttpRequestSpec rec = http.RecordedRequests[0];
            Assert.AreEqual("https://api.wavespeed.ai/api/v3/" + WaveSpeedAdapter.DefaultEditModel, rec.Url);
            StringAssert.Contains("\"images\":[\"https://ex.com/in.png\"]", Encoding.UTF8.GetString(rec.Body));
        }

        [Test]
        public void Submit_ImageMode_LocalPath_SendsDataUri()
        {
            string rel = WriteProjectFile("Assets/Generated/__assetgen_wavespeed_adapter/ref.png", new byte[] { 137, 80, 78, 71 });
            try
            {
                var http = new FakeHttpTransport { Handler = _ => Prediction("created") };

                Submit(new ImageGenRequest { Mode = "image", Prompt = "watercolor", ImagePath = rel }, http);

                StringAssert.Contains("\"images\":[\"data:image/png;base64,", Encoding.UTF8.GetString(http.RecordedRequests[0].Body));
            }
            finally { try { Directory.Delete(Path.Combine(ProjectRoot(), "Assets/Generated/__assetgen_wavespeed_adapter"), true); } catch { } }
        }

        [Test]
        public void Submit_EnvelopeErrorWithHttp200_ThrowsWithMessage_AndSendsOnce()
        {
            var http = new FakeHttpTransport { Handler = _ => Json("{\"code\":401,\"message\":\"invalid api key\",\"data\":null}") };

            var ex = Assert.Throws<Exception>(() => Submit(new ImageGenRequest { Mode = "text", Prompt = "x" }, http));

            StringAssert.Contains("invalid api key", ex.Message);
            StringAssert.DoesNotContain(Key, ex.Message);
            Assert.AreEqual(1, http.RecordedRequests.Count);
        }

        [Test]
        public void Submit_HttpError_Throws()
        {
            var http = new FakeHttpTransport { Handler = _ => Json("{\"code\":500,\"message\":\"server error\"}", 500) };

            var ex = Assert.Throws<Exception>(() => Submit(new ImageGenRequest { Mode = "text", Prompt = "x" }, http));

            StringAssert.Contains("status=500", ex.Message);
            Assert.AreEqual(1, http.RecordedRequests.Count);
        }

        [Test]
        public void Submit_InvalidModelId_ThrowsBeforeSending()
        {
            var http = new FakeHttpTransport { Handler = _ => Prediction("created") };

            Assert.Throws<InvalidOperationException>(() => Submit(new ImageGenRequest { Mode = "text", Prompt = "x", Model = "../predictions" }, http));
            Assert.AreEqual(0, http.RecordedRequests.Count);
        }

        [Test]
        public void Poll_MapsQueuedAndRunning()
        {
            Assert.AreEqual(ProviderPollState.Queued, Poll(new FakeHttpTransport { Handler = _ => Prediction("created") }).State);

            var http = new FakeHttpTransport { Handler = _ => Prediction("processing") };
            Assert.AreEqual(ProviderPollState.Running, Poll(http).State);
            HttpRequestSpec rec = http.RecordedRequests[0];
            Assert.AreEqual("GET", rec.Method);
            Assert.AreEqual("https://api.wavespeed.ai/api/v3/predictions/pred123/result", rec.Url);
            Assert.AreEqual("Bearer " + Key, rec.Headers["Authorization"]);
        }

        [Test]
        public void Poll_Completed_ReturnsFirstOutputUrl_AndExtension()
        {
            var http = new FakeHttpTransport { Handler = _ => Prediction("completed", ",\"outputs\":[\"https://cdn.example.com/out.jpeg\"]") };

            ProviderPollResult res = Poll(http);

            Assert.AreEqual(ProviderPollState.Succeeded, res.State);
            Assert.AreEqual("https://cdn.example.com/out.jpeg", res.DownloadUrl);
            Assert.AreEqual("jpeg", res.ResultExt);
            Assert.AreEqual(1f, res.Progress, 0.001f);
        }

        [Test]
        public void Poll_CompletedWithoutOutputs_Fails()
        {
            ProviderPollResult res = Poll(new FakeHttpTransport { Handler = _ => Prediction("completed", ",\"outputs\":[]") });

            Assert.AreEqual(ProviderPollState.Failed, res.State);
            Assert.IsNotEmpty(res.Error);
        }

        [Test]
        public void Poll_Failed_ReturnsProviderError()
        {
            ProviderPollResult res = Poll(new FakeHttpTransport { Handler = _ => Prediction("failed", ",\"error\":\"content rejected\"") });

            Assert.AreEqual(ProviderPollState.Failed, res.State);
            Assert.AreEqual("content rejected", res.Error);
        }

        [Test]
        public void Poll_TransientFailures_AreRetried_HonoringRetryAfter()
        {
            var responses = new Queue<HttpResult>(new[]
            {
                new HttpResult { Status = 429, Text = "{\"code\":429,\"message\":\"slow down\"}", RetryAfterSeconds = 7 },
                new HttpResult { Status = 503, Text = "unavailable" },
                new HttpResult { Status = 0, Text = null },
                Prediction("processing"),
            });
            var http = new FakeHttpTransport { Handler = _ => responses.Dequeue() };

            Assert.AreEqual(ProviderPollState.Running, Poll(http).State);
            Assert.AreEqual(4, http.RecordedRequests.Count);
            CollectionAssert.AreEqual(new[] { TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4) }, _delays);
        }

        [Test]
        public void Poll_PersistentServerError_ThrowsAfterBoundedRetries()
        {
            var http = new FakeHttpTransport { Handler = _ => Json("{\"code\":502,\"message\":\"bad gateway\"}", 502) };

            var ex = Assert.Throws<Exception>(() => Poll(http));

            StringAssert.Contains("status=502", ex.Message);
            Assert.AreEqual(5, http.RecordedRequests.Count);
        }

        [Test]
        public void Poll_AuthError_IsNotRetried()
        {
            var http = new FakeHttpTransport { Handler = _ => Json("{\"code\":401,\"message\":\"invalid api key\"}", 401) };

            Assert.Throws<Exception>(() => Poll(http));
            Assert.AreEqual(1, http.RecordedRequests.Count);
        }

        [Test]
        public void Poll_InvalidPredictionId_Throws()
        {
            var http = new FakeHttpTransport { Handler = _ => Prediction("processing") };

            Assert.Throws<InvalidOperationException>(() =>
                new WaveSpeedAdapter().PollAsync("../../x", Key, http, CancellationToken.None).GetAwaiter().GetResult());
            Assert.AreEqual(0, http.RecordedRequests.Count);
        }
    }
}
