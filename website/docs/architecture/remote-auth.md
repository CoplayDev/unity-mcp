# Remote Server Auth: Architecture

This document describes the internal design of the API key authentication system used when the MCP for Unity server runs in remote-hosted mode. It is intended for contributors and maintainers.

## Overview

```
MCP Client                    MCP Server                      External Auth
(Cursor, etc.)               (Python)                         Service
     |                            |                               |
     |  X-API-Key: abc123        |                               |
     |  POST /mcp (tool call)    |                               |
     |-------------------------->|                               |
     |                           |                               |
     |          UnityInstanceMiddleware.on_call_tool              |
     |                           |                               |
     |                   _resolve_user_id()                      |
     |                           |                               |
     |                           |  POST /validate               |
     |                           |  {"api_key": "abc123"}        |
     |                           |------------------------------>|
     |                           |                               |
     |                           |  {"valid":true,               |
     |                           |   "user_id":"user-42"}        |
     |                           |<------------------------------|
     |                           |                               |
     |                   Cache result (TTL)                      |
     |                           |                               |
     |            session user_id + active instance              |
     |            request effective target                      |
     |                           |                               |
     |            PluginHub.send_command_for_instance             |
     |            (user_id scoped session lookup)                 |
     |                           |                               |
     |  Tool result              |                               |
     |<--------------------------|                               |


Unity Plugin                  MCP Server                      External Auth
(C# WebSocket)               (Python)                         Service
     |                            |                               |
     |  WS /hub/plugin            |                               |
     |  X-API-Key: abc123        |                               |
     |-------------------------->|                               |
     |                           |                               |
     |              PluginHub.on_connect                          |
     |                           |  POST /validate               |
     |                           |------------------------------>|
     |                           |  {"valid":true, ...}          |
     |                           |<------------------------------|
     |                           |                               |
     |  accept()                 |                               |
     |  websocket.state.user_id = "user-42"                      |
     |<--------------------------|                               |
     |                           |                               |
     |  {"type":"register", ...} |                               |
     |-------------------------->|                               |
     |                           |                               |
     |          PluginRegistry.register(                          |
     |              ..., user_id="user-42")                      |
     |          _user_hash_to_session[("user-42","hash")] = sid  |
     |                           |                               |
     |  {"type":"registered"}    |                               |
     |<--------------------------|                               |
```

## Components

### ApiKeyService

**File:** `Server/src/services/api_key_service.py`

Singleton service that validates API keys against an external HTTP endpoint.

- **Singleton access:** `ApiKeyService.get_instance()` / `ApiKeyService.is_initialized()`
- **Initialization:** Constructed in `create_mcp_server()` when `config.http_remote_hosted` and `config.api_key_validation_url` are both set.
- **Validation:** `async validate(api_key) -> ValidationResult`
- **Caching:** In-memory dict keyed by raw API key. Entries store `(valid, user_id, metadata, expires_at)`.
- **Retry:** 1 retry with 100ms backoff on timeouts and connection errors.
- **Fail-closed:** Any unrecoverable error returns `ValidationResult(valid=False)`.

### PluginHub (WebSocket Auth Gate)

**File:** `Server/src/transport/plugin_hub.py`

The `on_connect` method validates the API key from the WebSocket handshake headers before accepting the connection.

- Reads `X-API-Key` from `websocket.headers`
- Validates via `ApiKeyService.validate()`
- Stores `user_id` and `api_key_metadata` on `websocket.state` for use during registration
- Rejects with close codes: `4401` (missing), `4403` (invalid), `1013` (service unavailable)

The `_handle_register` method reads `websocket.state.user_id` and passes it to `PluginRegistry.register()`.

The `get_sessions(user_id=None)` and `_resolve_session_id(unity_instance, user_id=None)` methods accept an optional `user_id` to scope session queries in remote-hosted mode.

### PluginRegistry (Dual-Index Session Storage)

**File:** `Server/src/transport/plugin_registry.py`

In-memory registry of connected Unity plugin sessions. Maintains two parallel index maps:

| Index | Key | Used In |
|-------|-----|---------|
| `_hash_to_session` | `project_hash -> session_id` | Local mode |
| `_user_hash_to_session` | `(user_id, project_hash) -> session_id` | Remote-hosted mode |

Both indexes are updated during `register()` and cleaned up during `unregister()`.

Key methods:

- `register(session_id, project_name, project_hash, unity_version, user_id=None)` - Registers a session and updates the appropriate index. If an existing session claims the same key, it is evicted.
- `get_session_id_by_hash(project_hash)` - Local-mode lookup.
- `get_session_id_by_hash(project_hash, user_id)` - Remote-mode lookup.
- `list_sessions(user_id=None)` - Returns sessions filtered by user. Raises `ValueError` if `user_id` is `None` while `config.http_remote_hosted` is `True`, preventing accidental cross-user leaks.

### UnityInstanceMiddleware

**File:** `Server/src/transport/unity_instance_middleware.py`

FastMCP middleware that resolves the authenticated user and effective Unity
target for every tool call and resource read. The authenticated user and active
instance are MCP-session state; an explicit per-call target is request-local.

Entry points:

- `on_call_tool(context, call_next)` - Intercepts tool calls.
- `on_read_resource(context, call_next)` - Intercepts resource reads.

Both delegate to `_inject_unity_instance(context)`, which:

1. Calls `_resolve_user_id()` to extract the user identity from the HTTP request.
2. If remote-hosted mode is active and no `user_id` is resolved, raises `RuntimeError` (surfaces as MCP error).
3. Stores the authenticated user in MCP session state so discovery and selector resolution are user-scoped.
4. Consumes an explicit `unity_instance` from a targetable tool envelope or resource URI query, if present.
5. Otherwise reads the active instance from FastMCP session state and applies the existing auto/default behavior when allowed.
6. Stores the resulting effective target in request-scoped `unity_instance` state with `serializable=False`.

The explicit target is never written to the session's active-instance key. This
keeps concurrent calls in one MCP session isolated from one another.

### _resolve_user_id_from_request

**File:** `Server/src/transport/unity_transport.py`

Extracts the `user_id` from the current HTTP request's `X-API-Key` header.

```
_resolve_user_id_from_request()
  -> if not config.http_remote_hosted: return None
  -> if not ApiKeyService.is_initialized(): return None
  -> get_http_headers() from FastMCP dependencies
  -> extract "x-api-key" header
  -> ApiKeyService.validate(api_key)
  -> return result.user_id if valid, else None
```

The middleware calls this indirectly through `_resolve_user_id()`, which adds an early return when not in remote-hosted mode (avoiding the import of FastMCP internals in local mode).

## Request Lifecycle

A complete authenticated MCP tool call or resource read follows this path:

1. **HTTP request arrives** at `/mcp` with `X-API-Key: <key>` header.

2. **FastMCP dispatches** the MCP request through its middleware chain.

3. **The middleware entry point** is invoked: `on_call_tool` for a tool call or `on_read_resource` for a resource read.

4. **`_inject_unity_instance`** runs:
   - Calls `_resolve_user_id()`, which calls `_resolve_user_id_from_request()`.
   - The request function imports `get_http_headers` from FastMCP and reads the `x-api-key` header.
   - `ApiKeyService.validate()` checks the cache or calls the external auth endpoint.
   - If valid, `user_id` is returned. If invalid or missing, `None` is returned.
   - In remote-hosted mode, `None` causes a `RuntimeError`.

5. **`user_id` stored in MCP session state** and used to scope visible Unity sessions.

6. **Explicit target consumed**, if present:
   - For a targetable tool, `unity_instance` is removed from the tool argument envelope before FastMCP validates the business arguments.
   - For a targetable resource, `unity_instance` is removed from the URI before FastMCP matches the resource template.
   - The selector is resolved against the authenticated user's sessions. Invalid and ambiguous selectors fail without fallback.

7. **Session active instance looked up** from FastMCP's session-scoped state. If no active instance exists, the existing auto/default behavior runs; remote-hosted mode disables auto-selection.

8. **Effective target injected** via request-scoped `ctx.set_state("unity_instance", target, serializable=False)`.

9. **Tool or resource executes**, reading the effective target from request state. A call without an explicit selector still uses the active session target.

10. **Command routed** through the user-scoped PluginHub session. The `unity_instance` routing envelope is Python-only and is not included in Unity command `params`.

## WebSocket Auth Flow

When a Unity plugin connects via WebSocket:

```
Plugin -> WS /hub/plugin (with X-API-Key header)
  |
  v
PluginHub.on_connect()
  |
  +-- config.http_remote_hosted && ApiKeyService.is_initialized()?
  |     |
  |     +-- No  -> accept() (local mode, no auth needed)
  |     |
  |     +-- Yes -> read X-API-Key from headers
  |           |
  |           +-- No key -> close(4401, "API key required")
  |           |
  |           +-- ApiKeyService.validate(key)
  |                 |
  |                 +-- valid=True  -> websocket.state.user_id = user_id
  |                 |                  accept()
  |                 |
  |                 +-- valid=False, "unavailable" in error
  |                 |                -> close(1013, "Try again later")
  |                 |
  |                 +-- valid=False -> close(4403, "Invalid API key")
```

After acceptance, when the plugin sends a `register` message, `_handle_register` reads `websocket.state.user_id` and passes it to `PluginRegistry.register()`.

## Session Registry Design

### Local Mode

```
project_hash  ->  session_id
"abc123"      ->  "uuid-1"
"def456"      ->  "uuid-2"
```

A single `_hash_to_session` dict. Any user can see any session. `list_sessions(user_id=None)` returns all sessions.

### Remote-Hosted Mode

```
(user_id, project_hash)    ->  session_id
("user-A", "abc123")       ->  "uuid-1"
("user-B", "abc123")       ->  "uuid-3"   (same project, different user)
("user-A", "def456")       ->  "uuid-2"
```

A separate `_user_hash_to_session` dict with composite keys. Two users working on cloned repos (same `project_hash`) get independent sessions.

### Reconnect Handling

When a Unity editor reconnects (e.g., after domain reload), `register()` detects the existing mapping for the same key and evicts the old session before inserting the new one. This ensures the latest WebSocket connection always wins.

### list_sessions Guard

`list_sessions(user_id=None)` raises `ValueError` when `config.http_remote_hosted` is `True`. This prevents code paths from accidentally listing all users' sessions. Every call site in remote-hosted mode must pass an explicit `user_id`.

## Caching Strategy

`ApiKeyService` maintains an in-memory cache:

```python
# api_key -> (valid, user_id, metadata, expires_at)
_cache: dict[str, tuple[bool, str | None, dict | None, float]]
```

### What Gets Cached

| Response | Cached? | Rationale |
|----------|---------|-----------|
| 200 + `valid: true` | Yes | Definitive valid result |
| 200 + `valid: false` | Yes | Definitive invalid result |
| 401 status | Yes | Definitive rejection |
| 5xx status | No | Transient; retry on next request |
| Timeout | No | Transient; retry on next request |
| Connection error | No | Transient; retry on next request |
| Unexpected exception | No | Transient; retry on next request |

Non-cacheable results use `ValidationResult(cacheable=False)`.

### Cache Lifecycle

- **TTL:** Configurable via `--api-key-cache-ttl` (default: 300 seconds).
- **Expiry:** Checked on read. Expired entries are deleted and re-validated.
- **Invalidation:** `invalidate_cache(api_key)` removes a single key. `clear_cache()` removes all.
- **Concurrency:** Protected by `asyncio.Lock`.

### Revocation Latency

A revoked key continues to work for up to `cache_ttl` seconds. Lower the TTL for faster revocation at the cost of more validation requests.

## Fail-Closed Behaviour

The system fails closed at every boundary:

| Component | Failure | Behaviour |
|-----------|---------|-----------|
| `ApiKeyService._validate_external` | Timeout after retries | `valid=False, cacheable=False` |
| `ApiKeyService._validate_external` | Connection error after retries | `valid=False, cacheable=False` |
| `ApiKeyService._validate_external` | 5xx status | `valid=False, cacheable=False` |
| `ApiKeyService._validate_external` | Unexpected exception | `valid=False, cacheable=False` |
| `PluginHub.on_connect` | Auth service unavailable | Close `1013` (retry hint) |
| `UnityInstanceMiddleware._inject_unity_instance` | No user_id in remote-hosted mode | `RuntimeError` |

API keys are never logged in full. Keys longer than 8 characters are redacted to `xxxx...yyyy` in log messages.

## Session and request state

FastMCP stores persistent state under the MCP session ID. MCP for Unity uses
session-scoped state for authentication and the normal selection, and
request-scoped state for the effective target of one request:

| State | Scope | Purpose |
|-------|-------|---------|
| `user_id` | MCP session | Authenticated identity used to scope remote session discovery |
| `mcpforunity.active_instance` | MCP session | The instance selected by `set_active_instance` |
| `unity_instance` | Current request (`serializable=False`) | The resolved target used by one tool call or resource read |

The active selection is isolated by FastMCP's session ID, not by a peer-supplied
`client_id` and not by a process-global dictionary. An explicit per-call target
is resolved after authentication and is never written to the active-instance
state. This allows concurrent requests in one session to target different
Editors without changing the session default.

In remote-hosted mode, the authenticated `user_id` limits instance discovery
and selector resolution. The same user's MCP sessions still keep independent
active selections.

## Disabled Features in Remote-Hosted Mode

| Feature | Local Mode | Remote-Hosted Mode | Reason |
|---------|-----------|-------------------|--------|
| Auto-select sole instance | Enabled | Disabled | Implicit behaviour is dangerous with multiple users |
| CLI REST routes | Enabled | Disabled | No auth layer on these endpoints |
| `list_sessions(user_id=None)` | Returns all | Raises `ValueError` | Prevents accidental cross-user session leaks |

## Configuration Flow

```
CLI args / env vars
       |
       v
main.py: parser.parse_args()
       |
       +-- config.http_remote_hosted = args or env
       +-- config.api_key_validation_url = args or env
       +-- config.api_key_login_url = args or env
       +-- config.api_key_cache_ttl = args or env (float)
       +-- config.api_key_service_token_header = args or env
       +-- config.api_key_service_token = args or env
       |
       +-- Validate: remote-hosted requires validation URL
       |     (exits with code 1 if missing)
       |
       v
create_mcp_server()
       |
       +-- get_unity_instance_middleware()  ->  registers middleware
       |
       +-- if remote-hosted + validation URL:
       |     ApiKeyService(
       |       validation_url, cache_ttl,
       |       service_token_header, service_token
       |     )
       |
       +-- WebSocketRoute("/hub/plugin", PluginHub)
       |
       +-- if not remote-hosted:
             register CLI routes (/api/command, /api/instances, /api/custom-tools)
```

## Key Files

| File | Role |
|------|------|
| `Server/src/core/config.py` | `ServerConfig` dataclass with auth fields |
| `Server/src/main.py` | CLI argument parsing, startup validation, service initialization |
| `Server/src/services/api_key_service.py` | API key validation singleton with caching and retry |
| `Server/src/transport/plugin_hub.py` | WebSocket auth gate, user-scoped session queries |
| `Server/src/transport/plugin_registry.py` | Dual-index session storage (local + user-scoped) |
| `Server/src/transport/unity_instance_middleware.py` | User-scoped discovery, session active-instance state, and request-local effective-target injection |
| `Server/src/transport/unity_transport.py` | `_resolve_user_id_from_request` helper |
