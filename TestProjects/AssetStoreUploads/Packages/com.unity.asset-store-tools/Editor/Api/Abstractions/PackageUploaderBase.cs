using AssetStoreTools.Api.Responses;
using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AssetStoreTools.Api
{
    internal abstract class PackageUploaderBase : IPackageUploader
    {
        protected const int UploadChunkSizeBytes = 32768;
        protected const int UploadResponseTimeoutMs = 10000;

        protected abstract void ValidateSettings();
        public abstract Task<PackageUploadResponse> Upload(IAssetStoreClient client, IProgress<float> progress = null, CancellationToken cancellationToken = default);

        protected void EnsureSuccessResponse(HttpResponseMessage response)
        {
            try
            {
                response.EnsureSuccessStatusCode();
            }
            catch
            {
                throw new Exception(response.Content.ReadAsStringAsync().Result);
            }
        }

        protected void WaitForUploadCompletion(Task<HttpResponseMessage> response, FileStream requestFileStream, IProgress<float> progress, CancellationToken cancellationToken)
        {
            // Progress tracking
            int updateIntervalMs = 100;
            bool allBytesSent = false;
            DateTime timeOfCompletion = default;

            while (!response.IsCompleted)
            {
                float uploadProgress = (float)requestFileStream.Position / requestFileStream.Length * 100;
                progress?.Report(uploadProgress);
                Thread.Sleep(updateIntervalMs);

                // A timeout for rare cases, when package uploading reaches 100%, but Put task IsComplete value remains 'False'
                if (requestFileStream.Position == requestFileStream.Length)
                {
                    if (!allBytesSent)
                    {
                        allBytesSent = true;
                        timeOfCompletion = DateTime.UtcNow;
                    }
                    else if (DateTime.UtcNow.Subtract(timeOfCompletion).TotalMilliseconds > UploadResponseTimeoutMs)
                    {
                        throw new TimeoutException();
                    }
                }
            }
        }
    }
}
