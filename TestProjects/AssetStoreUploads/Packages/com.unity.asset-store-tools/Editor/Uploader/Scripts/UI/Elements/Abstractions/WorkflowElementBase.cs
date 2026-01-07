using AssetStoreTools.Uploader.Data;
using AssetStoreTools.Utility;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine.UIElements;

namespace AssetStoreTools.Uploader.UI.Elements
{
    internal abstract class WorkflowElementBase : VisualElement
    {
        // Data
        protected IWorkflow Workflow;
        public string Name => Workflow.Name;
        public string DisplayName => Workflow.DisplayName;

        // UI Elements that all workflows have
        protected PathSelectionElement PathSelectionElement;
        protected PreviewGenerationElement PreviewGenerationElement;
        protected ValidationElementBase ValidationElement;
        protected PackageUploadElement UploadElement;

        public event Action OnInteractionAvailable;
        public event Action OnInteractionUnavailable;

        public WorkflowElementBase(IWorkflow workflow)
        {
            Workflow = workflow;
        }

        protected void CreatePathElement(string labelText, string labelTooltip)
        {
            PathSelectionElement = new PathSelectionElement(labelText, labelTooltip);
            PathSelectionElement.OnBrowse += BrowsePath;
            Add(PathSelectionElement);
        }

        protected void CreatePreviewGenerationElement()
        {
            PreviewGenerationElement = new PreviewGenerationElement(Workflow);
            PreviewGenerationElement.style.display = DisplayStyle.None;
            var callback = new Action(() =>
                PreviewGenerationElement.style.display = ASToolsPreferences.Instance.UseLegacyExporting
                ? DisplayStyle.None
                : DisplayStyle.Flex);
            RegisterCallback<AttachToPanelEvent>((_) => { ASToolsPreferences.OnSettingsChange += callback; });
            RegisterCallback<DetachFromPanelEvent>((_) => { ASToolsPreferences.OnSettingsChange -= callback; });
            Add(PreviewGenerationElement);
        }

        protected void CreateValidationElement(ValidationElementBase validationElement)
        {
            ValidationElement = validationElement;
            ValidationElement.style.display = DisplayStyle.None;
            Add(ValidationElement);
        }

        protected void CreateUploadElement(IWorkflow workflow, bool exposeExportButton)
        {
            UploadElement = new PackageUploadElement(workflow, exposeExportButton);
            UploadElement.OnInteractionAvailable += EnableInteraction;
            UploadElement.OnInteractionUnavailable += DisableInteraction;
            UploadElement.style.display = DisplayStyle.None;
            Add(UploadElement);
        }

        protected abstract void BrowsePath();

        protected void SetPathSelectionTextField(string value)
        {
            if (string.IsNullOrEmpty(value))
                return;

            PathSelectionElement.SetPath(value);
            ValidationElement.style.display = DisplayStyle.Flex;
            UploadElement.style.display = DisplayStyle.Flex;

            if (PreviewGenerationElement != null && !ASToolsPreferences.Instance.UseLegacyExporting)
            {
                PreviewGenerationElement.style.display = DisplayStyle.Flex;
            }
        }

        protected void CheckForMissingMetas(IEnumerable<string> paths)
        {
            bool displayDialog = ASToolsPreferences.Instance.DisplayHiddenMetaDialog && FileUtility.IsMissingMetaFiles(paths);
            if (!displayDialog)
                return;

            var selectedOption = EditorUtility.DisplayDialogComplex(
                    "Notice",
                    "Your package includes hidden folders which do not contain meta files. " +
                    "Hidden folders will not be exported unless they contain meta files.\n\nWould you like meta files to be generated?",
                    "Yes", "No", "No and do not display this again");

            switch (selectedOption)
            {
                case 0:
                    try
                    {
                        FileUtility.GenerateMetaFiles(paths);
                        EditorUtility.DisplayDialog(
                            "Success",
                            "Meta files have been generated. Please note that further manual tweaking may be required to set up correct references",
                            "OK");
                    }
                    catch (Exception e)
                    {
                        EditorUtility.DisplayDialog(
                            "Error",
                            $"Meta file generation failed: {e.Message}",
                            "OK"
                            );
                    }
                    break;
                case 1:
                    // Do nothing
                    return;
                case 2:
                    ASToolsPreferences.Instance.DisplayHiddenMetaDialog = false;
                    ASToolsPreferences.Instance.Save();
                    return;
            }
        }

        public bool Is(IWorkflow workflow)
        {
            return Workflow == workflow;
        }

        protected virtual void EnableInteraction()
        {
            PathSelectionElement.SetEnabled(true);
            ValidationElement.SetEnabled(true);
            PreviewGenerationElement?.SetEnabled(true);
            UploadElement.SetEnabled(true);
            OnInteractionAvailable?.Invoke();
        }

        protected virtual void DisableInteraction()
        {
            PathSelectionElement.SetEnabled(false);
            ValidationElement.SetEnabled(false);
            PreviewGenerationElement?.SetEnabled(false);
            UploadElement.SetEnabled(false);
            OnInteractionUnavailable?.Invoke();
        }

        protected abstract void Deserialize();
    }
}