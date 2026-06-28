using System.Text;
using System.Threading;
using MCPForUnity.Editor.Services.AssetGen.Http;
using MCPForUnity.Editor.Services.AssetGen.Providers;
using NUnit.Framework;

namespace MCPForUnityTests.Editor.AssetGen
{
    /// <summary>
    /// Exercises the Meshy adapter against a <see cref="FakeHttpTransport"/> — no network.
    /// Asserts request shaping (endpoint, method, Bearer scheme), the task id field, and
    /// poll-state mapping. The bearer secret is never asserted beyond the "Bearer " prefix.
    /// </summary>
    public class MeshyAdapterTests
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
        public void Submit_PostsTextEndpoint_WithBearerHeader_AndReturnsResultId()
        {
            var http = new FakeHttpTransport { Handler = _ => Json("{\"result\":\"task_meshy_1\"}") };
            var adapter = new MeshyAdapter();
            var req = new ModelGenRequest { Provider = "meshy", Mode = "text", Prompt = "a brass lantern", Format = "glb" };

            string taskId = adapter.SubmitAsync(req, "msy_secret_value", http, CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.AreEqual("task_meshy_1", taskId);
            HttpRequestSpec rec = http.RecordedRequests[0];
            StringAssert.Contains("/openapi/v2/text-to-3d", rec.Url);
            Assert.AreEqual("POST", rec.Method);
            Assert.IsTrue(rec.Headers.ContainsKey("Authorization"));
            StringAssert.StartsWith("Bearer ", rec.Headers["Authorization"]);

            string body = Encoding.UTF8.GetString(rec.Body);
            StringAssert.Contains("a brass lantern", body);
            StringAssert.Contains("preview", body);
        }

        [Test]
        public void Poll_Succeeded_ReturnsGlbUrl()
        {
            var http = new FakeHttpTransport
            {
                Handler = _ => Json(
                    "{\"status\":\"SUCCEEDED\",\"progress\":100," +
                    "\"model_urls\":{\"glb\":\"https://assets.meshy.ai/model.glb\",\"fbx\":\"https://assets.meshy.ai/model.fbx\"}}")
            };
            var adapter = new MeshyAdapter();
            var req = new ModelGenRequest { Provider = "meshy", Mode = "text", Prompt = "x", Format = "glb" };
            adapter.SubmitAsync(req, "k", new FakeHttpTransport { Handler = _ => Json("{\"result\":\"id1\"}") }, CancellationToken.None)
                .GetAwaiter().GetResult();

            ProviderPollResult res = adapter.PollAsync("id1", "k", http, CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(ProviderPollState.Succeeded, res.State);
            Assert.AreEqual("https://assets.meshy.ai/model.glb", res.DownloadUrl);
            Assert.AreEqual(1f, res.Progress, 0.001f);
        }

        [Test]
        public void Poll_InProgress_ReturnsRunning_WithProgress()
        {
            var http = new FakeHttpTransport { Handler = _ => Json("{\"status\":\"IN_PROGRESS\",\"progress\":37}") };
            var adapter = new MeshyAdapter();

            ProviderPollResult res = adapter.PollAsync("id1", "k", http, CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(ProviderPollState.Running, res.State);
            Assert.AreEqual(0.37f, res.Progress, 0.001f);
        }

        [Test]
        public void Poll_Failed_MapsFailed_WithError()
        {
            var http = new FakeHttpTransport
            {
                Handler = _ => Json("{\"status\":\"FAILED\",\"progress\":0,\"task_error\":{\"message\":\"render error\"}}")
            };
            var adapter = new MeshyAdapter();

            ProviderPollResult res = adapter.PollAsync("id1", "k", http, CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(ProviderPollState.Failed, res.State);
            Assert.IsNotEmpty(res.Error);
        }
    }
}
