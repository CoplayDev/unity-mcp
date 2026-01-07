using AssetStoreTools.Api.Responses;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AssetStoreTools.Api
{
    internal class UnityPackageUploadSettings
    {
        public string VersionId { get; set; }
        public string UnityPackagePath { get; set; }
        public string RootGuid { get; set; }
        public string RootPath { get; set; }
        public string ProjectPath { get; set; }
    }

    internal class UnityPackageUploader : PackageUploaderBase
    {
        private UnityPackageUploadSettings _settings;
        private Uri _uploadUri;

        public UnityPackageUploader(UnityPackageUploadSettings settings)
        {
            _settings = settings;
        }

        protected override void ValidateSettings()
        {
            if (string.IsNullOrEmpty(_settings.VersionId))
                throw new Exception("Version Id is unset");

            if (string.IsNullOrEmpty(_settings.UnityPackagePath)
                || !File.Exists(_settings.UnityPackagePath))
                throw new Exception("Package file could not be found");

            if (!_settings.UnityPackagePath.EndsWith(".unitypackage"))
                throw new Exception("Provided package file is not .unitypackage");
        }

        public override async Task<PackageUploadResponse> Upload(IAssetStoreClient client, IProgress<float> progress = null, CancellationToken cancellationToken = default)
        {
            try
            {
                ValidateSettings();

                var endpoint = Constants.Api.UploadUnityPackageUrl(_settings.VersionId);
                var query = new Dictionary<string, string>()
                {
                    { "root_guid", _settings.RootGuid },
                    { "root_path", _settings.RootPath },
                    { "project_path", _settings.ProjectPath }
                };

                _uploadUri = ApiUtility.CreateUri(endpoint, query, true);
            }
            catch (Exception e)
            {
                return new PackageUploadResponse() { Success = false, Status = UploadStatus.Fail, Exception = e };
            }

            return await Task.Run(() => UploadTask(client, progress, cancellationToken));
        }

        private PackageUploadResponse UploadTask(IAssetStoreClient client, IProgress<float> progress, CancellationToken cancellationToken)
        {
            try
            {
                using (FileStream requestFileStream = new FileStream(_settings.UnityPackagePath, FileMode.Open, FileAccess.Read))
                {
                    var content = new StreamContent(requestFileStream, UploadChunkSizeBytes);

                    var response = client.Put(_uploadUri, content, cancellationToken);
                    WaitForUploadCompletion(response, requestFileStream, progress, cancellationToken);

                    cancellationToken.ThrowIfCancellationRequested();
                    EnsureSuccessResponse(response.Result);

                    var responseStr = response.Result.Content.ReadAsStringAsync().Result;
                    return new PackageUploadResponse(responseStr);
                }
            }
            catch (OperationCanceledException e)
            {
                return new PackageUploadResponse() { Success = false, Cancelled = true, Status = UploadStatus.Cancelled, Exception = e };
            }
            catch (TimeoutException e)
            {
                return new PackageUploadResponse() { Success = true, Status = UploadStatus.ResponseTimeout, Exception = e };
            }
            catch (Exception e)
            {
                return new PackageUploadResponse() { Success = false, Exception = e, Status = UploadStatus.Fail };
            }
        }
    }
}