namespace MCPForUnity.Editor.ActionTrace.Capture
{
    /// <summary>
    /// Built-in capture point identifiers.
    /// </summary>
    public static class BuiltInCapturePoints
    {
        public const string UnityCallbacks = "UnityCallbacks";
        public const string AssetPostprocessor = "AssetPostprocessor";
        public const string PropertyTracking = "PropertyTracking";
        public const string SelectionTracking = "SelectionTracking";
        public const string HierarchyTracking = "HierarchyTracking";
        public const string BuildTracking = "BuildTracking";
        public const string CompilationTracking = "CompilationTracking";
        public const string ToolInvocation = "ToolInvocation";
    }
}
