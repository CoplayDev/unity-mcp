using System;
using MCPForUnity.Editor.Helpers;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MCPForUnity.Editor.Windows.Components.Auth
{
    /// <summary>
    /// Dedicated Auth section for configuring MCP HTTP/WebSocket auth.
    /// </summary>
    public class McpAuthSection
    {
        private Toggle authEnabledToggle;
        private TextField allowedIpsField;
        private TextField authTokenField;
        private Button copyTokenButton;
        private Button regenerateTokenButton;

        public event Action OnAuthChanged;

        public VisualElement Root { get; }

        public McpAuthSection(VisualElement root)
        {
            Root = root;
            CacheUI();
            Initialize();
            RegisterCallbacks();
        }

        private void CacheUI()
        {
            authEnabledToggle = Root.Q<Toggle>("auth-enabled-toggle");
            allowedIpsField = Root.Q<TextField>("allowed-ips-field");
            authTokenField = Root.Q<TextField>("auth-token-field");
            copyTokenButton = Root.Q<Button>("copy-token-button");
            regenerateTokenButton = Root.Q<Button>("regenerate-token-button");
        }

        private void Initialize()
        {
            if (authTokenField != null)
            {
                authTokenField.isPasswordField = true;
            }

            if (authEnabledToggle != null)
            {
                authEnabledToggle.value = AuthPreferencesUtility.GetAuthEnabled();
            }

            if (allowedIpsField != null)
            {
                allowedIpsField.value = AuthPreferencesUtility.GetAllowedIpsRaw();
            }

            EnsureTokenExistsIfNeeded();
            UpdateTokenField();
            UpdateEnabledState();
        }

        private void RegisterCallbacks()
        {
            if (authEnabledToggle != null)
            {
                authEnabledToggle.RegisterValueChangedCallback(evt =>
                {
                    AuthPreferencesUtility.SetAuthEnabled(evt.newValue);
                    if (evt.newValue)
                    {
                        EnsureTokenExistsIfNeeded();
                    }
                    UpdateEnabledState();
                    OnAuthChanged?.Invoke();
                });
            }

            if (allowedIpsField != null)
            {
                allowedIpsField.RegisterValueChangedCallback(evt =>
                {
                    AuthPreferencesUtility.SetAllowedIps(evt.newValue);
                    OnAuthChanged?.Invoke();
                });
            }

            if (copyTokenButton != null)
            {
                copyTokenButton.clicked += () =>
                {
                    string token = AuthPreferencesUtility.GetAuthToken();
                    if (!string.IsNullOrEmpty(token))
                    {
                        EditorGUIUtility.systemCopyBuffer = token;
                        McpLog.Info("Auth token copied to clipboard.");
                    }
                };
            }

            if (regenerateTokenButton != null)
            {
                regenerateTokenButton.clicked += () =>
                {
                    string newToken = AuthPreferencesUtility.GenerateNewToken();
                    AuthPreferencesUtility.SetAuthToken(newToken);
                    UpdateTokenField();
                    OnAuthChanged?.Invoke();
                };
            }
        }

        private void EnsureTokenExistsIfNeeded()
        {
            if (authEnabledToggle != null && authEnabledToggle.value)
            {
                var token = AuthPreferencesUtility.GetAuthToken();
                if (string.IsNullOrEmpty(token))
                {
                    AuthPreferencesUtility.SetAuthToken(AuthPreferencesUtility.GenerateNewToken());
                }
            }
        }

        private void UpdateTokenField()
        {
            if (authTokenField != null)
            {
                authTokenField.value = AuthPreferencesUtility.GetAuthToken();
            }
        }

        private void UpdateEnabledState()
        {
            bool enabled = authEnabledToggle != null && authEnabledToggle.value;
            allowedIpsField?.SetEnabled(enabled);
            authTokenField?.SetEnabled(enabled);
            copyTokenButton?.SetEnabled(enabled);
            regenerateTokenButton?.SetEnabled(enabled);
        }

        public void Refresh()
        {
            if (authEnabledToggle != null)
            {
                authEnabledToggle.value = AuthPreferencesUtility.GetAuthEnabled();
            }

            if (allowedIpsField != null)
            {
                allowedIpsField.value = AuthPreferencesUtility.GetAllowedIpsRaw();
            }

            UpdateTokenField();
            UpdateEnabledState();
        }
    }
}
