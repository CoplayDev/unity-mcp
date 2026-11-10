using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Windows.Components.AssetGen;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace MCPForUnityTests.Editor.Blender
{
    /// <summary>
    /// Drives the Blender Bridge panel built from the real UXML: the addon status line, the optional
    /// checkout note, the import button, and the automatic socket probe against a fake addon. Every
    /// Blender EditorPref is saved and restored, and the socket defaults to a closed local port so no
    /// test ever reaches a real Blender.
    /// </summary>
    public class McpBlenderBridgePanelTests
    {
        private const string UxmlPath = "Packages/com.coplaydev.unity-mcp/Editor/Windows/Components/AssetGen/McpAssetGenSection.uxml";
        private static readonly Color Green = new Color(0.4f, 0.8f, 0.4f);
        private static readonly Color Gray = new Color(0.7f, 0.7f, 0.7f);

        private string savedHost, savedFork, savedAddons;
        private int? savedPort;
        private string root;

        [SetUp]
        public void SetUp()
        {
            savedHost = EditorPrefs.HasKey(EditorPrefKeys.BlenderHost) ? EditorPrefs.GetString(EditorPrefKeys.BlenderHost) : null;
            savedPort = EditorPrefs.HasKey(EditorPrefKeys.BlenderPort) ? EditorPrefs.GetInt(EditorPrefKeys.BlenderPort) : (int?)null;
            savedFork = EditorPrefs.HasKey(EditorPrefKeys.BlenderForkPath) ? EditorPrefs.GetString(EditorPrefKeys.BlenderForkPath) : null;
            savedAddons = EditorPrefs.HasKey(EditorPrefKeys.BlenderAddonsDir) ? EditorPrefs.GetString(EditorPrefKeys.BlenderAddonsDir) : null;

            root = Path.Combine(Path.GetTempPath(), "blender-panel-test-" + Guid.NewGuid().ToString("N")).Replace('\\', '/');
            BlenderBridgePrefs.Host = "127.0.0.1";
            BlenderBridgePrefs.Port = ClosedPort();
            BlenderBridgePrefs.ForkPath = string.Empty;
            BlenderBridgePrefs.AddonsDirOverride = root + "/missing/scripts/addons";
        }

        [TearDown]
        public void TearDown()
        {
            Restore(EditorPrefKeys.BlenderHost, savedHost);
            Restore(EditorPrefKeys.BlenderForkPath, savedFork);
            Restore(EditorPrefKeys.BlenderAddonsDir, savedAddons);
            if (savedPort.HasValue) EditorPrefs.SetInt(EditorPrefKeys.BlenderPort, savedPort.Value);
            else EditorPrefs.DeleteKey(EditorPrefKeys.BlenderPort);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        private static void Restore(string key, string value)
        {
            if (value == null) EditorPrefs.DeleteKey(key);
            else EditorPrefs.SetString(key, value);
        }

        /// <summary>A local port nothing listens on, so a probe fails fast instead of finding a real Blender.</summary>
        private static int ClosedPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        /// <summary>Clones the Asset Gen UXML and builds the panel controller on its Blender block.</summary>
        private static VisualElement BuildPanel()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            Assert.IsNotNull(tree, $"Asset Gen UXML not found at {UxmlPath}");
            VisualElement block = tree.CloneTree().Q<VisualElement>("blender-bridge-panel");
            Assert.IsNotNull(block, "blender-bridge-panel is missing from the UXML");
            new McpBlenderBridgePanel(block);
            return block;
        }

        private static Label AddonStatus(VisualElement block) => block.Q<Label>("blender-addons-resolved");

        [Test]
        public void AddonStatus_MissingAddonsFolder_IsGrayAndNotCreatedYet()
        {
            Label status = AddonStatus(BuildPanel());
            StringAssert.Contains("(not created yet)", status.text);
            StringAssert.Contains("BlenderMCP addon: not installed", status.text);
            Assert.AreEqual(Gray, status.style.color.value);
        }

        [Test]
        public void AddonStatus_FolderWithoutAddon_ReportsNotInstalled()
        {
            Directory.CreateDirectory(root + "/addons");
            BlenderBridgePrefs.AddonsDirOverride = root + "/addons";
            Label status = AddonStatus(BuildPanel());
            StringAssert.DoesNotContain("not created yet", status.text);
            StringAssert.Contains("BlenderMCP addon: not installed", status.text);
            Assert.AreEqual(Gray, status.style.color.value);
        }

        [Test]
        public void AddonStatus_FolderWithAddon_IsGreen()
        {
            Directory.CreateDirectory(root + "/addons");
            File.WriteAllText(root + "/addons/" + BlenderBridgePrefs.AddonFileName, "# addon\n");
            BlenderBridgePrefs.AddonsDirOverride = root + "/addons";
            Label status = AddonStatus(BuildPanel());
            StringAssert.Contains("BlenderMCP addon: installed", status.text);
            Assert.AreEqual(Green, status.style.color.value);
        }

        [Test]
        public void AddonStatus_WithCheckout_ComparesInstalledAddon()
        {
            Directory.CreateDirectory(root + "/addons");
            Directory.CreateDirectory(root + "/checkout");
            File.WriteAllText(root + "/addons/" + BlenderBridgePrefs.AddonFileName, "# addon v1\n");
            File.WriteAllText(root + "/checkout/" + BlenderBridgePrefs.AddonFileName, "# addon v1\n");
            BlenderBridgePrefs.AddonsDirOverride = root + "/addons";
            BlenderBridgePrefs.ForkPath = root + "/checkout";

            Label status = AddonStatus(BuildPanel());
            StringAssert.Contains("in sync with checkout", status.text);
            Assert.AreEqual(Green, status.style.color.value);

            File.WriteAllText(root + "/checkout/" + BlenderBridgePrefs.AddonFileName, "# addon v2\n");
            status = AddonStatus(BuildPanel());
            StringAssert.Contains("differs from checkout (Sync Addon)", status.text);
            Assert.AreEqual(Gray, status.style.color.value);
        }

        [Test]
        public void CheckoutNote_IsNeutralAndOptional_AndImportButtonIsExplicit()
        {
            VisualElement block = BuildPanel();
            Assert.IsNull(block.Q(className: "warning-banner"), "The optional checkout must not look like a warning.");
            Assert.IsNull(block.Q("blender-not-configured"));
            StringAssert.Contains("Sync Addon and Check Updates", block.Q<Label>("blender-fork-help").text);
            Assert.IsFalse(block.Q<Button>("blender-sync-button").enabledSelf, "Sync Addon still needs a checkout.");

            Button import = block.Q<Button>("blender-import-button");
            Assert.AreEqual("Import Blender Selection (GLB)", import.text);
            StringAssert.Contains("selected in Blender", import.tooltip);
        }

        /// <summary>Answers every connection like the BlenderMCP addon answering get_scene_info.</summary>
        private static async Task ServeFakeAddon(TcpListener listener)
        {
            byte[] reply = Encoding.UTF8.GetBytes("{\"status\": \"success\", \"result\": {\"name\": \"Scene\", \"object_count\": 0}}");
            while (true)
            {
                TcpClient client;
                try { client = await listener.AcceptTcpClientAsync(); }
                catch (Exception) { return; } // listener stopped
                using (client)
                {
                    try
                    {
                        NetworkStream stream = client.GetStream();
                        await stream.ReadAsync(new byte[4096], 0, 4096);
                        await stream.WriteAsync(reply, 0, reply.Length);
                    }
                    catch (Exception) { /* client went away; keep serving */ }
                }
            }
        }

        [UnityTest]
        public IEnumerator AutomaticProbe_ReachesAFakeAddon_WithoutDisablingActions()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                _ = Task.Run(() => ServeFakeAddon(listener));
                BlenderBridgePrefs.Port = port;

                VisualElement block = BuildPanel();
                Label status = block.Q<Label>("blender-status-label");
                StringAssert.StartsWith($"Checking Blender at 127.0.0.1:{port}", status.text);
                Assert.IsTrue(block.Q<Button>("blender-import-button").enabledSelf, "An automatic probe must not disable the action buttons.");

                DateTime deadline = DateTime.UtcNow.AddSeconds(15);
                while (!status.text.StartsWith("Blender reachable") && DateTime.UtcNow < deadline) yield return null;
                Assert.AreEqual($"Blender reachable at 127.0.0.1:{port}", status.text, "The panel never reported the fake addon as reachable.");
                Assert.IsTrue(block.Q<VisualElement>("blender-status-dot").ClassListContains("valid"));
            }
            finally
            {
                listener.Stop();
            }
        }

        [UnityTest]
        public IEnumerator AutomaticProbe_ClosedPort_ReportsUnreachable()
        {
            VisualElement block = BuildPanel();
            Label status = block.Q<Label>("blender-status-label");
            VisualElement dot = block.Q<VisualElement>("blender-status-dot");

            DateTime deadline = DateTime.UtcNow.AddSeconds(15);
            while (!dot.ClassListContains("invalid") && DateTime.UtcNow < deadline) yield return null;
            Assert.IsTrue(dot.ClassListContains("invalid"), $"The probe never failed; status: {status.text}");
            StringAssert.Contains("not reachable", status.text);
        }
    }
}
