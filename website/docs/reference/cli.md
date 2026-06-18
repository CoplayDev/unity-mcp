---
id: cli
slug: /reference/cli
title: CLI Reference
sidebar_label: CLI
description: The mcp-for-unity command-line interface — invocation, global flags, command groups, and how each maps to the equivalent MCP tool.
---

# CLI Reference

The `mcp-for-unity` CLI is a developer-facing terminal for the same Unity automations the MCP tools expose. Both invoke the same C# `HandleCommand` methods on the Unity side — see [Three-Layer Python Design](/architecture/python-layers) for why both layers exist.

## Invocation

```bash
# Run via uvx (no install)
uvx --from mcpforunityserver mcp-for-unity <command> [args]

# Run from a Server checkout
cd Server && uv run mcp-for-unity <command> [args]

# Run via the dedicated CLI entry point (alias)
uvx --from mcpforunityserver unity-mcp <command> [args]
```

## How it talks to Unity

The CLI uses **HTTP** to the Python server (default `http://127.0.0.1:8080`), regardless of how your MCP clients are configured. The Python server in turn talks to the connected Unity Editor via WebSocket. MCP tools take a similar path via WebSocket directly; CLI commands take HTTP.

## Global flags

| Flag | Default | Meaning |
|---|---|---|
| `--host` | `127.0.0.1` | Python server host to connect to |
| `--port` | `8080` | Python server port |
| `--instance` | (auto) | Target Unity instance (`Name@hash`, hash prefix, or port number) |
| `--format` | `text` | Output format: `text` or `json` |
| `--verbose / -v` | off | Print full request/response payloads |
| `--version` | — | Print CLI version and exit |
| `--help` | — | Show command help |

For multi-instance setups, see [Multi-Instance Routing](/guides/multi-instance).

## Command groups

The CLI mirrors the MCP tool catalog. Each command group wraps one or more `manage_*` tools.

| Group | What it does | Equivalent MCP tool |
|---|---|---|
| `mcp-for-unity instance` | List instances, check connection, set active | [`set_active_instance`](/reference/tools/core/set_active_instance) |
| `mcp-for-unity scene` | Load/save/query/edit scenes | [`manage_scene`](/reference/tools/core/manage_scene) |
| `mcp-for-unity gameobject` | Create/transform/delete GameObjects | [`manage_gameobject`](/reference/tools/core/manage_gameobject) |
| `mcp-for-unity component` | Add/remove/configure components | [`manage_components`](/reference/tools/core/manage_components) |
| `mcp-for-unity script` | Create/read/modify C# scripts | [`manage_script`](/reference/tools/core/manage_script) |
| `mcp-for-unity asset` | Asset import/create/modify/search | [`manage_asset`](/reference/tools/core/manage_asset) |
| `mcp-for-unity material` | Material CRUD + shader props | [`manage_material`](/reference/tools/core/manage_material) |
| `mcp-for-unity prefab` | Prefab create/instantiate/unpack | [`manage_prefabs`](/reference/tools/core/manage_prefabs) |
| `mcp-for-unity texture` | Texture create + patterns/gradients | [`manage_texture`](/reference/tools/vfx/manage_texture) |
| `mcp-for-unity shader` | Shader CRUD | [`manage_shader`](/reference/tools/vfx/manage_shader) |
| `mcp-for-unity vfx` | VFX, particle systems, trails | [`manage_vfx`](/reference/tools/vfx/manage_vfx) |
| `mcp-for-unity camera` | Camera + Cinemachine presets | [`manage_camera`](/reference/tools/core/manage_camera) |
| `mcp-for-unity graphics` | Volumes, post-processing, light bake | [`manage_graphics`](/reference/tools/core/manage_graphics) |
| `mcp-for-unity lighting` | Lighting-specific operations | (subset of graphics) |
| `mcp-for-unity physics` | 3D + 2D physics, joints, queries | [`manage_physics`](/reference/tools/core/manage_physics) |
| `mcp-for-unity audio` | Audio operations | (subset of asset) |
| `mcp-for-unity animation` | Animator + AnimationClip | [`manage_animation`](/reference/tools/animation/manage_animation) |
| `mcp-for-unity ui` | UI Toolkit — UXML/USS/UIDocument | [`manage_ui`](/reference/tools/ui/manage_ui) |
| `mcp-for-unity build` | Player builds across platforms | [`manage_build`](/reference/tools/core/manage_build) |
| `mcp-for-unity editor` | Editor state, play mode, undo/redo | [`manage_editor`](/reference/tools/core/manage_editor) |
| `mcp-for-unity packages` | UPM install/remove/embed | [`manage_packages`](/reference/tools/core/manage_packages) |
| `mcp-for-unity probuilder` | ProBuilder meshes | [`manage_probuilder`](/reference/tools/probuilder/manage_probuilder) |
| `mcp-for-unity profiler` | Profiler session + counters + snapshots | [`manage_profiler`](/reference/tools/profiling/manage_profiler) |
| `mcp-for-unity code` | Execute arbitrary C# in the Editor | [`execute_code`](/reference/tools/scripting_ext/execute_code) |
| `mcp-for-unity batch` | Run multiple operations atomically | [`batch_execute`](/reference/tools/core/batch_execute) |
| `mcp-for-unity tool` | Activate/deactivate tool groups | [`manage_tools`](/reference/tools/core/manage_tools) |
| `mcp-for-unity reflect` | Inspect Unity APIs via reflection | [`unity_reflect`](/reference/tools/docs/unity_reflect) |
| `mcp-for-unity docs` | Fetch Unity docs (ScriptReference, Manual) | [`unity_docs`](/reference/tools/docs/unity_docs) |

## Discovering subcommands and flags

Every group supports `--help`:

```bash
mcp-for-unity scene --help
mcp-for-unity scene load --help
```

The help text is the authoritative per-command reference — flags, choices, and defaults all live there because the CLI is built on Click and self-describes.

## Examples

See [CLI Examples](/guides/cli-examples) for end-to-end walkthroughs and the [CLI Usage Guide](/guides/cli) for narrative context (when to use the CLI vs an MCP client).

## Source

CLI command definitions: [`Server/src/cli/commands/`](https://github.com/CoplayDev/unity-mcp/tree/beta/Server/src/cli/commands). Entry point: [`Server/src/cli/main.py`](https://github.com/CoplayDev/unity-mcp/blob/beta/Server/src/cli/main.py).
