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

## Dynamic fal model catalog

- Open Asset Generation: cached choices appear immediately, and fal image/audio catalogs refresh in the background after 24 hours. Refresh forces a fetch. Tripo, Meshy and OpenRouter remain bundled in this version.
- Run `generate_audio(action="list_models")` or `generate_image(action="list_models", provider="fal")`. The response includes `models` and `catalogs` with source, verification time, staleness, errors and refresh progress. If `refreshing` is true, query `list_models` again later. `refresh_models` forces a background fetch. CLI equivalent: `unity-mcp asset-gen list-models --kind audio --refresh`.
- Discovery uses the public [fal model search API](https://fal.ai/docs/platform-apis/v1/models), optionally attaching the locally configured fal key for higher rate limits. A complete metadata fetch is followed by OpenAPI checks for a bounded shortlist (known models, saved selection, vendor highlights, then five recent candidates). Recency is not a quality benchmark. Speech/vector endpoints, unsupported required inputs and unknown output shapes are excluded.
- Each kind commits independently after its pages/schema batches succeed. Simulate a timeout/429 or malformed response: the previous successful snapshot and its original timestamp must remain. Automatic failed fetches back off for two minutes; manual Refresh retries immediately. Successful empty snapshots must not restore removed bundled entries.
- Select an audio model using `text` instead of `prompt`, fractional seconds or milliseconds: verify the adapter uses the live field names, units and limits. Image editing is offered only when the exact `/edit` endpoint has a compatible schema. Unsupported editing and dimensions return an error.
- Save a model selection, then make it unavailable in a fake catalog: the panel must preserve the saved ID and ask for another selection; generation must not silently switch models. Automatic refresh must preserve unsaved API-key text.
- Before each fal generation, a free exact-endpoint query verifies status and captures the current schema. An unavailable/incompatible endpoint or a failed verification must fail the job before a paid submit. This does not run paid generation probes or measure output quality.
- Cache lives in the project's ignored `Library/MCPForUnity/fal-model-catalog.json`; it contains no provider credentials. Delete/corrupt the cache and reopen: bundled entries appear as unverified until discovery succeeds.
- Requests are serialized and paced, and HTTP 429 responses get bounded retries; a failed fetch still preserves the cache. For a real, unpaid integration check, set `MCPFORUNITY_RUN_LIVE_CATALOG=1` and explicitly run `FalModelCatalogTests.LivePublicCatalog_RefreshAndExactEndpointVerification` in EditMode. Regular runs exclude this network test.

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
