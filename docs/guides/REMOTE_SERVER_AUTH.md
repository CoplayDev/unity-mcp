# Remote Server API Key Authentication

When running the MCP for Unity server as a shared remote service, API key authentication ensures that only authorized users can access the server and that each user's Unity sessions are isolated from one another.

This guide covers how to configure, deploy, and use the feature.

## Prerequisites

### External Auth Service

You need an external HTTP endpoint that validates API keys. The server delegates all key validation to this endpoint rather than managing keys itself.

The endpoint must:

- Accept `POST` requests with a JSON body: `{"api_key": "<key>"}`
- Return a JSON response indicating validity and the associated user identity
- Be reachable from the MCP server over the network

See [Validation Contract](#validation-contract) for the full request/response specification.

### Transport Mode

API key authentication is only available when running with HTTP transport (`--transport http`). It has no effect in stdio mode.

## Server Configuration

### CLI Arguments

| Argument | Environment Variable | Default | Description |
| -------- | -------------------- | ------- | ----------- |
| `--http-remote-hosted` | `UNITY_MCP_HTTP_REMOTE_HOSTED` | `false` | Enable remote-hosted mode. Requires API key auth. |
| `--api-key-validation-url URL` | `UNITY_MCP_API_KEY_VALIDATION_URL` | None | External endpoint to validate API keys (required). |
| `--api-key-login-url URL` | `UNITY_MCP_API_KEY_LOGIN_URL` | None | URL where users can obtain or manage API keys. |
| `--api-key-cache-ttl SECONDS` | `UNITY_MCP_API_KEY_CACHE_TTL` | `300` | How long validated keys are cached (seconds). |
| `--api-key-service-token-header HEADER` | `UNITY_MCP_API_KEY_SERVICE_TOKEN_HEADER` | None | Header name for server-to-auth-service authentication. |
| `--api-key-service-token TOKEN` | `UNITY_MCP_API_KEY_SERVICE_TOKEN` | None | Token value sent to the auth service for server authentication. |

Environment variables take effect when the corresponding CLI argument is not provided. For boolean flags, set the env var to `true`, `1`, or `yes`.

### Startup Validation

The server validates its configuration at startup:

- If `--http-remote-hosted` is set but `--api-key-validation-url` is not provided (and the env var is also unset), the server logs an error and exits with code 1.

### Example

```bash
python -m src.main \
  --transport http \
  --http-host 0.0.0.0 \
  --http-port 8080 \
  --http-remote-hosted \
  --api-key-validation-url https://auth.example.com/api/validate-key \
  --api-key-login-url https://app.example.com/api-keys \
  --api-key-cache-ttl 120
```

Or using environment variables:

```bash
export UNITY_MCP_TRANSPORT=http
export UNITY_MCP_HTTP_HOST=0.0.0.0
export UNITY_MCP_HTTP_PORT=8080
export UNITY_MCP_HTTP_REMOTE_HOSTED=true
export UNITY_MCP_API_KEY_VALIDATION_URL=https://auth.example.com/api/validate-key
export UNITY_MCP_API_KEY_LOGIN_URL=https://app.example.com/api-keys

python -m src.main
```

### Service Token (Optional)

If your auth service requires the MCP server to authenticate itself (server-to-server auth), configure a service token:

```bash
--api-key-service-token-header X-Service-Token \
--api-key-service-token "your-server-secret"
```

This adds the specified header to every validation request sent to the auth endpoint.

We strongly recommend using this feature because it ensures that the entity requesting validation is the MCP server itself, not an imposter.

## Unity Plugin Setup

When connecting to a remote-hosted server, Unity users need to provide their API key:

1. Open the MCP for Unity window in the Unity Editor.
2. Select HTTP Remote as the connection mode.
3. Enter the API key in the API Key field. The key is stored in `EditorPrefs` (per-machine, not source-controlled).
4. Click **Get API Key** to open the login URL in a browser if you need a new key. This fetches the URL from the server's `/api/auth/login-url` endpoint.

The API key is a one-time entry per machine. It persists across Unity sessions until explicitly cleared.

## MCP Client Configuration

When an API key is configured, the Unity plugin's MCP client configurators automatically include the `X-API-Key` header in generated configuration files.

Example generated config for **Cursor** (`~/.cursor/mcp.json`):

```json
{
  "mcpServers": {
    "mcp-for-unity": {
      "url": "http://remote-server:8080/mcp",
      "headers": {
        "X-API-Key": "<your-api-key>"
      }
    }
  }
}
```

Example for **Claude Code** (CLI):

```bash
claude mcp add --transport http mcp-for-unity http://remote-server:8080/mcp \
  --header "X-API-Key: <your-api-key>"
```

Similar header injection works for VS Code, Windsurf, Cline, and other supported MCP clients.

## Behaviour Changes in Remote-Hosted Mode

Enabling `--http-remote-hosted` changes several server behaviours compared to the default local mode:

### Authentication Enforcement

All MCP tool and resource calls require a valid API key. The `X-API-Key` header must be present on every HTTP request to the `/mcp` endpoint. If the key is missing or invalid, the middleware raises a `RuntimeError` that surfaces as an MCP error response.

### WebSocket Auth Gate

Unity plugins connecting via WebSocket (`/hub/plugin`) are validated during the handshake:

| Scenario | WebSocket Close Code | Reason |
| -------- | -------------------- | ------ |
| No API key header | `4401` | API key required |
| Invalid API key | `4403` | Invalid API key |
| Auth service unavailable | `1013` | Try again later |
| Valid API key | Connection accepted | user_id stored in connection state |

### Session Isolation

Each user can only see and interact with their own Unity instances. When User A calls `set_active_instance` or lists instances, they only see Unity editors that connected with User A's API key. User B's sessions are invisible to User A.

### Auto-Select Disabled

In local mode, the server automatically selects the sole connected Unity instance. In remote-hosted mode, this auto-selection is disabled. Users must explicitly call `set_active_instance` with a `Name@hash` from the `mcpforunity://instances` resource.

### CLI Routes Disabled

The following REST endpoints are disabled in remote-hosted mode to prevent unauthenticated access:

- `POST /api/command`
- `GET /api/instances`
- `GET /api/custom-tools`

### Endpoints Always Available

These endpoints remain accessible regardless of auth:

| Endpoint | Method | Purpose |
| -------- | ------ | ------- |
| `/health` | GET | Health check for load balancers and monitoring |
| `/api/auth/login-url` | GET | Returns the login URL for API key management |

## Validation Contract

### Request

```http
POST <api-key-validation-url>
Content-Type: application/json

{
  "api_key": "<the-api-key>"
}
```

If a service token is configured, an additional header is sent:

```http
<service-token-header>: <service-token-value>
```

### Response (Valid Key)

```json
{
  "valid": true,
  "user_id": "user-abc-123",
  "metadata": {}
}
```

- `valid` (bool, required): Must be `true`.
- `user_id` (string, required): Stable identifier for the user. Used for session isolation.
- `metadata` (object, optional): Arbitrary metadata stored alongside the validation result.

### Response (Invalid Key)

```json
{
  "valid": false,
  "error": "API key expired"
}
```

- `valid` (bool, required): Must be `false`.
- `error` (string, optional): Human-readable reason.

### Response (HTTP 401)

A `401` status code is also treated as an invalid key (no body parsing required).

### Timeouts and Retries

- Request timeout: 5 seconds
- Retries: 1 (with 100ms backoff)
- Failure mode: deny by default (treated as invalid on any error)

Transient failures (5xx, timeouts, network errors) are **not cached**, so subsequent requests will retry the auth service.

## Error Reference

| Context | Condition | Response |
| ------- | --------- | -------- |
| MCP tool/resource | Missing API key (remote-hosted) | `RuntimeError` → MCP `isError: true` |
| MCP tool/resource | Invalid API key | `RuntimeError` → MCP `isError: true` |
| WebSocket connect | Missing API key | Close `4401` "API key required" |
| WebSocket connect | Invalid API key | Close `4403` "Invalid API key" |
| WebSocket connect | Auth service down | Close `1013` "Try again later" |
| `/api/auth/login-url` | Login URL not configured | HTTP `404` with admin guidance message |
| Server startup | Remote-hosted without validation URL | `SystemExit(1)` |

## Troubleshooting

### "API key authentication required" error on every tool call

The server is in remote-hosted mode but no API key is being sent. Ensure the MCP client configuration includes the `X-API-Key` header, or set it in the Unity plugin's connection settings.

### Server exits immediately with code 1

The `--http-remote-hosted` flag requires `--api-key-validation-url`. Provide the URL via CLI argument or `UNITY_MCP_API_KEY_VALIDATION_URL` environment variable.

### WebSocket connection closes with 4401

The Unity plugin is not sending an API key. Enter the key in the MCP for Unity window's connection settings.

### WebSocket connection closes with 1013

The external auth service is unreachable. Check network connectivity between the MCP server and the validation URL. The Unity plugin can retry the connection.

### User cannot see their Unity instance

Session isolation is active. The Unity editor and the MCP client must use API keys that resolve to the same `user_id`. Verify that the Unity plugin's WebSocket connection and the MCP client's HTTP requests use the same API key.

### Stale auth after key rotation

Validated keys are cached for `--api-key-cache-ttl` seconds (default: 300). After rotating or revoking a key, there is a delay equal to the TTL before the old key stops working. Lower the TTL for faster revocation at the cost of more frequent validation requests.
