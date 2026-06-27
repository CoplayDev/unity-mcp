---
description: How MCP for Unity tracks adoption with cookieless, aggregate-only external analytics — PyPI installs via pypistats and docs traffic via GoatCounter.
---

# External analytics

## What is tracked and why

MCP for Unity uses two external data sources to understand adoption:

- **PyPI installs** — daily, weekly, and monthly download counts for `mcpforunityserver`, fetched from the public [pypistats.org](https://pypistats.org/packages/mcpforunityserver) API. No API key required; the data is already public.
- **Docs traffic** — aggregate pageview totals from the docs site, collected via [GoatCounter](https://www.goatcounter.com/) when provisioned.

Both sources are **cookieless, store no personal data, and expose only aggregates**. This is distinct from the in-product telemetry (`architecture/telemetry`) which runs inside the Unity Editor and is controlled by the user from the MCP for Unity settings window.

## Privacy stance

- No cookies, no fingerprinting, no user IDs.
- No PII is transmitted or stored.
- GoatCounter is a privacy-first analytics service. Its [privacy policy](https://www.goatcounter.com/help/privacy) explicitly commits to not selling data.
- The aggregated numbers committed to the repo (`website/static/stats/data.json`) contain only counts and a timestamp.

## How data flows

1. A GitHub Actions workflow (`.github/workflows/stats.yml`) runs daily at 06:00 UTC.
2. `website/scripts/fetch-stats.mjs` fetches PyPI recent-download counts and, when GoatCounter secrets are present, the total pageview count.
3. The result is written to `website/static/stats/data.json` and committed to `beta` with `[skip ci]`.
4. The `/stats` page fetches `data.json` at runtime (client-side) and renders the bar charts and totals.

## Maintainer provisioning

GoatCounter analytics only activate when two repository secrets are set. Until then, the docs site builds and the `/stats` page renders with a "pending account provisioning" message.

**Steps for a CoplayDev maintainer:**

1. Create a free GoatCounter account at [goatcounter.com](https://www.goatcounter.com/) for the docs site (e.g. site code `mcp-for-unity`).
2. In GoatCounter settings, generate an API token with read access to stats.
3. Add two repository secrets in **Settings → Secrets and variables → Actions**:
   - `GOATCOUNTER_TOKEN` — the API token from step 2.
   - `GOATCOUNTER_SITE` — the site code from step 1 (e.g. `mcp-for-unity`).
4. Add `GOATCOUNTER_CODE` as a **Actions variable** (not a secret — it's not sensitive) with the same site code value, so the `GOATCOUNTER_CODE=demo npm run build` gate in CI works.
5. Grant the `stats` workflow **Contents: write** permission (already set in `stats.yml`).
6. Trigger the workflow manually via **Actions → Update adoption stats → Run workflow** to generate the first real `data.json`.

The docs site build with `GOATCOUNTER_CODE` set injects the GoatCounter tracking script into every page. Without the variable, the plugin is not loaded and no script is injected.
