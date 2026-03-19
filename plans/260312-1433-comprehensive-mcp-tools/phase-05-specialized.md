---
phase: 5
status: pending
priority: medium-low
---

# Phase 5: Specialized & Niche

**Theme:** UI Toolkit, Localization, Netcode, Profiler, Behavior AI.
**Package deps:** `com.unity.localization`, `com.unity.netcode.gameobjects`, `com.unity.behavior`.

---

## Tool 17: `manage_ui_toolkit`

**Purpose:** UI Toolkit (UIElements) — UIDocument, VisualElement queries, style inspection.
**Complements:** `manage_ui` covers Canvas/UGUI. This covers the newer UI Toolkit system.

### Actions (6)

| Action | Description | Key Params | Mode |
|--------|-------------|------------|------|
| `list_documents` | All UIDocument components | `page_size` | Any |
| `get_document` | Panel settings, source UXML, root element summary | `target` | Any |
| `query_elements` | Find elements by name, class, or type | `target`, `query` (USS selector) | Any |
| `get_element` | Element properties — style, class list, text, layout | `target`, `query` | Any |
| `set_style` | Modify inline style property | `target`, `query`, `property`, `value` | Any |
| `list_uxml_assets` | Find UXML assets in project | `filter`, `page_size` | Any |

### Files (no package guard — UI Toolkit is built-in in Unity 6)

| File | Notes |
|------|-------|
| `Server/src/services/tools/manage_ui_toolkit.py` | |
| `MCPForUnity/Editor/Tools/ManageUIToolkit.cs` | `UIDocument`, `VisualElement`, `UQueryBuilder` |
| `Server/tests/integration/test_manage_ui_toolkit.py` | ~6 tests |

---

## Tool 18: `manage_localization`

**Purpose:** Unity Localization — locales, string tables, entry inspection/editing.

### Actions (6)

| Action | Description | Key Params | Mode |
|--------|-------------|------------|------|
| `list_locales` | Available locales | — | Any |
| `get_active_locale` | Current active locale | — | Any |
| `set_active_locale` | Switch active locale | `locale_code` | Any |
| `list_tables` | String/Asset table collections | `type` (string/asset) | Edit |
| `get_entry` | Get localized string for key+locale | `table`, `key`, `locale` | Any |
| `set_entry` | Set localized string | `table`, `key`, `locale`, `value` | Edit |

### asmdef

```json
{"name": "com.unity.localization", "expression": "", "define": "UNITY_LOCALIZATION"}
// references: "Unity.Localization"
```

### Files

| File | Notes |
|------|-------|
| `Server/src/services/tools/manage_localization.py` | |
| `MCPForUnity/Editor/Tools/ManageLocalization.cs` | `#if UNITY_LOCALIZATION` |
| `Server/tests/integration/test_manage_localization.py` | ~6 tests |

---

## Tool 19: `manage_netcode`

**Purpose:** Netcode for GameObjects — network manager, objects, connection state.

### Actions (7)

| Action | Description | Key Params | Mode |
|--------|-------------|------------|------|
| `get_network_manager` | Transport, connection state, connected clients | — | Play |
| `list_network_objects` | All NetworkObject components | `page_size` | Any |
| `get_network_object` | Ownership, network ID, spawn state, RPCs | `target` | Any |
| `start_host` | Start as host | — | Play |
| `start_server` | Start as server | — | Play |
| `start_client` | Start as client | — | Play |
| `shutdown` | Stop networking | — | Play |

### asmdef

```json
{"name": "com.unity.netcode.gameobjects", "expression": "", "define": "UNITY_NETCODE"}
// references: "Unity.Netcode.Runtime"
```

### Files

| File | Notes |
|------|-------|
| `Server/src/services/tools/manage_netcode.py` | |
| `MCPForUnity/Editor/Tools/ManageNetcode.cs` | `#if UNITY_NETCODE` |
| `Server/tests/integration/test_manage_netcode.py` | ~7 tests |

---

## Tool 20: `manage_profiler`

**Purpose:** Deep profiler API — custom counters, memory snapshots, frame recording.
**Extends:** `rendering_stats` covers basic stats. This is deeper/configurable.

### Actions (6)

| Action | Description | Key Params | Mode |
|--------|-------------|------------|------|
| `get_counters` | Read named profiler counters | `counters` (comma-sep names) | Play |
| `list_categories` | Available ProfilerCategory names | — | Any |
| `start_recording` | Begin profiler recording to file | `path` | Play |
| `stop_recording` | Stop recording | — | Play |
| `get_frame_data` | Last N frames timing data | `count` | Play |
| `get_memory_snapshot` | Detailed memory breakdown | — | Play |

### Files (no package guard — Profiler is built-in)

| File | Notes |
|------|-------|
| `Server/src/services/tools/manage_profiler.py` | |
| `MCPForUnity/Editor/Tools/ManageProfiler.cs` | `ProfilerRecorder`, `Profiler`, `ProfilerCategory` |
| `Server/tests/integration/test_manage_profiler.py` | ~6 tests |

---

## Tool 21: `manage_behavior`

**Purpose:** Unity Behavior (AI) — behavior graph agents, blackboard variables, debug state.

### Actions (5)

| Action | Description | Key Params | Mode |
|--------|-------------|------------|------|
| `list_agents` | All BehaviorGraphAgent components | `page_size` | Any |
| `get_agent` | Graph name, running state, current node | `target` | Any |
| `list_variables` | Blackboard variables on agent | `target` | Any |
| `get_variable` | Variable name, type, value | `target`, `variable_name` | Any |
| `set_variable` | Set blackboard variable value | `target`, `variable_name`, `value` | Play |

### asmdef

```json
{"name": "com.unity.behavior", "expression": "", "define": "UNITY_BEHAVIOR"}
// references: "Unity.Behavior"
```

### Files

| File | Notes |
|------|-------|
| `Server/src/services/tools/manage_behavior.py` | |
| `MCPForUnity/Editor/Tools/ManageBehavior.cs` | `#if UNITY_BEHAVIOR` |
| `Server/tests/integration/test_manage_behavior.py` | ~5 tests |

---

## Implementation Order

17. `manage_ui_toolkit` — growing adoption, complements manage_ui
18. `manage_profiler` — extends rendering_stats
19. `manage_localization` — important for shipped games
20. `manage_netcode` — multiplayer debugging
21. `manage_behavior` — newest package, smallest user base

## Definition of Done

- [ ] All 5 tools registered and callable
- [ ] 3 tools properly guarded with `#if` defines
- [ ] asmdef updated with Localization, Netcode, Behavior
- [ ] ~30 integration tests passing
- [ ] STUDIO_TOOLS.md updated
- [ ] Full tool count reaches 53 (32 existing + 21 new)

## Final asmdef State

After all 5 phases, `MCPForUnity.Editor.asmdef` will have:

**versionDefines:** 14 total (4 existing + 10 new)
**references:** ~25 total (existing + new package refs)

All new tools compile only when their package is installed — zero impact on projects that don't use those packages.
