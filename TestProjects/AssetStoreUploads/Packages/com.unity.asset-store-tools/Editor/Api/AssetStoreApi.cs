using AssetStoreTools.Api.Models;
using AssetStoreTools.Api.Responses;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AssetStoreTools.Api
{
    internal class AssetStoreApi : IAssetStoreApi
    {
        private IAssetStoreClient _client;

        public AssetStoreApi(IAssetStoreClient client)
        {
            _client = client;
        }

        public async Task<AssetStoreToolsVersionResponse> GetLatestAssetStoreToolsVersion(CancellationToken cancellationToken = default)
        {
            try
            {
                var uri = ApiUtility.CreateUri(Constants.Api.AssetStoreToolsLatestVersionUrl, false);
                var response = await _client.Get(uri, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                response.EnsureSuccessStatusCode();
                var responseStr = response.Content.ReadAsStringAsync().Result;
                return new AssetStoreToolsVersionResponse(responseStr);
            }
            catch (OperationCanceledException e)
            {
                return new AssetStoreToolsVersionResponse() { Success = false, Cancelled = true, Exception = e };
            }
            catch (Exception e)
            {
                return new AssetStoreToolsVersionResponse() { Success = false, Exception = e };
            }
        }

        public async Task<AuthenticationResponse> Authenticate(IAuthenticationType authenticationType, CancellationToken cancellationToken = default)
        {
            try
            {
                var loginResponse = await authenticationType.Authenticate(_client, cancellationToken);
                if (loginResponse.Success)
                {
                    _client.SetSessionId(loginResponse.User.SessionId);
                }

                return loginResponse;
            }
            catch (OperationCanceledException e)
            {
                return new AuthenticationResponse() { Success = false, Cancelled = true, Exception = e };
            }
            catch (Exception e)
            {
                return new AuthenticationResponse() { Success = false, Exception = e };
            }
        }

        public void Deauthenticate()
        {
            _client.ClearSessionId();
        }

        public async Task<PackagesDataResponse> GetPackages(CancellationToken cancellationToken = default)
        {
            try
            {
                var mainDataResponse = await GetPackageDataMain(cancellationToken);
                if (!mainDataResponse.Success)
                    throw mainDataResponse.Exception;
                var additionalDataResponse = await GetPackageDataExtra(cancellationToken);
                if (!additionalDataResponse.Success)
                    throw additionalDataResponse.Exception;
                var categoryDataResponse = await GetCategories(cancellationToken);
                if (!categoryDataResponse.Success)
                    throw categoryDataResponse.Exception;

                var joinedData = ApiUtility.CombinePackageData(mainDataResponse.Packages, additionalDataResponse.Packages, categoryDataResponse.Categories);
                return new PackagesDataResponse() { Success = true, Packages = joinedData };
            }
            catch (OperationCanceledException e)
            {
                return new PackagesDataResponse() { Success = false, Cancelled = true, Exception = e };
            }
            catch (Exception e)
            {
                return new PackagesDataResponse() { Success = false, Exception = e };
            }
        }

        private async Task<PackagesDataResponse> GetPackageDataMain(CancellationToken cancellationToken)
        {
            try
            {
                var uri = ApiUtility.CreateUri(Constants.Api.GetPackagesUrl, true);
                var response = await _client.Get(uri, cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();
                response.EnsureSuccessStatusCode();

                var responseStr = response.Content.ReadAsStringAsync().Result;
                return new PackagesDataResponse(responseStr);
            }
            catch (OperationCanceledException e)
            {
                return new PackagesDataResponse() { Success = false, Cancelled = true, Exception = e };
            }
            catch (Exception e)
            {
                return new PackagesDataResponse() { Success = false, Exception = e };
            }
        }

        private async Task<PackagesAdditionalDataResponse> GetPackageDataExtra(CancellationToken cancellationToken)
        {
            try
            {
                var uri = ApiUtility.CreateUri(Constants.Api.GetPackagesAdditionalDataUrl, true);
                var response = await _client.Get(uri, cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();
                response.EnsureSuccessStatusCode();

                var responseStr = response.Content.ReadAsStringAsync().Result;
                return new PackagesAdditionalDataResponse(responseStr);
            }
            catch (OperationCanceledException e)
            {
                return new PackagesAdditionalDataResponse() { Success = false, Cancelled = true, Exception = e };
            }
            catch (Exception e)
            {
                return new PackagesAdditionalDataResponse() { Success = false, Exception = e };
            }
        }

        public async Task<CategoryDataResponse> GetCategories(CancellationToken cancellationToken)
        {
            try
            {
                var uri = ApiUtility.CreateUri(Constants.Api.GetCategoriesUrl, true);
                var response = await _client.Get(uri, cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();
                response.EnsureSuccessStatusCode();

                var responseStr = response.Content.ReadAsStringAsync().Result;
                return new CategoryDataResponse(responseStr);
            }
            catch (OperationCanceledException e)
            {
                return new CategoryDataResponse() { Success = false, Cancelled = true, Exception = e };
            }
            catch (Exception e)
            {
                return new CategoryDataResponse() { Success = false, Exception = e };
            }
        }

        public async Task<PackageThumbnailResponse> GetPackageThumbnail(Package package, CancellationToken cancellationToken = default)
        {
            try
            {
                if (string.IsNullOrEmpty(package.IconUrl))
                    throw new Exception($"Could not retrieve thumbnail for package {package.PackageId} - icon url is null");

                var response = await _client.Get(new Uri(package.IconUrl), cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();
                response.EnsureSuccessStatusCode();

                var responseBytes = response.Content.ReadAsByteArrayAsync().Result;
                return new PackageThumbnailResponse(responseBytes);
            }
            catch (OperationCanceledException e)
            {
                return new PackageThumbnailResponse() { Success = false, Cancelled = true, Exception = e };
            }
            catch (Exception e)
            {
                return new PackageThumbnailResponse() { Success = false, Exception = e };
            }
        }

        public async Task<RefreshedPackageDataResponse> RefreshPackageMetadata(Package package, CancellationToken cancellationToken = default)
        {
            try
            {
                var refreshedPackage = JObject.FromObject(package).DeepClone().ToObject<Package>();

                var packagesResponse = await GetPackageDataExtra(cancellationToken);
                if (!packagesResponse.Success)
                    throw packagesResponse.Exception;

                // Find the updated package data in the latest data json
                var packageRefreshSource = packagesResponse.Packages.FirstOrDefault(x => x.PackageId == refreshedPackage.PackageId);
                if (packageRefreshSource == null)
                    return new RefreshedPackageDataResponse() { Success = false, Exception = new MissingMemberException($"Unable to find downloaded package data for package id {package.PackageId}") };

                // Retrieve the category map
                var categoryData = await GetCategories(cancellationToken);
                if (!categoryData.Success)
                    return new RefreshedPackageDataResponse() { Success = false, Exception = packagesResponse.Exception };

                // Update the package data
                refreshedPackage.Name = packageRefreshSource.Name;
                refreshedPackage.Status = packageRefreshSource.Status;
                var newCategory = categoryData.Categories.FirstOrDefault(x => x.Id.ToString() == packageRefreshSource.CategoryId);
                refreshedPackage.Category = newCategory != null ? newCategory.Name : "Unknown";
                refreshedPackage.Modified = packageRefreshSource.Modified;
                refreshedPackage.Size = packageRefreshSource.Size;

                return new RefreshedPackageDataResponse() { Success = true, Package = refreshedPackage };
            }
            catch (OperationCanceledException)
            {
                return new RefreshedPackageDataResponse() { Success = false, Cancelled = true };
            }
            catch (Exception e)
            {
                return new RefreshedPackageDataResponse() { Success = false, Exception = e };
            }
        }

        public async Task<PackageUploadedUnityVersionDataResponse> GetPackageUploadedVersions(Package package, CancellationToken cancellationToken = default)
        {
            try
            {
                var uri = ApiUtility.CreateUri(Constants.Api.GetPackageUploadedVersionsUrl(package.PackageId, package.VersionId), true);
                var response = await _client.Get(uri, cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();
                response.EnsureSuccessStatusCode();

                var responseStr = response.Content.ReadAsStringAsync().Result;
                return new PackageUploadedUnityVersionDataResponse(responseStr);
            }
            catch (OperationCanceledException e)
            {
                return new PackageUploadedUnityVersionDataResponse() { Success = false, Cancelled = true, Exception = e };
            }
            catch (Exception e)
            {
                return new PackageUploadedUnityVersionDataResponse() { Success = false, Exception = e };
            }
        }

        public async Task<PackageUploadResponse> UploadPackage(IPackageUploader uploader, IProgress<float> progress = null, CancellationToken cancellationToken = default)
        {
            try
            {
                return await uploader.Upload(_client, progress, cancellationToken);
            }
            catch (OperationCanceledException e)
            {
                return new PackageUploadResponse() { Success = false, Cancelled = true, Exception = e };
            }
            catch (Exception e)
            {
                return new PackageUploadResponse() { Success = false, Exception = e };
            }
        }
    }
}