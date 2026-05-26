---
title: create_script
sidebar_label: create_script
description: "Create a new C# script at the given project path."
---

# `create_script`

> **Auto-generated** from the Python tool registry. Do not hand-edit outside `<!-- examples:start --><!-- examples:end -->` blocks — the generator (`tools/generate_docs_reference.py`) will overwrite them.

**Group:** `core` &nbsp;·&nbsp; **Module:** `services.tools.manage_script`

## Description

Create a new C# script at the given project path.

## Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `path` | `str` | yes | Path under Assets/ to create the script at, e.g., 'Assets/Scripts/My.cs' |
| `contents` | `str` | yes | Contents of the script to create (plain text C# code). The server handles Base64 encoding. |
| `script_type` | `str \| None` | — | Script type (e.g., 'C#') |
| `namespace` | `str \| None` | — | Namespace for the script |

## Returns

A `dict` containing the Unity response. The exact shape depends on the action.

## Examples

<!-- examples:start -->
*No examples yet. Add usage examples here — they will be preserved across regenerations.*
<!-- examples:end -->

