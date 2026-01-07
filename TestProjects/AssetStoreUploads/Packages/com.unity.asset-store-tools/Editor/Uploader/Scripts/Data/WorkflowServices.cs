using AssetStoreTools.Api;
using AssetStoreTools.Api.Responses;
using AssetStoreTools.Uploader.Services.Analytics;
using AssetStoreTools.Uploader.Services.Analytics.Data;
using AssetStoreTools.Uploader.Services.Api;
using System;
using System.Threading.Tasks;
using UnityEngine.Analytics;

namespace AssetStoreTools.Uploader.Data
{
    internal class WorkflowServices : IWorkflowServices
    {
        private IPackageDownloadingService _downloadingService;
        private IPackageUploadingService _uploadingService;
        private IAnalyticsService _analyticsService;

        public WorkflowServices(
            IPackageDownloadingService downloadingService,
            IPackageUploadingService uploadingService,
            IAnalyticsService analyticsService)
        {
            _downloadingService = downloadingService;
            _uploadingService = uploadingService;
            _analyticsService = analyticsService;
        }

        public Task<PackageUploadedUnityVersionDataResponse> GetPackageUploadedVersions(IPackage package, int timeoutMs)
        {
            return _downloadingService.GetPackageUploadedVersions(package, timeoutMs);
        }

        public Task<PackageUploadResponse> UploadPackage(IPackageUploader uploader, IProgress<float> progress)
        {
            return _uploadingService.UploadPackage(uploader, progress);
        }

        public void StopUploading(IPackageUploader uploader)
        {
            _uploadingService.StopUploading(uploader);
        }

        public Task<RefreshedPackageDataResponse> UpdatePackageData(IPackage package)
        {
            return _downloadingService.UpdatePackageData(package);
        }

        public AnalyticsResult SendAnalytic(IAssetStoreAnalytic data)
        {
            return _analyticsService.Send(data);
        }
    }
}