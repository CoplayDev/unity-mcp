---
title: Resource reference
sidebar_label: Resources
slug: /reference/resources
description: Auto-generated catalog of every MCP for Unity resource.
---

# Resource reference

> **Auto-generated** from the Python tool registry. Do not hand-edit outside `<!-- examples:start --><!-- examples:end -->` blocks — the generator (`tools/generate_docs_reference.py`) will overwrite them.

Resources are read-only state surfaces exposed to MCP clients. Tools mutate; resources observe.

## `cameras`

**URI:** `mcpforunity://scene/cameras`

List all cameras in the scene (Unity Camera + CinemachineCamera) with status. Includes Brain state, Cinemachine camera priorities, pipeline components, follow/lookAt targets, and Unity Camera info.

URI: mcpforunity://scene/cameras


## `custom_tools`

**URI:** `mcpforunity://custom-tools`

Lists custom tools available for the active Unity project.

URI: mcpforunity://custom-tools


## `editor_active_tool`

**URI:** `mcpforunity://editor/active-tool`

Currently active editor tool (Move, Rotate, Scale, etc.) and transform handle settings.

URI: mcpforunity://editor/active-tool


## `editor_prefab_stage`

**URI:** `mcpforunity://editor/prefab-stage`

Current prefab editing context if a prefab is open in isolation mode. Returns isOpen=false if no prefab is being edited.

URI: mcpforunity://editor/prefab-stage


## `editor_selection`

**URI:** `mcpforunity://editor/selection`

Detailed information about currently selected objects in the editor, including GameObjects, assets, and their properties.

URI: mcpforunity://editor/selection


## `editor_state`

**URI:** `mcpforunity://editor/state`

Canonical editor readiness snapshot. Includes advice and server-computed staleness.

URI: mcpforunity://editor/state


## `editor_windows`

**URI:** `mcpforunity://editor/windows`

All currently open editor windows with their titles, types, positions, and focus state.

URI: mcpforunity://editor/windows


## `gameobject`

**URI:** `mcpforunity://scene/gameobject/{instance_id}`

Get detailed information about a single GameObject by instance ID. Returns name, tag, layer, active state, transform data, parent/children IDs, and component type list (no full component properties).

URI: mcpforunity://scene/gameobject/{instance_id}

**Parameters:**

- `instance_id` (`str`, required) — 

## `gameobject_api`

**URI:** `mcpforunity://scene/gameobject-api`

Documentation for GameObject resources. Use find_gameobjects tool to get instance IDs, then access resources below.

URI: mcpforunity://scene/gameobject-api

**Parameters:**

- `_ctx` (`Context`, required) — 

## `gameobject_component`

**URI:** `mcpforunity://scene/gameobject/{instance_id}/component/{component_name}`

Get a specific component on a GameObject by type name. Returns the fully serialized component with all properties.

URI: mcpforunity://scene/gameobject/{instance_id}/component/{component_name}

**Parameters:**

- `instance_id` (`str`, required) — 
- `component_name` (`str`, required) — 

## `gameobject_components`

**URI:** `mcpforunity://scene/gameobject/{instance_id}/components`

Get all components on a GameObject with full property serialization. Supports pagination with pageSize and cursor parameters.

URI: mcpforunity://scene/gameobject/{instance_id}/components

**Parameters:**

- `instance_id` (`str`, required) — 
- `page_size` (`int`, optional) — 
- `cursor` (`int`, optional) — 
- `include_properties` (`bool`, optional) — 

## `get_tests`

**URI:** `mcpforunity://tests`

Provides the first page of Unity tests (default 50 items). For filtering or pagination, use the run_tests tool instead.

URI: mcpforunity://tests


## `get_tests_for_mode`

**URI:** `mcpforunity://tests/{mode}`

Provides the first page of tests for a specific mode (EditMode or PlayMode). For filtering or pagination, use the run_tests tool instead.

URI: mcpforunity://tests/{mode}

**Parameters:**

- `mode` (`Literal['EditMode', 'PlayMode']`, required) — 

## `menu_items`

**URI:** `mcpforunity://menu-items`

Provides a list of all menu items.

URI: mcpforunity://menu-items


## `prefab_api`

**URI:** `mcpforunity://prefab-api`

Documentation for Prefab resources. Use manage_asset action=search filterType=Prefab to find prefabs, then access resources below.

URI: mcpforunity://prefab-api

**Parameters:**

- `_ctx` (`Context`, required) — 

## `prefab_hierarchy`

**URI:** `mcpforunity://prefab/{encoded_path}/hierarchy`

Get the full hierarchy of a prefab with nested prefab information. Returns all GameObjects with their components and nesting depth.

URI: mcpforunity://prefab/{encoded_path}/hierarchy

**Parameters:**

- `encoded_path` (`str`, required) — 

## `prefab_info`

**URI:** `mcpforunity://prefab/{encoded_path}`

Get detailed information about a prefab asset by URL-encoded path. Returns prefab type, root object name, component types, child count, and variant info.

URI: mcpforunity://prefab/{encoded_path}

**Parameters:**

- `encoded_path` (`str`, required) — 

## `project_info`

**URI:** `mcpforunity://project/info`

Static project information including root path, Unity version, and platform. This data rarely changes.

URI: mcpforunity://project/info


## `project_layers`

**URI:** `mcpforunity://project/layers`

All layers defined in the project's TagManager with their indices (0-31). Read this before using add_layer or remove_layer tools.

URI: mcpforunity://project/layers


## `project_tags`

**URI:** `mcpforunity://project/tags`

All tags defined in the project's TagManager. Read this before using add_tag or remove_tag tools.

URI: mcpforunity://project/tags


## `renderer_features`

**URI:** `mcpforunity://pipeline/renderer-features`

Lists all URP renderer features on the active renderer with type, name, and active state.


## `rendering_stats`

**URI:** `mcpforunity://rendering/stats`

Snapshot of rendering performance statistics (draw calls, batches, triangles, frame time, etc.).


## `tool_groups`

**URI:** `mcpforunity://tool-groups`

Available tool groups and their tools. Use manage_tools to activate/deactivate groups per session.

URI: mcpforunity://tool-groups


## `unity_instances`

**URI:** `mcpforunity://instances`

Lists all running Unity Editor instances with their details.

URI: mcpforunity://instances


## `volumes`

**URI:** `mcpforunity://scene/volumes`

Lists all Volume components in the active scene with their profiles, effects, and settings.


