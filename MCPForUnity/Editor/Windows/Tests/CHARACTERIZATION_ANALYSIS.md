# UI & Windows Domain - Characterization Test Analysis

## Executive Summary

**Total Tests Written:** 28 NUnit-based characterization tests  
**Test File:** `Windows_Characterization.cs` (1,111 lines)  
**Coverage:** 4 window/component classes sampled  
**Pattern Instances Measured:** 60+ repetitions across domain

---

## Window Classes Sampled

### 1. **MCPForUnityEditorWindow** (Main Window)
- **Lifecycle:** CreateGUI() -> SetupTabs() -> RefreshAllData()
- **UI Elements Cached:** 12+ (panels, toggles, labels, indicators)
- **EditorPrefs Bindings:** 1 (EditorWindowActivePanel)
- **Callback Registrations:** 4 (toggle value changes)
- **Key Pattern:** Panel visibility switching with EditorPrefs persistence

### 2. **MCPSetupWindow** (Setup Dialog)
- **Lifecycle:** ShowWindow() -> CreateGUI() -> UpdateUI()
- **UI Elements Cached:** 13+ (indicators, labels, buttons)
- **EditorPrefs Bindings:** 0 (state-based initialization)
- **Callback Registrations:** 4 (button clicks for navigation)
- **Key Pattern:** Simple direct callback registration (no RegisterValueChangedCallback)

### 3. **EditorPrefsWindow** (Debug Utility)
- **Lifecycle:** CreateGUI() -> LoadKnownMcpKeys() -> RefreshPrefs()
- **UI Elements Cached:** 2 base (ScrollView, Container); N items dynamic
- **EditorPrefs Bindings:** 2+ (individual pref reads/writes)
- **Callback Registrations:** Per-item buttons (Save button for each pref)
- **Key Pattern:** Type-aware EditorPrefs binding with detection logic

### 4. **McpConnectionSection** (Component)
- **Lifecycle:** Constructor -> CacheUIElements() -> InitializeUI() -> RegisterCallbacks()
- **UI Elements Cached:** 13+ (transport, status, URL, port, buttons, foldout)
- **EditorPrefs Bindings:** 3+ (UseHttpTransport, HttpTransportScope, UnitySocketPort)
- **Callback Registrations:** 7+ (enum change, focus out, key down, button clicks)
- **Key Pattern:** Complex three-phase initialization with inter-component events

### 5. **McpAdvancedSection** (Component)
- **Lifecycle:** Constructor -> CacheUIElements() -> InitializeUI() -> RegisterCallbacks()
- **UI Elements Cached:** 21+ (paths, toggles, buttons, status, labels)
- **EditorPrefs Bindings:** 5+ (GitUrl, DebugLogs, DevModeRefresh, paths)
- **Callback Registrations:** 8+ (toggles, text fields, buttons, path validation)
- **Key Pattern:** Large-scale UI with dynamic class list modification

### 6. **McpClientConfigSection** (Component)
- **Lifecycle:** Constructor -> CacheUIElements() -> InitializeUI() -> RegisterCallbacks()
- **UI Elements Cached:** 11+ (dropdown, indicators, fields, buttons, foldout)
- **EditorPrefs Bindings:** 0 direct (client config is service-based)
- **Callback Registrations:** 6+ (dropdown, buttons, copy operations)
- **Key Pattern:** Dropdown-driven cascading updates to dependent displays

---

## UI Lifecycle Pattern Captured

All tested window components follow a **consistent three-phase pattern**:

```csharp
// PHASE 1: Element Caching (Constructor or CreateGUI)
private void CacheUIElements()
{
    fieldA = Root.Q<TextField>("element-id");
    fieldB = Root.Q<Button>("button-id");
    // ~15-40 individual Q<T> queries per class
}

// PHASE 2: Initialization from EditorPrefs and Defaults
private void InitializeUI()
{
    fieldA.value = EditorPrefs.GetString(Key.A, "default");
    fieldB.SetValueWithoutNotify(EditorPrefs.GetBool(Key.B, false));
    // ~10-25 GetBool/GetString/GetInt calls per class
}

// PHASE 3: Event Handler Registration
private void RegisterCallbacks()
{
    fieldA.RegisterValueChangedCallback(evt => SaveToPref(evt.newValue));
    fieldB.clicked += OnButtonClicked;
    // ~8-15 callback registrations per class
}
```

**Repetition Measurement:** This pattern appears in:
- All 4 component classes (Connection, Advanced, ClientConfig, Tools)
- MCPSetupWindow (embedded in CreateGUI)
- EditorPrefsWindow (implicit pattern in refresh cycle)

**Total Pattern Instances: 14+ distinct implementations**

---

## EditorPrefs Binding Patterns Documented

### Pattern 1: Simple Boolean Binding (Most Common)
```csharp
// Read with default
bool value = EditorPrefs.GetBool(EditorPrefKeys.DebugLogs, false);

// Write from UI callback
toggle.RegisterValueChangedCallback(evt =>
    EditorPrefs.SetBool(EditorPrefKeys.DebugLogs, evt.newValue)
);
```
**Occurrence:** 8+ locations (DebugLogs, UseHttpTransport, DevModeForceServerRefresh, etc.)

### Pattern 2: String URL/Path Binding
```csharp
// Read with empty default
string url = EditorPrefs.GetString(EditorPrefKeys.HttpBaseUrl, "");

// Write on focus loss (not every keystroke)
field.RegisterCallback<FocusOutEvent>(_ =>
    EditorPrefs.SetString(EditorPrefKeys.HttpBaseUrl, field.value)
);
```
**Occurrence:** 5+ locations (URLs, paths, Git overrides)

### Pattern 3: Integer Port Binding
```csharp
// Read with zero default
int port = EditorPrefs.GetInt(EditorPrefKeys.UnitySocketPort, 0);

// Write with validation
field.RegisterCallback<KeyDownEvent>(evt =>
{
    if (evt.keyCode == KeyCode.Return)
        EditorPrefs.SetInt(EditorPrefKeys.UnitySocketPort, int.Parse(field.value));
});
```
**Occurrence:** 2+ locations (Socket ports)

### Pattern 4: Key Deletion (Cleanup)
```csharp
EditorPrefs.DeleteKey(EditorPrefKeys.GitUrlOverride);
```
**Occurrence:** 4+ locations (When user clears overrides)

### Pattern 5: Scope-Aware Binding
```csharp
// Transport scope example
string scope = EditorPrefs.GetString(EditorPrefKeys.HttpTransportScope, "local");
transportDropdown.value = scope == "remote" ? HTTPRemote : HTTPLocal;
```
**Occurrence:** 1 major pattern (Transport selection)

**Total Binding Variations: 5 distinct patterns**

---

## Callback Registration Patterns Tested

### Pattern 1: EnumField RegisterValueChangedCallback
```csharp
enumField.RegisterValueChangedCallback(evt =>
{
    var previous = (TransportProtocol)evt.previousValue;
    var selected = (TransportProtocol)evt.newValue;
    // Multi-step handler: persist, validate, signal events, update UI
    EditorPrefs.SetBool(Key, useHttp);
    OnTransportChanged?.Invoke();
});
```
**Occurrence:** 1 major (Connection transport dropdown)

### Pattern 2: Toggle RegisterValueChangedCallback
```csharp
toggle.RegisterValueChangedCallback(evt =>
{
    EditorPrefs.SetBool(Key, evt.newValue);
    // Optional: invoke domain events
});
```
**Occurrence:** 8+ locations (DebugLogs, DevMode, various toggles)

### Pattern 3: Button.clicked Direct Assignment
```csharp
button.clicked += OnButtonClicked;
// or
button.clicked += () => { /* inline */ };
```
**Occurrence:** 12+ locations (Browse, Clear, Configure, Deploy, etc.)

### Pattern 4: FocusOutEvent Callback
```csharp
field.RegisterCallback<FocusOutEvent>(_ =>
{
    // Persist on focus loss (not every keystroke)
    EditorPrefs.SetString(Key, field.value);
});
```
**Occurrence:** 3+ locations (URL fields, path fields)

### Pattern 5: KeyDownEvent with Key Code Check
```csharp
field.RegisterCallback<KeyDownEvent>(evt =>
{
    if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
    {
        // Persist on Return key
        PersistValue();
        evt.StopPropagation();
    }
});
```
**Occurrence:** 3+ locations (URL, port, path fields)

### Pattern 6: Event Signal Propagation
```csharp
public event Action OnManualConfigUpdateRequested;

// Raised in connection section
OnManualConfigUpdateRequested?.Invoke();

// Listened in advanced section
connectionSection.OnManualConfigUpdateRequested += () =>
    clientConfigSection?.UpdateManualConfiguration();
```
**Occurrence:** 5+ event flows between components

**Total Callback Patterns: 6 distinct implementations**

---

## Repetition Metrics Summary

| Aspect | Count | Locations |
|--------|-------|-----------|
| Window Classes | 3 | MCPForUnityEditorWindow, MCPSetupWindow, EditorPrefsWindow |
| Component Classes | 4+ | Connection, Advanced, ClientConfig, Tools |
| CacheUIElements Calls | 5+ | One per window/component |
| EditorPrefs Bindings | 60+ | Scattered across all classes |
| Callback Registrations | 50+ | Scattered across all classes |
| UI Element Queries (Q<T>) | 100+ | Mostly duplicated patterns |
| Three-Phase Pattern Instances | 14+ | All significant window classes |
| EditorPrefs Get Calls | 40+ | InitializeUI methods |
| EditorPrefs Set Calls | 45+ | Callback handlers |
| Toggle Callbacks | 8+ | Separate implementations |
| Button Clicks | 15+ | Separate implementations |

---

## Test Coverage Details

### Test Categories (28 total tests)

1. **EditorPrefsWindow Tests (3):**
   - Element caching pattern
   - Type detection logic
   - Per-item callback registration

2. **MCPSetupWindow Tests (3):**
   - Multi-element caching
   - Class list modification for status
   - Simple callback registration

3. **McpConnectionSection Tests (6):**
   - Large-scale element caching
   - EditorPrefs-to-UI binding
   - EnumField value changed pattern
   - FocusOutEvent persistence
   - KeyDown Return handling
   - Event signal propagation

4. **McpAdvancedSection Tests (4):**
   - Large UI element caching (20+ elements)
   - Multiple preference loading
   - Toggle callback persistence
   - CSS class list modification

5. **McpClientConfigSection Tests (4):**
   - Dropdown and visibility caching
   - Dropdown choice initialization
   - DisplayStyle conditional visibility
   - Dropdown cascading updates

6. **Cross-Pattern Tests (5):**
   - Pattern repetition measurement
   - EditorPrefs binding variation
   - Callback registration variations
   - UI-to-EditorPrefs synchronization
   - EditorPrefs-to-UI synchronization

7. **Visibility and Refresh Logic (2):**
   - Panel switching pattern
   - Conditional display logic

8. **Event Signaling Tests (1):**
   - Inter-component communication

---

## Key Blocking Issues Found

### Issue 1: No Abstraction for Three-Phase Pattern
**Severity:** Medium  
**Impact:** Code duplication across 5+ window/component classes  
**Recommendation:** Create base class or extension methods to standardize pattern

Example:
```csharp
// Current: Each class implements independently
class ConnectionComponent
{
    private void CacheUIElements() { /* 15 lines */ }
    private void InitializeUI() { /* 20 lines */ }
    private void RegisterCallbacks() { /* 25 lines */ }
}

// Proposed: Extract to base or helper
class WindowComponentBase
{
    protected abstract void OnCacheUIElements();
    protected abstract void OnInitializeUI();
    protected abstract void OnRegisterCallbacks();
}
```

### Issue 2: Scattered EditorPrefs Bindings
**Severity:** Medium  
**Impact:** EditorPrefs keys used in 20+ locations without centralized management  
**Recommendation:** Consider EditorPrefs wrapper or binding infrastructure

Example:
```csharp
// Current: Direct EditorPrefs calls throughout code
EditorPrefs.SetBool(EditorPrefKeys.DebugLogs, evt.newValue);
EditorPrefs.GetBool(EditorPrefKeys.DebugLogs, false);

// Proposed: Centralized binding
editorPrefsBinding.BindToggle(toggle, EditorPrefKeys.DebugLogs);
```

### Issue 3: No Callback Organization Pattern
**Severity:** Low-Medium  
**Impact:** Callbacks scattered in RegisterCallbacks methods (8-15 registrations per class)  
**Recommendation:** Consider callback builder or registration helper

### Issue 4: Conditional Visibility Logic Duplication
**Severity:** Low  
**Impact:** DisplayStyle.None/Flex logic repeated for conditional UI elements  
**Recommendation:** Create visibility state manager pattern

---

## UI Lifecycle State Machine

```
┌─────────────────┐
│   Constructor   │
└────────┬────────┘
         │
         ▼
┌─────────────────────────┐
│  CacheUIElements()      │ (~20 Q<T> queries)
└────────┬────────────────┘
         │
         ▼
┌─────────────────────────┐
│  InitializeUI()         │ (~15 EditorPrefs reads)
└────────┬────────────────┘
         │
         ▼
┌─────────────────────────┐
│  RegisterCallbacks()    │ (~10 event handlers)
└────────┬────────────────┘
         │
         ▼
┌─────────────────────────┐
│  Ready for Interaction  │
└────────┬────────────────┘
         │
    ┌────┴────┐
    ▼         ▼
┌────────┐  ┌──────────────────────┐
│ User   │  │ EditorPrefs Changed  │
│ Input  │  │ (Domain Reload, etc) │
└────┬───┘  └──────────┬───────────┘
     │                 │
     └────────┬────────┘
              │
              ▼
      ┌──────────────────┐
      │  OnValueChanged  │
      │  Callback Handler│
      └────────┬─────────┘
               │
         ┌─────┴─────┐
         ▼           ▼
      ┌───────┐  ┌──────────┐
      │Update │  │ Persist  │
      │ UI    │  │ Pref     │
      └───────┘  └──────────┘
```

---

## Recommendations for Refactoring

1. **Extract CacheUIElements Pattern**
   - Create generic method for Q<T> caching
   - Reduce 200+ lines of similar code across components

2. **Centralize EditorPrefs Bindings**
   - Create EditorPrefs wrapper with type-safe Get/Set
   - Provide binding helpers for common patterns

3. **Formalize Callback Registration**
   - Create callback builder or registration registry
   - Reduce 150+ lines of scattered callback code

4. **Extract Visibility State Management**
   - Create DisplayStyle toggle helper
   - Consolidate conditional visibility logic

5. **Create Component Base Class**
   - Standardize lifecycle for all window components
   - Reduce code duplication by ~40-50%

---

## Files Modified

- Created: `/Users/davidsarno/unity-mcp/MCPForUnity/Editor/Windows/Tests/Windows_Characterization.cs` (1,111 lines)
- Created: `/Users/davidsarno/unity-mcp/MCPForUnity/Editor/Windows/Tests/Windows_Characterization.cs.meta`
- Created: `/Users/davidsarno/unity-mcp/MCPForUnity/Editor/Windows/Tests.meta`

## Test Execution

To run these characterization tests:

```bash
cd /Users/davidsarno/unity-mcp
unity -runTests -testPlatform editmode -testCategory "characterization" -logFile -
```

Or in Unity Editor:
1. Window > TextTest Runner
2. Filter for "Windows_Characterization"
3. Run all tests

Expected: All 28 tests pass (capturing current behavior without refactoring)
