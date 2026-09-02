using System;
using System.IO;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services.Blender;
using MCPForUnity.Editor.Tools.Blender;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MCPForUnity.Editor.Windows.Components.AssetGen
{
    /// <summary>
    /// Controller for the "Blender Bridge" block of the Asset Gen tab: addon socket host/port,
    /// the user's blender-mcp checkout, Blender's addons folder, plus Test Connection / Sync Addon /
    /// Check Updates / Import Selection buttons that call <see cref="BlenderBridgeTool"/> directly.
    /// Unlike the provider rows, nothing here is a paid API call — it is local file and socket I/O.
    /// </summary>
    public class McpBlenderBridgePanel
    {
        private TextField hostField;
        private IntegerField portField;
        private Button testButton;
        private TextField forkField;
        private Button forkSelectButton;
        private Button forkClearButton;
        private TextField addonsField;
        private Button addonsSelectButton;
        private Button addonsClearButton;
        private Label addonsResolvedLabel;
        private Label statusLabel;
        private Label actionStatusLabel;
        private VisualElement statusDot;
        private VisualElement notConfiguredBanner;
        private Button syncButton;
        private Button updatesButton;
        private Button importButton;

        public VisualElement Root { get; }

        public McpBlenderBridgePanel(VisualElement root)
        {
            Root = root;
            CacheUIElements();
            InitializeUI();
            RegisterCallbacks();
        }

        private void CacheUIElements()
        {
            hostField = Root.Q<TextField>("blender-host");
            portField = Root.Q<IntegerField>("blender-port");
            testButton = Root.Q<Button>("blender-test-button");
            forkField = Root.Q<TextField>("blender-fork-path");
            forkSelectButton = Root.Q<Button>("blender-fork-select-button");
            forkClearButton = Root.Q<Button>("blender-fork-clear-button");
            addonsField = Root.Q<TextField>("blender-addons-dir");
            addonsSelectButton = Root.Q<Button>("blender-addons-select-button");
            addonsClearButton = Root.Q<Button>("blender-addons-clear-button");
            addonsResolvedLabel = Root.Q<Label>("blender-addons-resolved");
            statusLabel = Root.Q<Label>("blender-status-label");
            actionStatusLabel = Root.Q<Label>("blender-action-status");
            statusDot = Root.Q<VisualElement>("blender-status-dot");
            notConfiguredBanner = Root.Q<VisualElement>("blender-not-configured");
            syncButton = Root.Q<Button>("blender-sync-button");
            updatesButton = Root.Q<Button>("blender-updates-button");
            importButton = Root.Q<Button>("blender-import-button");
        }

        private void InitializeUI()
        {
            if (hostField != null) hostField.tooltip = "Host the BlenderMCP addon socket listens on (Blender's N panel > BlenderMCP).";
            if (portField != null) portField.tooltip = $"Addon socket port. Default {BlenderBridgePrefs.DefaultPort}.";
            if (testButton != null) testButton.tooltip = "Send get_scene_info to the addon socket. Blender must be running with the addon connected.";
            if (forkField != null)
                forkField.tooltip = "Folder of your blender-mcp checkout (the one containing addon.py). Enables Sync Addon and Check Updates.";
            if (addonsField != null)
                addonsField.tooltip = "Blender's user addons folder. Leave empty to auto-detect the newest Blender version's scripts/addons.";
            if (syncButton != null) syncButton.tooltip = "Copy the checkout's addon.py into Blender's addons folder (backs up the old file).";
            if (updatesButton != null) updatesButton.tooltip = "git fetch the checkout and report how far behind its remotes it is.";
            if (importButton != null) importButton.tooltip = "Export what is selected in Blender as GLB, import it and place it in the open scene.";

            SyncFromPrefs();
        }

        private void RegisterCallbacks()
        {
            hostField?.RegisterCallback<FocusOutEvent>(_ =>
            {
                BlenderBridgePrefs.Host = hostField.text;
                hostField.SetValueWithoutNotify(BlenderBridgePrefs.Host);
                SetConnectionStatus(null, "Unknown — press Test Connection");
            });

            portField?.RegisterValueChangedCallback(evt =>
            {
                BlenderBridgePrefs.Port = evt.newValue;
                portField.SetValueWithoutNotify(BlenderBridgePrefs.Port);
                SetConnectionStatus(null, "Unknown — press Test Connection");
            });

            forkField?.RegisterCallback<FocusOutEvent>(_ => SetForkPath(forkField.text));
            if (forkSelectButton != null) forkSelectButton.clicked += OnSelectFork;
            if (forkClearButton != null) forkClearButton.clicked += () => SetForkPath(string.Empty);

            addonsField?.RegisterCallback<FocusOutEvent>(_ => SetAddonsDir(addonsField.text));
            if (addonsSelectButton != null) addonsSelectButton.clicked += OnSelectAddonsDir;
            if (addonsClearButton != null) addonsClearButton.clicked += () => SetAddonsDir(string.Empty);

            if (testButton != null) testButton.clicked += OnTestConnection;
            if (syncButton != null) syncButton.clicked += () =>
            {
                RunAction(new JObject { ["action"] = "sync_addon" });
                UpdateAddonStatus();
            };
            if (updatesButton != null) updatesButton.clicked += () => RunAction(new JObject { ["action"] = "check_updates" });
            if (importButton != null) importButton.clicked += () =>
                RunAction(new JObject { ["action"] = "import_model", ["selection_only"] = true, ["format"] = "glb" });
        }

        /// <summary>Re-reads prefs and the on-disk addon state. Never touches the socket.</summary>
        public void Refresh() => SyncFromPrefs();

        private void SyncFromPrefs()
        {
            hostField?.SetValueWithoutNotify(BlenderBridgePrefs.Host);
            portField?.SetValueWithoutNotify(BlenderBridgePrefs.Port);
            forkField?.SetValueWithoutNotify(BlenderBridgePrefs.ForkPath);
            addonsField?.SetValueWithoutNotify(BlenderBridgePrefs.AddonsDirOverride);

            bool configured = BlenderBridgePrefs.IsForkConfigured;
            notConfiguredBanner?.EnableInClassList("visible", !configured);
            syncButton?.SetEnabled(configured);
            updatesButton?.SetEnabled(configured);

            UpdateAddonStatus();
            SetConnectionStatus(null, BlenderDetection.IsInstalled()
                ? "Blender app detected — press Test Connection"
                : "Blender app not found on this machine — press Test Connection if it runs elsewhere");
        }

        private void SetForkPath(string path)
        {
            string normalized = BlenderBridgePrefs.NormalizePath(path);
            if (!string.IsNullOrEmpty(normalized) && !BlenderBridgePrefs.IsValidForkPath(normalized))
            {
                EditorUtility.DisplayDialog("Invalid blender-mcp checkout",
                    $"No {BlenderBridgePrefs.AddonFileName} found in:\n{normalized}\n\nPick the folder that contains addon.py.", "OK");
                forkField?.SetValueWithoutNotify(BlenderBridgePrefs.ForkPath);
                return;
            }
            BlenderBridgePrefs.ForkPath = normalized;
            SyncFromPrefs();
        }

        private void OnSelectFork()
        {
            string picked = EditorUtility.OpenFolderPanel("Select blender-mcp checkout (folder containing addon.py)",
                BlenderBridgePrefs.ForkPath, "");
            if (!string.IsNullOrEmpty(picked)) SetForkPath(picked);
        }

        private void SetAddonsDir(string path)
        {
            string normalized = BlenderBridgePrefs.NormalizePath(path);
            if (!string.IsNullOrEmpty(normalized) && !Directory.Exists(normalized))
            {
                EditorUtility.DisplayDialog("Folder not found", $"Blender addons folder does not exist:\n{normalized}", "OK");
                addonsField?.SetValueWithoutNotify(BlenderBridgePrefs.AddonsDirOverride);
                return;
            }
            BlenderBridgePrefs.AddonsDirOverride = normalized;
            SyncFromPrefs();
        }

        private void OnSelectAddonsDir()
        {
            string picked = EditorUtility.OpenFolderPanel("Select Blender user addons folder (…/scripts/addons)",
                BlenderBridgePrefs.ResolveAddonsDir() ?? "", "");
            if (!string.IsNullOrEmpty(picked)) SetAddonsDir(picked);
        }

        private void UpdateAddonStatus()
        {
            if (addonsResolvedLabel == null) return;

            string dir = BlenderBridgePrefs.ResolveAddonsDir();
            string text = string.IsNullOrEmpty(dir) ? "Addons folder: not found" : $"Addons folder: {dir}";
            bool ok = !string.IsNullOrEmpty(dir);

            if (BlenderBridgePrefs.IsForkConfigured && ok)
            {
                string src = BlenderBridgePrefs.ForkAddonPath;
                string dst = BlenderBridgePrefs.InstalledAddonPath;
                try
                {
                    if (!File.Exists(dst)) { text += " · addon not installed (Sync Addon)"; ok = false; }
                    else if (BlenderBridgeTool.FileMd5(src) == BlenderBridgeTool.FileMd5(dst)) text += " · addon in sync ✓";
                    else { text += " · addon differs from checkout (Sync Addon)"; ok = false; }
                }
                catch (Exception e)
                {
                    text += $" · could not compare addon files: {e.Message}";
                    ok = false;
                }
            }

            addonsResolvedLabel.text = text;
            addonsResolvedLabel.style.color = ok ? new Color(0.4f, 0.8f, 0.4f) : new Color(0.7f, 0.7f, 0.7f);
        }

        private void OnTestConnection()
        {
            bool ok = BlenderSocketClient.IsReachable(out string error);
            SetConnectionStatus(ok, ok
                ? $"Blender reachable at {BlenderBridgePrefs.Host}:{BlenderBridgePrefs.Port}"
                : Truncate(error, 160));
        }

        private void SetConnectionStatus(bool? ok, string text)
        {
            if (statusLabel != null) statusLabel.text = text;
            if (statusDot == null) return;
            statusDot.RemoveFromClassList("valid");
            statusDot.RemoveFromClassList("invalid");
            statusDot.RemoveFromClassList("warning");
            if (ok == true) statusDot.AddToClassList("valid");
            else if (ok == false) statusDot.AddToClassList("invalid");
            else statusDot.AddToClassList("warning");
        }

        private JObject RunAction(JObject parameters)
        {
            JObject json;
            try
            {
                json = JObject.FromObject(BlenderBridgeTool.HandleCommand(parameters));
            }
            catch (Exception e)
            {
                json = new JObject { ["success"] = false, ["error"] = e.Message };
            }

            bool ok = json.Value<bool?>("success") ?? false;
            string message = ok ? json["message"]?.ToString() : json["error"]?.ToString();
            SetActionStatus(message ?? (ok ? "done" : "failed"), !ok);

            string details = json.ToString(Formatting.Indented);
            if (ok) McpLog.Info($"[Blender Bridge] {parameters["action"]}: {message}\n{details}");
            else McpLog.Warn($"[Blender Bridge] {parameters["action"]} failed: {message}\n{details}");
            return json;
        }

        private void SetActionStatus(string text, bool isError)
        {
            if (actionStatusLabel == null) return;
            actionStatusLabel.text = Truncate(text, 240);
            if (isError) actionStatusLabel.style.color = new StyleColor(new Color(0.85f, 0.2f, 0.2f));
            else actionStatusLabel.style.color = StyleKeyword.Null;
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }
    }
}
