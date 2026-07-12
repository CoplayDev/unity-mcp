using System.Text;
using System.Threading;
using MCPForUnity.Editor.Services.AssetGen.Http;
using MCPForUnity.Editor.Services.AssetGen.Providers;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace MCPForUnityTests.Editor.AssetGen
{
    public class FalAudioAdapterTests
    {
        private const string StableAudio = "fal-ai/stable-audio-25/text-to-audio";
        private const string Resp = "https://queue.fal.run/fal-ai/stable-audio-25/text-to-audio/requests/r1";

        private static HttpResult Json(string body) => new HttpResult { Status = 200, IsSuccess = true, Text = body };

        private static AudioGenRequest Req(string model = null, float duration = 0f) =>
            new AudioGenRequest { Provider = "fal", Model = model, Prompt = "gentle rain", Duration = duration };

        private static string SubmittedBody(FakeHttpTransport fake) =>
            Encoding.UTF8.GetString(fake.RecordedRequests[0].Body);

        [Test]
        public void Submit_PostsModelEndpoint_WithKeyHeader_ReturnsResponseUrl()
        {
            var fake = new FakeHttpTransport
            {
                Handler = _ => Json("{\"request_id\":\"r1\",\"response_url\":\"" + Resp + "\"}")
            };
            var adapter = new FalAudioAdapter();

            string pid = adapter.SubmitAsync(Req(), "falkey123", fake, CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(Resp, pid);
            HttpRequestSpec sent = fake.RecordedRequests[0];
            Assert.AreEqual("POST", sent.Method);
            StringAssert.Contains(StableAudio, sent.Url);
            Assert.IsTrue(sent.Headers.ContainsKey("Authorization"));
            StringAssert.StartsWith("Key ", sent.Headers["Authorization"]);
        }

        [Test]
        public void Submit_ModelOverride_UsesModelEndpoint()
        {
            var fake = new FakeHttpTransport { Handler = _ => Json("{\"response_url\":\"" + Resp + "\"}") };
            var adapter = new FalAudioAdapter();

            adapter.SubmitAsync(Req("cassetteai/sound-effects-generator"), "falkey123", fake, CancellationToken.None)
                   .GetAwaiter().GetResult();

            StringAssert.Contains("cassetteai/sound-effects-generator", fake.RecordedRequests[0].Url);
        }

        [Test]
        public void Submit_StableAudio_WithDuration_IncludesSecondsTotal()
        {
            var fake = new FakeHttpTransport { Handler = _ => Json("{\"response_url\":\"" + Resp + "\"}") };
            var adapter = new FalAudioAdapter();

            adapter.SubmitAsync(Req(StableAudio, 30f), "falkey123", fake, CancellationToken.None).GetAwaiter().GetResult();

            StringAssert.Contains("seconds_total", SubmittedBody(fake));
        }

        [Test]
        public void Submit_StableAudio_OverMax_ClampsTo190()
        {
            var fake = new FakeHttpTransport { Handler = _ => Json("{\"response_url\":\"" + Resp + "\"}") };
            var adapter = new FalAudioAdapter();

            adapter.SubmitAsync(Req(StableAudio, 250f), "falkey123", fake, CancellationToken.None).GetAwaiter().GetResult();

            JObject body = JObject.Parse(SubmittedBody(fake));
            Assert.AreEqual(190, (int)body["seconds_total"]);
        }

        [Test]
        public void Submit_CassetteSfx_WithDuration_IncludesDuration_ClampsTo30()
        {
            var fake = new FakeHttpTransport { Handler = _ => Json("{\"response_url\":\"" + Resp + "\"}") };
            var adapter = new FalAudioAdapter();

            adapter.SubmitAsync(Req("cassetteai/sound-effects-generator", 45f), "falkey123", fake, CancellationToken.None)
                   .GetAwaiter().GetResult();

            JObject body = JObject.Parse(SubmittedBody(fake));
            Assert.AreEqual(30, (int)body["duration"]);
        }

        [Test]
        public void Submit_Lyria_And_CassetteMusic_PromptOnly()
        {
            foreach (string model in new[] { "fal-ai/lyria2", "cassetteai/music-generator" })
            {
                var fake = new FakeHttpTransport { Handler = _ => Json("{\"response_url\":\"" + Resp + "\"}") };
                var adapter = new FalAudioAdapter();

                adapter.SubmitAsync(Req(model, 30f), "falkey123", fake, CancellationToken.None).GetAwaiter().GetResult();

                string body = SubmittedBody(fake);
                StringAssert.DoesNotContain("seconds_total", body);
                StringAssert.DoesNotContain("duration", body);
                StringAssert.Contains("prompt", body);
            }
        }

        [Test]
        public void Submit_FallbackResponseUrl_UsesModelRequestsPath()
        {
            var fake = new FakeHttpTransport { Handler = _ => Json("{\"request_id\":\"r1\"}") }; // no response_url
            var adapter = new FalAudioAdapter();

            string pid = adapter.SubmitAsync(Req(), "falkey123", fake, CancellationToken.None).GetAwaiter().GetResult();

            StringAssert.Contains(StableAudio + "/requests/r1", pid);
        }

        [Test]
        public void Poll_Status_KeyHeaderPresent()
        {
            var fake = new FakeHttpTransport
            {
                Handler = spec => spec.Url.EndsWith("/status")
                    ? Json("{\"status\":\"COMPLETED\"}")
                    : Json("{\"audio_file\":{\"url\":\"https://cdn.example.com/a.wav\"}}")
            };
            var adapter = new FalAudioAdapter();

            adapter.PollAsync(Resp, "falkey123", fake, CancellationToken.None).GetAwaiter().GetResult();

            foreach (HttpRequestSpec sent in fake.RecordedRequests)
            {
                Assert.IsTrue(sent.Headers.ContainsKey("Authorization"));
                StringAssert.StartsWith("Key ", sent.Headers["Authorization"]);
            }
        }

        [Test]
        public void Poll_Completed_WavResult_ReturnsWavExt()
        {
            var fake = new FakeHttpTransport
            {
                Handler = spec => spec.Url.EndsWith("/status")
                    ? Json("{\"status\":\"COMPLETED\"}")
                    : Json("{\"audio_file\":{\"url\":\"https://cdn.example.com/a.wav\"}}")
            };
            var adapter = new FalAudioAdapter();

            ProviderPollResult pr = adapter.PollAsync(Resp, "falkey123", fake, CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(ProviderPollState.Succeeded, pr.State);
            Assert.AreEqual("https://cdn.example.com/a.wav", pr.DownloadUrl);
            Assert.AreEqual("wav", pr.ResultExt);
        }

        [Test]
        public void Poll_Completed_Mp3Result_ReturnsMp3Ext()
        {
            var fake = new FakeHttpTransport
            {
                Handler = spec => spec.Url.EndsWith("/status")
                    ? Json("{\"status\":\"COMPLETED\"}")
                    : Json("{\"audio_file\":{\"url\":\"https://cdn.example.com/track.mp3\"}}")
            };
            var adapter = new FalAudioAdapter();

            ProviderPollResult pr = adapter.PollAsync(Resp, "falkey123", fake, CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual("mp3", pr.ResultExt);
        }

        [Test]
        public void Poll_AudioShape_And_BareUrl_Extracted()
        {
            foreach (string result in new[]
            {
                "{\"audio\":{\"url\":\"https://cdn.example.com/a.wav\"}}",
                "{\"audio_url\":\"https://cdn.example.com/a.wav\"}"
            })
            {
                var fake = new FakeHttpTransport
                {
                    Handler = spec => spec.Url.EndsWith("/status") ? Json("{\"status\":\"COMPLETED\"}") : Json(result)
                };
                var adapter = new FalAudioAdapter();

                ProviderPollResult pr = adapter.PollAsync(Resp, "falkey123", fake, CancellationToken.None).GetAwaiter().GetResult();

                Assert.AreEqual(ProviderPollState.Succeeded, pr.State);
                Assert.AreEqual("https://cdn.example.com/a.wav", pr.DownloadUrl);
            }
        }

        [Test]
        public void Poll_Completed_NoUrl_Fails()
        {
            var fake = new FakeHttpTransport
            {
                Handler = spec => spec.Url.EndsWith("/status") ? Json("{\"status\":\"COMPLETED\"}") : Json("{}")
            };
            var adapter = new FalAudioAdapter();

            ProviderPollResult pr = adapter.PollAsync(Resp, "falkey123", fake, CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(ProviderPollState.Failed, pr.State);
            StringAssert.Contains("audio", pr.Error);
        }

        [Test]
        public void Poll_InProgress_Running()
        {
            var fake = new FakeHttpTransport { Handler = _ => Json("{\"status\":\"IN_PROGRESS\"}") };
            var adapter = new FalAudioAdapter();

            ProviderPollResult pr = adapter.PollAsync(Resp, "falkey123", fake, CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(ProviderPollState.Running, pr.State);
        }

        [Test]
        public void Poll_InQueue_Queued()
        {
            var fake = new FakeHttpTransport { Handler = _ => Json("{\"status\":\"IN_QUEUE\"}") };
            var adapter = new FalAudioAdapter();

            ProviderPollResult pr = adapter.PollAsync(Resp, "falkey123", fake, CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(ProviderPollState.Queued, pr.State);
        }

        [Test]
        public void Poll_Failed_RedactsError()
        {
            var fake = new FakeHttpTransport
            {
                Handler = _ => Json("{\"status\":\"ERROR\",\"error\":\"boom falkey123 leaked\"}")
            };
            var adapter = new FalAudioAdapter();

            ProviderPollResult pr = adapter.PollAsync(Resp, "falkey123", fake, CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(ProviderPollState.Failed, pr.State);
            StringAssert.DoesNotContain("falkey123", pr.Error);
        }
    }
}
