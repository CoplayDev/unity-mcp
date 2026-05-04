# MCP for Unity - Developer Guide

| [English](README-DEV.md) | [简体中文](README-DEV-zh.md) |
|---------------------------|------------------------------|

## Contributing

**Branch off `beta`** to create PRs. The `main` branch is reserved for stable releases.

Before proposing major new features, please reach out to discuss - someone may already be working on it or it may have been considered previously. Open an issue or discussion to coordinate.

Keep PRs focused. A small fix with a clear repro and the exact Unity/package versions is much easier to review than a broad cleanup bundled with behavior changes.

For bug fixes, include:

- Unity version(s) tested
- package source used (`#beta`, `#main`, tag, fork branch, or `file:`)
- resolved Git commit from `Packages/packages-lock.json` when the package source is a Git URL
- commands/tests run locally

Avoid mixing release/version bumps with feature or bug-fix PRs. The release workflows handle package and server version updates.

## Repository Map

- `MCPForUnity/` - the Unity package: Editor UI, C# tool handlers, resources, compatibility shims, package metadata.
- `Server/` - the Python MCP server, CLI, FastMCP tool/resource registry, transports, and server-side tests.
- `TestProjects/UnityMCPTests/` - Unity project used for EditMode/PlayMode tests. Its manifest points at `file:../../../MCPForUnity`.
- `docs/` - user, contributor, release, migration, and reference documentation.
- `tools/` - release/build helpers, documentation update prompt, stress tools, and publishing scripts.
- `CustomTools/` - examples and support code for project-defined custom tools.

## Local Development Setup

### 0. Prepare the Python Server

For server work, use the same setup path as CI:

```bash
cd Server
uv sync
uv pip install -e ".[dev]"
uv run pytest tests/ -v --tb=short
```

Most Python unit tests do not need Unity running. Integration tests that talk to the Editor require a Unity instance with the bridge connected.

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

When testing Unity package changes, prefer **Local workspace**. Do not patch `Library/PackageCache/` directly; Unity may overwrite it on the next resolve.

`#beta` and `#main` are moving branch names. When debugging a package-source issue, check `Packages/packages-lock.json` as well as `Packages/manifest.json`; the lock file tells you the commit Unity actually resolved.

Example Git package lock entry:

```json
"com.coplaydev.unity-mcp": {
  "version": "https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#beta",
  "source": "git",
  "hash": "<resolved-git-commit>"
}
```

## Adding or Changing Tools and Resources

Built-in Unity tools usually have two sides:

- a C# handler under `MCPForUnity/Editor/Tools/`
- a Python-facing tool under `Server/src/services/tools/`

Resources follow the same split:

- C# resources under `MCPForUnity/Editor/Resources/`
- Python resources under `Server/src/services/resources/`

Use `[McpForUnityTool]` / `[McpForUnityResource]` on the Unity side and `@mcp_for_unity_tool` / `@mcp_for_unity_resource` on the server side. If the tool is long-running, use the existing `PendingResponse` / polling patterns instead of blocking the bridge.

Current server tool groups are:

- `core`
- `docs`
- `vfx`
- `animation`
- `ui`
- `scripting_ext`
- `testing`
- `probuilder`
- `profiling`

Tools with `group=None` are server meta-tools and are always visible. The default enabled group is `core`; other groups can be enabled through the Tools tab or `manage_tools`.

When adding, removing, or renaming tools/resources, update the public docs and client metadata. Start with `tools/UPDATE_DOCS_PROMPT.md`, then review the changes manually.

## Tool Selection & the Meta-Tool

MCP for Unity organizes tools into **groups**. You can selectively enable or disable tools to control which capabilities are exposed to AI clients — reducing context window usage and focusing the AI on relevant tools.

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

Useful narrower runs:

```bash
uv run pytest tests/test_manage_camera.py -v
uv run pytest tests/integration/test_run_tests_async.py -v
uv run pytest tests/ -v --tb=short
```

### Unity C# Tests

Located in `TestProjects/UnityMCPTests/Assets/Tests/`.

The test project consumes the local Unity package from this repo. Use it for package import/compile checks before opening a PR that touches `MCPForUnity/Runtime/`, `MCPForUnity/Editor/`, or package metadata.

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
run_tests(mode="PlayMode", init_timeout=120000)  # PlayMode may need longer init due to domain reload
get_test_job(job_id="<id>", wait_timeout=60)
```

### Code Coverage

```bash
cd Server
uv run pytest tests/ --cov --cov-report=html
open htmlcov/index.html
```

## Compatibility Notes

The Unity package currently declares Unity `2021.3` as its minimum version in `MCPForUnity/package.json`. Code that uses Unity API conditionals (`UNITY_2022_3_OR_NEWER`, `UNITY_2023_1_OR_NEWER`, `UNITY_6000_*`) should be tested against the affected editor line, not just the oldest supported version.

For compatibility PRs, note the exact editor versions you tested in the PR body. If a Git package URL is involved, include the resolved `packages-lock.json` hash.

## Troubleshooting During Development

- **Unity still loads an old Git package**: close Unity, check `Packages/packages-lock.json`, then refresh Package Manager. If needed, remove only the stale `Library/PackageCache/com.coplaydev.unity-mcp@<hash>` folder while Unity is closed.
- **Unity opens in Safe Mode after changing package source**: the package failed to compile before MCP can start. Fix the compile errors first; the MCP server cannot recover from package compile failures.
- **Server changes are not picked up**: make sure **Server Source Override** points to your local `Server/` directory and **Dev Mode (Force fresh server install)** is enabled.
- **Stdio tool visibility looks stale**: call `manage_tools(action="sync")` or restart the MCP session. HTTP mode can push `tools/list_changed` notifications automatically.
- **Multiple Unity editors are open**: use `mcpforunity://instances` and `set_active_instance` to confirm which project the server is targeting.

