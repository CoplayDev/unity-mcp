---
title: manage_shader
sidebar_label: manage_shader
description: "Manages shader scripts in Unity (create, read, update, delete)."
---

# `manage_shader`

> **Auto-generated** from the Python tool registry. Do not hand-edit outside `<!-- examples:start --><!-- examples:end -->` blocks — the generator (`tools/generate_docs_reference.py`) will overwrite them.

**Group:** `vfx` &nbsp;·&nbsp; **Module:** `services.tools.manage_shader`

## Description

Manages shader scripts in Unity (create, read, update, delete). Read-only action: read. Modifying actions: create, update, delete.

## Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `action` | `Literal['create', 'read', 'update', 'delete']` | yes | Perform CRUD operations on shader scripts. |
| `name` | `str` | yes | Shader name (no .cs extension) |
| `path` | `str` | yes | Asset path (default: "Assets/") |
| `contents` | `str \| None` | — | Shader code for 'create'/'update' |

## Returns

A `dict` containing the Unity response. The exact shape depends on the action.

## Examples

<!-- examples:start -->
*No examples yet. Add usage examples here — they will be preserved across regenerations.*
<!-- examples:end -->

