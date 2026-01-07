using AssetStoreTools.Api;
using AssetStoreTools.Api.Responses;
using AssetStoreTools.Uploader.Data;
using System.Threading;
using System.Threading.Tasks;

namespace AssetStoreTools.Uploader.Services.Api
{
    internal class PackageDownloadingService : IPackageDownloadingService
    {
        public const int MaxConcurrentTumbnailDownloads = 10;

        private IAssetStoreApi _api;
        private ICachingService _cachingService;

        private int _currentDownloads;
        private CancellationTokenSource _cancellationTokenSource;

        public PackageDownloadingService(IAssetStoreApi api, ICachingService cachingService)
        {
            _api = api;
            _cachingService = cachingService;
            _cancellationTokenSource = new CancellationTokenSource();
        }

        public void ClearPackageData()
        {
            _cachingService.DeletePackageMetadata();
        }

        public async Task<PackagesDataResponse> GetPackageData()
        {
            if (!_cachingService.GetCachedPackageMetadata(out var models))
            {
                var cancellationToken = _cancellationTokenSource.Token;
                var packagesResponse = await _api.GetPackages(cancellationToken);

                if (packagesResponse.Cancelled || !packagesResponse.Success)
                    return packagesResponse;

                _cachingService.CachePackageMetadata(packagesResponse.Packages);
                return packagesResponse;
            }

            return new PackagesDataResponse() { Success = true, Packages = models };
        }

        public async Task<RefreshedPackageDataResponse> UpdatePackageData(IPackage package)
        {
            var response = await _api.RefreshPackageMetadata(package.ToModel());

            if (response.Success)
                _cachingService.UpdatePackageMetadata(response.Package);

            return response;
        }

        public async Task<PackageThumbnailResponse> GetPackageThumbnail(IPackage package)
        {
            if (_cachingService.GetCachedPackageThumbnail(package.PackageId, out var cachedTexture))
            {
                return new PackageThumbnailResponse() { Success = true, Thumbnail = cachedTexture };
            }

            var cancellationToken = _cancellationTokenSource.Token;
            while (_currentDownloads >= MaxConcurrentTumbnailDownloads)
                await Task.Delay(100);

            if (cancellationToken.IsCancellationRequested)
                return new PackageThumbnailResponse() { Success = false, Cancelled = true };

            _currentDownloads++;
            var result = await _api.GetPackageThumbnail(package.ToModel(), cancellationToken);
            _currentDownloads--;

            if (result.Success && result.Thumbnail != null)
                _cachingService.CachePackageThumbnail(package.PackageId, result.Thumbnail);

            return result;
        }

        public async Task<PackageUploadedUnityVersionDataResponse> GetPackageUploadedVersions(IPackage package, int timeoutMs)
        {
            var timeoutTokenSource = new CancellationTokenSource();
            try
            {
                var versionsTask = _api.GetPackageUploadedVersions(package.ToModel(), timeoutTokenSource.Token);

                // Wait for versions to be retrieved, or a timeout to occur, whichever is first
                if (await Task.WhenAny(versionsTask, Task.Delay(timeoutMs)) != versionsTask)
                {
                    timeoutTokenSource.Cancel();
                }

                return await versionsTask;
            }
            finally
            {
                timeoutTokenSource.Dispose();
            }
        }

        public void StopDownloading()
        {
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource = new CancellationTokenSource();
        }
    }
}