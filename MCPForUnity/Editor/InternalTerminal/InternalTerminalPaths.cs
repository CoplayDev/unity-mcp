using System;
using System.IO;
using UnityEditor.PackageManager;

namespace WTL.InternalTerminal.Editor
{
    internal static class InternalTerminalPaths
    {
        private const string PackageName = "com.coplaydev.unity-mcp";
        private const string InternalTerminalRoot = "Editor/InternalTerminal";

        public static string PackageRoot
        {
            get
            {
                var packageInfo = PackageInfo.FindForAssembly(typeof(InternalTerminalPaths).Assembly);
                if (packageInfo != null && !string.IsNullOrEmpty(packageInfo.resolvedPath))
                {
                    return packageInfo.resolvedPath;
                }

                return Path.GetFullPath(Path.Combine("Packages", PackageName));
            }
        }

        public static string BackendRoot => Path.Combine(PackageRoot, "Editor", "InternalTerminal", "NodeBackend~");
        public static string BackendEntry => Path.Combine(BackendRoot, "server.js");
        public static string PackageAssetRoot => "Packages/" + PackageName + "/" + InternalTerminalRoot;

        public static string ToFileUrl(string path)
        {
            return new Uri(path).AbsoluteUri;
        }
    }
}
