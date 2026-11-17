---
title: manage_editor_windows
sidebar_label: manage_editor_windows
description: "List open Unity Editor tabs/windows or capture one as an MCP PNG image."
---

# `manage_editor_windows`

> **Auto-generated** from the Python tool registry. Do not hand-edit outside `<!-- examples:start --><!-- examples:end -->` blocks — the generator (`tools/generate_docs_reference.py`) will overwrite them.

**Group:** `core` &nbsp;·&nbsp; **Module:** `services.tools.manage_editor_windows`

## Description

List open Unity Editor tabs/windows or capture one as an MCP PNG image. Use action=list to obtain window IDs; inactive docked tabs are included. Screenshots read Unity's own window buffer, including covered windows. Selecting an inactive tab temporarily changes focus; the previous tab is restored by default. No mouse/keyboard control is used. Captures may contain private Editor data. One image per capture is intended for the assistant; client display may vary. No file is saved unless save_file=true. Requires a graphical Editor; batch mode, native OS dialogs and minimized windows are unsupported.

## Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `action` | `Literal['list', 'screenshot']` | — | List open windows or capture one selected tab. |
| `window_id` | `int \| None` | — | Current window ID from action=list. Use one selector only. |
| `window_title` | `str \| None` | — | Exact title, ignoring case. Duplicate titles require window_id. |
| `window_type` | `str \| None` | — | Exact C# type name or full name. Duplicate types require window_id. |
| `focus` | `bool` | — | Select the target tab before capture. False requires an already selected tab. |
| `restore_focus` | `bool` | — | Restore the previous tab and keyboard focus after capture. |
| `include_image` | `bool` | — | Return a PNG image block and separate metadata. |
| `save_file` | `bool` | — | Save a unique full-size PNG in Library/McpEditorScreenshots. Default false. |
| `max_resolution` | `int` | — | Maximum edge for the inline image, from 64 to 4096. Does not resize saved files. |

## Returns

A `dict` containing the Unity response. The exact shape depends on the action.

## Examples

<!-- examples:start -->
*No examples yet. Add usage examples here — they will be preserved across regenerations.*
<!-- examples:end -->

