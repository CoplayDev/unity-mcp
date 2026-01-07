using AssetStoreTools.Api;
using AssetStoreTools.Exporter;
using AssetStoreTools.Uploader.Data.Serialization;
using AssetStoreTools.Utility;
using AssetStoreTools.Validator;
using AssetStoreTools.Validator.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using PackageManager = UnityEditor.PackageManager;

namespace AssetStoreTools.Uploader.Data
{
    internal class HybridPackageWorkflow : WorkflowBase
    {
        public override string Name => "HybridPackageWorkflow";
        public override string DisplayName => "Local UPM Package";
        public override string PackageExtension => ".unitypackage";
        public override bool IsPathSet => _packageInfo != null;

        private HybridPackageWorkflowState _stateData;

        private PackageInfo _packageInfo;
        private List<PackageInfo> _dependencies;

        public override event Action OnChanged;

        public HybridPackageWorkflow(IPackage package, HybridPackageWorkflowState stateData, IWorkflowServices services)
            : base(package, services)
        {
            _stateData = stateData;
            Deserialize();
        }

        public PackageInfo GetPackage()
        {
            return _packageInfo;
        }

        public void SetPackage(PackageInfo packageInfo, bool serialize)
        {
            if (packageInfo == null)
                throw new ArgumentException("Package is null");

            _packageInfo = packageInfo;
            SetMetadata();
            if (serialize)
                Serialize();
        }

        public void SetPackage(string packageManifestPath, bool serialize)
        {
            if (!PackageUtility.GetPackageByManifestPath(packageManifestPath, out var package))
                throw new ArgumentException("Path does not correspond to a valid package");

            SetPackage(package, serialize);
        }

        private void SetMetadata()
        {
            LocalPackageGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(_packageInfo.GetManifestAsset()));
            LocalPackagePath = _packageInfo.assetPath;
            LocalProjectPath = _packageInfo.name;
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

        public List<PackageInfo> GetAvailableDependencies()
        {
            var availableDependencies = new List<PackageInfo>();
            if (_packageInfo == null)
                return availableDependencies;

            var packageDependencies = _packageInfo.dependencies.Select(x => x.name);
            foreach (var dependency in packageDependencies)
            {
                if (!PackageUtility.GetPackageByPackageName(dependency, out var package))
                    continue;

                if (package.source != PackageManager.PackageSource.Local
                    && package.source != PackageManager.PackageSource.Embedded)
                    continue;

                availableDependencies.Add(package);
            }

            return availableDependencies;
        }

        public override IEnumerable<string> GetAllPaths()
        {
            var paths = new List<string>();

            if (_packageInfo == null)
                return paths;

            paths.Add(_packageInfo.assetPath);
            paths.AddRange(_dependencies.Select(x => x.assetPath));

            return paths;
        }

        public override bool IsPathValid(string path, out string reason)
        {
            reason = string.Empty;

            if (!PackageUtility.GetPackageByManifestPath(path, out var package))
            {
                reason = "Selected path must point to a package manifest for a package that is imported into the current project";
                return false;
            }

            var packageSourceValid = package.source == PackageSource.Embedded || package.source == PackageSource.Local;
            if (!packageSourceValid)
            {
                reason = "Selected package must be a local or an embedded package";
                return false;
            }

            return true;
        }

        public override IValidator CreateValidator()
        {
            var validationPaths = GetAllPaths();

            var validationSettings = new CurrentProjectValidationSettings()
            {
                Category = Package?.Category,
                ValidationPaths = validationPaths.ToList(),
                ValidationType = ValidationType.UnityPackage
            };

            var validator = new CurrentProjectValidator(validationSettings);
            return validator;
        }

        public override IPackageExporter CreateExporter(string outputPath)
        {
            var exportPaths = GetAllPaths();

            var exportSettings = new DefaultExporterSettings()
            {
                ExportPaths = exportPaths.ToArray(),
                OutputFilename = outputPath,
                PreviewGenerator = CreatePreviewGenerator(exportPaths.ToList())
            };

            return new DefaultPackageExporter(exportSettings);
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
            if (_packageInfo == null)
                return;

            _stateData.SetPackageName(_packageInfo.name);
            _stateData.SetPackageDependencies(_dependencies.Select(x => x.name).OrderBy(x => x));
            OnChanged?.Invoke();
        }

        protected override void Deserialize()
        {
            var packageName = _stateData.GetPackageName();
            if (PackageUtility.GetPackageByPackageName(packageName, out var package))
                _packageInfo = package;

            _dependencies = new List<PackageInfo>();
            var dependencies = _stateData.GetPackageDependencies();
            foreach (var dependency in dependencies)
            {
                if (PackageUtility.GetPackageByPackageName(dependency, out var packageDependency))
                    _dependencies.Add(packageDependency);
            }

            DeserializeFromUploadedData();
        }

        private void DeserializeFromUploadedData()
        {
            DeserializeFromUploadedDataByPackageName();
            DeserializeFromUploadedDataByPackageGuid();
        }

        private void DeserializeFromUploadedDataByPackageName()
        {
            if (_packageInfo != null)
                return;

            var lastUploadedPackageName = Package.ProjectPath;
            if (!PackageUtility.GetPackageByPackageName(lastUploadedPackageName, out var package))
                return;

            _packageInfo = package;
        }

        private void DeserializeFromUploadedDataByPackageGuid()
        {
            if (_packageInfo != null)
                return;

            var lastUploadedGuid = Package.RootGuid;
            if (string.IsNullOrEmpty(lastUploadedGuid))
                return;

            var potentialPackageManifestPath = AssetDatabase.GUIDToAssetPath(lastUploadedGuid);
            if (string.IsNullOrEmpty(potentialPackageManifestPath) || !IsPathValid(potentialPackageManifestPath, out var _))
                return;

            if (!PackageUtility.GetPackageByManifestPath(potentialPackageManifestPath, out var package))
                return;

            _packageInfo = package;
        }
    }
}