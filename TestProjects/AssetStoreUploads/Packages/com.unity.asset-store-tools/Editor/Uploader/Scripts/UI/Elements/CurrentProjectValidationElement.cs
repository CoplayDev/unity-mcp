using AssetStoreTools.Uploader.Data;
using UnityEngine.UIElements;

namespace AssetStoreTools.Uploader.UI.Elements
{
    internal class CurrentProjectValidationElement : ValidationElementBase
    {
        public CurrentProjectValidationElement(IWorkflow workflow) : base(workflow)
        {
            Create();
        }

        private void Create()
        {
            CreateResultsBox();
        }

        private void CreateResultsBox()
        {
            var _viewReportButton = new Button(ViewReport) { text = "View report" };
            _viewReportButton.AddToClassList("validation-result-view-report-button");

            ResultsBox.Add(_viewReportButton);
        }

        private void ViewReport()
        {
            AssetStoreTools.ShowAssetStoreToolsValidator(Workflow.LastValidationSettings, Workflow.LastValidationResult);
        }
    }
}