using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace AssetStoreTools.Uploader.Data.Serialization
{
    internal class WorkflowStateData
    {
        [JsonProperty("package_id")]
        private string _packageId;
        [JsonProperty("active_workflow")]
        private string _activeWorkflow;
        [JsonProperty("assets_workflow")]
        private AssetsWorkflowState _assetsWorkflow;
        [JsonProperty("unitypackage_workflow")]
        private UnityPackageWorkflowState _unityPackageWorkflow;
        [JsonProperty("hybrid_workflow")]
        private HybridPackageWorkflowState _hybridPackageWorkflow;

        public WorkflowStateData()
        {
            _activeWorkflow = string.Empty;

            _assetsWorkflow = new AssetsWorkflowState();
            _unityPackageWorkflow = new UnityPackageWorkflowState();
            _hybridPackageWorkflow = new HybridPackageWorkflowState();
        }

        public WorkflowStateData(string packageId) : this()
        {
            SetPackageId(packageId);
        }

        public string GetPackageId()
        {
            return _packageId;
        }

        public void SetPackageId(string packageId)
        {
            _packageId = packageId;
        }

        public string GetActiveWorkflow()
        {
            return _activeWorkflow;
        }

        public void SetActiveWorkflow(string activeWorkflow)
        {
            _activeWorkflow = activeWorkflow;
        }

        public AssetsWorkflowState GetAssetsWorkflowState()
        {
            return _assetsWorkflow;
        }

        public UnityPackageWorkflowState GetUnityPackageWorkflowState()
        {
            return _unityPackageWorkflow;
        }

        public HybridPackageWorkflowState GetHybridPackageWorkflowState()
        {
            return _hybridPackageWorkflow;
        }
    }
}