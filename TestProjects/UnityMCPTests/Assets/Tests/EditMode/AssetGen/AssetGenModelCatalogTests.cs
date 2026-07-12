using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.Services.AssetGen;
using MCPForUnity.Editor.Services.AssetGen.Providers;
using NUnit.Framework;

namespace MCPForUnityTests.Editor.AssetGen
{
    /// <summary>
    /// Covers the curated model catalog: the four audio models + metadata, per-kind/per-provider
    /// defaults, the adapter-constant drift guard, and exact-id lookup.
    /// </summary>
    public class AssetGenModelCatalogTests
    {
        [Test]
        public void Curated_HasAllFourAudioModels()
        {
            IReadOnlyList<ModelEntry> audio = AssetGenModelCatalog.ForProvider("fal", "audio");

            CollectionAssert.AreEqual(
                new[]
                {
                    "fal-ai/stable-audio-25/text-to-audio",
                    "cassetteai/sound-effects-generator",
                    "cassetteai/music-generator",
                    "fal-ai/lyria2",
                },
                audio.Select(e => e.Id).ToList());

            CollectionAssert.AreEqual(
                new[] { 190f, 30f, 180f, 30f },
                audio.Select(e => e.MaxDurationSeconds).ToList());

            Assert.IsNotNull(audio[0].CommercialNote, "Stable Audio should carry a license caveat");
            Assert.IsNull(audio[1].CommercialNote);
            Assert.IsNull(audio[2].CommercialNote);
            Assert.IsNull(audio[3].CommercialNote);
        }

        [Test]
        public void DefaultModelId_PerKindPerProvider()
        {
            Assert.AreEqual("fal-ai/flux-2", AssetGenModelCatalog.DefaultModelId("fal", "image"));
            Assert.AreEqual("fal-ai/stable-audio-25/text-to-audio", AssetGenModelCatalog.DefaultModelId("fal", "audio"));
            Assert.AreEqual("v3.1-20260211", AssetGenModelCatalog.DefaultModelId("tripo", "model"));
            Assert.AreEqual("meshy-6", AssetGenModelCatalog.DefaultModelId("meshy", "model"));
            Assert.IsNull(AssetGenModelCatalog.DefaultModelId("nope", "audio"), "unknown provider => null, not a throw");
        }

        [Test]
        public void DefaultModelId_MatchesAdapterConstants()
        {
            // Drift guard: the panel's shown default must equal what an omitted `model` param
            // resolves to in the adapter. The catalog references the adapter constant, so these are
            // pinned together — this test fails loudly if anyone reorders or reliterals a default.
            Assert.AreEqual(TripoAdapter.ModelVersion, AssetGenModelCatalog.DefaultModelId("tripo", "model"));
            Assert.AreEqual(MeshyAdapter.DefaultModel, AssetGenModelCatalog.DefaultModelId("meshy", "model"));
            Assert.AreEqual(FalAdapter.DefaultModel, AssetGenModelCatalog.DefaultModelId("fal", "image"));
            Assert.AreEqual(FalAudioAdapter.DefaultModel, AssetGenModelCatalog.DefaultModelId("fal", "audio"));
        }

        [Test]
        public void Find_ReturnsEntry_ByExactId()
        {
            ModelEntry e = AssetGenModelCatalog.Find("cassetteai/music-generator");
            Assert.IsNotNull(e);
            Assert.AreEqual("audio", e.Kind);
            Assert.IsNull(AssetGenModelCatalog.Find("does/not/exist"));
        }
    }
}
