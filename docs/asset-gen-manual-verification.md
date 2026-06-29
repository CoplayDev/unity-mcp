# AI Asset Generation — Manual Verification Checklist

The `asset_gen` tools call real third-party APIs and write real files into a licensed
Unity Editor, so they **cannot be covered headlessly**. Run this checklist by hand with
genuine provider keys and an interactive Editor before shipping.

## Prerequisites

- [ ] A licensed Unity Editor with the package installed and the bridge connected.
- [ ] Enable the group: `manage_tools` → enable `asset_gen` (it is off by default).
- [ ] Open **Window → MCP for Unity → Asset Gen** tab to enter provider keys
      (stored in the OS secure store — Keychain / Windows Credential Manager / libsecret).

## Tripo (default 3D, text→3D)

- [ ] Enter the Tripo key in the **Asset Gen** tab.
- [ ] `generate_model(provider=tripo, mode=text, prompt="a low-poly oak tree", format=fbx)`.
- [ ] Poll `generate_model(action=status, job_id=<id>)` until it reports done.
- [ ] Confirm an FBX appears under `Assets/Generated/Models/`.
- [ ] Confirm it imports cleanly **with materials**.

## glTFast / GLB

- [ ] Install **glTFast** from the **Dependencies** tab.
- [ ] `generate_model(provider=tripo, mode=text, prompt="...", format=glb)`, poll status.
- [ ] Confirm the GLB imports correctly (no missing-importer error).

## fal.ai (default 2D image)

- [ ] Enter the fal key.
- [ ] `generate_image(provider=fal, prompt="a pixel-art coin", transparent=true)`.
- [ ] Confirm a PNG sprite under `Assets/Generated/Images/` with **alpha** preserved
      and **correct sRGB** color.

## OpenRouter (2D image)

- [ ] Enter the OpenRouter key.
- [ ] `generate_image(provider=openrouter, prompt="...")`.
- [ ] Confirm the inline-image path works (image bytes decode and import as a sprite).

## Sketchfab (3D import)

- [ ] Enter the Sketchfab token.
- [ ] `import_model(action=search, query="wooden chair")`, then
      `import_model(action=import, uid=<from search>)`.
- [ ] Confirm the downloaded zip extracts and the model imports.
- [ ] Confirm the **path-traversal guard** holds (no files written outside the target dir).

## Meshy (3D)

- [ ] Enter the Meshy key.
- [ ] `generate_model(provider=meshy, mode=text, prompt="...")`, poll status.
- [ ] Confirm the model imports.

## Image input & provider params (verify the post-review fixes)

These paths are covered by unit tests at the request-shaping layer only — confirm them against
**real provider APIs** (the unit tests can't validate that the provider accepts the shape).

- [ ] **Meshy text→3D textures:** `generate_model(provider=meshy, mode=text, prompt="...", texture=true)`
      → confirm the result is **textured** (Meshy runs a preview then a refine task internally).
- [ ] **Local image→3D (Meshy):** `generate_model(provider=meshy, mode=image, image_path=Assets/refs/x.png)`
      → confirm it imports a model derived from the local image.
- [ ] **Local image→image (fal):** `generate_image(provider=fal, mode=image, image_path=Assets/refs/x.png, prompt="make it night")`
      → confirm fal's `/edit` endpoint accepts the request and returns an edited image.
- [ ] **Local image→image (OpenRouter):** `generate_image(provider=openrouter, mode=image, image_path=..., prompt="...")`
      → confirm the reference image influences the result.
- [ ] **fal output size:** `generate_image(provider=fal, width=512, height=512, prompt="...")`
      → confirm the returned image is 512×512.
- [ ] **Tripo local image is rejected clearly:** `generate_model(provider=tripo, mode=image, image_path=...)`
      → confirm the job fails with a clear "Tripo requires a hosted image_url" message (no silent text fallback).
- [ ] **Sketchfab search filters/paging:** `import_model(action=search, query="chair", categories=furniture-home, count=12, cursor=<from prior cursors.next>)`
      → confirm filtering works and `cursors.next` advances the page.
- [ ] **Transparency is import-only:** `generate_image(transparent=true)` sets the Unity alpha-is-transparency
      flag but does NOT produce a transparent background (fal/FLUX limitation) — confirm the expectation.

## Multi-agent / security spot-check

- [ ] Confirm no key value ever appears in MCP tool output.
- [ ] Confirm no key value appears in logs.
- [ ] Confirm no key value appears in the job `status` payload.
- [ ] Confirm no key value is committed to git.
