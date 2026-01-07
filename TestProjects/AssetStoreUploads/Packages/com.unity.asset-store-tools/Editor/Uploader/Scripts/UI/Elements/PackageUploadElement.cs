using AssetStoreTools.Api;
using AssetStoreTools.Api.Responses;
using AssetStoreTools.Exporter;
using AssetStoreTools.Uploader.Data;
using AssetStoreTools.Utility;
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
#if !UNITY_2021_1_OR_NEWER
using UnityEditor.UIElements;
#endif
using UnityEngine;
using UnityEngine.UIElements;

namespace AssetStoreTools.Uploader.UI.Elements
{
    internal class PackageUploadElement : VisualElement
    {
        // Data
        private IWorkflow _workflow;
        private bool _enableExporting;

        // UI
        private VisualElement _exportAndUploadContainer;

        private Button _cancelUploadButton;
        private VisualElement _uploadProgressContainer;
        private ProgressBar _uploadProgressBar;
        private VisualElement _uploadProgressBarBackground;

        public event Action OnInteractionAvailable;
        public event Action OnInteractionUnavailable;

        public PackageUploadElement(IWorkflow workflow, bool exposeExportButton)
        {
            _workflow = workflow;
            _enableExporting = exposeExportButton;

            Create();
        }

        private void Create()
        {
            AddToClassList("uploading-box");

            CreateButtonContainer();
            CreateProgressContainer();
        }

        private void CreateButtonContainer()
        {
            _exportAndUploadContainer = new VisualElement();
            _exportAndUploadContainer.AddToClassList("uploading-export-and-upload-container");

            CreateExportButton();
            CreateUploadButton();
            Add(_exportAndUploadContainer);
        }

        private void CreateExportButton()
        {
            if (!_enableExporting)
                return;

            var _exportAndUploadButton = new Button(async () => await Export(true)) { name = "ExportButton", text = "Export" };
            _exportAndUploadButton.AddToClassList("uploading-export-button");

            _exportAndUploadContainer.Add(_exportAndUploadButton);
        }

        private void CreateUploadButton()
        {
            var _uploadButton = new Button(Upload) { name = "UploadButton" };
            _uploadButton.text = _enableExporting ? "Export and Upload" : "Upload";
            _uploadButton.AddToClassList("uploading-upload-button");

            _exportAndUploadContainer.Add(_uploadButton);
        }

        private void CreateProgressContainer()
        {
            _uploadProgressContainer = new VisualElement();
            _uploadProgressContainer.AddToClassList("uploading-progress-container");
            _uploadProgressContainer.style.display = DisplayStyle.None;

            _uploadProgressBar = new ProgressBar { name = "UploadProgressBar" };
            _uploadProgressBar.AddToClassList("uploading-progress-bar");
            _uploadProgressBarBackground = _uploadProgressBar.Q<VisualElement>(className: "unity-progress-bar__progress");

            _cancelUploadButton = new Button() { name = "CancelButton", text = "Cancel" };
            _cancelUploadButton.AddToClassList("uploading-cancel-button");

            _uploadProgressContainer.Add(_uploadProgressBar);
            _uploadProgressContainer.Add(_cancelUploadButton);

            Add(_uploadProgressContainer);
        }

        private async Task<PackageExporterResult> Export(bool interactive)
        {
            try
            {
                DisableInteraction();

                if (!_workflow.IsPathSet)
                {
                    EditorUtility.DisplayDialog("Exporting failed", "No path was selected. Please " +
                        "select a path and try again.", "OK");
                    return new PackageExporterResult() { Success = false, Exception = new Exception("No path was selected.") };
                }

                var rootProjectPath = Constants.RootProjectPath;
                var packageNameStripped = Regex.Replace(_workflow.PackageName, "[^a-zA-Z0-9]", "");
                var outputName = $"{packageNameStripped}-{DateTime.Now:yyyy-dd-M--HH-mm-ss}";

                string outputPath;
                if (interactive)
                {
                    outputPath = EditorUtility.SaveFilePanel("Export Package", rootProjectPath,
                        outputName, _workflow.PackageExtension.Remove(0, 1)); // Ignoring the '.' character since SaveFilePanel already appends it

                    if (string.IsNullOrEmpty(outputPath))
                        return new PackageExporterResult() { Success = false, Exception = null };
                }
                else
                {
                    outputPath = $"Temp/{outputName}{_workflow.PackageExtension}";
                }

                var exportResult = await _workflow.ExportPackage(outputPath);
                if (!exportResult.Success)
                {
                    Debug.LogError($"Package exporting failed: {exportResult.Exception}");
                    EditorUtility.DisplayDialog("Exporting failed", exportResult.Exception.Message, "OK");
                }
                else if (interactive)
                    Debug.Log($"Package exported to '{Path.GetFullPath(exportResult.ExportedPath).Replace("\\", "/")}'");

                return exportResult;
            }
            finally
            {
                if (interactive)
                    EnableInteraction();
            }
        }

        private async void Upload()
        {
            DisableInteraction();

            if (await ValidateUnityVersionsBeforeUpload() == false)
            {
                EnableInteraction();
                return;
            }

            var exportResult = await Export(false);
            if (!exportResult.Success)
            {
                EnableInteraction();
                return;
            }

            if (!_workflow.IsPathSet)
            {
                EditorUtility.DisplayDialog("Uploading failed", "No path was selected. Please " +
                    "select a path and try again.", "OK");
                EnableInteraction();
                return;
            }

            _exportAndUploadContainer.style.display = DisplayStyle.None;
            _uploadProgressContainer.style.display = DisplayStyle.Flex;

            _cancelUploadButton.clicked += Cancel;
            _workflow.OnUploadStateChanged += UpdateProgressBar;
            var response = await _workflow.UploadPackage(exportResult.ExportedPath);
            _workflow.OnUploadStateChanged -= UpdateProgressBar;

            await OnUploadingStopped(response);
        }

        private async Task<bool> ValidateUnityVersionsBeforeUpload()
        {
            var validationEnabled = ASToolsPreferences.Instance.UploadVersionCheck;
            if (!validationEnabled)
                return true;

            var requiredVersionUploaded = await _workflow.ValidatePackageUploadedVersions();
            if (requiredVersionUploaded)
                return true;

            var result = EditorUtility.DisplayDialogComplex("Asset Store Tools", $"You may upload this package, but you will need to add a package using Unity version {Constants.Uploader.MinRequiredUnitySupportVersion} " +
                "or higher to be able to submit a new asset", "Upload", "Cancel", "Upload and do not display this again");

            switch (result)
            {
                case 1:
                    return false;
                case 2:
                    ASToolsPreferences.Instance.UploadVersionCheck = false;
                    ASToolsPreferences.Instance.Save();
                    break;
            }

            return true;
        }

        private void UpdateProgressBar(UploadStatus? status, float? progress)
        {
            if (status != null)
            {
                _uploadProgressBarBackground.style.backgroundColor = GetColorByStatus(status.Value);
            }

            if (progress != null)
            {
                _uploadProgressBar.value = progress.Value;
                _uploadProgressBar.title = $"{progress.Value:0.#}%";

                if (progress == 100f && _cancelUploadButton.enabledInHierarchy)
                    _cancelUploadButton.SetEnabled(false);
            }
        }

        private void Cancel()
        {
            _cancelUploadButton.SetEnabled(false);
            _workflow.AbortUpload();
        }

        private async Task OnUploadingStopped(PackageUploadResponse response)
        {
            if (!response.Success && !response.Cancelled)
            {
                Debug.LogException(response.Exception);
            }

            if (response.Success)
            {
                await _workflow.RefreshPackage();
            }

            if (response.Status == UploadStatus.ResponseTimeout)
            {
                Debug.LogWarning($"All bytes for the package '{_workflow.PackageName}' have been uploaded, but a response " +
                        $"from the server was not received. This can happen because of Firewall restrictions. " +
                        $"Please make sure that a new version of your package has reached the Publishing Portal.");
            }

            _uploadProgressBar.title = GetProgressBarTitleByStatus(response.Status);

            _cancelUploadButton.clickable = null;
            _cancelUploadButton.clicked += Reset;
            _cancelUploadButton.text = "Done";
            _cancelUploadButton.SetEnabled(true);
        }

        private void Reset()
        {
            _cancelUploadButton.clickable = null;
            _cancelUploadButton.text = "Cancel";

            _workflow.ResetUploadStatus();
            UpdateProgressBar(UploadStatus.Default, 0f);

            _uploadProgressContainer.style.display = DisplayStyle.None;
            _exportAndUploadContainer.style.display = DisplayStyle.Flex;
            EnableInteraction();
        }

        public static Color GetColorByStatus(UploadStatus status)
        {
            switch (status)
            {
                default:
                case UploadStatus.Default:
                    return new Color(0.13f, 0.59f, 0.95f);
                case UploadStatus.Success:
                case UploadStatus.ResponseTimeout:
                    return new Color(0f, 0.50f, 0.14f);
                case UploadStatus.Cancelled:
                    return new Color(0.78f, 0.59f, 0f);
                case UploadStatus.Fail:
                    return new Color(0.69f, 0.04f, 0.04f);
            }
        }

        private string GetProgressBarTitleByStatus(UploadStatus status)
        {
            var progressBarTitle = "Upload: ";
            switch (status)
            {
                case UploadStatus.ResponseTimeout:
                    progressBarTitle += UploadStatus.Success;
                    break;
                default:
                    progressBarTitle += status;
                    break;
            }

            return progressBarTitle;
        }

        private void EnableInteraction()
        {
            _exportAndUploadContainer.SetEnabled(true);
            OnInteractionAvailable?.Invoke();
        }

        private void DisableInteraction()
        {
            _exportAndUploadContainer.SetEnabled(false);
            OnInteractionUnavailable?.Invoke();
            SetEnabled(true);
        }
    }
}