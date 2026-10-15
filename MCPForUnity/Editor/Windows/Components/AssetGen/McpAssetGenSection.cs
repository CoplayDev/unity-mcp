using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
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
    /// Generation itself is never triggered here — only via MCP tools / CLI. The one exception is
    /// the Blender Bridge block (<see cref="McpBlenderBridgePanel"/>): its buttons run local socket
    /// and file operations against a Blender on this machine, never a paid provider call.
    ///
    /// Keys are written to the OS secure store (<see cref="SecureKeyStore"/>), never to
    /// EditorPrefs or the project. The stored key is never read back into the field; only
    /// its presence is surfaced through the status label.
    /// </summary>
    public class McpAssetGenSection
    {
        // Fixed provider lists. Each Id is both the SecureKeyStore key and the
        // AssetGenPrefs enable-flag id. All model/marketplace providers below emit GLB.
        // fal's key and enable toggle live on its 2D Images row; the 3D row only picks a model.
        private static readonly (string Id, string Label)[] ModelProviders =
        {
            ("tripo", "Tripo"),
            ("meshy", "Meshy"),
            ("fal", "fal (3D)"),
            ("sketchfab", "Sketchfab"),
        };

        // Editor dropdowns open a native menu with no search, so long catalogs are capped and
        // the per-provider search field reaches the remaining models.
        internal const int MenuLimit = 25;

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
        private McpBlenderBridgePanel blenderPanel;

        // Per-provider enable toggles for the GLB-capable (model) providers, used to
        // recompute the glTFast notice when a toggle changes.
        private readonly List<(string Id, Toggle Toggle)> modelEnableToggles = new();
        private readonly List<(VisualElement Container, string Kind, string Provider)> modelControls = new();
        private readonly Dictionary<string, string> searches = new();
        // Last failed compatibility check per kind/provider, kept so a catalog rebuild cannot hide it.
        private readonly Dictionary<string, (string Id, string Error)> verifyErrors = new();

        public VisualElement Root { get; private set; }

        public McpAssetGenSection(VisualElement root)
        {
            Root = root;
            CacheUIElements();
            InitializeUI();
            RegisterCallbacks();
            Root.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                SubscribeCatalogs();
                // Catch up on catalog changes that fired while the tab was detached.
                RebuildModelControls(null, null);
            });
            Root.RegisterCallback<DetachFromPanelEvent>(_ => { FalModelCatalog.Changed -= OnFalChanged; OpenRouterModelCatalog.Changed -= OnRouterChanged; });
            if (Root.panel != null) SubscribeCatalogs();
            Root.schedule.Execute(() => { if (!modelControls.Any(c => FalModelCatalog.IsRefreshing(c.Kind)) && !OpenRouterModelCatalog.IsRefreshing) _ = RefreshCatalog(false); }).Every(60000);
            _ = RefreshCatalog(false);
        }

        private void SubscribeCatalogs()
        {
            FalModelCatalog.Changed -= OnFalChanged;
            FalModelCatalog.Changed += OnFalChanged;
            OpenRouterModelCatalog.Changed -= OnRouterChanged;
            OpenRouterModelCatalog.Changed += OnRouterChanged;
        }

        private void OnFalChanged(string kind) => RebuildModelControls(kind, "fal");

        private void OnRouterChanged() => RebuildModelControls("image", "openrouter");

        private void CacheUIElements()
        {
            providersContainer = Root.Q<VisualElement>("assetgen-providers-container");
            gltfastNotice = Root.Q<VisualElement>("gltfast-notice");
            formatDropdown = Root.Q<DropdownField>("assetgen-format-dropdown");
            outputRootField = Root.Q<TextField>("assetgen-output-root");
            autoNormalizeToggle = Root.Q<Toggle>("assetgen-auto-normalize");
            refreshButton = Root.Q<Button>("assetgen-refresh");
            refreshStatusLabel = Root.Q<Label>("assetgen-refresh-status");

            var blenderRoot = Root.Q<VisualElement>("blender-bridge-panel");
            if (blenderRoot != null) blenderPanel = new McpBlenderBridgePanel(blenderRoot);
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
                    "Refresh fal image, sound and 3D models, and OpenRouter images.";
                refreshButton.clicked += OnRefreshClicked;
            }
        }

        /// <summary>
        /// Re-reads key presence and forces a nonblocking fal catalog refresh.
        /// </summary>
        private void OnRefreshClicked()
        {
            SyncFromPrefs();
            _ = RefreshCatalog(true);
        }

        /// <summary>
        /// Re-reads secure-store presence and prefs and rebuilds the rows. Called when the
        /// tab becomes visible so keys set elsewhere (e.g. via CLI) are reflected.
        /// </summary>
        public void Refresh()
        {
            SyncFromPrefs();
            blenderPanel?.Refresh();
            _ = RefreshCatalog(false);
        }

        private async Task RefreshCatalog(bool force)
        {
            refreshButton?.SetEnabled(false);
            if (refreshStatusLabel != null) SetStatus(refreshStatusLabel, "Checking model catalogs…", true);
            try
            {
                // A committed refresh raises Changed, which rebuilds only the affected model controls;
                // unchanged catalogs leave the rows (and any unsaved API-key input) alone.
                await Task.WhenAll(FalModelCatalog.RefreshAsync("image", force), FalModelCatalog.RefreshAsync("audio", force),
                    FalModelCatalog.RefreshAsync("model", force), OpenRouterModelCatalog.RefreshAsync(force));
                string error = FalModelCatalog.LastError("image") ?? FalModelCatalog.LastError("audio") ?? FalModelCatalog.LastError("model") ?? OpenRouterModelCatalog.LastError;
                string verified = FalModelCatalog.VerifiedAt("audio") ?? FalModelCatalog.VerifiedAt("image");
                string when = DateTime.TryParse(verified, out var time) ? time.ToLocalTime().ToString("g") : "unknown";
                string label = error != null ? error : "Catalogs checked " + when + " · refreshed automatically every 24 hours";
                if (refreshStatusLabel != null) SetStatus(refreshStatusLabel, label, error == null);
            }
            finally { refreshButton?.SetEnabled(true); }
        }

        /// <summary>Rebuilds live-catalog model controls; a null kind or provider matches all.</summary>
        private void RebuildModelControls(string kind, string provider)
        {
            foreach (var control in modelControls.ToArray())
            {
                if (control.Provider != "fal" && control.Provider != "openrouter") continue;
                if (kind != null && control.Kind != kind || provider != null && control.Provider != provider) continue;
                control.Container.Clear();
                PopulateModelDropdown(control.Container, control.Kind, control.Provider);
            }
        }

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
            modelControls.Clear();

            var modelPanel = AddCategoryPanel("3D Models");
            foreach (var provider in ModelProviders)
            {
                if (provider.Id == "fal")
                {
                    AddSharedFalRow(modelPanel, "model", provider.Label);
                    // No toggle of its own: the glTFast notice reads the 2D fal row's enable pref.
                    modelEnableToggles.Add((provider.Id, null));
                    continue;
                }
                var toggle = AddProviderRow(modelPanel, provider.Id, provider.Label, "model");
                modelEnableToggles.Add((provider.Id, toggle));
            }

            var imagePanel = AddCategoryPanel("2D Images");
            foreach (var provider in ImageProviders)
            {
                AddProviderRow(imagePanel, provider.Id, provider.Label, "image");
            }

            var audioPanel = AddCategoryPanel("Sound (fal.ai)");
            AddSharedFalRow(audioPanel, "audio", "fal (audio)");
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
            bool live = providerId == "fal" || providerId == "openrouter";
            // Bundled-only providers with no models (the Sketchfab marketplace) get no selector at all.
            if (!live && AssetGenModelCatalog.ForProvider(providerId, kind).Count == 0) return;
            if (live)
            {
                // Same .setting-row / .setting-label layout as the Model row below, so both align.
                var searchRow = new VisualElement();
                searchRow.AddToClassList("setting-row");
                var searchLabel = new Label("Search");
                searchLabel.AddToClassList("setting-label");
                searchRow.Add(searchLabel);

                var search = new TextField { name = "model-search-" + kind + "-" + providerId };
                search.AddToClassList("setting-dropdown-inline");
                search.tooltip = "Filter this provider's models by name, id or use case.";
                string key = kind + "/" + providerId;
                search.SetValueWithoutNotify(searches.TryGetValue(key, out var term) ? term : "");
                search.RegisterValueChangedCallback(evt =>
                {
                    searches[key] = evt.newValue ?? "";
                    RebuildModelControls(kind, providerId);
                });
                searchRow.Add(search);
                parent.Add(searchRow);
            }
            var container = new VisualElement();
            parent.Add(container);
            modelControls.Add((container, kind, providerId));
            PopulateModelDropdown(container, kind, providerId);
        }

        private void PopulateModelDropdown(VisualElement parent, string kind, string providerId)
        {
            var all = AssetGenModelCatalog.ForProvider(providerId, kind);
            string key = kind + "/" + providerId;
            string selectedId = AssetGenPrefs.GetSelectedModel(kind, providerId);
            if (string.IsNullOrEmpty(selectedId)) selectedId = AssetGenModelCatalog.DefaultModelId(providerId, kind);
            string term = searches.TryGetValue(key, out var query) ? query : "";
            var matches = all.Where(m => (m.Id + " " + m.Label + " " + m.UseCase).IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            // Catalog order puts bundled and eagerly verified models first, so the cap keeps those.
            var models = matches.Take(MenuLimit).ToList();
            ModelEntry selected = all.FirstOrDefault(model => model.Id == selectedId);
            if (selected != null && !models.Contains(selected)) models.Insert(0, selected);
            if (models.Count == 0)
            {
                parent.Add(new Label("No models found. Clear the search or refresh the catalog."));
                return;
            }

            var choices = new List<string>();
            foreach (ModelEntry m in models) choices.Add(m.Label + " (" + m.Id + ")");

            int selectedIndex = selected == null ? choices.Count : models.IndexOf(selected);
            if (selected == null)
                choices.Add(string.IsNullOrEmpty(selectedId) ? "Choose a model" : "Saved model unavailable — choose another (" + selectedId + ")");

            // Lay the dropdown out like the Format row: a horizontal .setting-row (align-items:center,
            // min-height:24px) with a .setting-label + a label-less DropdownField. Adding the dropdown
            // straight into the column row instead makes flex-grow expand it vertically into a huge box.
            var dropdownRow = new VisualElement();
            dropdownRow.AddToClassList("setting-row");

            var modelLabel = new Label("Model");
            modelLabel.AddToClassList("setting-label");
            dropdownRow.Add(modelLabel);

            // The list-item callback goes through the ctor: the property is not public before Unity 6.
            var dropdown = new DropdownField(choices, 0, null, MenuItemText);
            dropdown.AddToClassList("setting-dropdown-inline");
            dropdown.tooltip = "The model generate_* uses for this provider when no explicit model is passed.";
            dropdown.SetValueWithoutNotify(choices[selectedIndex]);
            dropdownRow.Add(dropdown);

            parent.Add(dropdownRow);

            if (matches.Count > MenuLimit)
            {
                var more = new Label($"The menu shows {MenuLimit} of {matches.Count} models. Use Search to find the others.");
                more.AddToClassList("help-text");
                more.style.whiteSpace = WhiteSpace.Normal;
                parent.Add(more);
            }

            var meta = new Label();
            meta.AddToClassList("help-text");
            meta.style.whiteSpace = WhiteSpace.Normal;
            parent.Add(meta);

            var caveat = new Label();
            caveat.AddToClassList("validation-description");
            caveat.style.whiteSpace = WhiteSpace.Normal;
            parent.Add(caveat);

            // A null selection hides the caveat box instead of leaving it empty.
            UpdateModelCaveat(caveat, selected);
            if (selected != null)
            {
                UpdateModelMeta(meta, selected);
                if (verifyErrors.TryGetValue(key, out var failure) && failure.Id == selected.Id) ShowVerifyError(meta, failure.Error);
            }
            else if (string.IsNullOrEmpty(selectedId)) meta.text = "Choose a model before generating.";
            else meta.text = "Your saved selection is preserved. Choose an available model before generating.";

            dropdown.RegisterValueChangedCallback(evt =>
            {
                int index = choices.IndexOf(evt.newValue);
                if (index < 0 || index >= models.Count) return;
                ModelEntry picked = models[index];
                AssetGenPrefs.SetSelectedModel(kind, providerId, picked.Id);
                verifyErrors.Remove(key);
                UpdateModelMeta(meta, picked);
                UpdateModelCaveat(caveat, picked);
                if (providerId == "fal" || providerId == "openrouter") _ = VerifySelection(picked, meta);
            });
        }

        private async Task VerifySelection(ModelEntry picked, Label meta)
        {
            string key = picked.Kind + "/" + picked.Provider;
            meta.text = "Checking compatibility…";
            try
            {
                string apiKey = null;
                if (picked.Provider == "fal") try { SecureKeyStore.Current.TryGet("fal", out apiKey); } catch { }
                string mode = picked.Modes?.FirstOrDefault() ?? "text";
                var verified = picked.Provider == "fal"
                    ? await FalModelCatalog.VerifyForGeneration(picked.Id, picked.Kind, mode, CancellationToken.None, apiKey)
                    : await OpenRouterModelCatalog.VerifyForGeneration(picked.Id, mode, CancellationToken.None);
                if (AssetGenPrefs.GetSelectedModel(picked.Kind, picked.Provider) == picked.Id) UpdateModelMeta(meta, verified);
            }
            catch (Exception error)
            {
                if (AssetGenPrefs.GetSelectedModel(picked.Kind, picked.Provider) != picked.Id) return;
                string message = "Compatibility check failed: " + SecretRedactor.Scrub(error.Message);
                verifyErrors[key] = (picked.Id, message);
                ShowVerifyError(meta, message);
            }
        }

        /// <summary>
        /// Editor dropdowns open a native GenericMenu, which reads every '/' in a model id as a
        /// submenu separator. The menu shows a look-alike slash; the field keeps the real text.
        /// </summary>
        internal static string MenuItemText(string choice) => choice.Replace('/', '\u2215');

        private static void ShowVerifyError(Label meta, string message)
        {
            meta.text = message;
            meta.AddToClassList("warning-banner-text");
        }

        /// <summary>
        /// fal audio / 3D row: no enable toggle and no key field — these kinds reuse the single fal
        /// key owned by the Image "fal" row. Surfaces that key's presence and a model dropdown.
        /// </summary>
        private void AddSharedFalRow(VisualElement parent, string kind, string displayName)
        {
            var row = new VisualElement();
            row.style.marginBottom = 8;
            row.style.paddingBottom = 8;
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = new Color(0.3f, 0.3f, 0.3f, 0.3f);

            // Header: name + shared-key status inline to its right. No key field — the fal key is
            // owned by the 2D fal row.
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 2;

            var nameLabel = new Label(displayName);
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

            AddModelDropdown(row, kind, "fal");

            parent.Add(row);
        }

        /// <summary>
        /// The fal key is shared with the audio and 3D rows, whose "key present" status is snapshotted at
        /// build time. When the 2D fal key is saved/cleared, schedule a full rebuild so those rows
        /// reflect it without a manual Refresh. Deferred so we don't destroy the element whose
        /// callback is still running.
        /// </summary>
        private void RebuildIfSharedKey(string id)
        {
            if (!string.Equals(id, "fal", StringComparison.OrdinalIgnoreCase)) return;
            Root?.schedule.Execute(SyncFromPrefs);
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
            // Only live-catalog providers have a verification state; Tripo/Meshy are bundled by design.
            if (m.Provider == "fal" || m.Provider == "openrouter")
                parts.Add(m.VerifiedAt != null ? "compatibility verified" : m.FromRefresh ? "discovered · checked before generation" : "bundled · checked before generation");
            if (!string.IsNullOrEmpty(m.LicenseType)) parts.Add("license: " + m.LicenseType);
            label.text = string.Join(" · ", parts);
            label.tooltip = m.ModelUrl ?? m.Id;
            label.RemoveFromClassList("warning-banner-text");
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
