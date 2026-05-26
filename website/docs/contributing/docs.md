---
id: docs
slug: /contributing/docs
title: Docs Workflow
sidebar_label: Docs Workflow
description: How the documentation site is built, what's auto-generated, and how to ship a docs change.
---

# Docs Workflow

The documentation site you're reading is built with **Docusaurus 3** and lives under `/website` in the same repo as the code. Every doc change goes through normal PRs.

## What's hand-written vs. auto-generated

| Section | Source | Authored by |
|---|---|---|
| Getting Started, Guides, Architecture, Contributing, Migrations | `website/docs/<section>/*.md` | hand-written |
| **Tool reference** (`/reference/tools/**`) | generated from `Server/src/services/tools/` | `tools/generate_docs_reference.py` |
| **Resource catalog** (`/reference/resources/`) | generated from `Server/src/services/resources/` | same generator |

The generator owns everything inside the front-matter banner. The **examples block** between `<!-- examples:start -->` and `<!-- examples:end -->` is hand-written and **preserved across regenerations**.

## Editing a hand-written page

1. Find the file under `website/docs/`.
2. Edit the markdown.
3. Local preview: `cd website && npm run start` → http://localhost:3000/unity-mcp/
4. Commit and PR against `beta`.

CI runs `npm run build` on every PR via `.github/workflows/docs-deploy.yml`. The PR check fails if the build fails, so dead links and missing pages surface before merge.

## Adding examples to a generated tool page

Find the page at `website/docs/reference/tools/<group>/<tool-name>.md`. Look for:

```html
<!-- examples:start -->
*No examples yet. Add usage examples here — they will be preserved across regenerations.*
<!-- examples:end -->
```

Replace the placeholder text with your example. **Don't move or rename the markers** — the generator uses them to know where your content is.

Run the generator to verify your examples survive a regeneration round-trip:

```bash
cd Server && uv run python ../tools/generate_docs_reference.py
git diff website/docs/reference/tools/<group>/<tool-name>.md
# Should show only your additions; the rest of the file is regenerated identically.
```

## Adding a new doc page

1. Create the `.md` file under the appropriate `website/docs/<section>/`. Front-matter at minimum:

   ```yaml
   ---
   id: my-page
   slug: /<section>/my-page
   title: Page Title
   sidebar_label: Short Label
   description: One-line description (used in search and OG).
   ---
   ```

2. Add the page to `website/sidebars.js` under the right category.
3. Use **brand-neutral slugs** — never put `mcp-for-unity` or `unity-mcp` in a URL path. The product name lives in `docusaurus.config.js`.

## When a slug must change

URL changes break external links. Always add a redirect:

```js
// website/docusaurus.config.js → plugins → @docusaurus/plugin-client-redirects
{
  redirects: [
    { from: '/old/slug', to: '/new/slug' },
  ],
}
```

## When tool/resource registries change

The pre-commit hook (installed via `tools/install-hooks.sh`) auto-regenerates `website/docs/reference/` whenever you stage a change under `Server/src/services/{tools,resources,registry}/`. Without the hook:

```bash
cd Server && uv run python ../tools/generate_docs_reference.py
git add website/docs/reference/
```

CI (`.github/workflows/docs-generate.yml`) fails the PR if the committed reference is stale.

## Release notes sync

`website/docs/releases.md` and the README's "Recent Updates" block are **both** generated from the GitHub Releases API by `tools/sync_release_notes.py`. The script:

- Uses `gh api` when available, falls back to `urllib` (with `certifi` if installed) otherwise.
- Renders the full release history into `releases.md`, grouped by minor version, with each release body in a collapsible `<details>` block.
- Replaces the block between `<!-- recent-updates:start -->` and `<!-- recent-updates:end -->` in the root `README.md` with the latest five releases.

CI keeps both in sync automatically via `.github/workflows/sync-releases.yml`. Triggers are intentionally narrow so the workflow never blocks outsider PRs:

| Trigger | What happens |
|---|---|
| `release.{published,edited,unpublished,deleted}` | Sync fires within ~30 seconds and commits directly to `beta` with `[skip ci]`. This is the canonical entry point — the only time the synced files can legitimately go stale. |
| `workflow_dispatch` | Manual escape hatch (re-run after a one-off UI edit, or to backfill). |

**Not triggered on `pull_request`.** A drift check at PR time would fail outsider PRs that edit README for unrelated reasons (typo fix, citation tweak), and the contributor wouldn't have push access to regenerate. The synced files are maintained by the release pipeline, not by PR authors.

To sync manually:

```bash
python tools/sync_release_notes.py            # write
python tools/sync_release_notes.py --check    # exit non-zero on drift
```

Do not hand-edit `releases.md` or the `recent-updates` block in `README.md` — your change will be overwritten on the next sync.

## Deploy

The live site at `https://coplaydev.github.io/unity-mcp/` deploys automatically on push to `beta`. No manual step per change.

### First-time setup (maintainers only)

The first deploy requires GitHub Pages to be enabled for the repo:

1. **Settings → Pages → Source** → choose **GitHub Actions** (not "Deploy from a branch").
2. Push to `beta` (or run the `Docs — Build & Deploy` workflow via **Actions → Run workflow**).
3. After the deploy job succeeds, the URL appears under **Settings → Pages**.

The workflow uses `actions/configure-pages@v5` + `actions/deploy-pages@v4`, so once Pages is set to "GitHub Actions" source, the deploy step provisions everything else automatically.

### Custom domain

When ready, add a `CNAME` file at `website/static/CNAME` containing the domain (e.g. `unitymcp.dev`), update `url` and `baseUrl` in `docusaurus.config.js`, and configure the DNS provider per [GitHub's custom-domain guide](https://docs.github.com/en/pages/configuring-a-custom-domain-for-your-github-pages-site).

## Markdown format

Pages use **CommonMark** (Docusaurus `format: 'detect'` mode), not full MDX. That means `{...}` and `<tag>` in body text are literals — no JS-expression parsing. If you need React components, name the file `.mdx` instead of `.md`.

## Translations

Chinese versions of `README-DEV.md` and the overview live under `docs/i18n/` and `docs/development/README-DEV-zh.md`. Full Docusaurus i18n setup (with `locales: ['en', 'zh']`) is deferred until the English site is stable.

## Search

Local lunr search ships today via `@easyops-cn/docusaurus-search-local`. The Algolia DocSearch application is queued (free for OSS, 4–6 week approval); the swap-in is a config change when the time comes.

## Typography

The site uses **Satoshi** (Fontshare) for body and headings and **JetBrains Mono** (Google Fonts) for code. Both load via stylesheet links in `docusaurus.config.js`. Theme overrides live in `website/src/css/custom.css`.
