using AssetStoreTools.Api.Models;
using AssetStoreTools.Api.Responses;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AssetStoreTools.Api
{
    internal interface IAssetStoreApi
    {
        Task<AssetStoreToolsVersionResponse> GetLatestAssetStoreToolsVersion(CancellationToken cancellationToken = default);
        Task<AuthenticationResponse> Authenticate(IAuthenticationType authenticationType, CancellationToken cancellationToken = default);
        void Deauthenticate();
        Task<PackagesDataResponse> GetPackages(CancellationToken cancellationToken = default);
        Task<CategoryDataResponse> GetCategories(CancellationToken cancellationToken = default);
        Task<PackageThumbnailResponse> GetPackageThumbnail(Package package, CancellationToken cancellationToken = default);
        Task<RefreshedPackageDataResponse> RefreshPackageMetadata(Package package, CancellationToken cancellationToken = default);
        Task<PackageUploadedUnityVersionDataResponse> GetPackageUploadedVersions(Package package, CancellationToken cancellationToken = default);
        Task<PackageUploadResponse> UploadPackage(IPackageUploader uploader, IProgress<float> progress = null, CancellationToken cancellationToken = default);
    }
}