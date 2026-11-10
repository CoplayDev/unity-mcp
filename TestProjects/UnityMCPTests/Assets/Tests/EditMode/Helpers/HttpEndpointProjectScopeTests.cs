using System.Collections.Generic;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services;
using NUnit.Framework;
using UnityEditor;

namespace MCPForUnityTests.Editor.Helpers
{
    /// <summary>
    /// The HTTP Local / HTTP Remote URLs are stored per project so two editors on the same
    /// machine can use different servers without overwriting each other's URL.
    /// </summary>
    public class HttpEndpointProjectScopeTests
    {
        private const string OtherProjectHash = "0123456789abcdef";

        private static string LocalKey => ProjectIdentityUtility.GetProjectScopedKey(EditorPrefKeys.HttpBaseUrl);
        private static string RemoteKey => ProjectIdentityUtility.GetProjectScopedKey(EditorPrefKeys.HttpRemoteBaseUrl);
        private static string OtherProjectLocalKey => $"{EditorPrefKeys.HttpBaseUrl}_{OtherProjectHash}";

        private readonly Dictionary<string, string> _savedPrefs = new Dictionary<string, string>();

        private static IEnumerable<string> TouchedKeys()
        {
            yield return EditorPrefKeys.HttpBaseUrl;
            yield return EditorPrefKeys.HttpRemoteBaseUrl;
            yield return LocalKey;
            yield return RemoteKey;
            yield return OtherProjectLocalKey;
        }

        [SetUp]
        public void SetUp()
        {
            _savedPrefs.Clear();
            foreach (string key in TouchedKeys())
            {
                _savedPrefs[key] = EditorPrefs.HasKey(key) ? EditorPrefs.GetString(key) : null;
                EditorPrefs.DeleteKey(key);
            }
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var pair in _savedPrefs)
            {
                if (pair.Value == null)
                    EditorPrefs.DeleteKey(pair.Key);
                else
                    EditorPrefs.SetString(pair.Key, pair.Value);
            }
            EditorConfigurationCache.Instance.Refresh();
        }

        [Test]
        public void ProjectScopedKey_IsBaseKeyPlusProjectHash()
        {
            Assert.AreEqual(
                $"{EditorPrefKeys.HttpBaseUrl}_{ProjectIdentityUtility.GetProjectHash()}",
                LocalKey);
        }

        [Test]
        public void GetLocalBaseUrl_NothingStored_ReturnsDefault()
        {
            Assert.AreEqual("http://127.0.0.1:8080", HttpEndpointUtility.GetLocalBaseUrl());
        }

        [Test]
        public void GetLocalBaseUrl_NoProjectValue_FallsBackToMachineWideValue()
        {
            EditorPrefs.SetString(EditorPrefKeys.HttpBaseUrl, "http://127.0.0.1:9001");

            Assert.AreEqual("http://127.0.0.1:9001", HttpEndpointUtility.GetLocalBaseUrl());
        }

        [Test]
        public void SaveLocalBaseUrl_WritesProjectKeyOnly()
        {
            EditorPrefs.SetString(EditorPrefKeys.HttpBaseUrl, "http://127.0.0.1:9001");

            HttpEndpointUtility.SaveLocalBaseUrl("http://127.0.0.1:9002");

            Assert.AreEqual("http://127.0.0.1:9002", EditorPrefs.GetString(LocalKey));
            Assert.AreEqual("http://127.0.0.1:9001", EditorPrefs.GetString(EditorPrefKeys.HttpBaseUrl),
                "Machine-wide value must stay as-is for projects that never saved their own URL");
            Assert.AreEqual("http://127.0.0.1:9002", HttpEndpointUtility.GetLocalBaseUrl());
        }

        [Test]
        public void GetLocalBaseUrl_IgnoresOtherProjectsValue()
        {
            EditorPrefs.SetString(OtherProjectLocalKey, "http://127.0.0.1:9003");

            Assert.AreEqual("http://127.0.0.1:8080", HttpEndpointUtility.GetLocalBaseUrl());

            HttpEndpointUtility.SaveLocalBaseUrl("http://127.0.0.1:9002");

            Assert.AreEqual("http://127.0.0.1:9002", HttpEndpointUtility.GetLocalBaseUrl());
            Assert.AreEqual("http://127.0.0.1:9003", EditorPrefs.GetString(OtherProjectLocalKey),
                "Saving in this project must not change another project's URL");
        }

        [Test]
        public void SaveRemoteBaseUrl_WritesProjectKeyOnly()
        {
            EditorPrefs.SetString(EditorPrefKeys.HttpRemoteBaseUrl, "https://a.example.com");

            Assert.AreEqual("https://a.example.com", HttpEndpointUtility.GetRemoteBaseUrl());

            HttpEndpointUtility.SaveRemoteBaseUrl("b.example.com");

            Assert.AreEqual("https://b.example.com", HttpEndpointUtility.GetRemoteBaseUrl());
            Assert.AreEqual("https://a.example.com", EditorPrefs.GetString(EditorPrefKeys.HttpRemoteBaseUrl));
        }

        [Test]
        public void SaveRemoteBaseUrl_Empty_ClearsThisProjectWithoutFallingBack()
        {
            EditorPrefs.SetString(EditorPrefKeys.HttpRemoteBaseUrl, "https://a.example.com");

            HttpEndpointUtility.SaveRemoteBaseUrl(string.Empty);

            Assert.AreEqual(string.Empty, HttpEndpointUtility.GetRemoteBaseUrl());
            Assert.AreEqual("https://a.example.com", EditorPrefs.GetString(EditorPrefKeys.HttpRemoteBaseUrl));
        }

        [Test]
        public void ConfigurationCache_ReadsAndWritesProjectValue()
        {
            EditorPrefs.SetString(EditorPrefKeys.HttpBaseUrl, "http://127.0.0.1:9001");
            HttpEndpointUtility.SaveLocalBaseUrl("http://127.0.0.1:9002");
            EditorConfigurationCache.Instance.Refresh();

            Assert.AreEqual("http://127.0.0.1:9002", EditorConfigurationCache.Instance.HttpBaseUrl);

            EditorConfigurationCache.Instance.SetHttpRemoteBaseUrl("https://c.example.com");

            Assert.AreEqual("https://c.example.com", EditorPrefs.GetString(RemoteKey));
            Assert.IsFalse(EditorPrefs.HasKey(EditorPrefKeys.HttpRemoteBaseUrl));
        }
    }
}
