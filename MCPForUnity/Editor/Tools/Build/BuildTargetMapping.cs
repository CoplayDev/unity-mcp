using System;
using UnityEditor;
using UnityEditor.Build;

namespace MCPForUnity.Editor.Tools.Build
{
    public static class BuildTargetMapping
    {
        private const string VisionOSName = "VisionOS";

        public static bool TryResolveBuildTarget(string name, out BuildTarget target)
        {
            if (string.IsNullOrEmpty(name))
            {
                target = EditorUserBuildSettings.activeBuildTarget;
                return true;
            }

            switch (name.ToLowerInvariant())
            {
                case "windows64": target = BuildTarget.StandaloneWindows64; return true;
                case "windows": case "windows32": target = BuildTarget.StandaloneWindows; return true;
                case "osx": case "macos": target = BuildTarget.StandaloneOSX; return true;
                case "linux64": case "linux": target = BuildTarget.StandaloneLinux64; return true;
                case "android": target = BuildTarget.Android; return true;
                case "ios": target = BuildTarget.iOS; return true;
                case "webgl": target = BuildTarget.WebGL; return true;
                case "uwp": target = BuildTarget.WSAPlayer; return true;
                case "tvos": target = BuildTarget.tvOS; return true;
                default:
                    if (Enum.TryParse(name, true, out target))
                        return true;
                    target = default;
                    return false;
            }
        }

        public static BuildTargetGroup GetTargetGroup(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                case BuildTarget.StandaloneOSX:
                case BuildTarget.StandaloneLinux64:
                    return BuildTargetGroup.Standalone;
                case BuildTarget.iOS: return BuildTargetGroup.iOS;
                case BuildTarget.Android: return BuildTargetGroup.Android;
                case BuildTarget.WebGL: return BuildTargetGroup.WebGL;
                case BuildTarget.WSAPlayer: return BuildTargetGroup.WSA;
                case BuildTarget.tvOS: return BuildTargetGroup.tvOS;
                default:
                    if (IsVisionOSTarget(target)
                        && Enum.TryParse(VisionOSName, true, out BuildTargetGroup visionOSGroup))
                    {
                        return visionOSGroup;
                    }

                    return BuildTargetGroup.Unknown;
            }
        }

        public static NamedBuildTarget GetNamedBuildTarget(BuildTarget target)
        {
            return NamedBuildTarget.FromBuildTargetGroup(GetTargetGroup(target));
        }

        public static string TryResolveNamedBuildTarget(string name, out NamedBuildTarget namedTarget)
        {
            if (!TryResolveBuildTarget(name, out var buildTarget))
            {
                namedTarget = default;
                return GetUnknownBuildTargetMessage(name);
            }

            var targetGroup = GetTargetGroup(buildTarget);
            if (targetGroup == BuildTargetGroup.Unknown)
            {
                namedTarget = default;
                return IsVisionOSTarget(buildTarget)
                    ? "VisionOS build target is available, but its BuildTargetGroup is not exposed by this Unity editor installation."
                    : $"Build target group could not be resolved for target '{buildTarget}'.";
            }

            namedTarget = NamedBuildTarget.FromBuildTargetGroup(targetGroup);
            return null;
        }

        public static string GetUnknownBuildTargetMessage(string name)
        {
            if (string.Equals(name, "visionos", StringComparison.OrdinalIgnoreCase))
            {
                return "VisionOS build target is not available in this Unity editor installation. "
                    + "Install the visionOS build support module or use a Unity version/configuration that exposes BuildTarget.VisionOS.";
            }

            return $"Unknown build target: '{name}'. Valid targets: {GetValidTargetsList()}.";
        }

        private static string GetValidTargetsList()
        {
            string validTargets = "windows64, osx, linux64, android, ios, webgl, uwp, tvos";
            if (Enum.TryParse(VisionOSName, true, out BuildTarget _))
            {
                validTargets += ", visionos";
            }

            return validTargets;
        }

        private static bool IsVisionOSTarget(BuildTarget target)
        {
            return string.Equals(target.ToString(), VisionOSName, StringComparison.OrdinalIgnoreCase);
        }

        public static string GetDefaultOutputPath(BuildTarget target, string productName)
        {
            string basePath = $"Builds/{target}";
            switch (target)
            {
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                    return $"{basePath}/{productName}.exe";
                case BuildTarget.StandaloneOSX:
                    return $"{basePath}/{productName}.app";
                case BuildTarget.StandaloneLinux64:
                    return $"{basePath}/{productName}.x86_64";
                case BuildTarget.Android:
                    return EditorUserBuildSettings.buildAppBundle
                        ? $"{basePath}/{productName}.aab"
                        : $"{basePath}/{productName}.apk";
                case BuildTarget.iOS:
                case BuildTarget.WebGL:
                    return $"{basePath}/{productName}";
                default:
                    return $"{basePath}/{productName}";
            }
        }

        public static int ResolveSubtarget(string subtarget)
        {
            if (string.IsNullOrEmpty(subtarget))
                return (int)StandaloneBuildSubtarget.Player;
            string lower = subtarget.ToLowerInvariant();
            if (lower == "server")
                return (int)StandaloneBuildSubtarget.Server;
            return (int)StandaloneBuildSubtarget.Player;
        }
    }
}
