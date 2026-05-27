---
id: troubleshooting
slug: /guides/troubleshooting
title: Common Setup Problems
sidebar_label: Troubleshooting / FAQ
description: Real-world fixes for the issues people actually hit — macOS dyld errors, WSL2 bridging, DLL version conflicts, and per-client FAQs.
---

# Common Setup Problems

## macOS: Claude CLI fails to start (dyld ICU library not loaded)

**Symptoms:**
- MCP for Unity error: *"Failed to start Claude CLI. dyld: Library not loaded: /usr/local/opt/icu4c/lib/libicui18n.71.dylib …"*
- Running `claude` in Terminal fails with missing `libicui18n.xx.dylib`.

**Cause:**
Homebrew Node (or the `claude` binary) was linked against an ICU version that's no longer installed; dyld can't find that dylib.

**Fix options (pick one):**

**Reinstall Homebrew Node** (relinks to current ICU), then reinstall CLI:

```bash
brew update
brew reinstall node
npm uninstall -g @anthropic-ai/claude-code
npm install -g @anthropic-ai/claude-code
```

**Use NVM Node** (avoids Homebrew ICU churn):

```bash
nvm install --lts
nvm use --lts
npm install -g @anthropic-ai/claude-code
# Unity MCP → Claude Code → Choose Claude Location → ~/.nvm/versions/node/<ver>/bin/claude
```

**Use the native installer** (puts `claude` in a stable path):

```bash
# macOS / Linux
curl -fsSL https://claude.ai/install.sh | bash
# Unity MCP → Claude Code → Choose Claude Location → /opt/homebrew/bin/claude or ~/.local/bin/claude
```

**After fixing:** in MCP for Unity (Claude Code section), click **"Choose Claude Location"** and select the working `claude` binary, then **Register** again.

---

## WSL2: Connecting Claude Code (Linux) to Unity (Windows)

If you're running Claude Code from WSL2 and Unity on Windows, the MCP server runs on the Windows side but Claude Code needs to reach it from WSL. Here's how to bridge the two.

*Contributed by [@aollivier82](https://github.com/CoplayDev/unity-mcp/issues/712).*

### 1. Install the Unity package

In Unity Package Manager, add by git URL:

```text
https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#main
```

In the MCP for Unity settings, change the port to **8090** (or any free port — the default 8080 can conflict with other services like Tailscale).

### 2. Install uv on Windows

The MCP server requires `uv`. From an admin PowerShell:

```powershell
irm https://astral.sh/uv/install.ps1 | iex
```

### 3. Set up port forwarding from WSL to Windows

WSL2 runs in a separate network namespace, so you need to forward the MCP port. From an admin PowerShell:

```powershell
netsh interface portproxy add v4tov4 listenport=8090 listenaddress=0.0.0.0 connectport=8090 connectaddress=127.0.0.1
```

Then add a firewall rule to allow it:

```powershell
New-NetFirewallRule -DisplayName "Unity MCP Server" -Direction Inbound -LocalPort 8090 -Protocol TCP -Action Allow
```

### 4. Find your WSL host IP

From inside WSL:

```bash
cat /etc/resolv.conf | grep nameserver | awk '{print $2}'
```

This prints the Windows host IP as seen from WSL (e.g. `172.21.48.1`). It's a private address that varies per machine.

### 5. Add the MCP server to Claude Code

From WSL, using the IP from step 4:

```bash
claude mcp add --transport http UnityMCP http://<YOUR_WSL_HOST_IP>:8090/mcp
```

For example:

```bash
claude mcp add --transport http UnityMCP http://172.21.48.1:8090/mcp
```

Note: this uses **HTTP transport**, not stdio — since the server is running on the Windows side.

### 6. Verify

Start Unity, then start Claude Code. You should see `UnityMCP` listed as a connected MCP server. Test it by asking Claude to get your scene info.

**Notes:**
- If you restart your machine, the WSL host IP may change. Re-run step 4 and update the MCP config if needed.
- The port proxy persists across reboots. To remove it later: `netsh interface portproxy delete v4tov4 listenport=8090 listenaddress=0.0.0.0`
- If the connection fails, make sure Unity is running and the MCP server is started (check the MCP for Unity panel).

---

## DLL reference mismatch with Unity AI Assistant package

If you're using **Unity 6.3+** alongside the **Unity AI Assistant** package, you may encounter `System.Collections.Immutable` version conflicts.

*Reported by [@rkroska](https://github.com/CoplayDev/unity-mcp/issues/557).*

**Symptoms:**
- Compilation errors referencing `System.Collections.Immutable` version mismatches
- Errors appear after installing MCP for Unity in a project that has the Unity AI Assistant package

**Cause:**
Unity AI Assistant bundles `System.Collections.Immutable` v10, while MCP for Unity's CodeAnalysis dependency needs v9. Unity's built-in version may be v8. These conflict during assembly resolution.

**Fix options:**

- **Option A (recommended):** If you don't need Unity AI Assistant, remove it via Package Manager. Then install `System.Collections.Immutable` v9.0.0 as a DLL in `Assets/Plugins/`.
- **Option B:** If you need both packages, install `System.Collections.Immutable` v9.0.0 in `Assets/Plugins/` to satisfy MCP's dependency. The AI Assistant's v10 reference should be forward-compatible.

**Note:** This is a Unity assembly resolution issue, not specific to MCP for Unity. Unity doesn't have a NuGet-style dependency resolver, so DLL version conflicts must be resolved manually.

---

## "No Unity Instances Found"

:::tip When in doubt, restart your client
Clients like Claude Code or JetBrains Rider can get confused if you switch transport modes mid-session. Restart the client so it picks up the new configuration.
:::

If restarting doesn't fix it:
- Check the MCP for Unity status panel — does it say `Connected`?
- Open `mcpforunity://instances` in your client. If it returns an empty list, the Unity-side bridge isn't running.
- Try **Window → MCP for Unity → Restart Server**.

---

## FAQ — Claude Code

**Q: Unity can't find `claude` even though Terminal can.**
A: macOS apps launched from Finder / Hub don't inherit your shell PATH. In the MCP for Unity window, click **"Choose Claude Location"** and select the absolute path (e.g., `/opt/homebrew/bin/claude` or `~/.nvm/versions/node/<ver>/bin/claude`).

**Q: I installed via NVM; where is `claude`?**
A: Typically `~/.nvm/versions/node/<ver>/bin/claude`. The MCP for Unity UI also scans NVM versions and you can browse to it via **"Choose Claude Location"**.

**Q: The Register button says "Claude Not Found".**
A: Install the CLI or set the path. Click the orange **[HELP]** link in the MCP for Unity window for step-by-step install instructions, then choose the binary location. See also: [Install or Repair Claude Code CLI](/guides/claude-code-cli).

## FAQ — VS Code

**Q: When I first set up and start the MCP for Unity server in VS Code, I get a failed response that says `Canceled: Canceled`.**
A: Start a new chat — the bad chat didn't pick up the MCP server configuration.

![Canceled error screenshot](https://github.com/user-attachments/assets/571e2aeb-c286-4235-ab2b-8285c0db3296)

## FAQ — Cursor / Windsurf / VS Code (Windows uv path)

**Q: My MCP client keeps failing to launch the server even though `uv` is installed.**
A: Some Windows machines have multiple `uv.exe` locations. Auto-config sometimes picks a less stable path, causing the launch to fail or auto-rewrite on every restart. Use **"Choose UV Install Location"** in the MCP for Unity window and pin the **WinGet Links shim** path (`%LOCALAPPDATA%\Microsoft\WinGet\Links\uv.exe`) — it's stable across uv upgrades.
