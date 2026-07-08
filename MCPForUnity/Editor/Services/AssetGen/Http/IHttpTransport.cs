using System.Threading;
using System.Threading.Tasks;

namespace MCPForUnity.Editor.Services.AssetGen.Http
{
    /// <summary>
    /// The HTTP seam that provider adapters depend on. Production uses
    /// <see cref="UnityWebRequestTransport"/>; tests inject <see cref="FakeHttpTransport"/> so
    /// adapter request/response shaping can be verified without touching the network.
    /// </summary>
    public interface IHttpTransport
    {
        Task<HttpResult> SendAsync(HttpRequestSpec spec, CancellationToken ct);
    }
}
