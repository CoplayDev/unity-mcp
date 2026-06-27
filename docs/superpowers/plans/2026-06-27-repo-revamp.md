# Repo Revamp (Brand, Distribution, Analytics) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Standardize MCP for Unity's brand, rebuild the README into a real front door, and add a unified PyPI-installs + docs-traffic analytics dashboard — as one phased PR to `CoplayDev:beta`.

**Architecture:** Three phases in one PR. P1 establishes a single evolved brand (assets + canonical name) and sweeps it across every surface. P2 fills the hollow English README and tightens distribution metadata, consuming P1's assets. P3 adds a cookieless analytics pipeline (pypistats + GoatCounter → a scheduled GitHub Action → `data.json` → a Docusaurus `/stats` page). P3 is independent of P1/P2.

**Tech Stack:** Docusaurus 3.x (Node/JSX), Unity C# (Editor), Python `pyproject.toml`, GitHub Actions, SVG + `sharp` for asset generation, GoatCounter (cookieless web analytics), pypistats.org API.

## Global Constraints

Every task's requirements implicitly include these (verbatim from the spec):

- **Destination:** upstream PR to `CoplayDev:beta` on branch `revamp/brand-distribution-analytics`. Single PR, phased commits.
- **Preserve sponsor branding everywhere:** Coplay (`manifest.json` icon `coplay-logo.png`, footer, author fields) **and** Aura (README sponsor line, footer). Never remove or alter attribution.
- **Canonical user-facing name:** `MCP for Unity` (lowercase `f`). Leave internal identifiers unchanged: namespace `MCPForUnity`, UPM id `com.coplaydev.unity-mcp`, PyPI `mcpforunityserver`, CLI verb `UnityMCP`. **Keep** `manifest.json` `"name": "Unity MCP"` (registry identifier).
- **Brand = evolve:** keep indigo `#4f46e5` (light) / `#818cf8` (dark) and **Satoshi + JetBrains Mono** typography. Refine the mark, not a rebrand.
- **Unity range:** 2021.3 LTS → Unity 6.x. C# changes compile across the CI matrix — run `tools/check-unity-versions.sh` (needs local Unity Hub editors; if unavailable, note it and rely on careful review).
- **Docs build is the gate:** `npm run build` in `website/` must pass (`onBrokenLinks: 'throw'`).
- **Analytics:** cookieless, no PII, free tier, only aggregates committed to the repo.
- **Asset sync:** keep `docs/images/logo.png` and `website/static/img/logo.png` identical. Unity `.meta` files travel with any added/renamed asset under `MCPForUnity/`.
- **User review gates:** the user reviews at each phase boundary, and reviews the Task 3 C# diff specifically before it's finalized.
- **Per CLAUDE.md:** tests for new behavior; don't add comments/docstrings to code you didn't change; delete rather than deprecate.

---

# Phase P1 — Brand system & standardization

### Task 1: Produce & select an evolved brand mark (interactive gate)

**Files:**
- Create: `website/static/img/logo-mark.svg` (light, finalized from chosen concept)
- Create: `website/static/img/logo-mark-dark.svg` (dark variant)
- Create: `website/static/img/logo-full.svg` (wide hero lockup: mark + "MCP for Unity")

**Interfaces:**
- Produces: the canonical SVG sources every later asset (favicon, social card, package icon) is generated from in Task 2.

- [ ] **Step 1: Author 2–3 evolved mark concepts.** Each as an SVG using the indigo palette (`#4f46e5`/`#818cf8`) and `currentColor` for theme-derivable strokes (not hardcoded `#0a0a0b`/`#fafafa`). Keep each readable at 16px (square crop) and as a wide 40×16-ish lockup. Save concepts to a scratch dir (e.g. `website/static/img/_concepts/`).

- [ ] **Step 2: Show concepts side-by-side for selection.** Offer the brainstorming **visual companion** (its own message) and render the concepts in the browser tab for comparison; if declined, present them as inline SVG files the user opens. **GATE: wait for the user to pick one direction.**

- [ ] **Step 3: Finalize the chosen direction** into `logo-mark.svg`, `logo-mark-dark.svg`, and `logo-full.svg`. Delete the `_concepts/` scratch dir (delete, don't leave dead assets).

- [ ] **Step 4: Commit**

```bash
git add website/static/img/logo-mark.svg website/static/img/logo-mark-dark.svg website/static/img/logo-full.svg
git commit -m "feat(brand): evolved MCP for Unity logo mark (light/dark + full lockup)"
```

---

### Task 2: Generate the raster/favicon asset set from the SVG

**Files:**
- Create: `website/scripts/gen-brand-assets.mjs` (one-off generator)
- Modify: `website/package.json` (add `sharp` devDependency + a `gen:brand` script)
- Create (generated): `website/static/img/favicon.ico`, `favicon-32.png`, `apple-touch-icon.png` (180), `android-chrome-192.png`, `android-chrome-512.png`, `social-card.png` (1200×630), `github-social-preview.png` (1280×640), `logo.png` (2048×1152 hero)
- Create (generated): `MCPForUnity/Editor/Resources/PackageIcon.png` (128×128) + its `.meta`
- Modify: `docs/images/logo.png` (sync copy of the website hero)

**Interfaces:**
- Consumes: the SVG sources from Task 1.
- Produces: every brand raster consumed by Task 4 (docs config, package icon) and Task 6 (README).

- [ ] **Step 1: Add `sharp` and a script** to `website/package.json`:

```jsonc
// in "devDependencies"
"sharp": "^0.33.0"
// in "scripts"
"gen:brand": "node scripts/gen-brand-assets.mjs"
```

- [ ] **Step 2: Write `website/scripts/gen-brand-assets.mjs`** that rasterizes the SVGs to the exact sizes:

```js
import sharp from 'sharp';
import { mkdir, copyFile } from 'node:fs/promises';
import path from 'node:path';

const IMG = path.join(process.cwd(), 'static', 'img');
const mark = path.join(IMG, 'logo-mark.svg');
const full = path.join(IMG, 'logo-full.svg');

const png = (src, size, out) =>
  sharp(src, { density: 384 }).resize(size, size, { fit: 'contain', background: { r: 0, g: 0, b: 0, alpha: 0 } }).png().toFile(path.join(IMG, out));

const wide = (src, w, h, out) =>
  sharp(src, { density: 384 }).resize(w, h, { fit: 'contain', background: { r: 0, g: 0, b: 0, alpha: 0 } }).png().toFile(path.join(IMG, out));

await mkdir(IMG, { recursive: true });
await png(mark, 32, 'favicon-32.png');
await png(mark, 180, 'apple-touch-icon.png');
await png(mark, 192, 'android-chrome-192.png');
await png(mark, 512, 'android-chrome-512.png');
await wide(full, 2048, 1152, 'logo.png');
await wide(full, 1200, 630, 'social-card.png');
await wide(full, 1280, 640, 'github-social-preview.png');
// Unity package icon (square mark)
await mkdir(path.join(process.cwd(), '..', 'MCPForUnity', 'Editor', 'Resources'), { recursive: true });
await png(mark, 128, path.join('..', '..', 'MCPForUnity', 'Editor', 'Resources', 'PackageIcon.png').replace(/\\/g, '/'));
console.log('Brand assets generated.');
```

- [ ] **Step 3: Generate `favicon.ico`** (sharp doesn't emit multi-size `.ico`; use a one-off):

```bash
cd website && npm install && npm run gen:brand
npx -y png-to-ico static/img/favicon-32.png > static/img/favicon.ico
```

- [ ] **Step 4: Sync the README hero copy + verify dimensions**

```bash
cp website/static/img/logo.png docs/images/logo.png
sips -g pixelWidth -g pixelHeight website/static/img/social-card.png   # expect 1200 x 630
sips -g pixelWidth -g pixelHeight website/static/img/apple-touch-icon.png  # expect 180 x 180
```
Expected: dimensions match the comments above. (In Unity, let the Editor import `PackageIcon.png` to generate its `.meta`, then include the `.meta`.)

- [ ] **Step 5: Commit**

```bash
git add website/package.json website/package-lock.json website/scripts/gen-brand-assets.mjs \
        website/static/img/*.png website/static/img/favicon.ico docs/images/logo.png \
        MCPForUnity/Editor/Resources/PackageIcon.png MCPForUnity/Editor/Resources/PackageIcon.png.meta
git commit -m "feat(brand): generate favicon set, social cards, hero + UPM package icon"
```

---

### Task 3: Canonical product-name constant + Editor casing standardization

**Files:**
- Create: `MCPForUnity/Editor/Constants/ProductInfo.cs`
- Modify: `MCPForUnity/Editor/MenuItems/MCPForUnityMenu.cs` (menu paths)
- Modify: `MCPForUnity/Editor/Windows/MCPForUnityEditorWindow.cs` (window title, ~lines 98,101)
- Modify: `MCPForUnity/Editor/Windows/MCPSetupWindow.cs` (window title)
- Modify: `MCPForUnity/Editor/Setup/McpForUnitySkillInstaller.cs` (window title)

**Interfaces:**
- Produces: `MCPForUnity.Editor.Constants.ProductInfo.ProductName` (`"MCP for Unity"`) and `ProductInfo.MenuRoot` (`"Window/MCP for Unity"`) — used by later user-facing strings.

> ⚠️ This is the diff the user explicitly reviews before it's finalized (decision §9.3). The only behavioral change is the Editor menu path casing (`MCP **F**or Unity` → `MCP for Unity`).

- [ ] **Step 1: Create the constant** `MCPForUnity/Editor/Constants/ProductInfo.cs`:

```csharp
namespace MCPForUnity.Editor.Constants
{
    /// <summary>Canonical user-facing product identity strings.</summary>
    public static class ProductInfo
    {
        public const string ProductName = "MCP for Unity";
        public const string MenuRoot = "Window/MCP for Unity";
    }
}
```

- [ ] **Step 2: Update menu paths** in `MCPForUnityMenu.cs`. Replace every `[MenuItem("Window/MCP For Unity/...")]` literal so the path reads `Window/MCP for Unity/...` (lowercase `f`). Use `ProductInfo.MenuRoot + "/Toggle MCP Window"` etc. where the attribute allows a const expression; otherwise change the literal directly. Add `using MCPForUnity.Editor.Constants;`.

- [ ] **Step 3: Update window titles.** In `MCPForUnityEditorWindow.cs`, `MCPSetupWindow.cs`, and `McpForUnitySkillInstaller.cs`, replace user-facing `"MCP For Unity"` strings with `ProductInfo.ProductName`. Add the `using` where needed. Leave any internal/registry strings (`"Unity MCP"`, `MCPForUnity` namespace) untouched.

- [ ] **Step 4: Compile-check across the matrix**

Run: `tools/check-unity-versions.sh`
Expected: compiles on every installed matrix editor. (If no local Unity Hub editors: state that, and hand the diff to the user for review instead.)

- [ ] **Step 5: Commit**

```bash
git add MCPForUnity/Editor/Constants/ProductInfo.cs MCPForUnity/Editor/MenuItems/MCPForUnityMenu.cs \
        MCPForUnity/Editor/Windows/MCPForUnityEditorWindow.cs MCPForUnity/Editor/Windows/MCPSetupWindow.cs \
        MCPForUnity/Editor/Setup/McpForUnitySkillInstaller.cs
git commit -m "refactor(brand): single ProductInfo constant; standardize Editor name to 'MCP for Unity'"
```

- [ ] **Step 6: PAUSE — user reviews this diff** before continuing (decision §9.3).

---

### Task 4: Wire the new brand into the docs site + package

**Files:**
- Modify: `website/docusaurus.config.js` (favicon, navbar logo, `themeConfig.image`, add `themeColor` meta)
- Modify: `MCPForUnity/package.json` (add `"icon"` field if the schema supports it; otherwise rely on `Editor/Resources/PackageIcon.png`)

**Interfaces:**
- Consumes: assets from Task 2.

- [ ] **Step 1: Update `docusaurus.config.js`.** Point `favicon: 'img/favicon.ico'`; confirm `navbar.logo.src: 'img/logo-mark.svg'` / `srcDark: 'img/logo-mark-dark.svg'`; set `themeConfig.image: 'img/social-card.png'`. Add a theme-color meta:

```js
// in themeConfig
metadata: [{ name: 'theme-color', content: '#4f46e5' }],
```

- [ ] **Step 2: Add the package icon reference** in `MCPForUnity/package.json` if Unity's package schema accepts an `icon` path; otherwise leave the `Editor/Resources/PackageIcon.png` from Task 2 in place (Package Manager picks up `Editor/Resources` icons). Do **not** alter `name`, `displayName`, or `author`.

- [ ] **Step 3: Build the site**

Run: `cd website && npm run build`
Expected: build succeeds (no broken links); favicon/logo/social-card resolve.

- [ ] **Step 4: Visual check** — `npm run serve`, confirm the navbar mark renders in light **and** dark mode and the favicon shows in the tab.

- [ ] **Step 5: Commit**

```bash
git add website/docusaurus.config.js MCPForUnity/package.json
git commit -m "feat(brand): wire favicon/social-card/theme-color into docs site + package icon"
```

> **PHASE P1 REVIEW GATE:** user reviews the full brand sweep before P2.

---

# Phase P2 — README + distribution front door

### Task 5: Expand the README badge row

**Files:**
- Modify: `README.md` (badge block, ~lines 8–15)

- [ ] **Step 1: Replace the badge row** with the expanded set (keep existing Docs/Discord/Website/Unity/Python/MCP/MIT, add the six below):

```markdown
[![PyPI version](https://img.shields.io/pypi/v/mcpforunityserver?label=PyPI)](https://pypi.org/project/mcpforunityserver/)
[![Downloads](https://static.pepy.tech/badge/mcpforunityserver)](https://pepy.tech/project/mcpforunityserver)
[![Release](https://img.shields.io/github/v/release/CoplayDev/unity-mcp)](https://github.com/CoplayDev/unity-mcp/releases)
[![CI](https://img.shields.io/github/actions/workflow/status/CoplayDev/unity-mcp/python-tests.yml?branch=beta&label=tests)](https://github.com/CoplayDev/unity-mcp/actions/workflows/python-tests.yml)
[![OpenUPM](https://img.shields.io/npm/v/com.coplaydev.unity-mcp?label=OpenUPM&registry_uri=https://package.openupm.com)](https://openupm.com/packages/com.coplaydev.unity-mcp/)
[![Stars](https://img.shields.io/github/stars/CoplayDev/unity-mcp?style=flat)](https://github.com/CoplayDev/unity-mcp/stargazers)
```

- [ ] **Step 2: Verify badges resolve** — open each shields URL in a browser (or `curl -sI`), confirm 200 / a rendered SVG. Confirm `python-tests.yml` is the correct workflow filename (`ls .github/workflows/`).

- [ ] **Step 3: Commit**

```bash
git add README.md && git commit -m "docs(readme): add PyPI/downloads/release/CI/OpenUPM/stars badges"
```

---

### Task 6: Add "What it does" + capabilities catalog

**Files:**
- Modify: `README.md` (insert after the demo gif, before Install)

- [ ] **Step 1: Insert a "What it does" block + grouped capabilities.** Pull tool groups from `website/docs/reference/tools/` (core, scripting_ext, vfx, ui, animation, testing, probuilder, profiling, docs):

```markdown
## What it does

Control the Unity Editor in natural language from any MCP client. Describe what
you want — "add a player with a rigidbody", "write a save system", "run the
tests" — and the assistant drives the Editor through 40+ focused tools.

- **Scenes & GameObjects** — create, query, modify hierarchies, components, prefabs
- **Scripts** — create/edit C# with Roslyn validation
- **Assets & rendering** — materials, shaders, textures, VFX
- **Build, test & profile** — run EditMode/PlayMode tests, the profiler, builds
- **Any MCP client** — Claude, Cursor, VS Code, Windsurf, Gemini CLI, and more
- **Free & MIT**, multi-instance ready

<details>
<summary><b>Full tool catalog (40+ tools across 9 groups)</b></summary>

See the [tool reference](https://coplaydev.github.io/unity-mcp/reference/tools/) for
every tool: **core** (scenes, GameObjects, scripts, assets, prefabs, components,
editor control, console, menus), **scripting_ext**, **vfx** (shaders, textures),
**ui**, **animation**, **testing**, **probuilder**, **profiling**, **docs**.
</details>
```

- [ ] **Step 2: Commit**

```bash
git add README.md && git commit -m "docs(readme): add what-it-does + grouped capabilities catalog"
```

---

### Task 7: Add supported clients & versions matrix

**Files:**
- Modify: `README.md` (insert after capabilities)

- [ ] **Step 1: Insert the matrix** (client list from `MCPForUnity/README.md` + `README-zh.md`):

```markdown
## Supported clients & versions

| MCP client | Auto-configure | Notes |
|---|---|---|
| Claude Desktop / Claude Code | ✅ | stdio |
| Cursor | ✅ | stdio |
| VS Code (Copilot) | ✅ | stdio |
| Windsurf | ✅ | stdio |
| Cline | ✅ | stdio |
| Gemini CLI / Qwen Code | ✅ | stdio |
| Copilot CLI / OpenClaw / Antigravity | ✅ | stdio |

**Requirements:** Unity **2021.3 LTS → Unity 6.x**, Python **3.10+** (managed via `uv`).
Use **Window → MCP for Unity → Configure All Detected Clients** to set up every detected client at once.
```

- [ ] **Step 2: Commit**

```bash
git add README.md && git commit -m "docs(readme): add clients + Unity/Python version matrix"
```

---

### Task 8: Expand quickstart + architecture diagram + positioning

**Files:**
- Modify: `README.md` (Install → "Quickstart"; add architecture + positioning)

- [ ] **Step 1: Expand the install section into a 60-second quickstart** with prerequisites and an expected result; keep Asset Store + OpenUPM in `<details>`:

```markdown
## 60-second quickstart

**Prerequisites:** Unity 2021.3 LTS+, an MCP client, and [uv](https://docs.astral.sh/uv/) (auto-installed if missing).

1. **Install the package** (Unity → Package Manager → Add from git URL):
   `https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#beta`
2. **Configure your client:** `Window → MCP for Unity → Configure All Detected Clients`.
3. **Prompt it:** *"Create a cube at the origin and add a Rigidbody."*
   You should see the cube appear in the scene within a couple of seconds.

<details>
<summary>Alternative installs (Asset Store, OpenUPM)</summary>

- **OpenUPM:** `openupm add com.coplaydev.unity-mcp`
- **Asset Store:** [MCP for Unity](https://assetstore.unity.com/packages/slug/329908)
</details>
```

- [ ] **Step 2: Add an architecture diagram** (mermaid; mirrors `CLAUDE.md`):

```markdown
## How it works

\`\`\`mermaid
flowchart LR
  A[AI assistant<br/>Claude · Cursor · …] -- MCP (stdio/HTTP) --> B[Python server]
  B -- WebSocket + HTTP --> C[Unity Editor plugin]
  C -- Editor API --> D[Scene · Assets · Scripts]
\`\`\`
```

- [ ] **Step 3: Add a short positioning block** (reuses the Aura framing):

```markdown
## How it compares

- **MCP for Unity** — free, MIT, MCP-native Editor control from any client.
- **[Aura for Unity](https://www.tryaura.dev/)** — premium in-Editor AI assistant (sponsor).
- **Hand-rolled Editor scripting** — full control, but you build and maintain everything.
```

- [ ] **Step 4: Build the docs site** (the README mermaid renders on GitHub; just confirm nothing else broke): `cd website && npm run build`. Expected: pass.

- [ ] **Step 5: Commit**

```bash
git add README.md && git commit -m "docs(readme): expand quickstart, add architecture + positioning"
```

---

### Task 9: PyPI metadata, EN↔zh re-sync, maintainer checklist

**Files:**
- Modify: `Server/pyproject.toml` (`[project.urls]`)
- Modify: `Server/README.md` (docs link + badges)
- Modify: `docs/i18n/README-zh.md` (re-sync structure with the new EN README)
- Create: `docs/MAINTAINER_ACTIONS.md` (the CoplayDev-gated checklist)

- [ ] **Step 1: Add project URLs** to `Server/pyproject.toml`:

```toml
[project.urls]
Homepage = "https://coplaydev.github.io/unity-mcp/"
Documentation = "https://coplaydev.github.io/unity-mcp/"
Repository = "https://github.com/CoplayDev/unity-mcp.git"
Issues = "https://github.com/CoplayDev/unity-mcp/issues"
Changelog = "https://github.com/CoplayDev/unity-mcp/releases"
```

- [ ] **Step 2: Point `Server/README.md`'s "Full Documentation" link** at `https://coplaydev.github.io/unity-mcp/` and add the PyPI version + downloads badges (same URLs as Task 5).

- [ ] **Step 3: Re-sync `README-zh.md`** so its section order matches the new EN README (it already has the richer content; align headings/order, keep translations). Preserve the EN/中文 switcher in both.

- [ ] **Step 4: Create `docs/MAINTAINER_ACTIONS.md`** listing the CoplayDev-gated items (verbatim from spec §10): GitHub social-preview upload (`website/static/img/github-social-preview.png`), repo topics/website, Asset Store listing art, PyPI publish with new metadata, provision GoatCounter token + secret + Actions write, optional MCP-registry claims.

- [ ] **Step 5: Validate** — `cd Server && uv run python -c "import tomllib,pathlib; tomllib.loads(pathlib.Path('pyproject.toml').read_text())"` (parses clean). Optionally `npx -y markdown-link-check README.md`.

- [ ] **Step 6: Commit**

```bash
git add Server/pyproject.toml Server/README.md docs/i18n/README-zh.md docs/MAINTAINER_ACTIONS.md
git commit -m "docs(distribution): PyPI urls/badges, re-sync zh README, maintainer checklist"
```

> **PHASE P2 REVIEW GATE:** user reviews README + distribution before P3.

---

# Phase P3 — Unified analytics dashboard

### Task 10: Stats fetch + transform logic (TDD)

**Files:**
- Create: `website/scripts/fetch-stats.mjs`
- Test: `website/scripts/fetch-stats.test.mjs`

**Interfaces:**
- Produces: `normalizePypiRecent(recent)` → `{lastDay, lastWeek, lastMonth}`; `buildStats({recent, webPageviews, generatedAt})` → the `data.json` shape `{generatedAt, pypi:{lastDay,lastWeek,lastMonth}, web: {total}|null}`.

- [ ] **Step 1: Write the failing test** `website/scripts/fetch-stats.test.mjs`:

```js
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { normalizePypiRecent, buildStats } from './fetch-stats.mjs';

test('normalizePypiRecent extracts day/week/month', () => {
  const recent = { data: { last_day: 100, last_week: 700, last_month: 3000 } };
  assert.deepEqual(normalizePypiRecent(recent), { lastDay: 100, lastWeek: 700, lastMonth: 3000 });
});

test('normalizePypiRecent handles missing data', () => {
  assert.deepEqual(normalizePypiRecent({}), { lastDay: 0, lastWeek: 0, lastMonth: 0 });
});

test('buildStats shapes the unified object', () => {
  const recent = { data: { last_day: 1, last_week: 2, last_month: 3 } };
  const stats = buildStats({ recent, webPageviews: { total: 42 }, generatedAt: '2026-06-27T00:00:00Z' });
  assert.equal(stats.generatedAt, '2026-06-27T00:00:00Z');
  assert.deepEqual(stats.pypi, { lastDay: 1, lastWeek: 2, lastMonth: 3 });
  assert.deepEqual(stats.web, { total: 42 });
});
```

- [ ] **Step 2: Run it, verify it fails**

Run: `node --test website/scripts/fetch-stats.test.mjs`
Expected: FAIL (`Cannot find module './fetch-stats.mjs'`).

- [ ] **Step 3: Implement `website/scripts/fetch-stats.mjs`:**

```js
export function normalizePypiRecent(recent) {
  const d = (recent && recent.data) || {};
  return { lastDay: d.last_day ?? 0, lastWeek: d.last_week ?? 0, lastMonth: d.last_month ?? 0 };
}

export function buildStats({ recent, webPageviews = null, generatedAt }) {
  return { generatedAt, pypi: normalizePypiRecent(recent), web: webPageviews };
}

async function main() {
  const pkg = 'mcpforunityserver';
  const recent = await (await fetch(`https://pypistats.org/api/packages/${pkg}/recent`)).json();
  let web = null;
  const token = process.env.GOATCOUNTER_TOKEN, site = process.env.GOATCOUNTER_SITE;
  if (token && site) {
    try {
      const r = await fetch(`https://${site}.goatcounter.com/api/v0/stats/total`, {
        headers: { Authorization: `Bearer ${token}` },
      });
      if (r.ok) { const j = await r.json(); web = { total: j.total ?? null }; }
    } catch { /* leave web=null on failure */ }
  }
  const stats = buildStats({ recent, webPageviews: web, generatedAt: new Date().toISOString() });
  const fs = await import('node:fs/promises');
  const path = await import('node:path');
  const out = path.join(process.cwd(), 'website', 'static', 'stats', 'data.json');
  await fs.mkdir(path.dirname(out), { recursive: true });
  await fs.writeFile(out, JSON.stringify(stats, null, 2) + '\n');
  console.log(`Wrote ${out}`);
}

import { fileURLToPath } from 'node:url';
if (process.argv[1] === fileURLToPath(import.meta.url)) {
  main().catch((e) => { console.error(e); process.exit(1); });
}
```

- [ ] **Step 4: Run the test, verify it passes**

Run: `node --test website/scripts/fetch-stats.test.mjs`
Expected: PASS (3 tests).

- [ ] **Step 5: Generate a sample `data.json`** so the page renders before the Action exists:

```bash
node website/scripts/fetch-stats.mjs   # hits pypistats (keyless); web stays null
```

- [ ] **Step 6: Commit**

```bash
git add website/scripts/fetch-stats.mjs website/scripts/fetch-stats.test.mjs website/static/stats/data.json
git commit -m "feat(stats): pypistats+goatcounter fetch/transform with tests + sample data"
```

---

### Task 11: GoatCounter wiring (cookieless, env-gated)

**Files:**
- Modify: `website/package.json` (add `docusaurus-plugin-goatcounter` devDependency)
- Modify: `website/docusaurus.config.js` (env-gated plugin entry)

- [ ] **Step 1: Add the plugin dep** to `website/package.json` devDependencies: `"docusaurus-plugin-goatcounter": "^1.0.0"` (confirm latest + exact option name against its README; the option is the GoatCounter site `code`).

- [ ] **Step 2: Register it env-gated** in `docusaurus.config.js` so the build works with no account configured:

```js
plugins: [
  // ...existing plugins...
  ...(process.env.GOATCOUNTER_CODE
    ? [['docusaurus-plugin-goatcounter', { code: process.env.GOATCOUNTER_CODE }]]
    : []),
],
```

- [ ] **Step 3: Build with and without the env var**

Run: `cd website && npm install && npm run build` (no env) → PASS.
Run: `GOATCOUNTER_CODE=demo npm run build` → PASS (plugin injects the cookieless beacon).

- [ ] **Step 4: Commit**

```bash
git add website/package.json website/package-lock.json website/docusaurus.config.js
git commit -m "feat(stats): env-gated GoatCounter (cookieless) on the docs site"
```

---

### Task 12: `/stats` page

**Files:**
- Create: `website/src/pages/stats.jsx`

**Interfaces:**
- Consumes: `static/stats/data.json` (shape from Task 10) via runtime fetch (client-only, no SSR issue).

- [ ] **Step 1: Create `website/src/pages/stats.jsx`:**

```jsx
import React, { useEffect, useState } from 'react';
import Layout from '@theme/Layout';
import useBaseUrl from '@docusaurus/useBaseUrl';

function Bar({ label, value, max }) {
  const pct = max > 0 ? Math.round((value / max) * 100) : 0;
  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 12, margin: '6px 0' }}>
      <div style={{ width: 120 }}>{label}</div>
      <div style={{ flex: 1, background: 'var(--ifm-color-emphasis-200)', borderRadius: 4 }}>
        <div style={{ width: `${pct}%`, background: 'var(--ifm-color-primary)', height: 18, borderRadius: 4 }} />
      </div>
      <div style={{ width: 90, textAlign: 'right' }}>{(value || 0).toLocaleString()}</div>
    </div>
  );
}

export default function Stats() {
  const dataUrl = useBaseUrl('/stats/data.json');
  const [stats, setStats] = useState(null);
  const [error, setError] = useState(false);
  useEffect(() => {
    fetch(dataUrl).then((r) => r.json()).then(setStats).catch(() => setError(true));
  }, [dataUrl]);
  return (
    <Layout title="Stats" description="MCP for Unity adoption — PyPI installs and docs traffic (anonymous, aggregate)">
      <main className="container margin-vert--lg">
        <h1>Adoption stats</h1>
        <p>Anonymous, aggregate numbers only — PyPI installs of <code>mcpforunityserver</code> and docs traffic. No personal data.</p>
        {error && <p>Stats are being generated — check back soon.</p>}
        {stats && (
          <>
            <h2>PyPI installs</h2>
            <Bar label="Last day" value={stats.pypi.lastDay} max={stats.pypi.lastMonth} />
            <Bar label="Last week" value={stats.pypi.lastWeek} max={stats.pypi.lastMonth} />
            <Bar label="Last month" value={stats.pypi.lastMonth} max={stats.pypi.lastMonth} />
            <h2>Docs traffic</h2>
            {stats.web ? <p>{(stats.web.total || 0).toLocaleString()} total pageviews</p>
                       : <p>Web analytics pending account provisioning.</p>}
            <p style={{ opacity: 0.6, marginTop: 24 }}>Last updated: {stats.generatedAt}</p>
          </>
        )}
      </main>
    </Layout>
  );
}
```

- [ ] **Step 2: Build + serve**

Run: `cd website && npm run build && npm run serve`
Expected: `/stats` renders the PyPI bars from the sample `data.json`; "Docs traffic" shows the pending message (web is null).

- [ ] **Step 3: Commit**

```bash
git add website/src/pages/stats.jsx
git commit -m "feat(stats): /stats page rendering the unified install+traffic data"
```

---

### Task 13: Scheduled GitHub Action + navbar link + analytics doc

**Files:**
- Create: `.github/workflows/stats.yml`
- Modify: `website/docusaurus.config.js` (navbar link to `/stats`)
- Create: `website/docs/architecture/external-analytics.md`

- [ ] **Step 1: Create `.github/workflows/stats.yml`:**

```yaml
name: Update adoption stats
on:
  schedule:
    - cron: '0 6 * * *'
  workflow_dispatch: {}
permissions:
  contents: write
jobs:
  stats:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with: { ref: beta }
      - uses: actions/setup-node@v4
        with: { node-version: 20 }
      - name: Fetch stats
        env:
          GOATCOUNTER_TOKEN: ${{ secrets.GOATCOUNTER_TOKEN }}
          GOATCOUNTER_SITE: ${{ secrets.GOATCOUNTER_SITE }}
        run: node website/scripts/fetch-stats.mjs
      - name: Commit if changed
        run: |
          if [[ -n "$(git status --porcelain website/static/stats/data.json)" ]]; then
            git config user.name "github-actions[bot]"
            git config user.email "github-actions[bot]@users.noreply.github.com"
            git add website/static/stats/data.json
            git commit -m "chore: update adoption stats [skip ci]"
            git push
          else
            echo "No changes"
          fi
```

- [ ] **Step 2: Add the navbar link** in `docusaurus.config.js` `navbar.items`: `{ to: '/stats', label: 'Stats', position: 'left' }`.

- [ ] **Step 3: Write `website/docs/architecture/external-analytics.md`** — explains the external analytics (pypistats + GoatCounter), the cookieless/no-PII privacy stance, that it's distinct from the in-product telemetry, and the maintainer provisioning steps (token → `GOATCOUNTER_TOKEN`/`GOATCOUNTER_SITE` secrets, Actions write permission). Add a `description:` frontmatter line. Add it to `website/sidebars.js` under Architecture.

- [ ] **Step 4: Validate** — `node website/scripts/fetch-stats.mjs` runs clean locally (dry-run of the Action's core step); `cd website && npm run build` passes (new doc + navbar link, no broken links).

- [ ] **Step 5: Commit**

```bash
git add .github/workflows/stats.yml website/docusaurus.config.js \
        website/docs/architecture/external-analytics.md website/sidebars.js
git commit -m "feat(stats): daily stats workflow, navbar link, analytics architecture doc"
```

> **PHASE P3 REVIEW GATE:** user reviews analytics before the PR is opened. Opening the PR + the maintainer-gated items in `docs/MAINTAINER_ACTIONS.md` are a separate, explicit step (gh auth as `Scriptwonder`, push to fork, draft PR to `CoplayDev:beta`).

---

## Plan self-review notes

- **Spec coverage:** §4 brand → T1–T4; §4.2 naming → T3; §4.3 themeColor/sweep → T4; §5 README → T5–T8; §5.2 PyPI metadata + §5.3 distribution → T9; §6 analytics (sources, unification, /stats, workflow, privacy doc, maintainer provisioning) → T10–T13; §8 testing → folded into each task; §10 maintainer checklist → `docs/MAINTAINER_ACTIONS.md` (T9). Out-of-scope items (in-product telemetry, docs-IA rebuild, custom domain) intentionally untouched.
- **Type consistency:** `normalizePypiRecent`/`buildStats` signatures + the `{generatedAt, pypi:{lastDay,lastWeek,lastMonth}, web}` shape are consistent across T10 (producer), T12 (`/stats` consumer), and the sample `data.json`. `ProductInfo.ProductName`/`MenuRoot` used consistently in T3–T4.
- **Known verify-then-confirm points (not placeholders):** the chosen logo SVG (T1 interactive gate), the exact `docusaurus-plugin-goatcounter` option name (T11 — confirm against its README), and whether `MCPForUnity/package.json` accepts an `icon` field (T4 — else rely on `Editor/Resources`). Each has a concrete fallback.
