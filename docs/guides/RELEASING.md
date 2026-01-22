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

- Updates version references across the repo
- Commits the version bump to `main`
- Creates an annotated tag `vX.Y.Z` on that commit
- Creates a GitHub Release for the tag
- Publishes artifacts (Docker / PyPI / MCPB)
- Merges `main` back into `beta` so `beta` includes the bump commit

### 3) Verify release outputs

- Confirm a new tag exists: `vX.Y.Z`
- Confirm a GitHub Release exists for the tag
- Confirm artifacts:
  - Docker image published with version `X.Y.Z`
  - PyPI package published (if configured)
  - `unity-mcp-X.Y.Z.mcpb` attached to the GitHub Release

## Required repo settings (Branch Protection)

Because the release workflow pushes commits to `main` and `beta`, branch protection must allow GitHub Actions to push.

Recommended:

- Protect `main`:
  - Require PR before merging (so humans promote via PR)
  - Require approvals / status checks as desired
  - Allow GitHub Actions (or `github-actions[bot]`) to bypass PR requirements (so the release workflow can push the bump commit and tag)

- Protect `beta` (if protected):
  - Allow GitHub Actions (or `github-actions[bot]`) to push (needed for the `main -> beta` sync after release)

## Failure modes and recovery

### Tag already exists

The workflow fails if the computed tag already exists. Pick a different bump type or investigate why a tag already exists for that version.

### Workflow fails after pushing the bump commit

If the workflow pushes the version bump commit to `main` but fails before creating the tag/release:

- Do not immediately rerun the workflow: rerunning will compute a *new* version and bump again.
- Preferred recovery:
  - Fix the underlying issue.
  - Manually create the expected tag on the bump commit and create a GitHub Release for it, or revert the bump commit and re-run the workflow.

### Sync `main` back into `beta` fails

If `main -> beta` merge has conflicts, the workflow will fail.

Recovery:

- Create a PR `main -> beta` and resolve conflicts there, or
- Resolve locally and push to `beta` (if allowed by branch protection).
