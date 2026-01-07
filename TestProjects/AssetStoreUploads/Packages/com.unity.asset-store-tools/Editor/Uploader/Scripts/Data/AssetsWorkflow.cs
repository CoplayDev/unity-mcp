using AssetStoreTools.Api;
using AssetStoreTools.Exporter;
using AssetStoreTools.Uploader.Data.Serialization;
using AssetStoreTools.Utility;
using AssetStoreTools.Validator;
using AssetStoreTools.Validator.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace AssetStoreTools.Uploader.Data
{
    internal class AssetsWorkflow : WorkflowBase
    {
        public override string Name => "AssetsWorkflow";
        public override string DisplayName => "From Assets Folder";
        public override string PackageExtension => ".unitypackage";
        public override bool IsPathSet => !string.IsNullOrEmpty(_mainExportPath);
        public bool IsCompleteProject => Package.IsCompleteProject;

        private AssetsWorkflowState _stateData;

        private string _mainExportPath;
        private bool _includeDependencies;
        private List<PackageInfo> _dependencies;
        private List<string> _specialFolders;

        public override event Action OnChanged;

        // Special folders that would not work if not placed directly in the 'Assets' folder
        private readonly string[] _extraAssetFolderNames =
        {
            "Editor Default Resources", "Gizmos", "Plugins",
            "StreamingAssets", "Standard Assets", "WebGLTemplates",
            "ExternalDependencyManager", "XR"
        };

        public AssetsWorkflow(IPackage package, AssetsWorkflowState stateData, IWorkflowServices services)
            : base(package, services)
        {
            _stateData = stateData;
            Deserialize();
        }

        public string GetMainExportPath()
        {
            return _mainExportPath;
        }

        public void SetMainExportPath(string path, bool serialize)
        {
            _mainExportPath = path;
            SetMetadata();
            if (serialize)
                Serialize();
        }

        private void SetMetadata()
        {
            LocalPackageGuid = AssetDatabase.AssetPathToGUID(_mainExportPath);
            LocalPackagePath = _mainExportPath;
            LocalProjectPath = _mainExportPath;
        }

        public bool GetIncludeDependencies()
        {
            return _includeDependencies;
        }

        public void SetIncludeDependencies(bool value, bool serialize)
        {
            _includeDependencies = value;
            // Note: make sure that exporting does not fail when
            // a serialized dependency that has been removed from a project is sent to exporter
            if (serialize)
                Serialize();
        }

        public List<PackageInfo> GetDependencies()
        {
            return _dependencies;
        }

        public void SetDependencies(IEnumerable<string> dependencies, bool serialize)
        {
            _dependencies.Clear();
            foreach (var dependency in dependencies)
            {
                if (!PackageUtility.GetPackageByPackageName(dependency, out var package))
                    continue;
                _dependencies.Add(package);
            }

            if (serialize)
                Serialize();
        }

        public List<string> GetSpecialFolders()
        {
            return _specialFolders;
        }

        public void SetSpecialFolders(IEnumerable<string> specialFolders, bool serialize)
        {
            _specialFolders.Clear();
            foreach (var folder in specialFolders)
            {
                _specialFolders.Add(folder);
            }

            if (serialize)
                Serialize();
        }

        public override bool IsPathValid(string path, out string error)
        {
            error = string.Empty;

            var pathIsFolder = Directory.Exists(path);
            if (!pathIsFolder)
            {
                error = "Path must point to a valid folder";
                return false;
            }

            var pathWithinAssetsFolder = path.StartsWith("Assets/") && path != "Assets/";
            if (pathWithinAssetsFolder)
                return true;

            var pathIsAssetsFolder = path == "Assets" || path == "Assets/";
            if (pathIsAssetsFolder)
            {
                var assetsFolderSelectionAllowed = Package.IsCompleteProject;
                if (assetsFolderSelectionAllowed)
                    return true;

                error = "'Assets' folder is only available for packages tagged as a 'Complete Project'.";
                return false;
            }

            error = "Selected folder path must be within the project's Assets.";
            return false;
        }

        public List<string> GetAvailableDependencies()
        {
            var registryPackages = PackageUtility.GetAllRegistryPackages();
            return registryPackages.Select(x => x.name).ToList();
        }

        public List<string> GetAvailableSpecialFolders()
        {
            var specialFolders = new List<string>();

            foreach (var extraAssetFolderName in _extraAssetFolderNames)
            {
                var fullExtraPath = "Assets/" + extraAssetFolderName;

                if (!Directory.Exists(fullExtraPath))
                    continue;

                if (_mainExportPath.ToLower().StartsWith(fullExtraPath.ToLower()))
                    continue;

                // Don't include nested paths
                if (!fullExtraPath.ToLower().StartsWith(_mainExportPath.ToLower()))
                    specialFolders.Add(fullExtraPath);
            }

            return specialFolders;
        }

        public override IEnumerable<string> GetAllPaths()
        {
            var paths = new List<string>()
            {
                _mainExportPath
            };
            paths.AddRange(GetSpecialFolders());

            return paths;
        }

        public override IValidator CreateValidator()
        {
            var validationPaths = GetAllPaths();

            var validationSettings = new CurrentProjectValidationSettings()
            {
                Category = Package.Category,
                ValidationPaths = validationPaths.ToList(),
                ValidationType = ValidationType.UnityPackage
            };

            var validator = new CurrentProjectValidator(validationSettings);
            return validator;
        }

        public override IPackageExporter CreateExporter(string outputPath)
        {
            var exportPaths = GetAllPaths().ToList();

            if (IsCompleteProject && !exportPaths.Contains("ProjectSettings"))
            {
                exportPaths.Add("ProjectSettings");
            }

            var dependenciesToInclude = new List<string>();
            if (_includeDependencies)
            {
                dependenciesToInclude.AddRange(_dependencies.Select(x => x.name));
            }

            if (ASToolsPreferences.Instance.UseLegacyExporting)
            {
                var exportSettings = new LegacyExporterSettings()
                {
                    ExportPaths = exportPaths.ToArray(),
                    OutputFilename = outputPath,
                    IncludeDependencies = _includeDependencies,
                };

                return new LegacyPackageExporter(exportSettings);
            }
            else
            {
                var exportSettings = new DefaultExporterSettings()
                {
                    ExportPaths = exportPaths.ToArray(),
                    OutputFilename = outputPath,
                    Dependencies = dependenciesToInclude.ToArray(),
                    PreviewGenerator = CreatePreviewGenerator(exportPaths),
                };

                return new DefaultPackageExporter(exportSettings);
            }
        }

        protected override IPackageUploader CreatePackageUploader(string exportedPackagePath)
        {
            var uploaderSettings = new UnityPackageUploadSettings()
            {
                UnityPackagePath = exportedPackagePath,
                VersionId = Package.VersionId,
                RootGuid = LocalPackageGuid,
                RootPath = LocalPackagePath,
                ProjectPath = LocalProjectPath
            };

            var uploader = new UnityPackageUploader(uploaderSettings);
            return uploader;
        }

        protected override void Serialize()
        {
            _stateData.SetMainPath(_mainExportPath);
            _stateData.SetIncludeDependencies(_includeDependencies);
            _stateData.SetDependencies(_dependencies.Select(x => x.name));
            _stateData.SetSpecialFolders(_specialFolders);
            OnChanged?.Invoke();
        }

        protected override void Deserialize()
        {
            _mainExportPath = _stateData.GetMainPath();

            _specialFolders = new List<string>();
            foreach (var path in _stateData.GetSpecialFolders())
            {
                _specialFolders.Add(path);
            }

            _includeDependencies = _stateData.GetIncludeDependencies();

            _dependencies = new List<PackageInfo>();
            foreach (var dependency in _stateData.GetDependencies())
            {
                if (!PackageUtility.GetPackageByPackageName(dependency, out var package))
                    continue;

                _dependencies.Add(package);
            }

            DeserializeFromUploadedData();
        }

        private void DeserializeFromUploadedData()
        {
            DeserializeFromUploadedDataByGuid();
            DeserializeFromUploadedDataByPath();
        }

        private void DeserializeFromUploadedDataByGuid()
        {
            if (!string.IsNullOrEmpty(_mainExportPath))
                return;

            var lastUploadedGuid = Package.RootGuid;
            if (string.IsNullOrEmpty(lastUploadedGuid))
                return;

            var potentialPackagePath = AssetDatabase.GUIDToAssetPath(lastUploadedGuid);
            DeserializeFromUploadedDataByPath(potentialPackagePath);
        }

        private void DeserializeFromUploadedDataByPath()
        {
            if (!string.IsNullOrEmpty(_mainExportPath))
                return;

            var lastUploadedPath = Package.ProjectPath;
            if (string.IsNullOrEmpty(lastUploadedPath))
                return;

            DeserializeFromUploadedDataByPath(lastUploadedPath);
        }

        private void DeserializeFromUploadedDataByPath(string path)
        {
            if (string.IsNullOrEmpty(path) || !IsPathValid(path, out var _))
                return;

            _mainExportPath = path;
        }
    }
}