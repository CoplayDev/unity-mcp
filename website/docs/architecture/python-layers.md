---
id: python-layers
slug: /architecture/python-layers
title: Three-Layer Python Design
sidebar_label: Three-Layer Python Design
description: The Python server has three independent surfaces — MCP tools, CLI commands, and resources — that all funnel into the same C# Editor handlers.
---

# Three-Layer Python Design

The Python server (`Server/src/`) exposes three distinct surfaces. They look similar but serve different consumers and are **not auto-generated from each other**.

| Layer | Where | Framework | Consumer | Transport to Unity |
|---|---|---|---|---|
| **MCP Tools** | `Server/src/services/tools/` | FastMCP (`@mcp_for_unity_tool`) | AI assistants via MCP | WebSocket (`send_with_unity_instance`) |
| **CLI Commands** | `Server/src/cli/commands/` | Click (`@click.command`) | Developers in a terminal | HTTP (`run_command`) |
| **Resources** | `Server/src/services/resources/` | FastMCP (`@mcp_for_unity_resource`) | AI assistants, read-only | WebSocket |

Both MCP tools and CLI commands eventually call the same C# `HandleCommand` methods inside `MCPForUnity/Editor/Tools/`. Resources are read-only — they observe state without mutating it.

## Why three layers, not one

Each surface has a different shape of consumer:

- **MCP tools** need rich type annotations (`Annotated[Type, "description"]`) because they're handed to an LLM. The descriptions are the prompt the LLM reads.
- **CLI commands** need composable flags, shell-friendly defaults, and graceful error messages. Click gives those for free.
- **Resources** need to be cheap to call repeatedly because the LLM polls them. They use a lighter decorator and skip the routing middleware.

Trying to autogenerate one layer from another erodes the ergonomics of all three. The cost is keeping the three in sync — which is mostly a discipline problem solved by domain symmetry.

## Domain symmetry

When you add a new domain (say, `manage_navigation`), you write **three** files:

```
Server/src/services/tools/manage_navigation.py    # @mcp_for_unity_tool
Server/src/cli/commands/navigation.py             # @click.command
MCPForUnity/Editor/Tools/ManageNavigation.cs      # [McpForUnityTool]
```

The Python tool and CLI command both invoke the C# handler — they just take different paths to it.

## Tool registration

Tools are auto-discovered by walking `Server/src/services/tools/`. Each `.py` file with `@mcp_for_unity_tool`-decorated functions is imported at server startup; the decorator side-effects populate a global registry (`services.registry`). The registry is also what `tools/generate_docs_reference.py` reads to emit the [tool reference](/reference/tools).

A tool's `group` parameter controls per-session visibility — see [Tool Groups](/guides/tool-groups). `group=None` means the tool is always visible (server meta-tools like `set_active_instance` and `manage_tools`).

## Where the layers diverge from "just call the C# handler"

- **MCP tools** add parameter normalization (camelCase → snake_case via `ParamNormalizerMiddleware`), telemetry, and per-session routing.
- **CLI commands** add `@handle_unity_errors` for terminal-friendly stack traces, and synchronous wrappers around the async core.
- **Resources** skip middleware entirely — they're meant to be hot-path.

## Server entry point

`Server/src/main.py` (~935 lines) is the orchestrator:

1. Builds the FastMCP server
2. Calls `register_all_tools(mcp)` — auto-discovery
3. Calls `register_all_resources(mcp)` — same pattern, different decorator
4. Sets up the WebSocket hub for HTTP transport
5. Configures middleware (telemetry, normalization, instance routing)
6. Starts the transport (`http`/`stdio` from `core.config`)

## Where to read more

- Tool/CLI handler shape: `Server/src/services/tools/manage_material.py` is a canonical example
- Registry: `Server/src/services/registry/tool_registry.py` (~130 LOC, the single source the docs reference generator reads)
- Transport: `Server/src/transport/` — plugin hub (`plugin_hub.py`), websocket client, legacy stdio bridge
- C# side: `MCPForUnity/Editor/Tools/ManageMaterial.cs` is the C# half of `manage_material`
