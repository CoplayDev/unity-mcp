---
title: read_console
sidebar_label: read_console
description: "Gets messages from or clears the Unity Editor console."
---

# `read_console`

> **Auto-generated** from the Python tool registry. Do not hand-edit outside `<!-- examples:start --><!-- examples:end -->` blocks — the generator (`tools/generate_docs_reference.py`) will overwrite them.

**Group:** `core` &nbsp;·&nbsp; **Module:** `services.tools.read_console`

## Description

Gets messages from or clears the Unity Editor console. Defaults to 10 most recent entries. Use page_size/cursor for paging. Note: For maximum client compatibility, pass count as a quoted string (e.g., '5'). The 'get' action is read-only; 'clear' modifies ephemeral UI state (not project data).

## Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `action` | `Literal['get', 'clear'] \| None` | — | Get or clear the Unity Editor console. Defaults to 'get' if omitted. |
| `types` | `list[Literal['error', 'warning', 'log', 'all']] \| str \| None` | — | Message types to get (accepts list or JSON string) |
| `count` | `int \| str \| None` | — | Max messages to return in non-paging mode (accepts int or string, e.g., 5 or '5'). Ignored when paging with page_size/cursor. |
| `filter_text` | `str \| None` | — | Text filter for messages |
| `page_size` | `int \| str \| None` | — | Page size for paginated console reads. Defaults to 50 when omitted. |
| `cursor` | `int \| str \| None` | — | Opaque cursor for paging (0-based offset). Defaults to 0. |
| `format` | `Literal['plain', 'detailed', 'json'] \| None` | — | Output format |
| `include_stacktrace` | `bool \| str \| None` | — | Include stack traces in output (accepts true/false or 'true'/'false') |

## Returns

A `dict` containing the Unity response. The exact shape depends on the action.

## Examples

<!-- examples:start -->
*No examples yet. Add usage examples here — they will be preserved across regenerations.*
<!-- examples:end -->

