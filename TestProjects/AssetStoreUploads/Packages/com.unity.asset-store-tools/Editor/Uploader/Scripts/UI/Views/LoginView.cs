using AssetStoreTools.Api.Models;
using AssetStoreTools.Uploader.Services.Api;
using AssetStoreTools.Utility;
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AssetStoreTools.Uploader.UI.Views
{
    internal class LoginView : VisualElement
    {
        // Data
        private IAuthenticationService _authenticationService;
        private double _cloudLoginRefreshTime = 1d;
        private double _lastRefreshTime;

        // UI
        private Button _cloudLoginButton;
        private Label _cloudLoginLabel;

        private Box _errorBox;
        private Label _errorLabel;

        private TextField _emailField;
        private TextField _passwordField;
        private Button _credentialsLoginButton;

        public event Action<User> OnAuthenticated;

        public LoginView(IAuthenticationService authenticationService)
        {
            _authenticationService = authenticationService;
            Create();
        }

        public void Create()
        {
            styleSheets.Add(StyleSelector.UploaderWindow.LoginViewStyle);
            styleSheets.Add(StyleSelector.UploaderWindow.LoginViewTheme);

            CreateAssetStoreLogo();
            CreateCloudLogin();
            CreateErrorBox();
            CreateCredentialsLogin();
        }

        private void CreateAssetStoreLogo()
        {
            // Asset Store logo
            Image assetStoreLogo = new Image { name = "AssetStoreLogo" };
            assetStoreLogo.AddToClassList("asset-store-logo");

            Add(assetStoreLogo);
        }

        private void CreateCloudLogin()
        {
            VisualElement cloudLogin = new VisualElement { name = "CloudLogin" };

            _cloudLoginButton = new Button(LoginWithCloudToken) { name = "LoginButtonCloud" };
            _cloudLoginButton.AddToClassList("cloud-button-login");
            _cloudLoginButton.SetEnabled(false);

            _cloudLoginLabel = new Label { text = "Cloud login unavailable" };
            _cloudLoginLabel.AddToClassList("cloud-button-login-label");

            Label orLabel = new Label { text = "or" };
            orLabel.AddToClassList("cloud-label-or");

            _cloudLoginButton.Add(_cloudLoginLabel);

            cloudLogin.Add(_cloudLoginButton);
            cloudLogin.Add(orLabel);

            UpdateCloudLoginButton();
            EditorApplication.update += UpdateCloudLoginButton;
            Add(cloudLogin);
        }

        private void CreateErrorBox()
        {
            _errorBox = new Box() { name = "LoginErrorBox" };
            _errorBox.AddToClassList("error-container");
            _errorBox.style.display = DisplayStyle.None;

            var errorImage = new Image();
            _errorBox.Add(errorImage);

            _errorLabel = new Label();
            _errorBox.Add(_errorLabel);

            Add(_errorBox);
        }

        public void DisplayError(string message)
        {
            if (string.IsNullOrEmpty(message))
                return;

            _errorLabel.text = message;
            Debug.LogError(message);

            _errorBox.style.display = DisplayStyle.Flex;
        }

        private void ClearError()
        {
            _errorLabel.text = string.Empty;
            _errorBox.style.display = DisplayStyle.None;
        }

        private void CreateCredentialsLogin()
        {
            // Manual login
            VisualElement manualLoginBox = new VisualElement { name = "ManualLoginBox" };
            manualLoginBox.AddToClassList("credentials-container");

            // Email input box
            VisualElement inputBoxEmail = new VisualElement();
            inputBoxEmail.AddToClassList("credentials-input-container");

            Label emailTitle = new Label { text = "Email" };
            _emailField = new TextField();

            inputBoxEmail.Add(emailTitle);
            inputBoxEmail.Add(_emailField);

            manualLoginBox.Add(inputBoxEmail);

            // Password input box
            VisualElement inputBoxPassword = new VisualElement();
            inputBoxPassword.AddToClassList("credentials-input-container");

            Label passwordTitle = new Label { text = "Password" };
            _passwordField = new TextField { isPasswordField = true };

            inputBoxPassword.Add(passwordTitle);
            inputBoxPassword.Add(_passwordField);

            manualLoginBox.Add(inputBoxPassword);

            // Login button
            _credentialsLoginButton = new Button(LoginWithCredentials) { name = "LoginButtonCredentials" };
            _credentialsLoginButton.AddToClassList("credentials-button-login");

            Label loginDescriptionCredentials = new Label { text = "Login" };
            loginDescriptionCredentials.AddToClassList("credentials-button-login-label");

            _credentialsLoginButton.Add(loginDescriptionCredentials);

            manualLoginBox.Add(_credentialsLoginButton);

            Add(manualLoginBox);

            // Credentials login helpers
            VisualElement helperBox = new VisualElement { name = "HelperBox" };
            helperBox.AddToClassList("help-section-container");

            Button createAccountButton = new Button { name = "CreateAccountButton", text = "Create Publisher ID" };
            Button forgotPasswordButton = new Button { name = "ForgotPasswordButton", text = "Reset Password" };

            createAccountButton.AddToClassList("help-section-hyperlink-button");
            forgotPasswordButton.AddToClassList("help-section-hyperlink-button");

            createAccountButton.clicked += () => Application.OpenURL(Constants.Uploader.AccountRegistrationUrl);
            forgotPasswordButton.clicked += () => Application.OpenURL(Constants.Uploader.AccountForgottenPasswordUrl);

            helperBox.Add(createAccountButton);
            helperBox.Add(forgotPasswordButton);

            Add(helperBox);
        }

        public async void LoginWithSessionToken()
        {
            ASDebug.Log("Authenticating with session token...");
            ClearError();
            SetEnabled(false);

            var result = await _authenticationService.AuthenticateWithSessionToken();
            if (!result.Success)
            {
                // Session authentication fail does not display errors in the UI
                ASDebug.Log("No existing session was found");
                SetEnabled(true);
                return;
            }

            OnLoginSuccess(result.User);
        }

        private async void LoginWithCloudToken()
        {
            ASDebug.Log("Authenticating with cloud token...");
            ClearError();
            SetEnabled(false);

            var result = await _authenticationService.AuthenticateWithCloudToken();
            if (!result.Success)
            {
                OnLoginFail(result.Exception.Message);
                return;
            }

            OnLoginSuccess(result.User);
        }

        private async void LoginWithCredentials()
        {
            ASDebug.Log("Authenticating with credentials...");
            ClearError();
            var isValid = IsLoginDataValid(_emailField.text, _passwordField.value);
            SetEnabled(!isValid);

            if (!isValid)
                return;

            var result = await _authenticationService.AuthenticateWithCredentials(_emailField.text, _passwordField.text);
            if (result.Success)
                OnLoginSuccess(result.User);
            else
                OnLoginFail(result.Exception.Message);
        }

        private bool IsLoginDataValid(string email, string password)
        {
            if (string.IsNullOrEmpty(email))
            {
                DisplayError("Email field cannot be empty.");
                return false;
            }

            if (string.IsNullOrEmpty(password))
            {
                DisplayError("Password field cannot be empty.");
                return false;
            }

            return true;
        }

        private void UpdateCloudLoginButton()
        {
            if (_cloudLoginLabel == null)
                return;

            if (_lastRefreshTime + _cloudLoginRefreshTime > EditorApplication.timeSinceStartup)
                return;

            _lastRefreshTime = EditorApplication.timeSinceStartup;

            // Cloud login
            if (_authenticationService.CloudAuthenticationAvailable(out var username, out var _))
            {
                _cloudLoginLabel.text = $"Login as {username}";
                _cloudLoginButton.SetEnabled(true);
            }
            else
            {
                _cloudLoginLabel.text = "Cloud login unavailable";
                _cloudLoginButton.SetEnabled(false);
            }
        }

        private void OnLoginSuccess(User user)
        {
            ASDebug.Log($"Successfully authenticated as {user.Username}\n{user}");

            _emailField.value = string.Empty;
            _passwordField.value = string.Empty;

            OnAuthenticated?.Invoke(user);
            SetEnabled(true);
        }

        private void OnLoginFail(string message)
        {
            ASDebug.LogError($"Authentication failed: {message}");
            DisplayError(message);
            SetEnabled(true);
        }
    }
}