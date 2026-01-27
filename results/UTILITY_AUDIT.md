# Utility Audit - Pre-Refactor

**Date**: 2026-01-27
**Purpose**: Identify existing utilities before starting Quick Wins refactoring

---

## Summary

Before creating new utilities, audited the codebase to find what already exists. This prevents duplication and identifies opportunities to "patch in" existing helpers.

---

## C# Utilities (MCPForUnity/Editor/Helpers/)

### ✅ Already Exist - Ready to Patch In

1. **AssetPathUtility.cs** - Path normalization and validation
   - `NormalizeSeparators(string path)` - Converts backslashes to forward slashes
   - `SanitizeAssetPath(string path)` - Removes leading/trailing slashes, ensures Assets/ prefix
   - `IsValidAssetPath(string path)` - Validates path format
   - **Related to**: QW-3 in REFACTOR_PLAN.md
   - **Action**: Patch into ManageScene.cs, ManageShader.cs, ManageMaterial.cs, ManagePrefabs.cs

2. **ParamCoercion.cs** - Parameter type coercion from JToken
   - `CoerceInt(JToken, int default)` - Safe int parsing
   - `CoerceBool(JToken, bool default)` - Safe bool parsing with string support
   - `CoerceFloat(JToken, float default)` - Safe float parsing
   - `CoerceString(JToken, string default)` - Safe string extraction
   - `CoerceEnum<T>(JToken, T default)` - Safe enum parsing
   - `NormalizePropertyName(string)` - snake_case to camelCase conversion
   - **Related to**: P1-1 ToolParams Validation in REFACTOR_PLAN.md
   - **Action**: Already widely used, good foundation

3. **PropertyConversion.cs** - Unity type conversion from JToken
   - Handles Vector2/3/4, Quaternion, Color, LayerMask, AnimationCurve, Gradient, etc.
   - **Related to**: P1-3 Unify Type Conversion in REFACTOR_PLAN.md
   - **Action**: Already exists, consolidate with VectorParsing.cs

4. **VectorParsing.cs** - Vector string parsing
   - Parse comma/space-separated strings to Vector2/3/4
   - **Related to**: P1-3 Unify Type Conversion in REFACTOR_PLAN.md
   - **Action**: Consolidate with PropertyConversion.cs

5. **Pagination.cs** - Pagination support (just created)
   - `PaginationRequest.FromParams(JObject, int defaultPageSize)`
   - `PaginationResponse<T>.Create(IList<T>, PaginationRequest)`
   - **Status**: New, already working well

### Other Notable C# Helpers

- **GameObjectLookup.cs** - Finding GameObjects by various methods
- **ComponentOps.cs** - Component manipulation operations
- **MaterialOps.cs** - Material manipulation operations
- **TextureOps.cs** - Texture operations
- **PrefabUtilityHelper.cs** - Prefab operations
- **Response.cs** - SuccessResponse, ErrorResponse wrappers
- **McpLog.cs** - Logging utilities

---

## Python Utilities (Server/src/)

### ✅ CLI Utils Already Exist

Located in `Server/src/cli/utils/`:
- `config.py` - CLI configuration handling
- `connection.py` - Connection establishment and management
- `output.py` - Output formatting and display
- `suggestions.py` - Suggestion utilities

### ❌ Missing Utilities - Need Creation

1. **JSON Parser Utility**
   - **Pattern Found**: Duplicated in 5+ files
   - **Example** (from material.py, component.py, etc.):
   ```python
   try:
       parsed_value = json.loads(value)
   except json.JSONDecodeError:
       try:
           parsed_value = float(value)
       except ValueError:
           parsed_value = value
   ```
   - **Related to**: QW-2 in REFACTOR_PLAN.md
   - **Action**: Extract to `Server/src/cli/utils/parsers.py`
   - **Files to update**: material.py, component.py, asset.py, texture.py, vfx.py

2. **Search Method Constants**
   - **Pattern Found**: Duplicated in 6+ files
   - **Example** (from gameobject.py, vfx.py, component.py, etc.):
   ```python
   @click.option("--search-method",
                 type=click.Choice(["by_name", "by_path", "by_id", "by_tag", "by_layer", "by_component"]),
                 default="by_name")
   ```
   - **Variations**:
     - GameObjects: 6 methods (by_name, by_path, by_id, by_tag, by_layer, by_component)
     - VFX: 4 methods (by_name, by_path, by_id, by_tag)
     - Components: 3 methods (by_name, by_path, by_id)
   - **Related to**: QW-4 in REFACTOR_PLAN.md
   - **Action**: Extract to `Server/src/cli/utils/constants.py`
   - **Files to update**: gameobject.py, vfx.py (14 times!), component.py, material.py, animation.py, audio.py

3. **Confirmation Dialog Utility**
   - **Pattern Found**: Duplicated in 5 files
   - **Example** (from component.py, asset.py, shader.py, script.py, gameobject.py):
   ```python
   @click.option("--force", is_flag=True, help="Skip confirmation prompt.")
   ...
   if not force:
       click.confirm(f"<Action> '{target}'?", abort=True)
   ```
   - **Related to**: QW-5 in REFACTOR_PLAN.md
   - **Action**: Extract to `Server/src/cli/utils/confirmation.py`
   - **Files to update**: component.py, asset.py, shader.py, script.py, gameobject.py

---

## Recommendations for Quick Wins

### Updated QW-2: JSON Parser Utility (CREATE)
- Status: **Does NOT exist** - duplicated pattern in 5+ files
- Action: Create `Server/src/cli/utils/parsers.py`
- Estimated time: 30 minutes
- Estimated savings: ~50 lines elimination

### Updated QW-3: Path Normalization (PATCH IN)
- Status: **Already exists** as AssetPathUtility.cs
- Action: Patch into 8+ tools that duplicate normalization logic
- Estimated time: 45 minutes
- Estimated savings: ~100 lines elimination

### Updated QW-4: Search Method Constants (CREATE)
- Status: **Does NOT exist** - duplicated across 6+ files
- Action: Create `Server/src/cli/utils/constants.py`
- Estimated time: 20 minutes
- Estimated savings: Single source of truth, easier to extend

### Updated QW-5: Confirmation Dialog Utility (CREATE)
- Status: **Does NOT exist** - duplicated in 5 files
- Action: Create `Server/src/cli/utils/confirmation.py`
- Estimated time: 15 minutes
- Estimated savings: ~5 duplicate patterns eliminated

---

## Next Steps

1. ✅ Audit complete - existing utilities identified
2. Update REFACTOR_PLAN.md to reflect CREATE vs. PATCH IN actions
3. Begin Quick Wins in this order:
   - QW-1: Delete Dead Code (no dependencies)
   - QW-4: Create Search Method Constants (smallest, highest duplication)
   - QW-5: Create Confirmation Dialog Utility (small, clear pattern)
   - QW-2: Create JSON Parser Utility (medium, clear pattern)
   - QW-3: Patch in AssetPathUtility (largest, requires careful testing)
