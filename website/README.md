# MCP for Unity — Documentation Site

Docusaurus 3.x site for MCP for Unity. Deployed to https://coplaydev.github.io/unity-mcp/ by `.github/workflows/docs-deploy.yml` on every push to `beta` that touches `website/**`, `docs/**`, or the Python tool registry.

## Local development

```bash
cd website
npm install
npm run start    # serves at http://localhost:3000/unity-mcp/
```

Edits to Markdown under `docs/` hot-reload.

## Build

```bash
npm run build    # outputs to website/build/
npm run serve    # serves the build for local verification
```

## Layout

```
website/
  docs/                    Markdown content
    getting-started/       Overview, install, setup wizard, first prompt
    guides/                How-to content (migrated in M2)
    reference/             Tool & resource reference (auto-generated in M3)
    architecture/          System design notes
    contributing/          Dev setup, testing, releases
    migrations/            Version-upgrade guides
  src/
    css/custom.css         Theme overrides
  static/
    img/                   Logo, favicon, social card
  docusaurus.config.js     Site config — brand title/URL/social links live here
  sidebars.js              Navigation tree
```

## Brand-neutral URL policy

URL slugs must NOT contain `mcp-for-unity` or `unity-mcp`. The brand name lives in `docusaurus.config.js` (`title`, `tagline`, navbar/footer copy). A future product rename should touch this file and content text — not URL paths. See the rename-proofing section of the plan.

## Adding a redirect when renaming a slug

Never rename a published slug without adding an entry to `plugin-client-redirects` in `docusaurus.config.js`. External backlinks must keep working.

## Tool reference (M3+)

Files under `docs/reference/tools/` and `docs/reference/resources/` are **generated** from the Python `@mcp_for_unity_tool` and `@mcp_for_unity_resource` registries by `tools/generate_docs_reference.py`. Do not hand-edit those files outside the `<!-- examples:start --><!-- examples:end -->` blocks — the generator will overwrite them.
