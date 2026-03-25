# Conflict Resolution Guide

## Conflict Categories

### Category 1: Lock Files (AUTO-RESOLVE)
**Files**: `Server/uv.lock`, `manifest.json`, `packages-lock.json`

**Strategy**: Accept upstream version, then regenerate.
```bash
# For uv.lock
git checkout --theirs Server/uv.lock
git add Server/uv.lock
cd Server && uv lock && cd ..
git add Server/uv.lock

# For Unity manifest/lock
git checkout --theirs manifest.json packages-lock.json
git add manifest.json packages-lock.json
```

### Category 2: Studio-Only Files (KEEP OURS)
**Files**: Everything in the studio file registry (SKILL.md Section 6)

**Strategy**: Always keep our version.
```bash
git checkout --ours <file>
git add <file>
```

### Category 3: Upstream-Owned Tools (ACCEPT THEIRS)
**Files**: `Server/src/services/tools/manage_*.py` (upstream originals), `MCPForUnity/Editor/Tools/Manage*.cs` (upstream originals)

**Strategy**: Accept upstream changes. Our studio tools are in separate files and shouldn't conflict.
```bash
git checkout --theirs <file>
git add <file>
```

**After resolving**: Verify our studio tools still compile by checking imports and namespaces.

### Category 4: Shared Infrastructure (MANUAL MERGE)
**Files**: `.asmdef`, `CommandRegistry.cs`, `__init__.py`, `.gitignore`, shared helpers

**Strategy**: Must merge manually. Both sides may have valid additions.

**Steps**:
1. Open the conflicted file
2. Identify what each side added
3. Keep both additions where they don't conflict
4. For `.asmdef` — combine reference arrays from both sides
5. For `__init__.py` — combine import lists
6. For `.gitignore` — combine ignore patterns (deduplicate)

### Category 5: Removed Files (SPECIAL CASE)
**Files**: `ManageCamera.cs`, `ManagePackages.cs` (removed in commit `71ef5bc`)

We intentionally removed these to avoid upstream conflict. If upstream modifies them:
```bash
# Delete our side (we don't want them)
git rm MCPForUnity/Editor/Tools/ManageCamera.cs MCPForUnity/Editor/Tools/ManageCamera.cs.meta 2>/dev/null
git rm MCPForUnity/Editor/Tools/ManagePackages.cs MCPForUnity/Editor/Tools/ManagePackages.cs.meta 2>/dev/null
```

## Conflict Detection Script

Run after `git merge` to categorize all conflicts:

```bash
# List all conflicted files
CONFLICTS=$(git diff --name-only --diff-filter=U)

echo "=== Lock Files (auto-resolve) ==="
echo "$CONFLICTS" | grep -E '\.(lock|packages-lock)' || echo "None"

echo "=== Studio-Only (keep ours) ==="
echo "$CONFLICTS" | grep -E '^(\.claude/|CLAUDE\.md|CustomTools/|scripts/|\.github/|docs/)' || echo "None"

echo "=== Upstream Tools (accept theirs) ==="
# Upstream-owned tools: files that exist in upstream/beta at merge-base
echo "$CONFLICTS" | while read f; do
  if git show upstream/beta:"$f" &>/dev/null 2>&1; then
    echo "$f (upstream-owned)"
  fi
done

echo "=== Manual Merge Required ==="
echo "$CONFLICTS" | grep -E '\.(asmdef|gitignore)$' || echo "None"
```

## Post-Resolution Verification

After resolving all conflicts:

```bash
# 1. No remaining conflict markers
grep -rn "<<<<<<" --include="*.cs" --include="*.py" --include="*.json" . && echo "CONFLICT MARKERS FOUND!" || echo "Clean"

# 2. Python syntax check
cd Server && uv run python -c "import services.tools" && echo "Python imports OK" || echo "Python import FAILED"

# 3. Check paired meta files
find MCPForUnity/ -name "*.cs" | while read f; do
  [ ! -f "${f}.meta" ] && echo "MISSING META: ${f}.meta"
done
```
