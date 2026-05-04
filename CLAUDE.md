# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This Project Is

**MCP for Unity** is a bridge that lets AI assistants (Claude, Cursor, Windsurf, etc.) control the Unity Editor through the Model Context Protocol (MCP). It enables AI-driven game development workflows - creating GameObjects, editing scripts, managing assets, running tests, and more.

## Architecture

```text
AI Assistant (Claude/Cursor)
        ↓ MCP Protocol (stdio/HTTP)
Python Server (Server/src/)
        ↓ WebSocket + HTTP
Unity Editor Plugin (MCPForUnity/)
        ↓ Unity Editor API
Scene, Assets, Scripts
```

**Two codebases, one system:**
- `Server/` - Python MCP server using FastMCP
- `MCPForUnity/` - Unity C# Editor package

### Three Layers on the Python Side

The Python server has three distinct layers. These are **not** auto-generated from each other:

| Layer | Location | Framework | Purpose |
|-------|----------|-----------|---------|
| **MCP Tools** | `Server/src/services/tools/` | FastMCP (`@mcp_for_unity_tool`) | Exposed to AI assistants via MCP protocol |
| **CLI Commands** | `Server/src/cli/commands/` | Click (`@click.command`) | Terminal interface for developers |
| **Resources** | `Server/src/services/resources/` | FastMCP (`@mcp_for_unity_resource`) | Read-only state exposed to AI assistants |

MCP tools call Unity via WebSocket (`send_with_unity_instance`). CLI commands call Unity via HTTP (`run_command`). Both route to the same C# `HandleCommand` methods.

### Transport Modes

- **Stdio**: Single-agent only. Separate Python process per client. Legacy TCP bridge to Unity. New connections stomp old ones.
- **HTTP**: Multi-agent ready. Single shared Python server. WebSocket hub at `/hub/plugin`. Session isolation via `client_id`.

## Code Philosophy

### 1. Domain Symmetry
Python MCP tools mirror C# Editor tools. Each domain exists in both:
- `Server/src/services/tools/manage_material.py` ↔ `MCPForUnity/Editor/Tools/ManageMaterial.cs`
- CLI commands (`Server/src/cli/commands/`) also mirror these but are a separate implementation.

### 2. Minimal Abstraction
Avoid premature abstraction. Three similar lines of code is better than a helper that's used once. Only abstract when you have 3+ genuine use cases.

### 3. Delete Rather Than Deprecate
When removing functionality, delete it completely. No `_unused` renames, no `// removed` comments, no backwards-compatibility shims for internal code.

### 4. Test Coverage Required
Every new feature needs tests. Run them before PRs.

### 5. Keep Tools Focused
Each MCP tool does one thing well. Resist the urge to add "convenient" parameters that bloat the API surface.

### 6. Use Resources for Reading
Keep them smart and focused rather than "read everything" type resources. Resources should be quick and LLM-friendly.

## Key Patterns

### Python MCP Tool Registration
Tools in `Server/src/services/tools/` are auto-discovered. Use the `@mcp_for_unity_tool` decorator:
```python
from services.registry import mcp_for_unity_tool

@mcp_for_unity_tool(
    description="Does something in Unity.",
    group="core",  # core (default), vfx, animation, ui, scripting_ext, testing, probuilder, profiling, docs
)
async def manage_something(
    ctx: Context,
    action: Annotated[Literal["create", "delete"], "Action to perform"],
) -> dict[str, Any]:
    unity_instance = await get_unity_instance_from_context(ctx)
    params = {"action": action}
    response = await send_with_unity_instance(async_send_command_with_retry, unity_instance, "manage_something", params)
    return response
```

The `group` parameter controls tool visibility. Only `"core"` is enabled by default. Non-core groups (vfx, animation, etc.) start disabled and are toggled via `manage_tools`.

### Python CLI Error Handling
CLI commands (not MCP tools) use the `@handle_unity_errors` decorator:
```python
@handle_unity_errors
async def my_command(ctx, ...):
    result = await call_unity_tool(...)
```

### C# Tool Registration
Tools are auto-discovered by `CommandRegistry` via reflection. Use the `[McpForUnityTool]` attribute:
```csharp
[McpForUnityTool("manage_something", AutoRegister = false, Group = "core")]
public static class ManageSomething
{
    // Sync handler (most tools):
    public static object HandleCommand(JObject @params)
    {
        var p = new ToolParams(@params);
        // ...
        return new SuccessResponse("Done.", new { data = result });
    }

    // OR async handler (for long-running operations like play-test, refresh, batch):
    public static async Task<object> HandleCommand(JObject @params)
    {
        // CommandRegistry detects Task return type automatically
        await SomeAsyncOperation();
        return new SuccessResponse("Done.");
    }
}
```

Async handlers use `EditorApplication.update` polling with `TaskCompletionSource` — see `RefreshUnity.cs` for the canonical pattern.

### C# Parameter Handling
Use `ToolParams` for consistent parameter validation:
```csharp
var p = new ToolParams(parameters);
var pageSize = p.GetInt("page_size", "pageSize") ?? 50;
var name = p.RequireString("name");
```

### C# Resources
Resources use `[McpForUnityResource]` and follow the same `HandleCommand` pattern as tools. They provide read-only state to AI assistants.

### Paging Large Results
Always page results that could be large (hierarchies, components, search results):
- Use `page_size` and `cursor` parameters
- Return `next_cursor` when more results exist

### Composing Tools Internally (C#)
Use `CommandRegistry.InvokeCommandAsync` to call other tools from within a handler:
```csharp
var result = await CommandRegistry.InvokeCommandAsync("read_console", consoleParams);
```

### Unity API Compatibility Shims
We support a wide Unity version range (2021+ → 6.x → CoreCLR 6.8). When an API is renamed, deprecated, or removed across versions, **don't sprinkle `#if UNITY_x_y_OR_NEWER` at every call site** — add a shim in `MCPForUnity/Runtime/Helpers/Unity*Compat.cs` and route every caller through it.

The catalog of active shims, the policy for when to add one, what does NOT belong in a shim, and the reflection-cache pattern all live in **`MCPForUnity/Runtime/Helpers/UnityCompatShims.cs`** — the XML doc on that empty marker class is the source of truth and ships inside the UPM package, so end-users can `F12`/Go-to-definition into it. Sources for current deprecations: Unity 6.x upgrade guides and the [CoreCLR 2026 thread](https://discussions.unity.com/t/path-to-coreclr-2026-upgrade-guide/1714279).

## Commands

### Running Tests
```bash
# Python (all tests)
cd Server && uv run pytest tests/ -v

# Python (single test file)
cd Server && uv run pytest tests/test_manage_material.py -v

# Python (single test by name)
cd Server && uv run pytest tests/ -k "test_create_material" -v

# Unity - open TestProjects/UnityMCPTests in Unity, use Test Runner window
```

### Local Development
1. Set **Server Source Override** in MCP for Unity Advanced Settings to your local `Server/` path
2. Enable **Dev Mode** checkbox to force fresh installs
3. Use `mcp_source.py` to switch Unity package sources
4. Test on Windows and Mac if possible, and multiple clients (Claude Desktop and Claude Code are tricky for configuration as of this writing)

### Adding a New Tool
1. Add Python MCP tool in `Server/src/services/tools/manage_<domain>.py` using `@mcp_for_unity_tool`
2. Add Python CLI commands in `Server/src/cli/commands/<domain>.py` using Click
3. Add C# implementation in `MCPForUnity/Editor/Tools/Manage<Domain>.cs` with `[McpForUnityTool]`
4. Add Python tests in `Server/tests/test_manage_<domain>.py`
5. Add Unity tests in `TestProjects/UnityMCPTests/Assets/Tests/`

## What Not To Do

- Don't add features without tests
- Don't create helper functions for one-time operations
- Don't add error handling for scenarios that can't happen
- Don't commit to `main` directly - branch off `beta` for PRs
- Don't add docstrings/comments to code you didn't change
