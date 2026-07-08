using System;
using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Security;
using MCPForUnity.Editor.Services.AssetGen.Import;
using UnityEngine;
using UnityEngine.UIElements;

namespace MCPForUnity.Editor.Windows.Components.AssetGen
{
    /// <summary>
    /// Controller for the AI Asset Generation settings tab. This tab is CONFIG ONLY:
    /// it lets users enter/clear per-provider API keys, toggle providers on/off,
    /// presence-check a key, and set non-secret generation preferences.
    /// Generation itself is never triggered here — only via MCP tools / CLI.
    ///
    /// Keys are written to the OS secure store (<see cref="SecureKeyStore"/>), never to
    /// EditorPrefs or the project. The stored key is never read back into the field; only
    /// its presence is surfaced through the status label.
    /// </summary>
    public class McpAssetGenSection
    {
        // Fixed provider lists. Each Id is both the SecureKeyStore key and the
        // AssetGenPrefs enable-flag id. All model/marketplace providers below emit GLB.
        private static readonly (string Id, string Label)[] ModelProviders =
        {
            ("tripo", "Tripo"),
            ("meshy", "Meshy"),
            ("sketchfab", "Sketchfab"),
        };

        private static readonly (string Id, string Label)[] ImageProviders =
        {
            ("fal", "fal"),
            ("openrouter", "OpenRouter"),
        };

        // UI Elements
        private VisualElement providersContainer;
        private VisualElement gltfastNotice;
        private DropdownField formatDropdown;
        private TextField outputRootField;
        private Toggle autoNormalizeToggle;

        // Per-provider enable toggles for the GLB-capable (model) providers, used to
        // recompute the glTFast notice when a toggle changes.
        private readonly List<(string Id, Toggle Toggle)> modelEnableToggles = new();

        public VisualElement Root { get; private set; }

        public McpAssetGenSection(VisualElement root)
        {
            Root = root;
            CacheUIElements();
            InitializeUI();
            RegisterCallbacks();
        }

        private void CacheUIElements()
        {
            providersContainer = Root.Q<VisualElement>("assetgen-providers-container");
            gltfastNotice = Root.Q<VisualElement>("gltfast-notice");
            formatDropdown = Root.Q<DropdownField>("assetgen-format-dropdown");
            outputRootField = Root.Q<TextField>("assetgen-output-root");
            autoNormalizeToggle = Root.Q<Toggle>("assetgen-auto-normalize");
        }

        private void InitializeUI()
        {
            // One-time choices + tooltips; the field values are populated by SyncFromPrefs.
            if (formatDropdown != null)
            {
                formatDropdown.choices = new List<string> { "glb", "fbx", "obj" };
                formatDropdown.tooltip = "Default container format for generated 3D models.";
            }

            if (outputRootField != null)
            {
                outputRootField.tooltip =
                    $"Project-relative folder where generated assets are written. Empty = {AssetGenPrefs.DefaultOutputRoot}.";
            }

            if (autoNormalizeToggle != null)
            {
                autoNormalizeToggle.tooltip = "Uniformly scale imported models to the target size on import.";
            }

            SyncFromPrefs();
        }

        private void RegisterCallbacks()
        {
            if (formatDropdown != null)
            {
                formatDropdown.RegisterValueChangedCallback(evt =>
                {
                    AssetGenPrefs.DefaultFormat = evt.newValue;
                });
            }

            if (outputRootField != null)
            {
                outputRootField.RegisterCallback<FocusOutEvent>(_ =>
                {
                    AssetGenPrefs.OutputRoot = outputRootField.text?.Trim();
                    // Reflect the normalized/default value (empty -> default) without re-triggering.
                    outputRootField.SetValueWithoutNotify(AssetGenPrefs.OutputRoot);
                });
            }

            if (autoNormalizeToggle != null)
            {
                autoNormalizeToggle.RegisterValueChangedCallback(evt =>
                {
                    AssetGenPrefs.AutoNormalize = evt.newValue;
                });
            }
        }

        /// <summary>
        /// Re-reads secure-store presence and prefs and rebuilds the rows. Called when the
        /// tab becomes visible so keys set elsewhere (e.g. via CLI) are reflected.
        /// </summary>
        public void Refresh() => SyncFromPrefs();

        /// <summary>Rebuild the provider rows and reflect current prefs into the fields.</summary>
        private void SyncFromPrefs()
        {
            BuildProviderRows();
            formatDropdown?.SetValueWithoutNotify(NormalizeFormat(AssetGenPrefs.DefaultFormat));
            outputRootField?.SetValueWithoutNotify(AssetGenPrefs.OutputRoot);
            autoNormalizeToggle?.SetValueWithoutNotify(AssetGenPrefs.AutoNormalize);
            UpdateGltfastNotice();
        }

        private void BuildProviderRows()
        {
            if (providersContainer == null)
            {
                return;
            }

            providersContainer.Clear();
            modelEnableToggles.Clear();

            AddGroupLabel("3D Model Providers");
            foreach (var provider in ModelProviders)
            {
                var toggle = AddProviderRow(provider.Id, provider.Label);
                modelEnableToggles.Add((provider.Id, toggle));
            }

            AddGroupLabel("Image Providers");
            foreach (var provider in ImageProviders)
            {
                AddProviderRow(provider.Id, provider.Label);
            }

            AddBlenderHandoffRow();
        }

        private void AddGroupLabel(string text)
        {
            var label = new Label(text);
            label.AddToClassList("config-label");
            providersContainer.Add(label);
        }

        /// <summary>
        /// Informational handoff row (not a keyed provider): best-effort "is Blender installed"
        /// status + a pointer to the blender-to-unity workflow. BlenderMCP itself runs in the AI
        /// client and isn't detectable from Unity, so this only reports the local Blender app.
        /// </summary>
        private void AddBlenderHandoffRow()
        {
            AddGroupLabel("Blender → Unity Handoff");

            var row = new VisualElement();
            row.style.marginBottom = 8;

            bool blender = BlenderDetection.IsInstalled();
            var status = new Label(blender ? "Blender app detected ✓" : "Blender app not found on this machine");
            status.AddToClassList("help-text");
            status.style.color = blender ? new Color(0.4f, 0.8f, 0.4f) : new Color(0.7f, 0.7f, 0.7f);
            row.Add(status);

            var help = new Label(
                "Pair Blender with the BlenderMCP server in your AI client, then run the blender-to-unity " +
                "skill to export the current model — it imports via the import_model_file tool. (BlenderMCP " +
                "is configured in your AI client and can't be detected here.)");
            help.AddToClassList("help-text");
            help.style.whiteSpace = WhiteSpace.Normal;
            row.Add(help);

            providersContainer.Add(row);
        }

        private Toggle AddProviderRow(string id, string displayName)
        {
            var row = new VisualElement();
            row.style.marginBottom = 8;
            row.style.paddingBottom = 8;
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = new Color(0.3f, 0.3f, 0.3f, 0.3f);

            // Header: bold provider name + enable toggle.
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 2;

            var nameLabel = new Label(displayName);
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLabel.style.flexGrow = 1;
            header.Add(nameLabel);

            var enableToggle = new Toggle("Enabled");
            enableToggle.SetValueWithoutNotify(AssetGenPrefs.IsProviderEnabled(id));
            enableToggle.tooltip = $"Enable the {displayName} provider for asset generation.";
            header.Add(enableToggle);

            row.Add(header);

            var statusLabel = new Label();
            statusLabel.AddToClassList("help-text");

            // Masked key field + Save / Clear / Test buttons.
            var fieldRow = new VisualElement();
            fieldRow.style.flexDirection = FlexDirection.Row;
            fieldRow.style.alignItems = Align.Center;

            var keyField = new TextField();
            keyField.isPasswordField = true;
            keyField.maskChar = '*';
            keyField.style.flexGrow = 1;
            keyField.style.flexShrink = 1;
            keyField.style.marginRight = 4;
            keyField.tooltip =
                $"Paste your {displayName} API key, then press Save (or click away). " +
                "The key is stored in your OS secure store and is never read back into this field.";
            fieldRow.Add(keyField);

            var saveButton = new Button { text = "Save" };
            saveButton.AddToClassList("icon-button");
            fieldRow.Add(saveButton);

            var clearButton = new Button { text = "Clear" };
            clearButton.AddToClassList("icon-button");
            fieldRow.Add(clearButton);

            var testButton = new Button { text = "Test" };
            testButton.AddToClassList("icon-button");
            fieldRow.Add(testButton);

            row.Add(fieldRow);
            row.Add(statusLabel);

            // Persist the typed key, then clear the field so the secret is never displayed.
            void SaveKeyFromField()
            {
                string text = keyField.text?.Trim();
                if (string.IsNullOrEmpty(text))
                {
                    return;
                }

                try
                {
                    SecureKeyStore.Current.Set(id, text);
                    keyField.SetValueWithoutNotify(string.Empty);
                    SetStatus(statusLabel, "saved ✓", true);
                }
                catch (Exception ex)
                {
                    McpLog.Warn($"Failed to store {id} key: {ex.Message}");
                    SetStatus(statusLabel, "save failed", false);
                }
            }

            keyField.RegisterCallback<FocusOutEvent>(_ => SaveKeyFromField());
            saveButton.clicked += SaveKeyFromField;

            clearButton.clicked += () =>
            {
                try
                {
                    SecureKeyStore.Current.Delete(id);
                }
                catch (Exception ex)
                {
                    McpLog.Warn($"Failed to delete {id} key: {ex.Message}");
                }

                keyField.SetValueWithoutNotify(string.Empty);
                SetStatus(statusLabel, "not set", false);
            };

            // v1 surfaces presence only. Live endpoint validation (an actual auth ping to the
            // provider) is a future enhancement and intentionally not performed here.
            testButton.clicked += () =>
            {
                bool present = HasKey(id);
                SetStatus(statusLabel, present ? "key present ✓" : "no key set", present);
            };

            enableToggle.RegisterValueChangedCallback(evt =>
            {
                AssetGenPrefs.SetProviderEnabled(id, evt.newValue);
                UpdateGltfastNotice();
            });

            // Initial status reflects secure-store presence (existence only; never the value).
            bool has = HasKey(id);
            SetStatus(statusLabel, has ? "saved ✓" : "not set", has);

            providersContainer.Add(row);
            return enableToggle;
        }

        private static void SetStatus(Label label, string text, bool ok)
        {
            if (label == null)
            {
                return;
            }

            label.text = text;
            label.style.color = ok
                ? new Color(0.4f, 0.8f, 0.4f)
                : new Color(0.7f, 0.7f, 0.7f);
        }

        private static bool HasKey(string id)
        {
            try { return SecureKeyStore.Current.Has(id); }
            catch { return false; }
        }

        private static string NormalizeFormat(string format)
        {
            switch ((format ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "glb":
                case "fbx":
                case "obj":
                    return format.Trim().ToLowerInvariant();
                default:
                    return AssetGenPrefs.DefaultFormatValue;
            }
        }

        private void UpdateGltfastNotice()
        {
            if (gltfastNotice == null)
            {
                return;
            }

            bool anyGlbProviderEnabled = false;
            foreach (var entry in modelEnableToggles)
            {
                bool enabled = entry.Toggle != null
                    ? entry.Toggle.value
                    : AssetGenPrefs.IsProviderEnabled(entry.Id);
                if (enabled)
                {
                    anyGlbProviderEnabled = true;
                    break;
                }
            }

            bool show = anyGlbProviderEnabled && !ModelImportPipeline.IsGltfastAvailable();
            gltfastNotice.EnableInClassList("visible", show);
        }
    }
}
