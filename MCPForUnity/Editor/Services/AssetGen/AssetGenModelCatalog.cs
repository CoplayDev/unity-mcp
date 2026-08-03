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
        public bool FromRefresh;         // true => merged from a fal-catalog refresh (Phase 5)
    }

    /// <summary>
    /// Curated, always-present registry of selectable models per provider+kind, with metadata for
    /// the Asset Generation panel. The first curated entry per (provider, kind) is the default, and
    /// each default's <see cref="ModelEntry.Id"/> references the owning adapter's constant directly
    /// — so the panel's shown default always equals what an omitted <c>model</c> param resolves to
    /// (a drift-guard test pins the two). A fal-catalog refresh overlay is layered on in Phase 5.
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

            // Audio — fal (order: stable-audio, cassette SFX, cassette music, lyria). DurationField
            // is the request key each endpoint expects; null (Lyria) => prompt-only, no duration knob.
            new ModelEntry { Id = FalAudioAdapter.DefaultModel, Label = "Stable Audio 2.5", Provider = "fal", Kind = "audio", UseCase = "Music + SFX", PriceLabel = "$0.20/gen", MaxDurationSeconds = 190f,
                DurationField = "seconds_total", DefaultDurationSeconds = 30f,
                CommercialNote = "Free under $1M annual revenue (Stability Community License); an Enterprise license is required at or above $1M." },
            new ModelEntry { Id = "cassetteai/sound-effects-generator", Label = "CassetteAI SFX", Provider = "fal", Kind = "audio", UseCase = "Sound effects", PriceLabel = "$0.01/gen", MaxDurationSeconds = 30f,
                DurationField = "duration", DefaultDurationSeconds = 10f, MinDurationSeconds = 1f },
            new ModelEntry { Id = "cassetteai/music-generator", Label = "CassetteAI Music", Provider = "fal", Kind = "audio", UseCase = "Background music", PriceLabel = "$0.02/min", MaxDurationSeconds = 180f,
                DurationField = "duration", DefaultDurationSeconds = 10f, MinDurationSeconds = 1f },
            new ModelEntry { Id = "fal-ai/lyria2", Label = "Google Lyria 2", Provider = "fal", Kind = "audio", UseCase = "Background music", PriceLabel = "$0.10/30s", MaxDurationSeconds = 30f },
        };

        /// <summary>Curated entries for a provider+kind, in curated order (default first). Never null.</summary>
        public static IReadOnlyList<ModelEntry> ForProvider(string provider, string kind)
        {
            var result = new List<ModelEntry>();
            foreach (ModelEntry e in Curated)
                if (Eq(e.Provider, provider) && Eq(e.Kind, kind)) result.Add(e);
            return result;
        }

        /// <summary>The curated entry with this exact id, or null.</summary>
        public static ModelEntry Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (ModelEntry e in Curated)
                if (Eq(e.Id, id)) return e;
            return null;
        }

        /// <summary>The default model id for a provider+kind (the first curated entry), or null.</summary>
        public static string DefaultModelId(string provider, string kind)
        {
            foreach (ModelEntry e in Curated)
                if (Eq(e.Provider, provider) && Eq(e.Kind, kind)) return e.Id;
            return null;
        }

        /// <summary>
        /// The model id a generate_* tool should use: an explicit <paramref name="requested"/> wins,
        /// else the GUI-selected model for this (kind, provider), else the curated default. Null when
        /// nothing resolves (the adapter then falls back to its own constant). Single home for the
        /// empty -> GUI-selected -> catalog-default precedence shared by all three generate tools.
        /// </summary>
        public static string ResolveModel(string kind, string provider, string requested)
        {
            string model = requested;
            if (string.IsNullOrWhiteSpace(model)) model = AssetGenPrefs.GetSelectedModel(kind, provider);
            if (string.IsNullOrWhiteSpace(model)) model = DefaultModelId(provider, kind);
            return string.IsNullOrWhiteSpace(model) ? null : model;
        }

        /// <summary>Clears any test/refresh state. The refresh overlay is added in Phase 5; no-op today.</summary>
        internal static void ResetForTests() { }

        private static bool Eq(string a, string b)
            => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
