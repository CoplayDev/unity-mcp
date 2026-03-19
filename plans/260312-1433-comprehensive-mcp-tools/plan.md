---
status: completed
created: 2026-03-12
completed: 2026-03-12
branch: beta
---

# Master Plan: Comprehensive MCP Tools for Unity 6.3.x

**Goal:** Expand from 32 → 53 MCP tools covering all major Unity API domains.
**Source:** `plans/reports/brainstorm-260312-1433-comprehensive-mcp-tools-gap-analysis.md`

## Architecture

Each tool = 3-4 files following established pattern:

```
Server/src/services/tools/manage_<domain>.py     ← Python MCP tool (@mcp_for_unity_tool)
Server/src/cli/commands/<domain>.py              ← Click CLI (optional)
MCPForUnity/Editor/Tools/Manage<Domain>.cs       ← C# handler ([McpForUnityTool])
Server/tests/integration/test_manage_<domain>.py ← Integration tests
```

**Package-gated tools:** `#if DEFINE_NAME` in C#, define via `versionDefines` in `MCPForUnity.Editor.asmdef`.

## Phases Overview

| Phase | Tools | Theme | Est. Files |
|-------|-------|-------|-----------|
| [Phase 1](./phase-01-core-physics-sensory.md) | 4 tools | Core Physics & Sensory | 12 | ✅ Done |
| [Phase 2](./phase-02-editor-automation.md) | 4 tools | Editor Automation | 12 | ✅ Done |
| [Phase 3](./phase-03-popular-packages.md) | 4 tools | Popular Packages | 12 | ✅ Done |
| [Phase 4](./phase-04-2d-content.md) | 4 tools | 2D & Content | 12 | ✅ Done |
| [Phase 5](./phase-05-specialized.md) | 5 tools | Specialized & Niche | 15 | ✅ Done |
| **Total** | **21 tools** | | **63 files** | **All complete** |

## Dependencies

- Phase 1 has no dependencies (built-in Unity APIs)
- Phase 2 has no dependencies (built-in Editor APIs)
- Phase 3 requires optional packages (Input System, Timeline, Cinemachine)
- Phase 4 requires 2D packages for tilemap
- Phase 5 has heaviest package requirements (Addressables, Netcode, Localization, Behavior)

## asmdef Changes

All package-gated tools need entries in `MCPForUnity/Editor/MCPForUnity.Editor.asmdef`:

```json
// versionDefines additions:
{"name": "com.unity.ai.navigation",    "expression": "", "define": "UNITY_AI_NAVIGATION"}
{"name": "com.unity.inputsystem",      "expression": "", "define": "UNITY_INPUT_SYSTEM"}
{"name": "com.unity.timeline",         "expression": "", "define": "UNITY_TIMELINE"}
{"name": "com.unity.cinemachine",      "expression": "", "define": "UNITY_CINEMACHINE"}
{"name": "com.unity.2d.tilemap",       "expression": "", "define": "UNITY_TILEMAP"}
{"name": "com.unity.addressables",     "expression": "", "define": "UNITY_ADDRESSABLES"}
{"name": "com.unity.splines",          "expression": "", "define": "UNITY_SPLINES"}
{"name": "com.unity.localization",     "expression": "", "define": "UNITY_LOCALIZATION"}
{"name": "com.unity.netcode.gameobjects", "expression": "", "define": "UNITY_NETCODE"}
{"name": "com.unity.behavior",         "expression": "", "define": "UNITY_BEHAVIOR"}

// references additions:
"Unity.AI.Navigation", "Unity.AI.Navigation.Editor"
"Unity.InputSystem"
"Unity.Timeline", "Unity.Timeline.Editor"
"Cinemachine"
"Unity.2D.Tilemap", "Unity.2D.Tilemap.Editor"
"Unity.Addressables", "Unity.Addressables.Editor"
"Unity.Splines"
"Unity.Localization"
"Unity.Netcode.Runtime"
"Unity.Behavior"
```

## Key Decisions

1. **Separate `manage_physics` and `manage_physics2d`** — different Vector types, cleaner API
2. **Full build support** — async job pattern like `run_tests`
3. **`manage_render_pipeline`** added as Tier 2 — URP/HDRP volume profiles
4. **All new files** — no modifying upstream files (fork strategy)

## STUDIO_TOOLS.md Updates

After each phase, update `docs/reference/STUDIO_TOOLS.md` with new tool documentation.

## Risk Mitigations

- **asmdef bloat:** Add references + versionDefines incrementally per phase
- **Compile errors:** Every package-gated tool uses `#if` guard; missing packages = tool silently excluded
- **Tool count for LLMs:** Good descriptions are critical; each tool's description lists all actions
- **Test isolation:** Tests use monkeypatch + DummyContext pattern (no live Unity needed)
