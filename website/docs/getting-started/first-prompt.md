---
id: first-prompt
slug: /getting-started/first-prompt
title: Your First Prompt
sidebar_label: Your First Prompt
description: End-to-end walkthrough — from typing a prompt to seeing the result in your Unity scene.
---

# Your First Prompt

You've installed the package and connected a client. Here's what to actually say.

## Prerequisites

- [Install](./install) is complete
- The MCP for Unity status panel reads `Connected`
- Your scene is open in the Unity Editor (any scene will do — even an empty one)

## The prompt

In your MCP client (Claude Desktop, Cursor, etc.), say:

> Create a red, blue, and yellow cube in the current scene, spaced one unit apart on the X axis.

The assistant should:

1. Call `manage_scene` (or `find_gameobjects`) to inspect the active scene
2. Call `manage_gameobject` three times to create cubes
3. Call `manage_material` to create or assign colored materials
4. Call `manage_components` to attach the material to each cube's MeshRenderer

Total round trip is usually 5–15 seconds depending on your network and the client.

## What you should see in Unity

Three cubes appear in the **Hierarchy** panel. Switch to the Scene view to see them laid out. If the materials are correct, they'll render red, blue, and yellow.

If the cubes appear but materials are missing (gray), your project may be using URP/HDRP — the LLM should detect this from `manage_graphics` but sometimes guesses Standard. Tell it explicitly: *"This project uses URP, please use the URP/Lit shader."*

## Stretching it

Try escalating prompts in the same session:

> Add a directional light if there isn't one, and a perspective camera positioned at (0, 2, -5) looking at the cubes.

> Write a C# script that makes the red cube oscillate up and down by 0.5 units, attach it to the red cube, and enter Play mode.

> Run all tests in EditMode and report which ones fail.

Each of these uses a different tool group — `core` for objects/scripts, `core` again for editor mode control, `testing` for test runs (you may need to activate the `testing` group first; see [Tool Groups](/guides/tool-groups)).

## When something goes wrong

- **"I couldn't find any Unity instance"** — the server isn't reachable. Check the status panel.
- **"Multiple Unity instances detected"** — you have more than one Editor open. See [Multi-Instance Routing](/guides/multi-instance).
- **Tool calls succeed but nothing happens in the scene** — your client may be in dry-run mode, or you might have hit an MCP visibility toggle for the relevant tool. Ask the assistant to call `manage_tools` action `list_groups`.

## What to read next

- [Choosing an MCP Client](./clients) — capability differences across clients
- [Tool Groups](/guides/tool-groups) — enabling vfx, animation, ui, testing, etc.
- [Tool reference](/reference/tools) — every available tool with parameters
