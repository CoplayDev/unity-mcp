using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Services;
using NUnit.Framework;
using UnityEditor;

namespace MCPForUnityTests.Editor
{
    /// <summary>
    /// Synchronous configuration tests exercise user preferences independently of a resident
    /// harness's stdio pin. Restore the pin before returning to the Editor update loop.
    /// </summary>
    public abstract class TransportPreferenceTestBase
    {
        private bool _hadSessionPin;
        private bool _sessionPin;
        private bool _hadHttpPreference;
        private bool _httpPreference;

        [SetUp]
        public void SuspendSessionTransportOverride()
        {
            string key = EditorConfigurationCache.SessionKeyForceStdio;
            _sessionPin = SessionState.GetBool(key, false);
            _hadSessionPin = _sessionPin == SessionState.GetBool(key, true);
            _hadHttpPreference = EditorPrefs.HasKey(EditorPrefKeys.UseHttpTransport);
            _httpPreference = EditorPrefs.GetBool(EditorPrefKeys.UseHttpTransport, true);

            // Do not call UnpinStdioForSession: fixture setup must not emit a transport change.
            SessionState.EraseBool(key);
            EditorConfigurationCache.Instance.Refresh();
        }

        [TearDown]
        public void RestoreSessionTransportOverride()
        {
            if (_hadHttpPreference)
                EditorPrefs.SetBool(EditorPrefKeys.UseHttpTransport, _httpPreference);
            else
                EditorPrefs.DeleteKey(EditorPrefKeys.UseHttpTransport);

            string key = EditorConfigurationCache.SessionKeyForceStdio;
            if (_hadSessionPin)
                SessionState.SetBool(key, _sessionPin);
            else
                SessionState.EraseBool(key);
            EditorConfigurationCache.Instance.Refresh();
        }
    }
}
