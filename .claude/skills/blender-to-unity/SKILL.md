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
  `mcp__blender__get_object_info`). If empty, stop and tell the user — this skill does not generate models.

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
   `import_model_file(source_path=<temp path>, name=<asset name>, target_size=<final size in meters>)`.
   It returns `{ asset_path, asset_guid }`. Pass `target_size` as the intended final size, but treat
   it only as a hint: it rescales at import solely when the project's **Auto-normalize** pref is on,
   and even then is unreliable for Blender FBX (see the **Scale** note). Step 4 does the reliable
   normalization.
4. **Place it in the scene, normalized to size.** Ensure the scene has a camera + directional
   light (`manage_scene` / `manage_gameobject`). Instantiate the model at the chosen position via
   `manage_gameobject(action="create", prefab_path=<asset_path>, name=<asset name>, position=[x,y,z])`.
   Then normalize its size deterministically — Blender FBX commonly imports ~100× too large — by
   measuring the placed model's world bounds and scaling so its largest dimension equals your target
   size. Run via `execute_code` (substitute your object name and target meters):
   ```csharp
   var go = GameObject.Find("<asset name>");
   var rs = go.GetComponentsInChildren<Renderer>();
   var b = rs[0].bounds; for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
   float maxDim = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
   float target = 2f; // intended size in meters
   if (maxDim > 0.0001f) go.transform.localScale *= target / maxDim;
   ```
5. **Verify** with `manage_camera(action="screenshot", include_image=true)` and report the
   asset path + a screenshot.

## Notes
- **Scale.** Models from Blender almost always arrive at the wrong scale — its FBX unit handling
  makes them land ~100× too large in Unity. `import_model_file`'s `target_size` only rescales at
  import when the project's Auto-normalize pref is enabled, and its importer-level normalization is
  unreliable for Blender FBX (it can over- or under-shoot, e.g. a `target_size=2` model measured 200 m).
  The robust fix is the Step 4 measure-bounds-then-set-`localScale` routine, which hits the target
  size deterministically regardless of the import scale or the Auto-normalize pref.
- FBX is the default because glTFast is optional in MCP for Unity. If the import errors with
  "GLB import requires glTFast", re-export as FBX (or install glTFast from the Dependencies tab).
- Keep one model per handoff; for batches, repeat the loop with distinct names.
- This skill never sends API keys or file bytes over the MCP bridge — Unity reads the file from disk.
- `import_model_file` copies only the single source file; for multi-file exports (a text `.gltf` with an external `.bin`, or an `.obj` with a sibling `.mtl`/textures), zip them first and pass the `.zip` — a bare `.gltf`/`.obj` will lose its sidecars.
