using System.IO;
using CacheConstants = AssetStoreTools.Constants.Cache;

namespace AssetStoreTools.Utility
{
    internal static class CacheUtil
    {
        public static bool GetFileFromTempCache(string fileName, out string filePath)
        {
            return GetCacheFile(CacheConstants.TempCachePath, fileName, out filePath);
        }

        public static bool GetFileFromPersistentCache(string fileName, out string filePath)
        {
            return GetCacheFile(CacheConstants.PersistentCachePath, fileName, out filePath);
        }

        public static bool GetFileFromProjectPersistentCache(string projectPath, string fileName, out string filePath)
        {
            return GetCacheFile(Path.Combine(projectPath, CacheConstants.PersistentCachePath), fileName, out filePath);
        }

        private static bool GetCacheFile(string rootPath, string fileName, out string filePath)
        {
            filePath = Path.Combine(rootPath, fileName);
            return File.Exists(filePath);
        }

        public static void CreateFileInTempCache(string fileName, object content, bool overwrite)
        {
            CreateCacheFile(CacheConstants.TempCachePath, fileName, content, overwrite);
        }

        public static void CreateFileInPersistentCache(string fileName, object content, bool overwrite)
        {
            CreateCacheFile(CacheConstants.PersistentCachePath, fileName, content, overwrite);
        }

        private static void CreateCacheFile(string rootPath, string fileName, object content, bool overwrite)
        {
            if (!Directory.Exists(rootPath))
                Directory.CreateDirectory(rootPath);

            var fullPath = Path.Combine(rootPath, fileName);

            bool willUpdate = false;
            if (File.Exists(fullPath))
            {
                if (overwrite)
                {
                    File.Delete(fullPath);
                    willUpdate = true;
                }
                else
                    return;
            }

            switch (content)
            {
                case byte[] bytes:
                    File.WriteAllBytes(fullPath, bytes);
                    break;
                default:
                    File.WriteAllText(fullPath, content.ToString());
                    break;
            }

            var keyword = willUpdate ? "Updating" : "Creating";
            ASDebug.Log($"{keyword} cache file: '{fullPath}'");
        }

        public static void DeleteFileFromTempCache(string fileName)
        {
            DeleteFileFromCache(CacheConstants.TempCachePath, fileName);
        }

        public static void DeleteFileFromPersistentCache(string fileName)
        {
            DeleteFileFromCache(CacheConstants.PersistentCachePath, fileName);
        }

        private static void DeleteFileFromCache(string rootPath, string fileName)
        {
            var path = Path.Combine(rootPath, fileName);
            if (File.Exists(path))
                File.Delete(path);
        }

        //private static void CreateFileInPersistentCache(string fileName, object content, bool overwrite)
        //{
        //    CreateCacheFile(CacheConstants.PersistentCachePath, fileName, content, overwrite);
        //}

        //private static void CreateCacheFile(string rootPath, string fileName, object content, bool overwrite)
        //{
        //    if (!Directory.Exists(rootPath))
        //        Directory.CreateDirectory(rootPath);

        //    var fullPath = Path.Combine(rootPath, fileName);

        //    if (File.Exists(fullPath))
        //    {
        //        if (overwrite)
        //            File.Delete(fullPath);
        //        else
        //            return;
        //    }

        //    switch (content)
        //    {
        //        case byte[] bytes:
        //            File.WriteAllBytes(fullPath, bytes);
        //            break;
        //        default:
        //            File.WriteAllText(fullPath, content.ToString());
        //            break;
        //    }
        //    ASDebug.Log($"Creating cached file: '{fullPath}'");
        //}

        //public static void ClearTempCache()
        //{
        //    if (!File.Exists(Path.Combine(CacheConstants.TempCachePath, CacheConstants.PackageDataFile)))
        //        return;


        //    // Cache consists of package data and package texture thumbnails. We don't clear
        //    // texture thumbnails here since they are less likely to change. They are still
        //    // deleted and redownloaded every project restart (because of being stored in the 'Temp' folder)
        //    var fullPath = Path.Combine(CacheConstants.TempCachePath, CacheConstants.PackageDataFile);
        //    ASDebug.Log($"Deleting cached file '{fullPath}'");
        //    File.Delete(fullPath);
        //}

        //public static void CachePackageMetadata(List<Package> data)
        //{
        //    var serializerSettings = new JsonSerializerSettings()
        //    {
        //        ContractResolver = Package.CachedPackageResolver.Instance,
        //        Formatting = Formatting.Indented
        //    };

        //    CreateFileInTempCache(CacheConstants.PackageDataFile, JsonConvert.SerializeObject(data, serializerSettings), true);
        //}

        //public static void UpdatePackageMetadata(Package data)
        //{
        //    if (!GetCachedPackageMetadata(out var cachedData))
        //        return;

        //    var index = cachedData.FindIndex(x => x.PackageId.Equals(data.PackageId));
        //    if (index == -1)
        //    {
        //        cachedData.Add(data);
        //    }
        //    else
        //    {
        //        cachedData.RemoveAt(index);
        //        cachedData.Insert(index, data);
        //    }

        //    CachePackageMetadata(cachedData);
        //}

        //public static bool GetCachedPackageMetadata(out List<Package> data)
        //{
        //    data = new List<Package>();
        //    var path = Path.Combine(CacheConstants.TempCachePath, CacheConstants.PackageDataFile);
        //    if (!File.Exists(path))
        //        return false;

        //    try
        //    {
        //        var serializerSettings = new JsonSerializerSettings()
        //        {
        //            ContractResolver = Package.CachedPackageResolver.Instance
        //        };

        //        data = JsonConvert.DeserializeObject<List<Package>>(File.ReadAllText(path, Encoding.UTF8), serializerSettings);
        //        return true;
        //    }
        //    catch
        //    {
        //        return false;
        //    }
        //}

        //public static void CacheTexture(string packageId, Texture2D texture)
        //{
        //    CreateFileInTempCache($"{packageId}.png", texture.EncodeToPNG(), true);
        //}

        //public static bool GetCachedTexture(string packageId, out Texture2D texture)
        //{
        //    texture = new Texture2D(1, 1);
        //    var path = Path.Combine(CacheConstants.TempCachePath, $"{packageId}.png");
        //    if (!File.Exists(path))
        //        return false;

        //    texture.LoadImage(File.ReadAllBytes(path));
        //    return true;
        //}

        //public static void CacheWorkflowStateData(string packageId, WorkflowStateData data)
        //{
        //    var fileName = $"{packageId}-workflowStateData.asset";
        //    CreateFileInPersistentCache(fileName, JsonConvert.SerializeObject(data, Formatting.Indented), true);
        //}

        //public static bool GetCachedWorkflowStateData(string packageId, out WorkflowStateData data)
        //{
        //    data = null;
        //    var path = Path.Combine(CacheConstants.PersistentCachePath, $"{packageId}-workflowStateData.asset");
        //    if (!File.Exists(path))
        //        return false;

        //    data = JsonConvert.DeserializeObject<WorkflowStateData>(File.ReadAllText(path, Encoding.UTF8));
        //    return true;
        //}

        //public static void CacheValidationStateData(ValidationStateData data)
        //{
        //    var serializerSettings = new JsonSerializerSettings()
        //    {
        //        ContractResolver = ValidationStateDataContractResolver.Instance,
        //        Formatting = Formatting.Indented,
        //        TypeNameHandling = TypeNameHandling.Auto,
        //        Converters = new List<JsonConverter>() { new StringEnumConverter() }
        //    };

        //    CreateFileInPersistentCache(CacheConstants.ValidationResultFile, JsonConvert.SerializeObject(data, serializerSettings), true);
        //}

        //public static bool GetCachedValidationStateData(out ValidationStateData data)
        //{
        //    return GetCachedValidationStateData(Constants.RootProjectPath, out data);
        //}

        //public static bool GetCachedValidationStateData(string projectPath, out ValidationStateData data)
        //{
        //    data = null;
        //    var path = Path.Combine(projectPath, CacheConstants.PersistentCachePath, CacheConstants.ValidationResultFile);
        //    if (!File.Exists(path))
        //        return false;

        //    try
        //    {
        //        var serializerSettings = new JsonSerializerSettings()
        //        {
        //            ContractResolver = ValidationStateDataContractResolver.Instance,
        //            Formatting = Formatting.Indented,
        //            TypeNameHandling = TypeNameHandling.Auto,
        //            Converters = new List<JsonConverter>() { new StringEnumConverter() }
        //        };

        //        data = JsonConvert.DeserializeObject<ValidationStateData>(File.ReadAllText(path, Encoding.UTF8), serializerSettings);
        //        return true;
        //    }
        //    catch (System.Exception e)
        //    {
        //        UnityEngine.Debug.LogException(e);
        //        return false;
        //    }
        //}
    }
}
