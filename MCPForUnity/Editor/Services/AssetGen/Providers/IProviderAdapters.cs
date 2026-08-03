using System.Threading;
using System.Threading.Tasks;
using MCPForUnity.Editor.Services.AssetGen.Http;

namespace MCPForUnity.Editor.Services.AssetGen.Providers
{
    /// <summary>
    /// A generative 3D model provider (Tripo, Meshy, ...). Submit mints a provider-side
    /// job id; poll reports progress and, on success, the download URL. The api key is passed in
    /// at call time and never cached on the adapter.
    /// </summary>
    public interface IModelProviderAdapter
    {
        string Id { get; }
        Task<string> SubmitAsync(ModelGenRequest req, string apiKey, IHttpTransport http, CancellationToken ct);
        Task<ProviderPollResult> PollAsync(string providerJobId, string apiKey, IHttpTransport http, CancellationToken ct);
    }

    /// <summary>A generative 2D image provider (fal, OpenRouter, ...). Phase 7.</summary>
    public interface IImageProviderAdapter
    {
        string Id { get; }
        Task<string> SubmitAsync(ImageGenRequest req, string apiKey, IHttpTransport http, CancellationToken ct);
        Task<ProviderPollResult> PollAsync(string providerJobId, string apiKey, IHttpTransport http, CancellationToken ct);
    }

    /// <summary>
    /// A generative audio provider (fal.ai for v1). All v1 audio models route through the single
    /// fal adapter; the model is chosen inside <see cref="AudioGenRequest.Model"/>. The api key is
    /// passed in at call time and never cached on the adapter.
    /// </summary>
    public interface IAudioProviderAdapter
    {
        string Id { get; }
        Task<string> SubmitAsync(AudioGenRequest req, string apiKey, IHttpTransport http, CancellationToken ct);
        Task<ProviderPollResult> PollAsync(string providerJobId, string apiKey, IHttpTransport http, CancellationToken ct);
    }

    /// <summary>A 3D marketplace provider (Sketchfab, ...). Search/preview/resolve, not generative. Phase 6.</summary>
    public interface IMarketplaceProviderAdapter
    {
        string Id { get; }
        Task<string> SearchAsync(string query, string categories, bool downloadable, int? count, string cursor, string apiKey, IHttpTransport http, CancellationToken ct);
        Task<string> PreviewAsync(string uid, string apiKey, IHttpTransport http, CancellationToken ct);
        Task<string> ResolveDownloadUrlAsync(string uid, string apiKey, IHttpTransport http, CancellationToken ct);
    }
}
