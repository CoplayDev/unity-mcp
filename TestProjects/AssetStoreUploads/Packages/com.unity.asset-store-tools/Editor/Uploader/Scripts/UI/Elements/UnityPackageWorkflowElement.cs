using AssetStoreTools.Uploader.Data;
using AssetStoreTools.Utility;
using UnityEditor;

namespace AssetStoreTools.Uploader.UI.Elements
{
    internal class UnityPackageWorkflowElement : WorkflowElementBase
    {
        // Data
        private UnityPackageWorkflow _workflow;

        public UnityPackageWorkflowElement(UnityPackageWorkflow workflow) : base(workflow)
        {
            _workflow = workflow;
            Create();
        }

        private void Create()
        {
            CreatePathElement("Package path", "Select the .unitypackage file you would like to upload.");
            CreateValidationElement(new ExternalProjectValidationElement(_workflow));
            CreateUploadElement(_workflow, false);
            Deserialize();
        }

        protected override void BrowsePath()
        {
            // Path retrieval
            var absolutePackagePath = EditorUtility.OpenFilePanel("Select a .unitypackage file", Constants.RootProjectPath, "unitypackage");

            if (string.IsNullOrEmpty(absolutePackagePath))
                return;

            var relativeExportPath = FileUtility.AbsolutePathToRelativePath(absolutePackagePath, ASToolsPreferences.Instance.EnableSymlinkSupport);
            if (!_workflow.IsPathValid(relativeExportPath, out var error))
            {
                EditorUtility.DisplayDialog("Invalid selection", error, "OK");
                return;
            }

            HandleUnityPackageUploadPathSelection(relativeExportPath, true);
        }

        private void HandleUnityPackageUploadPathSelection(string selectedPackagePath, bool serialize)
        {
            if (string.IsNullOrEmpty(selectedPackagePath))
                return;

            _workflow.SetPackagePath(selectedPackagePath, serialize);
            SetPathSelectionTextField(selectedPackagePath);
        }

        protected override void Deserialize()
        {
            HandleUnityPackageUploadPathSelection(_workflow.GetPackagePath(), false);
        }
    }
}