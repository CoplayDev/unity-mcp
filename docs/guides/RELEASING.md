# Releasing (Maintainers)

This repo uses a two-branch flow to keep `main` stable for users:

- `beta`: integration branch where feature PRs land
- `main`: stable branch that should match the latest release tag

## Release checklist

### 1) Promote `beta` to `main` via PR

- Create a PR with:
  - base: `main`
  - compare: `beta`
- Ensure required CI checks are green.
- Merge the PR.

Release note quality depends on how you merge:

- Squash-merging feature PRs into `beta` is OK.
- Avoid squash-merging the `beta -> main` promotion PR. Prefer a merge commit (or rebase merge) so GitHub can produce better auto-generated release notes.

### 2) Run the Release workflow (manual)

- Go to **GitHub → Actions → Release**
- Click **Run workflow**
- Select:
  - `patch`, `minor`, or `major`
- Run it on branch: `main`

What the workflow does:

1. Creates a temporary `release/vX.Y.Z` branch with the version bump commit
2. Opens a PR from that branch into `main`
3. Auto-merges the PR (or waits for required checks, then merges)
4. Creates an annotated tag `vX.Y.Z` on the merged commit
5. Creates a GitHub Release for the tag
6. Publishes artifacts (Docker / PyPI / MCPB)
7. Opens a PR to merge `main` back into `beta` (so `beta` gets the bump)
8. Auto-merges the sync PR
9. Cleans up the temporary release branch

### 3) Verify release outputs

- Confirm a new tag exists: `vX.Y.Z`
- Confirm a GitHub Release exists for the tag
- Confirm artifacts:
  - Docker image published with version `X.Y.Z`
  - PyPI package published (if configured)
  - `unity-mcp-X.Y.Z.mcpb` attached to the GitHub Release

## Required repo settings

### Branch protection (Rulesets)

The release workflow uses PRs instead of direct pushes, so it works with strict branch protection. No bypass actors are required.

Recommended ruleset for `main`:

- Require PR before merging
- Allowed merge methods: `merge`, `rebase` (no squash for promotion PRs)
- Required approvals: `0` (so automated PRs can merge without human review)
- Optionally require status checks

Recommended ruleset for `beta`:

- Require PR before merging
- Allowed merge methods: `squash` (for feature PRs)
- Required approvals: `0` (so the sync PR can auto-merge)

### Enable auto-merge (required)

The workflow uses `gh pr merge --auto` to automatically merge PRs once checks pass.

To enable:

1. Go to **Settings → General**
2. Scroll to **Pull Requests**
3. Check **Allow auto-merge**

Without this setting, the workflow will fall back to direct merge attempts, which may fail if branch protection requires checks.

## Failure modes and recovery

### Tag already exists

The workflow fails if the computed tag already exists. Pick a different bump type or investigate why a tag already exists for that version.

### Bump PR fails to merge

If the version bump PR cannot be merged (e.g., required checks fail):

- The workflow will fail before creating a tag.
- Fix the issue, then either:
  - Manually merge the PR and create the tag/release, or
  - Close the PR, delete the `release/vX.Y.Z` branch, and re-run the workflow.

### Sync PR (`main -> beta`) fails

If the sync PR has merge conflicts:

- The workflow will fail after the release is published (artifacts are already out).
- Manually resolve conflicts in the sync PR and merge it.

### Leftover release branch

If the workflow fails mid-run, a `release/vX.Y.Z` branch may remain. Delete it manually before re-running:

```bash
git push origin --delete release/vX.Y.Z
```
