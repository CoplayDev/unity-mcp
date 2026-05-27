---
title: apply_text_edits
sidebar_label: apply_text_edits
description: "Apply small text edits to a C# script identified by URI."
---

# `apply_text_edits`

> **Auto-generated** from the Python tool registry. Do not hand-edit outside `<!-- examples:start --><!-- examples:end -->` blocks — the generator (`tools/generate_docs_reference.py`) will overwrite them.

**Group:** `core` &nbsp;·&nbsp; **Module:** `services.tools.manage_script`

## Description

Apply small text edits to a C# script identified by URI.
    IMPORTANT: This tool replaces EXACT character positions. Always verify content at target lines/columns BEFORE editing!
    RECOMMENDED WORKFLOW:
        1. First call resources/read with start_line/line_count to verify exact content
        2. Count columns carefully (or use find_in_file to locate patterns)
        3. Apply your edit with precise coordinates
        4. Consider script_apply_edits with anchors for safer pattern-based replacements
    Notes:
        - For method/class operations, use script_apply_edits (safer, structured edits)
        - For pattern-based replacements, consider anchor operations in script_apply_edits
        - Lines, columns are 1-indexed
        - Tabs count as 1 column

## Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `uri` | `str` | yes | URI of the script to edit under Assets/ directory, mcpforunity://path/Assets/... or file://... or Assets/... |
| `edits` | `list[dict[str, Any]]` | yes | List of edits to apply to the script, i.e. a list of {startLine,startCol,endLine,endCol,newText} (1-indexed!) |
| `precondition_sha256` | `str \| None` | — | Optional SHA256 of the script to edit, used to prevent concurrent edits |
| `strict` | `bool \| None` | — | Optional strict flag, used to enforce strict mode |
| `options` | `dict[str, Any] \| None` | — | Optional options, used to pass additional options to the script editor |

## Returns

A `dict` containing the Unity response. The exact shape depends on the action.

## Examples

<!-- examples:start -->
*No examples yet. Add usage examples here — they will be preserved across regenerations.*
<!-- examples:end -->

