using AssetStoreTools.Api.Models;
using AssetStoreTools.Uploader.Services;
using AssetStoreTools.Uploader.Services.Analytics;
using AssetStoreTools.Uploader.Services.Api;
using AssetStoreTools.Uploader.UI.Elements;
using AssetStoreTools.Uploader.UI.Views;
using AssetStoreTools.Utility;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AssetStoreTools.Uploader
{
    internal class UploaderWindow : AssetStoreToolsWindow
    {
        private const string DebugPhrase = "debug";

        // Services
        private ICachingService _cachingService;
        private IAuthenticationService _authenticationService;
        private IPackageDownloadingService _packageDownloadingService;
        private IPackageUploadingService _packageUploadingService;
        private IAnalyticsService _analyticsService;
        private IPackageFactoryService _packageFactoryService;

        // Data
        private bool _isQuitting = false;

        // UI
        private VisualElement _uploaderWindowRoot;
        private ScrollView _rootScrollView;

        private LoginView _loginView;
        private PackageListView _packageListView;
        private AccountToolbar _accountToolbar;

        protected override string WindowTitle => "Asset Store Uploader";

        protected override void Init()
        {
            RegisterServices();
            SetupWindow();
        }

        private void RegisterServices()
        {
            var uploaderServiceProvider = UploaderServiceProvider.Instance;
            _analyticsService = uploaderServiceProvider.GetService<IAnalyticsService>();
            _cachingService = uploaderServiceProvider.GetService<ICachingService>();
            _authenticationService = uploaderServiceProvider.GetService<IAuthenticationService>();
            _packageDownloadingService = uploaderServiceProvider.GetService<IPackageDownloadingService>();
            _packageUploadingService = uploaderServiceProvider.GetService<IPackageUploadingService>();
            _packageFactoryService = uploaderServiceProvider.GetService<IPackageFactoryService>();
        }

        private void SetupWindow()
        {
            minSize = new Vector2(400, 430);
            this.SetAntiAliasing(4);
            rootVisualElement.styleSheets.Add(StyleSelector.UploaderWindow.UploaderWindowStyle);
            rootVisualElement.styleSheets.Add(StyleSelector.UploaderWindow.UploaderWindowTheme);

            if (_cachingService.GetCachedUploaderWindow(out _uploaderWindowRoot))
            {
                rootVisualElement.Add(_uploaderWindowRoot);
                return;
            }

            _uploaderWindowRoot = new VisualElement();
            _uploaderWindowRoot.AddToClassList("uploader-window-root");
            rootVisualElement.Add(_uploaderWindowRoot);

            _rootScrollView = new ScrollView();
            _rootScrollView.AddToClassList("uploader-window-root-scrollview");
            _uploaderWindowRoot.Add(_rootScrollView);

            CreateLoginView();
            CreatePackageListView();
            CreateAccountToolbar();
            EditorApplication.wantsToQuit += OnWantsToQuit;

            _cachingService.CacheUploaderWindow(_uploaderWindowRoot);

            PerformAuthentication();
        }

        private void CreateLoginView()
        {
            _loginView = new LoginView(_authenticationService);
            _loginView.OnAuthenticated += OnAuthenticationSuccess;
            _rootScrollView.Add(_loginView);
        }

        private void CreatePackageListView()
        {
            _packageListView = new PackageListView(_packageDownloadingService, _packageFactoryService);
            _packageListView.OnInitializeError += PackageViewInitializationError;
            _rootScrollView.Add(_packageListView);
        }

        private void CreateAccountToolbar()
        {
            _accountToolbar = new AccountToolbar();
            _accountToolbar.OnRefresh += RefreshPackages;
            _accountToolbar.OnLogout += LogOut;
            _uploaderWindowRoot.Add(_accountToolbar);
        }

        private void PerformAuthentication()
        {
            ShowAuthenticationView();
            _loginView.LoginWithSessionToken();
        }

        private async void OnAuthenticationSuccess(User user)
        {
            _accountToolbar.SetUser(user);
            _accountToolbar.DisableButtons();

            ShowAccountPackageView();
            await _packageListView.LoadPackages(true);

            _accountToolbar.EnableButtons();
        }

        private async Task RefreshPackages()
        {
            _packageUploadingService.StopAllUploadinng();
            _packageDownloadingService.StopDownloading();

            await _packageListView.LoadPackages(false);
        }

        private void LogOut()
        {
            _packageUploadingService.StopAllUploadinng();
            _packageDownloadingService.StopDownloading();

            _authenticationService.Deauthenticate();
            _packageDownloadingService.ClearPackageData();

            _accountToolbar.SetUser(null);
            ShowAuthenticationView();
        }

        private void PackageViewInitializationError(Exception e)
        {
            _loginView.DisplayError(e.Message);
            LogOut();
        }

        private void ShowAuthenticationView()
        {
            HideElement(_accountToolbar);
            HideElement(_packageListView);
            ShowElement(_loginView);
        }

        private void ShowAccountPackageView()
        {
            HideElement(_loginView);
            ShowElement(_accountToolbar);
            ShowElement(_packageListView);
        }

        private void ShowElement(params VisualElement[] elements)
        {
            foreach (var e in elements)
                e.style.display = DisplayStyle.Flex;
        }

        private void HideElement(params VisualElement[] elements)
        {
            foreach (var e in elements)
                e.style.display = DisplayStyle.None;
        }

        private void OnDestroy()
        {
            if (!_isQuitting && _packageUploadingService.IsUploading)
            {
                EditorUtility.DisplayDialog("Notice", "Assets are still being uploaded to the Asset Store. " +
                    "If you wish to check on the progress, please re-open the Asset Store Uploader window", "OK");
            }
        }

        private bool OnWantsToQuit()
        {
            if (!_packageUploadingService.IsUploading)
                return true;

            _isQuitting = EditorUtility.DisplayDialog("Notice", "Assets are still being uploaded to the Asset Store. " +
                    "Would you still like to close Unity Editor?", "Yes", "No");

            return _isQuitting;
        }

        #region Debug Utility

        private readonly List<char> _debugBuffer = new List<char>();

        private void OnGUI()
        {
            CheckForDebugMode();
        }

        private void CheckForDebugMode()
        {
            Event e = Event.current;

            if (e.type != EventType.KeyDown || e.keyCode == KeyCode.None)
                return;

            _debugBuffer.Add(e.keyCode.ToString().ToLower()[0]);
            if (_debugBuffer.Count > DebugPhrase.Length)
                _debugBuffer.RemoveAt(0);

            if (string.Join(string.Empty, _debugBuffer.ToArray()) != DebugPhrase)
                return;

            ASDebug.DebugModeEnabled = !ASDebug.DebugModeEnabled;
            ASDebug.Log($"DEBUG MODE ENABLED: {ASDebug.DebugModeEnabled}");
            _debugBuffer.Clear();
        }

        #endregion
    }
}