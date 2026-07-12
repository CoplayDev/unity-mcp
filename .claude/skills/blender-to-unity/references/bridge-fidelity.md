# Blender → Unity Bridge — Fidelity Test Results

Empirical results from probing what survives the Blender→Unity file handoff. The bridge is a
file export/import seam (Blender writes FBX/glTF, Unity reads it), so fidelity = whatever those
interchange formats + Unity's importers support — not a BlenderMCP limitation.

**Test env:** Blender (BlenderMCP) → Unity 6000.4.11f1, URP, glTFast installed. FBX via the
built-in model importer; GLB via glTFast. Method: one controlled object per feature, exported
**both** ways (`export_scene.fbx` and `export_scene.gltf` GLB), imported both, inspected the
resulting mesh/material/animation assets.

## Results

| Probe | FBX | GLB (glTFast) |
|---|---|---|
| Mesh geometry (verts, normals, UVs) | ✅ | ✅ |
| Object hierarchy + transforms | ✅ | ✅ |
| Base / diffuse color | ✅ | ✅ (values shift sRGB↔linear, e.g. `0.20`→`0.48`; visually correct) |
| **Procedural / node material** (noise→color) | ❌ → flat default grey | ❌ → flat default white — **but bake it to a texture first (below) → ✅ arrives intact** |
| **Image texture** (checker on base color) | ❌ embedded but **not assigned** to `_BaseMap` | ✅ `baseColorTexture` = embedded image |
| **Vertex colors — data** | ✅ `mesh.colors` present | ✅ present |
| Vertex colors — *display* | ❌ URP Lit ignores them | ✅ glTFast shader displays them (confirmed — white-base mesh rendered its vertex colors) |
| **Alpha / transparency** | ❌ stays **Opaque** (alpha kept in color, surface not switched, rq 2000) | ✅ **Transparent** (Surface=1, rq 3000) |
| **Metallic / roughness** | ❌ metallic reset to 0 on URP conversion | ✅ `metal=1.0`, `rough=0.15` |
| **Modifier** (Subsurf, unapplied) | ✅ baked on export (8→384 verts) | ✅ baked (8→384 verts) |
| **Animation** (object transform) | ⚠️ in file, but importer default `animationType=None` → 0 clips; pass `animation_type="generic"` → 1 clip | ✅ auto-imported (1 clip) |
| **Armature + skinning** | ✅ skinned mesh, 3 bind poses | ✅ skinned mesh, 3 bind poses |
| **Skeletal animation** | ✅ (pass `animation_type="generic"`/`"humanoid"`) | ✅ auto (Animator + clip) |
| **Multi-material zones** (submeshes) | ✅ submeshes + a material each | ✅ submeshes + a material each (base colors native) |
| **Shape keys → blend shapes** | ✅ (Bulge, Spike) | ✅ (Bulge, Spike) |
| **Custom properties** | ➖ FBX user-props (not surfaced by default) | ➖ glTF `extras` (needs `export_extras`; not auto-exposed) |
| **Emission** (from the city scene) | ❌ dropped | ✅ native (`emissiveFactor` + `KHR_materials_emissive_strength`) |
| **Scale** | oversized, needs normalize | oversized (larger), needs normalize |

Legend: ✅ transfers · ❌ lost · ⚠️ needs an import setting · ➖ partial/shader-dependent.

## Verdict

**GLB via glTFast is materially higher-fidelity than FBX** for this pipeline: textures,
transparency, metallic/roughness, emission, and animation all transfer automatically. FBX only
ties on geometry, vertex-color *data*, and modifier baking, and it wins only where you need the
built-in importer's rig/humanoid pipeline or want URP-Lit-native materials (e.g. for URP fog).

Neither format carries **procedural/node materials** — those must be **baked to image textures in
Blender first**. Neither shows **vertex colors** without a vertex-color-reading shader.

## Gotchas discovered (worth automating around)

1. **`export_scene.gltf` defaults `use_active_scene=False`.** It exported the *wrong* scene (a
   different open scene) — 4.7 MB instead of 65 KB. Always pass **`use_active_scene=True`**.
2. **FBX `embed_textures=True` embeds but doesn't assign.** The image lands inside the FBX but
   Unity's URP material conversion leaves `_BaseMap` empty. Extract materials/textures and assign,
   or just use GLB.
3. **FBX animation needs a non-`None` rig mode.** The clip data is in the file, but the importer's
   default `animationType = None` yields **zero clips**. `import_model_file` takes
   `animation_type="generic" | "humanoid" | "legacy"` to set this at import time (previously you had
   to hand-edit the `ModelImporter` after the fact, which is what surfaced this whole gotcha). GLB is
   unaffected — glTFast imports animation itself.
4. **FBX drops metallic and emission** in the built-in→URP material conversion; GLB keeps both.
5. **Both need a scale normalize** (measure world bounds → set `localScale`); GLB tends to land
   even larger than FBX.
6. **Rigs & morphs need "no-apply" export.** For skinned / shape-key meshes, skip
   `bake_space_transform` (FBX) and `export_apply` (glTF) — applying modifiers bakes away the
   armature deform and morph targets. Use `use_mesh_modifiers=False` (FBX) / `export_apply=False`
   (glTF). Static-geometry exports still want the apply (it bakes Subsurf etc.).
7. **Imported animation doesn't auto-play.** The clip imports fine but the placed model just has an
   `Animator` with a **null controller**, so it looks frozen in edit mode. To *see* it: assign an
   `AnimatorController` referencing the clip (set the clip's `loopTime` for continuous playback) and
   enter **Play mode** (or drive it via the Animation window / Timeline / legacy `Animation`). This
   is a Unity playback detail, not a transfer failure — verified the bone rotated 37°→6° while
   playing and looped 15×.

## Recovering procedural / node materials (bake)

Neither format carries node graphs, but you can **bake them to an image texture in Blender**, then
they export as an ordinary texture. Verified: a `Voronoi → ColorRamp` material baked to 512px and
arrived in Unity with `baseColorTexture` assigned and the pattern visible.

Recipe (Cycles bake):
1. Give the object a real UV unwrap — `bpy.ops.uv.smart_project(...)`.
2. Add an empty Image + Image Texture node to the material and set it **active** (the bake target):
   `nt.nodes.active = texnode`.
3. `scene.render.engine='CYCLES'`; select the object as active; then
   `bpy.ops.object.bake(type='DIFFUSE', pass_filter={'COLOR'}, margin=4, use_clear=True)`.
4. Rewire the baked texture node into **Base Color** (replacing the procedural chain); `image.pack()`.
5. Export GLB as usual. (Bake `EMIT` for glow-driven looks; `COMBINED` to bake full lighting in.)

## Capture more in one export (GLB flags)

Enable the extra channels you want in `export_scene.gltf(..., export_format='GLB', use_active_scene=True, ...)`:
- `export_apply=True` — bakes modifiers (Subsurf etc.) — **static geometry only**; skinned / shape-key
  meshes need `export_apply=False` or the armature deform and morph targets bake away (gotcha 6)
- `export_animations=True` — **active or NLA-stashed** actions → clips; unassociated actions are
  dropped (Stash / Push Down them onto the object they animate first)
- `export_morph=True` — **shape keys → Unity blend shapes**
- `export_skins=True` — **armature / skinning**
- `export_tangents=True` — normal-map-ready meshes
- `export_extras=True` — Blender **custom properties**
- `export_cameras=True`, `export_lights=True` — cameras/lights come across (units differ — treat as reference, retune in Unity)

## Practical rules

- **Materials/textures/PBR/emission/animation matter → GLB** (glTFast installed).
- **Rigs/humanoid/URP-Lit-native materials matter → FBX** (then extract materials, set animation type).
- **Anything procedural/simulated (node materials, geometry nodes, particles, physics) → bake in
  Blender first** (bake to textures, apply/realize modifiers, convert particles to mesh).
