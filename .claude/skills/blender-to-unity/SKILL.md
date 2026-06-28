---
name: blender-to-unity
description: Hand off a model from Blender (via BlenderMCP) into Unity (via MCP for Unity) — export the current Blender model, import it through import_model_file, and place it in the open scene. Use when the user has BlenderMCP and MCP for Unity both connected and wants to bring a Blender model into Unity. Does NOT drive Blender's own generators; BlenderMCP owns how the model got into Blender.
---

# Blender → Unity Model Handoff

Bring whatever model is currently in Blender into the open Unity scene. The seam is the
local filesystem: Blender exports a file, Unity imports it. The two servers never talk
directly.

## Preconditions
- Both `mcp__blender__*` tools and MCP for Unity tools are connected.
- A model exists in the Blender scene (confirm with `mcp__blender__get_scene_info` /
  `get_object_info`). If empty, stop and tell the user — this skill does not generate models.

## Steps
1. **Resolve the Unity project path.** Read `mcpforunity://editor/state` for the project
   root (the editor dataPath's parent). Decide the export format: **FBX by default**
   (built-in importer, zero extra dependencies). Use glTF/.glb only if glTFast is installed
   and PBR fidelity matters.
2. **Export from Blender to a temp path** via `mcp__blender__execute_blender_code`:
   ```python
   import bpy, os, tempfile
   out = os.path.join(tempfile.gettempdir(), "blender_to_unity.fbx")
   # Export the selection if any, else the whole scene:
   bpy.ops.export_scene.fbx(filepath=out, use_selection=bool(bpy.context.selected_objects),
                            apply_unit_scale=True, bake_space_transform=True)
   print(out)
   ```
   (glTF branch: `bpy.ops.export_scene.gltf(filepath=out_glb, export_format='GLB')`.)
3. **Import into Unity** with `import_model_file`:
   `import_model_file(source_path=<temp path>, name=<asset name>, target_size=<meters, optional>)`.
   It returns `{ asset_path, asset_guid }`.
4. **Place it in the scene.** Ensure the scene has a camera + directional light
   (`manage_scene` / `manage_gameobject`). Instantiate the imported model into the open scene
   at the chosen position via `manage_gameobject(action="create", prefab_path=<asset_path>, ...)`.
5. **Verify** with `manage_camera(action="screenshot", include_image=true)` and report the
   asset path + a screenshot.

## Notes
- FBX is the default because glTFast is optional in MCP for Unity. If the import errors with
  "GLB import requires glTFast", re-export as FBX (or install glTFast from the Dependencies tab).
- Keep one model per handoff; for batches, repeat the loop with distinct names.
- This skill never sends API keys or file bytes over the MCP bridge — Unity reads the file from disk.
