---
id: uv-setup
slug: /guides/uv-setup
title: Install or Repair uv + Python
sidebar_label: uv + Python Setup
description: Install or repair uv and Python — the runtime MCP for Unity needs to launch the Python server from Cursor, VS Code, Windsurf, Rider, and other uv-based clients.
---

# Install or Repair uv + Python

The key to configuring MCP with **Cursor, VS Code, Windsurf, and Rider is [`uv`](https://docs.astral.sh/uv/)**.

- `uv` is a fast Python package manager used to install and run the Unity MCP Server (`mcp-for-unity`).
- **How it's used:** your MCP client config points to `command: uvx` with args like `--from mcpforunityserver mcp-for-unity --transport stdio`. The client invokes `uvx` directly to launch the server.
- **Why it matters:** if `uv` isn't installed or on PATH, Cursor / Windsurf / VS Code can't start the server. The MCP for Unity window will show **"uv Not Found"** until fixed.
- **Detection / override:** the MCP for Unity window auto-detects `uv` in common locations and on PATH. If not found, use **"Choose UV Install Location"** to navigate to your `uv` binary and save the path.

:::tip When in doubt, restart your client
Clients like Claude Code or JetBrains Rider can get confused if you switch from `http` to `stdio` (or vice versa). If they say **"No Unity Instances found"**, restart the client so it picks up the new configuration.
:::

## Requirements

You need **Python 3.10+** and the **`uv`** package manager.

### Verify

```bash
python3 --version   # should be 3.10+
uv --version        # should print a version like "uv 0.x"
```

## Install Python

**macOS:**

```bash
# Option A: Official installer (recommended)
# Download from https://www.python.org/downloads/

# Option B: Homebrew (3.12 is the latest LTS as of writing; 3.10 also works)
brew install python@3.12
```

**Windows:**

```powershell
# Official installer (recommended)
# Download from https://www.python.org/downloads/windows/
```

## Install uv

**macOS / Linux / WSL:**

```bash
curl -LsSf https://astral.sh/uv/install.sh | sh
# or Homebrew on macOS
brew install uv
```

**Windows PowerShell:**

```powershell
powershell -ExecutionPolicy ByPass -c "irm https://astral.sh/uv/install.ps1 | iex"
# or
winget install --id=astral-sh.uv -e
```

## Common uv locations

| OS | Path |
|---|---|
| **macOS** | `/opt/homebrew/bin/uv`, `/usr/local/bin/uv`, `~/.local/bin/uv` |
| **Linux** | `/usr/local/bin/uv`, `/usr/bin/uv`, `~/.local/bin/uv` |
| **Windows** | `%LOCALAPPDATA%/Programs/Python/Python3xx/Scripts/uv.exe` |

## MCP for Unity window behavior

- If `uv` isn't found, the status panel shows a red **"uv Not Found"** with a hint **"Make sure uv is installed! [CLICK]"**.
- Use **"Choose UV Install Location"** to browse to the `uv` binary. This saves the path and reconfigures automatically.
- On macOS, Unity launched from Finder may not inherit your PATH. Setting the `uv` location here is the easiest fix.

## Notes and gotchas

- **macOS GUI apps don't inherit your shell startup files.** PATH may differ from Terminal. Set `uv` via the MCP window to avoid PATH issues.
- **Windows vs WSL:** if you installed `uv` inside WSL only, Windows-native Unity can't see it. Install `uv` on Windows, or use the MCP window to point to a Windows `uv.exe`.
- **Custom locations:** if you installed `uv` somewhere non-standard, the picker path is stored in `UnityMCP.UvPath` and persists across sessions.

## What the "Repair Python Env" button does

- Deletes the server's `.venv` and `.python-version` (if present)
- Runs `uv sync` in the Unity MCP Server `src` directory to rebuild a clean environment
- Useful after Python upgrades or missing modules

## Where the Unity MCP Server is installed

| OS | Path |
|---|---|
| **macOS** | `~/Library/Application Support/UnityMCP/UnityMcpServer/src` (or `~/Library/AppSupport/UnityMCP/UnityMcpServer/src` via symlink) |
| **Windows** | `%USERPROFILE%/AppData/Local/UnityMCP/UnityMcpServer/src` |
| **Linux** | `~/.local/share/UnityMCP/UnityMcpServer/src` |

## Manual repair / run

```bash
cd <UnityMcpServer/src>
uv sync
uv run server.py
```
