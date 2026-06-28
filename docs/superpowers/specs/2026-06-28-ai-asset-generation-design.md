# AI Asset Generation for Unity-MCP — Design Spec

- **Status:** Approved (greenlit 2026-06-28)
- **Branch / worktree:** `feat/3d-asset-generation` at `.worktrees/3d-asset-generation`
- **Author:** Claude Code (brainstormed with @Scriptwonder)
- **Supersedes discussion:** research workflow `wf_f5c9a569-1d9` (3D + Unity-MCP arch) + 2D image-gen survey

## 1. Summary

Add an **AI Asset Generation** capability to MCP for Unity: users bring their own
provider API keys, and the plugin runs **3D model generation**, **3D marketplace
import**, and **2D image generation** itself, importing results straight into the
Unity project.

Modeled on BlenderMCP's "keys live in the editor, the MCP server holds nothing"
security model, adapted to Unity's AssetDatabase import model and Unity-MCP's
Python↔C# domain symmetry.

Two front doors, one engine:

- **GUI tab** (`Asset Generation`) — **configuration only**: enter/validate provider
  keys (stored in the OS secure store), toggle providers, see recent-job status.
  **No generation is triggered from the GUI.**
- **MCP tools + CLI** — the **only** way to trigger generation. The Python tool is a
  thin pass-through (no key, no bytes); the **C# side executes the provider HTTPS
  call**, downloads the result, and imports it.

## 2. Goals / Non-goals

### Goals
- One coherent `asset_gen` tool group covering 3D generation, 3D marketplace import,
  and 2D image generation, **off by default** (enabled via `manage_tools`, like vfx/animation).
- Bring-your-own-key with **strong at-rest key security** (OS secure store, never plaintext).
- Keys never leave the editor process, never cross the bridge, never appear in tool
  output, logs, job records, or committed files.
- Async, domain-reload-safe job lifecycle (submit → poll `status` → import), reusing
  the proven `PackageJobManager`/SessionState pattern.
- Heavy bytes (models/images) never traverse the MCP bridge — C# downloads via
  `UnityWebRequest` directly into `Assets/`.
- Correct Unity import: `ModelImporter` (scale/materials/rig) for 3D; `TextureImporter`
  (Sprite/Default, alpha, sRGB-vs-linear) for 2D.
- Full domain symmetry: every C# `[McpForUnityTool]` mirrored by a Python
  `@mcp_for_unity_tool` and a Click CLI command, with tests on both sides.
- Accessible first-run: curated best/most-accessible providers; graceful, instructive
  errors when an optional dependency (glTFast) or key is missing.

### Non-goals (v1)
- No GUI "Generate" button / prompt box (generation is MCP/CLI-only, by request).
- No Stability AI integration (dropped by request).
- No full 2D PBR-material auto-assembly (deferred until a PBR-capable 2D provider like
  fal PATINA / Scenario is added; v1 imports flat albedo / sprites with correct settings).
- No self-hosted Hunyuan (`LOCAL_API`) mode in v1 (official Tencent Cloud API only).
- No animation/rig retargeting beyond setting `ModelImporter.animationType`.

## 3. Providers (v1)

### 3D — generative (async submit → poll → download)
| Provider | Auth | Modalities | Formats | Notes |
|----------|------|-----------|---------|-------|
| **Tripo3D** ⭐ default | Bearer `tsk_` | text→3D, image→3D, multiview | GLB (native), FBX/OBJ/USDZ via convert | Best free tier; `POST /v2/openapi/task`, `GET /v2/openapi/task/{id}` |
| **Meshy** | Bearer | text→3D (preview→refine), image→3D | GLB/FBX/OBJ/USDZ | `api.meshy.ai/openapi/v2`; API needs user's Pro key |
| **Hunyuan3D** (Tencent) | SecretId+SecretKey, **TC3-HMAC-SHA256** | text→3D, image→3D | GLB/OBJ in ZIP | Heaviest adapter; `ai3d.tencentcloudapi.com` Submit/Query Job |

### 3D — marketplace (search → preview → import; not generative)
| Provider | Auth | Flow |
|----------|------|------|
| **Sketchfab** | `Authorization: Token` | `GET /v3/search` → `GET /v3/models/{uid}` (thumb) → `GET /v3/models/{uid}/download` → signed glTF zip → extract+import |

### 2D — image (aggregator; single general key)
| Provider | Auth | Notes |
|----------|------|-------|
| **fal.ai** ⭐ default | `Authorization: Key` | `POST queue.fal.run/{model}` → `request_id` → poll → result. Model id configurable (default FLUX). Background-removal + (future) PATINA PBR reachable as model slugs. |
| **OpenRouter** | Bearer | Unified key; image-capable multimodal models (e.g. `google/gemini-2.5-flash-image`). Default model configurable. |

All providers sit behind **one async-job abstraction** so providers are adapters, not
rewrites, and future ones (Rodin, Scenario, Replicate, fal PATINA) drop in cleanly.
Sketchfab's synchronous search and fal/OpenRouter's sync paths collapse to no-poll cases.

## 4. Architecture

```
Unity "Asset Generation" tab ──(write keys)──> ISecureKeyStore (OS Keychain/CredMan/libsecret)
                                                       ▲ read at call-time, C# only
AI agent / CLI ──> generate_model | import_model | generate_image   (Python MCP tool / Click CLI)
                       │  thin pass-through: NO key, NO bytes — only {action, provider, params, job_id}
                       ▼  bridge (WebSocket hub / legacy TCP)
C# HandleCommand ──> AssetGenJobManager.StartJob(...)  (GUID job, SessionState-persisted)
                       │  returns { job_id } immediately
                       ▼
ProviderAdapter (UnityWebRequest) ── submit ──> poll ──> download to Assets/Generated/...
                       ▼
ImportPipeline ── ModelImporter / TextureImporter ── (optional normalize) ──> { assetPath, guid }
                       ▼
status action (job_id) ──> { state, progress, assetPath | error }   (key-free, redacted)
```

**Why C# executes the provider call:** it is the only design that simultaneously
honors (a) keys entered in the GUI, (b) keys never leaving the editor / never crossing
the bridge, and (c) heavy bytes off the bridge + 64 MB-frame-cap-proof + transport
agnostic (local stdio, local HTTP, remote-hosted HTTP all behave identically).

### 4.1 Components (new code)

| Component | Path | Responsibility |
|-----------|------|----------------|
| Secure key store | `MCPForUnity/Editor/Security/SecureKeyStore/*` | Cross-platform at-rest key storage; redaction helpers |
| Provider adapters | `MCPForUnity/Editor/Services/AssetGen/Providers/*` | One class per provider; submit/poll/download; auth |
| Job manager | `MCPForUnity/Editor/Services/AssetGen/AssetGenJobManager.cs` | GUID jobs, SessionState, `EditorApplication.update` completion (mirror `PackageJobManager`) |
| Import pipeline | `MCPForUnity/Editor/Services/AssetGen/Import/{ModelImportPipeline,ImageImportPipeline}.cs` | Write to `Assets/`, drive importers, normalize |
| C# tools | `MCPForUnity/Editor/Tools/AssetGen/{GenerateModel,ImportModel,GenerateImage}.cs` | `[McpForUnityTool(..., Group="asset_gen", RequiresPolling=true)]` |
| GUI section | `MCPForUnity/Editor/Windows/Components/AssetGen/McpAssetGenSection.{cs,uxml}` | Config-only tab (keys, toggles, Test, glTFast notice, recent jobs) |
| Deps row | edit `MCPForUnityEditorWindow.cs` `BuildDependenciesSection` | Add glTFast (`com.unity.cloud.gltfast`) optional-dependency row |
| Python tools | `Server/src/services/tools/{generate_model,import_model,generate_image}.py` | Pass-through `@mcp_for_unity_tool(group="asset_gen")` |
| CLI | `Server/src/cli/commands/asset_gen.py` | Click group mirroring the tools |
| Tests | `Server/tests/test_asset_gen_*.py`, `TestProjects/UnityMCPTests/Assets/Tests/EditMode/AssetGen/*` | Both sides |

### 4.2 Tool surface

`generate_model` (providers: tripo, meshy, hunyuan)
- `action: generate` — `provider, mode(text|image), prompt?, imagePath?|imageUrl?, format(glb|fbx|obj|usdz), targetSize?, texture?, tier?, name?, outputFolder?` → `{ job_id }`
- `action: status` — `job_id` → `{ state, progress, assetPath?, error? }`
- `action: cancel` — `job_id`
- `action: list_providers` — `{ providers:[{id, configured, capabilities}] }` (no key values)

`import_model` (provider: sketchfab)
- `action: search` — `query, categories?, downloadable?, count?, cursor?` → results + uids
- `action: preview` — `uid` → base64 thumbnail (preview-before-import, BlenderMCP-style)
- `action: import` — `uid, targetSize?, name?, outputFolder?` → `{ job_id }` (download+import async)
- `action: status` / `cancel` / `list_providers`

`generate_image` (providers: fal, openrouter)
- `action: generate` — `provider, mode(text|image), prompt?, imagePath?|imageUrl?, model?, transparent?, width?, height?, name?, outputFolder?` → `{ job_id }` (sync providers resolve immediately)
- `action: remove_background` — `imagePath` → `{ job_id }` (fal BiRefNet/rembg)
- `action: status` / `cancel` / `list_providers`

Default output folders: `Assets/Generated/Models/`, `Assets/Generated/Sketchfab/`,
`Assets/Generated/Images/`. Name collisions get a numeric suffix.

## 5. Security design (key handling)

The user's explicit requirement: keys must be safely stored, resistant to theft.

### 5.1 At-rest storage — `ISecureKeyStore`
```
bool TryGet(string providerId, out string apiKey);
void Set(string providerId, string apiKey);
void Delete(string providerId);
bool Has(string providerId);   // existence only; never returns the value
```
Service namespace: `MCPForUnity.AssetGen`. One factory `SecureKeyStore.Current` selects:

- **macOS** — Keychain via `/usr/bin/security` generic passwords
  (`add-generic-password -U -s MCPForUnity.AssetGen -a <provider> -w <key>`, `find-…`, `delete-…`).
- **Windows** — Credential Manager via `advapi32` P/Invoke `CredWrite/CredRead/CredDelete`
  (`CRED_TYPE_GENERIC`, target `MCPForUnity.AssetGen:<provider>`), DPAPI-backed by the OS.
- **Linux** — `secret-tool` (libsecret) when present; otherwise `EncryptedFileKeyStore`.
- **Fallback** `EncryptedFileKeyStore` — AES-256-GCM, key derived from a per-user random
  salt (generated once, stored in the user profile dir, `chmod 600`) combined with a
  machine identifier; ciphertext under the user app-data dir, **never** under `Assets/`
  or the repo. Documented as weaker than an OS store.
- **Env override** (read-only, never persisted) — `MCPFORUNITY_<PROVIDER>_API_KEY` for
  CI/headless and power users. Resolution order: env → secure store.

Multi-secret providers (Hunyuan SecretId+SecretKey) store a JSON blob under one entry.

### 5.2 In-use hygiene ("no behavior to steal the key")
- Key read into a local variable **only** at the moment of the HTTP call; not cached on
  job records, not held in static fields, cleared promptly.
- **Redaction helper** (`SecretRedactor`) applied on every log/error path; keys never
  logged, never echoed. Provider error bodies are scrubbed before surfacing.
- **Never serialized** into `AssetGenJob` records (which persist to SessionState) — jobs
  hold provider id + params + status only.
- **Never crosses the bridge**: Python/CLI payloads carry no key material; the C# side
  reads from the store. `list_providers`/`get_status` expose only `configured: bool`.
- **No "read key" action** exists in any tool; the agent can trigger generation but can
  never retrieve a key value.
- **Never written** to project files, `ConfigJsonBuilder` output, `.meta`, or anything
  git-tracked.
- **Test/validate** buttons hit a cheap provider auth endpoint (Tripo balance, Sketchfab
  `/v3/me`, Meshy/fal/OpenRouter account ping) and report only success/failure.

### 5.3 Threat model
- **Protects against:** local plaintext disclosure (plist/registry), accidental git
  commit of keys, leakage over the MCP bridge / into agent-visible output / into logs.
- **Residual (documented):** code running as the same OS user with the editor's
  entitlements — notably the `execute_code` tool (arbitrary C# in-process) — can ask the
  OS store for an item, exactly as the editor can. We mitigate by exposing no generic
  key-read API, recommending least-privilege provider keys + easy revocation, and a UI
  note. This is strictly better than today's plaintext `EditorPrefs.ApiKey`.

EditorPrefs is still used for **non-secret** asset-gen config (selected provider,
default format, output folder, enabled toggles) under new `EditorPrefKeys` consts
(`MCPForUnity.AssetGen.*`).

## 6. Import pipeline

### 6.1 3D — `ModelImportPipeline`
- Download to `Assets/Generated/Models/<name>.<ext>` (UnityWebRequest, streamed to disk).
- `AssetDatabase.ImportAsset(path, ForceUpdate)`; then drive **`ModelImporter`**
  (greenfield — patterned on the `TextureImporter` branch in `ManageAsset.ModifyAsset`):
  `globalScale`, `useFileScale`, `importMaterials`/`materialImportMode`,
  `animationType`, then `WriteImportSettingsIfDirty` + reimport.
- **GLB/glTF** requires **glTFast** (`com.unity.cloud.gltfast`), an optional Deps-tab
  dependency. If absent and a provider returns GLB: fail the job with an actionable
  message ("Install glTFast from the Dependencies tab, or choose FBX output"). Prefer FBX
  when the provider offers it.
- ZIP outputs (Hunyuan, Sketchfab): extract with path-traversal guard (reject `..`,
  verify `abspath` stays within the temp dir), locate the model entry, import.
- **Auto-normalize** (default on, configurable): compute combined bounds, uniformly
  scale root so the largest dimension == `targetSize` (default 1m), optional single-mesh
  cleanup (analogue of BlenderMCP `_clean_imported_glb`).

### 6.2 2D — `ImageImportPipeline`
- Write/decode to `Assets/Generated/Images/<name>.png` (decode base64 in-process where
  the provider returns it; else download; download expiring URLs immediately).
- `TextureImporter`: `textureType` Sprite (`alphaIsTransparency=true`, `mipmapEnabled=false`)
  for sprites/icons vs Default; **sRGB for color maps, linear for normal/roughness/metallic**;
  `NormalMap` type for normals; pixel-art → `filterMode=Point`, uncompressed.
- (Deferred) PBR-set → Unity `Material` assembly with correct map slots + smoothness
  inversion, once a PBR-capable provider is added.

## 7. Async job lifecycle

Mirror `ManagePackages` + `PackageJobManager`:
- Tools declared `[McpForUnityTool(..., RequiresPolling = true, PollAction = "status", MaxPollSeconds = 300)]`.
- `generate`/`import` mint a GUID job, persist to **`SessionState`** (survives the
  domain reload that AssetDatabase import triggers — `TryRecoverJob`, `DomainReloadTimeoutMs`),
  return `{ job_id }` immediately.
- A completion callback on `EditorApplication.update` advances the provider poll +
  download + import, then `CompleteJob`.
- `status` returns `{ state: queued|running|importing|done|failed|canceled, progress,
  assetPath?, error? }`.

## 8. Domain symmetry & registration

- C# auto-discovered by `CommandRegistry` reflection via `[McpForUnityTool]`; command
  name string must equal the Python tool's send name.
- New tool group `asset_gen` added to the group enum/registry on both sides, **disabled
  by default**, toggled by `manage_tools` (parity with vfx/animation).
- Python tools strip `None` params, camelCase keys to match C# `ToolParams`.
- CLI commands use `@handle_unity_errors` + HTTP `run_command`.

## 9. Testing strategy

- **Python (pytest):** tool param mapping, action routing, status/job_id pass-through,
  error shaping — provider HTTP fully mocked. Files `Server/tests/test_asset_gen_*.py`.
- **C# EditMode (`TestProjects/UnityMCPTests`):**
  - `ISecureKeyStore` round-trip (set/get/delete/has) against the fallback
    `EncryptedFileKeyStore` (deterministic, CI-safe); redaction helper; env-override.
  - `AssetGenJobManager` lifecycle incl. simulated domain-reload recovery.
  - Provider adapters against a stub `IHttpClient` (inject a fake transport; assert
    request shape, auth header presence **without** asserting key value, poll loop,
    error handling). TC3-HMAC signer has a known-vector unit test.
  - Import pipeline: import a tiny fixture FBX/PNG, assert importer settings + normalize.
  - **Key never leaks**: assert job records / status payloads / logs contain no key.
- **Cross-version:** run `tools/check-unity-versions.sh` for any `#if UNITY_*`/shimmed
  API; route fragile `ModelImporter`/`UnityWebRequest`/UIToolkit calls through
  `Unity*Compat` shims per CLAUDE.md.
- **Definition of "test ready":** Python tests green; C# compiles across the CI matrix;
  EditMode tests authored; provider calls exercised against mocks. Live provider calls
  + live Unity import verified manually by the user (needs real keys + a licensed editor).

## 10. Risks & mitigations

| Risk | Mitigation |
|------|-----------|
| Key theft / leakage | OS secure store, redaction, no key over bridge / in output / in job records / in git; documented `execute_code` residual |
| GLB unimportable (no built-in importer) | glTFast optional Deps dep; prefer FBX; actionable error when missing |
| Domain reload during import kills polling | SessionState-persisted jobs (`PackageJobManager` pattern) |
| Large files over bridge (64 MB cap) | C# downloads to `Assets/` directly; only JSON crosses the bridge |
| TC3-HMAC signing complexity (Hunyuan) | Isolated `TencentSigner` with known-answer test; sequenced after Tripo/Meshy |
| Expiring result URLs (fal/Sketchfab/BFL) | Download immediately inside the job |
| Provider rate-limit / cost / failures | Default cheapest tier; backoff; surface quota/failed-job (often refunded) via `status` |
| Unity version drift (2021→6.x→CoreCLR) | `Unity*Compat` shims + `tools/check-unity-versions.sh` |
| GUI/MCP code-path divergence | GUI is config-only; the single C# handler is the only generation path |

## 11. Implementation phases (refined in the implementation plan)

0. **Scaffold** — `asset_gen` group on both sides; `EditorPrefKeys.AssetGen.*`; folders; package wiring.
1. **SecureKeyStore** — interface + platform impls + fallback + redactor + tests. *(security-first)*
2. **Provider abstraction + Tripo** — adapter interface; Tripo end-to-end (submit/poll/download) on a stub HTTP transport + tests.
3. **Job manager + C# `generate_model` + ModelImportPipeline** — SessionState jobs; FBX import first, GLB via glTFast.
4. **Python `generate_model` + CLI + tests** — pass-through, mocked.
5. **GUI `Asset Generation` tab** — config-only; keys via SecureKeyStore; toggles; Test; glTFast notice; recent-jobs readout.
6. **Meshy + Hunyuan (TC3-HMAC) + Sketchfab `import_model`**.
7. **2D `generate_image`** — fal.ai + OpenRouter adapters + ImageImportPipeline.
8. **Deps glTFast row + docs + version-compat sweep + full test pass**.

## 12. Open follow-ups (post-v1)
- 2D PBR-material auto-assembly (fal PATINA / Scenario / Leonardo).
- Aggregator-routed 3D (Hunyuan/Rodin/Tripo via fal/Replicate) as an alternative path.
- Hunyuan `LOCAL_API` self-hosted mode.
- Rodin (incl. the `vibecoding` free-trial key) as a premium 3D provider.
