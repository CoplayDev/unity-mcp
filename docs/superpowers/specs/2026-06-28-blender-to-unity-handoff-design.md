# BlenderMCP → UnityMCP Model Handoff — Design

- **Date:** 2026-06-28
- **Status:** Approved design (pending spec review) → implementation plan
- **Branch context:** asset-generation feature line (`feat/3d-asset-generation`)

## 1. Motivation

Users who run **BlenderMCP** alongside **MCP for Unity** can already do sophisticated
work in Blender — hand-modeling, cleanup/decimation/retopo, UV and material setup, and
Blender's own asset sources (Rodin/Hyper3D, Hunyuan, PolyHaven, Sketchfab). What's missing
is a clean way to **take whatever is in Blender and put it into Unity** — imported with good
settings and placed into the open scene — without hand-wiring file paths and import steps.

This design adds the one missing primitive on the Unity side and a skill that orchestrates
the end-to-end handoff.

## 2. Goals / Non-Goals

**Goals**
- Import a model file that already exists on disk (any DCC export) into the Unity project
  through the existing model-import pipeline, returning the asset path + GUID.
- A `blender-to-unity` skill that exports the current Blender model and brings it into the
  open Unity scene, then verifies visually.
- Reusable beyond Blender (Maya/Houdini/Marmoset exports work too).

**Non-Goals (YAGNI)**
- No C#↔Blender socket bridge; the two MCP servers never talk to each other directly.
- No bidirectional sync (Unity → Blender round-trip).
- No folder-watching / background auto-import.
- No Blender addon changes.
- The skill does **not** drive Blender's own generators/asset libraries — BlenderMCP owns
  how a model gets into Blender. Our scope begins at "there is a model in Blender."

## 3. Architecture

Both servers run on the same machine and are orchestrated by the MCP client (Claude). The
**integration seam is the local filesystem**: Blender *exports a file*, Unity *imports that
file*. Nothing Blender-specific enters UnityMCP's C#.

```
Claude (MCP client)
  ├─ BlenderMCP:  export current model  ──► <temp>/handoff.fbx
  └─ UnityMCP:    import_model_file(source_path) ──► Assets/<folder>/handoff.fbx (imported)
                  manage_gameobject(create) ──► placed in open scene
                  manage_camera(screenshot) ──► visual verify
```

### Format choice
- **Default: FBX.** Unity's built-in `ModelImporter` handles it with no extra package, and
  Blender exports it natively (`bpy.ops.export_scene.fbx`).
- **glTF/.glb when glTFast is installed** (or when PBR fidelity matters). The skill checks
  glTFast availability (Dependencies tab / a probe) and only then prefers glTF.

## 4. Component A — `import_model_file` (new UnityMCP tool)

A focused, DCC-agnostic tool in the `asset_gen` group. It does **import only**; placement is
the skill's job (single responsibility, per repo philosophy).

**Behavior**
1. Accept `source_path` pointing anywhere on disk (e.g. Blender's temp export).
2. Copy it under `Assets/<output_folder>/<name>.<ext>` (default folder
   `Assets/Generated/Models`, mirroring the existing asset-gen output convention).
3. Run the **existing** `ModelImportPipeline.ImportInto(...)`, inheriting for free:
   glTFast detection, FBX/OBJ/zip handling, scale-normalization (`target_size`), and
   `ModelImporter` material/animation settings.
4. Return `{ asset_path, asset_guid }`.

**Parameters**
| Param | Meaning |
|-------|---------|
| `source_path` | Absolute or project-relative path to the model file (`.fbx/.obj/.glb/.gltf/.zip`). Required. |
| `name` | Base name for the imported asset. Optional (defaults to source filename). |
| `output_folder` | Destination under `Assets/`. Optional. |
| `target_size` | Normalize largest dimension to this size (meters). Optional. |

**Error handling** (reuses pipeline errors): missing/unreadable `source_path`; unsupported
extension; glTF without glTFast (actionable message pointing at the Dependencies tab);
file failed to register as an asset. Errors are scrubbed via `SecretRedactor` for parity.

**Surfaces (per CLAUDE.md "Adding a New Tool"):**
- Python MCP tool: `Server/src/services/tools/import_model_file.py` (`@mcp_for_unity_tool`,
  thin pass-through — no bytes cross the bridge; the file is already local to the editor).
- Python CLI: `Server/src/cli/commands/` entry mirroring it.
- C# handler: `MCPForUnity/Editor/Tools/AssetGen/ImportModelFile.cs` (`[McpForUnityTool]`),
  delegating to `ModelImportPipeline`.
- Tests: Python pass-through test + C# EditMode test (fixture file → asserts import,
  GUID returned, scale-normalize applied, glTF-without-glTFast error path).

**Why a new tool and not an action on `import_model`:** `import_model` is semantically the
*Sketchfab marketplace* tool (search/preview/import-by-uid). A generic "import a file I
already have" is a different concern; a separate focused tool keeps both clean.

## 5. Component B — `blender-to-unity` skill

A client-side orchestration skill (no code in either server). Flow:

1. **Preflight** — confirm both BlenderMCP (`mcp__blender__*`) and UnityMCP tools are
   present; confirm a model exists in the Blender scene (`get_scene_info` / `get_object_info`).
2. **Resolve Unity project path** — read UnityMCP `editor/state` (or `project/info`) to learn
   the project root, and check glTFast availability to pick the export format.
3. **Export from Blender** — `execute_blender_code` running `bpy.ops.export_scene.fbx`
   (selection or scene) to a temp path. (glTF branch when chosen.)
4. **Import into Unity** — call `import_model_file` with the temp path + `name`/`target_size`.
5. **Place in scene** — `manage_gameobject(action=create, ...)` referencing the imported
   model asset, at a chosen position; ensure the scene has a camera + light.
6. **Verify** — `manage_camera(action=screenshot, include_image=true)` and report.

The skill documents the FBX-default rule, the temp→Assets handoff, and a **manual-verify
checklist** (mirroring the asset-gen Phase 8 checklist).

## 6. Data Flow & Handoff Mechanics

- **Same-machine assumption.** Blender and Unity share a filesystem; the handoff is a file,
  not a network payload. (If ever remote, that's a separate design.)
- **Path discovery.** The skill gets the Unity project root from UnityMCP rather than guessing.
- **Copy-under-Assets.** `import_model_file` copies the export into `Assets/` itself, so the
  skill can export to a throwaway temp dir and let the tool own placement + import settings.

## 7. Testing Strategy

- **C# EditMode** (`TestProjects/UnityMCPTests/Assets/Tests/EditMode/AssetGen/`): import a
  small fixture `.fbx`/`.obj`, assert asset path + GUID, scale-normalize, and the
  glTF-without-glTFast error path. No network — no fake transport needed.
- **Python** (`Server/tests/`): pass-through test asserting the params marshalled to the
  `import_model_file` command (mirrors existing `import_model` tests).
- **Skill:** manual-verify checklist run against a live Blender + Unity pair.

## 8. Implementation Phases (high level)

1. C# `ImportModelFile` handler + EditMode tests (the reusable primitive; independently useful).
2. Python MCP tool + CLI + pass-through test.
3. `blender-to-unity` skill + manual-verify checklist + README/docs note.

Detailed step-by-step plan to be produced by the writing-plans step.

## 9. Open Questions

- None blocking. Default output folder and FBX-vs-glTF heuristic are settled above; revisit
  only if user feedback shows the heuristic guesses wrong often.
