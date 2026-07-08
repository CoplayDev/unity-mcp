---
id: testing
slug: /contributing/testing
title: Testing
sidebar_label: Testing
description: How to run Python and Unity tests locally, what CI runs, and how to add new tests.
---

# Testing

Three test suites cover MCP for Unity: Python unit tests, Unity EditMode/PlayMode tests, and a multi-version Unity compile matrix. CI runs all three; you should run the two relevant to your change locally before pushing.

## Python tests

Location: `Server/tests/`

```bash
# All tests
cd Server && uv run pytest tests/ -v

# Single file
cd Server && uv run pytest tests/test_manage_material.py -v

# Single test by name pattern
cd Server && uv run pytest tests/ -k "test_create_material" -v
```

CI workflow: `.github/workflows/python-tests.yml`. Coverage is uploaded to Codecov on every run.

### Adding a Python test

For a new tool `manage_<domain>`, add `Server/tests/test_manage_<domain>.py`. Existing tests are the best template — most are integration-style: they spin up a fake Unity bridge, call the tool, and assert on the dispatched payload.

## Unity tests

Location: `TestProjects/UnityMCPTests/Assets/Tests/`

- **EditMode** (60+ files): tool validation, parser edge cases, scene paging, domain reload resilience, batch execution, AI property matching, scriptable objects, animation, physics, gameobject lifecycle
- **PlayMode**: basic integration smoke tests

To run locally, open `TestProjects/UnityMCPTests` in Unity, then **Window → General → Test Runner**.

CI runs both modes across a multi-Unity matrix via `.github/workflows/unity-tests.yml`.

### Local headless test harness

One command boots a headless Hub-licensed Editor against `TestProjects/UnityMCPTests` and runs the smoke + EditMode + PlayMode legs over the bridge, then tears down:

```bash
python tools/local_harness.py
```

This is the same entrypoint CI uses — `.github/workflows/e2e-bridge.yml` collapses its boot/wait/discover/run-smoke shell into this invocation.

Key flags:

- `--legs smoke,editmode,playmode` — subset of legs to run.
- `--project-path TestProjects/UnityMCPTests` — Unity project to boot (repo-relative or absolute).
- `--reuse` — attach to an already-resident bridge instead of booting one.
- `--keep-alive` — leave the Editor running after the legs (no teardown).
- `--no-warmup` — skip the warm-up import phase.

Exit-code contract: `0` all blocking legs passed, `1` a blocking-leg regression, `2` bridge unreachable / setup failure, `3` project does not compile, `4` no Unity license / Hub seat, `5` Editor binary/version not found.

It needs a Hub-activated Editor locally (no ULF/serial); none of the CI license staging applies.

### Adding a Unity test

Mirror the C# tool you're adding. For `ManageNavigation.cs` in `MCPForUnity/Editor/Tools/`, create `TestProjects/UnityMCPTests/Assets/Tests/EditMode/Editor/ManageNavigationTests.cs`. Use the existing assembly definition (`MCPForUnityTests.Editor.asmdef`) so the suite picks it up automatically.

## Multi-version compile matrix

This is the most common pre-push surprise: code that builds on your Unity version fails on another supported version because of an API rename. The local matrix check prevents that.

```bash
tools/check-unity-versions.sh           # compile-only across installed Unity Hub editors
tools/check-unity-versions.sh --full    # full EditMode test run on each version
```

The matrix is `tools/unity-versions.json`. The script discovers Unity installations via Unity Hub's standard locations on macOS, Windows, and Linux.

When you touch anything in `MCPForUnity/Runtime/Helpers/Unity*Compat.cs` or any `#if UNITY_*_OR_NEWER` block, run this. The [Unity Compat Shims](/architecture/unity-compat) doc explains the policy.

## Pre-push hook

`tools/install-hooks.sh` installs a pre-push hook that runs `check-unity-versions.sh` in compile-only mode when your push touches Unity-relevant paths. One-time setup:

```bash
tools/install-hooks.sh
```

To bypass for a single push: `git push --no-verify`. Use this when you're pushing docs-only or pure Python changes.

## Pre-commit hook (docs reference)

The same install script wires a pre-commit hook that regenerates `website/docs/reference/` whenever you stage a change under `Server/src/services/{tools,resources,registry}/`. CI fails if you skip this and the committed reference drifts — see `.github/workflows/docs-generate.yml`.

## Stress / load testing

Two scripts under `tools/`:

- `stress_mcp.py` — concurrent MCP tool calls; surfaces middleware contention
- `stress_editor_state.py` — hammers the `editor_state` resource; surfaces serialization hotspots

These are not part of CI; run them when you change transport, middleware, or hot-path serialization.

## What CI actually runs on every PR

| Workflow | Trigger | Duration | What it asserts |
|---|---|---|---|
| `python-tests.yml` | `Server/**` changes | ~2 min | `pytest` clean, coverage uploaded |
| `unity-tests.yml` | `MCPForUnity/**` / `TestProjects/**` changes | ~15 min × N versions | EditMode + PlayMode tests clean across the matrix |
| `docs-deploy.yml` | `website/**`, `docs/**`, tool/resource registry changes | ~1 min (build) | Docusaurus build succeeds; on push to `beta`, deploys to GitHub Pages |
| `docs-generate.yml` | same triggers as docs-deploy | ~1 min | Reference docs are not stale; decorator count matches MD count |

Skip-equivalent: if the only files you changed are README, governance, or unrelated metadata, only the relevant subset of these fires.
