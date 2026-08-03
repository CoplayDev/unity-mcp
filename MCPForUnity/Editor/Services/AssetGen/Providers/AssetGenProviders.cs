using System;
using System.Collections.Generic;
using MCPForUnity.Editor.Security;

namespace MCPForUnity.Editor.Services.AssetGen.Providers
{
    /// <summary>
    /// Factory + registry for asset-gen provider adapters. Resolves a provider id to its adapter
    /// (model: tripo/meshy; image: fal/openrouter; audio: fal; marketplace: sketchfab); unknown ids
    /// throw <see cref="NotSupportedException"/>. <see cref="List"/> advertises providers and reports
    /// <c>Configured</c> existence only — never a key value.
    /// </summary>
    public static class AssetGenProviders
    {
        public static IModelProviderAdapter Model(string id)
        {
            switch ((id ?? string.Empty).ToLowerInvariant())
            {
                case "tripo":
                    return new TripoAdapter();
                case "meshy":
                    return new MeshyAdapter();
                default:
                    throw new NotSupportedException($"Unknown model provider '{id}'.");
            }
        }

        public static IImageProviderAdapter Image(string id)
        {
            switch ((id ?? string.Empty).ToLowerInvariant())
            {
                case "fal":
                    return new FalAdapter();
                case "openrouter":
                    return new OpenRouterAdapter();
                default:
                    throw new NotSupportedException($"Unknown image provider '{id}'.");
            }
        }

        public static IAudioProviderAdapter Audio(string id)
        {
            switch ((id ?? string.Empty).ToLowerInvariant())
            {
                case "fal":
                    return new FalAudioAdapter();
                default:
                    throw new NotSupportedException($"Unknown audio provider '{id}'.");
            }
        }

        public static IMarketplaceProviderAdapter Marketplace(string id)
        {
            switch ((id ?? string.Empty).ToLowerInvariant())
            {
                case "sketchfab":
                    return new SketchfabAdapter();
                default:
                    throw new NotSupportedException($"Unknown marketplace provider '{id}'.");
            }
        }

        public static IReadOnlyList<ProviderInfo> List()
        {
            return new List<ProviderInfo>
            {
                new ProviderInfo { Id = "tripo", Kind = "model", Configured = IsConfigured("tripo"), Capabilities = new[] { "text", "image" } },
                new ProviderInfo { Id = "meshy", Kind = "model", Configured = IsConfigured("meshy"), Capabilities = new[] { "text", "image" } },
                new ProviderInfo { Id = "sketchfab", Kind = "marketplace", Configured = IsConfigured("sketchfab"), Capabilities = new[] { "search", "import" } },
                new ProviderInfo { Id = "fal", Kind = "image", Configured = IsConfigured("fal"), Capabilities = new[] { "text", "image" } },
                new ProviderInfo { Id = "openrouter", Kind = "image", Configured = IsConfigured("openrouter"), Capabilities = new[] { "text", "image" } },
                // fal appears twice by design — once per kind (image + audio) — sharing the single "fal" key.
                new ProviderInfo { Id = "fal", Kind = "audio", Configured = IsConfigured("fal"), Capabilities = new[] { "text", "music", "sfx" } },
            };
        }

        private static bool IsConfigured(string id)
        {
            try { return SecureKeyStore.Current.Has(id); }
            catch { return false; }
        }

        /// <summary>
        /// Standard "no key" message: points the user at the Asset Generation tab and the env override.
        /// Shared by the asset-gen tools and the job manager so the wording stays in one place.
        /// </summary>
        public static string MissingKeyMessage(string provider)
            => $"No API key configured for '{provider}'. Add it in the MCP for Unity → Asset Generation tab " +
               $"(or set MCPFORUNITY_{(provider ?? string.Empty).ToUpperInvariant()}_API_KEY).";
    }
}
