using AssetStoreTools.Previews.Data;
using AssetStoreTools.Utility;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using CacheConstants = AssetStoreTools.Constants.Cache;

namespace AssetStoreTools.Exporter
{
    internal class DefaultPackageExporter : PackageExporterBase
    {
        private const string TemporaryExportPathName = "CustomExport";

        private DefaultExporterSettings _defaultExportSettings;

        public DefaultPackageExporter(DefaultExporterSettings settings) : base(settings)
        {
            _defaultExportSettings = settings;
        }

        protected override void ValidateSettings()
        {
            base.ValidateSettings();

            if (_defaultExportSettings.ExportPaths == null || _defaultExportSettings.ExportPaths.Length == 0)
                throw new ArgumentException("Export paths array cannot be empty");
        }

        protected override async Task<PackageExporterResult> ExportImpl()
        {
            return await this.Export();
        }

        private new async Task<PackageExporterResult> Export()
        {
            ASDebug.Log("Using custom package exporter");

            // Save assets before exporting
            EditorUtility.DisplayProgressBar(ProgressBarTitle, ProgressBarStepSavingAssets, 0.1f);
            AssetDatabase.SaveAssets();

            try
            {
                // Create a temporary export path
                PostExportCleanup();
                var temporaryExportPath = GetTemporaryExportPath();
                if (!Directory.Exists(temporaryExportPath))
                    Directory.CreateDirectory(temporaryExportPath);

                // Construct an unzipped package structure
                CreateTempPackageStructure(temporaryExportPath);

                var previewGenerationResult = await GeneratePreviews();
                InjectPreviews(previewGenerationResult, temporaryExportPath);

                // Build a .unitypackage file from the temporary folder
                CreateUnityPackage(temporaryExportPath, _defaultExportSettings.OutputFilename);

                EditorUtility.RevealInFinder(_defaultExportSettings.OutputFilename);

                ASDebug.Log($"Package file has been created at {_defaultExportSettings.OutputFilename}");
                return new PackageExporterResult() { Success = true, ExportedPath = _defaultExportSettings.OutputFilename, PreviewGenerationResult = previewGenerationResult };
            }
            catch (Exception e)
            {
                return new PackageExporterResult() { Success = false, Exception = e };
            }
            finally
            {
                PostExportCleanup();
            }
        }

        private string GetTemporaryExportPath()
        {
            return $"{CacheConstants.TempCachePath}/{TemporaryExportPathName}";
        }

        private void CreateTempPackageStructure(string tempOutputPath)
        {
            EditorUtility.DisplayProgressBar(ProgressBarTitle, ProgressBarStepGatheringFiles, 0.4f);
            var pathGuidPairs = GetPathGuidPairs(_defaultExportSettings.ExportPaths);

            foreach (var pair in pathGuidPairs)
            {
                var originalAssetPath = pair.Key;
                var outputAssetPath = $"{tempOutputPath}/{pair.Value}";

                if (Directory.Exists(outputAssetPath))
                {
                    var path1 = File.ReadAllText($"{outputAssetPath}/pathname");
                    var path2 = originalAssetPath;
                    throw new InvalidOperationException($"Multiple assets with guid {pair.Value} have been detected " +
                        $"when exporting the package. Please resolve the guid conflicts and try again:\n{path1}\n{path2}");
                }

                Directory.CreateDirectory(outputAssetPath);

                // Every exported asset has a pathname file
                using (StreamWriter writer = new StreamWriter($"{outputAssetPath}/pathname"))
                    writer.Write(originalAssetPath);

                // Only files (not folders) have an asset file
                if (File.Exists(originalAssetPath))
                    File.Copy(originalAssetPath, $"{outputAssetPath}/asset");

                // Most files and folders have an asset.meta file (but ProjectSettings folder assets do not)
                if (File.Exists($"{originalAssetPath}.meta"))
                    File.Copy($"{originalAssetPath}.meta", $"{outputAssetPath}/asset.meta");

                // To-do: handle previews in hidden folders as they are not part of the AssetDatabase
                var previewObject = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(originalAssetPath);
                if (previewObject == null)
                    continue;
            }

            if (_defaultExportSettings.Dependencies == null || _defaultExportSettings.Dependencies.Length == 0)
                return;

            var exportDependenciesDict = new JObject();
            var allRegistryPackages = PackageUtility.GetAllRegistryPackages();

            foreach (var exportDependency in _defaultExportSettings.Dependencies)
            {
                var registryPackage = allRegistryPackages.FirstOrDefault(x => x.name == exportDependency);
                if (registryPackage == null)
                {
                    // Package is either not from a registry source or does not exist in the project
                    UnityEngine.Debug.LogWarning($"Found an unsupported Package Manager dependency: {exportDependency}.\n" +
                                             "This dependency is not supported in the project's manifest.json and will be skipped.");
                    continue;
                }

                exportDependenciesDict[registryPackage.name] = registryPackage.version;
            }

            if (exportDependenciesDict.Count == 0)
                return;

            var exportManifestJson = new JObject();
            exportManifestJson["dependencies"] = exportDependenciesDict;

            var tempManifestDirectoryPath = $"{tempOutputPath}/packagemanagermanifest";
            Directory.CreateDirectory(tempManifestDirectoryPath);
            var tempManifestFilePath = $"{tempManifestDirectoryPath}/asset";

            File.WriteAllText(tempManifestFilePath, exportManifestJson.ToString());
        }

        private Dictionary<string, string> GetPathGuidPairs(string[] exportPaths)
        {
            var pathGuidPairs = new Dictionary<string, string>();

            foreach (var exportPath in exportPaths)
            {
                var assetPaths = GetAssetPaths(exportPath);

                foreach (var assetPath in assetPaths)
                {
                    var guid = GetAssetGuid(assetPath, true, true);
                    if (string.IsNullOrEmpty(guid))
                        continue;

                    pathGuidPairs.Add(assetPath, guid);
                }
            }

            return pathGuidPairs;
        }

        private async Task<PreviewGenerationResult> GeneratePreviews()
        {
            if (_defaultExportSettings.PreviewGenerator == null)
                return null;

            void ReportProgress(float progress)
            {
                EditorUtility.DisplayProgressBar(ProgressBarTitle, ProgressBarStepGeneratingPreviews, progress);
            }

            _defaultExportSettings.PreviewGenerator.OnProgressChanged += ReportProgress;
            var result = await _defaultExportSettings.PreviewGenerator.Generate();
            _defaultExportSettings.PreviewGenerator.OnProgressChanged -= ReportProgress;
            EditorUtility.ClearProgressBar();

            if (!result.Success)
            {
                UnityEngine.Debug.LogWarning($"An error was encountered while generating previews. Exported package may be missing previews.\n{result.Exception}");
            }

            return result;
        }

        private void InjectPreviews(PreviewGenerationResult result, string temporaryExportPath)
        {
            if (result == null || !result.Success)
                return;

            var injector = new PreviewInjector(result);
            injector.Inject(temporaryExportPath);
        }

        private void CreateUnityPackage(string pathToArchive, string outputPath)
        {
            if (Directory.GetDirectories(pathToArchive).Length == 0)
                throw new InvalidOperationException("Unable to export package. The specified path is empty");

            EditorUtility.DisplayProgressBar(ProgressBarTitle, ProgressBarStepCompressingPackage, 0.8f);

            // Archiving process working path will be set to the
            // temporary package path so adjust the output path accordingly
            if (!Path.IsPathRooted(outputPath))
                outputPath = $"{Application.dataPath.Substring(0, Application.dataPath.Length - "/Assets".Length)}/{outputPath}";

#if UNITY_EDITOR_WIN
            CreateUnityPackageUniversal(pathToArchive, outputPath);
#elif UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
            CreateUnityPackageOsxLinux(pathToArchive, outputPath);
#endif
        }

        private void CreateUnityPackageUniversal(string pathToArchive, string outputPath)
        {
            var _7zPath = EditorApplication.applicationContentsPath;
#if UNITY_EDITOR_WIN
            _7zPath = Path.Combine(_7zPath, "Tools", "7z.exe");
#elif UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
            _7zPath = Path.Combine(_7zPath, "Tools", "7za");
#endif
            if (!File.Exists(_7zPath))
                throw new FileNotFoundException("Archiving utility was not found in your Unity installation directory");

            var argumentsTar = $"a -r -ttar -y -bd archtemp.tar .";
            var result = StartProcess(_7zPath, argumentsTar, pathToArchive);
            if (result != 0)
                throw new Exception("Failed to compress the package");

            // Create a GZIP archive
            var argumentsGzip = $"a -tgzip -bd -y \"{outputPath}\" archtemp.tar";
            result = StartProcess(_7zPath, argumentsGzip, pathToArchive);
            if (result != 0)
                throw new Exception("Failed to compress the package");
        }

#if UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
        private void CreateUnityPackageOsxLinux(string pathToArchive, string outputPath)
        {
            var tarPath = "/usr/bin/tar";

            if (!File.Exists(tarPath))
            {
                // Fallback to the universal export method
                ASDebug.LogWarning("'/usr/bin/tar' executable not found. Falling back to 7za");
                CreateUnityPackageUniversal(pathToArchive, outputPath);
                return;
            }

            // Create a TAR archive
            var arguments = $"-czpf \"{outputPath}\" .";
            var result = StartProcess(tarPath, arguments, pathToArchive);
            if (result != 0)
                throw new Exception("Failed to compress the package");
        }
#endif

        private int StartProcess(string processPath, string arguments, string workingDirectory)
        {
            var info = new ProcessStartInfo()
            {
                FileName = processPath,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                CreateNoWindow = true,
                UseShellExecute = false
            };

#if UNITY_EDITOR_OSX
            // Prevent OSX-specific archive pollution
            info.EnvironmentVariables.Add("COPYFILE_DISABLE", "1");
#endif

            using (Process process = Process.Start(info))
            {
                process.WaitForExit();
                return process.ExitCode;
            }
        }

        protected override void PostExportCleanup()
        {
            base.PostExportCleanup();

            var tempExportPath = GetTemporaryExportPath();
            if (Directory.Exists(tempExportPath))
                Directory.Delete(tempExportPath, true);
        }
    }
}