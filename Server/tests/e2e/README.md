# Deterministic bridge E2E

`bridge_smoke.py` drives a **live Unity Editor** over the real wire path
(`send_command_with_retry`) with a fixed sequence of tool calls and exact
assertions. It is the no-LLM counterpart to `claude-nl-suite.yml`: deterministic,
free (no Anthropic API key), and therefore safe to gate PRs.

It is **not** collected by `pytest tests/` (the filename is not `test_*.py`), so
the normal unit suite never tries to reach a Unity instance.

## Run locally

Start a Unity Editor with the MCP bridge active, then:

```bash
cd Server
uv run python tests/e2e/bridge_smoke.py            # auto-discovers the instance
uv run python tests/e2e/bridge_smoke.py --instance MyProject@<hash>
```

Exit codes: `0` all steps passed · `1` a step failed an assertion (real bridge
regression) · `2` no Unity bridge reachable (setup problem, not a contract bug).

## CI

`.github/workflows/e2e-bridge.yml` boots headless Unity via
`McpForUnity.Editor.McpCiBoot.StartStdioForCi` (the same step the NL suite uses),
waits for the bridge status file, then runs this driver. It triggers on PRs that
touch `MCPForUnity/Editor`, `MCPForUnity/Runtime`, or `Server/src`, and on
`workflow_dispatch`. It self-skips (does not fail) when Unity license secrets are
absent.

## Adding steps

Append a `Step(...)` in `build_steps()` with a `check(resp)` callback that raises
`AssertionError` on failure. Use `_ok()` / `_result()` to stay tolerant of both
Unity response shapes. Keep new objects uniquely named (see the `_RUN` suffix) and
delete anything you create so reruns stay clean.
