using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MCPForUnity.Editor.Clients;
using MCPForUnity.Editor.Clients.Configurators;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Models;
using MCPForUnity.Editor.Services;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;

namespace MCPForUnityTests.Editor.Clients
{
    [TestFixture]
    public class WorkBuddyConfiguratorTests : TransportPreferenceTestBase
    {
        private string _tempDir;
        private readonly Dictionary<string, string> _savedPrefs = new();
        private bool _hadLock;
        private bool _locked;

        [SetUp]
        public void SetUp()
        {
            foreach (string key in new[] { EditorPrefKeys.HttpTransportScope, EditorPrefKeys.HttpBaseUrl })
                _savedPrefs[key] = EditorPrefs.HasKey(key) ? EditorPrefs.GetString(key) : null;
            _hadLock = EditorPrefs.HasKey(EditorPrefKeys.LockCursorConfig);
            _locked = EditorPrefs.GetBool(EditorPrefKeys.LockCursorConfig, false);
            EditorPrefs.SetString(EditorPrefKeys.HttpTransportScope, "local");
            EditorPrefs.SetString(EditorPrefKeys.HttpBaseUrl, "http://localhost:8080");
            EditorPrefs.SetBool(EditorPrefKeys.LockCursorConfig, false);
            EditorConfigurationCache.Instance.Refresh();
            _tempDir = Path.Combine(Path.GetTempPath(), "UnityMCPTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var pref in _savedPrefs)
            {
                if (pref.Value == null) EditorPrefs.DeleteKey(pref.Key);
                else EditorPrefs.SetString(pref.Key, pref.Value);
            }
            _savedPrefs.Clear();
            if (_hadLock) EditorPrefs.SetBool(EditorPrefKeys.LockCursorConfig, _locked);
            else EditorPrefs.DeleteKey(EditorPrefKeys.LockCursorConfig);
            EditorConfigurationCache.Instance.Refresh();
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
        }

        [Test]
        public void Registry_DiscoversWorkBuddyAsASeparateClient()
        {
            var workBuddy = McpClientRegistry.All.Single(c => c.Id == "workbuddy");
            Assert.IsInstanceOf<WorkBuddyConfigurator>(workBuddy);
            Assert.AreEqual("WorkBuddy", workBuddy.DisplayName);
            Assert.AreNotEqual(new CodeBuddyCliConfigurator().GetConfigPath(), workBuddy.GetConfigPath());
        }

        [Test]
        public void ConfigPath_UsesWorkBuddyUserConfigOnEveryPlatform()
        {
            var configurator = new WorkBuddyConfigurator();
            string expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".workbuddy", "mcp.json");
            Assert.AreEqual(expected, configurator.Client.windowsConfigPath);
            Assert.AreEqual(expected, configurator.Client.macConfigPath);
            Assert.AreEqual(expected, configurator.Client.linuxConfigPath);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Configure_RoundTripsTransportAndPreservesOtherSettings(bool http)
        {
            EditorConfigurationCache.Instance.SetUseHttpTransport(http);
            var configurator = new TempWorkBuddyConfigurator(Path.Combine(_tempDir, "mcp.json"));
            File.WriteAllText(configurator.GetConfigPath(), "{\"theme\":\"dark\",\"mcpServers\":{\"other\":{\"command\":\"other-server\"}}}");

            configurator.Configure();
            string firstWrite = File.ReadAllText(configurator.GetConfigPath());
            configurator.Configure();
            Assert.AreEqual(firstWrite, File.ReadAllText(configurator.GetConfigPath()), "Repeated configuration must be idempotent");
            Assert.AreEqual(McpStatus.Configured, configurator.CheckStatus(attemptAutoRewrite: false));
            Assert.AreEqual(http ? ConfiguredTransport.Http : ConfiguredTransport.Stdio, configurator.ConfiguredTransport);

            var root = JObject.Parse(firstWrite);
            Assert.AreEqual("dark", (string)root["theme"]);
            Assert.AreEqual("other-server", (string)root["mcpServers"]?["other"]?["command"]);
            var unity = (JObject)root["mcpServers"]?["unityMCP"];
            Assert.IsNotNull(unity);
            Assert.AreEqual(http ? "http" : "stdio", (string)unity["type"]);
            if (http)
            {
                Assert.AreEqual("http://127.0.0.1:8080/mcp", (string)unity["url"]);
                Assert.IsNull(unity["command"]);
            }
            else
            {
                Assert.IsNotEmpty((string)unity["command"]);
                CollectionAssert.Contains(unity["args"].ToObject<string[]>(), "stdio");
                Assert.IsNull(unity["url"]);
            }

            configurator.Unregister();
            root = JObject.Parse(File.ReadAllText(configurator.GetConfigPath()));
            Assert.IsNull(root["mcpServers"]?["unityMCP"]);
            Assert.AreEqual("other-server", (string)root["mcpServers"]?["other"]?["command"]);
            Assert.AreEqual("dark", (string)root["theme"]);
            Assert.AreEqual(McpStatus.NotConfigured, configurator.Status);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Configure_InvalidJsonLeavesExistingFileUnchanged(bool http)
        {
            EditorConfigurationCache.Instance.SetUseHttpTransport(http);
            var configurator = new TempWorkBuddyConfigurator(Path.Combine(_tempDir, "mcp.json"));
            const string incompleteJson = "{\"theme\":\"dark\",\"mcpServers\":{\"other\":{\"command\":\"other-server\"}}";
            File.WriteAllText(configurator.GetConfigPath(), incompleteJson);
            byte[] original = File.ReadAllBytes(configurator.GetConfigPath());

            var error = Assert.Throws<InvalidOperationException>(() => configurator.Configure());

            StringAssert.Contains("Fix its JSON", error.Message);
            CollectionAssert.AreEqual(original, File.ReadAllBytes(configurator.GetConfigPath()));
            Assert.IsFalse(File.Exists(configurator.GetConfigPath() + ".tmp"));
            Assert.IsFalse(File.Exists(configurator.GetConfigPath() + ".backup"));
        }

        [Test]
        public void Configure_UnreadableExistingFileIsLeftUnchanged()
        {
            var configurator = new TempWorkBuddyConfigurator(Path.Combine(_tempDir, "mcp.json"));
            const string existingJson = "{\"theme\":\"dark\",\"mcpServers\":{\"other\":{\"command\":\"other-server\"}}}";
            File.WriteAllText(configurator.GetConfigPath(), existingJson);
            byte[] original = File.ReadAllBytes(configurator.GetConfigPath());

            using (new FileStream(configurator.GetConfigPath(), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var error = Assert.Throws<InvalidOperationException>(() => configurator.Configure());
                StringAssert.Contains("Could not read", error.Message);
            }

            CollectionAssert.AreEqual(original, File.ReadAllBytes(configurator.GetConfigPath()));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Configure_PreservesExistingTemporaryAndBackupArtifacts(bool temporaryIsDirectory)
        {
            var configurator = new TempWorkBuddyConfigurator(Path.Combine(_tempDir, "mcp.json"));
            string path = configurator.GetConfigPath();
            File.WriteAllText(path, "{\"theme\":\"current\",\"mcpServers\":{\"other\":{\"command\":\"other-server\"}}}");
            const string temporaryContents = "user temporary contents";
            if (temporaryIsDirectory) Directory.CreateDirectory(path + ".tmp");
            else File.WriteAllText(path + ".tmp", temporaryContents);
            File.WriteAllText(path + ".backup", "{\"theme\":\"stale backup\"}");
            byte[] originalBackup = File.ReadAllBytes(path + ".backup");
            byte[] originalTemporary = temporaryIsDirectory ? null : File.ReadAllBytes(path + ".tmp");
            string[] originalEntries = Directory.GetFileSystemEntries(_tempDir);

            configurator.Configure();

            var root = JObject.Parse(File.ReadAllText(path));
            Assert.AreEqual("current", (string)root["theme"]);
            Assert.AreEqual("other-server", (string)root["mcpServers"]["other"]["command"]);
            Assert.IsNotNull(root["mcpServers"]["unityMCP"]);
            CollectionAssert.AreEqual(originalBackup, File.ReadAllBytes(path + ".backup"));
            if (temporaryIsDirectory) Assert.IsTrue(Directory.Exists(path + ".tmp"));
            else CollectionAssert.AreEqual(originalTemporary, File.ReadAllBytes(path + ".tmp"));
            CollectionAssert.AreEquivalent(originalEntries, Directory.GetFileSystemEntries(_tempDir));
        }

        [Test]
        public void AtomicWrite_FailurePreservesExistingBackupAndCleansOwnTemporaryFiles()
        {
            string path = Path.Combine(_tempDir, "destination-directory");
            Directory.CreateDirectory(path);
            string child = Path.Combine(path, "keep.txt");
            File.WriteAllText(child, "keep directory contents");
            File.WriteAllText(path + ".backup", "user backup contents");
            byte[] originalBackup = File.ReadAllBytes(path + ".backup");
            string[] originalEntries = Directory.GetFileSystemEntries(_tempDir);

            Assert.Throws<Exception>(() => McpConfigurationHelper.WriteAtomicFile(path, "replacement"));

            Assert.AreEqual("keep directory contents", File.ReadAllText(child));
            CollectionAssert.AreEqual(originalBackup, File.ReadAllBytes(path + ".backup"));
            CollectionAssert.AreEquivalent(originalEntries, Directory.GetFileSystemEntries(_tempDir));
        }

        [Test]
        public void IsInstalled_RequiresTheWorkBuddyDirectory()
        {
            var configurator = new TempWorkBuddyConfigurator(Path.Combine(_tempDir, ".workbuddy", "mcp.json"));
            Assert.IsFalse(configurator.IsInstalled);
            Directory.CreateDirectory(Path.GetDirectoryName(configurator.GetConfigPath()));
            Assert.IsTrue(configurator.IsInstalled);
            Assert.IsFalse(File.Exists(configurator.GetConfigPath()));
        }

        private sealed class TempWorkBuddyConfigurator : WorkBuddyConfigurator
        {
            private readonly string _path;
            public TempWorkBuddyConfigurator(string path) => _path = path;
            public override string GetConfigPath() => _path;
        }
    }
}
