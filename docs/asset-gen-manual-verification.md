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

## Multi-agent / security spot-check

- [ ] Confirm no key value ever appears in MCP tool output.
- [ ] Confirm no key value appears in logs.
- [ ] Confirm no key value appears in the job `status` payload.
- [ ] Confirm no key value is committed to git.
