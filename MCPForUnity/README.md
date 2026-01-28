# MCP for Unity — Editor Plugin Guide

Use this guide to configure and run MCP for Unity inside the Unity Editor. Installation is covered elsewhere; this document focuses on the Editor window, client configuration, and troubleshooting.

## Open the window
- Unity menu: Window > MCP for Unity

The window has four areas: Server Status, Unity Bridge, MCP Client Configuration, and Script Validation.

---

## Quick start
1. Open Window > MCP for Unity.
2. Click “Auto-Setup”.
3. If prompted:
   - Select the packaged server folder (`Server`) if you want to run the bundled implementation.
   - Install Python and/or uv/uvx if missing so the server can be managed locally.
   - For Claude Code, ensure the `claude` CLI is installed.
4. Click “Start Bridge” if the Unity Bridge shows “Stopped”.
5. Use your MCP client (Cursor, VS Code, Windsurf, Claude Code) to connect.

---

## Server Status
- Status dot and label:
  - Installed / Installed (Embedded) / Not Installed.
- Mode and ports:
  - Mode: Auto or Standard.
  - Ports: Unity (varies; shown in UI), MCP 6500.
- Actions:
  - Auto-Setup: Registers/updates your selected MCP client(s), ensures bridge connectivity. Shows “Connected ✓” after success.
  - Rebuild MCP Server: Rebuilds the Python based MCP server
  - Select server folder…: Choose the local `Server` folder (dev only; usually not needed when using uvx).
  - Verify again: Re-checks server presence.
  - If Python isn’t detected, use “Open Install Instructions”.
- HTTP Server Command foldout:
  - Expands to display the exact `uvx` command Unity will run.
  - Includes a copy button and the “Start Local HTTP Server” action so you can launch or reuse the command elsewhere.

---

## Unity Bridge
- Shows Running or Stopped with a status dot.
- Start/Stop Bridge button toggles the Unity bridge process used by MCP clients to talk to Unity.
- Tip: After Auto-Setup, the bridge may auto-start in Auto mode.

---

## MCP Client Configuration
- Select Client: Choose your target MCP client (e.g., Cursor, VS Code, Windsurf, Claude Code).
- Per-client actions:
  - Cursor / VS Code / Windsurf:
    - Auto Configure: Writes/updates your config to launch the server via `uvx` with the current package version:
      - Command: uvx (or your overridden path)
      - Args: --from <git-url> mcp-for-unity
    - Manual Setup: Opens a window with a pre-filled JSON snippet to copy/paste into your client config.
    - Choose UV Install Location: If uv/uvx isn’t on PATH, select the executable.
    - A compact “Config:” line shows the resolved config file name once uv/server are detected.
  - Claude Code:
    - Register with Claude Code / Unregister MCP for Unity with Claude Code.
    - If the CLI isn’t found, click “Choose Claude Install Location”.
    - The window displays the resolved Claude CLI path when detected.

Notes:
- The UI shows a status dot and a short status text (e.g., “Configured”, “uv Not Found”, “Claude Not Found”).
- Use “Auto Configure” for one-click setup; use “Manual Setup” when you prefer to review/copy config.

---

## Script Validation
- Validation Level options:
  - Basic — Only syntax checks
  - Standard — Syntax + Unity practices
  - Comprehensive — All checks + semantic analysis
  - Strict — Full semantic validation (requires Roslyn)
- Pick a level based on your project’s needs. A description is shown under the dropdown.

---

## Troubleshooting
- Python or `uv` not found:
  - Help: [Fix MCP for Unity with Cursor, VS Code & Windsurf](https://github.com/CoplayDev/unity-mcp/wiki/1.-Fix-Unity-MCP-and-Cursor,-VSCode-&-Windsurf)
- Claude CLI not found:
  - Help: [Fix MCP for Unity with Claude Code](https://github.com/CoplayDev/unity-mcp/wiki/2.-Fix-Unity-MCP-and-Claude-Code)

---

## Tips
- Use Cmd+Shift+M (macOS) / Ctrl+Shift+M (Windows, Linux) to toggle the MCP for Unity window.
- Enable “Show Debug Logs” in the header for more details in the Console when diagnosing issues.

---
