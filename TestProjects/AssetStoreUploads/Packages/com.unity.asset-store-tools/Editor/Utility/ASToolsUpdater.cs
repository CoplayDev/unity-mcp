using AssetStoreTools.Api;
using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AssetStoreTools.Utility
{
    [InitializeOnLoad]
    internal class ASToolsUpdater : AssetStoreToolsWindow
    {
        protected override string WindowTitle => "Asset Store Tools Update Check";

        private static IAssetStoreApi _api;

        private VisualElement _loadingContainer;
        private VisualElement _versionInfoContainer;

        private Image _loadingImage;
        private double _lastTimeSinceStartup;
        private double _timeSinceLoadingImageChange;
        private int _loadingImageIndex;

        private static bool _updateCheckPerformed
        {
            get
            {
                return SessionState.GetBool("AST_UpdateChecked", false);
            }
            set
            {
                SessionState.SetBool("AST_UpdateChecked", value);
            }
        }

        static ASToolsUpdater()
        {
            _api = new AssetStoreApi(new AssetStoreClient());
            // Retrieving cached SessionState/PlayerPrefs values is not allowed from an instance field initializer
            EditorApplication.update += CheckForUpdatesAfterEditorUpdate;
        }

        private static async void CheckForUpdatesAfterEditorUpdate()
        {
            EditorApplication.update -= CheckForUpdatesAfterEditorUpdate;

            if (!ShouldCheckForUpdates())
                return;

            await CheckForUpdates((success, currentVersion, latestVersion) =>
            {
                if (success && currentVersion < latestVersion)
                {
                    AssetStoreTools.OpenUpdateChecker();
                }
            });
        }

        private static bool ShouldCheckForUpdates()
        {
            if (!ASToolsPreferences.Instance.CheckForUpdates)
                return false;

            return _updateCheckPerformed == false;
        }

        private static async Task CheckForUpdates(Action<bool, Version, Version> OnUpdatesChecked)
        {
            _updateCheckPerformed = true;
            var latestVersionResult = await _api.GetLatestAssetStoreToolsVersion();
            if (!latestVersionResult.Success)
            {
                OnUpdatesChecked?.Invoke(false, null, null);
                return;
            }

            Version currentVersion = null;
            Version latestVersion = null;

            try
            {
                var latestVersionStr = latestVersionResult.Version;
                var currentVersionStr = PackageUtility.GetAllPackages().FirstOrDefault(x => x.name == "com.unity.asset-store-tools").version;

                currentVersion = new Version(currentVersionStr);
                latestVersion = new Version(latestVersionStr);
            }
            catch
            {
                OnUpdatesChecked?.Invoke(false, null, null);
            }

            OnUpdatesChecked?.Invoke(true, currentVersion, latestVersion);
        }

        protected override void Init()
        {
            rootVisualElement.styleSheets.Add(StyleSelector.UpdaterWindow.UpdaterWindowStyle);
            rootVisualElement.styleSheets.Add(StyleSelector.UpdaterWindow.UpdaterWindowTheme);

            SetupLoadingSpinner();
            _ = CheckForUpdates(OnVersionsRetrieved);
        }

        private void OnVersionsRetrieved(bool success, Version currentVersion, Version latestVersion)
        {
            if (_loadingContainer != null)
                _loadingContainer.style.display = DisplayStyle.None;

            if (success)
            {
                SetupVersionInfo(currentVersion, latestVersion);
            }
            else
            {
                SetupFailInfo();
            }
        }

        private void SetupLoadingSpinner()
        {
            _loadingContainer = new VisualElement();
            _loadingContainer.AddToClassList("updater-loading-container");
            _loadingImage = new Image();
            EditorApplication.update += LoadingSpinLoop;

            _loadingContainer.Add(_loadingImage);
            rootVisualElement.Add(_loadingContainer);
        }

        private void SetupVersionInfo(Version currentVersion, Version latestVersion)
        {
            _versionInfoContainer = new VisualElement();
            _versionInfoContainer.AddToClassList("updater-info-container");

            AddDescriptionLabels(currentVersion, latestVersion);
            AddUpdateButtons(currentVersion, latestVersion);
            AddCheckForUpdatesToggle();

            rootVisualElement.Add(_versionInfoContainer);
        }

        private void AddDescriptionLabels(Version currentVersion, Version latestVersion)
        {
            var descriptionText = currentVersion < latestVersion ?
                "An update to the Asset Store Publishing Tools is available. Updating to the latest version is highly recommended." :
                "Asset Store Publishing Tools are up to date!";

            var labelContainer = new VisualElement();
            labelContainer.AddToClassList("updater-info-container-labels");
            var descriptionLabel = new Label(descriptionText);
            descriptionLabel.AddToClassList("updater-info-container-labels-description");

            var currentVersionRow = new VisualElement();
            currentVersionRow.AddToClassList("updater-info-container-labels-row");
            var latestVersionRow = new VisualElement();
            latestVersionRow.AddToClassList("updater-info-container-labels-row");

            var currentVersionLabel = new Label("Current version:");
            currentVersionLabel.AddToClassList("updater-info-container-labels-row-identifier");
            var latestVersionLabel = new Label("Latest version:");
            latestVersionLabel.AddToClassList("updater-info-container-labels-row-identifier");

            var currentVersionLabelValue = new Label(currentVersion.ToString());
            var latestVersionLabelValue = new Label(latestVersion.ToString());

            currentVersionRow.Add(currentVersionLabel);
            currentVersionRow.Add(currentVersionLabelValue);
            latestVersionRow.Add(latestVersionLabel);
            latestVersionRow.Add(latestVersionLabelValue);

            labelContainer.Add(descriptionLabel);
            labelContainer.Add(currentVersionRow);
            labelContainer.Add(latestVersionRow);

            _versionInfoContainer.Add(labelContainer);
        }

        private void AddUpdateButtons(Version currentVersion, Version latestVersion)
        {
            if (currentVersion >= latestVersion)
                return;

            var buttonContainer = new VisualElement();
            buttonContainer.AddToClassList("updater-info-container-buttons");
            var latestVersionButton = new Button(() => Application.OpenURL(Constants.Updater.AssetStoreToolsUrl)) { text = "Get the latest version" };
            var skipVersionButton = new Button(Close) { text = "Skip for now" };

            buttonContainer.Add(latestVersionButton);
            buttonContainer.Add(skipVersionButton);

            _versionInfoContainer.Add(buttonContainer);
        }

        private void AddCheckForUpdatesToggle()
        {
            var toggleContainer = new VisualElement();
            toggleContainer.AddToClassList("updater-info-container-toggle");
            var checkForUpdatesToggle = new Toggle() { text = "Check for Updates", value = ASToolsPreferences.Instance.CheckForUpdates };
            checkForUpdatesToggle.RegisterValueChangedCallback(OnCheckForUpdatesToggleChanged);

            toggleContainer.Add(checkForUpdatesToggle);
            _versionInfoContainer.Add(toggleContainer);
        }

        private void OnCheckForUpdatesToggleChanged(ChangeEvent<bool> evt)
        {
            ASToolsPreferences.Instance.CheckForUpdates = evt.newValue;
            ASToolsPreferences.Instance.Save();
        }

        private void SetupFailInfo()
        {
            var failContainer = new VisualElement();
            failContainer.AddToClassList("updater-fail-container");

            var failImage = new Image();
            var failDescription = new Label("Asset Store Publishing Tools could not retrieve information about the latest version.");

            failContainer.Add(failImage);
            failContainer.Add(failDescription);

            rootVisualElement.Add(failContainer);
        }

        private void LoadingSpinLoop()
        {
            var currentTimeSinceStartup = EditorApplication.timeSinceStartup;
            var deltaTime = EditorApplication.timeSinceStartup - _lastTimeSinceStartup;
            _lastTimeSinceStartup = currentTimeSinceStartup;

            _timeSinceLoadingImageChange += deltaTime;
            if (_timeSinceLoadingImageChange < 0.075)
                return;

            _timeSinceLoadingImageChange = 0;

            _loadingImage.image = EditorGUIUtility.IconContent($"WaitSpin{_loadingImageIndex++:00}").image;
            if (_loadingImageIndex > 11)
                _loadingImageIndex = 0;
        }

        private void OnDestroy()
        {
            EditorApplication.update -= LoadingSpinLoop;
        }
    }
}