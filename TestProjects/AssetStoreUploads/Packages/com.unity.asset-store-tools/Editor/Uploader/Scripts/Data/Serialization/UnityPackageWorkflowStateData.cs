using Newtonsoft.Json;

namespace AssetStoreTools.Uploader.Data.Serialization
{
    internal class UnityPackageWorkflowState
    {
        [JsonProperty("package_path")]
        private AssetPath _packagePath;

        public UnityPackageWorkflowState()
        {
            _packagePath = new AssetPath();
        }

        public string GetPackagePath()
        {
            return _packagePath?.ToString();
        }

        public void SetPackagePath(string path)
        {
            _packagePath = new AssetPath(path);
        }
    }
}