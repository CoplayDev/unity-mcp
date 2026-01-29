# MCP for Unity Server

[![MCP](https://badge.mcpx.dev?status=on 'MCP Enabled')](https://modelcontextprotocol.io/introduction)
[![python](https://img.shields.io/badge/Python-3.10+-3776AB.svg?style=flat&logo=python&logoColor=white)](https://www.python.org)
[![License](https://img.shields.io/badge/License-MIT-red.svg 'MIT License')](https://opensource.org/licenses/MIT)
[![Discord](https://img.shields.io/badge/discord-join-red.svg?logo=discord&logoColor=white)](https://discord.gg/y4p8KfzrN4)

Model Context Protocol server for Unity Editor integration. Control Unity through natural language using AI assistants like Claude, Cursor, and more.

**Maintained by [Coplay](https://www.coplay.dev/?ref=unity-mcp)** - This project is not affiliated with Unity Technologies.

💬 **Join our community:** [Discord Server](https://discord.gg/y4p8KfzrN4)

**Required:** Install the [Unity MCP Plugin](https://github.com/CoplayDev/unity-mcp?tab=readme-ov-file#-step-1-install-the-unity-package) to connect Unity Editor with this MCP server. You also need `uvx` (requires [uv](https://docs.astral.sh/uv/)) to run the server.

---

## Installation

### Option 1: PyPI

Install and run directly from PyPI using `uvx`.

**Run Server (HTTP):**

```bash
uvx --from mcpforunityserver mcp-for-unity --transport http --http-url http://localhost:8080
```

**MCP Client Configuration (HTTP):**

```json
{
  "mcpServers": {
    "UnityMCP": {
      "url": "http://localhost:8080/mcp"
    }
  }
}
```

**MCP Client Configuration (stdio):**

```json
{
  "mcpServers": {
    "UnityMCP": {
      "command": "uvx",
      "args": [
        "--from",
        "mcpforunityserver",
        "mcp-for-unity",
        "--transport",
        "stdio"
      ]
    }
  }
}
```

### Option 2: From GitHub Source

Use this to run the latest released version from the repository. Change the version to `main` to run the latest unreleased changes from the repository.

```json
{
  "mcpServers": {
    "UnityMCP": {
      "command": "uvx",
      "args": [
        "--from",
        "git+https://github.com/CoplayDev/unity-mcp@v9.2.0#subdirectory=Server",
        "mcp-for-unity",
        "--transport",
        "stdio"
      ]
    }
  }
}
```

### Option 3: Docker

**Use Pre-built Image:**

```bash
docker run -p 8080:8080 msanatan/mcp-for-unity-server:latest --transport http --http-url http://0.0.0.0:8080
```

**Build Locally:**

```bash
docker build -t unity-mcp-server .
docker run -p 8080:8080 unity-mcp-server --transport http --http-url http://0.0.0.0:8080
```

Configure your MCP client with `"url": "http://localhost:8080/mcp"`.

### Option 4: Local Development

For contributing or modifying the server code:

```bash
# Clone the repository
git clone https://github.com/CoplayDev/unity-mcp.git
cd unity-mcp/Server

# Run with uv
uv run src/main.py --transport stdio
```

---

## Configuration

The server connects to Unity Editor automatically when both are running. Most users do not need to change any settings.

### CLI options

These options apply to the `mcp-for-unity` command (whether run via `uvx`, Docker, or `python src/main.py`).

- `--transport {stdio,http}` - Transport protocol (default: `stdio`)
- `--http-url URL` - Base URL used to derive host/port defaults (default: `http://localhost:8080`)
- `--http-host HOST` - Override HTTP bind host (overrides URL host)
- `--http-port PORT` - Override HTTP bind port (overrides URL port)
- `--http-remote-hosted` - Treat HTTP transport as remotely hosted
  - Requires API key authentication (see below)
  - Disables local/CLI-only HTTP routes (`/api/command`, `/api/instances`, `/api/custom-tools`)
  - Forces explicit Unity instance selection for MCP tool/resource calls
  - Isolates Unity sessions per user
- `--api-key-validation-url URL` - External endpoint to validate API keys (required when `--http-remote-hosted` is set)
- `--api-key-login-url URL` - URL where users can obtain/manage API keys (served by `/api/auth/login-url`)
- `--api-key-cache-ttl SECONDS` - Cache duration for validated keys (default: `300`)
- `--api-key-service-token-header HEADER` - Header name for server-to-auth-service authentication (e.g. `X-Service-Token`)
- `--api-key-service-token TOKEN` - Token value sent to the auth service for server authentication
- `--default-instance INSTANCE` - Default Unity instance to target (project name, hash, or `Name@hash`)
- `--project-scoped-tools` - Keep custom tools scoped to the active Unity project and enable the custom tools resource
- `--unity-instance-token TOKEN` - Optional per-launch token set by Unity for deterministic lifecycle management
- `--pidfile PATH` - Optional path where the server writes its PID on startup (used by Unity-managed terminal launches)

### Environment variables

- `UNITY_MCP_TRANSPORT` - Transport protocol: `stdio` or `http`
- `UNITY_MCP_HTTP_URL` - HTTP server URL (default: `http://localhost:8080`)
- `UNITY_MCP_HTTP_HOST` - HTTP bind host (overrides URL host)
- `UNITY_MCP_HTTP_PORT` - HTTP bind port (overrides URL port)
- `UNITY_MCP_HTTP_REMOTE_HOSTED` - Enable remote-hosted mode (`true`, `1`, or `yes`)
- `UNITY_MCP_DEFAULT_INSTANCE` - Default Unity instance to target (project name, hash, or `Name@hash`)
- `UNITY_MCP_SKIP_STARTUP_CONNECT=1` - Skip initial Unity connection attempt on startup

API key authentication (remote-hosted mode):

- `UNITY_MCP_API_KEY_VALIDATION_URL` - External endpoint to validate API keys
- `UNITY_MCP_API_KEY_LOGIN_URL` - URL where users can obtain/manage API keys
- `UNITY_MCP_API_KEY_CACHE_TTL` - Cache TTL for validated keys in seconds (default: `300`)
- `UNITY_MCP_API_KEY_SERVICE_TOKEN_HEADER` - Header name for server-to-auth-service authentication
- `UNITY_MCP_API_KEY_SERVICE_TOKEN` - Token value sent to the auth service for server authentication

Telemetry:

- `DISABLE_TELEMETRY=1` - Disable anonymous telemetry (opt-out)
- `UNITY_MCP_DISABLE_TELEMETRY=1` - Same as `DISABLE_TELEMETRY`
- `MCP_DISABLE_TELEMETRY=1` - Same as `DISABLE_TELEMETRY`
- `UNITY_MCP_TELEMETRY_ENDPOINT` - Override telemetry endpoint URL
- `UNITY_MCP_TELEMETRY_TIMEOUT` - Override telemetry request timeout (seconds)

### Examples

**Stdio (default):**

```bash
uvx --from mcpforunityserver mcp-for-unity --transport stdio
```

**HTTP (local):**

```bash
uvx --from mcpforunityserver mcp-for-unity --transport http --http-host 127.0.0.1 --http-port 8080
```

**HTTP (remote-hosted with API key auth):**

```bash
uvx --from mcpforunityserver mcp-for-unity \
  --transport http \
  --http-host 0.0.0.0 \
  --http-port 8080 \
  --http-remote-hosted \
  --api-key-validation-url https://auth.example.com/api/validate-key \
  --api-key-login-url https://app.example.com/api-keys
```

**Disable telemetry:**

```bash
DISABLE_TELEMETRY=1 uvx --from mcpforunityserver mcp-for-unity --transport stdio
```

---

## Remote-Hosted Mode

When deploying the server as a shared remote service (e.g. for a team or Asset Store users), enable `--http-remote-hosted` to activate API key authentication and per-user session isolation.

**Requirements:**

- An external HTTP endpoint that validates API keys. The server POSTs `{"api_key": "..."}` and expects `{"valid": true, "user_id": "..."}` or `{"valid": false}` in response.
- `--api-key-validation-url` must be provided (or `UNITY_MCP_API_KEY_VALIDATION_URL`). The server exits with code 1 if this is missing.

**What changes in remote-hosted mode:**

- All MCP tool/resource calls and Unity plugin WebSocket connections require a valid `X-API-Key` header.
- Each user only sees Unity instances that connected with their API key (session isolation).
- Auto-selection of a sole Unity instance is disabled; users must explicitly call `set_active_instance`.
- CLI REST routes (`/api/command`, `/api/instances`, `/api/custom-tools`) are disabled.
- `/health` and `/api/auth/login-url` remain accessible without authentication.

**MCP client config with API key:**

```json
{
  "mcpServers": {
    "UnityMCP": {
      "url": "http://remote-server:8080/mcp",
      "headers": {
        "X-API-Key": "<your-api-key>"
      }
    }
  }
}
```

For full details, see [Remote Server Auth Guide](../docs/guides/REMOTE_SERVER_AUTH.md) and [Architecture Reference](../docs/reference/REMOTE_SERVER_AUTH_ARCHITECTURE.md).

---

## MCP Resources

The server provides read-only MCP resources for querying Unity Editor state. Resources provide up-to-date information about your Unity project without modifying it.

**Accessing Resources:**

Resources are accessed by their URI (not their name). Always use `ListMcpResources` to get the correct URI format.

**Example URIs:**
- `mcpforunity://editor/state` - Editor readiness snapshot
- `mcpforunity://project/tags` - All project tags
- `mcpforunity://scene/gameobject/{instance_id}` - GameObject details by ID
- `mcpforunity://prefab/{encoded_path}` - Prefab info by asset path

**Important:** Resource names use underscores (e.g., `editor_state`) but URIs use slashes/hyphens (e.g., `mcpforunity://editor/state`). Always use the URI from `ListMcpResources()` when reading resources.

**All resource descriptions now include their URI** for easy reference. List available resources to see the complete catalog with URIs.

---

## Example Prompts

Once connected, try these commands in your AI assistant:

- "Create a 3D player controller with WASD movement"
- "Add a rotating cube to the scene with a red material"
- "Create a simple platformer level with obstacles"
- "Generate a shader that creates a holographic effect"
- "List all GameObjects in the current scene"

---

## Documentation

For complete documentation, troubleshooting, and advanced usage:

📖 **[Full Documentation](https://github.com/CoplayDev/unity-mcp#readme)**

---

## Requirements

- **Python:** 3.10 or newer
- **Unity Editor:** 2021.3 LTS or newer
- **uv:** Python package manager ([Installation Guide](https://docs.astral.sh/uv/getting-started/installation/))

---

## License

MIT License - See [LICENSE](https://github.com/CoplayDev/unity-mcp/blob/main/LICENSE)
