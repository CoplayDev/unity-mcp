using AssetStoreTools.Api.Models;
using AssetStoreTools.Uploader.Data.Serialization;
using AssetStoreTools.Utility;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AssetStoreTools.Uploader.Services
{
    internal class CachingService : ICachingService
    {
        private VisualElement _cachedUploaderWindow;

        public bool GetCachedUploaderWindow(out VisualElement uploaderWindow)
        {
            uploaderWindow = _cachedUploaderWindow;
            return uploaderWindow != null;
        }

        public void CacheUploaderWindow(VisualElement uploaderWindow)
        {
            _cachedUploaderWindow = uploaderWindow;
        }

        public void CacheSessionToken(string sessionToken)
        {
            if (string.IsNullOrEmpty(sessionToken))
                throw new ArgumentException("Session token cannot be null");

            EditorPrefs.SetString(Constants.Cache.SessionTokenKey, sessionToken);
        }

        public bool GetCachedSessionToken(out string sessionToken)
        {
            sessionToken = EditorPrefs.GetString(Constants.Cache.SessionTokenKey, string.Empty);
            return !string.IsNullOrEmpty(sessionToken);
        }

        public void ClearCachedSessionToken()
        {
            EditorPrefs.DeleteKey(Constants.Cache.SessionTokenKey);
        }

        public bool GetCachedPackageMetadata(out List<Package> data)
        {
            data = new List<Package>();
            if (!CacheUtil.GetFileFromTempCache(Constants.Cache.PackageDataFileName, out var filePath))
                return false;

            try
            {
                var serializerSettings = new JsonSerializerSettings()
                {
                    ContractResolver = Package.CachedPackageResolver.Instance
                };

                data = JsonConvert.DeserializeObject<List<Package>>(File.ReadAllText(filePath, Encoding.UTF8), serializerSettings);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void CachePackageMetadata(List<Package> data)
        {
            if (data == null)
                throw new ArgumentException("Package data cannot be null");

            var serializerSettings = new JsonSerializerSettings()
            {
                ContractResolver = Package.CachedPackageResolver.Instance,
                Formatting = Formatting.Indented
            };

            CacheUtil.CreateFileInTempCache(Constants.Cache.PackageDataFileName, JsonConvert.SerializeObject(data, serializerSettings), true);
        }

        public void DeletePackageMetadata()
        {
            CacheUtil.DeleteFileFromTempCache(Constants.Cache.PackageDataFileName);
        }

        public void UpdatePackageMetadata(Package data)
        {
            if (!GetCachedPackageMetadata(out var cachedData))
                return;

            var index = cachedData.FindIndex(x => x.PackageId.Equals(data.PackageId));
            if (index == -1)
            {
                cachedData.Add(data);
            }
            else
            {
                cachedData.RemoveAt(index);
                cachedData.Insert(index, data);
            }

            CachePackageMetadata(cachedData);
        }

        public bool GetCachedPackageThumbnail(string packageId, out Texture2D texture)
        {
            texture = null;
            if (!CacheUtil.GetFileFromTempCache(Constants.Cache.PackageThumbnailFileName(packageId), out var filePath))
                return false;

            texture = new Texture2D(1, 1);
            texture.LoadImage(File.ReadAllBytes(filePath));
            return true;
        }

        public void CachePackageThumbnail(string packageId, Texture2D texture)
        {
            CacheUtil.CreateFileInTempCache(Constants.Cache.PackageThumbnailFileName(packageId), texture.EncodeToPNG(), true);
        }

        public bool GetCachedWorkflowStateData(string packageId, out WorkflowStateData data)
        {
            data = null;

            if (string.IsNullOrEmpty(packageId))
                return false;

            if (!CacheUtil.GetFileFromPersistentCache(Constants.Cache.WorkflowStateDataFileName(packageId), out var filePath))
                return false;

            try
            {
                data = JsonConvert.DeserializeObject<WorkflowStateData>(File.ReadAllText(filePath, Encoding.UTF8));
                if (string.IsNullOrEmpty(data.GetPackageId()))
                    return false;
            }
            catch
            {
                return false;
            }

            return true;
        }

        public void CacheWorkflowStateData(WorkflowStateData data)
        {
            if (data == null)
                throw new ArgumentException("Workflow state data cannot be null");

            CacheUtil.CreateFileInPersistentCache(Constants.Cache.WorkflowStateDataFileName(data.GetPackageId()), JsonConvert.SerializeObject(data, Formatting.Indented), true);
        }
    }
}
