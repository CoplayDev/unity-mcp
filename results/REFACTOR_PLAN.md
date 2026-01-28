# Unity-MCP Comprehensive Refactor Plan

**Generated**: 2026-01-26
**Based on**: 10 parallel domain analyses
**Lead Maintainer**: David Sarno

---

## Executive Summary

This plan consolidates findings from analysis of all 10 major domains in unity-mcp. The codebase is functional and well-intentioned, but shows signs of organic growth without unifying patterns. Key themes across domains:

1. **Repetitive boilerplate** — Same patterns copy-pasted across files (CLI commands, Editor tools, UI sections, Configurators)
2. **State management fragmentation** — Multiple boolean flags, dual status tracking, scattered EditorPrefs
3. **Over-engineering** — Complex middleware, nested templates, triple validation, excessive null-checking
4. **Dead code accumulation** — Unused decorators, deprecated shims, legacy fallback paths
5. **Asymmetries** — Python/C# naming mismatches, model duplication across languages

**Total estimated code reduction**: 25-40% with moderate effort refactors.

---

## Priority Matrix

| Priority | Category | Impact | Effort | Risk |
|----------|----------|--------|--------|------|
| **P0** | Quick Wins | High | Low | Low |
| **P1** | High Priority | High | Medium | Low-Med |
| **P2** | Medium Priority | Medium | Medium | Medium |
| **P3** | Long-term | High | High | Higher |

---

## P0: Quick Wins (Do First)

These deliver immediate value with minimal risk. Can be done in any order.

**Note**: Utilities audited 2026-01-27 (see `results/UTILITY_AUDIT.md`). Quick wins updated to reflect:
- ✅ **Patch in existing utilities** where they already exist (QW-3: AssetPathUtility)
- ❌ **Create new utilities** where patterns are duplicated but not extracted (QW-2, QW-4, QW-5)

### QW-1: Delete Dead Code ✅ COMPLETE (2026-01-27)
**Impact**: Cleaner codebase, reduced cognitive load (~86 lines removed)
**Effort**: 1 hour (actual)
**Risk**: Very low

**Deleted:**
| Domain | Item | Location |
|--------|------|----------|
| Helpers | `reload_sentinel.py` | `Server/src/utils/` — entire file deleted (10 lines) |
| Transport | `with_unity_instance()` decorator | `Server/src/transport/unity_transport.py:28-76` — 49 lines removed |
| Core | `ServerConfig.configure_logging()` | `Server/src/core/config.py:49-51` — 3 lines removed |
| Models | Deprecated accessors | `TransportManager.cs:26-27` — 2 lines removed |
| UI | Commented `maxSize` | `McpSetupWindow.cs:37` — 1 line removed |
| UI | Stop button backward-compat | `McpConnectionSection.cs` — 21 lines removed (stopHttpServerButton references) |

**NOT Deleted (Refactor plan was incorrect - actively used):**
- ❌ `list_sessions_sync()`, `send_command_to_plugin()` — do not exist (never defined)
- ❌ STDIO framing config — USED in unity_connection.py (require_framing, handshake_timeout, etc.)
- ❌ `port_registry_ttl` — USED in stdio_port_registry.py
- ❌ `reload_retry_ms` — USED in plugin_hub.py and unity_connection.py

**Tests Updated:** Characterization tests updated to document removal of configure_logging
**Verification:** All 59 config/transport tests passing

### QW-2: Create JSON Parser Utility (CLI) ✅ COMPLETE (2026-01-27)
**Status**: ✅ Created `Server/src/cli/utils/parsers.py` (audited 2026-01-27)
**Impact**: Eliminated ~60 lines of duplication across 8 modules
**Effort**: 30 minutes (actual)
**Risk**: Very low

**Created utilities:**
```python
# cli/utils/parsers.py
def parse_value_safe(value: str) -> Any:
    """Try JSON → float → string fallback (no exit)"""

def parse_json_or_exit(value: str, context: str = "parameter") -> Any:
    """Parse JSON with quote/bool fixes, exit on error"""

def parse_json_dict_or_exit(value: str, context: str) -> dict[str, Any]:
    """Parse JSON dict, exit if not dict"""

def parse_json_list_or_exit(value: str, context: str) -> list[Any]:
    """Parse JSON list, exit if not list"""
```

**Files updated**: `material.py` (2 patterns), `component.py` (3 patterns), `texture.py` (removed local function), `vfx.py` (2 patterns), `asset.py`, `editor.py`, `script.py`, `batch.py`

**Tests**: All 23 material/component CLI tests passing

### QW-3: Patch In AssetPathUtility (Editor Tools) ✅ COMPLETE (2026-01-27)
**Status**: ✅ Patched into 5 files (audited 2026-01-27)
**Impact**: Eliminated 10+ duplicated path normalization patterns
**Effort**: 20 minutes (actual)
**Risk**: Very low

**Existing utility** provides:
- `NormalizeSeparators(string path)` - Converts backslashes to forward slashes
- `SanitizeAssetPath(string path)` - Removes leading/trailing slashes, ensures Assets/ prefix
- `IsValidAssetPath(string path)` - Validates path format

**Files updated**:
- `ManageScene.cs` - 2 patterns replaced (lines 104, 131)
- `ManageShader.cs` - 2 patterns replaced (lines 69, 85)
- `ManageScript.cs` - 4 patterns replaced (lines 63, 66, 81, 82, 185, 2639)
- `GameObjectModify.cs` - 1 pattern replaced (line 50)
- `ManageScriptableObject.cs` - 1 pattern replaced (line 1444)

**Total**: 10+ `path.Replace('\\', '/')` patterns replaced with centralized utility calls

**Note**: Many more opportunities exist in ManageVFX.cs and other files, but focused on highest-duplication patterns first

### QW-4: Create Search Method Constants (CLI) ✅ COMPLETE (2026-01-27)
**Status**: ✅ Created `Server/src/cli/utils/constants.py`
**Impact**: Eliminated ~30+ lines of duplicated Click.Choice declarations
**Effort**: 25 minutes (actual)
**Risk**: Very low

**Created constants:**
```python
# cli/utils/constants.py
SEARCH_METHODS_FULL = ["by_name", "by_path", "by_id", "by_tag", "by_layer", "by_component"]
SEARCH_METHODS_BASIC = ["by_id", "by_name", "by_path"]
SEARCH_METHODS_RENDERER = ["by_name", "by_path", "by_tag", "by_layer", "by_component"]
SEARCH_METHODS_TAGGED = ["by_name", "by_path", "by_id", "by_tag"]

SEARCH_METHOD_CHOICE_FULL = click.Choice(SEARCH_METHODS_FULL)
SEARCH_METHOD_CHOICE_BASIC = click.Choice(SEARCH_METHODS_BASIC)
SEARCH_METHOD_CHOICE_RENDERER = click.Choice(SEARCH_METHODS_RENDERER)
SEARCH_METHOD_CHOICE_TAGGED = click.Choice(SEARCH_METHODS_TAGGED)
```

**Files updated**: `vfx.py` (14 occurrences!), `gameobject.py`, `component.py`, `material.py`, `animation.py`, `audio.py`

**Tests**: All 49 CLI commands characterization tests passing

### QW-5: Create Confirmation Dialog Utility (CLI) ✅ COMPLETE (2026-01-27)
**Status**: ✅ Created `Server/src/cli/utils/confirmation.py`
**Impact**: Eliminated 5+ duplicate confirmation patterns, consistent UX
**Effort**: 20 minutes (actual)
**Risk**: Very low

**Created utility:**
```python
# cli/utils/confirmation.py
def confirm_destructive_action(
    action: str,
    item_type: str,
    item_name: str,
    force: bool,
    extra_context: str = ""
) -> None:
    """Prompt user to confirm destructive action unless --force flag is set."""
    if not force:
        if extra_context:
            message = f"{action} {item_type} {extra_context} '{item_name}'?"
        else:
            message = f"{action} {item_type} '{item_name}'?"
        click.confirm(message, abort=True)
```

**Files updated**: `component.py`, `gameobject.py`, `script.py`, `shader.py`, `asset.py`

**Tests**: All 49 CLI commands characterization tests passing

### Quick Wins Verification Summary (2026-01-27)

**All QW-1 through QW-5 complete and verified!**

**Python Test Coverage:**
- ✅ Config/Transport: 59/59 tests passing (verified QW-1 config changes)
- ✅ CLI Commands: 49/49 tests passing (verified QW-2, QW-4, QW-5)
- **Total Python**: 108/108 tests passing

**C# Test Coverage (Unity EditMode):**
- ✅ Tools: 240/245 tests passing (5 explicit skipped) - verified QW-3 AssetPathUtility patches
- ✅ Models: 53/53 tests passing - verified QW-1 TransportManager changes
- ✅ Windows: 29/29 tests passing - verified QW-1 McpConnectionSection changes
- **Total C#**: 322/327 tests passing (5 explicit skipped)

**Live Integration Tests:**
- ✅ ManageScene path normalization (created/deleted test scene)
- ✅ ManageScript path normalization (created/deleted test script)

**Impact Summary:**
- Code removed: ~180+ lines (dead code + duplicated patterns)
- New utilities created: 3 files (parsers.py, constants.py, confirmation.py)
- Files refactored: 16 total (5 C# Editor tools, 11 Python CLI commands)
- Zero test failures, zero regressions

---

## P1: High Priority (Do Next)

Significant impact, moderate effort, manageable risk. Pick 2-3 to tackle after quick wins.

### P1-1: ToolParams Validation Wrapper (Editor Tools) ✅ COMPLETE (2026-01-27)
**Status**: ✅ Created ToolParams class, applied to ManageScript and ReadConsole
**Impact**: Eliminates 997+ IsNullOrEmpty validation lines
**Effort**: 2-3 hours (actual)
**Risk**: Low

```csharp
public class ToolParams
{
    private readonly JObject _params;
    public ToolParams(JObject @params) => _params = @params;

    public string GetRequired(string key, string errorMsg = null)
    {
        var value = _params[key]?.ToString();
        if (string.IsNullOrEmpty(value))
            throw new ParameterException(errorMsg ?? $"'{key}' is required");
        return value;
    }

    public string Get(string key, string defaultValue = null) =>
        _params[key]?.ToString() ?? defaultValue;

    public int? GetInt(string key) =>
        int.TryParse(_params[key]?.ToString(), out var i) ? i : (int?)null;
}
```

**Known Issue (Documented by Characterization Tests)**:
Tools currently have **inconsistent null parameter handling** at the HandleCommand entry point:
- **ManageEditor.cs:27** - No null check, throws `NullReferenceException` when @params is null
- **FindGameObjects.cs:26-29** - Checks for null, returns `ErrorResponse` gracefully

The ToolParams wrapper above assumes it receives a valid JObject. As part of this refactoring, all tools should add a consistent null check at HandleCommand entry:

```csharp
public static object HandleCommand(JObject @params)
{
    if (@params == null)
        return new ErrorResponse("Parameters cannot be null.");

    var p = new ToolParams(@params);
    // ... rest of implementation
}
```

**Usage**: Replace manual validation in 20+ tools.

### P1-2: EditorPrefs Binding Helper (UI)
**Impact**: Eliminates 50+ scattered get/notify/callback/set patterns
**Effort**: 2 hours
**Risk**: Low

```csharp
public class BoundEditorPref<T>
{
    public T Value { get; private set; }
    public BoundEditorPref(string key, T defaultValue, Action<T> onChanged = null) { ... }
    public void Bind(BaseField<T> field) { ... }
}
```

### P1-3: Unify Type Conversion Foundation (Helpers) ✅ COMPLETE (2026-01-27)
**Status**: ✅ Added nullable coercion methods and consolidated TryParse patterns
**Impact**: Consolidates PropertyConversion + ParamCoercion + VectorParsing patterns
**Effort**: 4-5 hours (actual)
**Risk**: Low-Medium

Create single `UnityTypeConverter` class as foundation:
- JSON → Unity type conversion
- Import from 36+ tool files
- Reduces 300+ lines to ~200 in one place

### P1-4: Consolidate Session Models (Models)
**Impact**: Eliminates PluginSession/SessionDetails duplication
**Effort**: 2 hours
**Risk**: Low

Keep `PluginSession` as internal, add `to_api_response()` method that generates `SessionDetails` format. Remove duplicate field definitions.

### P1-5: Configuration Cache (Editor Integration) ✅ COMPLETE (2026-01-27)
**Status**: ✅ Created EditorConfigurationCache singleton, replaced 25 UseHttpTransport reads
**Impact**: Reduces scattered EditorPrefs reads from 50+ to ~10
**Effort**: 2 hours (actual)
**Risk**: Low

Created `EditorConfigurationCache.cs` with:
- Singleton pattern for centralized config access
- `UseHttpTransport` property (most frequently read - 25 occurrences)
- Change notification event (`OnConfigurationChanged`)
- `Refresh()` method for explicit cache invalidation
- 13 unit tests for cache behavior

Updated 13 files to use cache instead of direct EditorPrefs reads.

### P1-6: Unified Test Fixtures (Testing) ✅ COMPLETE (2026-01-27)
**Status**: ✅ Consolidated duplicate test fixtures
**Impact**: Eliminates repeated DummyMCP/DummyContext definitions
**Effort**: 2 hours (actual)
**Risk**: Very low

Create `Server/tests/conftest_helpers.py` with shared test fixtures. Import in all integration tests instead of redefining.

---

## P2: Medium Priority (After P1s)

Moderate impact, require some structural changes.

### P2-1: Command Wrapper Decorator (CLI) ✅ COMPLETE (2026-01-27)
**Status**: ✅ Created `handle_unity_errors` decorator, applied to all CLI commands
**Impact**: Eliminates 20x repeated try/except/format_output pattern
**Effort**: 4-5 hours (actual)
**Risk**: Medium

```python
@standard_command("manage_scene")
def load(scene: str, by_index: bool):
    return {"action": "load", "scene": scene, "byIndex": by_index}
```

Decorator handles get_config, run_command, error handling, format_output.

### P2-2: Base Section Controller (UI)
**Impact**: 15-20% code reduction in UI sections
**Effort**: 4-6 hours
**Risk**: Medium

```csharp
public abstract class BaseEditorSection
{
    protected VisualElement Root { get; }
    protected abstract void CacheUIElements();
    protected abstract void InitializeUI();
    protected abstract void RegisterCallbacks();

    protected T SafeQuery<T>(string selector) where T : VisualElement =>
        Root?.Q<T>(selector);
}
```

All 5 section controllers inherit from base.

### P2-3: Configurator Builder Pattern (Client Config) ✅ ALREADY DONE (Audit 2026-01-27)
**Status**: ✅ Already implemented via inheritance pattern
**Audit Finding**: 15 configurator files totaling 649 lines already use base classes:
- `JsonFileMcpConfigurator` for JSON-based clients (Cursor, VSCode, etc.)
- `ClaudeCliMcpConfigurator` for CLI-based clients (Claude Code)
- Individual configurators are 26-32 lines each (minimal boilerplate)
**Impact**: N/A - work was already completed
**Effort**: 0 hours (already done)
**Risk**: N/A

The original plan overestimated the duplication. No further action needed.

### P2-4: Merge Transport State Management (Editor Integration)
**Impact**: Eliminates parallel state tracking in WebSocket/Stdio clients
**Effort**: 4-5 hours
**Risk**: Medium

TransportManager becomes single source of truth for all state. Individual clients only manage connection lifecycle.

### P2-5: Extract SessionResolver (Transport)
**Impact**: Single source of truth for session resolution
**Effort**: 4-5 hours
**Risk**: Medium

Create dedicated `SessionResolver` class handling all retry/wait logic. Both middleware and `send_command_for_instance()` delegate to it.

### P2-6: Consolidate VFX Tools (Editor Tools) ⚠️ REVISED (Audit 2026-01-27)
**Audit Finding**: 12 files, 2377 total lines, but:
- ManageVFX.cs alone is **1023 lines (43%)** — the real problem
- Other 11 files are small (22-295 lines each) and already organized by type
- Merging small files wouldn't reduce complexity, just create bigger files

**Revised Approach**: Split ManageVFX.cs instead of merging others
**Impact**: Medium (addresses the actual problem: 1023-line dispatcher)
**Effort**: 4-6 hours
**Risk**: Medium

Current structure (already reasonable):
```
ManageVFX.cs (1023 lines - SPLIT THIS)
├── Particle*: ParticleCommon, ParticleControl, ParticleRead, ParticleWrite (556 lines total)
├── Line*: LineCreate, LineRead, LineWrite (461 lines total)
├── Trail*: TrailControl, TrailRead, TrailWrite (215 lines total)
└── ManageVfxCommon.cs (22 lines)
```

### P2-7: Split AssetPathUtility (Helpers)
**Impact**: Single responsibility, easier to test/maintain
**Effort**: 3-4 hours
**Risk**: Low-Medium

Break 372-line mega-utility into:
1. `AssetPathUtility` — Path validation, normalization (80 lines)
2. `PackageVersionUtility` — Package.json reading, version checks (90 lines)
3. `UvxCommandBuilder` — uvx argument construction (70 lines)

### P2-8: CLI Consistency Pass (CLI) ✅ CORE COMPLETE (2026-01-27)
**Status**: Core consistency issues fixed, optional enhancements remain
**Impact**: Reduces user errors, improves discoverability, consistent UX
**Effort**: 1 hour (actual for core items)
**Risk**: Low

**Problem**: CLI commands have inconsistent patterns leading to user errors:

1. **Confirmation flags inconsistency**:
   - `gameobject delete --force` ✓
   - `asset delete --force` ✓
   - `texture delete` — NO confirmation flag
   - `shader delete` — NO confirmation flag
   - Some use `-f`, some only `--force`

2. **Subcommand structure confusion**:
   - `vfx particle info` (3 levels) — easy to guess `vfx particle-info` incorrectly
   - `editor console` (2 levels)

3. **Positional vs named args inconsistency**:
   - `material create PATH` (positional)
   - `shader read PATH` (positional)
   - Inconsistent between similar commands

**Actions**:
1. Add `--force`/`-f` to ALL destructive commands (texture delete, shader delete, etc.)
2. Add command aliases where reasonable (accept both `vfx particle-info` and `vfx particle info`)
3. Audit all commands for consistent flag naming (`-f`/`--force` everywhere)
4. Document standard patterns in CLI README
5. Consider adding better error messages that suggest correct syntax

**Files to update**:
- `texture.py` — add `--force` to delete
- `shader.py` — add `--force` to delete
- `script.py` — verify `--force` consistency
- All command files — audit for `-f` short flag availability

### P2-9: Focus Nudge Improvements (Utils)
**Impact**: More reliable automated testing, less manual intervention
**Effort**: 1-2 hours
**Risk**: Low

**Problem**: Focus nudge mechanism fails to keep Unity responsive during test runs:

1. **Short focus duration**: `focus_duration_s = 0.5` is too short
   - macOS re-throttles Unity almost immediately after focus returns to terminal
   - Unity doesn't get enough time to make meaningful test progress

2. **Rate limiter too aggressive**: `_MIN_NUDGE_INTERVAL_S = 5.0`
   - After a nudge, Unity gets throttled again within seconds
   - Rate limiter prevents re-nudging for 5 seconds
   - Creates cycle where Unity is throttled most of the time

3. **Symptom**: Tests get stuck requiring manual Unity focus

**Proposed fixes**:
1. Increase `focus_duration_s` from 0.5s to 2-3s
2. Reduce rate limit from 5s to 2-3s
3. Consider keeping Unity focused during entire test run (focus once at start, restore at end)
4. Add configuration option for focus behavior during tests

**File to update**: `Server/src/utils/focus_nudge.py`

---

## P3: Long-term / Larger Refactors

High impact but require significant effort and careful planning.

### P3-1: Decompose ServerManagementService ✅ COMPLETE (2026-01-27)
**Status**: ✅ Decomposed 1489-line monolith to 876 lines + 5 focused components
**Impact**: Reduces 1489-line monolith via dependency injection
**Effort**: 10-15 hours (actual)
**Risk**: High (mitigated by characterization tests)

Extracted components (with interfaces for testability):
- `ProcessDetector` — Platform-specific process inspection (~200 lines)
- `PidFileManager` — PID file and handshake state management (~250 lines)
- `ProcessTerminator` — Platform-specific process termination (~80 lines)
- `ServerCommandBuilder` — uvx/server command construction (~145 lines)
- `TerminalLauncher` — Platform-specific terminal launching (~135 lines)
- `ServerManagementService` — Refactored orchestrator (876 lines, down from 1489)

**Tests**: 585 total (127 new component tests), all passing
**Critical fix**: Added PID validation to prevent `kill -1` catastrophe

### P3-2: Base Tool Framework (Editor Tools)
**Impact**: 40-50% boilerplate reduction across 42 tools
**Effort**: 15-20 hours
**Risk**: High

```csharp
abstract class ManagedToolBase
{
    protected object ExecuteWithAction(JObject @params,
        Dictionary<string, Func<JObject, object>> actionHandlers);
    protected string ValidateAction(JObject @params, IEnumerable<string> validActions);
}
```

All tools register action handlers, base handles dispatch + error handling.

### P3-3: Unified Instrumentation Layer (Core)
**Impact**: Merges log_execution + telemetry_* into single decorator
**Effort**: 8-10 hours
**Risk**: Medium-High

Single `@instrument()` decorator handles logging, telemetry, timing. Eliminates 3 decorator layers wrapping each function.

### P3-4: Separate WebSocket Lifecycle from Routing (Transport)
**Impact**: Clear separation, easier to test
**Effort**: 10-12 hours
**Risk**: High

Split PluginHub into:
- Pure WebSocket handler (lifecycle/registration)
- Stateless command router (query sessions, route commands)

### P3-5: Code Generation for Tool Scaffolding (Editor Tools)
**Impact**: 50-60% boilerplate reduction, auto-sync Python/C#
**Effort**: 20-25 hours
**Risk**: High

Define tool metadata in YAML, generate:
- Parameter validation code
- Action dispatcher
- Python CLI signatures
- API documentation

---

## Recommended Execution Order

### Phase 1: Foundation (Week 1-2)
1. **QW-1 through QW-5** — Delete dead code, extract utilities
2. **P1-1** — ToolParams validation wrapper
3. **P1-6** — Unified test fixtures

**Deliverables**: Clean codebase baseline, validation utilities

### Phase 2: Consolidation (Week 3-4)
1. **P1-2** — EditorPrefs binding helper
2. **P1-3** — Type conversion foundation
3. **P1-4** — Session model consolidation
4. **P1-5** — Configuration cache

**Deliverables**: Unified helpers, reduced duplication

### Phase 3: Patterns (Week 5-8)
1. **P2-1** — Command wrapper decorator
2. **P2-2** — Base section controller
3. **P2-3** — Configurator builder pattern
4. **P2-6** — VFX tool consolidation

**Deliverables**: Consistent patterns across domains

### Phase 4: Architecture (Month 2-3)
1. **P2-4** — Transport state management
2. **P2-5** — SessionResolver extraction
3. **P3-1** — ServerManagementService decomposition

**Deliverables**: Cleaner architecture, testable components

### Phase 5: Framework (Future)
1. **P3-2** — Base tool framework
2. **P3-3** — Unified instrumentation
3. **P3-5** — Code generation (if warranted by growth)

**Deliverables**: Scalable patterns for future development

---

## Metrics to Track

| Metric | Current (Est.) | Target |
|--------|----------------|--------|
| Avg lines per CLI command module | ~200 | ~150 |
| Avg lines per Editor tool | ~800 | ~500 |
| Lines in ServerManagementService | 1487 | ~300 |
| Lines in McpClientConfiguratorBase | 877 | ~200 |
| Duplicate JSON parse patterns | 5+ | 0 |
| Duplicate path normalization | 8+ | 0 |
| Test fixture definitions | 10+ | 1 |

---

## Risk Mitigations

1. **Incremental migrations** — Refactor one module at a time, keep old and new working simultaneously
2. **Feature flags** — Use compile-time flags to switch between old/new implementations during transition
3. **Regression tests** — Add tests before refactoring critical paths (especially Transport, ServerManagement)
4. **Code review** — All P2+ changes should have thorough review
5. **Rollback plan** — Git branches per major refactor, easy to revert

---

## Quick Reference: Files to Prioritize

### Highest Complexity (Refactor First)
1. `MCPForUnity/Editor/Services/ServerManagementService.cs` (1487 lines)
2. `MCPForUnity/Editor/Tools/ManageScript.cs` (2666 lines)
3. `MCPForUnity/Editor/Tools/ManageScriptableObject.cs` (1522 lines)
4. `MCPForUnity/Editor/Clients/McpClientConfiguratorBase.cs` (877 lines)
5. `MCPForUnity/Editor/Helpers/VectorParsing.cs` (730 lines)
6. `Server/src/transport/plugin_hub.py` (600+ lines, complex state)

### Most Duplicated (Quick Extraction Wins)
1. Path normalization — 8+ duplicate implementations
2. JSON parsing — 5+ duplicate try/except blocks
3. Parameter validation — 997+ IsNullOrEmpty checks
4. EditorPrefs get/set — 50+ scattered patterns
5. Search method choices — 4+ duplicate definitions

---

---

## Regression-Safe Refactoring Methodology

### Core Principles

1. **Test Before You Touch** — Write characterization tests that capture current behavior before changing anything
2. **One Commit = One Change** — Never mix refactoring with behavior changes in the same commit
3. **Parallel Implementation** — Keep old and new code running side-by-side, compare outputs
4. **Small Increments** — Each change should be reviewable in <15 minutes

### Parallel Implementation Pattern

For high-risk refactors, run old and new code simultaneously and compare outputs:

```csharp
// C# Example: Validating refactored tool behavior
public static object HandleCommand(JObject @params)
{
    var oldResult = HandleCommand_LEGACY(@params);

    #if UNITY_MCP_VALIDATE_REFACTOR
    var newResult = HandleCommand_NEW(@params);

    if (!ResultsEquivalent(oldResult, newResult))
    {
        McpLog.Warn($"[REFACTOR VALIDATION] Behavior mismatch detected");
        McpLog.Warn($"  Old: {JsonConvert.SerializeObject(oldResult)}");
        McpLog.Warn($"  New: {JsonConvert.SerializeObject(newResult)}");
        // Log to file for analysis
        File.AppendAllText("refactor_mismatches.log", $"{DateTime.Now}: {action}\n");
    }
    #endif

    return oldResult; // Use old until fully validated
}
```

```python
# Python Example: Validating refactored command behavior
def send_with_unity_instance_validated(send_func, instance, command, params):
    old_result = send_with_unity_instance_legacy(send_func, instance, command, params)

    if os.environ.get("UNITY_MCP_VALIDATE_REFACTOR"):
        new_result = send_with_unity_instance_new(send_func, instance, command, params)

        if old_result != new_result:
            logger.warning(f"Refactor mismatch: {command}")
            logger.warning(f"  Old: {old_result}")
            logger.warning(f"  New: {new_result}")

    return old_result
```

### Feature Flags for Gradual Rollout

```csharp
// Unity: EditorPrefs-based feature flags
public static class RefactorFlags
{
    public static bool UseNewToolFramework =>
        EditorPrefs.GetBool("UnityMCP.Refactor.NewToolFramework", false);

    public static bool UseNewSessionResolver =>
        EditorPrefs.GetBool("UnityMCP.Refactor.NewSessionResolver", false);

    // Easy rollback if something breaks
    public static void DisableAllRefactors()
    {
        EditorPrefs.SetBool("UnityMCP.Refactor.NewToolFramework", false);
        EditorPrefs.SetBool("UnityMCP.Refactor.NewSessionResolver", false);
        // ...
    }
}
```

```python
# Python: Environment-based feature flags
REFACTOR_FLAGS = {
    "new_session_resolver": os.environ.get("UNITY_MCP_NEW_SESSION_RESOLVER", "0") == "1",
    "new_command_wrapper": os.environ.get("UNITY_MCP_NEW_COMMAND_WRAPPER", "0") == "1",
}
```

---

## Parallel Subagent Execution Workflow

The refactoring can be executed using parallel AI subagents, mirroring the analysis approach. This allows all 10 domains to be refactored simultaneously with human checkpoints.

### Workflow Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│  PHASE 1: PARALLEL TEST WRITING                                      │
│  ════════════════════════════════                                    │
│                                                                      │
│  Launch 10 subagents simultaneously, each writing characterization   │
│  tests for their domain. Tests capture CURRENT behavior.             │
│                                                                      │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐     ┌──────────┐           │
│  │ Agent 1  │ │ Agent 2  │ │ Agent 3  │ ... │ Agent 10 │           │
│  │Transport │ │CLI Cmds  │ │Ed Tools  │     │Build/Test│           │
│  │  Tests   │ │  Tests   │ │  Tests   │     │  Tests   │           │
│  └────┬─────┘ └────┬─────┘ └────┬─────┘     └────┬─────┘           │
│       │            │            │                 │                  │
│       └────────────┴────────────┴─────────────────┘                  │
│                           │                                          │
│                           ▼                                          │
│                    Write to tests/                                   │
└─────────────────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────────────┐
│  CHECKPOINT 1: RUN ALL TESTS                                         │
│  ═══════════════════════════                                         │
│                                                                      │
│  Human runs: pytest tests/ -v && Unity Test Runner                   │
│  All tests must pass (they test current behavior, so they should)    │
│  Fix any test issues before proceeding                               │
└─────────────────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────────────┐
│  PHASE 2: PARALLEL REFACTORING                                       │
│  ═════════════════════════════                                       │
│                                                                      │
│  Launch 10 subagents simultaneously, each refactoring their domain.  │
│  Agents implement parallel validation (old vs new comparison).       │
│                                                                      │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐     ┌──────────┐           │
│  │ Agent 1  │ │ Agent 2  │ │ Agent 3  │ ... │ Agent 10 │           │
│  │Transport │ │CLI Cmds  │ │Ed Tools  │     │Build/Test│           │
│  │ Refactor │ │ Refactor │ │ Refactor │     │ Refactor │           │
│  └────┬─────┘ └────┬─────┘ └────┬─────┘     └────┬─────┘           │
│       │            │            │                 │                  │
│       └────────────┴────────────┴─────────────────┘                  │
│                           │                                          │
│                           ▼                                          │
│              Refactored code with _LEGACY preserved                  │
└─────────────────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────────────┐
│  CHECKPOINT 2: RUN ALL TESTS                                         │
│  ═══════════════════════════                                         │
│                                                                      │
│  Human runs: pytest tests/ -v && Unity Test Runner                   │
│  Tests should still pass (refactors are behavior-preserving)         │
│  Collect any failures for Phase 3                                    │
└─────────────────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────────────┐
│  PHASE 3: PARALLEL FIXING                                            │
│  ════════════════════════                                            │
│                                                                      │
│  For any domains with test failures, launch fix agents in parallel.  │
│  Agents receive: failing test output + their refactored code.        │
│                                                                      │
│  ┌──────────┐ ┌──────────┐                                          │
│  │ Agent 2  │ │ Agent 7  │  (only domains with failures)            │
│  │CLI Fixes │ │ UI Fixes │                                          │
│  └────┬─────┘ └────┬─────┘                                          │
│       │            │                                                 │
│       └────────────┘                                                 │
│              │                                                       │
│              ▼                                                       │
│         Fixed code                                                   │
└─────────────────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────────────┐
│  CHECKPOINT 3: FINAL VALIDATION                                      │
│  ══════════════════════════════                                      │
│                                                                      │
│  Human runs: Full test suite + manual smoke testing                  │
│  Enable refactor flags one domain at a time in real usage            │
│  Monitor for mismatch warnings from parallel validation              │
└─────────────────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────────────┐
│  PHASE 4: CLEANUP                                                    │
│  ═══════════════                                                     │
│                                                                      │
│  Once validated, launch cleanup agents to:                           │
│  - Remove _LEGACY code paths                                         │
│  - Remove parallel validation code                                   │
│  - Remove feature flags (or keep for future)                         │
│  - Update documentation                                              │
└─────────────────────────────────────────────────────────────────────┘
```

### Subagent Prompts

**Phase 1: Test Writing Agent Template**
```
You are analyzing the {DOMAIN} domain in unity-mcp at {PATH}.

Your task: Write characterization tests that capture CURRENT behavior.

1. Read the key files in your domain
2. Identify the main public functions/methods
3. Write tests that assert current behavior (not ideal behavior)
4. Tests should cover:
   - Happy path
   - Edge cases you observe in the code
   - Error handling paths

Write tests to: {TEST_PATH}

Do NOT refactor any code. Only write tests for existing behavior.
```

**Phase 2: Refactoring Agent Template**
```
You are refactoring the {DOMAIN} domain in unity-mcp at {PATH}.

Reference the analysis at: results/{NN}-{domain}.md
Reference the refactor plan at: results/REFACTOR_PLAN.md

Your task: Implement the refactors identified for your domain.

Rules:
1. Preserve ALL existing public method signatures
2. Keep _LEGACY versions of refactored functions
3. Add parallel validation that compares old vs new output
4. Each change should be a pure refactor (no behavior change)
5. Run existing tests mentally - would they still pass?

Implement these specific items from the plan:
{LIST OF P0/P1/P2 ITEMS FOR THIS DOMAIN}

Write refactored code. Keep _LEGACY code intact for comparison.
```

**Phase 3: Fix Agent Template**
```
You are fixing test failures in the {DOMAIN} domain.

Failing tests:
{TEST_OUTPUT}

Your refactored code is at: {PATH}
The _LEGACY code is preserved for comparison.

Your task:
1. Understand why the test is failing
2. Determine if it's a refactor bug or a test bug
3. Fix the issue while maintaining the refactor goals
4. Ensure the fix doesn't change external behavior

Do NOT delete _LEGACY code yet - we need it for validation.
```

### Domain-to-Agent Mapping

| Agent | Domain | Primary Paths | Key Refactor Items |
|-------|--------|---------------|-------------------|
| 1 | Transport & Communication | `Server/src/transport/` | P2-5: SessionResolver |
| 2 | CLI Commands | `Server/src/cli/commands/` | QW-2,3,4,5 + P2-1 |
| 3 | Editor Tools | `Editor/Tools/` | P1-1, P2-6, QW-3 |
| 4 | Editor Integration | `Editor/Services/` | P1-5, P2-4, P3-1 |
| 5 | Client Config | `Editor/Clients/`, `Editor/Models/` | P2-3 |
| 6 | Core Infrastructure | `Server/src/core/` | QW-1 (dead code), P3-3 |
| 7 | Helper Utilities | `Editor/Helpers/`, `Server/src/utils/` | P1-3, P2-7 |
| 8 | UI & Windows | `Editor/Windows/` | P1-2, P2-2 |
| 9 | Models & Data | `Server/src/models/`, `Editor/Models/` | P1-4 |
| 10 | Build/Release/Testing | `tools/`, `Server/tests/` | P1-6, QW-1 |

### Execution Commands

**Phase 1: Launch Test Writers**
```
# Launch all 10 test-writing agents in parallel
# Each writes to their domain's test directory
```

**Checkpoint 1: Run Tests**
```bash
# Python tests
cd Server && pytest tests/ -v --tb=short

# Unity tests (from Unity Editor)
# Window > General > Test Runner > Run All
```

**Phase 2: Launch Refactoring Agents**
```
# Launch all 10 refactoring agents in parallel
# Each implements their domain's P0/P1/P2 items
```

**Checkpoint 2: Run Tests + Validation**
```bash
# Python tests with validation enabled
UNITY_MCP_VALIDATE_REFACTOR=1 pytest tests/ -v

# Check for mismatch warnings
grep "REFACTOR VALIDATION" server.log
```

**Phase 3: Launch Fix Agents (as needed)**
```
# Only for domains with failures
# Provide failing test output to each agent
```

### Safety Guarantees

1. **No data loss** — _LEGACY code preserved until final cleanup
2. **Easy rollback** — Feature flags can disable any refactor instantly
3. **Visibility** — Parallel validation logs all behavior differences
4. **Human checkpoints** — Tests run by human between each phase
5. **Incremental** — Can stop after any phase with working code

---

## Conclusion

The unity-mcp codebase is functional but has accumulated technical debt from organic growth. The refactors outlined here would reduce code volume by 25-40%, improve maintainability, and establish patterns that scale with future development.

**Start with Quick Wins** — they're low-risk and build momentum.
**Focus on P1s** — highest ROI for moderate effort.
**Plan P3s carefully** — significant but transformative.

This plan should be treated as a living document. Update priorities as you progress and discover new patterns or issues.
