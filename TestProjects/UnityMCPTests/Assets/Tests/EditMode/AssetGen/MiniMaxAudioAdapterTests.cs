using System;
using System.Text;
using System.Threading;
using MCPForUnity.Editor.Services.AssetGen.Http;
using MCPForUnity.Editor.Services.AssetGen.Providers;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace MCPForUnityTests.Editor.AssetGen
{
    public class MiniMaxAudioAdapterTests
    {
        private const string TestKey = "test-provider-key";

        private static HttpResult Json(string body)
            => new HttpResult { Status = 200, IsSuccess = true, Text = body };

        private static HttpResult Completed(string audio)
            => Json("{\"data\":{\"status\":2,\"audio\":\"" + audio
                    + "\"},\"base_resp\":{\"status_code\":0,\"status_msg\":\"success\"}}");

        private static AudioGenRequest Request()
            => new AudioGenRequest
            {
                Provider = "minimax",
                Model = "music-cover",
                Prompt = "Warm acoustic folk cover",
                AudioUrl = "https://example.com/reference.mp3",
                OutputFormat = "url",
                AudioFormat = "mp3"
            };

        private static string SubmittedBody(FakeHttpTransport fake)
            => Encoding.UTF8.GetString(fake.RecordedRequests[0].Body);

        [SetUp]
        public void SetUp() => Environment.SetEnvironmentVariable(MiniMaxAudioAdapter.RegionEnvVar, null);

        [TearDown]
        public void TearDown() => Environment.SetEnvironmentVariable(MiniMaxAudioAdapter.RegionEnvVar, null);

        [Test]
        public void Submit_PostsGlobalCoverRequest_WithBearerAuthorization()
        {
            var fake = new FakeHttpTransport { Handler = _ => Completed("https://example.com/result.mp3") };
            var adapter = new MiniMaxAudioAdapter();

            adapter.SubmitAsync(Request(), TestKey, fake, CancellationToken.None).GetAwaiter().GetResult();

            HttpRequestSpec sent = fake.RecordedRequests[0];
            Assert.AreEqual("POST", sent.Method);
            Assert.AreEqual(MiniMaxAudioAdapter.GlobalEndpoint, sent.Url);
            StringAssert.StartsWith("Bearer ", sent.Headers["Authorization"]);
            JObject body = JObject.Parse(SubmittedBody(fake));
            Assert.AreEqual("music-cover", (string)body["model"]);
            Assert.AreEqual("https://example.com/reference.mp3", (string)body["audio_url"]);
            Assert.AreEqual(false, (bool)body["stream"]);
            Assert.AreEqual("url", (string)body["output_format"]);
            Assert.AreEqual("mp3", (string)body["audio_setting"]["format"]);
        }

        [Test]
        public void Submit_ChinaRegion_UsesChinaEndpoint_AndWatermarkField()
        {
            Environment.SetEnvironmentVariable(MiniMaxAudioAdapter.RegionEnvVar, "cn_zh");
            var request = Request();
            request.AigcWatermark = true;
            var fake = new FakeHttpTransport { Handler = _ => Completed("494433") };

            new MiniMaxAudioAdapter().SubmitAsync(request, TestKey, fake, CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.AreEqual(MiniMaxAudioAdapter.ChinaEndpoint, fake.RecordedRequests[0].Url);
            JObject body = JObject.Parse(SubmittedBody(fake));
            Assert.AreEqual(true, (bool)body["aigc_watermark"]);
        }

        [Test]
        public void Submit_FeatureIdAndRequestOptions_AreSentWithRawAudio()
        {
            var request = Request();
            request.CoverFeatureId = "feature-id";
            request.Lyrics = "[Verse]\nA rewritten verse";
            request.LyricsOptimizer = true;
            request.IsInstrumental = false;
            var fake = new FakeHttpTransport { Handler = _ => Completed("494433") };

            new MiniMaxAudioAdapter().SubmitAsync(request, TestKey, fake, CancellationToken.None)
                .GetAwaiter().GetResult();

            JObject body = JObject.Parse(SubmittedBody(fake));
            Assert.AreEqual("feature-id", (string)body["cover_feature_id"]);
            Assert.AreEqual("[Verse]\nA rewritten verse", (string)body["lyrics"]);
            Assert.AreEqual(true, (bool)body["lyrics_optimizer"]);
            Assert.AreEqual(false, (bool)body["is_instrumental"]);
            Assert.AreEqual("https://example.com/reference.mp3", (string)body["audio_url"]);
            Assert.IsNull(body["audio_base64"]);
        }

        [Test]
        public void Submit_PromptIsOptional()
        {
            var request = Request();
            request.Prompt = null;
            var fake = new FakeHttpTransport { Handler = _ => Completed("494433") };

            new MiniMaxAudioAdapter().SubmitAsync(request, TestKey, fake, CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.IsNull(JObject.Parse(SubmittedBody(fake))["prompt"]);
        }

        [Test]
        public void Submit_Base64Source_IsSent()
        {
            var request = Request();
            request.AudioUrl = null;
            request.AudioBase64 = "SUQz";
            var fake = new FakeHttpTransport { Handler = _ => Completed("494433") };

            new MiniMaxAudioAdapter().SubmitAsync(request, TestKey, fake, CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.AreEqual("SUQz", (string)JObject.Parse(SubmittedBody(fake))["audio_base64"]);
        }

        [TestCase("mp3")]
        [TestCase("wav")]
        [TestCase("pcm")]
        public void Poll_HexResult_DecodesAndPreservesAudioFormat(string format)
        {
            var request = Request();
            request.OutputFormat = "hex";
            request.AudioFormat = format;
            var fake = new FakeHttpTransport { Handler = _ => Completed("494433") };
            var adapter = new MiniMaxAudioAdapter();

            string id = adapter.SubmitAsync(request, TestKey, fake, CancellationToken.None)
                .GetAwaiter().GetResult();
            ProviderPollResult result = adapter.PollAsync(id, TestKey, fake, CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.AreEqual(ProviderPollState.Succeeded, result.State);
            CollectionAssert.AreEqual(new byte[] { 0x49, 0x44, 0x33 }, result.InlineData);
            Assert.AreEqual(format, result.ResultExt);
        }

        [Test]
        public void Poll_UrlResult_ReturnsDownloadUrl()
        {
            var fake = new FakeHttpTransport { Handler = _ => Completed("https://example.com/result.mp3") };
            var adapter = new MiniMaxAudioAdapter();

            string id = adapter.SubmitAsync(Request(), TestKey, fake, CancellationToken.None)
                .GetAwaiter().GetResult();
            ProviderPollResult result = adapter.PollAsync(id, TestKey, fake, CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.AreEqual(ProviderPollState.Succeeded, result.State);
            Assert.AreEqual("https://example.com/result.mp3", result.DownloadUrl);
            Assert.AreEqual("mp3", result.ResultExt);
        }

        [Test]
        public void Submit_NonZeroResponseCode_FailsAndRedactsKey()
        {
            var fake = new FakeHttpTransport
            {
                Handler = _ => Json("{\"base_resp\":{\"status_code\":1004,\"status_msg\":\"bad " + TestKey + "\"}}")
            };
            var adapter = new MiniMaxAudioAdapter();

            string id = adapter.SubmitAsync(Request(), TestKey, fake, CancellationToken.None)
                .GetAwaiter().GetResult();
            ProviderPollResult result = adapter.PollAsync(id, TestKey, fake, CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.AreEqual(ProviderPollState.Failed, result.State);
            StringAssert.DoesNotContain(TestKey, result.Error);
        }

        [Test]
        public void Submit_RejectsMissingOrConflictingCoverSources()
        {
            var request = Request();
            request.AudioUrl = null;
            var adapter = new MiniMaxAudioAdapter();
            var fake = new FakeHttpTransport();

            Assert.Throws<ArgumentException>(() => adapter.SubmitAsync(
                request, TestKey, fake, CancellationToken.None).GetAwaiter().GetResult());

            request.AudioUrl = "https://example.com/reference.mp3";
            request.AudioBase64 = "SUQz";
            Assert.Throws<ArgumentException>(() => adapter.SubmitAsync(
                request, TestKey, fake, CancellationToken.None).GetAwaiter().GetResult());
        }
    }
}
