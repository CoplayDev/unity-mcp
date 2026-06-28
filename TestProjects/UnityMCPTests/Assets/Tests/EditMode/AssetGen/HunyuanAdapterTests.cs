using System.Text;
using System.Threading;
using MCPForUnity.Editor.Services.AssetGen.Http;
using MCPForUnity.Editor.Services.AssetGen.Providers;
using NUnit.Framework;

namespace MCPForUnityTests.Editor.AssetGen
{
    /// <summary>
    /// Exercises the Hunyuan adapter against a <see cref="FakeHttpTransport"/>. The TC3 signer
    /// runs for real (the fake ignores headers), so this confirms request shaping, JobId parsing,
    /// and the DONE→Succeeded mapping with a zip <c>ResultExt</c>. The secret pair never logs.
    /// </summary>
    public class HunyuanAdapterTests
    {
        // The Hunyuan provider key is a JSON blob carrying the Tencent secret pair.
        private const string Key = "{\"secretId\":\"AKIDexample\",\"secretKey\":\"secretexample\"}";

        private static HttpResult Json(string json)
            => new HttpResult { Status = 200, IsSuccess = true, Text = json, Body = Encoding.UTF8.GetBytes(json) };

        [Test]
        public void Submit_ParsesJobId()
        {
            var http = new FakeHttpTransport { Handler = _ => Json("{\"Response\":{\"JobId\":\"job-123\",\"RequestId\":\"r1\"}}") };
            var adapter = new HunyuanAdapter();
            var req = new ModelGenRequest { Provider = "hunyuan", Mode = "text", Prompt = "a stone golem" };

            string jobId = adapter.SubmitAsync(req, Key, http, CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual("job-123", jobId);
            HttpRequestSpec rec = http.RecordedRequests[0];
            Assert.AreEqual("POST", rec.Method);
            StringAssert.Contains("ai3d.tencentcloudapi.com", rec.Url);
            Assert.IsTrue(rec.Headers.ContainsKey("Authorization"));
            StringAssert.StartsWith("TC3-HMAC-SHA256 ", rec.Headers["Authorization"]);
            Assert.AreEqual("SubmitHunyuanTo3DJob", rec.Headers["X-TC-Action"]);
        }

        [Test]
        public void Poll_Done_Succeeds_WithZipResultExt()
        {
            var http = new FakeHttpTransport
            {
                Handler = _ => Json(
                    "{\"Response\":{\"Status\":\"DONE\"," +
                    "\"ResultFile3Ds\":[{\"Url\":\"https://hunyuan.example.com/result.zip\"}]}}")
            };
            var adapter = new HunyuanAdapter();

            ProviderPollResult res = adapter.PollAsync("job-123", Key, http, CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(ProviderPollState.Succeeded, res.State);
            Assert.AreEqual("https://hunyuan.example.com/result.zip", res.DownloadUrl);
            Assert.AreEqual("zip", res.ResultExt);
        }

        [Test]
        public void Poll_Run_ReturnsRunning()
        {
            var http = new FakeHttpTransport { Handler = _ => Json("{\"Response\":{\"Status\":\"RUN\"}}") };
            var adapter = new HunyuanAdapter();

            ProviderPollResult res = adapter.PollAsync("job-123", Key, http, CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(ProviderPollState.Running, res.State);
        }

        [Test]
        public void Poll_Fail_ReturnsFailed()
        {
            var http = new FakeHttpTransport
            {
                Handler = _ => Json("{\"Response\":{\"Status\":\"FAIL\",\"ErrorMessage\":\"quota exceeded\"}}")
            };
            var adapter = new HunyuanAdapter();

            ProviderPollResult res = adapter.PollAsync("job-123", Key, http, CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(ProviderPollState.Failed, res.State);
            Assert.IsNotEmpty(res.Error);
        }

        [Test]
        public void Submit_InvalidKeyBlob_Throws()
        {
            var http = new FakeHttpTransport { Handler = _ => Json("{\"Response\":{\"JobId\":\"x\"}}") };
            var adapter = new HunyuanAdapter();
            var req = new ModelGenRequest { Provider = "hunyuan", Mode = "text", Prompt = "x" };

            Assert.Throws<System.Exception>(() =>
                adapter.SubmitAsync(req, "not-json", http, CancellationToken.None).GetAwaiter().GetResult());
        }
    }
}
