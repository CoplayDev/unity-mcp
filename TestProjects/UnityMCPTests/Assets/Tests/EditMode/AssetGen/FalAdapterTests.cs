using System.Threading;
using MCPForUnity.Editor.Services.AssetGen.Http;
using MCPForUnity.Editor.Services.AssetGen.Providers;
using NUnit.Framework;

namespace MCPForUnityTests.Editor.AssetGen
{
    public class FalAdapterTests
    {
        private const string Resp = "https://queue.fal.run/fal-ai/flux/dev/requests/r1";

        private static HttpResult Json(string body) => new HttpResult { Status = 200, IsSuccess = true, Text = body };

        private static ImageGenRequest Req() => new ImageGenRequest { Provider = "fal", Mode = "text", Prompt = "a cat" };

        [Test]
        public void Submit_PostsModelEndpoint_WithKeyHeader_ReturnsResponseUrl()
        {
            var fake = new FakeHttpTransport
            {
                Handler = spec => Json("{\"request_id\":\"r1\",\"response_url\":\"" + Resp + "\"}")
            };
            var adapter = new FalAdapter();

            string pid = adapter.SubmitAsync(Req(), "falkey123", fake, CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(Resp, pid);
            HttpRequestSpec sent = fake.RecordedRequests[0];
            Assert.AreEqual("POST", sent.Method);
            StringAssert.Contains("fal-ai/flux/dev", sent.Url);
            Assert.IsTrue(sent.Headers.ContainsKey("Authorization"));
            StringAssert.StartsWith("Key ", sent.Headers["Authorization"]);
        }

        [Test]
        public void Poll_Completed_FetchesResult_ReturnsImageUrl()
        {
            var fake = new FakeHttpTransport
            {
                Handler = spec =>
                {
                    if (spec.Url.EndsWith("/status")) return Json("{\"status\":\"COMPLETED\"}");
                    return Json("{\"images\":[{\"url\":\"https://cdn.example.com/img.png\"}]}");
                }
            };
            var adapter = new FalAdapter();

            ProviderPollResult pr = adapter.PollAsync(Resp, "falkey123", fake, CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(ProviderPollState.Succeeded, pr.State);
            Assert.AreEqual("https://cdn.example.com/img.png", pr.DownloadUrl);
        }

        [Test]
        public void Poll_InProgress_ReturnsRunning()
        {
            var fake = new FakeHttpTransport { Handler = spec => Json("{\"status\":\"IN_PROGRESS\"}") };
            var adapter = new FalAdapter();

            ProviderPollResult pr = adapter.PollAsync(Resp, "falkey123", fake, CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(ProviderPollState.Running, pr.State);
        }
    }
}
