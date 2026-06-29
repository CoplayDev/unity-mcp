namespace MCPForUnity.Editor.Services.AssetGen.Http
{
    /// <summary>
    /// Transport-agnostic result of an <see cref="HttpRequestSpec"/>. <see cref="Status"/> is
    /// the numeric HTTP status code; <see cref="IsSuccess"/> reflects the transport's own view
    /// of success (e.g. UnityWebRequest.Result.Success), which adapters combine with their own
    /// body-level checks.
    /// </summary>
    public sealed class HttpResult
    {
        public int Status;
        public byte[] Body;
        public string Text;
        public bool IsSuccess;

        /// <summary>True when the transport reports success or the status code is 2xx.</summary>
        public bool Ok => IsSuccess || (Status >= 200 && Status < 300);
    }
}
