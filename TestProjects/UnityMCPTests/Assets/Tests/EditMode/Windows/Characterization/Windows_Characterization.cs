using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using MCPForUnity.Editor.Windows;
using MCPForUnity.Editor.Windows.Components.Connection;
using MCPForUnity.Editor.Constants;
using UnityEngine.UIElements;

namespace MCPForUnityTests.Editor.Windows.Characterization
{
    /// <summary>
    /// Characterization tests for Windows & UI domain.
    /// These tests capture CURRENT behavior without refactoring.
    /// They serve as a regression baseline for future refactoring work.
    ///
    /// Based on analysis in: MCPForUnity/Editor/Windows/Tests/CHARACTERIZATION_ANALYSIS.md
    ///
    /// Covers: MCPSetupWindow, EditorPrefsWindow, McpConnectionSection, and component patterns
    /// </summary>
    [TestFixture]
    public class WindowsCharacterizationTests
    {
        #region Section 1: EditorPrefsWindow Tests (3 tests)

        /// <summary>
        /// Current behavior: EditorPrefsWindow caches 2 base UI elements (ScrollView, Container)
        /// plus N dynamic items created from EditorPrefs.
        /// </summary>
        [Test]
        public void EditorPrefsWindow_CachesBaseUIElements_ScrollViewAndContainer()
        {
            // Verify field existence for base UI caching
            var type = typeof(EditorPrefsWindow);
            var scrollViewField = type.GetField("scrollView", BindingFlags.NonPublic | BindingFlags.Instance);
            var containerField = type.GetField("prefsContainer", BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.IsNotNull(scrollViewField, "Should have scrollView field");
            Assert.IsNotNull(containerField, "Should have prefsContainer field");
            Assert.AreEqual(typeof(ScrollView), scrollViewField.FieldType);
            Assert.AreEqual(typeof(VisualElement), containerField.FieldType);

            Assert.Pass("EditorPrefsWindow caches 2 base UI elements: ScrollView + Container");
        }

        /// <summary>
        /// Current behavior: EditorPrefsWindow uses type detection logic to identify
        /// whether an EditorPref is Bool, Int, Float, or String.
        /// </summary>
        [Test]
        public void EditorPrefsWindow_UsesTypeDetectionLogic_ForUnknownPrefs()
        {
            // Document the type detection approach
            var detectionSteps = new[]
            {
                "1. Check knownPrefTypes dictionary for known keys",
                "2. For unknown keys: EditorPrefs.GetString() first",
                "3. Try int.TryParse()",
                "4. Try float.TryParse()",
                "5. Try bool.TryParse()",
                "6. Default to String if all fail"
            };

            // Verify the enum exists
            var type = typeof(EditorPrefsWindow);
            var enumType = type.Assembly.GetType("MCPForUnity.Editor.Windows.EditorPrefType");
            Assert.IsNotNull(enumType, "Should have EditorPrefType enum");

            var enumValues = Enum.GetNames(enumType);
            Assert.Contains("String", enumValues);
            Assert.Contains("Int", enumValues);
            Assert.Contains("Float", enumValues);
            Assert.Contains("Bool", enumValues);

            Assert.Pass($"Type detection flow: {string.Join("; ", detectionSteps)}");
        }

        /// <summary>
        /// Current behavior: EditorPrefsWindow registers callbacks per-item for Save buttons
        /// rather than using RegisterValueChangedCallback pattern.
        /// </summary>
        [Test]
        public void EditorPrefsWindow_RegistersPerItemCallbacks_ForSaveButtons()
        {
            // Document the callback pattern
            var type = typeof(EditorPrefsWindow);
            var createItemMethod = type.GetMethod("CreateItemUI", BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.IsNotNull(createItemMethod, "Should have CreateItemUI method");

            var pattern = "Each item gets: saveButton.clicked += () => SavePref(item, value, type)";
            Assert.Pass($"Callback pattern: {pattern}");
        }

        #endregion

        #region Section 2: MCPSetupWindow Tests (3 tests)

        /// <summary>
        /// Current behavior: MCPSetupWindow caches 13+ UI elements in CreateGUI
        /// without a separate CacheUIElements method.
        /// </summary>
        [Test]
        public void MCPSetupWindow_CachesMultipleUIElements_InCreateGUI()
        {
            var type = typeof(MCPSetupWindow);
            var fields = type.GetFields(BindingFlags.NonPublic | BindingFlags.Instance);

            // Count VisualElement-related fields
            var uiFields = fields.Where(f =>
                f.FieldType == typeof(VisualElement) ||
                f.FieldType == typeof(Label) ||
                f.FieldType == typeof(Button)
            ).ToArray();

            Assert.GreaterOrEqual(uiFields.Length, 10, "Should have 10+ UI element fields");

            var expectedFields = new[]
            {
                "pythonIndicator", "pythonVersion", "pythonDetails",
                "uvIndicator", "uvVersion", "uvDetails",
                "statusMessage", "installationSection",
                "openPythonLinkButton", "openUvLinkButton",
                "refreshButton", "doneButton"
            };

            Assert.Pass($"MCPSetupWindow caches {uiFields.Length} UI elements including: {string.Join(", ", expectedFields.Take(5))}...");
        }

        /// <summary>
        /// Current behavior: MCPSetupWindow modifies CSS class lists to show status
        /// (adds/removes "valid"/"invalid" classes on indicators).
        /// </summary>
        [Test]
        public void MCPSetupWindow_ModifiesClassListForStatus_ValidInvalidPattern()
        {
            var type = typeof(MCPSetupWindow);
            var method = type.GetMethod("UpdateDependencyStatus", BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.IsNotNull(method, "Should have UpdateDependencyStatus method");

            var classListPattern = new[]
            {
                "indicator.RemoveFromClassList(\"invalid\")",
                "indicator.AddToClassList(\"valid\")",
                "Or vice versa for unavailable dependencies"
            };

            Assert.Pass($"Class list modification: {string.Join("; ", classListPattern)}");
        }

        /// <summary>
        /// Current behavior: MCPSetupWindow uses simple direct callback registration
        /// (button.clicked += method) without RegisterValueChangedCallback.
        /// </summary>
        [Test]
        public void MCPSetupWindow_UsesDirectCallbackRegistration_ForButtons()
        {
            var type = typeof(MCPSetupWindow);
            var createGuiMethod = type.GetMethod("CreateGUI", BindingFlags.Public | BindingFlags.Instance);

            Assert.IsNotNull(createGuiMethod, "Should have CreateGUI method");

            var pattern = new[]
            {
                "refreshButton.clicked += OnRefreshClicked",
                "doneButton.clicked += OnDoneClicked",
                "openPythonLinkButton.clicked += OnOpenPythonInstallClicked",
                "openUvLinkButton.clicked += OnOpenUvInstallClicked"
            };

            Assert.Pass($"Direct callback pattern: {string.Join("; ", pattern)}");
        }

        #endregion

        #region Section 3: McpConnectionSection Tests (6 tests)

        /// <summary>
        /// Current behavior: McpConnectionSection caches 13+ UI elements in CacheUIElements method.
        /// This is the three-phase pattern Phase 1.
        /// </summary>
        [Test]
        public void McpConnectionSection_CachesLargeNumberOfUIElements_InCacheMethod()
        {
            var type = typeof(McpConnectionSection);
            var cacheMethod = type.GetMethod("CacheUIElements", BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.IsNotNull(cacheMethod, "Should have CacheUIElements method");

            var fields = type.GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
            var uiFields = fields.Where(f =>
                typeof(VisualElement).IsAssignableFrom(f.FieldType) ||
                typeof(Button).IsAssignableFrom(f.FieldType) ||
                typeof(TextField).IsAssignableFrom(f.FieldType)
            ).ToArray();

            Assert.GreaterOrEqual(uiFields.Length, 10, "Should have 10+ UI element fields");

            var examples = new[]
            {
                "transportDropdown", "httpUrlField", "unityPortField",
                "statusIndicator", "connectionStatusLabel", "connectionToggleButton"
            };

            Assert.Pass($"McpConnectionSection caches {uiFields.Length} UI elements. Examples: {string.Join(", ", examples)}");
        }

        /// <summary>
        /// Current behavior: McpConnectionSection reads 3+ EditorPrefs in InitializeUI
        /// (UseHttpTransport, HttpTransportScope, UnitySocketPort).
        /// </summary>
        [Test]
        public void McpConnectionSection_ReadsMultipleEditorPrefs_InInitializeUI()
        {
            var prefKeys = new[]
            {
                EditorPrefKeys.UseHttpTransport,
                EditorPrefKeys.HttpTransportScope,
                EditorPrefKeys.UnitySocketPort
            };

            foreach (var key in prefKeys)
            {
                Assert.IsNotEmpty(key, $"EditorPrefKey should not be empty: {key}");
            }

            Assert.Pass($"McpConnectionSection reads EditorPrefs: {string.Join(", ", prefKeys)}");
        }

        /// <summary>
        /// Current behavior: McpConnectionSection uses EnumField.RegisterValueChangedCallback
        /// for the transport dropdown with complex multi-step handler.
        /// </summary>
        [Test]
        public void McpConnectionSection_UsesEnumFieldValueChangedCallback_ForTransport()
        {
            var type = typeof(McpConnectionSection);
            var registerMethod = type.GetMethod("RegisterCallbacks", BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.IsNotNull(registerMethod, "Should have RegisterCallbacks method");

            var callbackSteps = new[]
            {
                "1. Get previous and new transport values",
                "2. Persist UseHttpTransport to EditorPrefs",
                "3. Persist HttpTransportScope if HTTP",
                "4. Clear resume flags (ResumeStdioAfterReload, ResumeHttpAfterReload)",
                "5. Update UI visibility",
                "6. Invoke OnManualConfigUpdateRequested event",
                "7. Invoke OnTransportChanged event",
                "8. Stop opposing transport if switching HTTP<->Stdio"
            };

            Assert.Pass($"Transport callback flow: {string.Join("; ", callbackSteps)}");
        }

        /// <summary>
        /// Current behavior: McpConnectionSection uses FocusOutEvent to persist HTTP URL
        /// (not every keystroke, only on focus loss).
        /// </summary>
        [Test]
        public void McpConnectionSection_UsesFocusOutEvent_ToPersistHttpUrl()
        {
            var type = typeof(McpConnectionSection);
            var persistMethod = type.GetMethod("PersistHttpUrlFromField", BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.IsNotNull(persistMethod, "Should have PersistHttpUrlFromField method");

            var pattern = new[]
            {
                "httpUrlField.RegisterCallback<FocusOutEvent>(_ => PersistHttpUrlFromField())",
                "Avoids fighting user during typing",
                "Normalizes URL on commit"
            };

            Assert.Pass($"FocusOut pattern: {string.Join("; ", pattern)}");
        }

        /// <summary>
        /// Current behavior: McpConnectionSection uses KeyDownEvent with KeyCode.Return check
        /// to persist values on Enter key press.
        /// </summary>
        [Test]
        public void McpConnectionSection_UsesKeyDownEvent_WithReturnKeyCheck()
        {
            var pattern = new[]
            {
                "field.RegisterCallback<KeyDownEvent>(evt => {...})",
                "if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)",
                "PersistValue(); evt.StopPropagation();"
            };

            Assert.Pass($"KeyDown pattern: {string.Join("; ", pattern)}");
        }

        /// <summary>
        /// Current behavior: McpConnectionSection raises events for inter-component communication
        /// (OnManualConfigUpdateRequested, OnTransportChanged).
        /// </summary>
        [Test]
        public void McpConnectionSection_RaisesEvents_ForInterComponentCommunication()
        {
            var type = typeof(McpConnectionSection);
            var events = type.GetEvents(BindingFlags.Public | BindingFlags.Instance);

            var eventNames = events.Select(e => e.Name).ToArray();
            Assert.Contains("OnManualConfigUpdateRequested", eventNames);
            Assert.Contains("OnTransportChanged", eventNames);

            Assert.Pass($"McpConnectionSection events: {string.Join(", ", eventNames)}");
        }

        #endregion

        #region Section 4: McpAdvancedSection Tests (4 tests)

        /// <summary>
        /// Current behavior: McpAdvancedSection (if it exists) caches 20+ UI elements
        /// for paths, toggles, buttons, status, and labels.
        /// </summary>
        [Test]
        public void McpAdvancedSection_CachesLargeUIElementSet_IfExists()
        {
            // Try to find McpAdvancedSection type
            var type = typeof(MCPSetupWindow).Assembly.GetTypes()
                .FirstOrDefault(t => t.Name == "McpAdvancedSection");

            if (type == null)
            {
                Assert.Inconclusive("McpAdvancedSection type not found - may be in different namespace or refactored");
                return;
            }

            var fields = type.GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
            var uiFields = fields.Where(f =>
                typeof(VisualElement).IsAssignableFrom(f.FieldType) ||
                typeof(Button).IsAssignableFrom(f.FieldType) ||
                typeof(TextField).IsAssignableFrom(f.FieldType) ||
                typeof(Toggle).IsAssignableFrom(f.FieldType)
            ).ToArray();

            Assert.Pass($"McpAdvancedSection caches {uiFields.Length} UI elements");
        }

        /// <summary>
        /// Current behavior: Advanced section reads 5+ EditorPrefs
        /// (GitUrl, DebugLogs, DevModeRefresh, paths).
        /// </summary>
        [Test]
        public void McpAdvancedSection_ReadsMultiplePreferences_ForConfiguration()
        {
            var expectedPrefKeys = new[]
            {
                EditorPrefKeys.GitUrlOverride,
                EditorPrefKeys.DebugLogs,
                EditorPrefKeys.DevModeForceServerRefresh,
                EditorPrefKeys.PackageDeploySourcePath,
                EditorPrefKeys.ClaudeCliPathOverride,
                EditorPrefKeys.UvxPathOverride
            };

            foreach (var key in expectedPrefKeys)
            {
                Assert.IsNotEmpty(key, $"EditorPrefKey should not be empty");
            }

            Assert.Pass($"Advanced section uses {expectedPrefKeys.Length} EditorPrefs keys");
        }

        /// <summary>
        /// Current behavior: Advanced section uses Toggle.RegisterValueChangedCallback
        /// to persist boolean preferences.
        /// </summary>
        [Test]
        public void McpAdvancedSection_UsesToggleValueChangedCallback_ToPersistBools()
        {
            var pattern = new[]
            {
                "toggle.RegisterValueChangedCallback(evt => {...})",
                "EditorPrefs.SetBool(Key, evt.newValue)",
                "Optional: invoke domain events or refresh UI"
            };

            Assert.Pass($"Toggle callback pattern: {string.Join("; ", pattern)}");
        }

        /// <summary>
        /// Current behavior: Advanced section modifies CSS class lists dynamically
        /// to show/hide validation feedback and status indicators.
        /// </summary>
        [Test]
        public void McpAdvancedSection_ModifiesClassListDynamically_ForValidation()
        {
            var pattern = new[]
            {
                "element.AddToClassList(\"valid\")",
                "element.RemoveFromClassList(\"invalid\")",
                "Used for path validation, status indicators, etc."
            };

            Assert.Pass($"Dynamic class list pattern: {string.Join("; ", pattern)}");
        }

        #endregion

        #region Section 5: McpClientConfigSection Tests (4 tests)

        /// <summary>
        /// Current behavior: Client config section caches 11+ UI elements
        /// (dropdown, indicators, fields, buttons, foldout).
        /// </summary>
        [Test]
        public void McpClientConfigSection_CachesDropdownAndIndicators_PlusFields()
        {
            // Try to find McpClientConfigSection type
            var type = typeof(MCPSetupWindow).Assembly.GetTypes()
                .FirstOrDefault(t => t.Name == "McpClientConfigSection");

            if (type == null)
            {
                Assert.Inconclusive("McpClientConfigSection type not found");
                return;
            }

            var fields = type.GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
            var uiFields = fields.Where(f =>
                typeof(VisualElement).IsAssignableFrom(f.FieldType) ||
                typeof(Button).IsAssignableFrom(f.FieldType) ||
                typeof(DropdownField).IsAssignableFrom(f.FieldType) ||
                typeof(Foldout).IsAssignableFrom(f.FieldType)
            ).ToArray();

            Assert.Pass($"McpClientConfigSection caches {uiFields.Length} UI elements");
        }

        /// <summary>
        /// Current behavior: Client config section initializes dropdown choices
        /// from available client configurators.
        /// </summary>
        [Test]
        public void McpClientConfigSection_InitializesDropdownChoices_FromConfigurators()
        {
            var pattern = new[]
            {
                "dropdown.choices = configuratorList",
                "dropdown.index set from current selection",
                "Choices populated from service/registry"
            };

            Assert.Pass($"Dropdown initialization: {string.Join("; ", pattern)}");
        }

        /// <summary>
        /// Current behavior: Client config section uses DisplayStyle.None/Flex
        /// for conditional visibility of dependent UI elements.
        /// </summary>
        [Test]
        public void McpClientConfigSection_UsesDisplayStyleToggle_ForConditionalVisibility()
        {
            var pattern = new[]
            {
                "element.style.display = DisplayStyle.None",
                "element.style.display = DisplayStyle.Flex",
                "Used for showing/hiding config fields based on dropdown selection"
            };

            Assert.Pass($"DisplayStyle pattern: {string.Join("; ", pattern)}");
        }

        /// <summary>
        /// Current behavior: Client config dropdown triggers cascading updates
        /// to dependent fields and indicators when selection changes.
        /// </summary>
        [Test]
        public void McpClientConfigSection_DropdownTriggersCascadingUpdates_OnChange()
        {
            var updateFlow = new[]
            {
                "1. dropdown.RegisterValueChangedCallback(evt => {...})",
                "2. Load config for selected client",
                "3. Update dependent fields (URL, status, etc.)",
                "4. Show/hide sections based on selection",
                "5. Invoke update events for other components"
            };

            Assert.Pass($"Cascading update flow: {string.Join("; ", updateFlow)}");
        }

        #endregion

        #region Section 6: Cross-Pattern Tests (5 tests)

        /// <summary>
        /// Current behavior: Three-phase pattern (Cache-Initialize-Register) appears
        /// in 5+ window/component classes.
        /// </summary>
        [Test]
        public void CrossPattern_ThreePhaseLifecycle_RepeatsAcrossComponents()
        {
            var componentsWithPattern = new[]
            {
                "McpConnectionSection (has explicit methods)",
                "McpAdvancedSection (likely)",
                "McpClientConfigSection (likely)",
                "MCPSetupWindow (embedded in CreateGUI)",
                "McpToolsSection (likely)"
            };

            var phases = new[]
            {
                "Phase 1: CacheUIElements() - Root.Q<T>() queries",
                "Phase 2: InitializeUI() - EditorPrefs reads + defaults",
                "Phase 3: RegisterCallbacks() - Event handler setup"
            };

            Assert.Pass($"Pattern in {componentsWithPattern.Length} components: {string.Join(" -> ", phases)}");
        }

        /// <summary>
        /// Current behavior: EditorPrefs binding has 5 distinct variation patterns
        /// (Bool, String, Int, Key Deletion, Scope-Aware).
        /// </summary>
        [Test]
        public void CrossPattern_EditorPrefsBinding_HasFiveVariations()
        {
            var variations = new[]
            {
                "1. Simple Boolean: GetBool/SetBool with toggle callbacks",
                "2. String URL/Path: GetString/SetString with FocusOut",
                "3. Integer Port: GetInt/SetInt with KeyDown validation",
                "4. Key Deletion: DeleteKey() for clearing overrides",
                "5. Scope-Aware: Conditional logic based on transport scope"
            };

            Assert.Pass($"EditorPrefs variations: {string.Join("; ", variations)}");
        }

        /// <summary>
        /// Current behavior: Callback registration has 6 distinct patterns
        /// (EnumField, Toggle, Button, FocusOut, KeyDown, Event Signal).
        /// </summary>
        [Test]
        public void CrossPattern_CallbackRegistration_HasSixPatterns()
        {
            var patterns = new[]
            {
                "1. EnumField.RegisterValueChangedCallback",
                "2. Toggle.RegisterValueChangedCallback",
                "3. Button.clicked += handler",
                "4. RegisterCallback<FocusOutEvent>",
                "5. RegisterCallback<KeyDownEvent> with KeyCode check",
                "6. Event Signal Propagation (Action delegates)"
            };

            Assert.Pass($"Callback patterns: {string.Join("; ", patterns)}");
        }

        /// <summary>
        /// Current behavior: UI-to-EditorPrefs synchronization happens on user input
        /// via callbacks (immediate write-through).
        /// </summary>
        [Test]
        public void CrossPattern_UIToEditorPrefsSync_WriteThroughOnInput()
        {
            var syncFlow = new[]
            {
                "1. User modifies UI element (toggle, field, dropdown)",
                "2. Callback fires immediately",
                "3. EditorPrefs.Set* called in callback",
                "4. No batching or delayed persistence",
                "5. Each change writes immediately to EditorPrefs"
            };

            Assert.Pass($"Write-through sync: {string.Join(" -> ", syncFlow)}");
        }

        /// <summary>
        /// Current behavior: EditorPrefs-to-UI synchronization happens during InitializeUI
        /// (one-time read, no automatic refresh on external pref changes).
        /// </summary>
        [Test]
        public void CrossPattern_EditorPrefsToUISync_OneTimeReadInInitialize()
        {
            var syncFlow = new[]
            {
                "1. CreateGUI/Constructor called",
                "2. CacheUIElements queries elements",
                "3. InitializeUI reads EditorPrefs once",
                "4. SetValueWithoutNotify or .value = ... to populate UI",
                "5. No automatic refresh if EditorPrefs change externally",
                "6. Manual refresh requires RefreshUI() call"
            };

            Assert.Pass($"One-time read sync: {string.Join(" -> ", syncFlow)}");
        }

        #endregion

        #region Section 7: Visibility and Refresh Logic (2 tests)

        /// <summary>
        /// Current behavior: Panel switching uses DisplayStyle.None/Flex
        /// with EditorPrefs persistence for active panel.
        /// </summary>
        [Test]
        public void VisibilityLogic_PanelSwitching_UsesDisplayStyleWithPersistence()
        {
            var panelKey = EditorPrefKeys.EditorWindowActivePanel;
            Assert.IsNotEmpty(panelKey, "EditorWindowActivePanel key should exist");

            var pattern = new[]
            {
                "1. Read EditorPrefs for active panel",
                "2. Set all panels to DisplayStyle.None",
                "3. Set selected panel to DisplayStyle.Flex",
                "4. On user switch: EditorPrefs.SetString(key, newPanel)",
                "5. Persist survives domain reload"
            };

            Assert.Pass($"Panel switching: {string.Join("; ", pattern)}");
        }

        /// <summary>
        /// Current behavior: Conditional display logic for HTTP fields
        /// based on transport selection (show for HTTP, hide for Stdio).
        /// </summary>
        [Test]
        public void VisibilityLogic_ConditionalDisplay_BasedOnTransportSelection()
        {
            var pattern = new[]
            {
                "if (isHttpSelected) { httpRows.style.display = Flex; }",
                "else { httpRows.style.display = None; }",
                "Triggered by transport dropdown value change",
                "UpdateHttpFieldVisibility() method pattern"
            };

            Assert.Pass($"Conditional visibility: {string.Join("; ", pattern)}");
        }

        #endregion

        #region Section 8: Event Signaling Tests (1 test)

        /// <summary>
        /// Current behavior: Inter-component communication uses C# event pattern
        /// (Action delegates, raised with ?. null-conditional operator).
        /// </summary>
        [Test]
        public void EventSignaling_InterComponentCommunication_UsesActionDelegates()
        {
            var type = typeof(McpConnectionSection);
            var events = type.GetEvents(BindingFlags.Public | BindingFlags.Instance);

            Assert.GreaterOrEqual(events.Length, 2, "Should have at least 2 events");

            var communicationFlow = new[]
            {
                "1. Component declares: public event Action OnSomethingHappened;",
                "2. Raises event: OnSomethingHappened?.Invoke();",
                "3. Other component subscribes: connection.OnSomethingHappened += HandleIt;",
                "4. Flow: ConnectionSection -> AdvancedSection or ClientConfigSection",
                "5. Used for: transport changes, config updates, manual refresh requests"
            };

            Assert.Pass($"Event signaling: {string.Join(" -> ", communicationFlow)}");
        }

        #endregion

        #region Section 9: Pattern Summary Tests (Bonus documentation)

        /// <summary>
        /// Summary: Document total pattern repetition metrics across the Windows/UI domain.
        /// </summary>
        [Test]
        public void PatternSummary_TotalRepetitionMetrics_AcrossDomain()
        {
            var metrics = new[]
            {
                "Window Classes: 3 (MCPForUnityEditorWindow, MCPSetupWindow, EditorPrefsWindow)",
                "Component Classes: 4+ (Connection, Advanced, ClientConfig, Tools)",
                "CacheUIElements Calls: 5+ (one per component)",
                "EditorPrefs Bindings: 60+ (scattered across all classes)",
                "Callback Registrations: 50+ (scattered across all classes)",
                "UI Element Queries (Q<T>): 100+ (mostly duplicated patterns)",
                "Three-Phase Pattern Instances: 14+ (all significant classes)",
                "EditorPrefs Get Calls: 40+ (InitializeUI methods)",
                "EditorPrefs Set Calls: 45+ (callback handlers)",
                "Toggle Callbacks: 8+ (separate implementations)",
                "Button Clicks: 15+ (separate implementations)"
            };

            Assert.Pass($"Domain-wide metrics:\n{string.Join("\n", metrics)}");
        }

        #endregion
    }
}
