using System;
using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services.AssetGen.Providers;

namespace MCPForUnity.Editor.Services.AssetGen
{
    /// <summary>
    /// One selectable model in the Asset Generation panel, plus the metadata the GUI shows
    /// (use-case / price / max duration) and the license caveat to surface. <see cref="Id"/> is the
    /// exact string passed as the tool's <c>model</c> param — it reaches the adapter request as the
    /// fal endpoint, the Tripo <c>model_version</c>, the Meshy <c>ai_model</c>, or the OpenRouter
    /// model slug.
    /// </summary>
    public sealed class ModelEntry
    {
        public string Id;
        public string Label;
        public string Provider;
        public string Kind;              // image | model | audio
        public string UseCase;
        public string PriceLabel;
        public float MaxDurationSeconds; // 0 => not time-bounded (image / 3D)
        // Audio duration knob. DurationField is the request key ("seconds_total" / "duration");
        // null => the model has no duration control (prompt-only). DefaultDurationSeconds is used
        // when the caller passes 0 to a model whose endpoint requires a duration. MinDurationSeconds
        // is the clamp floor.
        public string DurationField;
        public float DefaultDurationSeconds;
        public float MinDurationSeconds;
        public bool Loopable;
        public string CommercialNote;    // non-null => show a license caveat under the dropdown
        public bool FromRefresh;
        public string PromptField = "prompt";
        public bool DurationIsInteger = true;
        public float DurationScale = 1f;  // request units per second (e.g. milliseconds)
        public string EditModelId;
        public string ImageInputField = "image_urls";
        public bool ImageInputIsArray = true;
        public bool SupportsNumImages = true;
        public bool EditSupportsNumImages = true;
        public bool SupportsImageSize = true;
        public string LicenseType;
        public string ModelUrl;
        public string VerifiedAt;
        public string OutputFormat;
        public string EditOutputFormat;
        public string[] Modes;
        public string ModelOutputField;
        public string TextureField;
        public string RouterProviderTag;
        public Newtonsoft.Json.Linq.JObject RouterParameters;
    }

    /// <summary>
    /// Shared registry for the panel and tools. Live fal and OpenRouter snapshots replace
    /// bundled entries, including removals. Discovered entries are verified before generation.
    /// </summary>
    public static class AssetGenModelCatalog
    {
        private static readonly ModelEntry[] Curated =
        {
            // Image — fal (FalAdapter.DefaultModel is first => the default)
            new ModelEntry { Id = FalAdapter.DefaultModel, Label = "FLUX.2", Provider = "fal", Kind = "image", UseCase = "General image" },
            new ModelEntry { Id = "fal-ai/flux-2/flash", Label = "FLUX.2 Flash", Provider = "fal", Kind = "image", UseCase = "Fast / cheap image" },
            new ModelEntry { Id = "fal-ai/flux-2-pro", Label = "FLUX.2 Pro", Provider = "fal", Kind = "image", UseCase = "Top-quality image" },

            // Image — openrouter
            new ModelEntry { Id = OpenRouterAdapter.DefaultModel, Label = "Gemini 2.5 Flash Image", Provider = "openrouter", Kind = "image", UseCase = "General image" },

            // 3D — tripo / meshy (defaults reference the adapter constants)
            new ModelEntry { Id = TripoAdapter.ModelVersion, Label = "Tripo v3.1", Provider = "tripo", Kind = "model", UseCase = "Text / image -> 3D" },
            new ModelEntry { Id = "P1-20260311", Label = "Tripo P1 (premium)", Provider = "tripo", Kind = "model", UseCase = "Premium 3D" },
            new ModelEntry { Id = MeshyAdapter.DefaultModel, Label = "Meshy 6", Provider = "meshy", Kind = "model", UseCase = "Text / image -> 3D" },
            new ModelEntry { Id = FalModelAdapter.DefaultModel, Label = "Hunyuan3D", Provider = "fal", Kind = "model", UseCase = "Text -> 3D", Modes = new[] { "text" } },

            // Audio — fal (order: stable-audio, cassette SFX, cassette music, lyria). DurationField
            // is the request key each endpoint expects; null (Lyria) => prompt-only, no duration knob.
            new ModelEntry { Id = FalAudioAdapter.DefaultModel, Label = "Stable Audio 2.5", Provider = "fal", Kind = "audio", UseCase = "Music + SFX", MaxDurationSeconds = 190f,
                DurationField = "seconds_total", DefaultDurationSeconds = 30f,
                CommercialNote = "Review the model's license and provider terms before commercial use." },
            new ModelEntry { Id = "cassetteai/sound-effects-generator", Label = "CassetteAI SFX", Provider = "fal", Kind = "audio", UseCase = "Sound effects", MaxDurationSeconds = 30f,
                DurationField = "duration", DefaultDurationSeconds = 10f, MinDurationSeconds = 1f },
            new ModelEntry { Id = "cassetteai/music-generator", Label = "CassetteAI Music", Provider = "fal", Kind = "audio", UseCase = "Background music", MaxDurationSeconds = 180f,
                DurationField = "duration", DefaultDurationSeconds = 10f, MinDurationSeconds = 1f },
            new ModelEntry { Id = "fal-ai/lyria2", Label = "Google Lyria 2", Provider = "fal", Kind = "audio", UseCase = "Background music", MaxDurationSeconds = 30f },
        };

        internal static IReadOnlyList<ModelEntry> Bundled(string provider, string kind)
            => Curated.Where(e => Eq(e.Provider, provider) && Eq(e.Kind, kind)).ToArray();

        /// <summary>Current entries for a provider+kind. Never null.</summary>
        public static IReadOnlyList<ModelEntry> ForProvider(string provider, string kind)
        {
            if (Eq(provider, "fal") && FalModelCatalog.TryGet(kind, out var entries)) return entries;
            if (Eq(provider, "openrouter") && Eq(kind, "image") && OpenRouterModelCatalog.TryGet(out var images)) return images;
            return Bundled(provider, kind);
        }

        /// <summary>The current entry with this id, or null.</summary>
        public static ModelEntry Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (string kind in new[] { "audio", "image", "model" })
                foreach (string provider in new[] { "fal", "openrouter", "tripo", "meshy" })
                    foreach (ModelEntry e in ForProvider(provider, kind))
                        if (Eq(e.Id, id)) return e;
            return null;
        }

        /// <summary>The first current entry for a provider+kind, or null.</summary>
        public static string DefaultModelId(string provider, string kind)
        {
            return ForProvider(provider, kind).FirstOrDefault(e => e.Modes == null || e.Modes.Contains("text"))?.Id;
        }

        /// <summary>
        /// The model id a generate_* tool should use: an explicit <paramref name="requested"/> wins,
        /// else the GUI-selected model for this (kind, provider), else the catalog default. Missing
        /// saved selections missing from a live catalog are rejected; explicit IDs are verified at submit.
        /// Single home for the
        /// empty -> GUI-selected -> catalog-default precedence shared by all three generate tools.
        /// </summary>
        public static string ResolveModel(string kind, string provider, string requested)
        {
            string model = requested;
            if (string.IsNullOrWhiteSpace(model)) model = AssetGenPrefs.GetSelectedModel(kind, provider);
            if (string.IsNullOrWhiteSpace(model)) model = DefaultModelId(provider, kind);
            bool authoritative = Eq(provider, "fal") && FalModelCatalog.Source(kind) != "bundled"
                || Eq(provider, "openrouter") && kind == "image" && OpenRouterModelCatalog.Source != "bundled";
            var entries = ForProvider(provider, kind);
            if (authoritative && string.IsNullOrWhiteSpace(requested)
                && (string.IsNullOrWhiteSpace(model) || !entries.Any(e => Eq(e.Id, model))))
                throw new InvalidOperationException($"Model '{model}' is not in the current {kind} catalog. Refresh models and choose an available model; your saved selection has been preserved.");
            return string.IsNullOrWhiteSpace(model) ? null : model;
        }

        internal static void ResetForTests(bool isolate = false)
        {
            FalModelCatalog.ResetForTests(isolate);
            OpenRouterModelCatalog.ResetForTests(isolate);
        }

        private static bool Eq(string a, string b)
            => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
