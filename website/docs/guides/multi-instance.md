---
id: multi-instance
slug: /guides/multi-instance
title: Multi-Instance Routing
sidebar_label: Multi-Instance Routing
description: Drive several Unity Editors from a single MCP session with set_active_instance and per-call routing.
---

# Multi-Instance Routing

You can have several Unity Editors open at once and aim a single MCP session at any of them.

## When this comes up

- You're refactoring a shared package and need to test the same change in two projects
- You're comparing behavior between Unity LTS and Unity 6
- You have a runtime project + a tooling project both connected
- You're driving a CI fixture project alongside your day-to-day work

## How instances are identified

Each connected Unity Editor advertises a stable ID of the form `Name@hash`, where:

- `Name` is the project's `productName` from Player Settings
- `hash` is a stable 8-character hash derived from the project path

Example: `MyGame@a1b2c3d4`.

You can also reference an instance by:

- **Hash prefix** (e.g. `a1b` if it's unambiguous)
- **Port number** — stdio transport only

## Discovering instances

Read the resource:

> `mcpforunity://instances`

It returns the list of currently connected Editors with their `Name@hash`, project path, transport, and port. Most MCP clients expose this as the `unity_instances` resource.

## Setting the active instance for the session

```
set_active_instance(instance="MyGame@a1b2c3d4")
```

Once set, **every subsequent tool call** in the session routes to that instance until you change it. This is the most common pattern: choose once, then prompt normally.

You can also use:

```
set_active_instance(instance="a1b")         # hash prefix
set_active_instance(instance="6401")        # port number (stdio only)
```

## Routing a single call without changing the session default

Pass `unity_instance` on the individual tool call:

```
manage_scene(action="get_hierarchy", unity_instance="MyGame@a1b2c3d4")
```

This is useful for comparing two projects in the same prompt — e.g., "Read the same script from both projects and tell me what differs."

The server accepts the same value formats as `set_active_instance`: `Name@hash`, hash prefix, or (stdio) port number.

## What happens with no active instance

- **One Unity Editor connected** → it's used automatically.
- **Multiple Editors connected and no active set** → the server errors with the available instance list. Call `set_active_instance` and retry.

## HTTP vs stdio differences

- **HTTP**: instance state is keyed by the MCP session (`MCP-Session-Id`), so two MCP clients can target different Editors at the same time on the same Python server.
- **Stdio**: port-number shorthand works because there's a separate Python process per client, and the session key is a per-subprocess UUID. HTTP shares one process and uses `Name@hash` exclusively.

The session is the *only* key. It is deliberately not the client id — see the routing contract for why.

## Running several agents against one Editor

Routing decides *which* Editor a call reaches. It says nothing about what happens when several agents
reach the same one at once, which is the other half of the multi-agent story.

A single Editor executes one command at a time. Unity's receive loop awaits each command to completion
before reading the next frame off the socket, so concurrent calls queue rather than overlap. Under a
four-agent write load, cheap reads that normally take ~5 s stretched to ~17 s while another agent was
churning the hierarchy, and recovered within a cycle or two once it stopped. Batching calls does not
help: throughput stayed flat at roughly 2–3 seconds per call whether five or ten were issued together.

Expect spurious "instance not found" errors. Resolving an instance runs before the call is dispatched,
and a domain reload briefly empties the registry while the Editor re-registers, so calls landing in
that window fail with:

```
Instance 'MyGame@a1b2c3d4' not found. Available: none.
Read mcpforunity://instances for current sessions.
```

`Available: none` is misleading. The Editor is usually alive and serving other calls a second or two
either side. These failures are clean, because the call never reached Unity — nothing was applied.

**Retrying is not free.** There is no idempotency key, so the server cannot tell a retry from a fresh
command, and neither can Unity. Whether a failed command is safe to retry depends on how far it got:

| Where the command was when the server gave up | Effect | Safe to retry |
|---|---|---|
| Not yet dispatched (instance resolution failed) | None | Yes |
| Queued, never started (connection torn down) | None | Yes |
| Already executing in Unity, exceeded the timeout | **Applied** — late result is discarded | No, applies twice |

The last row is the one to watch. The command runs to completion and its result is dropped, so the
caller is told it failed while the effect landed. It needs a command that exceeds the 30 second budget
*after* Unity has begun executing it, which ordinary tool calls do not approach — but `execute_code`,
long imports and test runs can. Treat `hint: "retry"` on those as "check before retrying", not
"retry blindly".

None of this degraded the Editor itself. Four agents issuing 527 calls over eleven minutes left it
alive and responsive, with memory growth proportional to the work done and flat thereafter.

## Related reference

- [`set_active_instance`](/reference/tools/core/set_active_instance) — full tool reference
- [`unity_instances` resource](/reference/resources) — discovery surface
- [Instance Routing](/architecture/instance-routing) — the routing contract and its rationale
