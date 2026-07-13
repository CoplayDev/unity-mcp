using System;
using System.IO;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Security;
using MCPForUnity.Editor.Services.AssetGen;
using MCPForUnity.Editor.Services.AssetGen.Http;
using MCPForUnity.Editor.Services.AssetGen.Providers;
using MCPForUnity.Editor.Tools.AssetGen;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MCPForUnityTests.Editor.AssetGen
{
    public class GenerateImageTests
    {
        private const string TestFolder = "Assets/Generated/__assetgen_imgtest";
        private string _dir;
        private EncryptedFileKeyStore _store;

        [SetUp]
        public void SetUp()
        {
            AssetGenJobManager.ResetForTests();
            Environment.SetEnvironmentVariable("MCPFORUNITY_FAL_API_KEY", null);
            Environment.SetEnvironmentVariable("MCPFORUNITY_OPENROUTER_API_KEY", null);
            _dir = Path.Combine(Path.GetTempPath(), "mcp_imghandler_" + Guid.NewGuid().ToString("N"));
            _store = new EncryptedFileKeyStore(_dir);
            SecureKeyStore.OverrideForTests(_store);
            AssetGenJobManager.TransportOverrideForTests = new FakeHttpTransport();
        }

        [TearDown]
        public void TearDown()
        {
            AssetGenJobManager.ResetForTests();
            SecureKeyStore.ResetForTests();
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
            try
            {
                string dp = Application.dataPath.Replace('\\', '/');
                string abs = Path.Combine(dp.Substring(0, dp.Length - "Assets".Length), TestFolder);
                if (Directory.Exists(abs)) Directory.Delete(abs, true);
                if (File.Exists(abs + ".meta")) File.Delete(abs + ".meta");
            }
            catch { }
        }

        private static JObject Call(JObject p)
            => JObject.Parse(JsonConvert.SerializeObject(GenerateImage.HandleCommand(p)));

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

        [Test]
        public void Generate_WithKey_ReturnsPendingJobId()
        {
            _store.Set("fal", "falkey");
            JObject gen = Call(new JObject { ["action"] = "generate", ["provider"] = "fal", ["mode"] = "text", ["prompt"] = "a cat" });
            Assert.AreEqual("pending", (string)gen["_mcp_status"]);
            Assert.IsFalse(string.IsNullOrEmpty((string)gen["data"]["job_id"]));
        }

        [Test]
        public void Generate_NoKey_ReturnsError()
        {
            JObject resp = Call(new JObject { ["action"] = "generate", ["provider"] = "fal", ["mode"] = "text", ["prompt"] = "a cat" });
            Assert.AreEqual(false, (bool)resp["success"]);
            StringAssert.Contains("No API key", (string)resp["error"]);
        }

        [Test]
        public void Generate_ImageMode_MissingFile_ReturnsError()
        {
            _store.Set("fal", "falkey");
            JObject resp = Call(new JObject { ["action"] = "generate", ["provider"] = "fal", ["mode"] = "image", ["imagePath"] = "Assets/does_not_exist_zzz.png" });
            Assert.AreEqual(false, (bool)resp["success"]);
            StringAssert.Contains("not found", ((string)resp["error"]).ToLowerInvariant());
        }

        [Test]
        public void Generate_ImageMode_PathOutsideAssets_ReturnsError()
        {
            _store.Set("fal", "falkey");
            string tmp = Path.Combine(Path.GetTempPath(), "mcp_imgin_" + Guid.NewGuid().ToString("N") + ".png");
            File.WriteAllBytes(tmp, new byte[] { 137, 80, 78, 71 });
            try
            {
                JObject resp = Call(new JObject { ["action"] = "generate", ["provider"] = "fal", ["mode"] = "image", ["imagePath"] = tmp, ["prompt"] = "edit it" });
                Assert.AreEqual(false, (bool)resp["success"]);
                StringAssert.Contains("Assets", (string)resp["error"]);
            }
            finally { try { File.Delete(tmp); } catch { } }
        }

        [Test]
        public void Generate_ImageMode_ProjectLocalPath_Accepted_ReturnsPending()
        {
            _store.Set("fal", "falkey");
            string rel = WriteProjectFile(TestFolder + "/ref.png", new byte[] { 137, 80, 78, 71 });

            JObject gen = Call(new JObject { ["action"] = "generate", ["provider"] = "fal", ["mode"] = "image", ["imagePath"] = rel, ["prompt"] = "edit it" });

            Assert.AreEqual("pending", (string)gen["_mcp_status"]);
        }

        [Test]
        public void Generate_ImageMode_UnsupportedExtension_ReturnsError()
        {
            _store.Set("fal", "falkey");
            string rel = WriteProjectFile(TestFolder + "/bad.tga", new byte[] { 0, 0, 2 });

            JObject resp = Call(new JObject { ["action"] = "generate", ["provider"] = "fal", ["mode"] = "image", ["imagePath"] = rel, ["prompt"] = "x" });

            Assert.AreEqual(false, (bool)resp["success"]);
            StringAssert.Contains("Unsupported", (string)resp["error"]);
        }

        [Test]
        public void ListProviders_ImageOnly()
        {
            JObject resp = Call(new JObject { ["action"] = "list_providers" });
            Assert.AreEqual(true, (bool)resp["success"]);
            string s = resp.ToString();
            StringAssert.Contains("fal", s);
            StringAssert.Contains("openrouter", s);
            StringAssert.DoesNotContain("tripo", s); // model providers excluded
        }

        [Test]
        public void Generate_UnknownProvider_ReturnsError()
        {
            _store.Set("bogus", "k");
            JObject resp = Call(new JObject { ["action"] = "generate", ["provider"] = "bogus", ["mode"] = "text", ["prompt"] = "a cat" });
            Assert.AreEqual(false, (bool)resp["success"]);
        }

        private static HttpResult FalStatus(HttpRequestSpec spec) =>
            spec.Url.EndsWith("/status")
                ? new HttpResult { Status = 200, IsSuccess = true, Text = "{\"status\":\"IN_PROGRESS\"}" }
                : new HttpResult { Status = 200, IsSuccess = true, Text = "{\"response_url\":\"https://queue.fal.run/x/requests/r1\"}" };

        [Test]
        public void Generate_NoModelParam_UsesSelectedImageModelPref()
        {
            _store.Set("fal", "falkey");
            AssetGenPrefs.SetSelectedModel("image", "fal", "fal-ai/flux-2-pro");
            var fake = new FakeHttpTransport { Handler = FalStatus };
            AssetGenJobManager.TransportOverrideForTests = fake;
            AssetGenJobManager.PollIntervalSeconds = 0;
            try
            {
                JObject gen = Call(new JObject { ["action"] = "generate", ["provider"] = "fal", ["mode"] = "text", ["prompt"] = "a cat" });
                string jobId = (string)gen["data"]["job_id"];
                for (int i = 0; i < 6; i++) AssetGenJobManager.TryAdvanceForTests(jobId);

                HttpRequestSpec post = fake.RecordedRequests.Find(r => r.Method == "POST");
                Assert.IsNotNull(post, "expected a submit POST");
                StringAssert.Contains("flux-2-pro", post.Url); // pref drove the model when none was passed
            }
            finally { AssetGenPrefs.SetSelectedModel("image", "fal", ""); }
        }

        [Test]
        public void Generate_ExplicitModel_OverridesPref()
        {
            _store.Set("fal", "falkey");
            AssetGenPrefs.SetSelectedModel("image", "fal", "fal-ai/flux-2-pro");
            var fake = new FakeHttpTransport { Handler = FalStatus };
            AssetGenJobManager.TransportOverrideForTests = fake;
            AssetGenJobManager.PollIntervalSeconds = 0;
            try
            {
                JObject gen = Call(new JObject { ["action"] = "generate", ["provider"] = "fal", ["mode"] = "text", ["prompt"] = "a cat", ["model"] = "fal-ai/flux-2/flash" });
                string jobId = (string)gen["data"]["job_id"];
                for (int i = 0; i < 6; i++) AssetGenJobManager.TryAdvanceForTests(jobId);

                HttpRequestSpec post = fake.RecordedRequests.Find(r => r.Method == "POST");
                Assert.IsNotNull(post);
                StringAssert.Contains("flux-2/flash", post.Url);
                StringAssert.DoesNotContain("flux-2-pro", post.Url);
            }
            finally { AssetGenPrefs.SetSelectedModel("image", "fal", ""); }
        }

        [Test]
        public void OpenRouterInline_EndToEnd_ReachesDone()
        {
            _store.Set("openrouter", "orkey");
            byte[] png = { 137, 80, 78, 71, 13, 10, 26, 10 }; // PNG magic; bytes only need to be written, not validated
            string b64 = Convert.ToBase64String(png);
            AssetGenJobManager.TransportOverrideForTests = new FakeHttpTransport
            {
                Handler = spec => new HttpResult
                {
                    Status = 200,
                    IsSuccess = true,
                    Text = "{\"choices\":[{\"message\":{\"images\":[{\"image_url\":{\"url\":\"data:image/png;base64," + b64 + "\"}}]}}]}"
                }
            };
            AssetGenJobManager.PollIntervalSeconds = 0;
            AssetGenJobManager.ImportOverrideForTests = (job, path) => { job.AssetPath = path; return job; };

            var req = new ImageGenRequest { Provider = "openrouter", Mode = "text", Prompt = "a cat", Name = "imgtest", OutputFolder = TestFolder };
            AssetGenJob job = AssetGenJobManager.StartImageGeneration(req);

            int guard = 0;
            while (!AssetGenJobManager.TryAdvanceForTests(job.JobId) && guard++ < 50) { }
            Assert.Less(guard, 50);
            Assert.AreEqual(AssetGenJobState.Done, job.State);
            StringAssert.EndsWith("imgtest.png", job.AssetPath);
        }
    }
}
