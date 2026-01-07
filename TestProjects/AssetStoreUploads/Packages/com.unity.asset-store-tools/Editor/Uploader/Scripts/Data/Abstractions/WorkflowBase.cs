using AssetStoreTools.Api;
using AssetStoreTools.Api.Responses;
using AssetStoreTools.Exporter;
using AssetStoreTools.Previews;
using AssetStoreTools.Previews.Data;
using AssetStoreTools.Previews.Generators;
using AssetStoreTools.Uploader.Services.Analytics.Data;
using AssetStoreTools.Utility;
using AssetStoreTools.Validator;
using AssetStoreTools.Validator.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace AssetStoreTools.Uploader.Data
{
    internal abstract class WorkflowBase : IWorkflow
    {
        protected IPackage Package;

        public abstract string Name { get; }
        public abstract string DisplayName { get; }
        public string PackageName => Package.Name;
        public abstract string PackageExtension { get; }
        public abstract bool IsPathSet { get; }

        protected string LocalPackageGuid;
        protected string LocalPackagePath;
        protected string LocalProjectPath;

        public bool GenerateHighQualityPreviews { get; set; }
        public ValidationSettings LastValidationSettings { get; private set; }
        public ValidationResult LastValidationResult { get; private set; }

        private IWorkflowServices _services;
        private IPackageUploader _activeUploader;

        public abstract event Action OnChanged;
        public event Action<UploadStatus?, float?> OnUploadStateChanged;

        public WorkflowBase(IPackage package, IWorkflowServices services)
        {
            Package = package;
            _services = services;
        }

        public abstract IEnumerable<string> GetAllPaths();

        public abstract IValidator CreateValidator();

        public ValidationResult Validate()
        {
            var validator = CreateValidator();
            var result = CreateValidator().Validate();

            LastValidationSettings = validator.Settings;
            LastValidationResult = result;

            return result;
        }

        protected IPreviewGenerator CreatePreviewGenerator(List<string> inputPaths)
        {
            PreviewGenerationSettings settings;
            IPreviewGenerator generator;

            // Filter out ProjectSettings
            inputPaths = inputPaths.Where(x => x == "Assets" || x.StartsWith("Assets/") || x.StartsWith("Packages/")).ToList();

            if (!GenerateHighQualityPreviews)
            {
                settings = new NativePreviewGenerationSettings()
                {
                    InputPaths = inputPaths.ToArray(),
                    OverwriteExisting = false,
                    OutputPath = Constants.Previews.Native.DefaultOutputPath,
                    Format = Constants.Previews.Native.DefaultFormat,
                    PreviewFileNamingFormat = Constants.Previews.DefaultFileNameFormat,
                    WaitForPreviews = Constants.Previews.Native.DefaultWaitForPreviews,
                    ChunkedPreviewLoading = Constants.Previews.Native.DefaultChunkedPreviewLoading,
                    ChunkSize = Constants.Previews.Native.DefaultChunkSize
                };

                generator = new NativePreviewGenerator((NativePreviewGenerationSettings)settings);
            }
            else
            {
                settings = new CustomPreviewGenerationSettings()
                {
                    InputPaths = inputPaths.ToArray(),
                    OverwriteExisting = false,
                    Width = Constants.Previews.Custom.DefaultWidth,
                    Height = Constants.Previews.Custom.DefaultHeight,
                    Depth = Constants.Previews.Custom.DefaultDepth,
                    NativeWidth = Constants.Previews.Custom.DefaultNativeWidth,
                    NativeHeight = Constants.Previews.Custom.DefaultNativeHeight,
                    OutputPath = Constants.Previews.Custom.DefaultOutputPath,
                    Format = Constants.Previews.Custom.DefaultFormat,
                    PreviewFileNamingFormat = Constants.Previews.DefaultFileNameFormat,
                    AudioSampleColor = Constants.Previews.Custom.DefaultAudioSampleColor,
                    AudioBackgroundColor = Constants.Previews.Custom.DefaultAudioBackgroundColor,
                };

                generator = new CustomPreviewGenerator((CustomPreviewGenerationSettings)settings);
            }

            return generator;
        }

        public abstract IPackageExporter CreateExporter(string outputPath);

        public virtual async Task<PackageExporterResult> ExportPackage(string outputPath)
        {
            var exporter = CreateExporter(outputPath);
            var result = await exporter.Export();
            return result;
        }

        public async Task<bool> ValidatePackageUploadedVersions()
        {
            var unityVersionSupported = string.Compare(Application.unityVersion, Constants.Uploader.MinRequiredUnitySupportVersion, StringComparison.Ordinal) >= 0;
            if (unityVersionSupported)
                return true;

            var response = await _services.GetPackageUploadedVersions(Package, 5000);
            if (response.Cancelled || response.Success == false)
                return true;

            return response.UnityVersions.Any(x => string.Compare(x, Constants.Uploader.MinRequiredUnitySupportVersion, StringComparison.Ordinal) >= 0);
        }

        private bool ValidatePackageBeforeUpload(string packagePath, out string error)
        {
            error = string.Empty;

            if (!File.Exists(packagePath))
            {
                error = $"File '{packagePath}' was not found.";
                return false;
            }

            if (!ValidatePackageSize(packagePath, out error))
            {
                return false;
            }

            return true;
        }

        private bool ValidatePackageSize(string packagePath, out string error)
        {
            error = string.Empty;

            long packageSize = new FileInfo(packagePath).Length;
            long packageSizeLimit = Constants.Uploader.MaxPackageSizeBytes;

            if (packageSize <= packageSizeLimit)
                return true;

            float packageSizeInGB = packageSize / (float)1073741824; // (1024 * 1024 * 1024)
            float maxPackageSizeInGB = packageSizeLimit / (float)1073741824;
            error = $"The size of your package ({packageSizeInGB:0.0} GB) exceeds the maximum allowed package size of {maxPackageSizeInGB:0} GB. " +
                $"Please reduce the size of your package.";

            return false;
        }

        public async Task<PackageUploadResponse> UploadPackage(string packagePath)
        {
            if (!ValidatePackageBeforeUpload(packagePath, out var error))
            {
                return new PackageUploadResponse() { Success = false, Status = UploadStatus.Fail, Exception = new Exception(error) };
            }

            _activeUploader = CreatePackageUploader(packagePath);
            var progress = new Progress<float>();

            var time = System.Diagnostics.Stopwatch.StartNew();

            progress.ProgressChanged += ReportUploadProgress;
            var response = await _services.UploadPackage(_activeUploader, progress);
            progress.ProgressChanged -= ReportUploadProgress;

            // Send analytics
            time.Stop();
            if (!response.Cancelled)
                SendAnalytics(packagePath, response.Status, time.Elapsed.TotalSeconds);

            OnUploadStateChanged?.Invoke(response.Status, null);
            _activeUploader = null;
            return response;
        }

        protected abstract IPackageUploader CreatePackageUploader(string exportedPackagePath);

        private void ReportUploadProgress(object _, float value)
        {
            OnUploadStateChanged?.Invoke(null, value);
        }

        private void SendAnalytics(string packagePath, UploadStatus uploadStatus, double timeTakenSeconds)
        {
            try
            {
                var analytic = new PackageUploadAnalytic(
                    packageId: Package.PackageId,
                    category: Package.Category,
                    usedValidator: LastValidationResult != null,
                    validationSettings: LastValidationSettings,
                    validationResult: LastValidationResult,
                    uploadFinishedReason: uploadStatus,
                    timeTaken: timeTakenSeconds,
                    packageSize: new FileInfo(packagePath).Length,
                    workflow: Name
                    );

                var result = _services.SendAnalytic(analytic);
            }
            catch (Exception e) { ASDebug.LogError($"Could not send analytics: {e}"); }
        }

        public void AbortUpload()
        {
            if (_activeUploader != null)
                _services.StopUploading(_activeUploader);

            _activeUploader = null;
        }

        public void ResetUploadStatus()
        {
            OnUploadStateChanged?.Invoke(UploadStatus.Default, 0f);
        }

        public async Task RefreshPackage()
        {
            var response = await _services.UpdatePackageData(Package);
            if (!response.Success)
                return;

            Package.UpdateData(response.Package);
        }

        public abstract bool IsPathValid(string path, out string reason);

        protected abstract void Serialize();

        protected abstract void Deserialize();
    }
}