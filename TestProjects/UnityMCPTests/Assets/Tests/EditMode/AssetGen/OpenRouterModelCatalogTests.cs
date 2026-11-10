using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services.AssetGen;
using MCPForUnity.Editor.Services.AssetGen.Http;
using MCPForUnity.Editor.Services.AssetGen.Providers;
using MCPForUnity.Editor.Tools.AssetGen;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace MCPForUnityTests.Editor.AssetGen
{
    public class OpenRouterModelCatalogTests
    {
        private const string Id = "test/image-v3";
        private FakeHttpTransport http;
        private string directory;
        private DateTime now;
        private static HttpResult Json(JObject body) => new HttpResult { Status = 200, Text = body.ToString() };
        private static JObject Parameters(int minimum = 0, int maximum = 1) => new JObject
        {
            ["input_references"] = new JObject { ["type"] = "range", ["min"] = minimum, ["max"] = maximum },
            ["output_format"] = new JObject { ["type"] = "enum", ["values"] = new JArray("jpeg", "png") },
        };
        private static JObject Model(string id = Id, JObject parameters = null) => new JObject
        {
            ["id"] = id, ["name"] = "New image model",
            ["architecture"] = new JObject { ["input_modalities"] = new JArray("text", "image"), ["output_modalities"] = new JArray("image") },
            ["supported_parameters"] = parameters ?? Parameters(),
            // Untrusted endpoint URL must never be followed.
            ["endpoints"] = "https://untrusted.invalid/endpoints",
        };
        private static JObject Endpoint(JObject parameters, string tag = "vendor") => new JObject
        {
            ["provider_tag"] = tag, ["supported_parameters"] = parameters,
        };

        [SetUp]
        public void SetUp()
        {
            AssetGenJobManager.ResetForTests();
            AssetGenModelCatalog.ResetForTests(true);
            directory = Path.Combine(Path.GetTempPath(), "or_catalog_" + Guid.NewGuid().ToString("N"));
            OpenRouterModelCatalog.CachePathOverrideForTests = Path.Combine(directory, "catalog.json");
            now = new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);
            OpenRouterModelCatalog.UtcNow = () => now;
            http = new FakeHttpTransport { Handler = r => Json(r.Url.EndsWith("/endpoints")
                ? new JObject { ["id"] = Id, ["endpoints"] = new JArray(Endpoint(Parameters())) }
                : new JObject { ["data"] = new JArray(Model()) }) };
            OpenRouterModelCatalog.TransportOverrideForTests = http;
        }

        [TearDown]
        public void TearDown()
        {
            AssetGenJobManager.ResetForTests();
            AssetGenModelCatalog.ResetForTests(true);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        [Test]
        public void Discovery_ReplacesBundledModels_AndSurvivesOfflineCacheReload()
        {
            Assert.IsTrue(OpenRouterModelCatalog.RefreshAsync().Result);
            Assert.AreEqual(Id, AssetGenModelCatalog.DefaultModelId("openrouter", "image"));
            var model = AssetGenModelCatalog.ForProvider("openrouter", "image").Single();
            Assert.IsNull(model.VerifiedAt);
            CollectionAssert.AreEqual(new[] { "text", "image" }, model.Modes);
            OpenRouterModelCatalog.ReloadCacheForTests();
            Assert.AreEqual("cache", OpenRouterModelCatalog.Source);
            int requests = http.RecordedRequests.Count;
            now = now.AddHours(23);
            Assert.IsTrue(OpenRouterModelCatalog.RefreshAsync().Result);
            Assert.AreEqual(requests, http.RecordedRequests.Count);
            http.Handler = _ => new HttpResult { Status = 500, Text = "do not persist this body" };
            now = now.AddHours(2);
            Assert.IsFalse(OpenRouterModelCatalog.RefreshAsync().Result);
            Assert.IsTrue(OpenRouterModelCatalog.IsStale);
            Assert.IsNotNull(AssetGenModelCatalog.ForProvider("openrouter", "image").Single());
            StringAssert.Contains("500", OpenRouterModelCatalog.LastError);
            StringAssert.DoesNotContain("do not persist", OpenRouterModelCatalog.LastError);
            Assert.IsTrue(http.RecordedRequests.All(r => r.Method == "GET" && !r.Headers.ContainsKey("Authorization")));
        }

        [Test]
        public void LargeCache_AboveTheOldTwoMegabyteLimit_StillLoadsAfterReload()
        {
            var models = Enumerable.Range(0, 4000).Select(i => (JToken)Model("test/image-" + i.ToString("D4"))).ToArray();
            http.Handler = _ => Json(new JObject { ["data"] = new JArray(models) });
            Assert.IsTrue(OpenRouterModelCatalog.RefreshAsync(true).Result);
            Assert.Greater(new FileInfo(OpenRouterModelCatalog.CachePathOverrideForTests).Length, 2 * 1024 * 1024);
            OpenRouterModelCatalog.ReloadCacheForTests();
            Assert.AreEqual("cache", OpenRouterModelCatalog.Source);
            Assert.AreEqual(4000, AssetGenModelCatalog.ForProvider("openrouter", "image").Count);
        }

        [Test]
        public void EmptySuccessfulCatalog_RemovesRetiredSavedModel_WithoutSubstitution()
        {
            string old = AssetGenPrefs.GetSelectedModel("image", "openrouter");
            AssetGenPrefs.SetSelectedModel("image", "openrouter", Id);
            try
            {
                http.Handler = _ => Json(new JObject { ["data"] = new JArray() });
                Assert.IsTrue(OpenRouterModelCatalog.RefreshAsync(true).Result);
                OpenRouterModelCatalog.ReloadCacheForTests();
                Assert.IsEmpty(AssetGenModelCatalog.ForProvider("openrouter", "image"));
                Assert.Throws<InvalidOperationException>(() => AssetGenModelCatalog.ResolveModel("image", "openrouter", null));
                Assert.AreEqual(Id, AssetGenPrefs.GetSelectedModel("image", "openrouter"));
            }
            finally { AssetGenPrefs.SetSelectedModel("image", "openrouter", old); }
        }

        [Test]
        public void EndpointVerification_UsesDefinitiveCapabilities_AndPinsCompatibleProvider()
        {
            http.Handler = r => Json(r.Url.EndsWith("/endpoints")
                ? new JObject { ["id"] = Id, ["endpoints"] = new JArray(Endpoint(Parameters(0, 0), "text-only"), Endpoint(Parameters(), "image-vendor")) }
                : new JObject { ["data"] = new JArray(Model()) });
            var entry = OpenRouterModelCatalog.VerifyForGeneration(Id, "image", CancellationToken.None).Result;
            Assert.AreEqual("image-vendor", entry.RouterProviderTag);
            Assert.AreEqual("png", entry.OutputFormat);
            Assert.IsTrue(http.RecordedRequests.All(r => new Uri(r.Url).Host == "openrouter.ai"));
            var adapter = new OpenRouterAdapter();
            var paid = new FakeHttpTransport { Handler = _ => Json(new JObject { ["data"] = new JArray(new JObject
                { ["b64_json"] = Convert.ToBase64String(new byte[] { 255, 216, 255, 1 }), ["media_type"] = "image/jpeg" }) }) };
            adapter.SubmitAsync(new ImageGenRequest { CatalogEntry = entry, Model = Id, Mode = "image", ImageUrl = "https://example.com/ref.png", Prompt = "paint" }, "test-key", paid, CancellationToken.None).GetAwaiter().GetResult();
            var request = paid.RecordedRequests.Single();
            Assert.AreEqual("https://openrouter.ai/api/v1/images", request.Url);
            var body = JObject.Parse(Encoding.UTF8.GetString(request.Body));
            Assert.AreEqual("image-vendor", (string)body["provider"]["only"][0]);
            Assert.IsFalse((bool)body["provider"]["allow_fallbacks"]);
            Assert.AreEqual("https://example.com/ref.png", (string)body["input_references"][0]["image_url"]["url"]);
            Assert.IsNull(body["messages"]);
            Assert.AreEqual("jpg", adapter.PollAsync("ready", "test-key", paid, CancellationToken.None).Result.ResultExt);
            Assert.Throws<InvalidOperationException>(() => adapter.SubmitAsync(new ImageGenRequest { CatalogEntry = entry, Model = Id, Width = 512, Height = 512 }, "test-key", paid, CancellationToken.None).GetAwaiter().GetResult());
        }

        [Test]
        public void Discovery_ExcludesVectorOnly_AndDistinguishesImageRequiredModels()
        {
            var vector = Parameters(); vector["output_format"]["values"] = new JArray("svg");
            http.Handler = _ => Json(new JObject { ["data"] = new JArray(Model("test/vector", vector), Model(Id, Parameters(1, 1))) });
            Assert.IsTrue(OpenRouterModelCatalog.RefreshAsync(true).Result);
            var entry = AssetGenModelCatalog.ForProvider("openrouter", "image").Single();
            CollectionAssert.AreEqual(new[] { "image" }, entry.Modes);
            Assert.IsNull(AssetGenModelCatalog.DefaultModelId("openrouter", "image"));
        }

        [Test]
        public void UnavailableModel_FailsBeforePaidSubmission()
        {
            string key = Environment.GetEnvironmentVariable("MCPFORUNITY_OPENROUTER_API_KEY");
            Environment.SetEnvironmentVariable("MCPFORUNITY_OPENROUTER_API_KEY", "test-key");
            try
            {
                http.Handler = _ => Json(new JObject { ["data"] = new JArray() });
                var paid = new FakeHttpTransport();
                AssetGenJobManager.TransportOverrideForTests = paid;
                var job = AssetGenJobManager.StartImageGeneration(new ImageGenRequest { Provider = "openrouter", Model = Id, Prompt = "cat" });
                var tick = typeof(AssetGenJobManager).GetMethod("Tick", BindingFlags.NonPublic | BindingFlags.Static);
                for (int i = 0; i < 6; i++) tick.Invoke(null, null);
                Assert.AreEqual(AssetGenJobState.Failed, job.State);
                Assert.IsEmpty(paid.RecordedRequests);
            }
            finally { Environment.SetEnvironmentVariable("MCPFORUNITY_OPENROUTER_API_KEY", key); }
        }

        [UnityTest, Explicit("Reads public OpenRouter image discovery; no paid generation.")]
        public IEnumerator LivePublicCatalog_AndExactEndpoint()
        {
            if (Environment.GetEnvironmentVariable("MCPFORUNITY_RUN_LIVE_CATALOG") != "1") Assert.Ignore("Opt-in live discovery test.");
            OpenRouterModelCatalog.TransportOverrideForTests = new UnityWebRequestTransport();
            OpenRouterModelCatalog.UtcNow = () => DateTime.UtcNow;
            var refresh = OpenRouterModelCatalog.RefreshAsync(true);
            while (!refresh.IsCompleted) yield return null;
            Assert.IsTrue(refresh.Result, OpenRouterModelCatalog.LastError);
            var entries = AssetGenModelCatalog.ForProvider("openrouter", "image");
            Assert.IsNotEmpty(entries);
            TestContext.WriteLine("OpenRouter: " + entries.Count + " discovered image models");
            var verify = OpenRouterModelCatalog.VerifyForGeneration(entries.First(e => e.Modes.Contains("text")).Id, "text", CancellationToken.None);
            while (!verify.IsCompleted) yield return null;
            Assert.IsFalse(verify.IsFaulted, verify.Exception?.ToString());
            TestContext.WriteLine("Verified " + verify.Result.Id + " via " + verify.Result.RouterProviderTag);
        }
    }
}
