# Unity-MCP Resources Reference

Resources provide read-only access to Unity state. Use resources to inspect before using tools to modify.

## Table of Contents

- [Editor State Resources](#editor-state-resources)
- [Scene & GameObject Resources](#scene--gameobject-resources)
- [Prefab Resources](#prefab-resources)
- [Project Resources](#project-resources)
- [Instance Resources](#instance-resources)
- [Test Resources](#test-resources)

---

## URI Scheme

All resources use `mcpforunity://` scheme:

```
mcpforunity://{category}/{resource_path}[?query_params]
```

**Categories:** `editor`, `scene`, `prefab`, `project`, `menu-items`, `custom-tools`, `tests`, `instances`

---

## Editor State Resources

### mcpforunity://editor/state

**Purpose:** Editor readiness snapshot - check before tool operations.

**Returns:**
```json
{
  "unity_version": "2022.3.10f1",
  "is_compiling": false,
  "is_domain_reload_pending": false,
  "play_mode": {
    "is_playing": false,
    "is_paused": false
  },
  "active_scene": {
    "path": "Assets/Scenes/Main.unity",
    "name": "Main"
  },
  "ready_for_tools": true,
  "blocking_reasons": [],
  "recommended_retry_after_ms": null,
  "staleness": {
    "age_ms": 150,
    "is_stale": false
  }
}
```

**Key Fields:**
- `ready_for_tools`: Only proceed if `true`
- `is_compiling`: Wait if `true`
- `blocking_reasons`: Array explaining why tools might fail
- `recommended_retry_after_ms`: Suggested wait time

### mcpforunity://editor/selection

**Purpose:** Currently selected objects.

**Returns:**
```json
{
  "activeObject": "Player",
  "activeGameObject": "Player",
  "activeInstanceID": 12345,
  "count": 3,
  "gameObjects": ["Player", "Enemy", "Wall"],
  "assetGUIDs": []
}
```

### mcpforunity://editor/active-tool

**Purpose:** Current editor tool state.

**Returns:**
```json
{
  "activeTool": "Move",
  "isCustom": false,
  "pivotMode": "Center",
  "pivotRotation": "Global"
}
```

### mcpforunity://editor/windows

**Purpose:** All open editor windows.

**Returns:**
```json
{
  "windows": [
    {
      "title": "Scene",
      "typeName": "UnityEditor.SceneView",
      "isFocused": true,
      "position": {"x": 0, "y": 0, "width": 800, "height": 600}
    }
  ]
}
```

### mcpforunity://editor/prefab-stage

**Purpose:** Current prefab editing context.

**Returns:**
```json
{
  "isOpen": true,
  "assetPath": "Assets/Prefabs/Player.prefab",
  "prefabRootName": "Player",
  "isDirty": false
}
```

---

## Scene & GameObject Resources

### mcpforunity://scene/gameobject-api

**Purpose:** Documentation for GameObject resources (read this first).

### mcpforunity://scene/gameobject/{instance_id}

**Purpose:** Basic GameObject data (metadata, no component properties).

**Parameters:**
- `instance_id` (int): GameObject instance ID from `find_gameobjects`

**Returns:**
```json
{
  "instanceID": 12345,
  "name": "Player",
  "tag": "Player",
  "layer": 8,
  "layerName": "Player",
  "active": true,
  "activeInHierarchy": true,
  "isStatic": false,
  "transform": {
    "position": [0, 1, 0],
    "rotation": [0, 0, 0],
    "scale": [1, 1, 1]
  },
  "parent": {"instanceID": 0},
  "children": [{"instanceID": 67890}],
  "componentTypes": ["Transform", "Rigidbody", "PlayerController"],
  "path": "/Player"
}
```

### mcpforunity://scene/gameobject/{instance_id}/components

**Purpose:** All components with full property serialization (paginated).

**Parameters:**
- `instance_id` (int): GameObject instance ID
- `page_size` (int): Default 25, max 100
- `cursor` (int): Pagination cursor
- `include_properties` (bool): Default true, set false for just types

**Returns:**
```json
{
  "gameObjectID": 12345,
  "gameObjectName": "Player",
  "components": [
    {
      "type": "Transform",
      "properties": {
        "position": {"x": 0, "y": 1, "z": 0},
        "rotation": {"x": 0, "y": 0, "z": 0, "w": 1}
      }
    },
    {
      "type": "Rigidbody",
      "properties": {
        "mass": 1.0,
        "useGravity": true
      }
    }
  ],
  "cursor": 0,
  "pageSize": 25,
  "nextCursor": null,
  "hasMore": false
}
```

### mcpforunity://scene/gameobject/{instance_id}/component/{component_name}

**Purpose:** Single component with full properties.

**Parameters:**
- `instance_id` (int): GameObject instance ID
- `component_name` (string): e.g., "Rigidbody", "Camera", "Transform"

**Returns:**
```json
{
  "gameObjectID": 12345,
  "gameObjectName": "Player",
  "component": {
    "type": "Rigidbody",
    "properties": {
      "mass": 1.0,
      "drag": 0,
      "angularDrag": 0.05,
      "useGravity": true,
      "isKinematic": false
    }
  }
}
```

---

## Prefab Resources

### mcpforunity://prefab-api

**Purpose:** Documentation for prefab resources.

### mcpforunity://prefab/{encoded_path}

**Purpose:** Prefab asset information.

**Parameters:**
- `encoded_path` (string): URL-encoded path, e.g., `Assets%2FPrefabs%2FPlayer.prefab`

**Path Encoding:**
```
Assets/Prefabs/Player.prefab → Assets%2FPrefabs%2FPlayer.prefab
```

**Returns:**
```json
{
  "assetPath": "Assets/Prefabs/Player.prefab",
  "guid": "abc123...",
  "prefabType": "Regular",
  "rootObjectName": "Player",
  "rootComponentTypes": ["Transform", "PlayerController"],
  "childCount": 5,
  "isVariant": false,
  "parentPrefab": null
}
```

### mcpforunity://prefab/{encoded_path}/hierarchy

**Purpose:** Full prefab hierarchy with nested prefab info.

**Returns:**
```json
{
  "prefabPath": "Assets/Prefabs/Player.prefab",
  "total": 6,
  "items": [
    {
      "name": "Player",
      "instanceId": 12345,
      "path": "/Player",
      "activeSelf": true,
      "childCount": 2,
      "componentTypes": ["Transform", "PlayerController"]
    },
    {
      "name": "Model",
      "path": "/Player/Model",
      "isNestedPrefab": true,
      "nestedPrefabPath": "Assets/Prefabs/PlayerModel.prefab"
    }
  ]
}
```

---

## Project Resources

### mcpforunity://project/info

**Purpose:** Static project configuration.

**Returns:**
```json
{
  "projectRoot": "/Users/dev/MyProject",
  "projectName": "MyProject",
  "unityVersion": "2022.3.10f1",
  "platform": "StandaloneWindows64",
  "assetsPath": "/Users/dev/MyProject/Assets"
}
```

### mcpforunity://project/tags

**Purpose:** All tags defined in TagManager.

**Returns:**
```json
["Untagged", "Respawn", "Finish", "EditorOnly", "MainCamera", "Player", "GameController", "Enemy"]
```

### mcpforunity://project/layers

**Purpose:** All layers with indices (0-31).

**Returns:**
```json
{
  "0": "Default",
  "1": "TransparentFX",
  "2": "Ignore Raycast",
  "4": "Water",
  "5": "UI",
  "8": "Player",
  "9": "Enemy"
}
```

### mcpforunity://menu-items

**Purpose:** All available Unity menu items.

**Returns:**
```json
[
  "File/New Scene",
  "File/Open Scene",
  "File/Save",
  "Edit/Undo",
  "Edit/Redo",
  "GameObject/Create Empty",
  "GameObject/3D Object/Cube",
  "Window/General/Console"
]
```

### mcpforunity://custom-tools

**Purpose:** Custom tools available in the active Unity project.

**Returns:**
```json
{
  "project_id": "MyProject",
  "tool_count": 3,
  "tools": [
    {
      "name": "capture_screenshot",
      "description": "Capture screenshots in Unity",
      "parameters": [
        {"name": "filename", "type": "string", "required": true},
        {"name": "width", "type": "int", "required": false},
        {"name": "height", "type": "int", "required": false}
      ]
    }
  ]
}
```

---

## Instance Resources

### mcpforunity://instances

**Purpose:** All running Unity Editor instances (for multi-instance workflows).

**Returns:**
```json
{
  "transport": "http",
  "instance_count": 2,
  "instances": [
    {
      "id": "MyProject@abc123",
      "name": "MyProject",
      "hash": "abc123",
      "unity_version": "2022.3.10f1",
      "connected_at": "2024-01-15T10:30:00Z"
    },
    {
      "id": "TestProject@def456",
      "name": "TestProject",
      "hash": "def456",
      "unity_version": "2022.3.10f1",
      "connected_at": "2024-01-15T11:00:00Z"
    }
  ],
  "warnings": []
}
```

**Use with:** `set_active_instance(instance="MyProject@abc123")`

---

## Test Resources

### mcpforunity://tests

**Purpose:** All tests in the project.

**Returns:**
```json
[
  {
    "name": "TestSomething",
    "full_name": "MyTests.TestSomething",
    "mode": "EditMode"
  },
  {
    "name": "TestOther",
    "full_name": "MyTests.TestOther",
    "mode": "PlayMode"
  }
]
```

### mcpforunity://tests/{mode}

**Purpose:** Tests filtered by mode.

**Parameters:**
- `mode` (string): "EditMode" or "PlayMode"

**Example:** `mcpforunity://tests/EditMode`

---

## Best Practices

### 1. Check Editor State First

```python
# Before any complex operation:
# Read mcpforunity://editor/state
# Check ready_for_tools == true
```

### 2. Use Find Then Read Pattern

```python
# 1. find_gameobjects to get IDs
result = find_gameobjects(search_term="Player")

# 2. Read resource for full data
# mcpforunity://scene/gameobject/{id}
```

### 3. Paginate Large Queries

```python
# Start with include_properties=false for component lists
# mcpforunity://scene/gameobject/{id}/components?include_properties=false&page_size=25

# Then read specific components as needed
# mcpforunity://scene/gameobject/{id}/component/Rigidbody
```

### 4. URL-Encode Prefab Paths

```python
# Wrong:
# mcpforunity://prefab/Assets/Prefabs/Player.prefab

# Correct:
# mcpforunity://prefab/Assets%2FPrefabs%2FPlayer.prefab
```

### 5. Multi-Instance Awareness

```python
# Always check mcpforunity://instances when:
# - First connecting
# - Commands fail unexpectedly
# - Working with multiple projects
```
