using AssetStoreTools.Utility;
using Newtonsoft.Json;
using System.IO;
using UnityEditor;

namespace AssetStoreTools.Uploader.Data.Serialization
{
    internal class AssetPath
    {
        [JsonProperty("path")]
        private string _path = string.Empty;
        [JsonProperty("guid")]
        private string _guid = string.Empty;

        [JsonIgnore]
        public string Path { get => _path; set { SetAssetPath(value); } }
        [JsonIgnore]
        public string Guid { get => _guid; set { _guid = value; } }

        public AssetPath() { }

        public AssetPath(string path)
        {
            SetAssetPath(path);
        }

        private void SetAssetPath(string path)
        {
            _path = path.Replace("\\", "/");
            if (TryGetGuid(_path, out var guid))
                _guid = guid;
        }

        private bool TryGetGuid(string path, out string guid)
        {
            guid = string.Empty;

            var relativePath = FileUtility.AbsolutePathToRelativePath(path, ASToolsPreferences.Instance.EnableSymlinkSupport);

            if (!relativePath.StartsWith("Assets/") && !relativePath.StartsWith("Packages/"))
                return false;

            guid = AssetDatabase.AssetPathToGUID(relativePath);
            return !string.IsNullOrEmpty(guid);
        }

        public override string ToString()
        {
            var pathFromGuid = AssetDatabase.GUIDToAssetPath(_guid);
            if (!string.IsNullOrEmpty(pathFromGuid) && (File.Exists(pathFromGuid) || Directory.Exists(pathFromGuid)))
                return pathFromGuid;

            if (File.Exists(_path) || Directory.Exists(_path))
                return _path;

            return string.Empty;
        }
    }
}