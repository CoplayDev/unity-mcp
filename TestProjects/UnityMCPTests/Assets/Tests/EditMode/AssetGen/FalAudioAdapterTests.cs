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
        public void Submit_CassetteMusic_WithDuration_IncludesDuration_ClampsTo180()
        {
            var fake = new FakeHttpTransport { Handler = _ => Json("{\"response_url\":\"" + Resp + "\"}") };
            var adapter = new FalAudioAdapter();

            adapter.SubmitAsync(Req("cassetteai/music-generator", 300f), "falkey123", fake, CancellationToken.None)
                   .GetAwaiter().GetResult();

            JObject body = JObject.Parse(SubmittedBody(fake));
            Assert.AreEqual(180, (int)body["duration"]);
        }

        [Test]
        public void Submit_CassetteMusic_DefaultDuration_IncludesDuration_NotPromptOnly()
        {
            // C2: CassetteAI Music REQUIRES duration; a default (Duration=0) call must still send a
            // valid duration >= 1, not a prompt-only body (which fal rejects with 422).
            var fake = new FakeHttpTransport { Handler = _ => Json("{\"response_url\":\"" + Resp + "\"}") };
            var adapter = new FalAudioAdapter();

            adapter.SubmitAsync(Req("cassetteai/music-generator"), "falkey123", fake, CancellationToken.None)
                   .GetAwaiter().GetResult();

            JObject body = JObject.Parse(SubmittedBody(fake));
            Assert.IsNotNull(body["duration"], "duration must be present for a required-duration model");
            Assert.GreaterOrEqual((int)body["duration"], 1);
        }

        [Test]
        public void Submit_CassetteMusic_FractionalDuration_FloorsToAtLeastOne()
        {
            // C3: Duration=0.5 must not (int)-truncate to 0 (which fal rejects); it clamps to >= 1.
            var fake = new FakeHttpTransport { Handler = _ => Json("{\"response_url\":\"" + Resp + "\"}") };
            var adapter = new FalAudioAdapter();

            adapter.SubmitAsync(Req("cassetteai/music-generator", 0.5f), "falkey123", fake, CancellationToken.None)
                   .GetAwaiter().GetResult();

            JObject body = JObject.Parse(SubmittedBody(fake));
            Assert.GreaterOrEqual((int)body["duration"], 1);
        }

        [Test]
        public void Submit_CassetteMusic_FractionalDuration_Floors_NotRounds()
        {
            // Duration must be floored (not rounded) so it never exceeds the requested value:
            // 10.9 -> 10, not 11.
            var fake = new FakeHttpTransport { Handler = _ => Json("{\"response_url\":\"" + Resp + "\"}") };
            var adapter = new FalAudioAdapter();

            adapter.SubmitAsync(Req("cassetteai/music-generator", 10.9f), "falkey123", fake, CancellationToken.None)
                   .GetAwaiter().GetResult();

            JObject body = JObject.Parse(SubmittedBody(fake));
            Assert.AreEqual(10, (int)body["duration"]);
        }

        [Test]
        public void Submit_Lyria_PromptOnly()
        {
            var fake = new FakeHttpTransport { Handler = _ => Json("{\"response_url\":\"" + Resp + "\"}") };
            var adapter = new FalAudioAdapter();

            adapter.SubmitAsync(Req("fal-ai/lyria2", 30f), "falkey123", fake, CancellationToken.None).GetAwaiter().GetResult();

            string body = SubmittedBody(fake);
            StringAssert.DoesNotContain("seconds_total", body);
            StringAssert.DoesNotContain("duration", body);
            StringAssert.Contains("prompt", body);
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

        [Test]
        public void Poll_UnknownStatus_FailsFast_NotRunning()
        {
            // C4: an unmapped terminal status must fail immediately, not poll until the job timeout.
            var fake = new FakeHttpTransport { Handler = _ => Json("{\"status\":\"WEIRD_UNKNOWN\"}") };
            var adapter = new FalAudioAdapter();

            ProviderPollResult pr = adapter.PollAsync(Resp, "falkey123", fake, CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(ProviderPollState.Failed, pr.State);
            StringAssert.Contains("WEIRD_UNKNOWN", pr.Error);
        }

        [Test]
        public void Poll_ForeignHost_Throws_AndSendsNothing()
        {
            // H3: the key must never be attached to a provider-controlled host.
            var fake = new FakeHttpTransport();
            var adapter = new FalAudioAdapter();

            Assert.Throws<System.Exception>(() =>
                adapter.PollAsync("https://attacker.example/harvest", "falkey123", fake, CancellationToken.None)
                       .GetAwaiter().GetResult());
            Assert.IsEmpty(fake.RecordedRequests, "no request (and no key) may be sent to a foreign host");
        }

        [Test]
        public void Submit_ForeignResponseUrl_Throws()
        {
            // H3: a poisoned response_url from the provider must be rejected before it's used to poll.
            var fake = new FakeHttpTransport { Handler = _ => Json("{\"response_url\":\"https://evil.example/x\"}") };
            var adapter = new FalAudioAdapter();

            Assert.Throws<System.Exception>(() =>
                adapter.SubmitAsync(Req(), "falkey123", fake, CancellationToken.None).GetAwaiter().GetResult());
        }
    }
}
