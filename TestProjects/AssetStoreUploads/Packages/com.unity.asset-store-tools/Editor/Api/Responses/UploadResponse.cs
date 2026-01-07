using System;

namespace AssetStoreTools.Api.Responses
{
    internal class PackageUploadResponse : AssetStoreResponse
    {
        public UploadStatus Status { get; set; }

        public PackageUploadResponse() : base() { }
        public PackageUploadResponse(Exception e) : base(e) { }

        public PackageUploadResponse(string json)
        {
            try
            {
                ValidateAssetStoreResponse(json);
                Status = UploadStatus.Success;
                Success = true;
            }
            catch (Exception e)
            {
                Success = false;
                Status = UploadStatus.Fail;
                Exception = e;
            }
        }
    }
}