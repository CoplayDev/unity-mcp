# Prefab Stage Lookup Test Plan

This document outlines the exact MCP calls to test the prefab stage GameObject lookup fix.

## Prerequisites
- Unity Editor running with MCP for Unity installed
- HTTP server running (Window > MCP for Unity > Start Local HTTP Server)
- At least one prefab in the project (or create one first)

## Test Sequence

### Step 1: Check Current State
**Resource:** `mcpforunity://editor/prefab-stage`
- Should return `{"isOpen": false}` if no prefab is open

### Step 2: Find a Prefab to Test With
**Tool:** `manage_asset`
**Parameters:**
```json
{
  "action": "search",
  "search_term": "prefab",
  "asset_type": "Prefab",
  "page_size": 10
}
```

### Step 3: Open Prefab Stage
**Tool:** `manage_prefabs`
**Parameters:**
```json
{
  "action": "open_stage",
  "prefab_path": "Assets/Prefabs/YourPrefab.prefab",  // Use actual path from Step 2
  "mode": "InIsolation"
}
```

### Step 4: Verify Prefab Stage is Open
**Resource:** `mcpforunity://editor/prefab-stage`
- Should return `{"isOpen": true, "prefabRootName": "..."}`

### Step 5: Test Path-Based Search (Root Object)
**Tool:** `find_gameobjects`
**Parameters:**
```json
{
  "search_method": "by_path",
  "search_term": "<root-object-name>",  // Name of root prefab object
  "include_inactive": true
}
```
**Expected:** Should find the root GameObject (this was broken before the fix)

### Step 6: Test Nested Path Search
**Tool:** `find_gameobjects`
**Parameters:**
```json
{
  "search_method": "by_path",
  "search_term": "<root>/<child>",  // Path to nested object
  "include_inactive": true
}
```
**Expected:** Should find nested GameObjects by path

### Step 7: Test Get Hierarchy in Prefab Stage
**Tool:** `manage_scene`
**Parameters:**
```json
{
  "action": "get_hierarchy"
}
```
**Expected:** Should return the prefab stage hierarchy (not the scene hierarchy)

### Step 8: Test Name-Based Search
**Tool:** `find_gameobjects`
**Parameters:**
```json
{
  "search_method": "by_name",
  "search_term": "<object-name>",
  "include_inactive": true
}
```
**Expected:** Should find objects by name in prefab stage

### Step 9: Cleanup
**Tool:** `manage_prefabs`
**Parameters:**
```json
{
  "action": "close_stage"
}
```

## What Was Fixed

Before the fix:
- `GameObject.Find(path)` doesn't work in Prefab Stage mode
- `find_gameobjects` with `by_path` would fail or return empty results
- `get_hierarchy` would return scene hierarchy instead of prefab stage hierarchy

After the fix:
- `PrefabStageUtility.GetCurrentPrefabStage()` is checked first
- Path-based searches iterate through prefab stage objects manually
- Hierarchy queries use prefab stage scene when in isolation mode

## Success Criteria

✅ All path-based searches work in prefab stage
✅ Hierarchy queries return prefab stage objects
✅ No errors or empty results when prefab stage is open
✅ Normal scene mode still works correctly
