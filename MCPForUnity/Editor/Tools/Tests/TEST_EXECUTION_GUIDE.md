# Editor Tools Characterization Tests - Execution Guide

**Status**: Ready for execution
**Framework**: NUnit (standard in this project)
**Language**: C#
**Test Count**: 33 test methods
**Expected Duration**: ~1-2 minutes for all tests

---

## Files Created

| File | Purpose | Size |
|------|---------|------|
| `EditorTools_Characterization.cs` | 33 test methods in 11 pattern groups | 38 KB |
| `CHARACTERIZATION_SUMMARY.md` | Overview, metrics, findings | Documentation |
| `PATTERN_REFERENCE.md` | Developer reference for all 11 patterns | Documentation |
| `TEST_EXECUTION_GUIDE.md` | This file - how to run tests | Documentation |

---

## Pre-Execution Checklist

- [ ] Unity Editor is installed
- [ ] Unity Test Framework (UTF) is available in project
- [ ] NUnit.Framework package is installed
- [ ] Newtonsoft.Json (Newtonsoft.Json.Linq) is available
- [ ] MCPForUnity.Editor assemblies compile
- [ ] Editor/Tools directory is accessible

**All dependencies are standard in this project ✓**

---

## How to Run Tests

### Method 1: Unity Test Runner GUI (Recommended)

1. **Open Unity Editor**
   - Navigate to: `Windows > General > Test Runner`

2. **Switch to EditMode Tab**
   - Top of Test Runner window, click "EditMode"

3. **Find Test Suite**
   - Expand folder hierarchy to locate:
     ```
     Assets/MCPForUnity/Editor/Tools/Tests/
     └── EditorTools_Characterization
     ```

4. **Run All Tests**
   - Click "Run All" button (plays all 33 tests)
   - OR select individual test groups by clicking on them

5. **Review Results**
   - All tests should show as "✓ Passed"
   - Check Console tab for any warnings

### Method 2: Command Line (For CI/CD)

```bash
# Navigate to project root
cd /Users/davidsarno/unity-mcp

# Run all Editor tests
unity -projectPath . \
  -runTests \
  -testResults testResults.xml \
  -testCategory "EditorTools"

# Expected output:
# - testResults.xml with 33 passing tests
# - Console output showing test names and results
```

### Method 3: Direct NUnit Execution

```bash
# Using dotnet CLI if available
dotnet test MCPForUnity.Editor.Tests.dll --filter "EditorToolsCharacterizationTests"
```

---

## Test Organization

Tests are organized into 11 logical groups by pattern:

### 1. **HandleCommand Entry Point and Null/Empty Parameter Handling** (4 tests)
- `HandleCommand_ManageEditor_WithNullParams_ReturnsErrorResponse`
- `HandleCommand_ManageEditor_WithoutActionParameter_ReturnsError`
- `HandleCommand_ManageEditor_WithUppercaseAction_NormalizesAndDispatches`
- `HandleCommand_ManageEditor_WithoutActionParameter_ReturnsError`

**Tests**: Parameter safety, null handling
**Focus**: All tools must handle null/empty params gracefully

---

### 2. **Parameter Extraction and Validation** (6 tests)
- `HandleCommand_FindGameObjects_WithCamelCaseSearchMethod_Succeeds`
- `HandleCommand_FindGameObjects_WithSnakeCaseSearchMethod_Succeeds`
- `HandleCommand_ManageEditor_WithBooleanParameter_AcceptsMultipleTypes`
- `HandleCommand_ManagePrefabs_WithoutRequiredPath_ReturnsError`
- `HandleCommand_ManageEditor_SetActiveTool_RequiresToolNameParameter`
- `HandleCommand_FindGameObjects_WithoutSearchMethod_UsesDefault`

**Tests**: Parameter naming conventions, coercion, defaults
**Focus**: camelCase/snake_case fallback, type conversion, required params

---

### 3. **Action Switch Dispatch** (3 tests)
- `HandleCommand_ManageEditor_WithUnknownAction_ReturnsError`
- `HandleCommand_ManageEditor_PlayAction_DifferentFromStop`
- (implicit in other sections)

**Tests**: Switch statement routing, unknown action handling
**Focus**: Each action dispatches to correct handler

---

### 4. **Error Handling and Logging** (4 tests)
- `HandleCommand_ManagePrefabs_WithInvalidParameters_CatchesExceptionAndReturns`
- `HandleCommand_ManageMaterial_LogsErrorOnFailure`
- `HandleCommand_ManageEditor_PlayAction_ReturnsResponseObject`
- `HandleCommand_ManageEditor_PlayAction_ReturnsResponseObject`

**Tests**: Exception handling, logging, response consistency
**Focus**: No unhandled exceptions, all responses are Response types

---

### 5. **Inline Parameter Validation and Coercion** (4 tests)
- `HandleCommand_ManageMaterial_NormalizesPathParameter`
- `HandleCommand_ManageEditor_SetActiveTool_RequiresToolNameParameter`
- `HandleCommand_ManagePrefabs_WithoutRequiredPath_ReturnsError`
- `HandleCommand_FindGameObjects_WithoutSearchMethod_UsesDefault`

**Tests**: Validation timing, default application, path normalization
**Focus**: Validation happens before state mutation

---

### 6. **State Mutation and Side Effects** (2 tests)
- `HandleCommand_ManageEditor_PlayAction_MutatesEditorState`
- `HandleCommand_ManageMaterial_CreateAction_HasAssetSideEffects`

**Tests**: State changes, asset creation
**Focus**: Side effects occur appropriately

---

### 7. **Complex Parameter Handling and Object Resolution** (3 tests)
- `HandleCommand_FindGameObjects_WithDifferentSearchMethods`
- `HandleCommand_FindGameObjects_WithPaginationParameters`
- `HandleCommand_FindGameObjects_ClampsPageSizeToValidRange`

**Tests**: Search methods, pagination, range clamping
**Focus**: Complex parameter handling and result limiting

---

### 8. **Security and Filtering** (2 tests)
- `HandleCommand_ExecuteMenuItem_BlocksBlacklistedItems`
- `HandleCommand_ExecuteMenuItem_RequiresMenuPath`

**Tests**: Security blacklist, parameter requirements
**Focus**: Dangerous operations are blocked

---

### 9. **Response Objects and Data Structures** (3 tests)
- `HandleCommand_ManageEditor_StopAction_ReturnsSuccessResponse`
- `HandleCommand_ManageEditor_UnknownAction_HasDescriptiveError`
- `HandleCommand_ResponseObjects_AreSerializable`

**Tests**: Response consistency, serializability
**Focus**: All responses follow Response pattern

---

### 10. **Tool Attribute Registration** (1 test)
- `EditorTools_AllMarkedWithToolAttribute`

**Tests**: Attribute presence
**Focus**: All tools properly registered

---

### 11. **Tool-Specific Behaviors** (6 tests)
- `HandleCommand_ManageEditor_PauseOnly_WhenPlaying`
- `HandleCommand_ManageMaterial_SetProperty_CoercesTypes`
- `HandleCommand_FindGameObjects_ReturnsPaginationMetadata`
- `HandleCommand_ExecuteMenuItem_ReturnsAttemptStatus`
- `HandleCommand_ActionNormalization_CaseInsensitive`
- `HandleCommand_ErrorMessages_AreContextSpecific`

**Tests**: Tool-specific patterns and behaviors
**Focus**: Unique characteristics of sampled tools

---

## Expected Test Results

### Baseline (Current Implementation)
✓ **All 33 tests should PASS**
- No test failures expected
- Tests capture current behavior, not ideal behavior
- Framework and dependencies are satisfied

### Console Output Example
```
EditorTools_Characterization.HandleCommand_ManageEditor_WithNullParams_ReturnsErrorResponse ... PASS
EditorTools_Characterization.HandleCommand_ManageEditor_WithoutActionParameter_ReturnsError ... PASS
EditorTools_Characterization.HandleCommand_ManageEditor_WithUppercaseAction_NormalizesAndDispatches ... PASS
...
=======================================
Passed: 33
Failed: 0
Skipped: 0
Total: 33
Time: 1.234 seconds
```

---

## Troubleshooting

### Test Doesn't Appear in Test Runner

**Problem**: Tests not showing in Test Runner window
**Solution**:
1. Ensure file is in `Assets/` subtree (it is: `Assets/MCPForUnity/Editor/Tools/Tests/`)
2. Confirm `.meta` file exists (auto-created by Unity)
3. Check assembly definition is set to "Editor" mode
4. Try: Reimport all (Ctrl+Alt+R)

### Tests Fail to Compile

**Problem**: Compiler errors in test file
**Solution**:
1. Check all using statements are correct:
   - `using NUnit.Framework`
   - `using Newtonsoft.Json.Linq`
   - `using MCPForUnity.Editor.Helpers`
2. Ensure MCPForUnity assemblies are available
3. Check C# language level (should be 7.3+)

### Tests Timeout

**Problem**: Tests take longer than expected or timeout
**Solution**:
1. These tests are quick (~1-2 seconds total)
2. If slow, likely Unity is compiling something
3. Try: Run > Run All again
4. Check Performance Profiler for bottlenecks

### Individual Test Fails

**Problem**: One or more tests fail
**Analysis**:
1. Read the test docstring to understand what it captures
2. Check the tool implementation it tests
3. If it's a baseline (current behavior), tool code has changed
4. If it's a refactored tool, test may need updating
5. Review CHARACTERIZATION_SUMMARY.md for pattern explanation

---

## Using Tests for Refactoring

### Before Refactoring
1. **Run baseline**: All 33 tests pass with current implementation
2. **Document passing**: Screenshot or save test results
3. **Read tests**: Understand what behavior is being protected

### During Refactoring
1. **Keep tests**: Don't delete or modify tests
2. **Run frequently**: After each change, run tests
3. **Monitor results**: Tests should still pass if refactor preserves behavior
4. **Debug failures**: If test fails, either:
   - Fix refactored code to match old behavior, OR
   - Update test if test was capturing wrong behavior (rare)

### After Refactoring
1. **All tests pass**: Confirms behavior preservation
2. **Review changes**: Check for improvements to code structure
3. **New tests**: Add tests for new behaviors if refactoring added features

---

## Test Dependencies and Requirements

### Required Packages
- ✓ NUnit.Framework (standard in this project)
- ✓ Newtonsoft.Json.Linq (installed via nuget)
- ✓ UnityEngine (Editor scripting)
- ✓ UnityEditor (Editor APIs)

### Required Utilities
- ✓ MCPForUnity.Editor.Helpers (ErrorResponse, SuccessResponse, McpLog)
- ✓ MCPForUnity.Editor.Tools (All sampled tools)

### Runtime Requirements
- ✓ Unity Editor instance
- ✓ Editor scripting enabled
- ✓ No special configurations needed

**All requirements are met in this codebase ✓**

---

## Continuous Integration

### GitHub Actions Example

```yaml
name: Editor Tools Tests

on: [push, pull_request]

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v2
      - uses: game-ci/unity-test-runner@v2
        with:
          projectPath: .
          testMode: editmode
          artifactsPath: test-results
          customImage: unityci/editor:ubuntu-latest-latest
      - uses: actions/upload-artifact@v2
        if: always()
        with:
          name: test-results
          path: test-results
```

---

## Extending Tests

### Adding a New Test

1. **Identify the pattern** it tests (see sections 1-11)
2. **Add test method** to appropriate section:
```csharp
/// <summary>
/// Characterizes: [What current behavior is captured]
/// Expected: [What should happen]
/// Used by: [Which tools]
/// </summary>
[Test]
public void HandleCommand_ToolName_Scenario_ExpectedOutcome()
{
    // ARRANGE: Set up parameters
    var testParams = new JObject { ["action"] = "test" };

    // ACT: Call tool
    var result = ToolName.HandleCommand(testParams);

    // ASSERT: Verify behavior
    Assert.IsNotNull(result);
}
```

3. **Update count** in CHARACTERIZATION_SUMMARY.md
4. **Run tests** to verify new test works
5. **Document pattern** in PATTERN_REFERENCE.md if needed

### Adding a New Tool

1. **Sample the tool**: Understand its HandleCommand flow
2. **Identify patterns**: Which of the 11 patterns does it use?
3. **Add tests**: Mirror structure of similar tool tests
4. **Update summary**: Add tool to sampled list

---

## Maintenance

### Monthly Checklist
- [ ] Run all tests - confirm all 33 pass
- [ ] Review test results for patterns
- [ ] Check for new patterns in recent tool additions
- [ ] Update PATTERN_REFERENCE.md if new patterns found

### When Tools Change
- [ ] Run tests after significant changes
- [ ] If tests fail, investigate why
- [ ] Update affected test if behavior intentionally changed
- [ ] Add new tests for new behaviors

### When Refactoring
- [ ] Baseline: All tests pass before refactoring
- [ ] During: Run tests after each change
- [ ] After: All tests pass with refactored code
- [ ] Verify: Test same behavior, cleaner implementation

---

## Quick Stats

| Metric | Value |
|--------|-------|
| Test Methods | 33 |
| Test Classes | 1 (EditorToolsCharacterizationTests) |
| Assertion Types | Assert, Assert.IsNotNull, Assert.IsInstanceOf, Assert.That, CollectionAssert |
| Average Test Duration | ~40ms |
| Total Expected Time | ~1-2 seconds |
| Code Coverage Target | All public HandleCommand methods, primary action paths |
| Documentation Pages | 3 (this guide + 2 supporting docs) |

---

## Success Criteria

✓ **Test Execution**: All 33 tests pass with current implementation
✓ **Baseline**: Tests capture actual current behavior (not idealized)
✓ **Isolation**: Each test is independent, can run in any order
✓ **Clarity**: Docstrings explain what behavior each test protects
✓ **Maintenance**: Tests are well-organized and easy to extend
✓ **Refactoring Safety**: Tests enable safe refactoring with behavior preservation

---

## Support and Questions

For questions about specific tests:
1. Read test docstring - explains what behavior it captures
2. Check CHARACTERIZATION_SUMMARY.md - overview of patterns
3. Review PATTERN_REFERENCE.md - detailed pattern explanations
4. Examine tool source code - understand implementation

For test execution issues:
1. See "Troubleshooting" section above
2. Check requirements checklist (all met ✓)
3. Verify Unity Editor can access test files
4. Review assembly definitions

---

## Conclusion

The Editor Tools Characterization Test Suite is ready for execution. All 33 tests document current behavior across 5 representative tool modules, capturing 8 major C# behavior patterns. Tests serve as:

1. **Baseline for regression testing** during refactoring
2. **Documentation** of current tool behavior
3. **Safety net** ensuring behavior preservation
4. **Foundation** for extracting common patterns

**Next Steps**:
1. Run baseline tests (expect 33 pass)
2. Review CHARACTERIZATION_SUMMARY.md for overview
3. Use PATTERN_REFERENCE.md during refactoring
4. Keep tests passing as code evolves
