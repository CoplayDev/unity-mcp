using UnityEditor;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Helper for getting human-readable build target names.
    /// Converts Unity's BuildTarget enum to user-friendly platform names.
    /// </summary>
    internal static class BuildTargetUtility
    {
        /// <summary>
        /// Gets a human-readable name for a BuildTarget.
        /// </summary>
        public static string GetBuildTargetName(BuildTarget target)
        {
            return target switch
            {
                BuildTarget.StandaloneWindows => "Windows",
                BuildTarget.StandaloneWindows64 => "Windows64",
                BuildTarget.StandaloneOSX => "macOS",
                BuildTarget.StandaloneLinux64 => "Linux64",
                BuildTarget.Android => "Android",
                BuildTarget.iOS => "iOS",
                BuildTarget.WebGL => "WebGL",
                BuildTarget.WSAPlayer => "UWP",
                BuildTarget.PS4 => "PS4",
                BuildTarget.PS5 => "PS5",
                BuildTarget.XboxOne => "Xbox One",
                BuildTarget.Switch => "Switch",
                BuildTarget.tvOS => "tvOS",
                BuildTarget.NoTarget => "No Target",
                _ => target.ToString()
            };
        }

        /// <summary>
        /// Gets the BuildTarget from a platform name string.
        /// Reverse of GetBuildTargetName.
        /// </summary>
        public static BuildTarget? ParseBuildTarget(string platformName)
        {
            return platformName?.ToLowerInvariant() switch
            {
                "windows" => BuildTarget.StandaloneWindows,
                "windows64" => BuildTarget.StandaloneWindows64,
                "macos" => BuildTarget.StandaloneOSX,
                "linux" or "linux64" => BuildTarget.StandaloneLinux64,
                "android" => BuildTarget.Android,
                "ios" => BuildTarget.iOS,
                "webgl" => BuildTarget.WebGL,
                "uwp" => BuildTarget.WSAPlayer,
                "ps4" => BuildTarget.PS4,
                "ps5" => BuildTarget.PS5,
                "xboxone" or "xbox" => BuildTarget.XboxOne,
                "switch" => BuildTarget.Switch,
                "tvos" => BuildTarget.tvOS,
                _ => null
            };
        }
    }
}
