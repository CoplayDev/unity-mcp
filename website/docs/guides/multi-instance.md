---
id: multi-instance
slug: /guides/multi-instance
title: Multi-Instance Routing
sidebar_label: Multi-Instance Routing
description: Drive several Unity Editors from a single MCP session with an active instance and optional per-call routing.
---

# Multi-Instance Routing

MCP for Unity can drive several connected Unity Editors from one MCP session.
The normal workflow is active-instance based: select an Editor once, then omit routing
parameters from ordinary tool calls and resource reads.

## When this comes up

- You're refactoring a shared package and need to test the same change in two projects
- You're comparing behavior between Unity LTS and Unity 6
- You have a runtime project + a tooling project both connected
- You're driving a CI fixture project alongside your day-to-day work

## Instance identifiers

Read the discovery resource before selecting an instance:

> `mcpforunity://instances`

Each entry contains an `id` in the form `Name@hash`, where `Name` is derived
from the project directory name and `hash` identifies the project path.
Selectors support:

- An exact `Name@hash` ID, which is the recommended form
- A unique hash prefix
- A port number in stdio mode only

Invalid or ambiguous explicit selectors fail instead of falling back to the
active or default instance.

## Set the active instance

```text
set_active_instance(instance="MyGame@a1b2c3d4")
```

The active instance is stored in the current MCP session. Subsequent targetable
tools and Unity-backed resources use it until the session changes it. This is
the recommended pattern for normal work.

You can also use a unique hash prefix or, in stdio mode, a port:

```text
set_active_instance(instance="a1b")
set_active_instance(instance="6401")
```

## Route one tool call

Targetable tools expose an optional `unity_instance` parameter in `tools/list`.
It is an advanced override and is normally omitted:

```text
manage_scene(
  action="get_hierarchy",
  unity_instance="OtherProject@f6e7d8c9"
)
```

The override applies only to this call. It does not update the active instance
or affect later calls. Project-scoped custom tools use the same contract through
`execute_custom_tool`.

Server-only tools such as `set_active_instance`, `manage_tools`, and
`debug_request_context` do not expose this routing parameter because their
behavior is not a Unity Editor operation.

## Route one resource read

MCP resource reads do not have tool arguments. Unity-backed resources therefore
advertise an RFC 6570 query form through `resources/templates/list`:

```text
mcpforunity://editor/state{?unity_instance}
```

Expand it as a normal URI query. URL-encoding `@` is recommended:

```text
mcpforunity://editor/state?unity_instance=OtherProject%40f6e7d8c9
```

Existing resource URIs remain valid. Parameterized resources keep their
business query parameters in the same template. For example:

```text
mcpforunity://scene/gameobject/123/components?page_size=5&cursor=0&include_properties=false&unity_instance=OtherProject%40f6e7d8c9
```

Inspect both `resources/list` and `resources/templates/list`: concrete default
resources remain discoverable through the former, while parameterized and
per-call forms are advertised through the latter.

The discovery and server-only resources `unity_instances`, `tool_groups`,
`gameobject_api`, and `prefab_api` do not expose per-call targeting.

## Routing priority and compatibility

Targetable MCP calls resolve their effective instance in this order:

1. Explicit per-call `unity_instance`
2. The MCP session's active instance
3. The server's existing auto/default behavior

When `unity_instance` is omitted, existing behavior is unchanged. A sole local
Editor can still be auto-selected; multiple Editors with no active selection
still produce the existing selection error.

## Concurrent calls

Per-call routing is request-local, not session state. Calls in the same MCP
session can overlap while targeting different Editors, including Tool/Tool,
Resource/Resource, and Tool/Resource combinations. An explicit override can
also run alongside an unqualified call that uses the active instance.

Completion, errors, timeouts, and cancellation cannot write the override back to
the active instance or leak it into a later request.

## Local HTTP REST API

The local REST endpoints use request-local routing as well. They do not use the
MCP session's active instance.

For `POST /api/command`, put `unity_instance` at the request envelope's top
level. It must not appear inside the Unity command `params`:

```json
{
  "type": "manage_scene",
  "unity_instance": "OtherProject@f6e7d8c9",
  "params": {
    "action": "get_active"
  }
}
```

For project-scoped tool discovery, use the historical query name:

```text
GET /api/custom-tools?instance=OtherProject%40f6e7d8c9
```

These local REST routes are disabled in remote-hosted mode.

## HTTP and stdio

- **HTTP MCP:** active selection is isolated by the FastMCP session ID. Multiple sessions can hold different active instances on one Python server.
- **stdio MCP:** one server process normally belongs to one client session. Port-number selectors are available because local Editor port discovery exists.
- **Remote-hosted HTTP:** instance discovery and selector resolution are scoped to the authenticated user. Auto-selection is disabled, so set an active instance or provide an explicit per-call selector.

## Related reference

- [`set_active_instance`](/reference/tools/core/set_active_instance) — full tool reference
- [`unity_instances` resource](/reference/resources) — discovery surface
- [Transport Modes](/architecture/transports)
