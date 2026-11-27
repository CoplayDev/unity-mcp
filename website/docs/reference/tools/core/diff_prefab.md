---
title: diff_prefab
sidebar_label: diff_prefab
description: "Read-only comparison of one regular text-serialized Prefab under Assets/ against Git HEAD."
---

# `diff_prefab`

> **Auto-generated** from the Python tool registry. Do not hand-edit outside `<!-- examples:start --><!-- examples:end -->` blocks — the generator (`tools/generate_docs_reference.py`) will overwrite them.

**Group:** `core` &nbsp;·&nbsp; **Module:** `services.tools.diff_prefab`

## Description

Read-only comparison of one regular text-serialized Prefab under Assets/ against Git HEAD. Returns GameObject paths, component names and file IDs, property paths, and old/new values. Compares the saved worktree file, including unstaged edits. Requires Git and a connected Unity editor on the same machine, using stdio or loopback HTTP. Both versions must exist and be at most 2 MiB each. Results may be truncated by count or size. Nested Prefabs and Prefab Variants are unsupported.

## Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `prefab_path` | `str` | yes | Project-relative Prefab path, e.g. Assets/Prefabs/Player.prefab. |
| `max_changes` | `int` | — | Maximum changes to return (1–200). |

## Returns

A `dict` containing the Unity response. The exact shape depends on the action.

## Examples

<!-- examples:start -->
Call `diff_prefab` in a local Unity MCP session with the Prefab's project-relative path.

```json
{"prefab_path": "Assets/Prefabs/Player.prefab", "max_changes": 20}
```

Each entry in `data.changes` includes the GameObject path, component, serialized field path, and old and new values. File IDs distinguish same-name GameObjects and multiple components of the same type.
<!-- examples:end -->

