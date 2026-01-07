#if !UNITY_2021_1_OR_NEWER
using System;
using System.Reflection;
#endif
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace AssetStoreTools.Utility
{
    internal static class PackageUtility
    {
        public class PackageInfoSampleMetadata
        {
            public string DisplayName;
            public string Description;
            public string Path;
        }

        public class PackageInfoUnityVersionMetadata
        {
            /// <summary>
            /// Major bit of the Unity version, e.g. 2021.3
            /// </summary>
            public string Version;
            /// <summary>
            /// Minor bit of the Unity version, e.g. 0f1
            /// </summary>
            public string Release;

            public override string ToString()
            {
                if (string.IsNullOrEmpty(Version))
                    return Release;

                if (string.IsNullOrEmpty(Release))
                    return Release;

                return $"{Version}.{Release}";
            }
        }

        /// <summary>
        /// Returns a package identifier, consisting of package name and package version
        /// </summary>
        /// <param name="package"></param>
        /// <returns></returns>
        public static string GetPackageIdentifier(this PackageInfo package)
        {
            return $"{package.name}-{package.version}";
        }

        public static PackageInfo[] GetAllPackages()
        {
#if !UNITY_2021_1_OR_NEWER
            var method = typeof(PackageInfo).GetMethod("GetAll", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new Type[0], null);
            var packages = method?.Invoke(null, null) as PackageInfo[];
#else
            var packages = PackageInfo.GetAllRegisteredPackages();
#endif
            return packages;
        }

        public static PackageInfo[] GetAllLocalPackages()
        {
            var packages = GetAllPackages();
            var localPackages = packages.Where(x => x.source == PackageSource.Embedded || x.source == PackageSource.Local)
                .Where(x => x.isDirectDependency).ToArray();
            return localPackages;
        }

        public static PackageInfo[] GetAllRegistryPackages()
        {
            var packages = GetAllPackages();
            var registryPackages = packages.Where(x => x.source == PackageSource.Registry || x.source == PackageSource.BuiltIn)
                .OrderBy(x => string.Compare(x.type, "module", System.StringComparison.OrdinalIgnoreCase) == 0)
                .ThenBy(x => x.name).ToArray();

            return registryPackages;
        }

        public static bool GetPackageByManifestPath(string packageManifestPath, out PackageInfo package)
        {
            package = null;

            if (string.IsNullOrEmpty(packageManifestPath))
                return false;

            var fileInfo = new FileInfo(packageManifestPath);
            if (!fileInfo.Exists)
                return false;

            var allPackages = GetAllPackages();

            package = allPackages.FirstOrDefault(x => Path.GetFullPath(x.resolvedPath).Equals(fileInfo.Directory.FullName));
            return package != null;
        }

        public static bool GetPackageByPackageName(string packageName, out PackageInfo package)
        {
            package = null;

            if (string.IsNullOrEmpty(packageName))
                return false;

            return GetPackageByManifestPath($"Packages/{packageName}/package.json", out package);
        }

        public static TextAsset GetManifestAsset(this PackageInfo packageInfo)
        {
            return AssetDatabase.LoadAssetAtPath<TextAsset>($"{packageInfo.assetPath}/package.json");
        }

        public static List<PackageInfoSampleMetadata> GetSamples(this PackageInfo packageInfo)
        {
            var samples = new List<PackageInfoSampleMetadata>();

            var packageManifest = packageInfo.GetManifestAsset();
            var json = JObject.Parse(packageManifest.text);

            if (!json.ContainsKey("samples") || json["samples"].Type != JTokenType.Array)
                return samples;

            var sampleList = json["samples"].ToList();
            foreach (JObject sample in sampleList)
            {
                var displayName = string.Empty;
                var description = string.Empty;
                var path = string.Empty;

                if (sample.ContainsKey("displayName"))
                    displayName = sample["displayName"].ToString();
                if (sample.ContainsKey("description"))
                    description = sample["description"].ToString();
                if (sample.ContainsKey("path"))
                    path = sample["path"].ToString();

                if (!string.IsNullOrEmpty(displayName) || !string.IsNullOrEmpty(description) || !string.IsNullOrEmpty(path))
                    samples.Add(new PackageInfoSampleMetadata() { DisplayName = displayName, Description = description, Path = path });
            }

            return samples;
        }

        public static PackageInfoUnityVersionMetadata GetUnityVersion(this PackageInfo packageInfo)
        {
            var packageManifest = packageInfo.GetManifestAsset();
            var json = JObject.Parse(packageManifest.text);

            var unityVersion = string.Empty;
            var unityRelease = string.Empty;

            if (json.ContainsKey("unity"))
                unityVersion = json["unity"].ToString();
            if (json.ContainsKey("unityRelease"))
                unityRelease = json["unityRelease"].ToString();

            return new PackageInfoUnityVersionMetadata()
            {
                Version = unityVersion,
                Release = unityRelease
            };
        }
    }
}