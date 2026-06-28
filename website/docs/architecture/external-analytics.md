---
description: How MCP for Unity tracks adoption with cookieless, aggregate-only analytics — a public PyPI downloads badge plus a private, maintainer-only dashboard.
---

# External analytics

## What is tracked and why

MCP for Unity uses two external data sources to understand adoption:

- **PyPI installs** — daily, weekly, and monthly download counts for `mcpforunityserver`, fetched from the public [pypistats.org](https://pypistats.org/packages/mcpforunityserver) API. No API key required; this data is already public, and a downloads badge appears in the README.
- **Docs traffic** — aggregate pageview totals for this site, collected via [GoatCounter](https://www.goatcounter.com/) when provisioned.

Both sources are **cookieless, store no personal data, and expose only aggregates**. This is distinct from the [in-product telemetry](./telemetry) which runs inside the Unity Editor and is controlled by the user from the MCP for Unity settings window.

## What is public vs private

- **Public:** the PyPI **downloads badge** in the README (download counts are public on PyPI regardless of what we do).
- **Private (maintainer-only):** the unified install + docs-traffic **dashboard**. It is posted to the GitHub Actions run summary, which only repo collaborators can see. There is **no public stats page and no stats file published to the site**.

## Privacy stance

- No cookies, no fingerprinting, no user IDs.
- No PII is transmitted or stored.
- GoatCounter is a privacy-first analytics service; its [privacy policy](https://www.goatcounter.com/help/privacy) commits to not selling data. The GoatCounter dashboard should be kept **private** so traffic numbers stay maintainer-only.
- The maintainer summary contains only counts and a timestamp.

## How data flows

1. A GitHub Actions workflow (`.github/workflows/stats.yml`) runs daily at 06:00 UTC, and on demand via **Run workflow**.
2. `website/scripts/fetch-stats.mjs` fetches PyPI recent-download counts and, when GoatCounter secrets are present, the total pageview count.
3. The script renders a Markdown table that the workflow appends to `$GITHUB_STEP_SUMMARY`.
4. Collaborators read the numbers on the **Actions → Adoption stats** run page. Nothing is committed to the repo or published to the docs site.

## Maintainer provisioning

The docs-traffic half activates only when GoatCounter is configured. Until then the PyPI numbers still appear in the summary and the docs-traffic line reads "pending GoatCounter provisioning".

**Steps for a CoplayDev maintainer:**

1. Create a free GoatCounter account at [goatcounter.com](https://www.goatcounter.com/) for the docs site (e.g. site code `mcp-for-unity`); keep its dashboard **private**.
2. Generate an API token with read access to stats.
3. Add two repository **secrets** (Settings → Secrets and variables → Actions):
   - `GOATCOUNTER_TOKEN` — the API token.
   - `GOATCOUNTER_SITE` — the site code (e.g. `mcp-for-unity`).
4. Add `GOATCOUNTER_CODE` as an **Actions variable** with the same site code, so the docs build injects the cookieless tracking beacon. This enables docs-traffic *collection* on the public site; the numbers themselves stay private in GoatCounter and the Actions summary.
5. Run **Actions → Adoption stats → Run workflow** to generate the first summary.

The `stats` workflow needs only `contents: read` — it posts to the run summary and never commits.
