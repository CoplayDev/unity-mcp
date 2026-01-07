using AssetStoreTools.Uploader.Data;
using AssetStoreTools.Uploader.Services;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

namespace AssetStoreTools.Uploader.UI.Elements
{
    internal class PackageGroupElement : VisualElement
    {
        // Data
        public string Name => _packageGroup.Name;
        private IPackageGroup _packageGroup;
        private List<PackageElement> _packageElements;
        private bool _isExpanded;

        private IPackageFactoryService _packageFactory;

        // UI
        private Button _groupExpanderBox;
        private VisualElement _groupContent;

        private Label _expanderLabel;
        private Label _groupLabel;

        public PackageGroupElement(IPackageGroup packageGroup, IPackageFactoryService packageFactory)
        {
            _packageGroup = packageGroup;
            _packageElements = new List<PackageElement>();
            _packageGroup.OnPackagesSorted += RefreshPackages;
            _packageGroup.OnPackagesFiltered += RefreshPackages;

            _packageFactory = packageFactory;

            Create();
        }

        private void Create()
        {
            CreatePackageGroup();
            CreatePackageGroupContent();
            AddPackagesToGroupContent();
        }

        protected void CreatePackageGroup()
        {
            _groupExpanderBox = new Button(OnPackageGroupClicked);
            _groupExpanderBox.AddToClassList("package-group-expander-box");

            _expanderLabel = new Label { name = "ExpanderLabel", text = "►" };
            _expanderLabel.AddToClassList("package-group-expander");

            _groupLabel = new Label { text = $"{_packageGroup.Name} ({_packageGroup.Packages.Count})" };
            _groupLabel.AddToClassList("package-group-label");
            FormatGroupLabel(_packageGroup.Packages.Count);

            _groupExpanderBox.Add(_expanderLabel);
            _groupExpanderBox.Add(_groupLabel);

            Add(_groupExpanderBox);
        }

        private void CreatePackageGroupContent()
        {
            _groupContent = new VisualElement { name = "GroupContentBox" };
            _groupContent.AddToClassList("package-group-content-box");
            Toggle(false);

            var groupSeparator = new VisualElement { name = "GroupSeparator" };
            groupSeparator.AddToClassList("package-group-separator");

            if (_packageGroup.Name.ToLower() != "draft")
            {
                _groupLabel.SetEnabled(false);
                _groupContent.AddToClassList("unity-disabled");
                groupSeparator.style.display = DisplayStyle.Flex;
            }

            Add(_groupContent);
            Add(groupSeparator);
        }

        private void AddPackagesToGroupContent()
        {
            foreach (var package in _packageGroup.Packages)
            {
                var packageElement = new PackageElement(package, _packageFactory);
                packageElement.OnSelected += () => OnPackageSelected(packageElement);
                _packageElements.Add(packageElement);
            }
        }

        private void FormatGroupLabel(int displayedPackageCount)
        {
            if (_packageGroup.Packages.Count == displayedPackageCount)
                _groupLabel.text = $"{Name} ({displayedPackageCount})";
            else
                _groupLabel.text = $"{Name} ({displayedPackageCount}/{_packageGroup.Packages.Count})";
        }

        private void RefreshPackages(List<IPackage> packages)
        {
            _groupContent.Clear();

            foreach (var package in packages)
            {
                var correspondingElement = _packageElements.FirstOrDefault(x => x.Is(package));
                if (correspondingElement == null)
                    continue;

                _groupContent.Add(correspondingElement);
            }

            FormatGroupLabel(packages.Count());
        }

        private void OnPackageGroupClicked()
        {
            Toggle(!_isExpanded);
        }

        public void Toggle(bool expand)
        {
            if (expand)
            {
                _expanderLabel.text = "▼";
                _groupContent.style.display = DisplayStyle.Flex;
            }
            else
            {
                _expanderLabel.text = "►";
                _groupContent.style.display = DisplayStyle.None;
            }

            _isExpanded = expand;
        }

        private void OnPackageSelected(PackageElement packageElement)
        {
            foreach (var element in _packageElements)
            {
                if (element == packageElement)
                    continue;

                element.Unselect();
            }
        }
    }
}
