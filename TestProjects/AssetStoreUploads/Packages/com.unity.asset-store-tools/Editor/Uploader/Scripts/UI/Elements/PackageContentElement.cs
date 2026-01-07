using AssetStoreTools.Uploader.Data;
using AssetStoreTools.Utility;
using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace AssetStoreTools.Uploader.UI.Elements
{
    internal class PackageContentElement : VisualElement
    {
        // Data
        private IPackageContent _content;
        private List<WorkflowElementBase> _workflowElements;

        // UI
        private VisualElement _workflowSelectionBox;
        private ToolbarMenu _toolbarMenu;

        public PackageContentElement(IPackageContent content)
        {
            _content = content;
            content.OnActiveWorkflowChanged += ActiveWorkflowChanged;

            _workflowElements = new List<WorkflowElementBase>();

            Create();
        }

        private void Create()
        {
            AddToClassList("package-content-element");

            CreateWorkflowSelection();
            CreateWorkflows();
            Deserialize();
        }

        private void CreateWorkflowSelection()
        {
            _workflowSelectionBox = new VisualElement();
            _workflowSelectionBox.AddToClassList("package-content-option-box");

            VisualElement labelHelpRow = new VisualElement();
            labelHelpRow.AddToClassList("package-content-option-label-help-row");

            Label workflowLabel = new Label { text = "Upload type" };
            Image workflowLabelTooltip = new Image
            {
                tooltip = "Select what content you are uploading to the Asset Store"
                + "\n\n- From Assets Folder - content located within the project's 'Assets' folder or one of its subfolders"
                + "\n\n- Pre-exported .unitypackage - content that has already been compressed into a .unitypackage file"
#if UNITY_ASTOOLS_EXPERIMENTAL
                + "\n\n- Local UPM Package - content that is located within the project's 'Packages' folder. Only embedded and local packages are supported"
#endif
            };

            labelHelpRow.Add(workflowLabel);
            labelHelpRow.Add(workflowLabelTooltip);

            _toolbarMenu = new ToolbarMenu();
            _toolbarMenu.AddToClassList("package-content-option-dropdown");

            foreach (var workflow in _content.GetAvailableWorkflows())
            {
                AppendToolbarActionForWorkflow(workflow);
            }

            _workflowSelectionBox.Add(labelHelpRow);
            _workflowSelectionBox.Add(_toolbarMenu);

            Add(_workflowSelectionBox);
        }

        private void AppendToolbarActionForWorkflow(IWorkflow workflow)
        {
            _toolbarMenu.menu.AppendAction(workflow.DisplayName, _ =>
            {
                _content.SetActiveWorkflow(workflow);
            });
        }

        private void CreateWorkflows()
        {
            foreach (var workflow in _content.GetAvailableWorkflows())
            {
                WorkflowElementBase element;
                switch (workflow)
                {
                    case AssetsWorkflow assetsWorkflow:
                        element = new AssetsWorkflowElement(assetsWorkflow);
                        break;
                    case UnityPackageWorkflow unityPackageWorkflow:
                        element = new UnityPackageWorkflowElement(unityPackageWorkflow);
                        break;
#if UNITY_ASTOOLS_EXPERIMENTAL
                    case HybridPackageWorkflow hybridPackageWorkflow:
                        element = new HybridPackageWorkflowElement(hybridPackageWorkflow);
                        break;
#endif
                    default:
                        ASDebug.LogWarning("Package Content Element received an undefined workflow");
                        continue;
                }

                element.OnInteractionAvailable += EnableInteraction;
                element.OnInteractionUnavailable += DisableInteraction;
                _workflowElements.Add(element);
                Add(element);
            }
        }

        private void ActiveWorkflowChanged(IWorkflow workflow)
        {
            _toolbarMenu.text = workflow.DisplayName;
            foreach (var workflowElement in _workflowElements)
            {
                bool show = workflowElement.Is(workflow);
                workflowElement.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private void EnableInteraction()
        {
            _workflowSelectionBox.SetEnabled(true);
        }

        private void DisableInteraction()
        {
            _workflowSelectionBox.SetEnabled(false);
        }

        private void Deserialize()
        {
            ActiveWorkflowChanged(_content.GetActiveWorkflow());
        }
    }
}