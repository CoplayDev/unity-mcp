using AssetStoreTools.Api;
using AssetStoreTools.Exporter;
using AssetStoreTools.Uploader.Data.Serialization;
using AssetStoreTools.Validator;
using AssetStoreTools.Validator.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace AssetStoreTools.Uploader.Data
{
    internal class UnityPackageWorkflow : WorkflowBase
    {
        public override string Name => "UnityPackageWorkflow";
        public override string DisplayName => "Pre-exported .unitypackage";
        public override string PackageExtension => ".unitypackage";
        public override bool IsPathSet => !string.IsNullOrEmpty(_packagePath);

        private UnityPackageWorkflowState _workflowState;
        private string _packagePath;

        public override event Action OnChanged;

        public UnityPackageWorkflow(IPackage package, UnityPackageWorkflowState workflowState, IWorkflowServices services)
            : base(package, services)
        {
            _workflowState = workflowState;
            Deserialize();
        }

        public void SetPackagePath(string path, bool serialize)
        {
            _packagePath = path;
            SetMetadata();
            if (serialize)
                Serialize();
        }

        private void SetMetadata()
        {
            LocalPackageGuid = string.Empty;
            LocalPackagePath = string.Empty;
            LocalProjectPath = _packagePath;
        }

        public string GetPackagePath()
        {
            return _packagePath;
        }

        public override IEnumerable<string> GetAllPaths()
        {
            return new List<string>() { _packagePath };
        }

        public override bool IsPathValid(string path, out string error)
        {
            error = null;

            var pathIsUnityPackage = path.EndsWith(PackageExtension);
            var pathExists = File.Exists(path);

            if (!pathIsUnityPackage || !pathExists)
            {
                error = "Path must point to a .unitypackage file";
                return false;
            }

            return true;
        }

        public override IValidator CreateValidator()
        {
            var validationSettings = new ExternalProjectValidationSettings()
            {
                Category = Package.Category,
                PackagePath = GetPackagePath()
            };

            var validator = new ExternalProjectValidator(validationSettings);
            return validator;
        }

        public override IPackageExporter CreateExporter(string _)
        {
            // This workflow already takes exported packages as input
            throw new InvalidOperationException($"{nameof(UnityPackageWorkflow)} already takes exported packages as input");
        }

        public override Task<PackageExporterResult> ExportPackage(string _)
        {
            return Task.FromResult(new PackageExporterResult() { Success = true, ExportedPath = GetPackagePath() });
        }

        protected override IPackageUploader CreatePackageUploader(string exportedPackagePath)
        {
            var uploaderSettings = new UnityPackageUploadSettings()
            {
                VersionId = Package.VersionId,
                UnityPackagePath = exportedPackagePath,
                RootGuid = LocalPackageGuid,
                RootPath = LocalPackagePath,
                ProjectPath = LocalProjectPath
            };

            var uploader = new UnityPackageUploader(uploaderSettings);
            return uploader;
        }

        protected override void Serialize()
        {
            _workflowState.SetPackagePath(_packagePath);
            OnChanged?.Invoke();
        }

        protected override void Deserialize()
        {
            _packagePath = _workflowState.GetPackagePath();
            DeserializeFromUploadedData();
        }

        private void DeserializeFromUploadedData()
        {
            if (!string.IsNullOrEmpty(_packagePath))
                return;

            var potentialPackagePath = Package.ProjectPath;
            if (string.IsNullOrEmpty(potentialPackagePath) || !IsPathValid(potentialPackagePath, out var _))
                return;

            _packagePath = potentialPackagePath;
        }
    }
}