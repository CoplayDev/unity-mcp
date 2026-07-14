using System.Text;
using System.Threading;
using MCPForUnity.Editor.Services.AssetGen.Http;
using MCPForUnity.Editor.Services.AssetGen.Providers;
using NUnit.Framework;

namespace MCPForUnityTests.Editor.AssetGen
{
    /// <summary>
    /// Exercises the Tripo adapter against a <see cref="FakeHttpTransport"/> — no network.
    /// Asserts request shaping (endpoint, method, auth scheme, body) and poll-state mapping.
    /// The bearer secret is never asserted beyond the "Bearer " scheme prefix.
    /// </summary>
    public class TripoAdapterTests
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
        public void Submit_PostsTaskEndpoint_WithBearerHeader_AndReturnsTaskId()
        {
            var http = new FakeHttpTransport
            {
                Handler = _ => Json("{\"code\":0,\"data\":{\"task_id\":\"abc123\"}}")
            };
            var adapter = new TripoAdapter();
            var req = new ModelGenRequest { Provider = "tripo", Mode = "text", Prompt = "a red dragon" };

            string taskId = adapter.SubmitAsync(req, "tsk_secret_value", http, CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.AreEqual("abc123", taskId);
            Assert.AreEqual(1, http.RecordedRequests.Count);

            HttpRequestSpec rec = http.RecordedRequests[0];
            StringAssert.Contains("/v2/openapi/task", rec.Url);
            Assert.AreEqual("POST", rec.Method);
            Assert.IsTrue(rec.Headers.ContainsKey("Authorization"));
            StringAssert.StartsWith("Bearer ", rec.Headers["Authorization"]);

            string body = Encoding.UTF8.GetString(rec.Body);
            StringAssert.Contains("a red dragon", body);
            StringAssert.Contains("text_to_model", body);
        }

        [Test]
        public void Poll_MapsSuccess_WithDownloadUrl()
        {
            var http = new FakeHttpTransport
            {
                Handler = _ => Json(
                    "{\"code\":0,\"data\":{\"status\":\"success\",\"progress\":100," +
                    "\"output\":{\"pbr_model\":\"https://cdn.tripo3d.ai/model.glb\"}}}")
            };
            var adapter = new TripoAdapter();

            ProviderPollResult res = adapter.PollAsync("abc123", "tsk_secret_value", http, CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.AreEqual(ProviderPollState.Succeeded, res.State);
            Assert.AreEqual("https://cdn.tripo3d.ai/model.glb", res.DownloadUrl);
        }

        [Test]
        public void Poll_MapsRunning_WithProgress()
        {
            var http = new FakeHttpTransport
            {
                Handler = _ => Json("{\"code\":0,\"data\":{\"status\":\"running\",\"progress\":42}}")
            };
            var adapter = new TripoAdapter();

            ProviderPollResult res = adapter.PollAsync("abc123", "k", http, CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.AreEqual(ProviderPollState.Running, res.State);
            Assert.AreEqual(0.42f, res.Progress, 0.001f);
        }

        [Test]
        public void Poll_MapsFailed_WithError()
        {
            var http = new FakeHttpTransport
            {
                Handler = _ => Json("{\"code\":0,\"data\":{\"status\":\"failed\",\"progress\":0}}")
            };
            var adapter = new TripoAdapter();

            ProviderPollResult res = adapter.PollAsync("abc123", "k", http, CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.AreEqual(ProviderPollState.Failed, res.State);
            Assert.IsNotEmpty(res.Error);
        }

        [Test]
        public void Submit_UsesRequestModel_WhenProvided()
        {
            var http = new FakeHttpTransport { Handler = _ => Json("{\"code\":0,\"data\":{\"task_id\":\"t\"}}") };
            var adapter = new TripoAdapter();
            var req = new ModelGenRequest { Provider = "tripo", Mode = "text", Prompt = "x", Model = "P1-20260311" };

            adapter.SubmitAsync(req, "k", http, CancellationToken.None).GetAwaiter().GetResult();

            string body = Encoding.UTF8.GetString(http.RecordedRequests[0].Body);
            StringAssert.Contains("\"model_version\":\"P1-20260311\"", body);
        }

        [Test]
        public void Submit_FallsBackToDefault_WhenModelEmpty()
        {
            var http = new FakeHttpTransport { Handler = _ => Json("{\"code\":0,\"data\":{\"task_id\":\"t\"}}") };
            var adapter = new TripoAdapter();
            var req = new ModelGenRequest { Provider = "tripo", Mode = "text", Prompt = "x" }; // Model null

            adapter.SubmitAsync(req, "k", http, CancellationToken.None).GetAwaiter().GetResult();

            string body = Encoding.UTF8.GetString(http.RecordedRequests[0].Body);
            StringAssert.Contains("\"model_version\":\"" + TripoAdapter.ModelVersion + "\"", body);
        }

        [Test]
        public void Submit_ImageMode_UsesImageToModel()
        {
            var http = new FakeHttpTransport
            {
                Handler = _ => Json("{\"code\":0,\"data\":{\"task_id\":\"img1\"}}")
            };
            var adapter = new TripoAdapter();
            var req = new ModelGenRequest
            {
                Provider = "tripo",
                Mode = "image",
                ImageUrl = "https://example.com/in.png"
            };

            adapter.SubmitAsync(req, "k", http, CancellationToken.None).GetAwaiter().GetResult();

            string body = Encoding.UTF8.GetString(http.RecordedRequests[0].Body);
            StringAssert.Contains("image_to_model", body);
            StringAssert.Contains("https://example.com/in.png", body);
        }

        [Test]
        public void Submit_ImageMode_HonorsModelVersion_WhenProvided()
        {
            var http = new FakeHttpTransport { Handler = _ => Json("{\"code\":0,\"data\":{\"task_id\":\"img1\"}}") };
            var adapter = new TripoAdapter();
            var req = new ModelGenRequest
            {
                Provider = "tripo",
                Mode = "image",
                ImageUrl = "https://example.com/in.png",
                Model = "P1-20260311",
            };

            adapter.SubmitAsync(req, "k", http, CancellationToken.None).GetAwaiter().GetResult();

            string body = Encoding.UTF8.GetString(http.RecordedRequests[0].Body);
            StringAssert.Contains("image_to_model", body);
            StringAssert.Contains("\"model_version\":\"P1-20260311\"", body);
        }

        [Test]
        public void Submit_ImageMode_FallsBackToDefaultModelVersion_WhenModelEmpty()
        {
            var http = new FakeHttpTransport { Handler = _ => Json("{\"code\":0,\"data\":{\"task_id\":\"img1\"}}") };
            var adapter = new TripoAdapter();
            var req = new ModelGenRequest
            {
                Provider = "tripo",
                Mode = "image",
                ImageUrl = "https://example.com/in.png", // Model null
            };

            adapter.SubmitAsync(req, "k", http, CancellationToken.None).GetAwaiter().GetResult();

            string body = Encoding.UTF8.GetString(http.RecordedRequests[0].Body);
            StringAssert.Contains("\"model_version\":\"" + TripoAdapter.ModelVersion + "\"", body);
        }

        [Test]
        public void Submit_ImageMode_LocalPathOnly_Throws()
        {
            // Tripo can't take a local image inline (no data-URI support, upload not wired) — it must
            // fail clearly rather than silently falling back to text mode.
            var adapter = new TripoAdapter();
            var req = new ModelGenRequest { Provider = "tripo", Mode = "image", ImagePath = "/tmp/whatever.png" };

            Assert.Throws<System.Exception>(() =>
                adapter.SubmitAsync(req, "k", new FakeHttpTransport(), CancellationToken.None).GetAwaiter().GetResult());
        }
    }
}
