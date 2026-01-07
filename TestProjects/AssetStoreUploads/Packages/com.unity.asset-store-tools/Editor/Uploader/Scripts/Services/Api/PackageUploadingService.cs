using AssetStoreTools.Api;
using AssetStoreTools.Api.Responses;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;

namespace AssetStoreTools.Uploader.Services.Api
{
    internal class PackageUploadingService : IPackageUploadingService
    {
        private class UploadInProgress
        {
            public IPackageUploader Uploader;
            public IProgress<float> Progress = new Progress<float>();
            public CancellationTokenSource CancellationTokenSource = new CancellationTokenSource();

            public UploadInProgress(IPackageUploader uploader, IProgress<float> progress)
            {
                Uploader = uploader;
                Progress = progress;
                CancellationTokenSource = new CancellationTokenSource();
            }
        }

        private IAssetStoreApi _api;
        private List<UploadInProgress> _uploadsInProgress;

        public bool IsUploading => _uploadsInProgress.Count > 0;

        public PackageUploadingService(IAssetStoreApi api)
        {
            _api = api;
            _uploadsInProgress = new List<UploadInProgress>();
        }

        public async Task<PackageUploadResponse> UploadPackage(IPackageUploader uploader, IProgress<float> progress)
        {
            using (var cancellationTokenSource = new CancellationTokenSource())
            {
                var uploadInProgress = StartTrackingUpload(uploader, progress);
                var response = await _api.UploadPackage(uploadInProgress.Uploader, uploadInProgress.Progress, uploadInProgress.CancellationTokenSource.Token);
                StopTrackingUpload(uploadInProgress);

                return response;
            }
        }

        private UploadInProgress StartTrackingUpload(IPackageUploader uploader, IProgress<float> progress)
        {
            // If this is the first upload - lock reload assemblies and prevent entering play mode
            if (_uploadsInProgress.Count == 0)
            {
                EditorApplication.LockReloadAssemblies();
                EditorApplication.playModeStateChanged += PreventEnteringPlayMode;
            }

            var uploadInProgress = new UploadInProgress(uploader, progress);
            _uploadsInProgress.Add(uploadInProgress);

            return uploadInProgress;
        }

        private void StopTrackingUpload(UploadInProgress uploadInProgress)
        {
            _uploadsInProgress.Remove(uploadInProgress);

            // If this was the last upload - unlock reload assemblies and allow entering play mode
            if (_uploadsInProgress.Count > 0)
                return;

            EditorApplication.UnlockReloadAssemblies();
            EditorApplication.playModeStateChanged -= PreventEnteringPlayMode;
        }

        private void PreventEnteringPlayMode(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.ExitingEditMode)
                return;

            EditorApplication.ExitPlaymode();
            EditorUtility.DisplayDialog("Notice", "Entering Play Mode is not allowed while there's a package upload in progress.\n\n" +
                                                  "Please wait until the upload is finished or cancel the upload from the Asset Store Uploader window", "OK");
        }

        public void StopUploading(IPackageUploader uploader)
        {
            var uploadInProgress = _uploadsInProgress.FirstOrDefault(x => x.Uploader == uploader);
            if (uploadInProgress == null)
                return;

            uploadInProgress.CancellationTokenSource.Cancel();
        }

        public void StopAllUploadinng()
        {
            foreach (var uploadInProgress in _uploadsInProgress)
                uploadInProgress.CancellationTokenSource.Cancel();
        }
    }
}