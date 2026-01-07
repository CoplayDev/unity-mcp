using AssetStoreTools.Api.Models;
using AssetStoreTools.Uploader.Data.Serialization;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace AssetStoreTools.Uploader.Services
{
    internal interface ICachingService : IUploaderService
    {
        void CacheUploaderWindow(VisualElement uploaderWindow);
        bool GetCachedUploaderWindow(out VisualElement uploaderWindow);
        void CacheSessionToken(string sessionToken);
        bool GetCachedSessionToken(out string sessionToken);
        void ClearCachedSessionToken();
        bool GetCachedPackageMetadata(out List<Package> data);
        void UpdatePackageMetadata(Package data);
        void CachePackageMetadata(List<Package> data);
        void DeletePackageMetadata();
        bool GetCachedPackageThumbnail(string packageId, out Texture2D texture);
        void CachePackageThumbnail(string packageId, Texture2D texture);
        bool GetCachedWorkflowStateData(string packageId, out WorkflowStateData data);
        void CacheWorkflowStateData(WorkflowStateData data);
    }
}
