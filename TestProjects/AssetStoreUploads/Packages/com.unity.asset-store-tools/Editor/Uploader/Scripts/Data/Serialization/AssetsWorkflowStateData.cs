using Newtonsoft.Json;
using System.Collections.Generic;

namespace AssetStoreTools.Uploader.Data.Serialization
{
    internal class AssetsWorkflowState
    {
        [JsonProperty("main_path")]
        private AssetPath _mainPath;
        [JsonProperty("special_folders")]
        private List<AssetPath> _specialFolders;
        [JsonProperty("include_dependencies")]
        private bool _includeDependencies;
        [JsonProperty("dependencies")]
        private List<string> _dependencies;

        public AssetsWorkflowState()
        {
            _mainPath = new AssetPath();
            _includeDependencies = false;
            _dependencies = new List<string>();
            _specialFolders = new List<AssetPath>();
        }

        public string GetMainPath()
        {
            return _mainPath?.ToString();
        }

        public void SetMainPath(string path)
        {
            _mainPath = new AssetPath(path);
        }

        public bool GetIncludeDependencies()
        {
            return _includeDependencies;
        }

        public void SetIncludeDependencies(bool value)
        {
            _includeDependencies = value;
        }

        public List<string> GetDependencies()
        {
            return _dependencies;
        }

        public void SetDependencies(IEnumerable<string> dependencies)
        {
            _dependencies = new List<string>();
            foreach (var dependency in dependencies)
                _dependencies.Add(dependency);
        }

        public List<string> GetSpecialFolders()
        {
            var specialFolders = new List<string>();
            foreach (var folder in _specialFolders)
            {
                var path = folder.ToString();
                if (!string.IsNullOrEmpty(path))
                    specialFolders.Add(path);
            }

            return specialFolders;
        }

        public void SetSpecialFolders(List<string> specialFolders)
        {
            _specialFolders = new List<AssetPath>();
            foreach (var path in specialFolders)
                _specialFolders.Add(new AssetPath(path));
        }
    }
}