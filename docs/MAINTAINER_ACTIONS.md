# Maintainer Actions (CoplayDev-gated)

Items in this checklist require CoplayDev organization access or credentials that
cannot be committed to the repository. Complete these after the PR lands on
`CoplayDev:beta`.

---

## GitHub repository settings

- [ ] **Upload the GitHub social preview image.**
  Go to: `https://github.com/CoplayDev/unity-mcp` → Settings → Social preview.
  Upload: `website/static/img/github-social-preview.png` (2560×1280, 2:1 — rendered from the brand HTML templates, not `gen:brand`).

- [ ] **Set repository topics.**
  Suggested topics: `unity`, `mcp`, `model-context-protocol`, `ai`, `gamedev`,
  `unity3d`, `llm`, `claude`, `cursor`, `game-development`.
  Go to: repository homepage → gear icon next to "About" → Topics.

- [ ] **Set repository website URL.**
  Go to: repository homepage → gear icon next to "About" → Website.
  Value: `https://coplaydev.github.io/unity-mcp/`

---

## Unity Asset Store

- [ ] **Update Asset Store listing art.**
  The hero banner and icon should reflect the new brand assets from
  `website/static/img/` (social-card, logo-mark). Log in to the
  [Unity Publisher Portal](https://publisher.unity.com/) and update
  the listing images for the MCP for Unity package.

---

## PyPI

- [ ] **Publish a new release with the updated metadata.**
  The `[project.urls]` block in `Server/pyproject.toml` now includes
  `Homepage`, `Documentation`, and `Changelog` in addition to
  `Repository` and `Issues`. These appear on the PyPI project page
  once a new version is published.

  ```bash
  cd Server
  uv build
  uv publish
  ```

  Requires PyPI credentials with publish rights to `mcpforunityserver`.

---

## Adoption analytics (private, maintainer-only)

The daily stats workflow (`.github/workflows/stats.yml`) is **read-only** — it posts a
unified table to the **GitHub Actions run summary** (collaborators only) and never commits.
Leave the repo's default workflow permissions at read-only; do **not** enable repo-wide
"Read and write".

- [ ] **Provision the GitHub traffic PAT** (powers the dashboard's lead "real-user" rows —
  unique repo cloners/viewers). Create a fine-grained PAT scoped to `CoplayDev/unity-mcp`
  with **Repository permissions → Administration: read**, then add it as a repository
  **secret** `STATS_GITHUB_TOKEN`. (The default `GITHUB_TOKEN` returns 401 on the traffic API,
  so without this PAT those rows render `—`.)

- [ ] **(Docs traffic) Create a GoatCounter account** at [goatcounter.com](https://www.goatcounter.com/)
  for the docs site (`coplaydev.github.io/unity-mcp`); keep its dashboard **private**.

- [ ] **Generate a GoatCounter API token** (Settings → API tokens → `readonly` scope) and add
  two repository **secrets**:
  - `GOATCOUNTER_TOKEN` — the API token
  - `GOATCOUNTER_SITE` — the site code (subdomain, e.g. `unity-mcp`)

- [ ] **Add `GOATCOUNTER_CODE` as an Actions _variable_** (Settings → Secrets and variables →
  Actions → Variables) with the same site code. It is already wired in:
  `.github/workflows/docs-deploy.yml`'s Build step maps it from `vars.GOATCOUNTER_CODE`, so once
  the variable is set the cookieless beacon is injected on the next docs deploy.

---

## MCP Registry (optional)

- [ ] **Claim the MCP for Unity entry** in any MCP registries or directories
  (e.g., [mcp.so](https://mcp.so/), [glama.ai/mcp](https://glama.ai/mcp/)) to
  ensure the canonical metadata (name, description, docs URL, PyPI package) is
  up to date and correctly attributed to CoplayDev.
