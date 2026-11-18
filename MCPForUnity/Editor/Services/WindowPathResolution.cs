namespace MCPForUnity.Editor.Services
{
    /// <summary>Optional window discovery contract. Existing path-service implementations remain source-compatible.</summary>
    public interface IWindowPathResolver
    {
        /// <summary>Capture preferences on main; invoke the returned read-only operation on a worker.</summary>
        System.Func<WindowPathResolution> CaptureWindowResolution();
    }

    /// <summary>Read-only path discovery result for editor-window refreshes.</summary>
    public sealed class WindowPathResolution
    {
        public string UvxPath { get; set; }
        public string ClaudePath { get; set; }
        public string OverridePath { get; set; }
        public bool HasOverride { get; set; }
        public bool HasFallback { get; set; }
        public bool UvxValid { get; set; }
    }
}
