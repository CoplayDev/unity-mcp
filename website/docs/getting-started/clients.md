---
id: clients
slug: /getting-started/clients
title: Choosing an MCP Client
sidebar_label: Choosing a Client
description: Capability matrix across every MCP client MCP for Unity auto-configures.
---

# Choosing an MCP Client

MCP for Unity auto-configures every client the package detects on your machine. The differences below decide which one fits your workflow.

## Capability matrix

| Client | Transport | Auto-config | Streaming reasoning | Free tier | Notes |
|---|---|---|---|---|---|
| **Claude Desktop** | stdio only | yes | yes | yes (rate-limited) | Easiest setup. Stdio is silently chosen even if HTTP is selected globally. |
| **Claude Code** | HTTP | yes | yes | needs Anthropic plan | First-party. Strong with multi-tool workflows. |
| **Cursor** | HTTP | yes | yes | partial | Requires an MCP toggle in Cursor's own settings after auto-config. |
| **VS Code (Copilot)** | HTTP | yes | yes | with Copilot | Configures under `servers` (not `mcpServers`). |
| **Windsurf** | HTTP | yes | yes | yes | Auto-connects after config. |
| **Cline** | HTTP | yes | yes | yes | Auto-connects after config. |
| **GitHub Copilot CLI** | HTTP | yes | yes | with Copilot | Terminal-based agent. |
| **Codex** | HTTP | yes | yes | with OpenAI | Auto-connects. |
| **Qwen Code** | HTTP | yes | yes | yes | Auto-connects. |
| **Gemini CLI** | HTTP | yes | yes | yes | Auto-connects. |
| **OpenClaw** | HTTP / stdio | yes | yes | yes | Requires `openclaw-mcp-bridge` plugin enabled. Follows MCP for Unity's transport choice. |
| **Antigravity** | HTTP | yes | yes | varies | Requires an MCP toggle in Antigravity settings. |

## How to pick

- **You want it to just work**: Claude Desktop. Stdio means no port conflicts and no firewall prompts.
- **You're building a multi-agent or remote workflow**: anything HTTP. Multiple clients can share one Python server; see [Multi-Instance Routing](/guides/multi-instance).
- **You're already in your IDE**: Cursor, VS Code Copilot, or Cline — keeps you in flow.
- **You want a terminal**: Claude Code, Copilot CLI, Codex, Gemini CLI, or Qwen Code.

## Manual configuration

If auto-config doesn't run (offline machine, sandboxed install, etc.), copy the snippet under **Manual MCP client configuration** in [Install](./install) into your client's MCP config file.

## Per-client toggle locations

A few clients need an MCP toggle flipped on after the configurator writes their config. Find it here:

- **Cursor** → Settings → MCP → enable the `unityMCP` server
- **Antigravity** → Settings → MCP servers → enable
- **OpenClaw** → enable the `openclaw-mcp-bridge` plugin

Everything else just connects on next launch.

## When you update the package

Run **Window → MCP for Unity → Configure All Detected Clients** any time. It's safe to re-run — the configurator writes idempotently.
