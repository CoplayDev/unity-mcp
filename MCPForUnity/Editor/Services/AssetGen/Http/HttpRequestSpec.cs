using System.Collections.Generic;

namespace MCPForUnity.Editor.Services.AssetGen.Http
{
    /// <summary>
    /// Transport-agnostic description of a single HTTP request. Provider adapters build one
    /// of these and hand it to an <see cref="IHttpTransport"/>; this keeps adapters free of
    /// any direct dependency on UnityWebRequest so they can be unit-tested without a network.
    /// </summary>
    public sealed class HttpRequestSpec
    {
        public string Method;
        public string Url;
        public Dictionary<string, string> Headers = new Dictionary<string, string>();
        public byte[] Body;
        public string ContentType;
    }
}
