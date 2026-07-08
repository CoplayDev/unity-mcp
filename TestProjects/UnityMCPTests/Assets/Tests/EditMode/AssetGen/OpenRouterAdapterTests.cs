using System;
using System.IO;
using System.Threading;
using MCPForUnity.Editor.Services.AssetGen.Http;
using MCPForUnity.Editor.Services.AssetGen.Providers;
using NUnit.Framework;
using UnityEngine;

namespace MCPForUnityTests.Editor.AssetGen
{
    public class OpenRouterAdapterTests
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
        public void Submit_ImageMode_IncludesReferenceImageInBody()
        {
            var fake = new FakeHttpTransport
            {
                Handler = spec => Json("{\"choices\":[{\"message\":{\"images\":[{\"image_url\":{\"url\":\"data:image/png;base64,AAAA\"}}]}}]}")
            };
            var adapter = new OpenRouterAdapter();
            var req = new ImageGenRequest { Provider = "openrouter", Mode = "image", Prompt = "make it watercolor", ImageUrl = "https://ex.com/in.png" };

            adapter.SubmitAsync(req, "orkey", fake, CancellationToken.None).GetAwaiter().GetResult();

            string sent = System.Text.Encoding.UTF8.GetString(fake.RecordedRequests[0].Body);
            StringAssert.Contains("image_url", sent);
            StringAssert.Contains("https://ex.com/in.png", sent);
        }

        [Test]
        public void Submit_ImageMode_LocalPath_SendsDataUri()
        {
            string rel = WriteProjectFile("Assets/Generated/__assetgen_openrouter_adapter/ref.png", new byte[] { 137, 80, 78, 71 });
            try
            {
                var fake = new FakeHttpTransport
                {
                    Handler = spec => Json("{\"choices\":[{\"message\":{\"images\":[{\"image_url\":{\"url\":\"data:image/png;base64,AAAA\"}}]}}]}")
                };
                var adapter = new OpenRouterAdapter();
                var req = new ImageGenRequest { Provider = "openrouter", Mode = "image", Prompt = "watercolor", ImagePath = rel };

                adapter.SubmitAsync(req, "orkey", fake, CancellationToken.None).GetAwaiter().GetResult();

                string sent = System.Text.Encoding.UTF8.GetString(fake.RecordedRequests[0].Body);
                StringAssert.Contains("image_url", sent);
                StringAssert.Contains("data:image/png;base64,", sent);
            }
            finally { try { Directory.Delete(Path.Combine(ProjectRoot(), "Assets/Generated/__assetgen_openrouter_adapter"), true); } catch { } }
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
