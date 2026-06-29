---
title: import_model_file
sidebar_label: import_model_file
description: "Import a local 3D model file that already exists on disk (e.g. an FBX/OBJ/glTF exported from Blender or another DCC tool) into the Unity project."
---

# `import_model_file`

> **Auto-generated** from the Python tool registry. Do not hand-edit outside `<!-- examples:start --><!-- examples:end -->` blocks — the generator (`tools/generate_docs_reference.py`) will overwrite them.

**Group:** `asset_gen` &nbsp;·&nbsp; **Module:** `services.tools.import_model_file`

## Description

Import a local 3D model file that already exists on disk (e.g. an FBX/OBJ/glTF exported from Blender or another DCC tool) into the Unity project. The file is copied under Assets/ and run through Unity's model-import pipeline (scale-normalize, material settings; glTF requires glTFast). Carries no API keys and no file bytes over the bridge.

Params: source_path (absolute or Assets-relative path to a .fbx/.obj/.glb/.gltf/.zip), name, output_folder (under Assets/), target_size. Returns { asset_path, asset_guid }.

For multi-file exports (a text .gltf with an external .bin, or an .obj with a sibling .mtl/textures), zip them and pass the .zip — a bare .gltf/.obj is copied without its sidecars.

## Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `source_path` | `str` | yes | Path to the model file on disk (.fbx/.obj/.glb/.gltf/.zip). |
| `name` | `str \| None` | — | Base name for the imported asset. |
| `output_folder` | `str \| None` | — | Destination folder under Assets/ for the import. |
| `target_size` | `float \| None` | — | Normalize the largest dimension to this size (meters). |

## Returns

A `dict` containing the Unity response. The exact shape depends on the action.

## Examples

<!-- examples:start -->
*No examples yet. Add usage examples here — they will be preserved across regenerations.*
<!-- examples:end -->

