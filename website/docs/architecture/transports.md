---
id: transports
slug: /architecture/transports
title: Transport Modes
sidebar_label: Transport Modes
description: HTTP versus stdio — when to use each, what the trade-offs are, and how multi-agent isolation works.
---

# Transport Modes

MCP for Unity supports two transports between the MCP client and the Python server. The choice affects multi-agent capability, configuration shape, and a few subtle behaviors around instance routing.

## Quick decision

| If you want… | Use |
|---|---|
| Multiple MCP clients sharing one Unity instance | **HTTP** |
| Multiple Unity instances driven from one client | either |
| Lowest setup friction | **stdio** (Claude Desktop default) |
| Remote-hosted server (cloud, Docker) | **HTTP** |
| Marketplace distribution that can't ship Python | **HTTP** (remote URL) |

## HTTP (default)

**Architecture:** one Python process, one shared WebSocket hub at `/hub/plugin`, multiple MCP clients can connect concurrently. Each client gets a `client_id` and session-keyed state.

**Endpoint:** `http://localhost:8080/mcp`

**MCP client config:**

```json
{
  "mcpServers": {
    "unityMCP": { "url": "http://localhost:8080/mcp" }
  }
}
```

**What you gain:**
- Multi-agent: Claude Code and Cursor open at the same time, both seeing the same Unity Editor
- Session isolation: each client's active instance, tool-group visibility, and middleware state are independent
- Remote hosting: the server can run on a different machine or in a container

**What you give up:**
- Port-number shorthand for `set_active_instance` (HTTP enforces `Name@hash`)
- A small amount of setup complexity if you bind to LAN — see [Security](https://github.com/CoplayDev/unity-mcp/blob/beta/SECURITY.md)

## Stdio

**Architecture:** the MCP client spawns a dedicated Python process via `stdio`, communicating over stdin/stdout. The Python process talks to Unity over a legacy TCP bridge.

**MCP client config (macOS/Linux):**

```json
{
  "mcpServers": {
    "unityMCP": {
      "command": "uvx",
      "args": ["--from", "mcpforunityserver", "mcp-for-unity", "--transport", "stdio"]
    }
  }
}
```

**What you gain:**
- Lowest configuration friction; works without HTTP port allocation
- Port-number shorthand: `set_active_instance(instance="6401")`
- Claude Desktop only supports stdio — that's why MCP for Unity silently selects stdio when configuring Claude Desktop, even if you have HTTP picked elsewhere

**What you give up:**
- Single-agent: a new MCP client connection replaces the previous one
- No native session isolation: switching the active Unity instance in one client affects what the next client sees
- Cannot host remotely

## What "instance" means in each mode

- **HTTP**: instance state is keyed by `client_id` in middleware. Two clients can hold different active instances concurrently against the same Unity Editor pool.
- **Stdio**: instance state is process-local. Since there's one Python process per client, isolation is implicit — but switching processes loses the old state.

See [Multi-Instance Routing](/guides/multi-instance) for the routing API.

## Switching transport

In the Unity Editor: **Window → MCP for Unity → Settings**, pick `HTTP` or `stdio`, click **Configure All Detected Clients**. The configurator rewrites each client's MCP config to match.

Claude Desktop is the exception — it's always written as stdio regardless of your selection, because it doesn't support HTTP.

## Network security (HTTP only)

By default, HTTP binds to loopback (`127.0.0.1` / `::1`). Binding to all interfaces (`0.0.0.0` / `::`) requires explicit opt-in: **Advanced Settings → Allow LAN Bind (HTTP Local)**.

Remote endpoints require `https://`. To allow plaintext `http://` for a remote URL, opt in via **Allow Insecure Remote HTTP**. Both guards are fail-closed: if you don't flip the switch, the server refuses the unsafe configuration.

## Where this is implemented

- Python: `Server/src/transport/` (plugin hub, websocket transport, legacy stdio bridge)
- C#: `MCPForUnity/Editor/Services/` (transport clients, server management, stdio bridge host)
- v8 migration notes: [/migrations/v8](/migrations/v8) — the architectural story of HTTP arriving
