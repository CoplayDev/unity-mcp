# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Context

**MCP for Unity** — a bridge that lets AI assistants (Claude, Cursor, Windsurf, etc.) control the Unity Editor through the Model Context Protocol (MCP). Forked from [CoplayDev/unity-mcp](https://github.com/CoplayDev/unity-mcp).

**Fork strategy:** We avoid modifying upstream code. Studio-specific tools go in separate files. When syncing with upstream, our additions should not conflict.

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
- `Server/` — Python MCP server (FastMCP 2.x, Python 3.10+, managed by `uv`)
- `MCPForUnity/` — Unity C# Editor package (Unity 2021.3+, Newtonsoft.Json)

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

### Domain Symmetry
Python MCP tools mirror C# Editor tools. Each domain exists in both:
- `Server/src/services/tools/manage_material.py` ↔ `MCPForUnity/Editor/Tools/ManageMaterial.cs`
- CLI commands (`Server/src/cli/commands/`) also mirror these but are a separate implementation.

## Key Patterns

### Python MCP Tool Registration
Tools in `Server/src/services/tools/` are auto-discovered. Use the `@mcp_for_unity_tool` decorator:
```python
from services.registry import mcp_for_unity_tool

@mcp_for_unity_tool(
    description="Does something in Unity.",
    group="core",  # core (default), vfx, animation, ui, scripting_ext, testing, probuilder, docs
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
var name = p.GetRequired("name");     // returns Result<string> with error handling
var size = p.GetInt("page_size") ?? 50;
```

### C# Resources
Resources use `[McpForUnityResource]` and follow the same `HandleCommand` pattern as tools. They provide read-only state to AI assistants.

### Paging
Always page results that could be large. Use `page_size` + `cursor` parameters, return `next_cursor` when more results exist.

### Composing Tools Internally (C#)
Use `CommandRegistry.InvokeCommandAsync` to call other tools from within a handler:
```csharp
var result = await CommandRegistry.InvokeCommandAsync("read_console", consoleParams);
```

## Commands

### Python Tests
```bash
# Python (all tests)
cd Server && uv run pytest tests/ -v

# Python (single test file)
cd Server && uv run pytest tests/test_manage_material.py -v

# Python (single test by name)
cd Server && uv run pytest tests/ -k "test_create_material" -v

# Python (with coverage)
cd Server && uv run pytest tests/ --cov --cov-report=html

# Unity - open TestProjects/UnityMCPTests in Unity, use Test Runner window
```

### Unity C# Tests (via CLI, requires Unity + MCP bridge running)
```bash
cd Server
uv run python -m cli.main editor tests                    # EditMode (default)
uv run python -m cli.main editor tests --mode PlayMode    # PlayMode
uv run python -m cli.main editor tests --async            # async launch
uv run python -m cli.main editor poll-test <job_id> --wait 60  # poll async job
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

**For studio-specific tools:** Create new files rather than modifying existing upstream files to minimize merge conflicts.

## Git Workflow

- **Branch off `beta`** for PRs — `main` is for stable releases only
- Remote: `origin` → `git@github-the1studio:The1Studio/unity-mcp.git` (our fork)
- Upstream: `CoplayDev/unity-mcp` (sync periodically)

## Code Philosophy

- **Minimal abstraction** — Three similar lines > a helper used once. Abstract only at 3+ use cases.
- **Delete rather than deprecate** — No `_unused` renames or `// removed` comments.
- **Keep tools focused** — One tool, one job. No "convenient" parameter bloat.
- **Resources for reading** — Keep resources smart and focused, not "read everything" dumps.
- **Test coverage required** — Every new feature needs tests. Run them before PRs.
