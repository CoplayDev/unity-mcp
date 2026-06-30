---
id: tool-groups
slug: /guides/tool-groups
title: Tool Groups and manage_tools
sidebar_label: Tool Groups
description: Per-session visibility for the 47 tools. Activate vfx, animation, ui, testing, etc. only when you need them.
---

# Tool Groups

MCP for Unity ships 47 tools, but exposing all of them to the LLM at once balloons the prompt and dilutes routing decisions. So tools are sorted into **groups**, and only `core` is enabled by default.

## The groups

| Group | Default | Description |
|---|---|---|
| `core` | enabled | Essential scene, script, asset, and editor tools — always on. |
| `animation` | off | Animator control, AnimationClip creation. |
| `ui` | off | UI Toolkit — UXML, USS, UIDocument. |
| `vfx` | off | VFX Graph, shaders, procedural textures. |
| `scripting_ext` | off | ScriptableObject management. |
| `testing` | off | Test runner and async test jobs. |
| `probuilder` | off | ProBuilder 3D modeling. Requires `com.unity.probuilder` package. |
| `profiling` | off | Profiler session control, counters, memory snapshots, Frame Debugger. |
| `docs` | off | Unity API reflection and documentation lookup. |

## Enabling a group

Use the `manage_tools` meta-tool from your prompt:

> Activate the `vfx` group so we can author shaders.

The assistant calls:

```
manage_tools(action="activate", group="vfx")
```

After activation, the group's tools appear in the next tool listing and are usable for the remainder of the session.

## Listing what's available

```
manage_tools(action="list_groups")
```

Returns every group with its current activation state and tool names.

## Deactivating

```
manage_tools(action="deactivate", group="vfx")
```

Useful when a group's tools are confusing the assistant — e.g., `manage_shader` and `manage_material` both apply to materials in different ways. Disabling the one you're not using keeps the assistant focused.

## Other actions

- `sync` — refreshes visibility from the Unity Editor's per-tool toggle UI. Use after toggling tools in `Window > MCP for Unity > Tools`.
- `reset` — restores defaults (only `core` enabled).

## Why this exists

Three reasons:

1. **Prompt economy**: each visible tool adds tokens to every assistant call. Hiding what you're not using is real money saved at scale.
2. **Routing clarity**: when the LLM picks between 47 tools versus the 30 core tools, the wrong-tool rate drops measurably.
3. **Package hygiene**: tools in `probuilder` only work if `com.unity.probuilder` is installed; hiding them by default avoids confusing errors.

## Server vs. session state

- The Unity Editor maintains a per-tool **toggle UI** (`Window > MCP for Unity > Tools`) that controls server-side visibility.
- The `manage_tools` meta-tool controls **per-session** visibility — different MCP sessions can see different groups even against the same server.

`sync` reconciles the two: it pulls the Editor's toggle states into the current session.

## Related reference

- [`manage_tools`](/reference/tools/core/manage_tools) — full tool reference
- [`tool_groups` resource](/reference/resources) — discoverable group catalog
