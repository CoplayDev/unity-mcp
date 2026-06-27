# MCP for Unity — Repo Revamp: Brand Standardization, Distribution & Unified Analytics

- **Date:** 2026-06-27
- **Branch:** `revamp/brand-distribution-analytics` (off `beta`)
- **Author:** Shutong Wu (Scriptwonder)
- **Destination:** Upstream PR(s) to `CoplayDev:beta`
- **Status:** Draft — awaiting user review before implementation

---

## 1. Goal & Scope

Make the repo a better place for **users to discover, understand, and adopt** MCP for Unity, by:

1. **Standardizing the brand** — one coherent logo/icon/mark system + product name, applied consistently across every surface (README, docs site, Unity package, PyPI, MCP manifest, social/OG).
2. **Rebuilding the front door** — a README that actually sells and orients (capabilities, clients, versions, architecture, badges), plus tightened distribution/discoverability surfaces (PyPI, OpenUPM, Asset Store, MCP registries, awesome-lists, social preview).
3. **Adding a unified analytics dashboard** — external install + browsing data (PyPI downloads + privacy-first docs web analytics) surfaced in one place.

This is **not** a docs rebuild — the Docusaurus site is already strong. The work is *standardize + distribute + measure*.

### Non-goals (out of scope)

- Rewriting/restructuring the 87-page docs site IA (it's healthy; only minor polish included).
- Touching the existing **in-product telemetry** (`Server/src/core/telemetry.py`) — that already exists and is comprehensive; the new analytics here is **external** (PyPI + web), a different concern.
- Changing sponsor relationships or removing Coplay/Aura attribution (preserved as upstream requirement).
- A custom docs domain / CNAME (already deferred upstream).

---

## 2. Background — what already exists (reality check)

| Area | Current reality |
|---|---|
| README | 116 lines. Strong *frame* (logo, value prop, demo gif, badges, citation, i18n switcher, sponsor credits) but hollow middle — capabilities/clients/versions/architecture all link out. |
| Docs | Full Docusaurus 3.x site, 87 pages, deployed to `coplaydev.github.io/unity-mcp` on `beta` push. Well-branded, SEO-ready. **No GitHub Wiki.** |
| Branding | `logo.png` (1024×576, duplicated in `docs/images/` + `website/static/img/`), `logo-mark.svg`/`-dark.svg` (40×16), `favicon.png` (**1024×576 — wrong**), `social-card.png` (1200×630 — correct). Indigo `#4f46e5`/`#818cf8`, Satoshi + JetBrains Mono. |
| Telemetry | **Already exists** — anonymous, opt-out, posts to Coplay endpoint. Out of scope here. |
| PyPI | Published as `mcpforunityserver` v9.7.3. `project.urls` has only Repository + Issues. |
| Naming | Product name spelled 4 ways (see §4.2). |

---

## 3. Program structure — one branch → 3 focused PRs

Upstream reviewers want focused, reviewable PRs. Decompose into three, off the umbrella branch:

| PR | Title | Depends on | Can land independently? |
|---|---|---|---|
| **PR 1** | Brand system & standardization | — | Yes (foundation) |
| **PR 2** | README + distribution front door | PR 1 (consumes new brand assets) | After PR 1 |
| **PR 3** | Unified analytics dashboard | — (independent) | Yes, in parallel; but full activation needs CoplayDev account provisioning |

Each PR gets its own branch cut from this umbrella when ready, so they can be reviewed/merged separately.

---

## 4. Workstream A — Brand system & standardization (PR 1)

### 4.1 New brand assets (deliverables)

I propose **2–3 SVG mark directions** for the user to pick from (visual-companion offered at that step). Constraints to honor:

- Preserve the **indigo** identity (`#4f46e5` / `#818cf8`) unless the user wants a palette refresh — proposed as "evolve, not revolution."
- Keep **Satoshi + JetBrains Mono** typography.
- Marks must work at favicon scale (square) **and** wide (navbar 40×16).
- SVGs should use `currentColor`/CSS vars where possible instead of hardcoded `#0a0a0b`/`#fafafa`, so light/dark derive cleanly.

Asset list to produce from the chosen direction:

| Asset | Sizes/formats | Consumers |
|---|---|---|
| Logo mark (light + dark) | SVG (square + wide lockups) | navbar, README, package |
| Favicon set | 16, 32, 48 `.ico` + 180 (Apple), 192/512 (Android) | docs site (**fixes the 1024×576 bug**) |
| Full logo / hero | SVG + 2× PNG (e.g. 2048×1152) | README hero, docs landing |
| Social/OG card | 1200×630 PNG (refresh art) | docs `themeConfig.image`, GitHub social-preview |
| GitHub social-preview | 1280×640 PNG | repo Settings (maintainer-applied) |
| UPM package icon | 128×128 PNG (+ `.meta`) | Unity Package Manager |

### 4.2 Product-name standardization

Pick **"MCP for Unity"** (lowercase f) as the canonical user-facing name. Changes:

- C# Editor UI: `"MCP For Unity"` → `"MCP for Unity"` in menu paths (`MCPForUnityMenu.cs`), window titles (`MCPForUnityEditorWindow.cs`), `MCPSetupWindow`. Introduce a single `const string ProductName = "MCP for Unity"` source of truth and route user-facing strings through it.
- **Keep** internal names untouched: namespace `MCPForUnity`, package id `com.coplaydev.unity-mcp`, PyPI `mcpforunityserver`, CLI verbs `UnityMCP` (these are correct as URLs/identifiers).
- `manifest.json` `"name": "Unity MCP"` → **OPEN DECISION** (§9): align to "MCP for Unity" vs keep if the MCP registry requires the current string. Verify before changing.

> ⚠️ Menu-path rename is mildly user-facing (muscle memory / saved layouts). Low risk, but call it out in the PR.

### 4.3 Apply brand everywhere (standardization sweep)

Replace/sync assets and references across: `README.md`, `docs/i18n/README-zh.md`, `docs/images/` ↔ `website/static/img/` (keep the two logo copies in sync), `docusaurus.config.js` (favicon, navbar logo, `themeConfig.image`), `MCPForUnity/package.json` (icon), `manifest.json`. Add `themeColor` meta to the docs site.

### 4.4 Constraints / gotchas

- Unity `.meta` files must travel with any replaced/renamed asset in `MCPForUnity/`.
- Preserve Coplay (`manifest.json` icon = `coplay-logo.png`, footer) + Aura sponsor lines.
- README image refs are relative paths that render on GitHub — keep `docs/images/` copies authoritative for the README.

---

## 5. Workstream B — README + distribution front door (PR 2)

### 5.1 New README outline

Keep the existing strong frame; fill the hollow middle (content ports back from `README-zh.md` / `CLAUDE.md` / `Server/README.md`). Target ~180–220 lines, still scannable. **[KEEP]/[EXPAND]/[ADD]:**

1. Hero logo + i18n switcher — **[KEEP]** (re-sync EN ↔ zh, which have diverged).
2. Sponsor credit (Aura + Godot AI cross-promo) — **[KEEP]**.
3. Badge row — **[EXPAND]**: add PyPI version, downloads (pepy), GitHub release, CI status, OpenUPM version, GitHub stars.
4. One-line value prop — **[KEEP]**.
5. Demo gif — **[KEEP]** (optional 2nd gif: scripting/test run).
6. What it does / why — **[ADD]** 1 paragraph + 4 bullets.
7. Capabilities / feature catalog — **[ADD]** grouped by domain; full 40-tool/25-resource list in `<details>` + link to docs reference.
8. Supported clients & versions matrix — **[ADD]** one table (Claude Desktop/Code, Cursor, VS Code Copilot, Windsurf, Cline, Gemini CLI, Qwen, Copilot CLI, OpenClaw, Antigravity) + support window (Unity 2021.3 LTS → 6.x, Python 3.10+).
9. 60-second Quickstart — **[EXPAND]**: prerequisites → install (git URL; Asset Store + OpenUPM in `<details>`) → Configure All Detected Clients → first prompt + expected result.
10. Architecture diagram — **[ADD]** the `CLAUDE.md` stack as mermaid/ASCII.
11. Recent Updates — **[KEEP]** (auto-generated).
12. Social proof — **[EXPAND]**: surface star count high; keep star-history chart + SIGGRAPH Asia citation.
13. Comparison / positioning — **[ADD]** short: MCP for Unity (free/MIT/MCP-native) vs Aura (premium) vs hand-rolled Editor scripting.
14–19. Read the Docs / Community / Contributing / Advanced / Citation / Sponsor + Disclaimer + License — **[KEEP]**.

### 5.2 PyPI metadata polish (`Server/pyproject.toml`)

Add `project.urls`: `Homepage` + `Documentation` → `coplaydev.github.io/unity-mcp/`, `Changelog` → releases. Point `Server/README.md` "Full Documentation" link at the docs site; add PyPI version + downloads badges there.

### 5.3 Distribution & discoverability surface map

| Channel | Improvement | Maintainer-gated? |
|---|---|---|
| PyPI page | `project.urls` + badges (above) | Publish = CoplayDev |
| OpenUPM | Surface in English README + add OpenUPM version badge | No (doc) |
| Unity Asset Store | Link/badge in install section; mirror new hero in listing | Listing = CoplayDev publisher |
| GitHub About/topics | Add a few topics; confirm website link target | Repo admin |
| GitHub social-preview image | Upload 1280×640 (from PR 1 art) | Repo admin |
| awesome-mcp / awesome-unity | Submit PRs (1-line entries) | No |
| MCP registries (Smithery, mcp.so, Glama, PulseMCP) | Submit/claim listings | Submission open; claim may need org email |
| Docs SEO | per-page descriptions (already 86%), JSON-LD, sitemap priority | Doc PR |

Items needing a maintainer get a clearly-labeled "**requires CoplayDev maintainer action**" block in the PR description.

---

## 6. Workstream C — Unified analytics dashboard (PR 3)

### 6.1 Data sources

- **PyPI downloads → `pypistats.org` JSON API** (keyless): recent totals + per-version / per-python / per-OS. Supplement with **ClickPy** only if >180-day history is needed. **Skip BigQuery** (cost trap at 170TB+).
- **README badge → pepy.tech** total-downloads (most reliable headline number).
- **Docs web analytics → GoatCounter** (RECOMMENDED): cookieless, no consent banner, free for OSS, first-class Docusaurus SPA plugin, **clean CSV/JSON export** (needed to feed the unified page). **Alternative: Cloudflare Web Analytics** (zero-ops, but fiddly export). See §9 open decision.

### 6.2 Unification — scheduled GitHub Action → `stats.json` → `/stats` page

The only approach that unifies both sources, stays org-owned, holds the API token as a secret (impossible client-side on a static host), and renders the install→read funnel in one view:

1. `.github/workflows/stats.yml` cron (e.g. daily `0 6 * * *`).
2. Fetch pypistats JSON (keyless) for `mcpforunityserver`.
3. Fetch web-analytics export (GoatCounter `/export` or Cloudflare GraphQL) via repo secret.
4. Write `website/static/stats/data.json`, commit (default `GITHUB_TOKEN`).
5. Docusaurus `/stats` page reads JSON, renders with a small chart lib (installs vs docs pageviews).

### 6.3 Privacy stance

Matches the project's existing anonymous/opt-out ethos: cookieless, no PII leaves the browser, only aggregates are committed. Document the web-analytics choice in `website/docs/architecture/` (alongside the existing telemetry doc).

### 6.4 Maintainer-gated provisioning (CoplayDev)

PR 3 lands the **code**, but full activation needs the org to provision (the code degrades gracefully / uses a sample `data.json` until then):

- Create/own the **GoatCounter** (or Cloudflare) account → API token → repo secret (`GOATCOUNTER_TOKEN` or `CLOUDFLARE_API_TOKEN` + `CLOUDFLARE_ACCOUNT_ID`).
- Enable Actions write permission so the cron can commit `data.json`.
- (Only if BigQuery/pepy-API later) GCP billing / pepy Pro — not in this plan.

`pypistats` + `pepy` badge need **zero secrets** — so a meaningful slice of PR 3 works immediately.

---

## 7. Sequencing & dependencies

```
PR 1 (brand) ──> PR 2 (README/distribution, consumes brand)
PR 3 (analytics) ── independent ──> [maintainer provisions accounts] ──> full activation
```

Recommended order: **PR 1 → PR 2**, with **PR 3 in parallel**. Brand-concept selection (visual companion) is the first interactive step inside PR 1.

---

## 8. Testing & validation

- **Docs site:** `npm run build` in `website/` (`onBrokenLinks: 'throw'` catches link breaks); visually verify favicon/logo/social-card swap in light + dark.
- **Unity:** name-constant + menu-path changes compile across the CI matrix (`tools/check-unity-versions.sh`); verify menu still appears and window titles render.
- **README:** render-check on GitHub (relative image paths), validate all links, confirm badges resolve.
- **Analytics:** workflow dry-run produces valid `data.json`; `/stats` page renders from a committed sample without secrets; pypistats/pepy endpoints reachable.
- **Per CLAUDE.md:** any new behavior gets tests; run Python tests if Server code is touched (PyPI metadata change is config-only).

---

## 9. Open decisions (for user review)

1. **Web-analytics provider:** GoatCounter (recommended — API-first, best for the unified page) vs Cloudflare (zero-ops) vs run both. *Default if you don't pick: GoatCounter.*
2. **Brand visual direction:** evolve the current indigo + Satoshi identity (recommended) vs a fuller palette/type refresh. *(Concrete 2–3 mark concepts come during PR 1, with the visual companion.)*
3. **Editor menu casing:** apply `"MCP For Unity"` → `"MCP for Unity"` (recommended) — accept the minor menu-path churn?
4. **`manifest.json` name:** align `"Unity MCP"` → `"MCP for Unity"`, or keep (pending MCP-registry requirement check)? *Default: verify first, keep if required.*
5. **PR granularity:** 3 PRs as scoped (recommended) vs different split.

---

## 10. Maintainer-action checklist (CoplayDev) — to include in PRs

- [ ] Upload GitHub social-preview image (repo Settings).
- [ ] Add/confirm repo topics + website link.
- [ ] Update Unity Asset Store listing art/description to match new hero.
- [ ] Publish PyPI release carrying updated `pyproject.toml` metadata.
- [ ] Provision GoatCounter/Cloudflare account + token → repo secret; enable Actions write.
- [ ] (Optional) Claim MCP-registry listings needing an org email.

---

## 11. Success criteria

- One brand, zero drift: every surface in §4.3 uses the new assets + canonical name; favicon renders correctly; build passes.
- README front door is self-contained for the "what/why/how/which-client" questions without clicking out; badges show version + downloads + CI.
- A working `/stats` page renders PyPI installs + docs pageviews from a committed `data.json` (sample until accounts are provisioned).
- Each PR is independently reviewable with maintainer-gated items clearly flagged.
