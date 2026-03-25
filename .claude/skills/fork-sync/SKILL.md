---
name: fork-sync
description: Sync fork with upstream CoplayDev/unity-mcp repo. Use /fork-sync [status|sync|cherry-pick|diff] to analyze divergence, merge upstream changes, or cherry-pick specific commits into origin/beta.
---

# Fork Sync — Upstream Synchronization Skill

You are synchronizing The1Studio's fork of `CoplayDev/unity-mcp` with the upstream repository.

## allowed-tools

Bash, Read, Edit, Write, Grep, Glob, AskUserQuestion

## Context

- **Origin**: `git@github-the1studio:The1Studio/unity-mcp.git` (The1Studio fork)
- **Upstream**: `https://github.com/CoplayDev/unity-mcp.git` (CoplayDev original)
- **Sync branch**: `beta` (both sides)
- **Fork strategy**: Avoid modifying upstream code. Studio-specific tools go in separate files. Our additions should not conflict.

## Instructions

### 1. Parse arguments

The user's argument is: `$ARGUMENTS`

Valid subcommands: `status`, `sync`, `cherry-pick`, `diff`, or empty.

| Subcommand | Purpose |
|------------|---------|
| `status` (default) | Show divergence analysis — commits ahead/behind, file overlap, conflict risk |
| `sync` | Full merge of `upstream/beta` into current branch |
| `cherry-pick` | Pick specific upstream commits (interactive selection) |
| `diff` | Show detailed diff between fork and upstream |

If empty or invalid, default to `status`.

### 2. Verify git state

Run these checks before any operation:

```bash
# Must be in the unity-mcp repo
git rev-parse --show-toplevel

# Must have upstream remote
git remote get-url upstream

# Check for uncommitted changes
git status --porcelain
```

**If uncommitted changes exist**: STOP and ask user to commit or stash first. Never proceed with dirty working tree.

**If upstream remote missing**: Add it:
```bash
git remote add upstream https://github.com/CoplayDev/unity-mcp.git
```

### 3. Fetch latest upstream

```bash
git fetch upstream --quiet
git fetch origin --quiet
```

### 4. Execute subcommand

---

#### 4a. `status` — Divergence Analysis

Generate a comprehensive sync status report:

```bash
# Commit counts
MERGE_BASE=$(git merge-base origin/beta upstream/beta)
UPSTREAM_AHEAD=$(git rev-list --count origin/beta..upstream/beta)
FORK_AHEAD=$(git rev-list --count upstream/beta..origin/beta)

# Files changed upstream since last sync
git diff --stat $MERGE_BASE..upstream/beta

# Files changed in our fork since last sync
git diff --stat $MERGE_BASE..origin/beta

# Overlapping files (potential conflicts)
comm -12 \
  <(git diff --name-only $MERGE_BASE..upstream/beta | sort) \
  <(git diff --name-only $MERGE_BASE..origin/beta | sort)
```

**Output format:**

```
## Fork Sync Status

**Last sync point**: <merge-base-hash> (<date>)
**Upstream ahead**: N commits
**Fork ahead**: N commits

### Upstream Changes (since last sync)
- <file-change-summary>

### Our Fork Changes (since last sync)
- <file-change-summary>

### Potential Conflicts (files modified on both sides)
- <overlapping-files-list>

### Risk Assessment
- HIGH: N files with overlapping changes
- MEDIUM: N shared directories with different files changed
- LOW: N independent changes (no overlap)

### Recommendation
<sync-now | wait | cherry-pick-specific>
```

**Risk classification**:
- **HIGH**: Same file modified on both sides (especially `.cs`, `.py`, `.asmdef` files)
- **MEDIUM**: Same directory but different files (e.g., both added tools in `Server/src/services/tools/`)
- **LOW**: Changes in completely separate areas

---

#### 4b. `sync` — Full Upstream Merge

**Pre-sync checklist** (run automatically):
1. Verify clean working tree
2. Run `status` first and show divergence report
3. Ask user to confirm after seeing the report

**Execute merge:**

```bash
# Ensure we're on beta
git checkout beta

# Merge upstream/beta with a descriptive commit message
git merge upstream/beta --no-edit -m "sync: merge upstream/beta into fork ($(git rev-list --count origin/beta..upstream/beta) commits)"
```

**If merge succeeds with no conflicts:**
1. Show summary of merged changes
2. Run post-merge steps (see Section 5)
3. Report success

**If merge has conflicts:**
1. Run conflict analysis (see `references/conflict-resolution.md`)
2. Categorize each conflict by type and risk
3. Present conflicts to user with resolution recommendations
4. For each conflict, suggest the appropriate resolution:

| Conflict Location | Likely Resolution |
|-------------------|-------------------|
| `Server/uv.lock` | Accept upstream, then run `cd Server && uv lock` |
| `MCPForUnity/Editor/Tools/Manage*.cs` (upstream file) | Accept upstream changes, verify our separate files still compile |
| `MCPForUnity/Editor/Tools/Manage*.cs` (our studio file) | Keep ours — these are separate files |
| `.gitignore` | Manual merge — combine both additions |
| `CLAUDE.md` | Keep ours — this is fork-specific |
| `.claude/*` | Keep ours — this is fork-specific |
| `CustomTools/*` | Keep ours — upstream doesn't have this |
| `Server/src/services/tools/*.py` (upstream file) | Accept upstream, verify our tools still work |
| `MCPForUnity/Editor/*.asmdef` | CAREFUL — manual merge, may need both references |
| `manifest.json` / `packages-lock.json` | Accept upstream, then verify Unity resolves |

5. After user resolves conflicts:
   ```bash
   git add -A
   git commit --no-edit
   ```
6. Run post-merge steps (see Section 5)

---

#### 4c. `cherry-pick` — Selective Commits

Show upstream commits not yet in our fork:

```bash
git log --oneline upstream/beta --not origin/beta | head -30
```

Ask user which commits to pick (by hash or range). Then:

```bash
git cherry-pick <hash1> <hash2> ...
```

If conflicts occur, follow same resolution guide as `sync`.

---

#### 4d. `diff` — Detailed Comparison

Show what upstream has that we don't, grouped by area:

```bash
MERGE_BASE=$(git merge-base origin/beta upstream/beta)

echo "=== Python Server Changes ==="
git diff $MERGE_BASE..upstream/beta -- Server/

echo "=== Unity Plugin Changes ==="
git diff $MERGE_BASE..upstream/beta -- MCPForUnity/

echo "=== Test Changes ==="
git diff $MERGE_BASE..upstream/beta -- TestProjects/ Server/tests/

echo "=== Docs/Config Changes ==="
git diff $MERGE_BASE..upstream/beta -- docs/ README.md .github/
```

### 5. Post-merge steps

After any successful merge or cherry-pick:

**Step 1 — Python lock file:**
```bash
cd Server && uv lock && cd ..
```

**Step 2 — Verify Python tests:**
```bash
cd Server && uv run pytest tests/ -v --timeout=30 2>&1 | tail -20
```

**Step 3 — Check for namespace/reference issues:**
```bash
# Look for broken C# references in our studio tools
grep -r "using " MCPForUnity/Editor/Tools/ --include="*.cs" | grep -v "^Binary" | sort -u
```

**Step 4 — Summary report:**

```
## Sync Complete

**Merged**: N upstream commits
**Conflicts resolved**: N
**Post-merge fixes**:
- [ ] uv.lock regenerated
- [ ] Python tests: PASS/FAIL
- [ ] C# namespace check: OK/ISSUES

**Next steps**:
- Push to origin: `git push origin beta`
- Open Unity project and verify compilation
- Run Unity tests via Test Runner
```

**Step 5 — Ask user:**
- Push to origin now?
- Any follow-up needed?

### 6. Studio-specific file registry

These files/directories are **ours only** — upstream doesn't have them. Always keep ours in conflicts:

```
.claude/
CLAUDE.md
CustomTools/
MCPForUnity/Editor/Tools/ManageAddressables.cs
MCPForUnity/Editor/Tools/ManageAssetHunter.cs
MCPForUnity/Editor/Tools/ManageAudio.cs
MCPForUnity/Editor/Tools/ManageBehavior.cs
MCPForUnity/Editor/Tools/ManageBuild.cs
MCPForUnity/Editor/Tools/ManageCinemachine.cs
MCPForUnity/Editor/Tools/ManageDots.cs
MCPForUnity/Editor/Tools/ManageDotsGraphics.cs
MCPForUnity/Editor/Tools/ManageDotsPhysics.cs
MCPForUnity/Editor/Tools/ManageRenderingStats.cs
MCPForUnity/Editor/Tools/ManageValidationSnapshot.cs
MCPForUnity/Editor/Tools/Graphics/PerformanceSessionRecorder.cs
MCPForUnity/Editor/Helpers/AssetPathUtility.cs
MCPForUnity/Editor/Services/Server/TerminalLauncher.cs
Server/src/services/tools/manage_addressables.py
Server/src/services/tools/manage_asset_hunter.py
Server/src/services/tools/manage_audio.py
Server/src/services/tools/manage_behavior.py
Server/src/services/tools/manage_build.py
Server/src/services/tools/manage_cinemachine.py
Server/src/services/tools/manage_dots.py
Server/src/services/tools/manage_dots_graphics.py
Server/src/services/tools/manage_dots_physics.py
Server/src/services/tools/manage_rendering_stats.py
Server/src/services/tools/manage_validation_snapshot.py
scripts/
.github/
docs/
```

**Keep this list updated** when adding new studio-specific tools.

### 7. Gotchas & warnings

- **uv.lock**: Always regenerate after merge — never manually resolve conflicts in lock files
- **Meta files**: Unity `.meta` files must stay paired with their `.cs` files. If upstream deletes a file we also have, the `.meta` must go too
- **.asmdef references**: If upstream adds new assembly references, our studio tools may need to add them too if they depend on the same assemblies
- **Removed tools**: We previously removed `ManageCamera.cs` and `ManagePackages.cs` to avoid upstream conflicts (commit `71ef5bc`). If upstream changes these, ignore those changes
- **Python imports**: After merge, verify no circular imports in `Server/src/services/tools/`
- **WebSocket protocol**: If upstream changes the WebSocket message format, all tools (both upstream and ours) are affected — test thoroughly
