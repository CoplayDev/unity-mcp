using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services.AssetGen;
using MCPForUnity.Editor.Services.AssetGen.Http;
using MCPForUnity.Editor.Services.AssetGen.Providers;
using MCPForUnity.Editor.Tools.AssetGen;
using MCPForUnity.Editor.Windows.Components.AssetGen;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace MCPForUnityTests.Editor.AssetGen
{
    public class FalModelCatalogTests
    {
        private const string Music = "test/music-v2";
        private string directory;
        private string oldSelection;
        private DateTime now;
        private FakeHttpTransport http;

        [SetUp]
        public void SetUp()
        {
            AssetGenJobManager.ResetForTests();
            AssetGenModelCatalog.ResetForTests(true);
            oldSelection = AssetGenPrefs.GetSelectedModel("audio", "fal");
            AssetGenPrefs.SetSelectedModel("audio", "fal", "");
            directory = Path.Combine(Path.GetTempPath(), "fal_catalog_" + Guid.NewGuid().ToString("N"));
            now = new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);
            FalModelCatalog.UtcNow = () => now;
            FalModelCatalog.CachePathOverrideForTests = Path.Combine(directory, "catalog.json");
            http = new FakeHttpTransport();
            FalModelCatalog.TransportOverrideForTests = http;
            Serve(Endpoint(Music));
        }

        [TearDown]
        public void TearDown()
        {
            AssetGenJobManager.ResetForTests();
            AssetGenPrefs.SetSelectedModel("audio", "fal", oldSelection);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        internal static JObject Endpoint(string id, string kind = "audio", string prompt = "prompt", bool edit = false)
        {
            var input = new JObject { ["type"] = "object", ["properties"] = new JObject { [prompt] = new JObject { ["type"] = "string" } }, ["required"] = new JArray(prompt) };
            var output = new JObject { ["type"] = "object", ["properties"] = kind == "audio"
                ? new JObject { ["audio"] = new JObject { ["$ref"] = "#/components/schemas/File" } }
                : kind == "model" ? new JObject { ["model_glb"] = new JObject { ["$ref"] = "#/components/schemas/File" } }
                : new JObject { ["images"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["$ref"] = "#/components/schemas/File" } } } };
            if (edit)
            {
                input["properties"]["image_url"] = new JObject { ["type"] = "string" };
                ((JArray)input["required"]).Add("image_url");
            }
            return new JObject
            {
                ["endpoint_id"] = id,
                ["metadata"] = new JObject { ["status"] = "active", ["category"] = kind == "model" ? edit ? "image-to-3d" : "text-to-3d" : edit ? "image-to-image" : kind == "audio" ? "text-to-audio" : "text-to-image", ["display_name"] = "Test model", ["tags"] = new JArray(kind == "audio" ? "music" : "image"), ["updated_at"] = "2026-10-01T12:00:00Z" },
                ["openapi"] = new JObject
                {
                    ["paths"] = new JObject
                    {
                        ["/" + id] = new JObject { ["post"] = new JObject { ["requestBody"] = new JObject { ["content"] = new JObject { ["application/json"] = new JObject { ["schema"] = new JObject { ["$ref"] = "#/components/schemas/Input" } } } } } },
                        ["/" + id + "/requests/{request_id}"] = new JObject { ["get"] = new JObject { ["responses"] = new JObject { ["200"] = new JObject { ["content"] = new JObject { ["application/json"] = new JObject { ["schema"] = new JObject { ["$ref"] = "#/components/schemas/Output" } } } } } } },
                    },
                    ["components"] = new JObject { ["schemas"] = new JObject { ["Input"] = input, ["Output"] = output, ["File"] = new JObject { ["type"] = "object", ["properties"] = new JObject { ["url"] = new JObject { ["type"] = "string" } } } } },
                },
            };
        }

        private static JObject Input(JObject model) => (JObject)model["openapi"]["components"]["schemas"]["Input"];
        private static HttpResult Json(JObject json) => new HttpResult { Status = 200, Text = json.ToString() };
        private static HttpResult Models(params JObject[] models) => Json(new JObject { ["models"] = new JArray(models), ["has_more"] = false });
        private void Serve(params JObject[] models)
        {
            http.Handler = request =>
            {
                bool details = request.Url.Contains("endpoint_id=");
                string category = new[] { "text-to-image", "image-to-image", "text-to-3d", "image-to-3d", "text-to-audio" }
                    .FirstOrDefault(value => request.Url.Contains("category=" + value));
                return Models(models.Where(model => details
                    ? request.Url.Contains("endpoint_id=" + Uri.EscapeDataString((string)model["endpoint_id"]) + "&")
                    : (string)model["metadata"]["category"] == category).ToArray());
            };
        }
        private bool Refresh(string kind = "audio", bool force = true) => FalModelCatalog.RefreshAsync(kind, force).GetAwaiter().GetResult();

        [Test]
        public void CompleteCatalog_IsNotLimitedToEagerSchemaShortlist_AndCanBeSearched()
        {
            var endpoints = Enumerable.Range(0, 40).Select(i => Endpoint("test/music-" + i.ToString("D2"))).ToArray();
            Serve(endpoints);
            Assert.IsTrue(Refresh());
            var entries = AssetGenModelCatalog.ForProvider("fal", "audio");
            Assert.AreEqual(40, entries.Count);
            Assert.Less(entries.Count(e => e.VerifiedAt != null), entries.Count);
            var discovered = entries.First(e => e.VerifiedAt == null);
            Assert.IsTrue(discovered.FromRefresh);
            var page = JObject.FromObject(GenerateAudio.HandleCommand(new JObject { ["action"] = "list_models", ["search"] = "music-", ["offset"] = 30, ["limit"] = 5 }));
            Assert.AreEqual(40, (int)page["data"]["total"]);
            Assert.AreEqual(5, page["data"]["models"].Count());
            Assert.IsTrue((bool)page["data"]["has_more"]);
            Assert.AreEqual("discovered", (string)page["data"]["models"][0]["status"]);
            var verified = FalModelCatalog.VerifyForGeneration(discovered.Id, "audio", "text", CancellationToken.None).GetAwaiter().GetResult();
            Assert.IsNotNull(verified.VerifiedAt);
            Assert.IsNotNull(AssetGenModelCatalog.Find(discovered.Id).VerifiedAt);
        }

        [Test]
        public void NativeImageEndpoint_DoesNotRequireAGuessedEditAlias()
        {
            Serve(Endpoint("test/restyle", "image", edit: true));
            Assert.IsTrue(Refresh("image"));
            var entry = FalModelCatalog.VerifyForGeneration("test/restyle", "image", "image", CancellationToken.None).GetAwaiter().GetResult();
            Assert.AreEqual("test/restyle", entry.EditModelId);
            CollectionAssert.AreEqual(new[] { "image" }, entry.Modes);
            Assert.Throws<InvalidOperationException>(() => FalModelCatalog.VerifyForGeneration(entry.Id, "image", "text", CancellationToken.None).GetAwaiter().GetResult());
        }

        [TestCase("audio"), TestCase("image"), TestCase("model")]
        public void DirectJobWithNoModel_ResolvesCurrentCatalogBeforePreflight(string kind)
        {
            string oldKey = Environment.GetEnvironmentVariable("MCPFORUNITY_FAL_API_KEY");
            string selection = AssetGenPrefs.GetSelectedModel(kind, "fal");
            Environment.SetEnvironmentVariable("MCPFORUNITY_FAL_API_KEY", "test-key");
            AssetGenPrefs.SetSelectedModel(kind, "fal", "");
            try
            {
                string id = "test/replacement-" + kind;
                Serve(Endpoint(id, kind));
                Assert.IsTrue(Refresh(kind));
                var paid = new FakeHttpTransport { Handler = _ => Json(new JObject { ["request_id"] = "r1", ["response_url"] = "https://queue.fal.run/test/app/requests/r1" }) };
                AssetGenJobManager.TransportOverrideForTests = paid;
                AssetGenJob job = kind == "audio" ? AssetGenJobManager.StartAudioGeneration(new AudioGenRequest { Provider = "fal", Prompt = "rain" })
                    : kind == "image" ? AssetGenJobManager.StartImageGeneration(new ImageGenRequest { Provider = "fal", Mode = "text", Prompt = "rain" })
                    : AssetGenJobManager.StartModelGeneration(new ModelGenRequest { Provider = "fal", Mode = "text", Prompt = "chair" });
                AssetGenJobManager.TryAdvanceForTests(job.JobId);
                AssetGenJobManager.TryAdvanceForTests(job.JobId);
                Assert.AreNotEqual(AssetGenJobState.Failed, job.State, job.Error);
                Assert.AreEqual("https://queue.fal.run/" + id, paid.RecordedRequests.First().Url);
            }
            finally
            {
                Environment.SetEnvironmentVariable("MCPFORUNITY_FAL_API_KEY", oldKey);
                AssetGenPrefs.SetSelectedModel(kind, "fal", selection);
            }
        }

        [Test]
        public void Batch404_DoesNotHideActiveModels_WhenAnotherModelIsRemoved()
        {
            const string removed = "test/music-missing";
            http.Handler = r => !r.Url.Contains("endpoint_id=") ? Models(Endpoint(Music), Endpoint(removed))
                : r.Url.Contains(Uri.EscapeDataString(removed)) ? new HttpResult { Status = 404, Text = "{\"error\":{\"type\":\"not_found\"}}" }
                : Models(Endpoint(Music));
            Assert.IsTrue(Refresh());
            Assert.AreEqual(Music, AssetGenModelCatalog.ForProvider("fal", "audio").Single().Id);
        }

        [Test]
        public void Fal3D_DiscoveryAndPreflight_UseNativeTextAndImageSchemas()
        {
            Serve(Endpoint("test/mesh-text", "model"), Endpoint("test/mesh-image", "model", edit: true));
            Assert.IsTrue(Refresh("model"));
            CollectionAssert.AreEquivalent(new[] { "test/mesh-text", "test/mesh-image" }, AssetGenModelCatalog.ForProvider("fal", "model").Select(e => e.Id));
            var entry = FalModelCatalog.VerifyForGeneration("test/mesh-image", "model", "image", CancellationToken.None).GetAwaiter().GetResult();
            Assert.AreEqual("model_glb", entry.ModelOutputField);
            var adapter = new FalModelAdapter();
            var submit = new FakeHttpTransport { Handler = r => r.Method == "POST" ? Json(new JObject { ["request_id"] = "r1" })
                : r.Url.EndsWith("/status") ? Json(new JObject { ["status"] = "COMPLETED" })
                : Json(new JObject { ["model_glb"] = new JObject { ["url"] = "https://example.com/model.glb" } }) };
            string pid = adapter.SubmitAsync(new ModelGenRequest { Mode = "image", CatalogEntry = entry, ImageUrl = "https://example.com/ref.jpg" }, "test-key", submit, CancellationToken.None).GetAwaiter().GetResult();
            Assert.AreEqual("https://queue.fal.run/test/mesh-image/requests/r1", pid);
            var body = JObject.Parse(Encoding.UTF8.GetString(submit.RecordedRequests[0].Body));
            Assert.AreEqual("https://example.com/ref.jpg", (string)body["image_url"]);
            Assert.IsNull(body["image_urls"]);
            var result = adapter.PollAsync(pid, "test-key", submit, CancellationToken.None).GetAwaiter().GetResult();
            Assert.AreEqual("glb", result.ResultExt);
            Assert.AreEqual("https://example.com/model.glb", result.DownloadUrl);
            Assert.Throws<InvalidOperationException>(() => adapter.SubmitAsync(new ModelGenRequest { CatalogEntry = entry, Format = "fbx" }, "test-key", submit, CancellationToken.None));
        }

        [Test]
        public void Refresh_DiscoversModels_WithoutKeys_AndSharesToolCatalog()
        {
            Assert.IsTrue(Refresh());
            Assert.AreEqual(Music, AssetGenModelCatalog.DefaultModelId("fal", "audio"));
            Assert.IsTrue(AssetGenModelCatalog.Find(Music).FromRefresh);
            Assert.IsTrue(http.RecordedRequests.All(request => request.Method == "GET" && !request.Headers.ContainsKey("Authorization")));
            var response = JObject.FromObject(GenerateAudio.HandleCommand(new JObject { ["action"] = "list_models" }));
            Assert.AreEqual(Music, (string)response["data"]["models"][0]["id"]);
            Assert.AreEqual("live", (string)response["data"]["catalogs"][0]["source"]);
            Assert.AreEqual(false, (bool)response["data"]["catalogs"][0]["stale"]);
            Assert.IsFalse(File.ReadAllText(FalModelCatalog.CachePathOverrideForTests).Contains("Authorization"));
        }

        [Test]
        public void Refresh_RemovesDeprecatedBundledModels_WithoutResurrectingDefaults()
        {
            var removed = Endpoint(FalAudioAdapter.DefaultModel);
            removed["metadata"]["status"] = "deprecated";
            Serve(removed);
            Assert.IsTrue(Refresh());
            Assert.IsEmpty(AssetGenModelCatalog.ForProvider("fal", "audio"));
            Assert.IsNull(AssetGenModelCatalog.Find(FalAudioAdapter.DefaultModel));
            Assert.Throws<InvalidOperationException>(() => AssetGenModelCatalog.ResolveModel("audio", "fal", null));
            FalModelCatalog.ReloadCacheForTests();
            Assert.IsEmpty(AssetGenModelCatalog.ForProvider("fal", "audio"), "Empty successful snapshots must survive reload.");
        }

        [Test]
        public void Refresh_FailureOnLaterPage_RetainsPreviousSnapshot()
        {
            Assert.IsTrue(Refresh());
            string checkedAt = FalModelCatalog.VerifiedAt("audio");
            http.Handler = request => request.Url.Contains("cursor=")
                ? new HttpResult { Status = 429 }
                : Json(new JObject { ["models"] = new JArray(), ["has_more"] = true, ["next_cursor"] = "page2" });
            now = now.AddDays(2);
            Assert.IsFalse(Refresh());
            Assert.AreEqual(Music, AssetGenModelCatalog.DefaultModelId("fal", "audio"));
            Assert.AreEqual(checkedAt, FalModelCatalog.VerifiedAt("audio"));
            StringAssert.Contains("429", FalModelCatalog.LastError("audio"));
            Assert.IsTrue(FalModelCatalog.IsStale("audio"));
        }

        [Test]
        public void Refresh_TraversesAllMetadataPages_BeforeReplacingCatalog()
        {
            var next = Endpoint("test/music-v3");
            http.Handler = request => request.Url.Contains("endpoint_id=") ? Models(Endpoint(Music), next)
                : request.Url.Contains("cursor=") ? Models(next)
                : Json(new JObject { ["models"] = new JArray(Endpoint(Music)), ["has_more"] = true, ["next_cursor"] = "next page+" });
            Assert.IsTrue(Refresh());
            CollectionAssert.AreEquivalent(new[] { Music, "test/music-v3" }, AssetGenModelCatalog.ForProvider("fal", "audio").Select(entry => entry.Id));
            Assert.IsTrue(http.RecordedRequests.Any(request => request.Url.Contains("cursor=next%20page%2B")));
        }

        [TestCase("{\"models\":[] ,\"has_more\":true}")]
        [TestCase("{\"wrong\":[]}")]
        [TestCase("not json")]
        public void Refresh_IncompleteOrMalformedResponse_DoesNotEraseCatalog(string body)
        {
            Assert.IsTrue(Refresh());
            http.Handler = _ => new HttpResult { Status = 200, Text = body };
            Assert.IsFalse(Refresh());
            Assert.AreEqual(Music, AssetGenModelCatalog.DefaultModelId("fal", "audio"));
        }

        [Test]
        public void Refresh_SchemaExpansionError_RetainsSnapshot()
        {
            Assert.IsTrue(Refresh());
            var failed = Endpoint(Music);
            failed["openapi"] = new JObject { ["error"] = new JObject { ["code"] = "expansion_failed" } };
            Serve(failed);
            Assert.IsFalse(Refresh());
            Assert.IsNotNull(AssetGenModelCatalog.Find(Music));
        }

        [Test]
        public void Refresh_FilterSpeechAndUnknownRequiredInputs()
        {
            var speech = Endpoint("test/tts-music", prompt: "text");
            var lyrics = Endpoint("test/music-lyrics");
            Input(lyrics)["properties"]["lyrics"] = new JObject { ["type"] = "string" };
            ((JArray)Input(lyrics)["required"]).Add("lyrics");
            Serve(Endpoint(Music), speech, lyrics);
            Assert.IsTrue(Refresh());
            CollectionAssert.AreEqual(new[] { Music }, AssetGenModelCatalog.ForProvider("fal", "audio").Select(entry => entry.Id));
        }

        [Test]
        public void FreshCache_SkipsNetwork_UntilTtlOrForcedRefresh()
        {
            Assert.IsTrue(Refresh());
            int count = http.RecordedRequests.Count;
            FalModelCatalog.ReloadCacheForTests();
            Assert.AreEqual("cache", FalModelCatalog.Source("audio"));
            now = now.AddHours(23);
            Assert.IsTrue(Refresh(force: false));
            Assert.AreEqual(count, http.RecordedRequests.Count);
            Assert.IsTrue(Refresh(force: true));
            now = now.AddHours(25);
            Assert.IsTrue(Refresh(force: false));
            Assert.Greater(http.RecordedRequests.Count, count);
        }

        [Test]
        public void FailedAutomaticRefresh_BacksOff_ButManualRefreshRetries()
        {
            http.Handler = _ => new HttpResult { Status = 500 };
            Assert.IsFalse(Refresh(force: false));
            int requests = http.RecordedRequests.Count;
            Assert.IsFalse(Refresh(force: false));
            Assert.AreEqual(requests, http.RecordedRequests.Count);
            Assert.IsFalse(Refresh(force: true));
            Assert.Greater(http.RecordedRequests.Count, requests);
        }

        [Test]
        public void RateLimitedRequest_RetriesWithBoundedDelay_WithoutLosingCatalog()
        {
            int count = 0;
            var waits = new System.Collections.Generic.List<TimeSpan>();
            FalModelCatalog.DelayOverrideForTests = (wait, _) => { waits.Add(wait); return Task.CompletedTask; };
            http.Handler = _ => count++ == 0 ? new HttpResult { Status = 429, RetryAfterSeconds = 999 }
                : Models(Endpoint(Music));
            Assert.IsTrue(Refresh());
            Assert.AreEqual(Music, AssetGenModelCatalog.DefaultModelId("fal", "audio"));
            Assert.Contains(TimeSpan.FromSeconds(10), waits);
            Assert.Contains(TimeSpan.FromSeconds(7), waits, "Public calls must use the conservative pacing interval.");
            Assert.IsTrue(waits.All(wait => wait <= TimeSpan.FromSeconds(10)));
        }

        [Test]
        public void CorruptCache_FallsBackToBundledEntries()
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(FalModelCatalog.CachePathOverrideForTests, "bad json");
            FalModelCatalog.ReloadCacheForTests();
            Assert.AreEqual("bundled", FalModelCatalog.Source("audio"));
            Assert.AreEqual(FalAudioAdapter.DefaultModel, AssetGenModelCatalog.DefaultModelId("fal", "audio"));
        }

        [Test]
        public void MissingSavedSelection_IsPreserved_AndGenerationErrors()
        {
            AssetGenPrefs.SetSelectedModel("audio", "fal", "test/music-retired");
            Assert.IsTrue(Refresh());
            Assert.Throws<InvalidOperationException>(() => AssetGenModelCatalog.ResolveModel("audio", "fal", null));
            var root = new VisualElement();
            root.Add(new VisualElement { name = "assetgen-providers-container" });
            var section = new McpAssetGenSection(root);
            Assert.AreEqual("test/music-retired", AssetGenPrefs.GetSelectedModel("audio", "fal"));
            Assert.IsTrue(root.Query<DropdownField>().ToList().Any(dropdown => dropdown.value?.Contains("Saved model unavailable") == true));
            Assert.IsFalse(root.Query<Label>(className: "validation-description").ToList()
                .Any(caveat => string.IsNullOrEmpty(caveat.text) && caveat.style.display.value != DisplayStyle.None), "An empty license caveat box must be hidden.");
        }

        [Test]
        public void LongCatalog_ModelMenuIsCapped_KeepsSelection_AndSearchReachesTheRest()
        {
            Serve(Enumerable.Range(0, 40).Select(i => Endpoint("test/music-" + i.ToString("D2"))).ToArray());
            Assert.IsTrue(Refresh());
            AssetGenPrefs.SetSelectedModel("audio", "fal", "test/music-39");
            var root = new VisualElement();
            root.Add(new VisualElement { name = "assetgen-providers-container" });
            var section = new McpAssetGenSection(root);
            DropdownField AudioMenu() => root.Query<DropdownField>().ToList().Single(d => d.choices.Any(c => c.Contains("test/music-")));

            var menu = AudioMenu();
            Assert.AreEqual(McpAssetGenSection.MenuLimit + 1, menu.choices.Count, "The saved selection beyond the cap stays selectable.");
            StringAssert.Contains("test/music-39", menu.value);
            Assert.IsTrue(menu.choices.All(c => !McpAssetGenSection.MenuItemText(c).Contains("/")), "GenericMenu turns every '/' into a submenu.");
            Assert.IsTrue(root.Query<Label>().ToList().Any(l => l.text.StartsWith($"The menu shows {McpAssetGenSection.MenuLimit} of 40 models")));

            var searches = (System.Collections.Generic.Dictionary<string, string>)typeof(McpAssetGenSection)
                .GetField("searches", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(section);
            searches["audio/fal"] = "music-30";
            typeof(McpAssetGenSection).GetMethod("RebuildModelControls", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(section, new object[] { null, null });
            menu = AudioMenu();
            Assert.AreEqual(2, menu.choices.Count);
            Assert.IsTrue(menu.choices.Any(c => c.Contains("test/music-30")), "Search must reach models beyond the menu cap.");
        }

        [Test]
        public void ProviderRows_OwnOneFalKeyField_AndSkipEmptyModelSelectors()
        {
            var root = new VisualElement();
            root.Add(new VisualElement { name = "assetgen-providers-container" });
            new McpAssetGenSection(root);
            // Keyed rows: tripo, meshy, sketchfab, fal (2D) and openrouter. fal 3D and audio reuse the 2D fal key.
            Assert.AreEqual(5, root.Query<TextField>().ToList().Count(field => field.isPasswordField));
            Assert.AreEqual(5, root.Query<Toggle>().ToList().Count(toggle => toggle.label == "Enabled"));
            Assert.IsTrue(root.Query<Label>().ToList().Any(label => label.text == "fal (3D)"));
            var sketchfab = root.Query<Label>().ToList().Single(label => label.text == "Sketchfab").parent.parent;
            Assert.IsEmpty(sketchfab.Query<DropdownField>().ToList());
            Assert.IsFalse(sketchfab.Query<Label>().ToList().Any(label => label.text.StartsWith("No models found")));
        }

        [Test]
        public void FailedCompatibilityCheck_StaysVisible_AfterCatalogRebuild()
        {
            Assert.IsTrue(Refresh());
            AssetGenPrefs.SetSelectedModel("audio", "fal", Music);
            var root = new VisualElement();
            root.Add(new VisualElement { name = "assetgen-providers-container" });
            var section = new McpAssetGenSection(root);
            Serve();
            var verify = typeof(McpAssetGenSection).GetMethod("VerifySelection", BindingFlags.NonPublic | BindingFlags.Instance);
            ((Task)verify.Invoke(section, new object[] { AssetGenModelCatalog.Find(Music), new Label() })).GetAwaiter().GetResult();
            typeof(McpAssetGenSection).GetMethod("RebuildModelControls", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(section, new object[] { null, null });
            var warning = root.Query<Label>().ToList().Single(label => label.text.StartsWith("Compatibility check failed"));
            StringAssert.Contains("unavailable", warning.text);
            Assert.IsTrue(warning.ClassListContains("warning-banner-text"));
        }

        [Test]
        public void NullableFractionalDuration_UsesLiveTextField_AndBounds()
        {
            var model = Endpoint("test/sfx-v2", prompt: "text");
            model["metadata"]["tags"] = new JArray("sfx");
            Input(model)["properties"]["duration_seconds"] = new JObject { ["anyOf"] = new JArray(
                new JObject { ["type"] = "number", ["minimum"] = 0.5, ["maximum"] = 22 }, new JObject { ["type"] = "null" }) };
            var entry = FalModelSchema.Parse(model, "audio", now.ToString("O"));
            Assert.IsNotNull(entry);
            var submit = new FakeHttpTransport { Handler = _ => new HttpResult { Status = 200, Text = "{\"response_url\":\"https://queue.fal.run/test/sfx-v2/requests/r1\"}" } };
            new FalAudioAdapter().SubmitAsync(new AudioGenRequest { Model = entry.Id, CatalogEntry = entry, Prompt = "rain", Duration = 0.75f }, "test-key", submit, CancellationToken.None).GetAwaiter().GetResult();
            var body = JObject.Parse(Encoding.UTF8.GetString(submit.RecordedRequests[0].Body));
            Assert.AreEqual("rain", (string)body["text"]);
            Assert.IsNull(body["prompt"]);
            Assert.AreEqual(0.75f, (float)body["duration_seconds"]);
        }

        [Test]
        public void MillisecondDuration_ConvertsFromSeconds_AndClampsToSchemaMinimum()
        {
            var model = Endpoint(Music);
            Input(model)["properties"]["music_length_ms"] = new JObject { ["type"] = "integer", ["minimum"] = 3000, ["maximum"] = 600000, ["default"] = 10000 };
            var entry = FalModelSchema.Parse(model, "audio", now.ToString("O"));
            var submit = new FakeHttpTransport { Handler = _ => new HttpResult { Status = 200, Text = "{\"response_url\":\"https://queue.fal.run/test/music-v2/requests/r1\"}" } };
            new FalAudioAdapter().SubmitAsync(new AudioGenRequest { Model = entry.Id, CatalogEntry = entry, Prompt = "rain", Duration = 1f }, "test-key", submit, CancellationToken.None).GetAwaiter().GetResult();
            var body = JObject.Parse(Encoding.UTF8.GetString(submit.RecordedRequests[0].Body));
            Assert.AreEqual(3000, (int)body["music_length_ms"]);
            Assert.AreEqual(JTokenType.Integer, body["music_length_ms"].Type);
        }

        [Test]
        public void SchemaGate_RejectsUnsupportedOutputAndUnsafeEndpointId()
        {
            var model = Endpoint(Music);
            model["openapi"]["components"]["schemas"]["Output"]["properties"] = new JObject { ["result"] = new JObject { ["type"] = "string" } };
            Assert.IsNull(FalModelSchema.Parse(model, "audio", now.ToString("O")));
            Assert.IsNull(FalModelSchema.Parse(Endpoint("test/../music"), "audio", now.ToString("O")));
            Assert.IsNull(FalModelSchema.Parse(Endpoint("https://attacker.invalid/music"), "audio", now.ToString("O")));
        }

        [Test]
        public void RecursiveNullableAndUnionSchemas_DoNotOverflowTheEditorStack()
        {
            var model = Endpoint(Music);
            var schemas = model["openapi"]["components"]["schemas"];
            schemas["File"] = new JObject { ["anyOf"] = new JArray(new JObject { ["$ref"] = "#/components/schemas/File" }, new JObject { ["type"] = "null" }) };
            Assert.IsNull(FalModelSchema.Parse(model, "audio", now.ToString("O")));
            model = Endpoint(Music);
            schemas = model["openapi"]["components"]["schemas"];
            schemas["Loop"] = new JObject { ["anyOf"] = new JArray(new JObject { ["$ref"] = "#/components/schemas/Loop" }, new JObject { ["type"] = "string" }) };
            Input(model)["properties"]["duration"] = new JObject { ["$ref"] = "#/components/schemas/Loop" };
            Assert.IsNull(FalModelSchema.Parse(model, "audio", now.ToString("O")));
        }

        [Test]
        public void ImageSchema_ChoosesImportableOutputFormat_AndRejectsVectorOnlyModels()
        {
            var model = Endpoint("test/image", "image");
            Input(model)["properties"]["output_format"] = new JObject { ["type"] = "string", ["enum"] = new JArray("webp", "png"), ["default"] = "webp" };
            Assert.AreEqual("png", FalModelSchema.Parse(model, "image", now.ToString("O")).OutputFormat);
            Input(model)["properties"]["output_format"]["enum"] = new JArray("svg");
            Assert.IsNull(FalModelSchema.Parse(model, "image", now.ToString("O")));
            Assert.IsNull(FalModelSchema.Parse(Endpoint("test/text-to-vector", "image"), "image", now.ToString("O")));
        }

        [Test]
        public void ExplicitUnlistedModel_RemainsAllowed_ForExactEndpointPreflight()
        {
            Assert.IsTrue(Refresh());
            Assert.AreEqual("test/music-custom", AssetGenModelCatalog.ResolveModel("audio", "fal", "test/music-custom"));
            var custom = Endpoint("test/music-custom");
            Serve(custom);
            Assert.IsNotNull(FalModelCatalog.VerifyForGeneration("test/music-custom", "audio", "text", CancellationToken.None).GetAwaiter().GetResult());
        }

        [Test]
        public void ImageRefresh_AdvertisesOnlyVerifiedEditing_AndShapesRequest()
        {
            const string imageId = "test/image-v2";
            Serve(Endpoint(imageId, "image"), Endpoint(imageId + "/edit", "image", edit: true));
            Assert.IsTrue(Refresh("image"));
            var entry = AssetGenModelCatalog.Find(imageId);
            Assert.AreEqual(imageId + "/edit", entry.EditModelId);
            var submit = new FakeHttpTransport { Handler = _ => new HttpResult { Status = 200, Text = "{\"response_url\":\"https://queue.fal.run/test/image-v2/requests/r1\"}" } };
            new FalAdapter().SubmitAsync(new ImageGenRequest { Model = imageId, CatalogEntry = entry, Mode = "image", Prompt = "rain", ImageUrl = "https://example.com/image.png" }, "test-key", submit, CancellationToken.None).GetAwaiter().GetResult();
            var body = JObject.Parse(Encoding.UTF8.GetString(submit.RecordedRequests[0].Body));
            Assert.AreEqual("https://example.com/image.png", (string)body["image_url"]);
            Assert.IsNull(body["image_urls"]);
            Assert.IsNull(body["num_images"]);
        }

        [Test]
        public void ImageWithoutEditSchema_RejectsImageMode_InsteadOfGuessingEndpoint()
        {
            Serve(Endpoint("test/image-v2", "image"));
            Assert.IsTrue(Refresh("image"));
            var entry = AssetGenModelCatalog.Find("test/image-v2");
            Assert.IsNull(entry.EditModelId);
            Assert.Throws<Exception>(() => new FalAdapter().SubmitAsync(new ImageGenRequest
                { Model = entry.Id, CatalogEntry = entry, Mode = "image", ImageUrl = "https://example.com/in.png" }, "test-key", new FakeHttpTransport(), CancellationToken.None).GetAwaiter().GetResult());
        }

        [Test]
        public void MissingFindEndpoints_404_IsANegativeResult_ButList404PreservesCache()
        {
            http.Handler = request => request.Url.Contains("endpoint_id=")
                ? new HttpResult { Status = 404, Text = "{\"error\":{\"type\":\"not_found\",\"message\":\"Endpoint(s) not found\"}}" }
                : Models(Endpoint(Music));
            Assert.IsTrue(Refresh());
            Assert.IsEmpty(AssetGenModelCatalog.ForProvider("fal", "audio"));
            http.Handler = _ => new HttpResult { Status = 404, Text = "{\"error\":{\"type\":\"not_found\"}}" };
            Assert.IsFalse(Refresh());
            Assert.AreEqual("live", FalModelCatalog.Source("audio"));
        }

        [TestCase("tripo"), TestCase("meshy")]
        public void Bundled3DModels_AdvertiseBothTextAndImageModes(string provider)
        {
            var response = JObject.FromObject(GenerateModel.HandleCommand(new JObject { ["action"] = "list_models", ["provider"] = provider }));
            CollectionAssert.AreEqual(new[] { "text", "image" }, response["data"]["models"][0]["capabilities"].Values<string>());
            Assert.AreEqual("unverified", (string)response["data"]["models"][0]["status"]);
        }

        [Test]
        public void GenerationPreflight_UnavailableModel_FailsBeforePaidSubmit()
        {
            string previousKey = Environment.GetEnvironmentVariable("MCPFORUNITY_FAL_API_KEY");
            Environment.SetEnvironmentVariable("MCPFORUNITY_FAL_API_KEY", "test-key");
            try
            {
                Serve();
                var submit = new FakeHttpTransport();
                AssetGenJobManager.TransportOverrideForTests = submit;
                var job = AssetGenJobManager.StartAudioGeneration(new AudioGenRequest { Provider = "fal", Model = Music, Prompt = "rain" });
                var tick = typeof(AssetGenJobManager).GetMethod("Tick", BindingFlags.NonPublic | BindingFlags.Static);
                for (int i = 0; i < 5; i++) tick.Invoke(null, null);
                Assert.AreEqual(AssetGenJobState.Failed, job.State);
                StringAssert.Contains("unavailable", job.Error);
                Assert.IsEmpty(submit.RecordedRequests, "A failed availability check must not submit a paid generation.");
                Assert.IsTrue(http.RecordedRequests.All(request => request.Headers["Authorization"] == "Key test-key" && new Uri(request.Url).Host == "api.fal.ai"));
            }
            finally { Environment.SetEnvironmentVariable("MCPFORUNITY_FAL_API_KEY", previousKey); }
        }

        private sealed class DelayedTransport : IHttpTransport
        {
            public readonly TaskCompletionSource<HttpResult> First = new();
            private int count;
            public Task<HttpResult> SendAsync(HttpRequestSpec spec, CancellationToken ct)
                => count++ == 0 ? First.Task : Task.FromResult(Models(Endpoint(Music)));
        }

        private sealed class LiveTransport : IHttpTransport
        {
            public async Task<HttpResult> SendAsync(HttpRequestSpec spec, CancellationToken ct)
            {
                var result = await new UnityWebRequestTransport().SendAsync(spec, ct);
                TestContext.WriteLine(spec.Url + " => " + result.Status + (result.Ok ? "" : " " + ProviderHttp.Truncate(result.Text)));
                return result;
            }
        }

        [UnityTest]
        public IEnumerator ConcurrentRefreshes_ShareOneOperation()
        {
            var delayed = new DelayedTransport();
            FalModelCatalog.TransportOverrideForTests = delayed;
            var first = FalModelCatalog.RefreshAsync("audio", true);
            var second = FalModelCatalog.RefreshAsync("audio", true);
            Assert.AreSame(first, second);
            Assert.IsTrue(FalModelCatalog.IsRefreshing("audio"));
            delayed.First.SetResult(Models(Endpoint(Music)));
            while (!first.IsCompleted) yield return null;
            Assert.IsTrue(first.Result);
            Assert.IsFalse(FalModelCatalog.IsRefreshing("audio"));
        }

        [UnityTest]
        public IEnumerator GenerationPreflight_DoesNotQueueBehindABackgroundRefresh()
        {
            var delayed = new DelayedTransport();
            FalModelCatalog.TransportOverrideForTests = delayed;
            var refresh = FalModelCatalog.RefreshAsync("audio", true);
            Assert.IsTrue(FalModelCatalog.IsRefreshing("audio"), "The refresh holds the request gate on its first page.");
            var verify = FalModelCatalog.VerifyForGeneration(Music, "audio", "text", CancellationToken.None, "test-key");
            Assert.IsTrue(verify.IsCompleted, "A paid-generation preflight must not wait for the background refresh.");
            Assert.IsNotNull(verify.Result.VerifiedAt);
            Assert.IsTrue(FalModelCatalog.IsRefreshing("audio"));
            delayed.First.SetResult(Models(Endpoint(Music)));
            while (!refresh.IsCompleted) yield return null;
            Assert.IsTrue(refresh.Result, FalModelCatalog.LastError("audio"));
        }

        [Test]
        public void LargeCache_AboveTheOldTwoMegabyteLimit_StillLoadsAfterReload()
        {
            Serve(Enumerable.Range(0, 4000).Select(i => Endpoint("test/music-" + i.ToString("D4"))).ToArray());
            Assert.IsTrue(Refresh());
            Assert.Greater(new FileInfo(FalModelCatalog.CachePathOverrideForTests).Length, 2 * 1024 * 1024);
            FalModelCatalog.ReloadCacheForTests();
            Assert.AreEqual("cache", FalModelCatalog.Source("audio"));
            Assert.AreEqual(4000, AssetGenModelCatalog.ForProvider("fal", "audio").Count);
        }

        [UnityTest]
        public IEnumerator BackgroundRefresh_PreservesUnsavedApiKeyInput()
        {
            var delayed = new DelayedTransport();
            FalModelCatalog.TransportOverrideForTests = delayed;
            var root = new VisualElement();
            root.Add(new VisualElement { name = "assetgen-providers-container" });
            var section = new McpAssetGenSection(root);
            var field = root.Query<TextField>().ToList().First(candidate => candidate.isPasswordField);
            field.value = "unsaved-test-input";
            delayed.First.SetResult(Models());
            while (FalModelCatalog.IsRefreshing("image") || FalModelCatalog.IsRefreshing("audio") || FalModelCatalog.IsRefreshing("model") || OpenRouterModelCatalog.IsRefreshing) yield return null;
            yield return null;
            Assert.AreEqual("unsaved-test-input", field.value);
            Assert.IsTrue(root.Contains(field), "The automatic refresh must not replace key-entry controls.");
        }

        [UnityTest, Explicit("Queries the public fal catalog and OpenAPI schemas; no paid generation.")]
        public IEnumerator LivePublicCatalog_RefreshAndExactEndpointVerification()
        {
            if (Environment.GetEnvironmentVariable("MCPFORUNITY_RUN_LIVE_CATALOG") != "1")
                Assert.Ignore("Set MCPFORUNITY_RUN_LIVE_CATALOG=1 to opt into public API verification.");
            FalModelCatalog.TransportOverrideForTests = new LiveTransport();
            FalModelCatalog.DelayOverrideForTests = null;
            FalModelCatalog.UtcNow = () => DateTime.UtcNow;
            var audio = FalModelCatalog.RefreshAsync("audio", true);
            var image = FalModelCatalog.RefreshAsync("image", true);
            var model = FalModelCatalog.RefreshAsync("model", true);
            while (!audio.IsCompleted || !image.IsCompleted || !model.IsCompleted) yield return null;
            Assert.IsTrue(audio.Result, FalModelCatalog.LastError("audio"));
            Assert.IsTrue(image.Result, FalModelCatalog.LastError("image"));
            Assert.IsTrue(model.Result, FalModelCatalog.LastError("model"));
            foreach (string kind in new[] { "audio", "image", "model" })
            {
                var entries = AssetGenModelCatalog.ForProvider("fal", kind);
                Assert.IsNotEmpty(entries);
                Assert.IsTrue(entries.All(entry => entry.FromRefresh));
                Assert.IsTrue(entries.Any(entry => !string.IsNullOrEmpty(entry.VerifiedAt)));
                TestContext.WriteLine(kind + ": " + entries.Count + " discovered; " + entries.Count(e => e.VerifiedAt != null) + " verified");
                var verify = FalModelCatalog.VerifyForGeneration(entries[0].Id, kind, "text", CancellationToken.None);
                while (!verify.IsCompleted) yield return null;
                Assert.IsFalse(verify.IsFaulted, verify.Exception?.ToString());
                Assert.IsNotNull(verify.Result);
            }
        }
    }
}
