# MCP for Unity Server

[![MCP](https://badge.mcpx.dev?status=on 'MCP Enabled')](https://modelcontextprotocol.io/introduction)
[![python](https://img.shields.io/badge/Python-3.10+-3776AB.svg?style=flat&logo=python&logoColor=white)](https://www.python.org)
[![License](https://img.shields.io/badge/License-MIT-red.svg 'MIT License')](https://opensource.org/licenses/MIT)
[![Discord](https://img.shields.io/badge/discord-join-red.svg?logo=discord&logoColor=white)](https://discord.gg/y4p8KfzrN4)

Model Context Protocol server for Unity Editor integration. Control Unity through natural language using AI assistants like Claude, Cursor, and more.

**Maintained by [Coplay](https://www.coplay.dev/?ref=unity-mcp)** - This project is not affiliated with Unity Technologies.

💬 **Join our community:** [Discord Server](https://discord.gg/y4p8KfzrN4)

**Required:** Install the [Unity MCP Plugin](https://github.com/CoplayDev/unity-mcp?tab=readme-ov-file#-step-1-install-the-unity-package) to connect Unity Editor with this MCP server.

---

## Installation

### Option 1: Using uvx (Recommended)

Run directly from GitHub without installation:

```bash
# HTTP (default)
uvx --from git+https://github.com/CoplayDev/unity-mcp@v8.1.6#subdirectory=Server \
    mcp-for-unity --transport http --http-url http://localhost:8080

```


### Option 2: Using uv (Local Installation)

For local development or custom installations:

```bash
# Clone the repository
git clone https://github.com/CoplayDev/unity-mcp.git
cd unity-mcp/Server

# Run with uv (HTTP)
uv run server.py --transport http --http-url http://localhost:8080

# Run with uv (stdio)
uv run server.py --transport stdio
```

**MCP Client Configuration (HTTP):**
```json
{
  "mcpServers": {
    "UnityMCP": {
      "url": "http://localhost:8080/mcp",
      "headers": { "X-API-Key": "<your key>" }
    }
  }
}
```

**MCP Client Configuration (stdio – Windows):**
```json
{
  "mcpServers": {
    "UnityMCP": {
      "command": "uv",
      "args": [
        "run",
        "--directory",
        "C:\\path\\to\\unity-mcp\\Server",
        "server.py",
        "--transport",
        "stdio"
      ]
    }
  }
}
```

**MCP Client Configuration (stdio – macOS/Linux):**
```json
{
  "mcpServers": {
    "UnityMCP": {
      "command": "uv",
      "args": [
        "run",
        "--directory",
        "/path/to/unity-mcp/Server",
        "server.py",
        "--transport",
        "stdio"
      ]
    }
  }
}
```

### Option 3: Using Docker

```bash
docker build -t unity-mcp-server .
docker run -p 8080:8080 unity-mcp-server --transport http --http-url http://0.0.0.0:8080
```

Configure your MCP client with `"url": "http://localhost:8080/mcp"` and include the `X-API-Key` header. For stdio-in-docker (rare), run the container with `--transport stdio` and use the same `command`/`args` pattern as the uv examples, wrapping it in `docker run -i ...` if needed.

---

## Configuration
The server connects to Unity Editor automatically when both are running.

**Authentication (optional; disabled by default)**
- Toggle with `--auth-enabled` (or `UNITY_MCP_AUTH_ENABLED=1`).
- Allowlist with `--allowed-ips "127.0.0.1,10.0.0.0/8"` (or `UNITY_MCP_ALLOWED_IPS`). Default `*`.
- Token with `--auth-token <value>` (or `UNITY_MCP_AUTH_TOKEN`). If omitted while enabled, the server generates one; empty string skips token checks.
- Token file lives at `api_key` (auto-created when needed):
  - macOS: `~/Library/Application Support/UnityMCP/api_key`
  - Windows: `%LOCALAPPDATA%\UnityMCP/api_key`
  - Linux: `~/.local/share/UnityMCP/api_key`
- HTTP clients send `X-API-Key: <key>`. When auth is disabled, no auth headers are required.

**Environment/flags**
- `DISABLE_TELEMETRY=true` - Opt out of anonymous usage analytics
- `LOG_LEVEL=DEBUG` - Enable detailed logging (default: INFO)

---
