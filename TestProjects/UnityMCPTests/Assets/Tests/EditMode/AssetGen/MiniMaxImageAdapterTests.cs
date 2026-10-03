using System;
using System.IO;
using System.Threading;
using MCPForUnity.Editor.Services.AssetGen.Http;
using MCPForUnity.Editor.Services.AssetGen.Providers;
using NUnit.Framework;
using UnityEngine;

namespace MCPForUnityTests.Editor.AssetGen
{
    public class MiniMaxImageAdapterTests
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
        public void Submit_TextMode_PostsToImageGeneration_WithBearerKey()
        {
            var fake = new FakeHttpTransport
            {
                Handler = spec => Json("{\"data\":{\"image_urls\":[\"https://cdn.minimax.io/img/x.png\"]},\"metadata\":{\"success_count\":\"1\",\"failed_count\":\"0\"},\"base_resp\":{\"status_code\":0}}")
            };
            var adapter = new MiniMaxImageAdapter();
            var req = new ImageGenRequest { Provider = "minimax", Mode = "text", Prompt = "a cat" };

            string pid = adapter.SubmitAsync(req, "mmkey123", fake, CancellationToken.None).GetAwaiter().GetResult();
            Assert.AreEqual("ready", pid);

            HttpRequestSpec sent = fake.RecordedRequests[0];
            Assert.AreEqual("POST", sent.Method);
            StringAssert.Contains("image_generation", sent.Url);
            StringAssert.Contains("api.minimax.io", sent.Url);
            StringAssert.StartsWith("Bearer ", sent.Headers["Authorization"]);

            string body = System.Text.Encoding.UTF8.GetString(sent.Body);
            StringAssert.Contains("\"model\":\"image-01\"", body);
            StringAssert.Contains("\"prompt\":\"a cat\"", body);
            // No subject_reference for plain text->image.
            StringAssert.DoesNotContain("subject_reference", body);
        }

        [Test]
        public void Submit_ExplicitModel_OverridesDefault()
        {
            var fake = new FakeHttpTransport
            {
                Handler = spec => Json("{\"data\":{\"image_urls\":[\"https://cdn.minimax.io/img/y.png\"]},\"base_resp\":{\"status_code\":0}}")
            };
            var adapter = new MiniMaxImageAdapter();
            var req = new ImageGenRequest { Provider = "minimax", Mode = "text", Prompt = "a dog", Model = "image-01-live" };

            adapter.SubmitAsync(req, "mmkey", fake, CancellationToken.None).GetAwaiter().GetResult();

            string body = System.Text.Encoding.UTF8.GetString(fake.RecordedRequests[0].Body);
            StringAssert.Contains("\"model\":\"image-01-live\"", body);
        }

        [Test]
        public void Submit_TextMode_ForwardsWidthAndHeight()
        {
            var fake = new FakeHttpTransport
            {
                Handler = spec => Json("{\"data\":{\"image_urls\":[\"https://cdn.minimax.io/img/z.png\"]},\"base_resp\":{\"status_code\":0}}")
            };
            var adapter = new MiniMaxImageAdapter();
            var req = new ImageGenRequest { Provider = "minimax", Mode = "text", Prompt = "a cat", Width = 768, Height = 1024 };

            adapter.SubmitAsync(req, "mmkey", fake, CancellationToken.None).GetAwaiter().GetResult();

            string body = System.Text.Encoding.UTF8.GetString(fake.RecordedRequests[0].Body);
            StringAssert.Contains("\"width\":768", body);
            StringAssert.Contains("\"height\":1024", body);
        }

        [Test]
        public void Submit_ImageMode_IncludesSubjectReferenceWithUrl()
        {
            var fake = new FakeHttpTransport
            {
                Handler = spec => Json("{\"data\":{\"image_urls\":[\"https://cdn.minimax.io/img/r.png\"]},\"base_resp\":{\"status_code\":0}}")
            };
            var adapter = new MiniMaxImageAdapter();
            var req = new ImageGenRequest { Provider = "minimax", Mode = "image", Prompt = "make it watercolor", ImageUrl = "https://ex.com/in.png" };

            adapter.SubmitAsync(req, "mmkey", fake, CancellationToken.None).GetAwaiter().GetResult();

            string body = System.Text.Encoding.UTF8.GetString(fake.RecordedRequests[0].Body);
            StringAssert.Contains("subject_reference", body);
            StringAssert.Contains("\"type\":\"character\"", body);
            StringAssert.Contains("\"image_file\":\"https://ex.com/in.png\"", body);
        }

        [Test]
        public void Submit_ImageMode_LocalPath_SendsDataUri()
        {
            string rel = WriteProjectFile("Assets/Generated/__assetgen_minimax_adapter/ref.png", new byte[] { 137, 80, 78, 71 });
            try
            {
                var fake = new FakeHttpTransport
                {
                    Handler = spec => Json("{\"data\":{\"image_urls\":[\"https://cdn.minimax.io/img/r.png\"]},\"base_resp\":{\"status_code\":0}}")
                };
                var adapter = new MiniMaxImageAdapter();
                var req = new ImageGenRequest { Provider = "minimax", Mode = "image", Prompt = "watercolor", ImagePath = rel };

                adapter.SubmitAsync(req, "mmkey", fake, CancellationToken.None).GetAwaiter().GetResult();

                string body = System.Text.Encoding.UTF8.GetString(fake.RecordedRequests[0].Body);
                StringAssert.Contains("subject_reference", body);
                StringAssert.Contains("data:image/png;base64,", body);
            }
            finally { try { Directory.Delete(Path.Combine(ProjectRoot(), "Assets/Generated/__assetgen_minimax_adapter"), true); } catch { } }
        }

        [Test]
        public void Submit_Then_Poll_ReturnsDownloadUrl()
        {
            var fake = new FakeHttpTransport
            {
                Handler = spec => Json("{\"data\":{\"image_urls\":[\"https://cdn.minimax.io/img/dl.png\"]},\"base_resp\":{\"status_code\":0}}")
            };
            var adapter = new MiniMaxImageAdapter();
            var req = new ImageGenRequest { Provider = "minimax", Mode = "text", Prompt = "a cat" };

            string pid = adapter.SubmitAsync(req, "mmkey", fake, CancellationToken.None).GetAwaiter().GetResult();
            ProviderPollResult pr = adapter.PollAsync(pid, "mmkey", fake, CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(ProviderPollState.Succeeded, pr.State);
            Assert.AreEqual("https://cdn.minimax.io/img/dl.png", pr.DownloadUrl);
        }

        [Test]
        public void Submit_Base64Response_PollReturnsInlineBytes()
        {
            byte[] expected = { 5, 6, 7, 8 };
            string b64 = Convert.ToBase64String(expected);
            var fake = new FakeHttpTransport
            {
                Handler = spec => Json("{\"data\":{\"image_base64\":[\"" + b64 + "\"]},\"base_resp\":{\"status_code\":0}}")
            };
            var adapter = new MiniMaxImageAdapter();
            var req = new ImageGenRequest { Provider = "minimax", Mode = "text", Prompt = "a cat" };

            string pid = adapter.SubmitAsync(req, "mmkey", fake, CancellationToken.None).GetAwaiter().GetResult();
            ProviderPollResult pr = adapter.PollAsync(pid, "mmkey", fake, CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(ProviderPollState.Succeeded, pr.State);
            CollectionAssert.AreEqual(expected, pr.InlineData);
        }

        [Test]
        public void Submit_NoImage_PollFails()
        {
            var fake = new FakeHttpTransport
            {
                Handler = spec => Json("{\"data\":{},\"base_resp\":{\"status_code\":0}}")
            };
            var adapter = new MiniMaxImageAdapter();
            var req = new ImageGenRequest { Provider = "minimax", Mode = "text", Prompt = "a cat" };

            adapter.SubmitAsync(req, "mmkey", fake, CancellationToken.None).GetAwaiter().GetResult();
            ProviderPollResult pr = adapter.PollAsync("ready", "mmkey", fake, CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(ProviderPollState.Failed, pr.State);
            Assert.IsNotEmpty(pr.Error);
        }

        [Test]
        public void Submit_NonZeroBaseRespStatus_Throws()
        {
            var fake = new FakeHttpTransport
            {
                Handler = spec => Json("{\"base_resp\":{\"status_code\":1001,\"status_msg\":\"invalid prompt\"}}")
            };
            var adapter = new MiniMaxImageAdapter();
            var req = new ImageGenRequest { Provider = "minimax", Mode = "text", Prompt = "a cat" };

            Exception ex = Assert.Throws<Exception>(() =>
                adapter.SubmitAsync(req, "mmkey", fake, CancellationToken.None).GetAwaiter().GetResult());
            StringAssert.Contains("MiniMax", ex.Message);
            StringAssert.Contains("1001", ex.Message);
            // The key must not leak into the thrown message.
            StringAssert.DoesNotContain("mmkey", ex.Message);
        }
    }
}
