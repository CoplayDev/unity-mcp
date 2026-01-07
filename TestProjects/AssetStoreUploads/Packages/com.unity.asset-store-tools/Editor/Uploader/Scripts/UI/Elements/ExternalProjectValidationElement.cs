using AssetStoreTools.Uploader.Data;
using AssetStoreTools.Utility;
using AssetStoreTools.Validator;
using System;
using System.IO;
using UnityEditor;
using UnityEngine.UIElements;

namespace AssetStoreTools.Uploader.UI.Elements
{
    internal class ExternalProjectValidationElement : ValidationElementBase
    {
        private VisualElement _projectButtonContainer;

        public ExternalProjectValidationElement(IWorkflow workflow) : base(workflow)
        {
            Create();
        }

        private void Create()
        {
            CreateProjectButtonContainer();
            CreateProjectButtons();
        }

        private void CreateProjectButtonContainer()
        {
            _projectButtonContainer = new VisualElement();
            _projectButtonContainer.AddToClassList("validation-result-view-report-button-container");

            ResultsBox.Add(_projectButtonContainer);
        }

        private void CreateProjectButtons()
        {
            var openButton = new Button(OpenProject) { text = "Open Project" };
            openButton.AddToClassList("validation-result-view-report-button");

            var saveButton = new Button(SaveProject) { text = "Save Project" };
            saveButton.AddToClassList("validation-result-view-report-button");

            _projectButtonContainer.Add(openButton);
            _projectButtonContainer.Add(saveButton);
        }

        private void OpenProject()
        {
            try
            {
                EditorUtility.DisplayProgressBar("Waiting...", "Validation project is open. Waiting for it to exit...", 0.4f);
                var projectPath = Workflow.LastValidationResult.ProjectPath;
                ExternalProjectValidator.OpenExternalValidationProject(projectPath);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private void SaveProject()
        {
            try
            {
                var projectPath = Workflow.LastValidationResult.ProjectPath;
                var savePath = EditorUtility.SaveFolderPanel("Select a folder", Environment.GetFolderPath(Environment.SpecialFolder.Desktop), string.Empty);
                if (string.IsNullOrEmpty(savePath))
                    return;

                var saveDir = new DirectoryInfo(savePath);
                if (!saveDir.Exists || saveDir.GetFileSystemInfos().Length != 0)
                {
                    EditorUtility.DisplayDialog("Saving project failed", "Selected directory must be an empty folder", "OK");
                    return;
                }

                EditorUtility.DisplayProgressBar("Saving...", "Saving project...", 0.4f);
                FileUtility.CopyDirectory(projectPath, savePath, true);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        protected override bool ConfirmValidation()
        {
            return EditorUtility.DisplayDialog("Notice", "Pre-exported package validation is performed in a separate temporary project. " +
                "It may take some time for the temporary project to be created, which will halt any actions in the current project. " +
                "The current project will resume work after the temporary project is exited.\n\nDo you wish to proceed?", "Yes", "No");
        }
    }
}