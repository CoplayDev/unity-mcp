# Security Hardening Pass (`harden/security`)

This fork applies a defense-in-depth hardening pass so the bridge is safe to run
on a **single-user machine** pointed at a **disposable, version-controlled Unity
project**. It does not try to remove the Editor's code-execution capability
(`manage_script` needs it) — it narrows *who* can reach it and removes the worst
always-on paths. Findings are referenced as R1–R12 from the security audit.

## What changed

| Task | Finding | Change |
|------|---------|--------|
| 1 | R1 | Removed the `runtime_compilation` tool (`CustomTools/RoslynRuntimeCompilation/`) and disabled the Roslyn DLL auto-installer (unverified NuGet fetch-and-run). |
| 2 | R4, R5 | Token-gated every local transport: the stdio TCP bridge requires the shared token as its first framed message (before any stale-client close, fixing the hijack), and HTTP-local `/api/command` + the WebSocket hub require an `X-Bridge-Token` header. Token: `UNITY_MCP_BRIDGE_TOKEN` env, else a `0600` `~/.unity-mcp/bridge-token` written by the Editor. |
| 3 | R9 | The Unity dispatcher's enable/disable is now a real boundary: non-core built-ins no longer default to enabled, so a direct socket call to a non-core tool (e.g. `execute_code`) is rejected. |
| 4 | R6 | `execute_code` (scripting_ext) and `execute_menu_item` are off by default; play-mode entry is opt-in (`AllowPlayMode`). `execute_menu_item` uses a conservative allow-list instead of a one-entry deny-list. |
| 5 | R11 | The launched server is pinned to an exact reviewed PyPI version (`mcpforunityserver==9.7.3`) instead of a floating latest/prerelease. See `server-pin.md`. |
| 6 | R10 | Telemetry is off by default. |
| 7 | R8 | Off-loopback HTTP binds fail loud and refuse to start in local mode unless the bridge-token gate is active. |

## Accepted risks (documented, not fixed)

- **R3 — `manage_script` can execute code via `[InitializeOnLoad]`.** Code
  execution cannot be removed while keeping script authoring, which is a core
  feature. **Compensating controls:** work only in a disposable, git-backed
  project, and require human approval on script-writing tool calls in the MCP
  client (the client's per-tool approval UI is the final backstop). Play-mode is
  also opt-in (Task 4), so authored code does not auto-run on entering play.

- **R12 — no local session isolation.** In local (non-remote-hosted) mode all
  sessions share one map and any client can be routed to any connected Unity
  instance. This is acceptable **only** for single-user use on one trusted
  machine. Do not run this on a shared / CI / multi-user host. Remote-hosted
  mode remains correctly per-user scoped and fails closed.

## Operating assumptions

- One trusted, single-user machine. The bridge token narrows the boundary to
  "processes that know the token," not "any local process," but it is not a
  substitute for OS-level multi-user isolation.
- The MCP client surfaces and gates destructive tool calls. Honor the
  `destructiveHint` annotations.
