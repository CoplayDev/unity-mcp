using System;
using System.Threading;
using MCPForUnity.Editor.Services.AssetGen.Http;
using MCPForUnity.Editor.Services.AssetGen.Providers;
using NUnit.Framework;

namespace MCPForUnityTests.Editor.AssetGen
{
    public class OpenRouterAdapterTests
    {
        private static HttpResult Json(string body) => new HttpResult { Status = 200, IsSuccess = true, Text = body };

        [Test]
        public void Submit_Then_Poll_ReturnsInlineImageBytes()
        {
            byte[] expected = { 1, 2, 3, 4 };
            string b64 = Convert.ToBase64String(expected);
            var fake = new FakeHttpTransport
            {
                Handler = spec => Json("{\"choices\":[{\"message\":{\"images\":[{\"image_url\":{\"url\":\"data:image/png;base64," + b64 + "\"}}]}}]}")
            };
            var adapter = new OpenRouterAdapter();
            var req = new ImageGenRequest { Provider = "openrouter", Mode = "text", Prompt = "a cat" };

            string pid = adapter.SubmitAsync(req, "orkey123", fake, CancellationToken.None).GetAwaiter().GetResult();
            Assert.AreEqual("ready", pid);

            // Submit posts to chat/completions with a Bearer key.
            HttpRequestSpec sent = fake.RecordedRequests[0];
            Assert.AreEqual("POST", sent.Method);
            StringAssert.Contains("chat/completions", sent.Url);
            StringAssert.StartsWith("Bearer ", sent.Headers["Authorization"]);

            ProviderPollResult pr = adapter.PollAsync(pid, "orkey123", fake, CancellationToken.None).GetAwaiter().GetResult();
            Assert.AreEqual(ProviderPollState.Succeeded, pr.State);
            CollectionAssert.AreEqual(expected, pr.InlineData);
        }

        [Test]
        public void Submit_NoImage_PollFails()
        {
            var fake = new FakeHttpTransport
            {
                Handler = spec => Json("{\"choices\":[{\"message\":{\"content\":\"sorry, no image\"}}]}")
            };
            var adapter = new OpenRouterAdapter();
            var req = new ImageGenRequest { Provider = "openrouter", Mode = "text", Prompt = "a cat" };

            adapter.SubmitAsync(req, "orkey123", fake, CancellationToken.None).GetAwaiter().GetResult();
            ProviderPollResult pr = adapter.PollAsync("ready", "orkey123", fake, CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(ProviderPollState.Failed, pr.State);
            Assert.IsNotEmpty(pr.Error);
        }
    }
}
