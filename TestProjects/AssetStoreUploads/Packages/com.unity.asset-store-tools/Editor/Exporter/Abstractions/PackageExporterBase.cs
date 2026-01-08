using AssetStoreTools.Utility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;

namespace AssetStoreTools.Exporter
{
    internal abstract class PackageExporterBase : IPackageExporter
    {
        public PackageExporterSettings Settings { get; private set; }

        public const string ProgressBarTitle = "Exporting Package";
        public const string ProgressBarStepSavingAssets = "Saving Assets...";
        public const string ProgressBarStepGatheringFiles = "Gathering files...";
        public const string ProgressBarStepGeneratingPreviews = "Generating previews...";
        public const string ProgressBarStepCompressingPackage = "Compressing package...";

        private static readonly string[] PluginFolderExtensions = { "androidlib", "bundle", "plugin", "framework", "xcframework" };

        public PackageExporterBase(PackageExporterSettings settings)
        {
            Settings = settings;
        }

        public async Task<PackageExporterResult> Export()
        {
            try
            {
                ValidateSettings();
            }
            catch (Exception e)
            {
                return new PackageExporterResult() { Success = false, Exception = e };
            }

            return await ExportImpl();
        }

        protected virtual void ValidateSettings()
        {
            if (Settings == null)
                throw new ArgumentException("Settings cannot be null");

            if (string.IsNullOrEmpty(Settings.OutputFilename))
                throw new ArgumentException("Output path cannot be null");

            if (Settings.OutputFilename.EndsWith("/") || Settings.OutputFilename.EndsWith("\\"))
                throw new ArgumentException("Output path must be a valid filename and not end with a directory separator character");
        }

        protected abstract Task<PackageExporterResult> ExportImpl();

        protected string[] GetAssetPaths(string rootPath)
        {
            // To-do: slight optimization is possible in the future by having a list of excluded folders/file extensions
            List<string> paths = new List<string>();

            // Add files within given directory
            var filePaths = Directory.GetFiles(rootPath).Select(p => p.Replace('\\', '/')).ToArray();
            paths.AddRange(filePaths);

            // Add directories within given directory
            var directoryPaths = Directory.GetDirectories(rootPath).Select(p => p.Replace('\\', '/')).ToArray();
            foreach (var nestedDirectory in directoryPaths)
                paths.AddRange(GetAssetPaths(nestedDirectory));

            // Add the given directory itself if it is not empty
            if (filePaths.Length > 0 || directoryPaths.Length > 0)
                paths.Add(rootPath);

            return paths.ToArray();
        }

        protected string GetAssetGuid(string assetPath, bool generateIfPlugin, bool scrapeFromMeta)
        {
            if (!FileUtility.ShouldHaveMeta(assetPath))
                return string.Empty;

            // Skip ProjectVersion.txt file specifically as it may introduce
            // project compatibility issues when imported
            if (string.Compare(assetPath, "ProjectSettings/ProjectVersion.txt", StringComparison.OrdinalIgnoreCase) == 0)
                return string.Empty;

            // Attempt retrieving guid from the Asset Database first
            var guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (guid != string.Empty)
                return guid;

            // Some special folders (e.g. SomeName.framework) do not have meta files inside them.
            // Their contents should be exported with any arbitrary GUID so that Unity Importer could pick them up
            if (generateIfPlugin && PathBelongsToPlugin(assetPath))
                return GUID.Generate().ToString();

            // Files in hidden folders (e.g. Samples~) are not part of the Asset Database,
            // therefore GUIDs need to be scraped from the .meta file.
            // Note: only do this for non-native exporter since the native exporter
            // will not be able to retrieve the asset path from a hidden folder
            if (scrapeFromMeta)
            {
                var metaPath = $"{assetPath}.meta";

                if (!File.Exists(metaPath))
                    return string.Empty;

                using (StreamReader reader = new StreamReader(metaPath))
                {
                    string line;
                    while ((line = reader.ReadLine()) != string.Empty)
                    {
                        if (!line.StartsWith("guid:"))
                            continue;
                        var metaGuid = line.Substring("guid:".Length).Trim();
                        return metaGuid;
                    }
                }
            }

            return string.Empty;
        }

        private bool PathBelongsToPlugin(string assetPath)
        {
            return PluginFolderExtensions.Any(extension => assetPath.ToLower().Contains($".{extension}/"));
        }

        protected virtual void PostExportCleanup()
        {
            EditorUtility.ClearProgressBar();
        }
    }
}