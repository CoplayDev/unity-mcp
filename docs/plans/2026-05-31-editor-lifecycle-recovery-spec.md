# Editor Lifecycle Recovery Spec

## Goal

Keep the MCP server usable when the Unity Editor closes, reloads, or restarts.
Tool calls should return structured retryable state or recover when the editor
comes back quickly, instead of surfacing opaque connection failures.

## Scope

This first PR targets stdio/legacy Unity socket transport because that is the
path used by Codex and common desktop clients. HTTP keep-running behavior
already exists separately and should not be rewritten here.

## Behavior

- If no Unity Editor instance can be discovered, return `success=false`,
  `error=editor_offline`, `hint=retry`, and `data.reason=editor_offline`.
- If a Unity instance is discovered but the socket connection is refused,
  return the same structured offline response.
- Before returning offline, wait for a short bounded reconnect window so a tool
  call made during editor restart can complete if the bridge comes back.
- The reconnect window is configurable with
  `UNITY_MCP_EDITOR_RECONNECT_MAX_WAIT_S`.
- Existing domain-reload `reloading` handling remains unchanged.

## Non-goals

- Do not keep a Unity-side bridge alive after the Unity Editor process exits.
- Do not redesign the HTTP plugin hub.
- Do not add a full command queue or multi-agent gateway in this PR.

## Verification

- Add Python tests for offline response classification.
- Add Python tests proving `send_command_with_retry` retries connection lookup
  during the reconnect window.
- Run the focused Python test file.
