using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using MCPForUnity.Editor.Security;
using MCPForUnity.Editor.Services.AssetGen;
using MCPForUnity.Editor.Services.AssetGen.Http;
using MCPForUnity.Editor.Tools.AssetGen;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace MCPForUnityTests.Editor.AssetGen
{
    /// <summary>
    /// Drives the import_model handler with a fake transport and a temp key store (no network).
    /// Covers search (key present/absent), import (returns a pending job_id), and the
    /// marketplace-only list_providers shape.
    /// </summary>
    public class ImportModelHandlerTests
    {
        private string _dir;
        private EncryptedFileKeyStore _store;

        [SetUp]
        public void SetUp()
        {
            AssetGenJobManager.ResetForTests();
            Environment.SetEnvironmentVariable("MCPFORUNITY_SKETCHFAB_API_KEY", null);
            _dir = Path.Combine(Path.GetTempPath(), "mcp_import_" + Guid.NewGuid().ToString("N"));
            _store = new EncryptedFileKeyStore(_dir);
            SecureKeyStore.OverrideForTests(_store);
            ImportModel.TransportOverrideForTests = null;
        }

        [TearDown]
        public void TearDown()
        {
            AssetGenJobManager.ResetForTests();
            SecureKeyStore.ResetForTests();
            ImportModel.TransportOverrideForTests = null;
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { /* ignore */ }
        }

        private static HttpResult Json(string body)
            => new HttpResult { Status = 200, IsSuccess = true, Text = body, Body = Encoding.UTF8.GetBytes(body) };

        // HandleCommand is async; the fake transport completes synchronously so awaiting here
        // never blocks on the editor loop (unlike the live UnityWebRequest path).
        private static JObject Call(JObject p)
            => JObject.Parse(JsonConvert.SerializeObject(ImportModel.HandleCommand(p).GetAwaiter().GetResult()));

        [Test]
        public void HandleCommand_IsAsync()
        {
            // Regression: search/preview hit UnityWebRequest, whose completion is pumped by the
            // editor loop. A synchronous handler that blocks on .GetResult() deadlocks the main
            // thread and freezes the editor. The handler must be async so the request can finish
            // on a later tick. See the import_model editor-freeze investigation.
            var ret = typeof(ImportModel)
                .GetMethod(nameof(ImportModel.HandleCommand))
                .ReturnType;
            Assert.IsTrue(typeof(Task).IsAssignableFrom(ret),
                "ImportModel.HandleCommand must return Task (async) to avoid blocking the Unity main thread.");
        }

        [Test]
        public void Search_WithKey_ReturnsResults()
        {
            _store.Set("sketchfab", "sfkey123");
            ImportModel.TransportOverrideForTests = new FakeHttpTransport
            {
                Handler = _ => Json("{\"results\":[{\"uid\":\"u1\",\"name\":\"Castle\"}]}")
            };

            JObject resp = Call(new JObject { ["action"] = "search", ["query"] = "castle" });

            Assert.AreEqual(true, (bool)resp["success"]);
            StringAssert.Contains("u1", resp.ToString());
        }

        [Test]
        public void Search_NoKey_ReturnsError()
        {
            JObject resp = Call(new JObject { ["action"] = "search", ["query"] = "castle" });
            Assert.AreEqual(false, (bool)resp["success"]);
            StringAssert.Contains("No API key", (string)resp["error"]);
        }

        [Test]
        public void Import_WithKey_ReturnsPendingJobId()
        {
            _store.Set("sketchfab", "sfkey123");
            // The job manager resolves the download URL through its own transport seam.
            AssetGenJobManager.TransportOverrideForTests = new FakeHttpTransport
            {
                Handler = _ => Json("{\"gltf\":{\"url\":\"https://dl.sketchfab.com/u1/file.zip\"}}")
            };

            JObject resp = Call(new JObject { ["action"] = "import", ["uid"] = "u1", ["name"] = "castle" });

            Assert.AreEqual("pending", (string)resp["_mcp_status"]);
            string jobId = (string)resp["data"]["job_id"];
            Assert.IsFalse(string.IsNullOrEmpty(jobId));
        }

        [Test]
        public void ListProviders_IncludesSketchfabOnly()
        {
            JObject resp = Call(new JObject { ["action"] = "list_providers" });
            Assert.AreEqual(true, (bool)resp["success"]);
            string s = resp.ToString();
            StringAssert.Contains("sketchfab", s);
            StringAssert.Contains("marketplace", s);
            StringAssert.DoesNotContain("tripo", s);
            StringAssert.DoesNotContain("\"fal\"", s);
        }
    }
}
