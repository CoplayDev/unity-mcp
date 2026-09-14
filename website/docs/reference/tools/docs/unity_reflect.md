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
### Check a class before writing code against it

> Does `Rigidbody` have `linearVelocity` in this Unity version?

```json
{
  "action": "get_type",
  "class_name": "Rigidbody"
}
```

Returns type metadata (namespace, assembly, base class, interfaces) plus the names of its methods, properties, fields and events, its extension methods and any obsolete members — no signatures. A cheap way to confirm an API exists in the editor that is actually open, instead of trusting what the model remembers.

### Get the exact signature of one member

> Show every overload of `Physics.Raycast`.

```json
{
  "action": "get_member",
  "class_name": "Physics",
  "member_name": "Raycast"
}
```

Methods come back with `overload_count` and one entry per overload. If the name is not a method, property or field of the type, extension methods are tried last.

### Resolve an ambiguous short name

> Which `Button` types are loaded?

```json
{
  "action": "get_type",
  "class_name": "Button"
}
```

When several loaded types share the short name, the response has `ambiguous: true` and a `matches` list of full names. Call again with one of them, e.g. `"class_name": "UnityEngine.UI.Button"`.

### Find a type by partial name

> Find my project's inventory classes.

```json
{
  "action": "search",
  "query": "Inventory",
  "scope": "project"
}
```

`scope` defaults to `unity` (UnityEngine / UnityEditor / Unity.* assemblies). `project` covers only the `Assembly-CSharp*` assemblies, so types in your own `.asmdef` assemblies need `packages` or `all`.
<!-- examples:end -->

