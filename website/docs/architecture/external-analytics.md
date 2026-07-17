---
description: How MCP for Unity tracks adoption with cookieless, aggregate-only analytics — a public PyPI downloads badge plus a private, maintainer-only dashboard.
---

# External analytics

## What is tracked and why

MCP for Unity tracks adoption with several aggregate signals, ordered from most to least honest about *real users*:

- **In-product DAU / WAU** — the true active-user count, deduplicated per anonymous install UUID by the in-product telemetry (`Server/src/core/telemetry.py`). Surfacing it on this dashboard needs a read API on the Coplay telemetry backend.
- **Unique repo cloners / viewers (14-day)** — from the GitHub repository traffic API; a strong proxy for developers actually pulling the code. Only collaborators can read it (needs a token), so it is private by nature.
- **GitHub stars / forks** — real accounts that starred or forked; public, an interest (not usage) signal.
- **PyPI installs** — daily/weekly/monthly download counts for `mcpforunityserver` from the public [pypistats.org](https://pypistats.org/packages/mcpforunityserver) API. **Heavily inflated** by CI, mirrors, `uvx` re-fetches, and Docker rebuilds — an install-event *reach* number, not a user count. A downloads badge appears in the README.
- **Docs traffic** — aggregate pageview totals from [GoatCounter](https://www.goatcounter.com/) when provisioned.

All sources are **cookieless, store no personal data, and expose only aggregates**. The DAU/WAU source is the [in-product telemetry](./telemetry) that runs inside the Unity Editor and is controlled by the user from the MCP for Unity settings window.

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

Stars, forks, and PyPI numbers work out of the box. The two highest-signal rows need setup:

**Unique repo cloners / viewers (GitHub traffic):**

1. Create a fine-grained PAT scoped to `CoplayDev/unity-mcp` with **Repository permissions → Administration: read** (the traffic API requires it; the default `GITHUB_TOKEN` returns 401/403).
2. Add it as the repository **secret** `STATS_GITHUB_TOKEN`.

**In-product DAU / WAU (the true active-user count):**

- The data is already collected by `Server/src/core/telemetry.py` (deduplicated per anonymous install UUID) and POSTed to the Coplay telemetry backend. Surfacing it here needs a **read / aggregate API** on that backend plus a token — back-end work owned by Coplay. Once available, add a `COPLAY_STATS_TOKEN` secret and a fetch in `fetch-stats.mjs`.

**Docs traffic (GoatCounter):**

1. Create a free [goatcounter.com](https://www.goatcounter.com/) site (e.g. code `mcp-for-unity`); keep its dashboard **private**.
2. Generate an API token with read access to stats.
3. Add secrets `GOATCOUNTER_TOKEN` (token) and `GOATCOUNTER_SITE` (site code), and an **Actions variable** `GOATCOUNTER_CODE` (same site code) so the docs build injects the cookieless beacon — collection happens on the public site, the numbers stay private.

Then run **Actions → Adoption stats → Run workflow**. The `stats` workflow needs only `contents: read` — it posts to the run summary and never commits.
