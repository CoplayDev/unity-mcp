# CLAUDE.md - Project Overview for AI Assistants

## What This Project Is

**MCP for Unity** is a bridge that lets AI assistants (Claude, Cursor, Windsurf, etc.) control the Unity Editor through the Model Context Protocol (MCP). It enables AI-driven game development workflows - creating GameObjects, editing scripts, managing assets, running tests, and more.

## Architecture

```text
AI Assistant (Claude/Cursor)
        ↓ MCP Protocol (stdio/SSE)
Python Server (Server/src/)
        ↓ WebSocket + HTTP
Unity Editor Plugin (MCPForUnity/)
        ↓ Unity Editor API
Scene, Assets, Scripts
```

**Two codebases, one system:**
- `Server/` - Python MCP server using FastMCP
- `MCPForUnity/` - Unity C# Editor package

## Directory Structure

```text
├── Server/                     # Python MCP Server
│   ├── src/
│   │   ├── cli/commands/       # Tool implementations (20 domain modules)
│   │   ├── transport/          # MCP protocol, WebSocket bridge
│   │   ├── services/           # Custom tools, resources
│   │   └── core/               # Telemetry, logging, config
│   └── tests/                  # 502 Python tests
├── MCPForUnity/                # Unity Editor Package
│   └── Editor/
│       ├── Tools/              # C# tool implementations (42 files)
│       ├── Services/           # Bridge, state management
│       ├── Helpers/            # Utilities (27 files)
│       └── Windows/            # Editor UI
├── TestProjects/UnityMCPTests/ # Unity test project (605 tests)
└── tools/                      # Build/release scripts
```

## Code Philosophy

### 1. Domain Symmetry
Python CLI commands mirror C# Editor tools. Each domain (materials, prefabs, scripts, etc.) exists in both:
- `Server/src/cli/commands/materials.py` ↔ `MCPForUnity/Editor/Tools/ManageMaterial.cs`

### 2. Minimal Abstraction
Avoid premature abstraction. Three similar lines of code is better than a helper that's used once. Only abstract when you have 3+ genuine use cases.

### 3. Delete Rather Than Deprecate
When removing functionality, delete it completely. No `_unused` renames, no `// removed` comments, no backwards-compatibility shims for internal code.

### 4. Test Coverage Required
Every new feature needs tests. We have 1100+ tests across Python and C#. Run them before PRs.

### 5. Keep Tools Focused
Each MCP tool does one thing well. Resist the urge to add "convenient" parameters that bloat the API surface.

### 6. Use Resources for reading.
Keep them smart and focused rather than "read everything" type resources. That way resources are quick and LLM-friendly. There are plenty of examples in the codebase to model on (gameobject, prefab, etc.)

## Key Patterns

### Parameter Handling (C#)
Use `ToolParams` for consistent parameter validation:
```csharp
var p = new ToolParams(parameters);
var pageSize = p.GetInt("page_size", "pageSize") ?? 50;
var name = p.RequireString("name");
```

### Error Handling (Python CLI)
Use the `@handle_unity_errors` decorator:
```python
@handle_unity_errors
async def my_command(ctx, ...):
    result = await call_unity_tool(...)
```

### Paging Large Results
Always page results that could be large (hierarchies, components, search results):
- Use `page_size` and `cursor` parameters
- Return `next_cursor` when more results exist

## Common Tasks

### Running Tests
```bash
# Python
cd Server && uv run pytest tests/ -v

# Unity - open TestProjects/UnityMCPTests in Unity, use Test Runner window
```

### Local Development
1. Set **Server Source Override** in MCP for Unity Advanced Settings to your local `Server/` path
2. Enable **Dev Mode** checkbox to force fresh installs
3. Use `mcp_source.py` to switch Unity package sources
4. Test on Windows and Mac if possible, and multiple clients (Claude Desktop and Claude Code are tricky for configuration       as of this writing)

### Adding a New Tool
1. Add Python command in `Server/src/cli/commands/<domain>.py`
2. Add C# implementation in `MCPForUnity/Editor/Tools/Manage<Domain>.cs`
3. Add tests in both `Server/tests/` and `TestProjects/UnityMCPTests/Assets/Tests/`

## What Not To Do

- Don't add features without tests
- Don't create helper functions for one-time operations
- Don't add error handling for scenarios that can't happen
- Don't commit to `main` directly - branch off `beta` for PRs
- Don't add docstrings/comments to code you didn't change
