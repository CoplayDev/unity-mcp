---
phase: 4
status: pending
priority: medium
---

# Phase 4: 2D & Content

**Theme:** Tilemap, Addressables, Splines, Video.
**Package deps:** `com.unity.2d.tilemap`, `com.unity.addressables`, `com.unity.splines`.

---

## Tool 13: `manage_tilemap`

**Purpose:** 2D tilemap — inspect, place/remove tiles, fill areas.

### Actions (8)

| Action | Description | Key Params | Mode |
|--------|-------------|------------|------|
| `list_tilemaps` | All Tilemap components | `page_size` | Any |
| `get_info` | Size, cell layout, orientation, tile count | `target` | Any |
| `get_tile` | Tile at position — type, sprite, color, flags | `target`, `position` (x,y,z) | Any |
| `set_tile` | Place tile at position | `target`, `position`, `tile_asset` (path) | Edit |
| `clear_tile` | Remove tile at position | `target`, `position` | Edit |
| `clear_all` | Clear entire tilemap | `target` | Edit |
| `get_bounds` | Used tile bounds (min/max cell) | `target` | Any |
| `fill_area` | Fill rectangle with tile | `target`, `min`, `max`, `tile_asset` | Edit |

### asmdef

```json
{"name": "com.unity.2d.tilemap", "expression": "", "define": "UNITY_TILEMAP"}
```

### Files

| File | Notes |
|------|-------|
| `Server/src/services/tools/manage_tilemap.py` | |
| `MCPForUnity/Editor/Tools/ManageTilemap.cs` | `#if UNITY_TILEMAP` |
| `Server/tests/integration/test_manage_tilemap.py` | ~8 tests |

---

## Tool 14: `manage_addressables`

**Purpose:** Addressable asset system — groups, entries, labels, content builds.

### Actions (7)

| Action | Description | Key Params | Mode |
|--------|-------------|------------|------|
| `list_groups` | All Addressable groups | — | Edit |
| `get_group` | Entries, schemas, build/load paths | `group_name` | Edit |
| `list_entries` | Entries in group with addresses, labels | `group_name`, `page_size` | Edit |
| `get_entry` | Entry detail — GUID, address, labels, deps | `address` or `guid` | Edit |
| `list_labels` | All labels in settings | — | Edit |
| `build` | Build Addressable content | `clean` (bool) | Edit |
| `analyze` | Run analyze rules for duplicates/issues | — | Edit |

### asmdef

```json
{"name": "com.unity.addressables", "expression": "", "define": "UNITY_ADDRESSABLES"}
// references: "Unity.Addressables", "Unity.Addressables.Editor"
```

### Files

| File | Notes |
|------|-------|
| `Server/src/services/tools/manage_addressables.py` | |
| `MCPForUnity/Editor/Tools/ManageAddressables.cs` | `#if UNITY_ADDRESSABLES` |
| `Server/tests/integration/test_manage_addressables.py` | ~7 tests |

---

## Tool 15: `manage_splines`

**Purpose:** Unity Splines — inspect, add/remove knots, evaluate positions along spline.

### Actions (7)

| Action | Description | Key Params | Mode |
|--------|-------------|------------|------|
| `list_splines` | All SplineContainer components | `page_size` | Any |
| `get_spline` | Knot count, length, closed state, knot data | `target`, `spline_index` | Any |
| `get_knot` | Knot position, rotation, tangents | `target`, `spline_index`, `knot_index` | Any |
| `add_knot` | Add knot at position | `target`, `spline_index`, `position`, `rotation` | Edit |
| `remove_knot` | Remove knot by index | `target`, `spline_index`, `knot_index` | Edit |
| `set_knot` | Modify knot position/tangents | `target`, `spline_index`, `knot_index`, `position` | Edit |
| `evaluate` | Position/tangent/up at t (0-1) | `target`, `spline_index`, `t` | Any |

### asmdef

```json
{"name": "com.unity.splines", "expression": "", "define": "UNITY_SPLINES"}
// references: "Unity.Splines"
```

### Files

| File | Notes |
|------|-------|
| `Server/src/services/tools/manage_splines.py` | |
| `MCPForUnity/Editor/Tools/ManageSplines.cs` | `#if UNITY_SPLINES` |
| `Server/tests/integration/test_manage_splines.py` | ~7 tests |

---

## Tool 16: `manage_video`

**Purpose:** VideoPlayer — inspect, control playback, set URL/clip.

### Actions (7)

| Action | Description | Key Params | Mode |
|--------|-------------|------------|------|
| `list_players` | All VideoPlayer components | `page_size` | Any |
| `get_player` | URL/clip, playback state, time, length, resolution | `target` | Any |
| `set_player` | Modify source, playback speed, loop, audio output | `target`, `properties` | Any |
| `play` | Play video | `target` | Play |
| `pause` | Pause video | `target` | Play |
| `stop` | Stop video | `target` | Play |
| `set_time` | Seek to time | `target`, `time` | Play |

### Files (no package guard — VideoPlayer is built-in)

| File | Notes |
|------|-------|
| `Server/src/services/tools/manage_video.py` | |
| `MCPForUnity/Editor/Tools/ManageVideo.cs` | No `#if` guard |
| `Server/tests/integration/test_manage_video.py` | ~7 tests |

---

## Implementation Order

13. `manage_tilemap` — core 2D workflow
14. `manage_addressables` — large project necessity
15. `manage_splines` — growing adoption
16. `manage_video` — simplest, quick win

## Definition of Done

- [ ] All 4 tools registered and callable
- [ ] 3 tools properly guarded with `#if` defines
- [ ] asmdef updated with Tilemap, Addressables, Splines
- [ ] ~29 integration tests passing
- [ ] STUDIO_TOOLS.md updated
