using AssetStoreTools.Uploader.Data;
using AssetStoreTools.Uploader.Services;
using AssetStoreTools.Uploader.Services.Api;
using AssetStoreTools.Uploader.UI.Elements;
using AssetStoreTools.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using PackageModel = AssetStoreTools.Api.Models.Package;

namespace AssetStoreTools.Uploader.UI.Views
{
    internal class PackageListView : VisualElement
    {
        // Data
        private List<IPackage> _packages;
        private readonly string[] _priorityGroupNames = { "draft", "published" };

        private IPackageDownloadingService _packageDownloadingService;
        private IPackageFactoryService _packageFactory;

        // UI
        private LoadingSpinner _loadingSpinner;
        private ScrollView _packageScrollView;
        private PackageListToolbar _packageListToolbar;

        public event Action<Exception> OnInitializeError;

        public PackageListView(IPackageDownloadingService packageDownloadingService, IPackageFactoryService elementFactory)
        {
            _packages = new List<IPackage>();
            _packageDownloadingService = packageDownloadingService;
            _packageFactory = elementFactory;

            Create();
            EditorSceneManager.activeSceneChangedInEditMode += OnSceneChange;
        }

        private void Create()
        {
            styleSheets.Add(StyleSelector.UploaderWindow.PackageListViewStyle);
            styleSheets.Add(StyleSelector.UploaderWindow.PackageListViewTheme);

            AddToClassList("package-list-view");

            CreateFilteringTools();
            CreateLoadingSpinner();
            CreateScrollView();

            ShowPackagesView();
        }

        private void CreateScrollView()
        {
            _packageScrollView = new ScrollView();
            Add(_packageScrollView);
        }

        private void CreateFilteringTools()
        {
            _packageListToolbar = new PackageListToolbar();
            Add(_packageListToolbar);
        }

        private void CreateLoadingSpinner()
        {
            _loadingSpinner = new LoadingSpinner();
            Add(_loadingSpinner);
        }

        private void InsertReadOnlyInfoBox(string infoText)
        {
            var groupHeader = new Box { name = "GroupReadOnlyInfoBox" };
            groupHeader.AddToClassList("package-group-info-box");

            var infoImage = new Image();
            groupHeader.Add(infoImage);

            var infoLabel = new Label { text = infoText };
            groupHeader.Add(infoLabel);

            _packageScrollView.Add(groupHeader);
        }

        public async Task LoadPackages(bool useCachedData)
        {
            _packages.Clear();
            _packageScrollView.Clear();
            _packageListToolbar.SetEnabled(false);

            if (!useCachedData)
            {
                _packageDownloadingService.ClearPackageData();
            }

            _loadingSpinner.Show();
            await Task.Delay(100);

            try
            {
                var response = await _packageDownloadingService.GetPackageData();

                if (response.Cancelled)
                {
                    ASDebug.Log("Package retrieval was cancelled");
                    return;
                }

                if (!response.Success)
                {
                    ASDebug.LogError(response.Exception);
                    OnInitializeError?.Invoke(response.Exception);
                    return;
                }

                var packageModels = response.Packages;
                ASDebug.Log($"Found {packageModels.Count} packages");

                if (packageModels.Count == 0)
                {
                    InsertReadOnlyInfoBox("You do not have any packages yet. Please visit the Publishing Portal if you " +
                        "would like to create one.");
                    return;
                }

                // Create package groups
                _packages = CreatePackages(packageModels);
                var packageGroups = CreatePackageGroups(_packages);
                var packageGroupElements = CreatePackageGroupElements(packageGroups);
                PopulatePackageList(packageGroupElements);

                // Setup filtering and thumbnails
                SetupFilteringToolbar(packageGroups);
                DownloadAndSetThumbnails();
            }
            finally
            {
                _loadingSpinner.Hide();
            }
        }

        private List<IPackage> CreatePackages(List<PackageModel> packageModels)
        {
            return _packages = packageModels.Select(x => _packageFactory.CreatePackage(x)).ToList();
        }

        private List<IPackageGroup> CreatePackageGroups(List<IPackage> packages)
        {
            var packageGroups = new List<IPackageGroup>();
            var packagesByStatus = packages.GroupBy(x => x.Status).ToDictionary(x => x.Key, x => x.ToList());

            foreach (var kvp in packagesByStatus)
            {
                var groupName = char.ToUpper(kvp.Key[0]) + kvp.Key.Substring(1);
                var groupPackages = kvp.Value;
                var packageGroup = _packageFactory.CreatePackageGroup(groupName, groupPackages);
                packageGroups.Add(packageGroup);
            }

            return packageGroups;
        }

        private List<PackageGroupElement> CreatePackageGroupElements(List<IPackageGroup> packageGroups)
        {
            var elements = new List<PackageGroupElement>();
            foreach (var packageGroup in packageGroups)
                elements.Add(new PackageGroupElement(packageGroup, _packageFactory));

            return elements;
        }

        private void PopulatePackageList(List<PackageGroupElement> packageGroups)
        {
            // Draft group
            var draftGroup = packageGroups.FirstOrDefault(x => x.Name.Equals("draft", StringComparison.OrdinalIgnoreCase));
            if (draftGroup != null)
            {
                draftGroup.Toggle(true);
                _packageScrollView.Add(draftGroup);
            }

            // Infobox will only be shown if:
            // 1) There is more than 1 group OR
            // 2) There is only 1 group, but it is not draft
            var showInfoBox = packageGroups.Count > 1
                || packageGroups.Count == 1 && !packageGroups[0].Name.Equals("draft", StringComparison.OrdinalIgnoreCase);

            if (showInfoBox)
                InsertReadOnlyInfoBox("Only packages with a 'Draft' status can be selected for uploading Assets");

            // Priority groups
            foreach (var priorityName in _priorityGroupNames)
            {
                var priorityGroup = packageGroups.FirstOrDefault(x => x.Name.Equals(priorityName, StringComparison.OrdinalIgnoreCase));
                if (priorityGroup == null || _packageScrollView.Contains(priorityGroup))
                    continue;

                _packageScrollView.Add(priorityGroup);
            }

            // The rest
            foreach (var group in packageGroups)
            {
                if (!_packageScrollView.Contains(group))
                    _packageScrollView.Add(group);
            }
        }

        private void SetupFilteringToolbar(List<IPackageGroup> packageGroups)
        {
            _packageListToolbar.SetPackageGroups(packageGroups);
            _packageListToolbar.Sort(PackageSorting.Name);
            _packageListToolbar.SetEnabled(true);
        }

        private void DownloadAndSetThumbnails()
        {
            foreach (var package in _packages)
            {
                DownloadAndSetThumbnail(package);
            }
        }

        private async void DownloadAndSetThumbnail(IPackage package)
        {
            var response = await _packageDownloadingService.GetPackageThumbnail(package);
            if (!response.Success)
                return;

            package.UpdateIcon(response.Thumbnail);
        }

        private void ShowPackagesView()
        {
            _packageScrollView.style.display = DisplayStyle.Flex;
            _packageListToolbar.style.display = DisplayStyle.Flex;
        }

        private void OnSceneChange(Scene _, Scene __)
        {
            DownloadAndSetThumbnails();
        }
    }
}
