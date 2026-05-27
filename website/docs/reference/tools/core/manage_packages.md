---
title: manage_packages
sidebar_label: manage_packages
description: "Manage Unity packages: query, install, remove, embed, and configure registries."
---

# `manage_packages`

> **Auto-generated** from the Python tool registry. Do not hand-edit outside `<!-- examples:start --><!-- examples:end -->` blocks — the generator (`tools/generate_docs_reference.py`) will overwrite them.

**Group:** `core` &nbsp;·&nbsp; **Module:** `services.tools.manage_packages`

## Description

Manage Unity packages: query, install, remove, embed, and configure registries.

QUERY (read-only):
- list_packages: List all installed packages
- search_packages: Search Unity registry by keyword
- get_package_info: Get details about a specific installed package
- ping: Check package manager availability
- status: Poll async job status (job_id required for list/search; optional for add/remove/embed)

INSTALL/REMOVE:
- add_package: Install a package (name, name@version, git URL, or file: path)
- remove_package: Remove a package (checks dependents; use force=true to override)

REGISTRIES:
- list_registries: List all scoped registries
- add_registry: Add a scoped registry (e.g., OpenUPM)
- remove_registry: Remove a scoped registry

UTILITY:
- embed_package: Copy package to local Packages/ for editing
- resolve_packages: Force re-resolution of all packages

## Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `action` | `str` | yes | The package action to perform. |
| `package` | `str \| None` | — | Package identifier (name, name@version, git URL, or file: path). |
| `force` | `bool \| None` | — | Force removal even if other packages depend on it. |
| `query` | `str \| None` | — | Search query for search_packages. |
| `job_id` | `str \| None` | — | Job ID for polling status. |
| `name` | `str \| None` | — | Registry name for add_registry/remove_registry. |
| `url` | `str \| None` | — | Registry URL for add_registry. |
| `scopes` | `list[str] \| None` | — | Registry scopes for add_registry. |

## Returns

A `dict` containing the Unity response. The exact shape depends on the action.

## Examples

<!-- examples:start -->
*No examples yet. Add usage examples here — they will be preserved across regenerations.*
<!-- examples:end -->

