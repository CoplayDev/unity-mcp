using System;
using System.IO;
using MCPForUnity.Editor.Security;
using MCPForUnity.Editor.Services.AssetGen;
using MCPForUnity.Editor.Services.AssetGen.Http;
using MCPForUnity.Editor.Tools.AssetGen;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace MCPForUnityTests.Editor.AssetGen
{
    public class GenerateAudioTests
    {
        private string _dir;
        private EncryptedFileKeyStore _store;

        [SetUp]
        public void SetUp()
        {
            AssetGenJobManager.ResetForTests();
            Environment.SetEnvironmentVariable("MCPFORUNITY_FAL_API_KEY", null);
            Environment.SetEnvironmentVariable("MCPFORUNITY_MINIMAX_API_KEY", null);
            _dir = Path.Combine(Path.GetTempPath(), "mcp_audiohandler_" + Guid.NewGuid().ToString("N"));
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
        }

        private static JObject Call(JObject p)
            => JObject.Parse(JsonConvert.SerializeObject(GenerateAudio.HandleCommand(p)));

        [Test]
        public void Generate_WithKey_ReturnsPendingJobId()
        {
            _store.Set("fal", "falkey");
            JObject gen = Call(new JObject { ["action"] = "generate", ["provider"] = "fal", ["prompt"] = "gentle rain" });
            Assert.AreEqual("pending", (string)gen["_mcp_status"]);
            Assert.IsFalse(string.IsNullOrEmpty((string)gen["data"]["job_id"]));
        }

        [Test]
        public void Generate_MissingKey_ReturnsError()
        {
            JObject resp = Call(new JObject { ["action"] = "generate", ["provider"] = "fal", ["prompt"] = "gentle rain" });
            Assert.AreEqual(false, (bool)resp["success"]);
            StringAssert.Contains("No API key", (string)resp["error"]);
        }

        [Test]
        public void Generate_EmptyPrompt_ReturnsError()
        {
            _store.Set("fal", "falkey");
            JObject resp = Call(new JObject { ["action"] = "generate", ["provider"] = "fal" });
            Assert.AreEqual(false, (bool)resp["success"]);
            StringAssert.Contains("prompt", ((string)resp["error"]).ToLowerInvariant());
        }

        [Test]
        public void Generate_UnknownProvider_ReturnsError()
        {
            JObject resp = Call(new JObject { ["action"] = "generate", ["provider"] = "elevenlabs", ["prompt"] = "gentle rain" });
            Assert.AreEqual(false, (bool)resp["success"]);
        }

        [Test]
        public void Generate_MiniMaxCover_WithReferenceUrl_ReturnsPendingJobId()
        {
            _store.Set("minimax", "test-provider-key");
            JObject gen = Call(new JObject
            {
                ["action"] = "generate",
                ["provider"] = "minimax",
                ["model"] = "music-cover",
                ["prompt"] = "Warm acoustic folk cover",
                ["audioUrl"] = "https://example.com/reference.mp3",
                ["audioFormat"] = "mp3",
                ["outputFormat"] = "url"
            });
            Assert.AreEqual("pending", (string)gen["_mcp_status"]);
            Assert.IsFalse(string.IsNullOrEmpty((string)gen["data"]["job_id"]));
        }

        [Test]
        public void Generate_MiniMaxCover_RequiresExactlyOneSource()
        {
            _store.Set("minimax", "test-provider-key");
            JObject none = Call(new JObject
            {
                ["action"] = "generate",
                ["provider"] = "minimax",
                ["prompt"] = "Warm acoustic folk cover"
            });
            StringAssert.Contains("exactly one", ((string)none["error"]).ToLowerInvariant());

            JObject two = Call(new JObject
            {
                ["action"] = "generate",
                ["provider"] = "minimax",
                ["prompt"] = "Warm acoustic folk cover",
                ["audioUrl"] = "https://example.com/reference.mp3",
                ["audioBase64"] = "SUQz"
            });
            StringAssert.Contains("exactly one", ((string)two["error"]).ToLowerInvariant());
        }

        [Test]
        public void Generate_MiniMaxCover_FeatureIdDoesNotReplaceReferenceAudio()
        {
            _store.Set("minimax", "test-provider-key");
            JObject missingSource = Call(new JObject
            {
                ["action"] = "generate",
                ["provider"] = "minimax",
                ["coverFeatureId"] = "feature-id"
            });
            StringAssert.Contains("exactly one", ((string)missingSource["error"]).ToLowerInvariant());

            JObject accepted = Call(new JObject
            {
                ["action"] = "generate",
                ["provider"] = "minimax",
                ["audioUrl"] = "https://example.com/reference.mp3",
                ["coverFeatureId"] = "feature-id"
            });
            Assert.AreEqual("pending", (string)accepted["_mcp_status"]);
        }

        [Test]
        public void ListProviders_FiltersAudioKind()
        {
            JObject resp = Call(new JObject { ["action"] = "list_providers" });
            Assert.AreEqual(true, (bool)resp["success"]);
            string s = resp.ToString();
            StringAssert.Contains("fal", s);
            StringAssert.Contains("audio", s);
            StringAssert.Contains("minimax", s);
            StringAssert.DoesNotContain("tripo", s);      // model providers excluded
            StringAssert.DoesNotContain("openrouter", s); // image providers excluded
        }
    }
}
