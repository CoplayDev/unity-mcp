# Editor Tools Pattern Reference

This document provides a quick reference guide for the behavior patterns captured by characterization tests, organized for easy lookup during refactoring.

---

## Pattern 1: HandleCommand Entry Point

**Pattern Name**: Single Entry Point Pattern
**Frequency**: 5/5 tools (100%)
**Signature**: `public static object HandleCommand(JObject @params)`

### Current Implementation Pattern
```csharp
public static object HandleCommand(JObject @params)
{
    try
    {
        // Extract and validate action
        string action = @params["action"]?.ToString()?.ToLowerInvariant();
        if (string.IsNullOrEmpty(action))
            return new ErrorResponse("Action parameter is required.");

        // Dispatch to handler
        switch (action)
        {
            case "action1":
                return Action1Handler(@params);
            default:
                return new ErrorResponse($"Unknown action: '{action}'");
        }
    }
    catch (Exception e)
    {
        McpLog.Error($"[ToolName] Error: {e}");
        return new ErrorResponse($"Internal error: {e.Message}");
    }
}
```

### Null Safety Pattern
- Uses `@params?.ToString()?.ToLowerInvariant()` for safe chaining
- Handles null params at entry point
- Returns ErrorResponse, never throws NullReferenceException

### Tests Covering This Pattern
- `HandleCommand_ManageEditor_WithNullParams_ReturnsErrorResponse`
- `HandleCommand_ManageEditor_WithoutActionParameter_ReturnsError`
- `HandleCommand_AllTools_SafelyHandleNullTokens`

### Refactor Opportunities
- **P3-2 (Base Tool Framework)**: Consolidate to abstract base with protected abstract ActionHandlers
- **P2-1 (Command Wrapper)**: Extract try-catch-log pattern to decorator

---

## Pattern 2: Action Parameter Extraction and Normalization

**Pattern Name**: Case-Insensitive Action Dispatch
**Frequency**: 4/5 tools (ManageEditor, ManageMaterial, ManagePrefabs, + implicit in others)

### Current Implementation Pattern
```csharp
string action = @params["action"]?.ToString()?.ToLowerInvariant();
if (string.IsNullOrEmpty(action))
{
    return new ErrorResponse("Action parameter is required.");
}

switch (action)
{
    case "play":
    case "pause":
    case "stop":
    case "set_active_tool":
        // Each case calls specific handler
    default:
        return new ErrorResponse($"Unknown action: '{action}'");
}
```

### Behavior
- Tolerates uppercase: "PLAY", "Play", "play" all work
- Requires non-empty action
- Unknown actions explicitly rejected

### Tests Covering This Pattern
- `HandleCommand_ManageEditor_WithUppercaseAction_NormalizesAndDispatches`
- `HandleCommand_ActionNormalization_CaseInsensitive`
- `HandleCommand_ManageEditor_WithUnknownAction_ReturnsError`

### Related Patterns
- Parameter name fallback (camelCase vs snake_case) - Pattern 3
- Action validation - Pattern 4

---

## Pattern 3: Parameter Name Fallback Convention

**Pattern Name**: Dual Naming Support (camelCase + snake_case)
**Frequency**: 3/5 tools (FindGameObjects, ManageComponents, ManageEditor)

### Current Implementation Pattern
```csharp
// Accept both naming conventions
string searchMethod = ParamCoercion.CoerceString(
    @params["searchMethod"] ?? @params["search_method"],
    "by_name"  // default
);

string searchTerm = ParamCoercion.CoerceString(
    @params["searchTerm"] ?? @params["search_term"] ?? @params["target"],
    null
);
```

### Behavior
- First checks camelCase: `@params["searchMethod"]`
- Falls back to snake_case: `@params["search_method"]`
- Applies default if both null

### Applies To
- `searchMethod` / `search_method`
- `searchTerm` / `search_term` / `target`
- `pageSize` / `page_size`
- `buildIndex` / `build_index`
- Many others in ManageScene

### Tests Covering This Pattern
- `HandleCommand_FindGameObjects_WithCamelCaseSearchMethod_Succeeds`
- `HandleCommand_FindGameObjects_WithSnakeCaseSearchMethod_Succeeds`

### Refactor Opportunities
- Standardize on single naming convention (recommend camelCase)
- Update Python CLI to use camelCase consistently
- Use ToolParams wrapper to eliminate duplication

---

## Pattern 4: Parameter Validation Before State Mutation

**Pattern Name**: Early Return on Invalid Parameters
**Frequency**: 5/5 tools (100%)

### Current Implementation Pattern
```csharp
// In action handler, validate BEFORE making any changes
private static object SetActiveTool(string toolName)
{
    if (string.IsNullOrEmpty(toolName))
        return new ErrorResponse("'toolName' parameter required for set_active_tool.");

    // Only after validation, proceed with state change
    EditorTools.SetActiveTool(toolName);
    return new SuccessResponse("Tool selected");
}
```

### Behavior
- Required parameters checked immediately
- Error returned if validation fails
- No state mutation occurs on error path

### Tests Covering This Pattern
- `HandleCommand_ManageEditor_SetActiveTool_RequiresToolNameParameter`
- `HandleCommand_ManagePrefabs_WithoutRequiredPath_ReturnsError`

### Refactor Opportunities
- **P1-1 (ToolParams Wrapper)**: Centralize validation
```csharp
var toolName = @params.GetRequired("toolName", "'toolName' required for set_active_tool");
// Returns early if missing
```

---

## Pattern 5: Parameter Coercion and Type Conversion

**Pattern Name**: Multi-Type Parameter Coercion
**Frequency**: 5/5 tools (100%)

### Current Implementation Pattern
```csharp
// ParamCoercion already extracted utility
string searchMethod = ParamCoercion.CoerceString(@params["searchMethod"], "by_name");
bool includeInactive = ParamCoercion.CoerceBool(@params["includeInactive"], false);
int pageSize = int.TryParse(...) ? value : defaultValue;
```

### Supported Coercions
- String → string (with null coercion)
- JSON boolean → bool
- String "true"/"false" → bool
- String number → int
- JSON object → JObject
- Type-specific (Color, Vector3, etc.) in ManageMaterial

### Tests Covering This Pattern
- `HandleCommand_ManageEditor_WithBooleanParameter_AcceptsMultipleTypes`
- `HandleCommand_ManageMaterial_SetProperty_CoercesTypes`

### Current State
- **Already well-extracted** in ParamCoercion utility
- Ready for **P1-3 (UnityTypeConverter)** to consolidate further

---

## Pattern 6: Switch-Based Action Dispatch

**Pattern Name**: Action Router via Switch Statement
**Frequency**: 3/5 tools (ManageEditor, ManageMaterial, ManagePrefabs)

### Current Implementation Pattern
```csharp
switch (action)
{
    case "play":
        return Play();
    case "pause":
        return Pause();
    case "stop":
        return Stop();
    case "set_active_tool":
        string toolName = @params["toolName"]?.ToString();
        if (string.IsNullOrEmpty(toolName))
            return new ErrorResponse("...");
        return SetActiveTool(toolName);
    default:
        return new ErrorResponse($"Unknown action: '{action}'");
}
```

### Observations
- Each case either:
  - Directly calls action handler with no params
  - Extracts additional params and validates
  - Calls handler with extracted params
- Default case rejects unknown actions
- Handler results returned as-is

### Tools Using This Pattern
1. **ManageEditor**: 8 cases (play, pause, stop, set_active_tool, add_tag, remove_tag, add_layer, remove_layer)
2. **ManageMaterial**: 7 cases (ping, create, set_material_shader_property, set_material_color, assign_material_to_renderer, set_renderer_color, get_material_info)
3. **ManagePrefabs**: 4 cases (create_from_gameobject, get_info, get_hierarchy, modify_contents)

### Tests Covering This Pattern
- `HandleCommand_ManageEditor_PlayAction_DifferentFromStop`
- `HandleCommand_ManageEditor_WithUnknownAction_ReturnsError`

### Refactor Opportunities
- **P3-2 (Base Tool Framework)**: Move switch to base class with virtual handler methods
- Pattern enables decorator for error handling

---

## Pattern 7: Error Handling with Exception Wrapping

**Pattern Name**: Try-Catch-Log-Return
**Frequency**: 5/5 tools (100%)

### Current Implementation Pattern
```csharp
public static object HandleCommand(JObject @params)
{
    try
    {
        string action = @params["action"]?.ToString()?.ToLowerInvariant();
        if (string.IsNullOrEmpty(action))
            return new ErrorResponse("Action parameter is required.");

        switch (action)
        {
            // ... cases ...
        }
    }
    catch (Exception e)
    {
        McpLog.Error($"[ManageEditor] Action '{action}' failed: {e}");
        return new ErrorResponse($"Internal error: {e.Message}");
    }
}
```

### Behavior
- Catches all exceptions
- Logs via McpLog.Error()
- Converts to ErrorResponse
- Never throws to caller

### Tests Covering This Pattern
- `HandleCommand_ManagePrefabs_WithInvalidParameters_CatchesExceptionAndReturns`
- `HandleCommand_ManageMaterial_LogsErrorOnFailure`

### Refactor Opportunities
- **P2-1 (Command Wrapper Decorator)**: Extract try-catch-log pattern
```csharp
[CommandHandler("manage_editor")]
public static object HandleCommand(JObject @params) =>
    CommandHandlerDecorator.Execute(HandleCommand_IMPL, @params);
```

---

## Pattern 8: Response Object Consistency

**Pattern Name**: SuccessResponse/ErrorResponse Return Types
**Frequency**: 5/5 tools (100%)

### Current Implementation Pattern
```csharp
public class ErrorResponse
{
    public string Message { get; set; }
    public object Data { get; set; }
    // Serializable to JSON bridge
}

public class SuccessResponse
{
    public string Message { get; set; }
    public object Data { get; set; }
    // Serializable to JSON bridge
}

// Usage
return new SuccessResponse("Action completed", new { result = value });
return new ErrorResponse("Parameter missing", new { expected = "path" });
```

### Behavior
- All tools return Response objects
- Never return raw data
- Never return null (always Response)
- Serializable for bridge communication

### Tests Covering This Pattern
- `HandleCommand_ManageEditor_PlayAction_ReturnsResponseObject`
- `HandleCommand_ResponseObjects_AreSerializable`

### Current State
- **Already well-implemented** across all tools
- No refactoring needed for this pattern

---

## Pattern 9: Path Normalization and Validation

**Pattern Name**: Asset Path Sanitization
**Frequency**: 3/5 tools (ManagePrefabs, ManageMaterial, ManageScene)

### Current Implementation Pattern (ManageMaterial example)
```csharp
private static string NormalizePath(string path)
{
    if (string.IsNullOrEmpty(path)) return path;

    // Normalize separators and ensure Assets/ root
    path = AssetPathUtility.SanitizeAssetPath(path);

    // Ensure extension
    if (!path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
        path += ".mat";

    return path;
}
```

### Normalizations Applied
- Replace backslashes: `\` → `/`
- Remove trailing slashes
- Ensure "Assets/" prefix
- Add/validate file extension

### Tools Implementing Similar Logic
1. ManageMaterial: `.mat` extension
2. ManagePrefabs: `.prefab` extension, complex path resolution
3. ManageScene: `.unity` extension, subdirectory handling

### Tests Covering This Pattern
- `HandleCommand_ManageMaterial_NormalizesPathParameter`

### Refactor Opportunities
- **QW-3 (Extract Path Normalizer)**:
```csharp
public static string NormalizeAssetPath(
    string path,
    string extension = null,
    string defaultDir = null)
{
    // Single implementation used by all tools
}
```

---

## Pattern 10: Pagination and Result Limiting

**Pattern Name**: Paginated Search Results
**Frequency**: 1-2 tools (FindGameObjects primary, ManageScene secondary)

### Current Implementation Pattern (FindGameObjects)
```csharp
// Parse pagination parameters
var pagination = PaginationRequest.FromParams(@params, defaultPageSize: 50);
pagination.PageSize = Mathf.Clamp(pagination.PageSize, 1, 500);

// Get all results and paginate
var allIds = GameObjectLookup.SearchGameObjects(...);
var paginatedResult = PaginationResponse<int>.Create(allIds, pagination);

// Return with pagination metadata
return new SuccessResponse("Found GameObjects", new
{
    instanceIDs = paginatedResult.Items,
    pageSize = paginatedResult.PageSize,
    cursor = paginatedResult.Cursor,
    nextCursor = paginatedResult.NextCursor,
    totalCount = paginatedResult.TotalCount,
    hasMore = paginatedResult.HasMore
});
```

### Parameters
- `pageSize`: Items per page (clamped 1-500)
- `cursor`: Position in result set
- `maxNodes` / `maxDepth`: Safety limits

### Response Metadata
- `items`: Current page results
- `pageSize`: Actual page size used
- `cursor`: Current position
- `nextCursor`: Position for next page
- `totalCount`: Total matching items
- `hasMore`: Whether more pages exist

### Tests Covering This Pattern
- `HandleCommand_FindGameObjects_WithPaginationParameters`
- `HandleCommand_FindGameObjects_ClampsPageSizeToValidRange`

### Current State
- **Already well-extracted** in PaginationRequest/Response
- No duplication issues

---

## Pattern 11: Security and Feature Blacklisting

**Pattern Name**: Safety Blacklist Pattern
**Frequency**: 1/5 tools (ExecuteMenuItem primary)

### Current Implementation Pattern (ExecuteMenuItem)
```csharp
private static readonly HashSet<string> _menuPathBlacklist = new HashSet<string>(
    StringComparer.OrdinalIgnoreCase)
{
    "File/Quit",
};

public static object HandleCommand(JObject @params)
{
    string menuPath = @params["menu_path"]?.ToString() ?? @params["menuPath"]?.ToString();
    if (string.IsNullOrWhiteSpace(menuPath))
        return new ErrorResponse("Required parameter 'menu_path' is missing or empty.");

    if (_menuPathBlacklist.Contains(menuPath))
        return new ErrorResponse($"Execution of menu item '{menuPath}' is blocked for safety reasons.");

    try
    {
        bool executed = EditorApplication.ExecuteMenuItem(menuPath);
        if (!executed)
            return new ErrorResponse($"Failed to execute menu item '{menuPath}'.");
        return new SuccessResponse($"Attempted to execute menu item: '{menuPath}'.");
    }
    catch (Exception e)
    {
        McpLog.Error($"[MenuItemExecutor] Failed: {e}");
        return new ErrorResponse($"Error setting up execution: {e.Message}");
    }
}
```

### Behavior
- Maintains hardcoded blacklist of disruptive items
- Checks against blacklist before execution
- Case-insensitive matching

### Tests Covering This Pattern
- `HandleCommand_ExecuteMenuItem_BlocksBlacklistedItems`
- `HandleCommand_ExecuteMenuItem_RequiresMenuPath`

### Refactor Opportunities
- Consider configuration-based blacklist (EditorPrefs)
- Document why each item is blacklisted
- Consider whitelist approach for high-security use cases

---

## Quick Reference: Refactor Mapping

| Pattern | Current Issues | Refactor Item | Impact |
|---------|---|---|---|
| HandleCommand entry | Repetitive across 42 tools | P3-2 Base Tool | 40-50% code reduction |
| Action dispatch switch | 3+ tools implement identically | P3-2 Base Tool | Consolidate to virtual methods |
| Parameter validation | 997+ IsNullOrEmpty checks | P1-1 ToolParams | Single validation utility |
| Path normalization | 8+ duplicate implementations | QW-3 AssetPathNormalizer | ~100 lines saved |
| Error handling | 20x repeated try-catch-log | P2-1 Command Wrapper | Decorator pattern |
| Type coercion | Already extracted in ParamCoercion | P1-3 UnityTypeConverter | Enhance foundation |
| Parameter name fallback | 4+ tools implement dual naming | Standardization | Pick camelCase |
| Response objects | Consistent across tools | Already good | No refactoring needed |
| Pagination | Already extracted | Already good | No refactoring needed |
| Security blacklist | Only in ExecuteMenuItem | Consider enhancement | Config-based blacklist |

---

## Usage During Refactoring

1. **Before starting a refactor**, read the pattern description and "Current Implementation Pattern" section
2. **Check the test file** for relevant tests covering that pattern
3. **Verify all tests pass** with current implementation before changing
4. **During refactoring**, keep tests passing to ensure behavior preservation
5. **After refactoring**, run tests to confirm no behavior changed
6. **If tests fail**, compare old vs new implementation using this reference

---

## For P1-1 (ToolParams Wrapper) Example Refactoring

Based on this reference, P1-1 would introduce:

```csharp
// New ToolParams wrapper (replaces inline validation)
public class ToolParams
{
    private readonly JObject _params;

    public ToolParams(JObject @params) => _params = @params;

    public string GetAction() =>
        (_params["action"]?.ToString() ?? "").ToLowerInvariant();

    public string GetRequired(string key, string errorMsg = null)
    {
        var value = _params[key]?.ToString();
        if (string.IsNullOrEmpty(value))
            throw new ParameterException(errorMsg ?? $"'{key}' is required");
        return value;
    }

    public string Get(string key, string defaultValue = null) =>
        _params[key]?.ToString() ?? defaultValue;
}

// Usage reduces from:
if (string.IsNullOrEmpty(toolName))
    return new ErrorResponse("'toolName' parameter required");

// To:
var toolName = @params.GetRequired("toolName", "'toolName' required");
```

This reference enables that refactor while tests ensure correctness.
