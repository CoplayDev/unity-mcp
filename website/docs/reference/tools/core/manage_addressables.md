---
title: manage_addressables
sidebar_label: manage_addressables
description: "Query and edit optional Unity Addressables configuration. ping reports the installed package version, API availability and settings existence. get_settings reads basic settings; group_list, entry_list, label_list, profile_list and profil…"
---

# `manage_addressables`

> **Auto-generated** from the Python tool registry. Do not hand-edit outside `<!-- examples:start --><!-- examples:end -->` blocks — the generator (`tools/generate_docs_reference.py`) will overwrite them.

**Group:** `core` &nbsp;·&nbsp; **Module:** `services.tools.manage_addressables`

## Description

Query and edit optional Unity Addressables configuration. ping reports the installed package version, API availability and settings existence. get_settings reads basic settings; group_list, entry_list, label_list, profile_list and profile_get return bounded pages (page_size/cursor). entry_find looks up an explicit entry by exactly one of guid/path, not implicit children of addressable folders. entry_list optionally filters by group GUID or exact name. profile_get returns raw variable values for profile_id (defaults to the active profile). Reuse next_cursor; restart without cursor if configuration changes. group_create adds bundled/content-update schemas; group_remove needs force for non-empty groups. entry_add registers an asset in group; entry_move changes group; entry_set_address changes address; entry_remove cancels registration without deleting the source asset. label_add/remove manage definitions by name; removal needs force while in use. label_set replaces an entry's labels with existing definitions ([] clears). profile_set changes an existing variable by name/value/profile_id; profile_set_active selects a profile. set_settings accepts a settings object with build_remote_catalog, unique_bundle_ids, contiguous_bundles, max_concurrent_web_requests (1-1024), catalog_requests_timeout (0-3600), remote_catalog_build_path and remote_catalog_load_path (existing profile variable names); initialize=true explicitly creates settings. get_settings returns catalog path variable IDs. build_content, build_update and build_clean queue a content operation for the active platform/builder. Poll build_status with job_id; only the latest job is retained. build_update requires content_state_path from the matching previous build and refuses changed static-content entries until regrouped. Native builds block Unity's main thread and cannot be cancelled; a client timeout does not cancel a build.

## Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `action` | `Literal['ping', 'get_settings', 'group_list', 'entry_list', 'entry_find', 'label_list', 'profile_list', 'profile_get', 'group_create', 'group_remove', 'entry_add', 'entry_remove', 'entry_move', 'entry_set_address', 'label_add', 'label_remove', 'label_set', 'profile_set', 'profile_set_active', 'set_settings', 'build_content', 'build_update', 'build_clean', 'build_status']` | yes | Addressables action to perform. |
| `group` | `str \| None` | — | Group GUID or exact name for listing, registration, move, or removal. |
| `guid` | `str \| None` | — | Asset GUID for an entry action; mutually exclusive with path. |
| `path` | `str \| None` | — | Project asset path for an entry action; mutually exclusive with guid. |
| `profile_id` | `str \| None` | — | Profile ID; profile_get defaults to active profile, writes require an explicit ID. |
| `page_size` | `int \| None` | — | Items per page, 1-200 (default 50). |
| `cursor` | `str \| None` | — | Opaque next_cursor from the preceding page. |
| `name` | `str \| None` | — | Group name, label name, or profile variable name, depending on action. |
| `address` | `str \| None` | — | New address for entry_set_address. |
| `labels` | `list[str] \| None` | — | Replacement labels for label_set; definitions must already exist. |
| `value` | `str \| None` | — | Raw string value for profile_set (empty is allowed). |
| `force` | `bool \| None` | — | Allow removal of a non-empty group or an in-use label. |
| `settings` | `dict[str, Any] \| None` | — | Whitelisted settings updates for set_settings. |
| `initialize` | `bool \| None` | — | set_settings only: explicitly create default settings if absent. |
| `content_state_path` | `str \| None` | — | Existing matching previous-build .bin file for build_update. |
| `job_id` | `str \| None` | — | Latest content-operation job ID for build_status. |
| `static_content` | `bool \| None` | — | group_create: prevent content changes after release; omit to keep Unity's default. |

## Returns

A `dict` containing the Unity response. The exact shape depends on the action.

## Examples

<!-- examples:start -->
Addressables is optional. Install a supported `com.unity.addressables` package in
the Unity project and create its settings explicitly before reading configuration.
`ping` also works without the package or settings. These actions do not create
groups, register assets or trigger builds.

```json
{"action": "ping"}
{"action": "group_list", "page_size": 20}
{"action": "entry_list", "group": "Characters", "page_size": 20}
{"action": "entry_find", "path": "Assets/Characters/Hero.prefab"}
{"action": "profile_get"}
```

Paged actions return `data.items`, `data.total` and `data.next_cursor`. Copy a
non-null cursor into the next request with the same action and filter. Groups
and entries are ordered by GUID, labels and profile variables by ordinal name,
and profiles by ID. A changed result set invalidates its cursor; restart the
query without one. Pages default to 50 items, with a maximum of 200.

`entry_list` and `entry_find` report explicit registrations. Registering a folder
does not make every child a separate explicit entry. A valid lookup that finds
no registration returns `data.found: false`. Group filters accept a GUID or an
exact name; use the GUID if names are ambiguous. Profile values are raw strings,
including tokens such as `[BuildTarget]`, rather than evaluated paths.

Failures include `ADDRESSABLES_NOT_INSTALLED`, `ADDRESSABLES_SETTINGS_NOT_FOUND`,
`ADDRESSABLES_MIGRATION_REQUIRED`, `ADDRESSABLES_API_UNSUPPORTED`,
`ADDRESSABLES_PROFILE_NOT_FOUND` and `INVALID_ARGUMENT`. Legacy settings are not
automatically migrated by a read; open Addressables in Unity to migrate them.
Queries also leave empty group identifiers and active-profile values unchanged.
`profile_get` without a valid active profile returns `ADDRESSABLES_PROFILE_NOT_FOUND`
instead of creating a default profile; pass an existing `profile_id` explicitly.

The `mcpforunity://addressables/settings` resource exposes the same basic
settings. `mcpforunity://addressables/groups` returns the first 50 groups; use
the tool with its returned cursor for subsequent pages.

Verified query fixtures: Unity 2022.3.62f3c1 with Addressables 1.22.3 and Unity
6000.0.47f1 with Addressables 2.10.3. This does not establish support for every
intermediate package version or for Tuanjie.
<!-- examples:end -->

