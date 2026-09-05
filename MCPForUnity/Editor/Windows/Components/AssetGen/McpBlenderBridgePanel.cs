using System;
using System.IO;
using System.Threading.Tasks;
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
    /// Check Updates / Import Selection buttons that call <see cref="BlenderBridgeTool"/>.
    /// Unlike the provider rows, nothing here is a paid API call — it is local file and socket I/O,
    /// awaited off the editor thread so the window never freezes while Blender works.
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
        private bool busy;

        public VisualElement Root { get; }

        public McpBlenderBridgePanel(VisualElement root)
        {
            Root = root;
            CacheUIElements();
            InitializeUI();
            RegisterCallbacks();
        }

        /// <summary>Looks up the UXML elements by name.</summary>
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

        /// <summary>Sets tooltips and populates the fields from prefs.</summary>
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

        /// <summary>Wires field persistence and the action buttons.</summary>
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
            if (syncButton != null) syncButton.clicked += async () =>
            {
                await RunActionAsync(new JObject { ["action"] = "sync_addon" });
                UpdateAddonStatus();
            };
            if (updatesButton != null) updatesButton.clicked += async () =>
                await RunActionAsync(new JObject { ["action"] = "check_updates" });
            if (importButton != null) importButton.clicked += async () =>
                await RunActionAsync(new JObject { ["action"] = "import_model", ["selection_only"] = true, ["format"] = "glb" });
        }

        /// <summary>Re-reads prefs and the on-disk addon state. Never touches the socket.</summary>
        public void Refresh() => SyncFromPrefs();

        /// <summary>Reflects prefs into the fields, banner, button states and addon status line.</summary>
        private void SyncFromPrefs()
        {
            hostField?.SetValueWithoutNotify(BlenderBridgePrefs.Host);
            portField?.SetValueWithoutNotify(BlenderBridgePrefs.Port);
            forkField?.SetValueWithoutNotify(BlenderBridgePrefs.ForkPath);
            addonsField?.SetValueWithoutNotify(BlenderBridgePrefs.AddonsDirOverride);

            notConfiguredBanner?.EnableInClassList("visible", !BlenderBridgePrefs.IsForkConfigured);
            UpdateButtonStates();
            UpdateAddonStatus();
            SetConnectionStatus(null, BlenderDetection.IsInstalled()
                ? "Blender app detected — press Test Connection"
                : "Blender app not found on this machine — press Test Connection if it runs elsewhere");
        }

        /// <summary>Validates and persists the checkout folder; rejects folders without addon.py.</summary>
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

        /// <summary>Opens a folder picker for the checkout.</summary>
        private void OnSelectFork()
        {
            string picked = EditorUtility.OpenFolderPanel("Select blender-mcp checkout (folder containing addon.py)",
                BlenderBridgePrefs.ForkPath, "");
            if (!string.IsNullOrEmpty(picked)) SetForkPath(picked);
        }

        /// <summary>Validates and persists the addons folder override.</summary>
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

        /// <summary>Opens a folder picker for the addons folder.</summary>
        private void OnSelectAddonsDir()
        {
            string picked = EditorUtility.OpenFolderPanel("Select Blender user addons folder (…/scripts/addons)",
                BlenderBridgePrefs.ResolveAddonsDir() ?? "", "");
            if (!string.IsNullOrEmpty(picked)) SetAddonsDir(picked);
        }

        /// <summary>Shows the resolved addons folder and whether the installed addon matches the checkout.</summary>
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

        /// <summary>Probes the addon socket off the editor thread and shows the result on the status dot.</summary>
        private async void OnTestConnection()
        {
            if (busy) return;
            BlenderEndpoint endpoint = BlenderBridgePrefs.Endpoint;
            SetBusy(true);
            SetConnectionStatus(null, $"Testing {endpoint}…");
            try
            {
                var (ok, error) = await BlenderSocketClient.ProbeAsync(endpoint);
                SetConnectionStatus(ok, ok ? $"Blender reachable at {endpoint}" : Truncate(error, 160));
            }
            catch (Exception e)
            {
                SetConnectionStatus(false, Truncate(e.Message, 160));
            }
            finally
            {
                SetBusy(false);
            }
        }

        /// <summary>Colours the status dot: green for reachable, red for failed, amber for unknown.</summary>
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

        /// <summary>Runs one bridge action without blocking the editor and reports its message.</summary>
        private async Task<JObject> RunActionAsync(JObject parameters)
        {
            if (busy) return null;
            SetBusy(true);
            SetActionStatus($"Running {parameters["action"]}…", false);
            JObject json;
            try
            {
                json = JObject.FromObject(await BlenderBridgeTool.HandleCommand(parameters));
            }
            catch (Exception e)
            {
                json = new JObject { ["success"] = false, ["error"] = e.Message };
            }
            finally
            {
                SetBusy(false);
            }

            bool ok = json.Value<bool?>("success") ?? false;
            string message = ok ? json["message"]?.ToString() : json["error"]?.ToString();
            SetActionStatus(message ?? (ok ? "done" : "failed"), !ok);

            string details = json.ToString(Formatting.Indented);
            if (ok) McpLog.Info($"[Blender Bridge] {parameters["action"]}: {message}\n{details}");
            else McpLog.Warn($"[Blender Bridge] {parameters["action"]} failed: {message}\n{details}");
            return json;
        }

        /// <summary>Disables the action buttons while a bridge call is in flight.</summary>
        private void SetBusy(bool value)
        {
            busy = value;
            UpdateButtonStates();
        }

        /// <summary>Applies busy state and checkout-dependent availability to the buttons.</summary>
        private void UpdateButtonStates()
        {
            bool configured = BlenderBridgePrefs.IsForkConfigured;
            testButton?.SetEnabled(!busy);
            importButton?.SetEnabled(!busy);
            syncButton?.SetEnabled(!busy && configured);
            updatesButton?.SetEnabled(!busy && configured);
        }

        /// <summary>Writes the last action's outcome under the buttons, red on error.</summary>
        private void SetActionStatus(string text, bool isError)
        {
            if (actionStatusLabel == null) return;
            actionStatusLabel.text = Truncate(text, 240);
            if (isError) actionStatusLabel.style.color = new StyleColor(new Color(0.85f, 0.2f, 0.2f));
            else actionStatusLabel.style.color = StyleKeyword.Null;
        }

        /// <summary>Caps a message for the status labels.</summary>
        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }
    }
}
