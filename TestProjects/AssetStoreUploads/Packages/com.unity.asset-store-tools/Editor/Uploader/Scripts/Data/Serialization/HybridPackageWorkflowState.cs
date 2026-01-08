using Newtonsoft.Json;
using System.Collections.Generic;

namespace AssetStoreTools.Uploader.Data.Serialization
{
    internal class HybridPackageWorkflowState
    {
        [JsonProperty("package_name")]
        private string _packageName;
        [JsonProperty("dependencies")]
        private List<string> _dependencies;

        public HybridPackageWorkflowState()
        {
            _packageName = string.Empty;
            _dependencies = new List<string>();
        }

        public string GetPackageName()
        {
            return _packageName;
        }

        public void SetPackageName(string packageName)
        {
            _packageName = packageName;
        }

        public List<string> GetPackageDependencies()
        {
            return _dependencies;
        }

        public void SetPackageDependencies(IEnumerable<string> dependencies)
        {
            _dependencies.Clear();
            foreach (var dependency in dependencies)
                _dependencies.Add(dependency);
        }
    }
}