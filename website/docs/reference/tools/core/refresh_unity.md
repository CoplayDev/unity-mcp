---
title: refresh_unity
sidebar_label: refresh_unity
description: "Request a Unity asset database refresh and optionally a script compilation."
---

# `refresh_unity`

> **Auto-generated** from the Python tool registry. Do not hand-edit outside `<!-- examples:start --><!-- examples:end -->` blocks — the generator (`tools/generate_docs_reference.py`) will overwrite them.

**Group:** `core` &nbsp;·&nbsp; **Module:** `services.tools.refresh_unity`

## Description

Request a Unity asset database refresh and optionally a script compilation. Can optionally wait for readiness.

## Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `mode` | `Literal['if_dirty', 'force']` | — | Refresh mode |
| `scope` | `Literal['assets', 'scripts', 'all']` | — | Refresh scope |
| `compile` | `Literal['none', 'request']` | — | Whether to request compilation |
| `wait_for_ready` | `bool` | — | If true, wait until mcpforunity://editor/state reports data.advice.ready_for_tools true |
| `on_external_scene_change` | `Literal['auto', 'reload', 'keep_editor']` | — | When an open scene changed on disk: auto reloads only if nothing is lost, reload takes the disk version, keep_editor overwrites it |

## Returns

A `dict` containing the Unity response. The exact shape depends on the action.

## Examples

<!-- examples:start -->
*No examples yet. Add usage examples here — they will be preserved across regenerations.*
<!-- examples:end -->

