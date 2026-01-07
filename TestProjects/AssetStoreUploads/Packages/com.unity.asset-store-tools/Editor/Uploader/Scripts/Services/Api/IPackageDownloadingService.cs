using AssetStoreTools.Api.Responses;
using AssetStoreTools.Uploader.Data;
using System.Threading.Tasks;

namespace AssetStoreTools.Uploader.Services.Api
{
    internal interface IPackageDownloadingService : IUploaderService
    {
        Task<PackagesDataResponse> GetPackageData();
        Task<RefreshedPackageDataResponse> UpdatePackageData(IPackage package);
        void ClearPackageData();
        Task<PackageThumbnailResponse> GetPackageThumbnail(IPackage package);
        Task<PackageUploadedUnityVersionDataResponse> GetPackageUploadedVersions(IPackage package, int timeoutMs);
        void StopDownloading();
    }
}