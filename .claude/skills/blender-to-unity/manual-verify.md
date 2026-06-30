# Manual Verify — blender-to-unity

Run against a live Blender (BlenderMCP) + Unity (MCP for Unity) pair.

- [ ] A cube/model exists in Blender (`get_scene_info` shows it).
- [ ] FBX export writes a non-empty file to the temp path (printed path exists, size > 0).
- [ ] `import_model_file` returns `success: true` with `asset_path` under `Assets/` and a non-empty `asset_guid`.
- [ ] The imported model appears in the Project window at `asset_path`.
- [ ] The model is instantiated in the open scene and visible in a `manage_camera` screenshot.
- [ ] Scale: after the Step 4 measure-bounds-then-`localScale` routine, the placed model's largest
      world dimension ≈ the target size (Blender FBX imports ~100× too large until normalized).
- [ ] glTF path: with glTFast installed, a `.glb` export imports successfully; without it,
      the error names glTFast/the Dependencies tab (and FBX still works).
- [ ] No API keys or file bytes appear in any bridge payload (handoff is filesystem-only).
