using System;
using System.Text;
using System.Threading;
using MCPForUnity.Editor.Services.AssetGen.Http;
using MCPForUnity.Editor.Services.AssetGen.Providers;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace MCPForUnityTests.Editor.AssetGen
{
    /// <summary>
    /// Covers the MiniMax music-generation adapter: it posts the region endpoint with a Bearer key,
    /// parses the synchronous response (base_resp.status_code / data.status / data.audio), returns a
    /// hosted URL or a decoded hex payload, honours the region env override, and never leaks the key
    /// to an unexpected host.
    /// </summary>
    public class MiniMaxAudioAdapterTests
    {
        private const string GlobalHost = "api.minimax.io";
        private const string ChinaHost = "api.minimaxi.com";

        private static HttpResult Json(string body) => new HttpResult { Status = 200, IsSuccess = true, Text = body };

        private static HttpResult Completed(string audio) =>
            Json("{\"data\":{\"status\":2,\"audio\":\"" + audio + "\"},\"base_resp\":{\"status_code\":0,\"status_msg\":\"success\"}}");

        private static AudioGenRequest Req(string model = null) =>
            new AudioGenRequest { Provider = "minimax", Model = model, Prompt = "calm ambient background" };

        private static string SubmittedBody(FakeHttpTransport fake) =>
            Encoding.UTF8.GetString(fake.RecordedRequests[0].Body);

        [SetUp]
        public void SetUp() => Environment.SetEnvironmentVariable(MiniMaxAudioAdapter.RegionEnvVar, null);

        [TearDown]
        public void TearDown() => Environment.SetEnvironmentVariable(MiniMaxAudioAdapter.RegionEnvVar, null);

        [Test]
        public void Submit_PostsGlobalEndpoint_WithBearerKey_JsonBody()
        {
            var fake = new FakeHttpTransport { Handler = _ => Completed("https://cdn.minimax.io/a.mp3") };
            var adapter = new MiniMaxAudioAdapter();

            string pid = adapter.SubmitAsync(Req(), "mmkey123", fake, CancellationToken.None).GetAwaiter().GetResult();

            Assert.IsFalse(string.IsNullOrEmpty(pid), "submit must return a non-empty provider job id");
            HttpRequestSpec sent = fake.RecordedRequests[0];
            Assert.AreEqual("POST", sent.Method);
            StringAssert.Contains(GlobalHost, sent.Url);
            Assert.IsTrue(sent.Headers.ContainsKey("Authorization"));
            StringAssert.StartsWith("Bearer ", sent.Headers["Authorization"]);
        }

        [Test]
        public void Submit_DefaultModel_MatchesAdapterConstant()
        {
            var fake = new FakeHttpTransport { Handler = _ => Completed("https://cdn.minimax.io/a.mp3") };
            var adapter = new MiniMaxAudioAdapter();

            adapter.SubmitAsync(Req(), "mmkey123", fake, CancellationToken.None).GetAwaiter().GetResult();

            JObject body = JObject.Parse(SubmittedBody(fake));
            Assert.AreEqual(MiniMaxAudioAdapter.DefaultModel, (string)body["model"]);
        }

        [Test]
        public void Submit_ModelOverride_IsSent()
        {
            var fake = new FakeHttpTransport { Handler = _ => Completed("https://cdn.minimax.io/a.mp3") };
            var adapter = new MiniMaxAudioAdapter();

            adapter.SubmitAsync(Req("music-2.6"), "mmkey123", fake, CancellationToken.None).GetAwaiter().GetResult();

            JObject body = JObject.Parse(SubmittedBody(fake));
            Assert.AreEqual("music-2.6", (string)body["model"]);
        }

        [Test]
        public void Submit_CnRegion_PostsChinaEndpoint()
        {
            Environment.SetEnvironmentVariable(MiniMaxAudioAdapter.RegionEnvVar, "cn");
            var fake = new FakeHttpTransport { Handler = _ => Completed("https://cdn.minimaxi.com/a.mp3") };
            var adapter = new MiniMaxAudioAdapter();

            adapter.SubmitAsync(Req(), "mmkey123", fake, CancellationToken.None).GetAwaiter().GetResult();

            StringAssert.Contains(ChinaHost, fake.RecordedRequests[0].Url);
        }

        [Test]
        public void Poll_UrlResult_Succeeds_WithDownloadUrl_AndMp3Ext()
        {
            var fake = new FakeHttpTransport { Handler = _ => Completed("https://cdn.minimax.io/track.mp3") };
            var adapter = new MiniMaxAudioAdapter();

            string pid = adapter.SubmitAsync(Req(), "mmkey123", fake, CancellationToken.None).GetAwaiter().GetResult();
            ProviderPollResult pr = adapter.PollAsync(pid, "mmkey123", fake, CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(ProviderPollState.Succeeded, pr.State);
            Assert.AreEqual("https://cdn.minimax.io/track.mp3", pr.DownloadUrl);
            Assert.AreEqual("mp3", pr.ResultExt);
            Assert.IsNull(pr.InlineData);
        }

        [Test]
        public void Poll_HexResult_DecodesToInlineData()
        {
            // "ID3" (mp3 header bytes) hex-encoded => 494433.
            var fake = new FakeHttpTransport { Handler = _ => Completed("494433") };
            var adapter = new MiniMaxAudioAdapter();

            string pid = adapter.SubmitAsync(Req(), "mmkey123", fake, CancellationToken.None).GetAwaiter().GetResult();
            ProviderPollResult pr = adapter.PollAsync(pid, "mmkey123", fake, CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(ProviderPollState.Succeeded, pr.State);
            Assert.IsNotNull(pr.InlineData);
            CollectionAssert.AreEqual(new byte[] { 0x49, 0x44, 0x33 }, pr.InlineData);
            Assert.AreEqual("mp3", pr.ResultExt);
        }

        [Test]
        public void Submit_NonZeroStatusCode_Fails_AndRedactsKey()
        {
            var fake = new FakeHttpTransport
            {
                Handler = _ => Json("{\"base_resp\":{\"status_code\":1004,\"status_msg\":\"bad key mmkey123\"}}")
            };
            var adapter = new MiniMaxAudioAdapter();

            string pid = adapter.SubmitAsync(Req(), "mmkey123", fake, CancellationToken.None).GetAwaiter().GetResult();
            ProviderPollResult pr = adapter.PollAsync(pid, "mmkey123", fake, CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(ProviderPollState.Failed, pr.State);
            StringAssert.DoesNotContain("mmkey123", pr.Error);
        }

        [Test]
        public void Submit_InProgressStatus_Fails_NoPollTarget()
        {
            var fake = new FakeHttpTransport
            {
                Handler = _ => Json("{\"data\":{\"status\":1},\"base_resp\":{\"status_code\":0}}")
            };
            var adapter = new MiniMaxAudioAdapter();

            string pid = adapter.SubmitAsync(Req(), "mmkey123", fake, CancellationToken.None).GetAwaiter().GetResult();
            ProviderPollResult pr = adapter.PollAsync(pid, "mmkey123", fake, CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(ProviderPollState.Failed, pr.State);
        }

        [Test]
        public void Submit_CompletedNoAudio_Fails()
        {
            var fake = new FakeHttpTransport
            {
                Handler = _ => Json("{\"data\":{\"status\":2},\"base_resp\":{\"status_code\":0}}")
            };
            var adapter = new MiniMaxAudioAdapter();

            string pid = adapter.SubmitAsync(Req(), "mmkey123", fake, CancellationToken.None).GetAwaiter().GetResult();
            ProviderPollResult pr = adapter.PollAsync(pid, "mmkey123", fake, CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(ProviderPollState.Failed, pr.State);
            StringAssert.Contains("audio", pr.Error);
        }

        [Test]
        public void Submit_HttpError_Throws_RedactsKey()
        {
            var fake = new FakeHttpTransport
            {
                Handler = _ => new HttpResult { Status = 401, IsSuccess = false, Text = "{\"base_resp\":{\"status_msg\":\"unauthorized mmkey123\"}}" }
            };
            var adapter = new MiniMaxAudioAdapter();

            Exception ex = Assert.Throws<Exception>(() =>
                adapter.SubmitAsync(Req(), "mmkey123", fake, CancellationToken.None).GetAwaiter().GetResult());
            StringAssert.DoesNotContain("mmkey123", ex.Message);
        }
    }
}
