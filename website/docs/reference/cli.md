---
id: cli
slug: /reference/cli
title: CLI Reference
sidebar_label: CLI
description: The unity-mcp command-line interface — invocation, global flags, command groups, and the MCP tools each group calls.
---

# CLI Reference

The `unity-mcp` CLI is a developer-facing terminal for the same Unity automations the MCP tools expose. Both invoke the same C# `HandleCommand` methods on the Unity side — see [Three-Layer Python Design](/architecture/python-layers) for why both layers exist.

## Invocation

```bash
# Run via uvx (no install)
uvx --from mcpforunityserver unity-mcp <command> [args]

# Run from a Server checkout
cd Server && uv run unity-mcp <command> [args]
```

The same package also installs `mcp-for-unity`, which starts the MCP server, not the CLI.

## How it talks to Unity

The CLI uses **HTTP** to the Python server (default `http://127.0.0.1:8080`), regardless of how your MCP clients are configured. The Python server in turn talks to the connected Unity Editor via WebSocket. MCP tools take a similar path via WebSocket directly; CLI commands take HTTP.

## Global flags

Global flags go before the command group: `unity-mcp --format json scene active`.

| Flag | Env variable | Default | Meaning |
|---|---|---|---|
| `--host`, `-h` | `UNITY_MCP_HOST` | `127.0.0.1` | Python server host to connect to |
| `--port`, `-p` | `UNITY_MCP_HTTP_PORT` | `8080` | Python server port |
| `--timeout`, `-t` | `UNITY_MCP_TIMEOUT` | `30` | Command timeout in seconds |
| `--format`, `-f` | `UNITY_MCP_FORMAT` | `text` | Output format: `text`, `json` or `table` |
| `--instance`, `-i` | `UNITY_MCP_INSTANCE` | (auto) | Target Unity instance (hash or `Name@hash`) |
| `--verbose`, `-v` | — | off | Print each command sent to Unity and its raw response to stderr |
| `--version` | — | — | Print CLI version and exit |
| `--help` | — | — | Show command help (`-h` is `--host`, not help) |

For multi-instance setups, see [Multi-Instance Routing](/guides/multi-instance).

## Command groups

The CLI mirrors the MCP tool catalog. Each command sends one or more MCP tool calls to Unity; the last column names them. Commands marked — only talk to the Python server.

| Command | What it does | MCP tool(s) it calls |
|---|---|---|
| `unity-mcp status` | Check the server connection and list Unity instances | — (server `/health`, `/api/instances`) |
| `unity-mcp instances` | List connected Unity instances | — (server `/api/instances`) |
| `unity-mcp raw` | Send any tool by name with JSON params | The named tool |
| `unity-mcp instance` | List instances, show the one this shell targets (`--instance` / `UNITY_MCP_INSTANCE`) | — (server `/api/instances`) |
| `unity-mcp scene` | Load/save/query/edit scenes | [`manage_scene`](/reference/tools/core/manage_scene) |
| `unity-mcp gameobject` | Find/create/modify/move/duplicate/delete GameObjects | [`manage_gameobject`](/reference/tools/core/manage_gameobject), [`find_gameobjects`](/reference/tools/core/find_gameobjects) (`find`), [`manage_components`](/reference/tools/core/manage_components) (`create --components`) |
| `unity-mcp component` | Add/remove/configure components | [`manage_components`](/reference/tools/core/manage_components) |
| `unity-mcp script` | Create/read/edit/validate/delete C# scripts | [`manage_script`](/reference/tools/core/manage_script) |
| `unity-mcp asset` | Asset import/create/modify/search | [`manage_asset`](/reference/tools/core/manage_asset) |
| `unity-mcp asset-gen` | Generate images, 3D models and audio; import models | [`generate_image`](/reference/tools/asset_gen/generate_image), [`generate_model`](/reference/tools/asset_gen/generate_model), [`generate_audio`](/reference/tools/asset_gen/generate_audio), [`import_model`](/reference/tools/asset_gen/import_model), [`import_model_file`](/reference/tools/asset_gen/import_model_file) |
| `unity-mcp blender` | Talk to a running Blender through the Blender Bridge | [`blender_bridge`](/reference/tools/asset_gen/blender_bridge) |
| `unity-mcp material` | Material CRUD + shader props | [`manage_material`](/reference/tools/core/manage_material) |
| `unity-mcp prefab` | Prefab create/open/save/close, inspect, headless modify | [`manage_prefabs`](/reference/tools/core/manage_prefabs) |
| `unity-mcp texture` | Procedural or image textures, sprites, pixel edits | [`manage_texture`](/reference/tools/vfx/manage_texture) |
| `unity-mcp shader` | Shader CRUD | [`manage_shader`](/reference/tools/vfx/manage_shader) |
| `unity-mcp vfx` | Particle systems, line and trail renderers; `raw` for any `manage_vfx` action | [`manage_vfx`](/reference/tools/vfx/manage_vfx) |
| `unity-mcp camera` | Camera + Cinemachine presets, screenshots | [`manage_camera`](/reference/tools/core/manage_camera) |
| `unity-mcp graphics` | Volumes, render pipeline, light baking, URP features, skybox | [`manage_graphics`](/reference/tools/core/manage_graphics) |
| `unity-mcp lighting` | Create a light GameObject | [`manage_gameobject`](/reference/tools/core/manage_gameobject) + [`manage_components`](/reference/tools/core/manage_components) |
| `unity-mcp physics` | 3D + 2D physics, joints, queries | [`manage_physics`](/reference/tools/core/manage_physics) |
| `unity-mcp audio` | Play, stop or set the volume of an AudioSource | [`manage_components`](/reference/tools/core/manage_components) |
| `unity-mcp animation` | Animator, AnimationClip and AnimatorController | [`manage_animation`](/reference/tools/animation/manage_animation) |
| `unity-mcp sprite` | Sprite sheet slicing → clips → Animator controller | [`manage_sprite`](/reference/tools/animation/manage_sprite) |
| `unity-mcp ui` | Create uGUI Canvas, text, button and image GameObjects | [`manage_gameobject`](/reference/tools/core/manage_gameobject) + [`manage_components`](/reference/tools/core/manage_components) |
| `unity-mcp build` | Player builds across platforms | [`manage_build`](/reference/tools/core/manage_build) |
| `unity-mcp editor` | Play mode, tags/layers, undo/redo, console, refresh, menu items, tests, custom tools | [`manage_editor`](/reference/tools/core/manage_editor), [`read_console`](/reference/tools/core/read_console) (`console`), [`refresh_unity`](/reference/tools/core/refresh_unity) (`refresh`), [`execute_menu_item`](/reference/tools/core/execute_menu_item) (`menu`), [`run_tests`](/reference/tools/testing/run_tests) (`tests`), [`get_test_job`](/reference/tools/testing/get_test_job) (`poll-test`), [`execute_custom_tool`](/reference/tools/core/execute_custom_tool) (`custom-tool`) |
| `unity-mcp packages` | UPM install/remove/embed | [`manage_packages`](/reference/tools/core/manage_packages) |
| `unity-mcp probuilder` | ProBuilder meshes | [`manage_probuilder`](/reference/tools/probuilder/manage_probuilder) |
| `unity-mcp profiler` | Profiler session + counters + snapshots | [`manage_profiler`](/reference/tools/profiling/manage_profiler) |
| `unity-mcp code` | Execute C# in the Editor; read and search script files | [`execute_code`](/reference/tools/scripting_ext/execute_code), [`manage_script`](/reference/tools/core/manage_script) (`read`, `search`) |
| `unity-mcp batch` | Run many commands in one request | [`batch_execute`](/reference/tools/core/batch_execute) |
| `unity-mcp tool` | List the custom tools registered for the active Unity project | — (server `/api/custom-tools`) |
| `unity-mcp custom_tool` | Same as `tool` | — (server `/api/custom-tools`) |
| `unity-mcp reflect` | Inspect Unity APIs via reflection | [`unity_reflect`](/reference/tools/docs/unity_reflect) |
| `unity-mcp docs` | Fetch a Unity ScriptReference page | [`unity_docs`](/reference/tools/docs/unity_docs) (runs in the CLI process) |

## Discovering subcommands and flags

Every group supports `--help`:

```bash
unity-mcp scene --help
unity-mcp scene load --help
```

The help text is the authoritative per-command reference — flags, choices, and defaults all live there because the CLI is built on Click and self-describes.

## Examples

See [CLI Examples](/guides/cli-examples) for end-to-end walkthroughs and the [CLI Usage Guide](/guides/cli) for narrative context (when to use the CLI vs an MCP client).

## Source

CLI command definitions: [`Server/src/cli/commands/`](https://github.com/CoplayDev/unity-mcp/tree/beta/Server/src/cli/commands). Entry point: [`Server/src/cli/main.py`](https://github.com/CoplayDev/unity-mcp/blob/beta/Server/src/cli/main.py).
