using System;
using System.Collections.Generic;
using MCPForUnity.Editor.Security;

namespace MCPForUnity.Editor.Services.AssetGen.Providers
{
    /// <summary>
    /// Factory + registry for asset-gen provider adapters. Only Tripo is implemented in this
    /// phase; the rest throw <see cref="NotSupportedException"/> until their phases land, while
    /// still appearing in <see cref="List"/> so the GUI/CLI can advertise them. <see cref="List"/>
    /// reports <c>Configured</c> existence only and never exposes a key value.
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
                case "hunyuan":
                    return new HunyuanAdapter();
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
                new ProviderInfo { Id = "hunyuan", Kind = "model", Configured = IsConfigured("hunyuan"), Capabilities = new[] { "text", "image" } },
                new ProviderInfo { Id = "sketchfab", Kind = "marketplace", Configured = IsConfigured("sketchfab"), Capabilities = new[] { "search", "import" } },
                new ProviderInfo { Id = "fal", Kind = "image", Configured = IsConfigured("fal"), Capabilities = new[] { "text", "image" } },
                new ProviderInfo { Id = "openrouter", Kind = "image", Configured = IsConfigured("openrouter"), Capabilities = new[] { "text", "image" } },
            };
        }

        private static bool IsConfigured(string id)
        {
            try { return SecureKeyStore.Current.Has(id); }
            catch { return false; }
        }
    }
}
