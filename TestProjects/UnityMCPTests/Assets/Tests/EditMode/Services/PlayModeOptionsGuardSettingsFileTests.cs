using System.IO;
using MCPForUnity.Editor.Services;
using NUnit.Framework;
using UnityEditor;

namespace MCPForUnityTests.Editor.Services
{
    [TestFixture]
    public class PlayModeOptionsGuardSettingsFileTests
    {
        private const string SettingsPath = "ProjectSettings/EditorSettings.asset";
        private const string GuardPrefix = "MCPForUnity.PlayModeOptions.";
        private const string MarkerPath = "Library/MCPPlayModeOptionsBackup.txt";

        private bool _originalEnabled;
        private EnterPlayModeOptions _originalOptions;
        private byte[] _settingsFile;
        private bool _guardPending;
        private bool _guardEnabled;
        private int _guardOptions;
        private byte[] _marker;

        private static string Settings(int enabled, int options, string newline = "\n") =>
            "%YAML 1.1" + newline +
            "--- !u!159 &1" + newline +
            "EditorSettings:" + newline +
            "  m_EnterPlayModeOptionsEnabled: " + enabled + newline +
            "  m_EnterPlayModeOptions: " + options + newline +
            "  m_GameObjectNamingDigits: 1" + newline;

        [SetUp]
        public void SetUp()
        {
            _originalEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            _originalOptions = EditorSettings.enterPlayModeOptions;
            _settingsFile = File.Exists(SettingsPath) ? File.ReadAllBytes(SettingsPath) : null;
            _guardPending = SessionState.GetBool(GuardPrefix + "PendingRestore", false);
            _guardEnabled = SessionState.GetBool(GuardPrefix + "OriginalEnabled", false);
            _guardOptions = SessionState.GetInt(GuardPrefix + "OriginalOptions", 0);
            _marker = File.Exists(MarkerPath) ? File.ReadAllBytes(MarkerPath) : null;
        }

        [TearDown]
        public void TearDown()
        {
            EditorSettings.enterPlayModeOptions = _originalOptions;
            EditorSettings.enterPlayModeOptionsEnabled = _originalEnabled;
            if (_settingsFile != null) File.WriteAllBytes(SettingsPath, _settingsFile);
            SessionState.SetBool(GuardPrefix + "PendingRestore", _guardPending);
            SessionState.SetBool(GuardPrefix + "OriginalEnabled", _guardEnabled);
            SessionState.SetInt(GuardPrefix + "OriginalOptions", _guardOptions);
            if (_marker == null)
            {
                if (File.Exists(MarkerPath)) File.Delete(MarkerPath);
            }
            else File.WriteAllBytes(MarkerPath, _marker);
        }

        [Test]
        public void RevertTemporaryValues_RestoresBothTemporaryValues()
        {
            string reverted = PlayModeOptionsGuard.RevertTemporaryValues(Settings(1, 1), false, EnterPlayModeOptions.None);

            Assert.AreEqual(Settings(0, 0), reverted);
        }

        [Test]
        public void RevertTemporaryValues_WhenOnlyDomainReloadWasAdded_RestoresOptionsOnly()
        {
            string reverted = PlayModeOptionsGuard.RevertTemporaryValues(
                Settings(1, 3), true, EnterPlayModeOptions.DisableSceneReload);

            Assert.AreEqual(Settings(1, 2), reverted);
        }

        [Test]
        public void RevertTemporaryValues_LeavesValuesItDidNotSet()
        {
            string settings = Settings(0, 2);

            Assert.AreEqual(settings, PlayModeOptionsGuard.RevertTemporaryValues(settings, false, EnterPlayModeOptions.None));
        }

        [Test]
        public void RevertTemporaryValues_KeepsLineEndings()
        {
            string reverted = PlayModeOptionsGuard.RevertTemporaryValues(Settings(1, 1, "\r\n"), false, EnterPlayModeOptions.None);

            Assert.AreEqual(Settings(0, 0, "\r\n"), reverted);
        }

        [Test]
        public void Restore_AfterAssetsWereSavedWithTemporaryValues_WritesOriginalsToDisk()
        {
            PlayModeOptionsGuard.Save(false, EnterPlayModeOptions.None);
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            AssetDatabase.SaveAssets();
            StringAssert.Contains("m_EnterPlayModeOptionsEnabled: 1", File.ReadAllText(SettingsPath));

            PlayModeOptionsGuard.Restore();

            string onDisk = File.ReadAllText(SettingsPath);
            StringAssert.Contains("m_EnterPlayModeOptionsEnabled: 0", onDisk);
            StringAssert.Contains("m_EnterPlayModeOptions: 0", onDisk);
            Assert.IsFalse(EditorSettings.enterPlayModeOptionsEnabled);
            Assert.AreEqual(EnterPlayModeOptions.None, EditorSettings.enterPlayModeOptions);
        }
    }
}
