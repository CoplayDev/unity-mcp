---
title: unity_reflect
sidebar_label: unity_reflect
description: "Inspect Unity's live C# API via reflection."
---

# `unity_reflect`

> **Auto-generated** from the Python tool registry. Do not hand-edit outside `<!-- examples:start --><!-- examples:end -->` blocks — the generator (`tools/generate_docs_reference.py`) will overwrite them.

**Group:** `docs` &nbsp;·&nbsp; **Module:** `services.tools.unity_reflect`

## Description

Inspect Unity's live C# API via reflection. Use this to verify that classes, methods, and properties exist before writing C# code — training data may be wrong or outdated.

Actions:
- get_type: Member summary (names only) for a class. Requires class_name.
- get_member: Full signature detail for one member. Requires class_name + member_name.
- search: Type name search across loaded assemblies. Requires query. Optional scope.

## Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `action` | `str` | yes | The reflection action to perform. |
| `class_name` | `str \| None` | — | Fully qualified or simple C# class name. |
| `member_name` | `str \| None` | — | Method, property, or field name to inspect. |
| `query` | `str \| None` | — | Search query for type name search. |
| `scope` | `str \| None` | — | Assembly scope for search: unity, packages, project, all. |

## Returns

A `dict` containing the Unity response. The exact shape depends on the action.

## Examples

<!-- examples:start -->
*No examples yet. Add usage examples here — they will be preserved across regenerations.*
<!-- examples:end -->

