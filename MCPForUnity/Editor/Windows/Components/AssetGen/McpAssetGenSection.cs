using System;
using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Security;
using MCPForUnity.Editor.Services.AssetGen;
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
        private Button refreshButton;
        private Label refreshStatusLabel;

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
            refreshButton = Root.Q<Button>("assetgen-refresh");
            refreshStatusLabel = Root.Q<Label>("assetgen-refresh-status");
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

            if (refreshButton != null)
            {
                refreshButton.tooltip =
                    "Re-check API-key presence and rebuild the provider/model rows. Picks up keys or " +
                    "prefs set elsewhere (CLI, env override). The model list is curated in-package.";
                refreshButton.clicked += OnRefreshClicked;
            }
        }

        /// <summary>
        /// Re-reads secure-store key presence and the curated catalog and rebuilds the rows — useful
        /// to pick up keys/prefs set elsewhere (CLI, env override). fal has no public list-models API,
        /// so the curated catalog is the source of truth; this never hits the network or blocks the tab.
        /// </summary>
        private void OnRefreshClicked()
        {
            SyncFromPrefs();
            if (refreshStatusLabel != null)
                SetStatus(refreshStatusLabel, "refreshed — using the built-in model catalog", true);
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

            var modelPanel = AddCategoryPanel("3D Models");
            foreach (var provider in ModelProviders)
            {
                var toggle = AddProviderRow(modelPanel, provider.Id, provider.Label, "model");
                modelEnableToggles.Add((provider.Id, toggle));
            }

            var imagePanel = AddCategoryPanel("2D Images");
            foreach (var provider in ImageProviders)
            {
                AddProviderRow(imagePanel, provider.Id, provider.Label, "image");
            }

            var audioPanel = AddCategoryPanel("Sound (fal.ai)");
            AddAudioRow(audioPanel);

            AddBlenderHandoffRow();
        }

        /// <summary>
        /// Creates a darker rounded panel with a title (added to the providers container). Each of the
        /// three categories (3D / 2D / sound) gets its own panel so they read as distinct blocks.
        /// </summary>
        private VisualElement AddCategoryPanel(string title)
        {
            var panel = new VisualElement();
            panel.style.backgroundColor = new Color(0f, 0f, 0f, 0.20f);
            panel.style.paddingTop = 8;
            panel.style.paddingBottom = 8;
            panel.style.paddingLeft = 8;
            panel.style.paddingRight = 8;
            panel.style.marginBottom = 10;
            panel.style.borderTopLeftRadius = 4;
            panel.style.borderTopRightRadius = 4;
            panel.style.borderBottomLeftRadius = 4;
            panel.style.borderBottomRightRadius = 4;

            var label = new Label(title);
            label.AddToClassList("config-label");
            label.style.marginTop = 0;
            panel.Add(label);

            providersContainer.Add(panel);
            return panel;
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

        private Toggle AddProviderRow(VisualElement parent, string id, string displayName, string kind)
        {
            var row = new VisualElement();
            row.style.marginBottom = 8;
            row.style.paddingBottom = 8;
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = new Color(0.3f, 0.3f, 0.3f, 0.3f);

            var statusLabel = new Label();
            statusLabel.AddToClassList("help-text");

            // Header: bold provider name, key status inline to its right, enable toggle far right.
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 2;

            var nameLabel = new Label(displayName);
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLabel.style.flexShrink = 0;
            header.Add(nameLabel);

            statusLabel.style.flexGrow = 1;
            statusLabel.style.marginLeft = 8;
            header.Add(statusLabel);

            var enableToggle = new Toggle("Enabled");
            enableToggle.SetValueWithoutNotify(AssetGenPrefs.IsProviderEnabled(id));
            enableToggle.tooltip = $"Enable the {displayName} provider for asset generation.";
            header.Add(enableToggle);

            row.Add(header);

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
                    RebuildIfSharedKey(id);
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
                RebuildIfSharedKey(id);
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

            // "Which model" selector for this provider (skipped for providers with no catalog
            // models, e.g. the Sketchfab marketplace).
            AddModelDropdown(row, kind, id);

            parent.Add(row);
            return enableToggle;
        }

        /// <summary>
        /// Adds a "Model" dropdown + metadata line for a (kind, provider) pair, if the catalog has
        /// any models for it. The dropdown shows friendly labels; the pref stores the model id.
        /// Selecting a model becomes the default that generate_* uses when no explicit model is passed.
        /// </summary>
        private void AddModelDropdown(VisualElement parent, string kind, string providerId)
        {
            IReadOnlyList<ModelEntry> models = AssetGenModelCatalog.ForProvider(providerId, kind);
            if (models.Count == 0) return;

            var choices = new List<string>();
            foreach (ModelEntry m in models) choices.Add(m.Label);

            string selectedId = AssetGenPrefs.GetSelectedModel(kind, providerId);
            if (string.IsNullOrEmpty(selectedId)) selectedId = AssetGenModelCatalog.DefaultModelId(providerId, kind);
            ModelEntry selected = AssetGenModelCatalog.Find(selectedId);
            if (selected == null)
            {
                // The stored pref points at a model that's no longer in the catalog (stale/invalid).
                // The dropdown falls back to the first model — clear the pref so generate_* resolves to
                // the same shown model instead of sending the stale id.
                selected = models[0];
                if (!string.IsNullOrEmpty(AssetGenPrefs.GetSelectedModel(kind, providerId)))
                    AssetGenPrefs.SetSelectedModel(kind, providerId, string.Empty);
            }

            // Lay the dropdown out like the Format row: a horizontal .setting-row (align-items:center,
            // min-height:24px) with a .setting-label + a label-less DropdownField. Adding the dropdown
            // straight into the column row instead makes flex-grow expand it vertically into a huge box.
            var dropdownRow = new VisualElement();
            dropdownRow.AddToClassList("setting-row");

            var modelLabel = new Label("Model");
            modelLabel.AddToClassList("setting-label");
            dropdownRow.Add(modelLabel);

            var dropdown = new DropdownField(choices, 0);
            dropdown.AddToClassList("setting-dropdown-inline");
            dropdown.tooltip = "The model generate_* uses for this provider when no explicit model is passed.";
            dropdown.SetValueWithoutNotify(selected.Label);
            dropdownRow.Add(dropdown);

            parent.Add(dropdownRow);

            var meta = new Label();
            meta.AddToClassList("help-text");
            meta.style.whiteSpace = WhiteSpace.Normal;
            parent.Add(meta);

            var caveat = new Label();
            caveat.AddToClassList("validation-description");
            caveat.style.whiteSpace = WhiteSpace.Normal;
            parent.Add(caveat);

            UpdateModelMeta(meta, selected);
            UpdateModelCaveat(caveat, selected);

            dropdown.RegisterValueChangedCallback(evt =>
            {
                ModelEntry picked = FindByLabel(models, evt.newValue);
                if (picked == null) return;
                AssetGenPrefs.SetSelectedModel(kind, providerId, picked.Id);
                UpdateModelMeta(meta, picked);
                UpdateModelCaveat(caveat, picked);
            });
        }

        /// <summary>
        /// Audio row: no enable toggle and no key field — audio reuses the single fal key owned by
        /// the Image "fal" row. Surfaces that key's presence and a fal-audio model dropdown.
        /// </summary>
        private void AddAudioRow(VisualElement parent)
        {
            var row = new VisualElement();
            row.style.marginBottom = 8;
            row.style.paddingBottom = 8;
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = new Color(0.3f, 0.3f, 0.3f, 0.3f);

            // Header: name + shared-key status inline to its right. No key field — audio reuses the
            // fal key owned by the 2D fal row.
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 2;

            var nameLabel = new Label("fal (audio)");
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLabel.style.flexShrink = 0;
            header.Add(nameLabel);

            bool hasFal = HasKey("fal");
            var status = new Label(hasFal ? "key present ✓ (shared with 2D fal)" : "no fal key — set it in 2D Images");
            status.AddToClassList("help-text");
            status.style.color = hasFal ? new Color(0.4f, 0.8f, 0.4f) : new Color(0.7f, 0.7f, 0.7f);
            status.style.flexGrow = 1;
            status.style.marginLeft = 8;
            header.Add(status);

            row.Add(header);

            AddModelDropdown(row, "audio", "fal");

            parent.Add(row);
        }

        /// <summary>
        /// The fal key is shared with the audio row, whose "key present" status is snapshotted at
        /// build time. When the 2D fal key is saved/cleared, schedule a full rebuild so the audio row
        /// reflects it without a manual Refresh. Deferred so we don't destroy the element whose
        /// callback is still running.
        /// </summary>
        private void RebuildIfSharedKey(string id)
        {
            if (!string.Equals(id, "fal", StringComparison.OrdinalIgnoreCase)) return;
            Root?.schedule.Execute(SyncFromPrefs);
        }

        private static ModelEntry FindByLabel(IReadOnlyList<ModelEntry> models, string label)
        {
            foreach (ModelEntry m in models)
                if (m.Label == label) return m;
            return null;
        }

        private static void UpdateModelMeta(Label label, ModelEntry m)
        {
            if (label == null || m == null) return;
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(m.UseCase)) parts.Add(m.UseCase);
            if (!string.IsNullOrEmpty(m.PriceLabel)) parts.Add(m.PriceLabel);
            // Only surface the "≤Ns" hint for models with an actual duration control (DurationField);
            // Lyria advertises a max but takes no duration input, so showing a hint would mislead.
            if (m.MaxDurationSeconds > 0f && !string.IsNullOrEmpty(m.DurationField)) parts.Add($"≤{m.MaxDurationSeconds:0}s");
            if (m.Loopable) parts.Add("loopable");
            label.text = string.Join(" · ", parts);
        }

        private static void UpdateModelCaveat(Label label, ModelEntry m)
        {
            if (label == null) return;
            bool show = m != null && !string.IsNullOrEmpty(m.CommercialNote);
            label.text = show ? m.CommercialNote : string.Empty;
            label.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
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
