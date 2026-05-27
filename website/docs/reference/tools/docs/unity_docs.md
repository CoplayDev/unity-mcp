---
title: unity_docs
sidebar_label: unity_docs
description: "Fetch official Unity documentation from docs.unity3d.com."
---

# `unity_docs`

> **Auto-generated** from the Python tool registry. Do not hand-edit outside `<!-- examples:start --><!-- examples:end -->` blocks — the generator (`tools/generate_docs_reference.py`) will overwrite them.

**Group:** `docs` &nbsp;·&nbsp; **Module:** `services.tools.unity_docs`

## Description

Fetch official Unity documentation from docs.unity3d.com. Returns descriptions, parameter details, code examples, and caveats. Use after unity_reflect confirms a type exists, to get usage patterns, gotchas, and code examples before writing implementation code.

Actions:
- get_doc: Fetch ScriptReference docs for a class or member. Requires class_name. Optional member_name, version.
- get_manual: Fetch a Unity Manual page. Requires slug (e.g., 'execution-order', 'urp/urp-introduction'). Optional version.
- get_package_doc: Fetch package documentation. Requires package, page, pkg_version (e.g., package='com.unity.render-pipelines.universal', page='2d-index', pkg_version='17.0').
- lookup: Search all doc sources in parallel (ScriptReference + Manual + package docs). Requires query or queries (comma-separated). Supports batch: queries='Physics.Raycast,NavMeshAgent,Light2D' searches all in one call. Optional package + pkg_version to also search package docs.

## Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `action` | `str` | yes | The documentation action to perform. |
| `class_name` | `str \| None` | — | Unity class name (e.g. 'Physics', 'Transform'). |
| `member_name` | `str \| None` | — | Method or property name to look up. |
| `version` | `str \| None` | — | Unity version (e.g. '6000.0.38f1'). Auto-extracted. |
| `slug` | `str \| None` | — | Manual page slug (e.g., 'execution-order'). |
| `package` | `str \| None` | — | Package name (e.g., 'com.unity.render-pipelines.universal'). |
| `page` | `str \| None` | — | Package doc page (e.g., 'index', '2d-index'). |
| `pkg_version` | `str \| None` | — | Package version major.minor (e.g., '17.0'). |
| `query` | `str \| None` | — | Single search query for lookup (class name, topic, or slug). |
| `queries` | `str \| None` | — | Comma-separated search queries for batch lookup (e.g., 'Physics.Raycast,NavMeshAgent,Light2D'). |

## Returns

A `dict` containing the Unity response. The exact shape depends on the action.

## Examples

<!-- examples:start -->
*No examples yet. Add usage examples here — they will be preserved across regenerations.*
<!-- examples:end -->

