# MCP for Unity - Developer Guide

| [English](README-DEV.md) | [简体中文](README-DEV-zh.md) |
|---------------------------|------------------------------|

## Contributing

**Branch off `beta`** to create PRs. The `main` branch is reserved for stable releases.

Before proposing major new features, please reach out to discuss - someone may already be working on it or it may have been considered previously. Open an issue or discussion to coordinate.

## Local Development Setup

### 1. Point Unity to Your Local Server

For the fastest iteration when working on the Python server:

1. Open Unity and go to **Window > MCP for Unity**
2. Open **Settings > Advanced Settings**
3. Set **Server Source Override** to your local `Server/` directory path
4. Enable **Dev Mode (Force fresh server install)** - this adds `--refresh` to uvx commands so your changes are picked up on every server start

### 2. Switch Package Sources

You may want to use the `mcp_source.py` script to quickly switch your Unity project between different MCP package sources [allows you to quickly point your personal project to your local or remote unity-mcp repo, or the live upstream (Coplay) versions of the unity-mcp package]:

```bash
python mcp_source.py
```

Options:
1. **Upstream main** - stable release (CoplayDev/unity-mcp)
2. **Upstream beta** - development branch (CoplayDev/unity-mcp#beta)
3. **Remote branch** - your fork's current branch
4. **Local workspace** - file: URL to your local MCPForUnity folder

After switching, open Package Manager in Unity and Refresh to re-resolve packages.

## Tool Selection & the Meta-Tool

MCP for Unity organizes tools into **groups** (Core, VFX & Shaders, Animation, UI Toolkit, Scripting Extensions, Testing). You can selectively enable or disable tools to control which capabilities are exposed to AI clients — reducing context window usage and focusing the AI on relevant tools.

### Using the Tools Tab in the Editor

Open **Window > MCP for Unity** and switch to the **Tools** tab. Each tool group is displayed as a collapsible foldout with:

- **Per-tool toggles** — click individual tool toggles to enable or disable them.
- **Group checkbox** — a checkbox embedded directly in each group's foldout header (next to the group title) enables or disables all tools in that group at once without expanding or collapsing the foldout.
- **Enable All / Disable All** — global buttons to toggle all tools.
- **Rescan** — re-discovers tools from assemblies (useful after adding new `[McpForUnityTool]` classes).
- **Reconfigure Clients** — re-registers tools with the server and reconfigures all detected MCP clients in one click, applying your changes without navigating back to the Clients tab.

### How Changes Propagate

Tool visibility changes work differently depending on the transport mode:

**HTTP mode** (recommended):

1. Toggling a tool calls `ReregisterToolsAsync()`, which sends the updated enabled tool list to the Python server over WebSocket.
2. The server updates its internal tool visibility via `mcp.enable()`/`mcp.disable()` per group.
3. The server sends a `tools/list_changed` MCP notification to all connected client sessions.
4. Already-connected clients (Claude Desktop, VS Code, etc.) automatically receive the updated tool list.

**Stdio mode**:

1. Toggles are persisted locally but cannot be pushed to the server (no WebSocket connection).
2. The server starts with all groups enabled. After changing toggles, ask the AI to run `manage_tools` with `action='sync'` — this pulls the current tool states from Unity and syncs server visibility.
3. Alternatively, restart the server to pick up changes.

### The `manage_tools` Meta-Tool

The server exposes a built-in `manage_tools` tool (always visible, not group-gated) that AIs can call directly:

| Action | Description |
|--------|-------------|
| `list_groups` | Lists all tool groups with their tools and enable/disable status |
| `activate` | Enables a tool group by name (e.g., `group="vfx"`) |
| `deactivate` | Disables a tool group by name |
| `sync` | Pulls current tool states from Unity and syncs server visibility (essential for stdio mode) |
| `reset` | Restores default tool visibility |

### When You Need to Reconfigure

After toggling tools on/off, MCP clients need to learn about the changes:

- **HTTP mode**: Changes propagate automatically via `tools/list_changed`. Most clients pick this up immediately. If a client doesn't, click **Reconfigure Clients** on the Tools tab, or go to Clients tab and click Configure.
- **Stdio mode**: The server process needs to be told about changes. Either ask the AI to call `manage_tools(action='sync')`, or restart the MCP session. Click **Reconfigure Clients** to re-register all clients with updated config.

## Running Tests

All major new features (and some minor ones) must include test coverage. It's so easy to get LLMs to write tests, ya gotta do it!

### Python Tests 

Located in `Server/tests/`:

```bash
cd Server
uv run pytest tests/ -v
```

### Unity C# Tests

Located in `TestProjects/UnityMCPTests/Assets/Tests/`.

**Using the CLI** (requires Unity running with MCP bridge connected):

```bash
cd Server

# Run EditMode tests (default)
uv run python -m cli.main editor tests

# Run PlayMode tests
uv run python -m cli.main editor tests --mode PlayMode

# Run async and poll for results (useful for long test runs)
uv run python -m cli.main editor tests --async
uv run python -m cli.main editor poll-test <job_id> --wait 60

# Show only failed tests
uv run python -m cli.main editor tests --failed-only
```

**Using MCP tools directly** (from any MCP client):

```
run_tests(mode="EditMode")
get_test_job(job_id="<id>", wait_timeout=60)
```

### Code Coverage

```bash
cd Server
uv run pytest tests/ --cov --cov-report=html
open htmlcov/index.html
```

