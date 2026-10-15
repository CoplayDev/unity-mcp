# LLM Prompt for Updating Documentation

Copy and paste this prompt into your LLM after you add, remove, rename or change an MCP tool or resource. Review its changes, then make sure the checks in step 5 pass.

---

## Prompt

I've just changed MCP tools or resources in this MCP for Unity repository. Please update the documentation to match.

1. **Read the current tools and resources**:
   - `Server/src/services/tools/`: tools (`@mcp_for_unity_tool`, including its `group`)
   - `Server/src/services/resources/`: resources (`@mcp_for_unity_resource`)
   - `Server/src/cli/commands/`: the Click CLI commands that mirror the tools

2. **Regenerate the reference pages.** `website/docs/reference/tools/**` and `website/docs/reference/resources/index.md` are generated from the registry:
   ```bash
   cd Server && uv run python ../tools/generate_docs_reference.py
   ```
   Do not hand-edit them, except for usage examples between `<!-- examples:start -->` and `<!-- examples:end -->`, which the generator keeps.

3. **Update the hand-maintained files**:
   - **manifest.json**: the `tools` array must list exactly the registered tools as `{"name", "description"}` entries; put new ones in alphabetical position. Resources are not listed. `Server/tests/test_manifest_tools.py` fails when a tool is missing, extra or listed twice, but it compares names only: when a tool's purpose changes, check its one-line description by hand.
   - **Tool groups**: if a tool adds a group or changes what a group covers, update the group's blurb in `TOOL_GROUPS` (`Server/src/services/registry/tool_registry.py`; it feeds the generated group pages, the `tool_groups` resource and `manage_tools`) and the group lists in `website/docs/guides/tool-groups.md` and `website/docs/contributing/dev-setup.md`.
   - **Tool count**: `README.md`, `docs/i18n/README-zh.md` and `website/docs/guides/tool-groups.md` state how many tools ship. Update the number if it changed.
   - **CLI docs**: the group table in `website/docs/reference/cli.md` and the "Complete Command Reference" table in `website/docs/guides/cli.md` must match the Click command tree (`cd Server && uv run unity-mcp --help`, then `--help` on each group).
   - **Skill**: if agents need new guidance for the change, update the skill. It has two copies, `unity-mcp-skill/` and `.claude/skills/unity-mcp-skill/`; keep both copies identical.

4. **Leave the release notes alone.** `README.md` has no tool or resource lists; it links to the website catalog. Its "Recent Updates" block (the latest 5 releases) and `website/docs/releases.md` are generated from GitHub Releases, whose notes GitHub builds from merged PR titles. After each release, `release.yml` dispatches `.github/workflows/sync-releases.yml`, which runs `tools/sync_release_notes.py` and merges the result into `beta` through its own PR. The "最近更新" block in `docs/i18n/README-zh.md` is not synced; it is updated by hand at release time.

5. **After updating**, run these checks:
   ```bash
   cd Server
   uv run python ../tools/generate_docs_reference.py --check
   uv run pytest tests/test_manifest_tools.py
   ```

Please show me the exact changes you're making to each file, and explain any discrepancies you find.

---
