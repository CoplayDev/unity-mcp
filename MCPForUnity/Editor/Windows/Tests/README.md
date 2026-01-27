# UI & Windows Domain - Characterization Tests

## Overview

This directory contains comprehensive characterization tests for the **UI & Windows domain** of the unity-mcp repository. These tests capture the CURRENT behavior patterns without making any refactoring changes.

## What Are Characterization Tests?

Characterization tests are used to document and verify the current behavior of a system. They serve as a baseline for understanding:
- How the system currently works
- What patterns and conventions are used
- Where code is repetitive or could be refactored
- How different components interact

These tests are NOT intended to be "perfect" tests—they intentionally capture existing behavior, even if that behavior could be improved.

## Files

- **`Windows_Characterization.cs`** - Main test file with 28 NUnit test cases
- **`CHARACTERIZATION_ANALYSIS.md`** - Detailed analysis of patterns, metrics, and findings
- **`README.md`** - This file

## Quick Stats

| Metric | Value |
|--------|-------|
| Total Tests | 28 |
| Lines of Test Code | 1,111 |
| Window Classes Sampled | 4 (+ 2 more analyzed) |
| UI Lifecycle Patterns Captured | 3-phase pattern (CacheUIElements → InitializeUI → RegisterCallbacks) |
| EditorPrefs Binding Patterns | 5 distinct variations |
| Callback Registration Patterns | 6 distinct patterns |
| Total Code Repetitions Measured | 60+ instances |

## Window Classes Tested

### Direct Test Coverage (4 classes)

1. **EditorPrefsWindow**
   - Tests: 3
   - Pattern Focus: Type-aware EditorPrefs binding, per-item callback registration
   - Key Method: CreateGUI → RefreshPrefs → CreateItemUI

2. **MCPSetupWindow**
   - Tests: 3
   - Pattern Focus: Simple direct callback registration, class list modification
   - Key Method: CreateGUI → UpdateUI

3. **McpConnectionSection** (Component)
   - Tests: 6
   - Pattern Focus: Complex three-phase initialization, EnumField callbacks, focus/key events
   - Key Methods: CacheUIElements → InitializeUI → RegisterCallbacks

4. **McpAdvancedSection** (Component)
   - Tests: 4
   - Pattern Focus: Large-scale UI caching (20+ elements), multiple EditorPrefs, class list manipulation
   - Key Methods: CacheUIElements → InitializeUI → RegisterCallbacks

5. **McpClientConfigSection** (Component)
   - Tests: 4
   - Pattern Focus: Dropdown-driven updates, conditional visibility, cascading changes
   - Key Methods: CacheUIElements → InitializeUI → RegisterCallbacks

### Additional Analysis (2 classes)

6. **MCPForUnityEditorWindow** (Main Window)
   - Analyzed but not directly tested (would require full window mocking)
   - Pattern: Panel visibility switching with EditorPrefs persistence

7. **McpToolsSection, McpValidationSection**
   - Referenced but not directly tested
   - Follow same three-phase pattern as Connection/Advanced

## Test Organization

### Test Categories

```
WindowsCharacterizationTests (class)
├── EditorPrefsWindow Tests (3 tests)
│   ├── CacheUIElements
│   ├── Type Detection & Known Types
│   └── Per-Item Callback Registration
├── MCPSetupWindow Tests (3 tests)
│   ├── Multi-Element Caching
│   ├── Class List Modification
│   └── Button Click Registration
├── McpConnectionSection Tests (6 tests)
│   ├── UI Element Caching
│   ├── EditorPrefs-to-UI Binding
│   ├── EnumField Value Changed
│   ├── FocusOut Event Handling
│   ├── KeyDown Return Handling
│   └── Event Signal Propagation
├── McpAdvancedSection Tests (4 tests)
│   ├── Large-Scale Element Caching
│   ├── Multiple Preference Loading
│   ├── Toggle Callback Persistence
│   └── CSS Class List Modification
├── McpClientConfigSection Tests (4 tests)
│   ├── Dropdown & Visibility Caching
│   ├── Dropdown Initialization
│   ├── DisplayStyle Conditional Visibility
│   └── Dropdown Cascading Updates
├── Cross-Pattern Tests (5 tests)
│   ├── Pattern Repetition Measurement
│   ├── EditorPrefs Binding Variations
│   ├── Callback Registration Variations
│   ├── UI-to-EditorPrefs Synchronization
│   └── EditorPrefs-to-UI Synchronization
├── Visibility & Refresh Tests (2 tests)
│   ├── Panel Switching Pattern
│   └── Conditional Display Logic
└── Event Signaling Tests (1 test)
    └── Inter-Component Communication
```

## Key Patterns Documented

### 1. Window Lifecycle Pattern (Found in 5+ classes)

```csharp
// PHASE 1: Caching
private void CacheUIElements()
{
    field1 = Root.Q<TextField>("id1");      // ~15-40 queries per class
    field2 = Root.Q<Button>("id2");
    // ...
}

// PHASE 2: Initialization
private void InitializeUI()
{
    field1.value = EditorPrefs.GetString(Key, "default");  // ~10-25 pref reads
    field2.SetValueWithoutNotify(true);
    // ...
}

// PHASE 3: Callback Registration
private void RegisterCallbacks()
{
    field1.RegisterValueChangedCallback(evt => { });       // ~8-15 handlers
    field2.clicked += OnClick;
    // ...
}
```

**Repetition Count:** 14+ distinct implementations (reduces to ~40-50% with abstraction)

### 2. EditorPrefs Binding Patterns (5 variations)

1. **Boolean Binding** (8+ locations)
   ```csharp
   EditorPrefs.GetBool(Key, false);
   EditorPrefs.SetBool(Key, value);
   ```

2. **String Binding** (5+ locations)
   ```csharp
   EditorPrefs.GetString(Key, "");
   EditorPrefs.SetString(Key, value);
   ```

3. **Integer Binding** (2+ locations)
   ```csharp
   EditorPrefs.GetInt(Key, 0);
   EditorPrefs.SetInt(Key, value);
   ```

4. **Key Deletion** (4+ locations)
   ```csharp
   EditorPrefs.DeleteKey(Key);
   ```

5. **Scope-Aware Binding** (1+ pattern)
   ```csharp
   string scope = EditorPrefs.GetString(Key, "default");
   ui.value = TranslateScope(scope);
   ```

### 3. Callback Registration Patterns (6 variations)

1. **EnumField RegisterValueChangedCallback** (1 major)
2. **Toggle RegisterValueChangedCallback** (8+ locations)
3. **Button.clicked Direct Assignment** (12+ locations)
4. **FocusOutEvent Callback** (3+ locations)
5. **KeyDownEvent with Key Code Check** (3+ locations)
6. **Event Signal Propagation** (5+ event flows)

## Running the Tests

### In Unity Editor
1. Open **Window > TextTest Runner**
2. Search for "Windows_Characterization"
3. Click "Run"

### Command Line
```bash
# Run all characterization tests
unity -runTests -testPlatform editmode -testClass WindowsCharacterizationTests -logFile -

# Run specific test
unity -runTests -testPlatform editmode -testClass WindowsCharacterizationTests -testMethod WindowsCharacterizationTests.EditorPrefsWindow_CacheUIElements_CachesScrollViewAndContainer -logFile -
```

### In JetBrains Rider
1. Right-click on test class or method
2. Select "Run" or "Run with Coverage"

## Test Structure

Each test follows a consistent AAA (Arrange-Act-Assert) pattern:

```csharp
[Test]
public void TestName()
{
    // Arrange: Set up test data and mocks
    var element = new VisualElement();
    
    // Act: Execute the behavior being tested
    element.AddToClassList("test-class");
    
    // Assert: Verify expected outcome
    Assert.True(element.ClassListContains("test-class"));
}
```

## Key Findings

### Measured Repetition

| Aspect | Count |
|--------|-------|
| CacheUIElements implementations | 5+ |
| InitializeUI implementations | 5+ |
| RegisterCallbacks implementations | 5+ |
| EditorPrefs Get calls | 40+ |
| EditorPrefs Set calls | 45+ |
| UI element Q<T> queries | 100+ |
| Toggle callbacks | 8+ |
| Button click handlers | 15+ |

### Blocking Issues Identified

1. **No abstraction for three-phase pattern** (Medium severity)
   - Could reduce code by 200+ lines

2. **Scattered EditorPrefs bindings** (Medium severity)
   - 60+ EditorPrefs operations without centralized management

3. **No callback organization pattern** (Low-medium severity)
   - 50+ callback registrations without systematic structure

4. **Conditional visibility logic duplication** (Low severity)
   - DisplayStyle.None/Flex repeated across components

## Use Cases

These characterization tests are useful for:

1. **Understanding current behavior** before making changes
2. **Detecting regressions** during refactoring
3. **Documenting patterns** that developers should follow
4. **Measuring code duplication** in the domain
5. **Creating a baseline** for future improvements
6. **Training new team members** on existing patterns

## Next Steps

After running these characterization tests, consider:

1. **Refactoring Window Lifecycle**
   - Extract three-phase pattern into base class
   - Reduce code duplication by 40-50%

2. **Centralizing EditorPrefs**
   - Create EditorPrefs wrapper or binding infrastructure
   - Reduce scattered calls from 60+ to <15

3. **Formalizing Callback Registration**
   - Create callback builder or registration helper
   - Reduce scattered code by organizing 50+ registrations

4. **Creating Visibility State Manager**
   - Consolidate DisplayStyle logic
   - Reduce conditional visibility duplication

## References

- Main window implementation: `/MCPForUnity/Editor/Windows/MCPForUnityEditorWindow.cs`
- Setup window: `/MCPForUnity/Editor/Windows/MCPSetupWindow.cs`
- EditorPrefs window: `/MCPForUnity/Editor/Windows/EditorPrefs/EditorPrefsWindow.cs`
- Connection component: `/MCPForUnity/Editor/Windows/Components/Connection/McpConnectionSection.cs`
- Advanced component: `/MCPForUnity/Editor/Windows/Components/Advanced/McpAdvancedSection.cs`
- Client config component: `/MCPForUnity/Editor/Windows/Components/ClientConfig/McpClientConfigSection.cs`

## Notes

- All tests use NUnit framework (standard for Unity)
- Tests use mocked VisualElement objects (no actual UI rendering)
- Tests capture CURRENT behavior without making improvements
- Tests verify implementation details, not just public contracts
- EditorPrefs are cleared between tests to ensure isolation
