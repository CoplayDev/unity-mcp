using AssetStoreTools.Utility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace AssetStoreTools.Exporter
{
    internal class LegacyPackageExporter : PackageExporterBase
    {
        private const string ExportMethodWithoutDependencies = "UnityEditor.PackageUtility.ExportPackage";
        private const string ExportMethodWithDependencies = "UnityEditor.PackageUtility.ExportPackageAndPackageManagerManifest";

        private LegacyExporterSettings _legacyExportSettings;

        public LegacyPackageExporter(LegacyExporterSettings settings) : base(settings)
        {
            _legacyExportSettings = settings;
        }

        protected override void ValidateSettings()
        {
            base.ValidateSettings();

            if (_legacyExportSettings.ExportPaths == null || _legacyExportSettings.ExportPaths.Length == 0)
                throw new ArgumentException("Export paths array cannot be empty");
        }

        protected override async Task<PackageExporterResult> ExportImpl()
        {
            return await this.Export();
        }

        private async new Task<PackageExporterResult> Export()
        {
            ASDebug.Log("Using native package exporter");

            try
            {
                var guids = GetGuids(_legacyExportSettings.ExportPaths, out bool onlyFolders);

                if (guids.Length == 0 || onlyFolders)
                    throw new ArgumentException("Package Exporting failed: provided export paths are empty or only contain empty folders");

                string exportMethod = ExportMethodWithoutDependencies;
                if (_legacyExportSettings.IncludeDependencies)
                    exportMethod = ExportMethodWithDependencies;

                var split = exportMethod.Split('.');
                var assembly = Assembly.Load(split[0]); // UnityEditor
                var typeName = $"{split[0]}.{split[1]}"; // UnityEditor.PackageUtility
                var methodName = split[2]; // ExportPackage or ExportPackageAndPackageManagerManifest

                var type = assembly.GetType(typeName);
                var method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public,
                    null, new Type[] { typeof(string[]), typeof(string) }, null);

                ASDebug.Log("Invoking native export method");

                method?.Invoke(null, new object[] { guids, _legacyExportSettings.OutputFilename });

                // The internal exporter methods are asynchronous, therefore
                // we need to wait for exporting to finish before returning
                await Task.Run(() =>
                {
                    while (!File.Exists(_legacyExportSettings.OutputFilename))
                        Thread.Sleep(100);
                });

                ASDebug.Log($"Package file has been created at {_legacyExportSettings.OutputFilename}");
                return new PackageExporterResult() { Success = true, ExportedPath = _legacyExportSettings.OutputFilename };
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

        private string[] GetGuids(string[] exportPaths, out bool onlyFolders)
        {
            var guids = new List<string>();
            onlyFolders = true;

            foreach (var exportPath in exportPaths)
            {
                var assetPaths = GetAssetPaths(exportPath);

                foreach (var assetPath in assetPaths)
                {
                    var guid = GetAssetGuid(assetPath, false, false);
                    if (string.IsNullOrEmpty(guid))
                        continue;

                    guids.Add(guid);
                    if (onlyFolders == true && (File.Exists(assetPath)))
                        onlyFolders = false;
                }
            }

            return guids.ToArray();
        }
    }
}