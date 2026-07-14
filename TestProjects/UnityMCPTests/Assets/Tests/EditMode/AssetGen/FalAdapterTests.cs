using System.Threading;
using MCPForUnity.Editor.Services.AssetGen.Http;
using MCPForUnity.Editor.Services.AssetGen.Providers;
using NUnit.Framework;

namespace MCPForUnityTests.Editor.AssetGen
{
    public class FalAdapterTests
    {
        private const string Resp = "https://queue.fal.run/fal-ai/flux-2/requests/r1";

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
            StringAssert.Contains("fal-ai/flux-2", sent.Url);
            Assert.IsTrue(sent.Headers.ContainsKey("Authorization"));
            StringAssert.StartsWith("Key ", sent.Headers["Authorization"]);
        }

        [Test]
        public void Submit_WithDimensions_IncludesImageSize()
        {
            var fake = new FakeHttpTransport
            {
                Handler = spec => Json("{\"request_id\":\"r1\",\"response_url\":\"" + Resp + "\"}")
            };
            var adapter = new FalAdapter();
            var req = new ImageGenRequest { Provider = "fal", Mode = "text", Prompt = "a cat", Width = 512, Height = 768 };

            adapter.SubmitAsync(req, "falkey123", fake, CancellationToken.None).GetAwaiter().GetResult();

            string sent = System.Text.Encoding.UTF8.GetString(fake.RecordedRequests[0].Body);
            StringAssert.Contains("image_size", sent);
            StringAssert.Contains("512", sent);
            StringAssert.Contains("768", sent);
        }

        [Test]
        public void Submit_ImageMode_UsesEditEndpoint_WithImageUrlsArray()
        {
            var fake = new FakeHttpTransport { Handler = _ => Json("{\"response_url\":\"" + Resp + "\"}") };
            var adapter = new FalAdapter();
            var req = new ImageGenRequest { Provider = "fal", Mode = "image", Prompt = "make it night", ImageUrl = "https://ex.com/in.png" };

            adapter.SubmitAsync(req, "falkey123", fake, CancellationToken.None).GetAwaiter().GetResult();

            HttpRequestSpec rec = fake.RecordedRequests[0];
            StringAssert.Contains("/edit", rec.Url);
            string body = System.Text.Encoding.UTF8.GetString(rec.Body);
            StringAssert.Contains("image_urls", body);
            StringAssert.Contains("https://ex.com/in.png", body);
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
        public void Submit_ImageMode_OmitsImageSize_EvenWithDimensions()
        {
            var fake = new FakeHttpTransport { Handler = _ => Json("{\"response_url\":\"" + Resp + "\"}") };
            var adapter = new FalAdapter();
            var req = new ImageGenRequest { Provider = "fal", Mode = "image", Prompt = "edit", ImageUrl = "https://ex.com/in.png", Width = 512, Height = 512 };

            adapter.SubmitAsync(req, "falkey123", fake, CancellationToken.None).GetAwaiter().GetResult();

            string body = System.Text.Encoding.UTF8.GetString(fake.RecordedRequests[0].Body);
            StringAssert.DoesNotContain("image_size", body); // /edit derives size from the source image
        }

        [Test]
        public void Submit_ImageMode_FallbackResponseUrl_UsesBaseModelPath()
        {
            var fake = new FakeHttpTransport { Handler = _ => Json("{\"request_id\":\"r1\"}") }; // no response_url
            var adapter = new FalAdapter();
            var req = new ImageGenRequest { Provider = "fal", Mode = "image", Prompt = "edit", ImageUrl = "https://ex.com/in.png" };

            string pid = adapter.SubmitAsync(req, "falkey123", fake, CancellationToken.None).GetAwaiter().GetResult();

            StringAssert.Contains("/fal-ai/flux-2/requests/r1", pid);
            StringAssert.DoesNotContain("/edit/requests", pid);
        }

        [Test]
        public void Poll_InProgress_ReturnsRunning()
        {
            var fake = new FakeHttpTransport { Handler = spec => Json("{\"status\":\"IN_PROGRESS\"}") };
            var adapter = new FalAdapter();

            ProviderPollResult pr = adapter.PollAsync(Resp, "falkey123", fake, CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(ProviderPollState.Running, pr.State);
        }

        [Test]
        public void Poll_UnknownStatus_FailsFast_NotRunning()
        {
            // C4: an unmapped terminal status must fail immediately, not poll until the job timeout.
            var fake = new FakeHttpTransport { Handler = _ => Json("{\"status\":\"WEIRD_UNKNOWN\"}") };
            var adapter = new FalAdapter();

            ProviderPollResult pr = adapter.PollAsync(Resp, "falkey123", fake, CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(ProviderPollState.Failed, pr.State);
            StringAssert.Contains("WEIRD_UNKNOWN", pr.Error);
        }

        [Test]
        public void Poll_ForeignHost_Throws_AndSendsNothing()
        {
            // H3: the key must never be attached to a provider-controlled host.
            var fake = new FakeHttpTransport();
            var adapter = new FalAdapter();

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
            var adapter = new FalAdapter();

            Assert.Throws<System.Exception>(() =>
                adapter.SubmitAsync(Req(), "falkey123", fake, CancellationToken.None).GetAwaiter().GetResult());
        }
    }
}
