using System.Threading.Tasks;

namespace MCPForUnity.Editor.Services.Transport
{
    /// <summary>
    /// Abstraction for MCP transport implementations (e.g. WebSocket push, stdio).
    /// </summary>
    public interface IMcpTransportClient
    {
        bool IsConnected { get; }
        string TransportName { get; }
        TransportState State { get; }

        Task<bool> StartAsync();
        Task StopAsync();
        Task<bool> VerifyAsync();
    }
}
