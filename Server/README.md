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
  - Disables local/CLI-only HTTP routes (`/api/command`, `/api/instances`)
  - Forces explicit Unity instance selection for MCP tool/resource calls
- `--default-instance INSTANCE` - Default Unity instance to target (project name, hash, or `Name@hash`)
- `--project-scoped-tools` - Keep custom tools scoped to the active Unity project and enable the custom tools resource
- `--unity-instance-token TOKEN` - Optional per-launch token set by Unity for deterministic lifecycle management
- `--pidfile PATH` - Optional path where the server writes its PID on startup (used by Unity-managed terminal launches)

### Environment variables

- `UNITY_MCP_TRANSPORT` - Transport protocol: `stdio` or `http`
- `UNITY_MCP_HTTP_URL` - HTTP server URL (default: `http://localhost:8080`)
- `UNITY_MCP_HTTP_HOST` - HTTP bind host (overrides URL host)
- `UNITY_MCP_HTTP_PORT` - HTTP bind port (overrides URL port)
- `UNITY_MCP_DEFAULT_INSTANCE` - Default Unity instance to target (project name, hash, or `Name@hash`)
- `UNITY_MCP_SKIP_STARTUP_CONNECT=1` - Skip initial Unity connection attempt on startup

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

**HTTP (remote-hosted):**

```bash
uvx --from mcpforunityserver mcp-for-unity --transport http --http-host 0.0.0.0 --http-port 8080 --http-remote-hosted
```

**Disable telemetry:**

```bash
DISABLE_TELEMETRY=1 uvx --from mcpforunityserver mcp-for-unity --transport stdio
```

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
