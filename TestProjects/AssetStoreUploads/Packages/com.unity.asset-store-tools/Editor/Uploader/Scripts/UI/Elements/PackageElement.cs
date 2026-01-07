using AssetStoreTools.Api;
using AssetStoreTools.Uploader.Data;
using AssetStoreTools.Uploader.Services;
using System;
#if !UNITY_2021_1_OR_NEWER
using UnityEditor.UIElements;
#endif
using UnityEngine;
using UnityEngine.UIElements;

namespace AssetStoreTools.Uploader.UI.Elements
{
    internal class PackageElement : VisualElement
    {
        // Data
        private IPackage _package;
        private bool _isSelected;

        private IPackageFactoryService _packageFactory;

        // UI
        private Button _foldoutBox;
        private Label _expanderLabel;
        private Label _assetLabel;
        private Label _lastDateSizeLabel;
        private Image _assetImage;

        private ProgressBar _uploadProgressBar;
        private VisualElement _uploadProgressBarBackground;

        private PackageContentElement _contentElement;

        public event Action OnSelected;

        public PackageElement(IPackage package, IPackageFactoryService packageFactory)
        {
            _package = package;
            _package.OnUpdate += Refresh;
            _package.OnIconUpdate += SetPackageThumbnail;

            _packageFactory = packageFactory;

            _isSelected = false;

            Create();
        }

        private void Create()
        {
            AddToClassList("package-full-box");

            _foldoutBox = new Button { name = "Package" };
            _foldoutBox.AddToClassList("package-foldout-box");
            if (_package.IsDraft)
                _foldoutBox.AddToClassList("package-foldout-box-draft");
            _foldoutBox.clickable.clicked += Toggle;

            // Expander, Icon and Asset Label
            VisualElement foldoutBoxInfo = new VisualElement { name = "foldoutBoxInfo" };
            foldoutBoxInfo.AddToClassList("package-foldout-box-info");

            VisualElement labelExpanderRow = new VisualElement { name = "labelExpanderRow" };
            labelExpanderRow.AddToClassList("package-expander-label-row");

            _expanderLabel = new Label { name = "ExpanderLabel", text = "►" };
            _expanderLabel.AddToClassList("package-expander");
            _expanderLabel.style.display = _package.IsDraft ? DisplayStyle.Flex : DisplayStyle.None;

            _assetImage = new Image { name = "AssetImage" };
            _assetImage.AddToClassList("package-image");

            VisualElement assetLabelInfoBox = new VisualElement { name = "assetLabelInfoBox" };
            assetLabelInfoBox.AddToClassList("package-label-info-box");

            _assetLabel = new Label { name = "AssetLabel", text = _package.Name };
            _assetLabel.AddToClassList("package-label");

            _lastDateSizeLabel = new Label { name = "AssetInfoLabel", text = FormatDateSize() };
            _lastDateSizeLabel.AddToClassList("package-info");

            assetLabelInfoBox.Add(_assetLabel);
            assetLabelInfoBox.Add(_lastDateSizeLabel);

            labelExpanderRow.Add(_expanderLabel);
            labelExpanderRow.Add(_assetImage);
            labelExpanderRow.Add(assetLabelInfoBox);

            var openInBrowserButton = new Button(OpenPackageInBrowser)
            {
                name = "OpenInBrowserButton",
                tooltip = "View your package in the Publishing Portal."
            };
            openInBrowserButton.AddToClassList("package-open-in-browser-button");

            // Header Progress bar
            _uploadProgressBar = new ProgressBar { name = "HeaderProgressBar" };
            _uploadProgressBar.AddToClassList("package-header-progress-bar");
            _uploadProgressBar.style.display = DisplayStyle.None;
            _uploadProgressBarBackground = _uploadProgressBar.Q<VisualElement>(className: "unity-progress-bar__progress");

            // Connect it all
            foldoutBoxInfo.Add(labelExpanderRow);
            foldoutBoxInfo.Add(openInBrowserButton);

            _foldoutBox.Add(foldoutBoxInfo);
            _foldoutBox.Add(_uploadProgressBar);

            Add(_foldoutBox);
        }

        private void CreateFoldoutContent()
        {
            var content = _packageFactory.CreatePackageContent(_package);
            if (content == null)
                return;

            _contentElement = new PackageContentElement(content);
            _contentElement.style.display = DisplayStyle.None;
            Add(_contentElement);

            SubscribeToContentWorkflowUpdates(content);
        }

        private void SubscribeToContentWorkflowUpdates(IPackageContent content)
        {
            foreach (var workflow in content.GetAvailableWorkflows())
            {
                workflow.OnUploadStateChanged += UpdateProgressBar;
            }
        }

        private void UpdateProgressBar(UploadStatus? status, float? progress)
        {
            if (status != null)
            {
                _uploadProgressBarBackground.style.backgroundColor = PackageUploadElement.GetColorByStatus(status.Value);
            }

            if (progress != null)
            {
                _uploadProgressBar.value = progress.Value;
            }
        }

        private void Toggle()
        {
            if (!_package.IsDraft)
                return;

            if (!Contains(_contentElement))
                CreateFoldoutContent();

            var shouldExpand = !_isSelected;
            _expanderLabel.text = shouldExpand ? "▼" : "►";

            if (shouldExpand)
                _foldoutBox.AddToClassList("package-foldout-box-expanded");
            else
                _foldoutBox.RemoveFromClassList("package-foldout-box-expanded");
            _contentElement.style.display = shouldExpand ? DisplayStyle.Flex : DisplayStyle.None;

            _isSelected = !_isSelected;
            ToggleProgressBar();

            if (_isSelected)
                OnSelected?.Invoke();
        }

        private void ToggleProgressBar()
        {
            if (!_isSelected && _uploadProgressBar.value != 0)
                _uploadProgressBar.style.display = DisplayStyle.Flex;
            else
                _uploadProgressBar.style.display = DisplayStyle.None;
        }

        public bool Is(IPackage package)
        {
            return package == _package;
        }

        public void Select()
        {
            if (!_isSelected)
                Toggle();
        }

        public void Unselect()
        {
            if (_isSelected)
                Toggle();
        }

        private void SetPackageThumbnail()
        {
            _assetImage.image = _package.Icon;
        }

        private void Refresh()
        {
            _assetLabel.text = _package.Name;
            _lastDateSizeLabel.text = FormatDateSize();
        }

        private string FormatDateSize()
        {
            return $"{_package.Category} | {_package.FormattedSize()} | {_package.FormattedModified()}";
        }

        private void OpenPackageInBrowser()
        {
            Application.OpenURL($"https://publisher.unity.com/packages/{_package.VersionId}/edit/upload");
        }
    }
}