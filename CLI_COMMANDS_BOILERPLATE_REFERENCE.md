# CLI Commands Domain - Boilerplate Reference

Quick reference for identified boilerplate patterns in the CLI Commands domain.

---

## Pattern Summary Table

| Pattern | Frequency | Files | Lines Duplicated | P0/P1/P2 | Test Ref | Refactor Target |
|---------|-----------|-------|------------------|----------|----------|-----------------|
| Try/except run_command | All 20 modules | prefab, component, material, asset, animation, ... | ~40 | P2-1 | TestErrorHandlingPattern | @standard_command() decorator |
| JSON parsing | 5 modules | component, material, asset, texture, vfx | ~50 | QW-2 | TestJSONParsingPattern | cli/utils/parsers.py |
| search_method parameter | 4+ modules | component, material, gameobject, vfx | ~20 | QW-4 | TestSearchMethodParameter | cli/utils/constants.py |
| Confirmation dialog | 3+ modules | component, asset, gameobject | ~5 | QW-5 | TestConfirmationDialogPattern | cli/utils/confirmation.py |
| Wrapped response handling | 1 module | prefab | ~10 | TBD | TestWrappedResponseHandling | Investigation needed |

---

## 1. Try/Except Error Handling Pattern

**Priority**: P2-1 (High - affects all 20 modules)
**Estimated Lines Eliminated**: ~40
**Refactoring Target**: `@standard_command()` decorator

### Current Implementation
```python
@component.command("add")
@click.argument("target")
@click.argument("component_type")
def add(target: str, component_type: str):
    config = get_config()

    params = {
        "action": "add",
        "target": target,
        "componentType": component_type,
    }

    try:
        result = run_command("manage_components", params, config)
        click.echo(format_output(result, config.format))
        if result.get("success"):
            print_success(f"Added {component_type} to '{target}'")
    except UnityConnectionError as e:
        print_error(str(e))
        sys.exit(1)
```

### Proposed Refactoring
```python
@standard_command("manage_components")
def add(target: str, component_type: str):
    """Add a component to a GameObject."""
    return {
        "action": "add",
        "target": target,
        "componentType": component_type,
    }

# Decorator would handle:
# 1. get_config()
# 2. run_command(tool_name, params, config)
# 3. format_output(result, config.format)
# 4. Check result.get("success")
# 5. print_success(f"Added {component_type}...")
# 6. Try/except UnityConnectionError
# 7. print_error + sys.exit(1)
```

### Test Coverage
- TestErrorHandlingPattern (4 tests)
- TestOutputFormattingPattern (2 tests)
- TestSuccessResponseHandling (4 tests)

---

## 2. JSON Parsing Pattern

**Priority**: QW-2 (Quick Win - 30 min effort)
**Estimated Lines Eliminated**: ~50
**Files Affected**: component.py, material.py, asset.py, texture.py, vfx.py
**Refactoring Target**: `cli/utils/parsers.py`

### Current Implementation - Variant A (Simple)
```python
# asset.py:132
if properties:
    try:
        params["properties"] = json.loads(properties)
    except json.JSONDecodeError as e:
        print_error(f"Invalid JSON for properties: {e}")
        sys.exit(1)
```

### Current Implementation - Variant B (JSON + Float + String Fallback)
```python
# material.py:142, component.py:138
try:
    parsed_value = json.loads(value)
except json.JSONDecodeError:
    try:
        parsed_value = float(value)
    except ValueError:
        parsed_value = value
```

### Proposed Refactoring
```python
# cli/utils/parsers.py
from typing import Any
import json

def try_parse_json(value: str, context: str = "") -> Any:
    """Parse JSON, fall back to float, fall back to string.

    Args:
        value: String to parse
        context: Optional context for error messages

    Returns:
        Parsed value (dict/list), float, or original string
    """
    try:
        return json.loads(value)
    except json.JSONDecodeError:
        try:
            return float(value)
        except ValueError:
            return value


def try_parse_json_strict(value: str, param_name: str = "parameter") -> Any:
    """Parse JSON with error handling.

    Args:
        value: String to parse
        param_name: Name for error messages

    Returns:
        Parsed JSON value

    Raises:
        SystemExit: On JSON decode error
    """
    try:
        return json.loads(value)
    except json.JSONDecodeError as e:
        print_error(f"Invalid JSON for {param_name}: {e}")
        sys.exit(1)
```

### Usage After Refactoring
```python
# Before
try:
    parsed_value = json.loads(value)
except json.JSONDecodeError:
    try:
        parsed_value = float(value)
    except ValueError:
        parsed_value = value

# After
parsed_value = try_parse_json(value)
```

### Test Coverage
- TestJSONParsingPattern (5 tests covering all variants)

---

## 3. Search Method Parameter Pattern

**Priority**: QW-4 (Quick Win - 20 min effort)
**Estimated Lines Eliminated**: ~20
**Files Affected**: component.py, material.py, gameobject.py, vfx.py
**Refactoring Target**: `cli/utils/constants.py`

### Current Implementation - Variant A (component.py)
```python
@click.option(
    "--search-method",
    type=click.Choice(["by_id", "by_name", "by_path"]),
    default=None,
    help="How to find the target GameObject."
)
def add(target: str, component_type: str, search_method: Optional[str]):
    # ...
    if search_method:
        params["searchMethod"] = search_method
```

### Current Implementation - Variant B (material.py)
```python
@click.option(
    "--search-method",
    type=click.Choice(["by_name", "by_path", "by_tag", "by_layer", "by_component"]),
    default=None,
    help="How to find the target GameObject."
)
def assign(material_path: str, target: str, search_method: Optional[str]):
    # ...
    if search_method:
        params["searchMethod"] = search_method
```

### Proposed Refactoring
```python
# cli/utils/constants.py
import click

# All available search methods across domain
ALL_SEARCH_METHODS = ["by_name", "by_path", "by_id", "by_tag", "by_layer", "by_component"]
SEARCH_METHOD_CHOICE = click.Choice(ALL_SEARCH_METHODS)

# Domain-specific subsets (if needed for validation)
GAMEOBJECT_SEARCH_METHODS = ["by_id", "by_name", "by_path"]
RENDERER_SEARCH_METHODS = ["by_name", "by_path", "by_tag", "by_layer", "by_component"]
```

### Usage After Refactoring
```python
from cli.utils.constants import SEARCH_METHOD_CHOICE, GAMEOBJECT_SEARCH_METHODS

@click.option(
    "--search-method",
    type=SEARCH_METHOD_CHOICE,
    default=None,
    help="How to find the target."
)
def add(target: str, component_type: str, search_method: Optional[str]):
    # Rest of implementation unchanged
    if search_method:
        params["searchMethod"] = search_method
```

### Test Coverage
- TestSearchMethodParameter (3 tests)

---

## 4. Confirmation Dialog Pattern

**Priority**: QW-5 (Quick Win - 15 min effort)
**Estimated Lines Eliminated**: ~5 (but inconsistency, so good for consolidation)
**Files Affected**: component.py, asset.py, gameobject.py
**Refactoring Target**: `cli/utils/confirmation.py`

### Current Implementation
```python
# component.py:94
if not force:
    click.confirm(f"Remove {component_type} from '{target}'?", abort=True)
```

### Proposed Refactoring
```python
# cli/utils/confirmation.py
import click

def confirm_destructive_action(
    target: str,
    action: str = "delete",
    force: bool = False
) -> bool:
    """Prompt for confirmation before destructive operation.

    Args:
        target: What is being modified (e.g., "Player", "Assets/Mat.mat")
        action: Action being performed (e.g., "Remove", "Delete")
        force: If True, skip confirmation

    Returns:
        True if proceeding (either --force or user confirmed)
        False never returned (uses abort=True)

    Example:
        confirm_destructive_action("MyComponent", "Remove from Player", force=False)
    """
    if not force:
        click.confirm(f"{action} '{target}'?", abort=True)
    return True
```

### Usage After Refactoring
```python
from cli.utils.confirmation import confirm_destructive_action

@component.command("remove")
@click.argument("target")
@click.argument("component_type")
@click.option("--force", "-f", is_flag=True, help="Skip confirmation prompt.")
def remove(target: str, component_type: str, force: bool):
    config = get_config()

    confirm_destructive_action(target, f"Remove {component_type}", force=force)

    params = {"action": "remove", "target": target, "componentType": component_type}

    try:
        result = run_command("manage_components", params, config)
        # ...
    except UnityConnectionError as e:
        # ...
```

### Test Coverage
- TestConfirmationDialogPattern (2 tests)

---

## 5. Wrapped Response Handling (Investigation Needed)

**Priority**: TBD (needs investigation)
**Status**: Only in prefab.py
**Impact**: May indicate inconsistent tool response structure
**Refactoring Target**: Unknown (TBD)

### Current Implementation
```python
# prefab.py:133, 182, 195
result = run_command("manage_prefabs", params, config)
response_data = result.get("result", result)
if response_data.get("success") and response_data.get("data"):
    data = response_data["data"]
    # Access data fields
```

### Issue
- Other modules don't use this pattern
- Suggests either:
  1. prefab tool wraps response differently
  2. prefab.py is over-defensive
  3. Historical artifact

### Action Items
1. Investigate why prefab.py needs unwrapping
2. Check if other modules also need this
3. Standardize response structure across tools
4. Add more comprehensive tests if widespread

### Test Coverage
- TestWrappedResponseHandling (2 tests)

---

## Implementation Roadmap

### Phase 1: Quick Wins (Week 1)
1. **QW-2** - Extract JSON Parser Utility (30 min)
   - Create cli/utils/parsers.py
   - Update component.py, material.py, asset.py (first 3)
   - Verify all TestJSONParsingPattern tests pass

2. **QW-4** - Consolidate Search Methods (20 min)
   - Create cli/utils/constants.py
   - Update component.py, material.py
   - Verify all TestSearchMethodParameter tests pass

3. **QW-5** - Extract Confirmation Utility (15 min)
   - Create cli/utils/confirmation.py
   - Update component.py
   - Verify TestConfirmationDialogPattern tests pass

### Phase 2: Major Refactor (Week 3-4)
1. **P2-1** - Command Wrapper Decorator (4-5 hours)
   - Design @standard_command() decorator
   - Implement base decorator
   - Migrate 3-5 commands (proof of concept)
   - Run all 49 tests after each command
   - Gradually migrate remaining 15-17 commands
   - Verify all tests pass at each step

### Phase 3: Investigation
1. **Wrapped Response Handling** - Investigate and resolve response structure inconsistency

---

## Quick Command Reference

### See Current Behavior
```bash
# View test class for pattern
grep -A 30 "class TestJSONParsingPattern" \
  /Users/davidsarno/unity-mcp/Server/tests/test_cli_commands_characterization.py

# Run specific test group
cd /Users/davidsarno/unity-mcp/Server
uv run pytest tests/test_cli_commands_characterization.py::TestJSONParsingPattern -v
```

### Verify Behavior Preserved After Refactoring
```bash
# Run all characterization tests
uv run pytest tests/test_cli_commands_characterization.py -v

# Run specific pattern tests
uv run pytest tests/test_cli_commands_characterization.py::TestErrorHandlingPattern -v
uv run pytest tests/test_cli_commands_characterization.py::TestJSONParsingPattern -v
uv run pytest tests/test_cli_commands_characterization.py::TestSearchMethodParameter -v
uv run pytest tests/test_cli_commands_characterization.py::TestConfirmationDialogPattern -v
```

---

## Key Statistics for Refactoring ROI

| Item | Count | Effort | Impact |
|------|-------|--------|--------|
| Commands affected by P2-1 | 20 | 4-5 hrs | High (eliminates 40 lines duplication) |
| Commands affected by QW-2 | 5 | 30 min | Medium (eliminates 50 lines duplication) |
| Commands affected by QW-4 | 4 | 20 min | Low (consolidates constants) |
| Commands affected by QW-5 | 3 | 15 min | Low (consistency improvement) |
| Total Lines Eliminated | ~110 | ~1 hour | Code reduction: ~5-10% domain |
| Test Coverage | 49 tests | 100% pass | Regression safety: High |

---

**Document Version**: 1.0
**Generated**: 2026-01-26
**Companion Document**: CHARACTERIZATION_TEST_SUMMARY.md
**Test File**: tests/test_cli_commands_characterization.py (1,051 lines, 49 tests)
