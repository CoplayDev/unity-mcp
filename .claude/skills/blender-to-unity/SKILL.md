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
- **`import_model_file` is in the `asset_gen` tool group, which is off by default** (only `core`
  loads). Enable it first with `manage_tools` (enable the `asset_gen` group), **or** call the C#
  handler straight through `batch_execute` —
  `{"tool":"import_model_file","params":{"sourcePath":...,"name":...,"outputFolder":...}}` (camelCase)
  — which dispatches by name regardless of group gating.

## Steps
1. **Resolve the Unity project path.** Read `mcpforunity://editor/state` for the project
   root (the editor dataPath's parent). Decide the export format:
   - **GLB (glTFast) when the model has a rig, animation, PBR (metallic/roughness), emission,
     or transparency** — glTFast carries all of these automatically, no post-processing
     (see [references/bridge-fidelity.md](references/bridge-fidelity.md)). Multi-material zones
     survive either format, so they alone don't force GLB.
   - **FBX otherwise** — when glTFast isn't installed, the model is plain geometry, or you
     specifically need the built-in importer's humanoid-avatar pipeline. FBX drops emission/metallic
     (Step 5 restores emission) and surfaces animation only with `animation_type` set (Step 3).
2. **Export from Blender to a temp path** via `mcp__blender__execute_blender_code`:
   ```python
   import bpy, os, tempfile
   out = os.path.join(tempfile.gettempdir(), "blender_to_unity.fbx")
   # Export the selection if any, else the whole scene:
   bpy.ops.export_scene.fbx(filepath=out, use_selection=bool(bpy.context.selected_objects),
                            apply_unit_scale=True, bake_space_transform=True)
   print(out)
   ```
   (glTF branch: `out_glb = os.path.join(tempfile.gettempdir(), "blender_to_unity.glb")`, then
   `bpy.ops.export_scene.gltf(filepath=out_glb, export_format='GLB', use_active_scene=True)`
   — its default `use_active_scene=False` can silently export a *different* open scene.)
3. **Import into Unity** with `import_model_file`:
   `import_model_file(source_path=<temp path>, name=<asset name>, target_size=<final size in meters>)`.
   For a **rigged/animated FBX**, also pass `animation_type="generic"` (or `"humanoid"`; `"legacy"`
   targets the old Animation-component system) — the importer defaults to `"none"`, which
   deliberately imports the mesh with **zero animation clips**. GLB ignores this (glTFast imports
   animation itself), so it's an FBX-only knob.
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
5. **Restore emission FBX dropped (FBX path).** Blender scenes commonly store their color/glow
   in *material emission* (and other Principled-node inputs). FBX carries base/diffuse color but
   **not emission**, so neon / "Tron" scenes import as dark bodies with black accents. If the
   import looks flat vs. Blender, restore it:
   a. Dump the emissive materials from Blender via `execute_blender_code`:
      ```python
      import bpy, json
      out = {}
      for m in bpy.data.materials:
          if not (m.use_nodes and m.node_tree): continue
          col, s = (0, 0, 0), 0.0
          p = next((n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
          es = next((k for k in ('Emission Color', 'Emission') if p and k in p.inputs), None)  # 4.x / 3.x name
          if es:
              col = tuple(p.inputs[es].default_value)[:3]
              s = float(p.inputs['Emission Strength'].default_value)
          e = next((n for n in m.node_tree.nodes if n.type == 'EMISSION'), None)
          if e and s == 0:
              col = tuple(e.inputs['Color'].default_value)[:3]; s = float(e.inputs['Strength'].default_value)
          if s > 0 and sum(col) > 0.01:
              out[m.name] = [round(col[0], 3), round(col[1], 3), round(col[2], 3), round(s, 3)]
      print(json.dumps(out))
      ```
   b. In Unity (`execute_code`): extract the FBX's materials so they're editable
      (`AssetDatabase.ExtractAsset` per `Material`, then `ImportAsset(fbx, ForceUpdate)`), then for
      each dumped name set `_EmissionColor = color * Mathf.Clamp(strength*0.45f, 1.5f, 5f)`,
      `EnableKeyword("_EMISSION")`, and `globalIlluminationFlags = RealtimeEmissive`. Match names
      **case-insensitively** — FBX mangles case and can split one material into variants
      (`EdgeCyan` → `EdgeCyan` + `EDGE_CYAN`); set every match. Add a global Bloom volume and enable
      the camera's `renderPostProcessing` so the emission actually glows.
6. **Verify** with `manage_camera(action="screenshot", include_image=true)` and report the
   asset path + a screenshot.

## Notes
- **Scale.** Models from Blender almost always arrive at the wrong scale — its FBX unit handling
  makes them land ~100× too large in Unity. `import_model_file`'s `target_size` only rescales at
  import when the project's Auto-normalize pref is enabled, and its importer-level normalization is
  unreliable for Blender FBX (it can over- or under-shoot, e.g. a `target_size=2` model measured 200 m).
  The robust fix is the Step 4 measure-bounds-then-set-`localScale` routine, which hits the target
  size deterministically regardless of the import scale or the Auto-normalize pref.
- **Materials & emission.** FBX carries base/diffuse color and transforms fine, but **drops
  emission and any node-based color** — so Blender neon / "Tron" scenes (color stored in
  emission) import as dark bodies with black accents. Two fixes: **(a) prefer glTF/GLB when glTFast
  is installed** — glTF's PBR model carries `emissiveFactor` + `KHR_materials_emissive_strength`
  natively, so emission survives with no post-step (`bpy.ops.export_scene.gltf(filepath=out, export_format='GLB', use_active_scene=True)`);
  **(b) with FBX, run Step 5** to dump Blender's emission and reapply it. Also check the mesh for a
  `color_attributes` (vertex color) layer — the *data* survives FBX, but URP Lit won't *display* it;
  that needs a vertex-color-reading shader, not `_EmissionColor`.
- FBX is the default because glTFast is optional in MCP for Unity. If the import errors with
  "GLB import requires glTFast", re-export as FBX (or install glTFast from the Dependencies tab).
- **Format fidelity.** [references/bridge-fidelity.md](references/bridge-fidelity.md) is a tested
  matrix of what each format carries. Summary: **GLB (glTFast)** keeps textures, metallic/roughness,
  emission, transparency, and animation automatically; **FBX** drops metallic + emission, needs
  `ModelImporter.animationType = Generic` to surface transform animation, and needs material/texture
  extraction to assign an embedded texture. Both bake modifiers and carry geometry + vertex-color
  *data* (URP Lit won't *display* vertex colors). Neither carries procedural/node materials — bake
  them to image textures in Blender first.
- **Animation doesn't auto-play.** An imported clip won't move the model until an `AnimatorController`
  drives it — the placed model has an `Animator` with a **null controller**, so it looks frozen. Build
  one with `manage_animation` (`controller_create` → add the clip as a looping state → `controller_assign`
  onto the instance) rather than hand-rolling `execute_code`. See bridge-fidelity gotcha #7.
- **Multi-material zones survive.** A mesh split into material slots in Blender (e.g. skin/shirt/pants
  regions) imports as submeshes with one material each — base colors carry over GLB natively — so you
  can "dress" a model with material zones and it arrives intact.
- Keep one model per handoff; for batches, repeat the loop with distinct names.
- This skill never sends API keys or file bytes over the MCP bridge — Unity reads the file from disk.
- `import_model_file` copies only the single source file; for multi-file exports (a text `.gltf` with an external `.bin`, or an `.obj` with a sibling `.mtl`/textures), zip them first and pass the `.zip` — a bare `.gltf`/`.obj` will lose its sidecars.
