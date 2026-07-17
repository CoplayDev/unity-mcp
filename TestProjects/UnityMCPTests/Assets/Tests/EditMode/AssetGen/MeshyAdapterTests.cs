using System;
using System.IO;
using System.Text;
using System.Threading;
using MCPForUnity.Editor.Services.AssetGen.Http;
using MCPForUnity.Editor.Services.AssetGen.Providers;
using NUnit.Framework;
using UnityEngine;

namespace MCPForUnityTests.Editor.AssetGen
{
    /// <summary>
    /// Exercises the Meshy adapter against a <see cref="FakeHttpTransport"/> — no network.
    /// Asserts request shaping (endpoint, method, Bearer scheme), the task id field, and
    /// poll-state mapping. The bearer secret is never asserted beyond the "Bearer " prefix.
    /// </summary>
    public class MeshyAdapterTests
    {
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
        public void Submit_UsesRequestModel_WhenProvided()
        {
            var http = new FakeHttpTransport { Handler = _ => Json("{\"result\":\"t\"}") };
            var adapter = new MeshyAdapter();
            var req = new ModelGenRequest { Provider = "meshy", Mode = "text", Prompt = "x", Model = "meshy-5" };

            adapter.SubmitAsync(req, "k", http, CancellationToken.None).GetAwaiter().GetResult();

            string body = Encoding.UTF8.GetString(http.RecordedRequests[0].Body);
            StringAssert.Contains("\"ai_model\":\"meshy-5\"", body);
        }

        [Test]
        public void Submit_FallsBackToDefault_WhenModelEmpty()
        {
            var http = new FakeHttpTransport { Handler = _ => Json("{\"result\":\"t\"}") };
            var adapter = new MeshyAdapter();
            var req = new ModelGenRequest { Provider = "meshy", Mode = "text", Prompt = "x" }; // Model null

            adapter.SubmitAsync(req, "k", http, CancellationToken.None).GetAwaiter().GetResult();

            string body = Encoding.UTF8.GetString(http.RecordedRequests[0].Body);
            StringAssert.Contains("\"ai_model\":\"" + MeshyAdapter.DefaultModel + "\"", body);
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
            // Texture=false -> single-phase (no refine), so a SUCCEEDED preview surfaces directly.
            var req = new ModelGenRequest { Provider = "meshy", Mode = "text", Prompt = "x", Format = "glb", Texture = false };
            adapter.SubmitAsync(req, "k", new FakeHttpTransport { Handler = _ => Json("{\"result\":\"id1\"}") }, CancellationToken.None)
                .GetAwaiter().GetResult();

            ProviderPollResult res = adapter.PollAsync("id1", "k", http, CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(ProviderPollState.Succeeded, res.State);
            Assert.AreEqual("https://assets.meshy.ai/model.glb", res.DownloadUrl);
            Assert.AreEqual(1f, res.Progress, 0.001f);
        }

        [Test]
        public void Submit_NoTaskIdInResponse_ErrorIncludesBody()
        {
            var fake = new FakeHttpTransport { Handler = _ => Json("{\"message\":\"quota exceeded\"}") };
            var adapter = new MeshyAdapter();
            var req = new ModelGenRequest { Provider = "meshy", Mode = "text", Prompt = "x", Texture = false };

            var ex = Assert.Throws<System.Exception>(() =>
                adapter.SubmitAsync(req, "k", fake, CancellationToken.None).GetAwaiter().GetResult());
            StringAssert.Contains("quota exceeded", ex.Message);
        }

        [Test]
        public void Poll_InProgress_ReturnsRunning_WithProgress()
        {
            var http = new FakeHttpTransport { Handler = _ => Json("{\"status\":\"IN_PROGRESS\",\"progress\":37}") };
            var adapter = new MeshyAdapter();
            // Submit single-phase (Texture=false) so progress is reported raw (not split across refine).
            adapter.SubmitAsync(
                new ModelGenRequest { Provider = "meshy", Mode = "text", Prompt = "x", Texture = false },
                "k", new FakeHttpTransport { Handler = _ => Json("{\"result\":\"id1\"}") }, CancellationToken.None)
                .GetAwaiter().GetResult();

            ProviderPollResult res = adapter.PollAsync("id1", "k", http, CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(ProviderPollState.Running, res.State);
            Assert.AreEqual(0.37f, res.Progress, 0.001f);
        }

        [Test]
        public void Submit_ImageMode_PollsV1ImageEndpoint()
        {
            var submitFake = new FakeHttpTransport { Handler = _ => Json("{\"result\":\"img1\"}") };
            var pollFake = new FakeHttpTransport
            {
                Handler = _ => Json("{\"status\":\"SUCCEEDED\",\"progress\":100,\"model_urls\":{\"glb\":\"https://m/i.glb\"}}")
            };
            var adapter = new MeshyAdapter();
            var req = new ModelGenRequest { Provider = "meshy", Mode = "image", ImageUrl = "https://ex.com/ref.png", Format = "glb" };

            string id = adapter.SubmitAsync(req, "k", submitFake, CancellationToken.None).GetAwaiter().GetResult();
            Assert.AreEqual("img1", id);
            StringAssert.Contains("/openapi/v1/image-to-3d", submitFake.RecordedRequests[0].Url);

            ProviderPollResult res = adapter.PollAsync(id, "k", pollFake, CancellationToken.None).GetAwaiter().GetResult();

            // Image tasks must be polled at the v1 image endpoint, not the v2 text endpoint.
            StringAssert.Contains("/openapi/v1/image-to-3d/img1", pollFake.RecordedRequests[0].Url);
            Assert.AreEqual(ProviderPollState.Succeeded, res.State);
            Assert.AreEqual("https://m/i.glb", res.DownloadUrl);
        }

        [Test]
        public void Submit_ImageMode_LocalPath_SendsDataUri()
        {
            string rel = WriteProjectFile("Assets/Generated/__assetgen_meshy_adapter/ref.png", new byte[] { 137, 80, 78, 71 });
            try
            {
                var fake = new FakeHttpTransport { Handler = _ => Json("{\"result\":\"id1\"}") };
                var adapter = new MeshyAdapter();
                var req = new ModelGenRequest { Provider = "meshy", Mode = "image", ImagePath = rel, Format = "glb" };

                adapter.SubmitAsync(req, "k", fake, CancellationToken.None).GetAwaiter().GetResult();

                HttpRequestSpec rec = fake.RecordedRequests[0];
                StringAssert.Contains("/openapi/v1/image-to-3d", rec.Url);
                StringAssert.Contains("data:image/png;base64,", Encoding.UTF8.GetString(rec.Body));
            }
            finally { try { Directory.Delete(Path.Combine(ProjectRoot(), "Assets/Generated/__assetgen_meshy_adapter"), true); } catch { } }
        }

        [Test]
        public void TextWithTexture_PreviewSucceeded_SubmitsRefine_ThenReturnsTexturedModel()
        {
            var submitFake = new FakeHttpTransport { Handler = _ => Json("{\"result\":\"prev1\"}") };
            var pollFake = new FakeHttpTransport
            {
                Handler = spec => spec.Method == "POST"
                    ? Json("{\"result\":\"refine1\"}")                                   // refine submit
                    : Json("{\"status\":\"SUCCEEDED\",\"progress\":100,\"model_urls\":{\"glb\":\"https://m/refined.glb\"}}")
            };
            var adapter = new MeshyAdapter();
            // Texture defaults to true -> two-phase preview+refine.
            var req = new ModelGenRequest { Provider = "meshy", Mode = "text", Prompt = "a chair", Format = "glb" };

            string previewId = adapter.SubmitAsync(req, "k", submitFake, CancellationToken.None).GetAwaiter().GetResult();
            Assert.AreEqual("prev1", previewId);

            // Poll #1: preview SUCCEEDED -> adapter submits a refine task and reports Running.
            ProviderPollResult p1 = adapter.PollAsync(previewId, "k", pollFake, CancellationToken.None).GetAwaiter().GetResult();
            Assert.AreEqual(ProviderPollState.Running, p1.State);

            bool refinePosted = false;
            foreach (HttpRequestSpec r in pollFake.RecordedRequests)
            {
                if (r.Method == "POST" && r.Body != null)
                {
                    string b = Encoding.UTF8.GetString(r.Body);
                    if (b.Contains("refine") && b.Contains("prev1")) refinePosted = true;
                }
            }
            Assert.IsTrue(refinePosted, "expected a refine POST carrying the preview_task_id");

            // Poll #2: the refine task SUCCEEDED -> textured model url surfaced.
            ProviderPollResult p2 = adapter.PollAsync(previewId, "k", pollFake, CancellationToken.None).GetAwaiter().GetResult();
            Assert.AreEqual(ProviderPollState.Succeeded, p2.State);
            Assert.AreEqual("https://m/refined.glb", p2.DownloadUrl);
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
