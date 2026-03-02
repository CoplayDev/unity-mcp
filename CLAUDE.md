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

## How the Tool System Works

Tools are registered on **both sides** through a decorator/attribute pattern:

**Python side** — `@mcp_for_unity_tool` decorator in `Server/src/services/tools/` registers MCP tools. Some tools forward to Unity (via `send_with_unity_instance`), others are server-only.

**C# side** — `[McpForUnityTool]` attribute in `MCPForUnity/Editor/Tools/` marks classes that handle incoming commands from the Python server. Tool discovery is automatic via `ToolDiscoveryService`.

**Domain symmetry:** Each domain exists in both codebases:
- `Server/src/cli/commands/materials.py` ↔ `MCPForUnity/Editor/Tools/ManageMaterial.cs`
- `Server/src/services/tools/manage_material.py` (MCP registration) routes to the C# handler

**The `cli/commands/` vs `services/tools/` split:** `services/tools/` contains the MCP tool definitions (what LLMs call). `cli/commands/` provides the `unity-mcp` CLI (`uv run python -m cli.main editor play`, etc.) — a separate interface using Click.

## Key Patterns

### Parameter Handling (C#)
`ToolParams` wraps `JObject` for consistent validation. Supports both snake_case and camelCase automatically:
```csharp
var p = new ToolParams(parameters);
var name = p.GetRequired("name");     // returns Result<string> with error handling
var size = p.GetInt("page_size") ?? 50;
```

### Error Handling (Python)
Use `@handle_unity_errors` decorator for CLI commands. For MCP tools, errors propagate through `send_with_unity_instance`.

### Paging
Always page results that could be large. Use `page_size` + `cursor` parameters, return `next_cursor` when more results exist.

## Commands

### Python Tests
```bash
cd Server
uv run pytest tests/ -v                          # all tests
uv run pytest tests/integration/test_find_gameobjects.py -v  # single file
uv run pytest tests/ -k "test_name_pattern" -v   # by name pattern
uv run pytest tests/ --cov --cov-report=html      # with coverage
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
1. In Unity: **Window > MCP for Unity > Settings > Advanced Settings**
2. Set **Server Source Override** to your local `Server/` path
3. Enable **Dev Mode** to force fresh installs (`--refresh` on uvx)
4. Use `python mcp_source.py` to switch Unity package source (upstream main/beta, your fork, or local path)

## Git Workflow

- **Branch off `beta`** for PRs — `main` is for stable releases only
- Remote: `origin` → `git@github-the1studio:The1Studio/unity-mcp.git` (our fork)
- Upstream: `CoplayDev/unity-mcp` (sync periodically)

## Adding a New Tool

1. Create Python MCP tool in `Server/src/services/tools/manage_<domain>.py` using `@mcp_for_unity_tool`
2. Create Python CLI command in `Server/src/cli/commands/<domain>.py` (optional, for CLI access)
3. Create C# handler in `MCPForUnity/Editor/Tools/Manage<Domain>.cs` with `[McpForUnityTool]`
4. Add tests in `Server/tests/` and `TestProjects/UnityMCPTests/Assets/Tests/`

**For studio-specific tools:** Create new files rather than modifying existing upstream files to minimize merge conflicts.

## Code Philosophy

- **Minimal abstraction** — Three similar lines > a helper used once. Abstract only at 3+ use cases.
- **Delete rather than deprecate** — No `_unused` renames or `// removed` comments.
- **Keep tools focused** — One tool, one job. No "convenient" parameter bloat.
- **Resources for reading** — Keep resources smart and focused, not "read everything" dumps.
