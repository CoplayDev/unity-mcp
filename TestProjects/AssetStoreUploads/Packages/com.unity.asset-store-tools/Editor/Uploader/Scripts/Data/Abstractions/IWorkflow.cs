using AssetStoreTools.Api;
using AssetStoreTools.Api.Responses;
using AssetStoreTools.Exporter;
using AssetStoreTools.Validator.Data;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AssetStoreTools.Uploader.Data
{
    internal interface IWorkflow
    {
        string Name { get; }
        string DisplayName { get; }
        string PackageName { get; }
        string PackageExtension { get; }
        bool IsPathSet { get; }

        event Action OnChanged;
        event Action<UploadStatus?, float?> OnUploadStateChanged;

        bool GenerateHighQualityPreviews { get; set; }
        ValidationSettings LastValidationSettings { get; }
        ValidationResult LastValidationResult { get; }

        IEnumerable<string> GetAllPaths();
        ValidationResult Validate();
        Task<PackageExporterResult> ExportPackage(string outputPath);
        Task<bool> ValidatePackageUploadedVersions();

        Task<PackageUploadResponse> UploadPackage(string exportedPackagePath);
        void AbortUpload();
        void ResetUploadStatus();
        Task RefreshPackage();
    }
}