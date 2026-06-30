namespace MCPForUnity.Editor.Services.AssetGen.Providers
{
    /// <summary>Normalized lifecycle state reported by a provider poll, across all providers.</summary>
    public enum ProviderPollState
    {
        Queued,
        Running,
        Succeeded,
        Failed
    }

    /// <summary>
    /// Outcome of a single provider poll. <see cref="Progress"/> is normalized to 0..1.
    /// On <see cref="ProviderPollState.Succeeded"/>, <see cref="DownloadUrl"/> points at the
    /// result the C# side will download into the project. On
    /// <see cref="ProviderPollState.Failed"/>, <see cref="Error"/> carries a redacted message.
    /// </summary>
    public sealed class ProviderPollResult
    {
        public ProviderPollState State;
        public float Progress;
        public string DownloadUrl;
        /// <summary>Inline result bytes for synchronous providers that return base64 (e.g. OpenRouter),
        /// so the job manager skips the download step. Takes precedence over <see cref="DownloadUrl"/>.</summary>
        public byte[] InlineData;
        /// <summary>Overrides the downloaded file extension, e.g. "zip" for archive results.</summary>
        public string ResultExt;
        public string Error;
    }

    /// <summary>Request to generate a 3D model. Shared by every model provider adapter.</summary>
    public sealed class ModelGenRequest
    {
        public string Provider;
        public string Mode; // text | image
        public string Prompt;
        public string ImagePath;
        public string ImageUrl;
        public string Format = "glb";
        public float TargetSize = 1f;
        public bool Texture = true;
        public string Tier;
        public string Name;
        public string OutputFolder;
    }

    /// <summary>Request to generate a 2D image. Shared by every image provider adapter.</summary>
    public sealed class ImageGenRequest
    {
        public string Provider;
        public string Mode; // text | image
        public string Prompt;
        public string ImagePath;
        public string ImageUrl;
        public string Model;
        public bool Transparent;
        public bool AsSprite = true; // import as Sprite (2D/UI) vs Default texture
        public int Width;
        public int Height;
        public string Name;
        public string OutputFolder;
    }

    /// <summary>
    /// Public, key-free description of a provider for <c>list_providers</c>. Never carries a key
    /// value — <see cref="Configured"/> reports existence only.
    /// </summary>
    public sealed class ProviderInfo
    {
        public string Id;
        public string Kind; // model | image | marketplace
        public bool Configured;
        public string[] Capabilities;
    }
}
