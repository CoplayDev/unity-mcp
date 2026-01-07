using AssetStoreTools.Api.Responses;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AssetStoreTools.Api
{
    internal interface IPackageUploader
    {
        Task<PackageUploadResponse> Upload(IAssetStoreClient client, IProgress<float> progress, CancellationToken cancellationToken = default);
    }
}