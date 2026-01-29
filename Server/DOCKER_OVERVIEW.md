# MCP for Unity Server (Docker Image)

[![MCP](https://badge.mcpx.dev?status=on 'MCP Enabled')](https://modelcontextprotocol.io/introduction)
[![License](https://img.shields.io/badge/License-MIT-red.svg 'MIT License')](https://opensource.org/licenses/MIT)
[![Discord](https://img.shields.io/badge/discord-join-red.svg?logo=discord&logoColor=white)](https://discord.gg/y4p8KfzrN4)

Model Context Protocol server for Unity Editor integration. Control Unity through natural language using AI assistants like Claude, Cursor, and more.

**Maintained by [Coplay](https://www.coplay.dev/?ref=unity-mcp)** - This project is not affiliated with Unity Technologies.

💬 **Join our community:** [Discord Server](https://discord.gg/y4p8KfzrN4)

**Required:** Install the [Unity MCP Plugin](https://github.com/CoplayDev/unity-mcp?tab=readme-ov-file#-step-1-install-the-unity-package) to connect Unity Editor with this MCP server.

---

## Quick Start

### 1. Pull the image

```bash
docker pull msanatan/mcp-for-unity-server:latest
```

### 2. Run the server

```bash
docker run -p 8080:8080 msanatan/mcp-for-unity-server:latest
```

This starts the MCP server on port 8080.

### 3. Configure your MCP Client

Add the following configuration to your MCP client (e.g., Claude Desktop config, Cursor settings):

```json
{
  "mcpServers": {
    "UnityMCP": {
      "url": "http://localhost:8080/mcp"
    }
  }
}
```

---

## Configuration

The server connects to the Unity Editor automatically when both are running. No additional configuration is needed.

**Environment Variables:**

- `DISABLE_TELEMETRY=true` - Opt out of anonymous usage analytics
- `LOG_LEVEL=DEBUG` - Enable detailed logging (default: INFO)

Example running with environment variables:

```bash
docker run -p 8080:8080 -e LOG_LEVEL=DEBUG msanatan/mcp-for-unity-server:latest
```

---

## Remote-Hosted Mode

To deploy as a shared remote service with API key authentication and per-user session isolation, pass `--http-remote-hosted` along with an API key validation URL:

```bash
docker run -p 8080:8080 \
  -e UNITY_MCP_HTTP_REMOTE_HOSTED=true \
  -e UNITY_MCP_API_KEY_VALIDATION_URL=https://auth.example.com/api/validate-key \
  -e UNITY_MCP_API_KEY_LOGIN_URL=https://app.example.com/api-keys \
  msanatan/mcp-for-unity-server:latest
```

In this mode:

- All MCP tool/resource calls and Unity plugin WebSocket connections require a valid `X-API-Key` header.
- Each user only sees Unity instances that connected with their API key.
- Users must explicitly call `set_active_instance` to select a Unity instance.

**Remote-hosted environment variables:**

| Variable | Description |
|----------|-------------|
| `UNITY_MCP_HTTP_REMOTE_HOSTED` | Enable remote-hosted mode (`true`, `1`, or `yes`) |
| `UNITY_MCP_API_KEY_VALIDATION_URL` | External endpoint to validate API keys (required) |
| `UNITY_MCP_API_KEY_LOGIN_URL` | URL where users can obtain/manage API keys |
| `UNITY_MCP_API_KEY_CACHE_TTL` | Cache TTL for validated keys in seconds (default: `300`) |
| `UNITY_MCP_API_KEY_SERVICE_TOKEN_HEADER` | Header name for server-to-auth-service authentication |
| `UNITY_MCP_API_KEY_SERVICE_TOKEN` | Token value sent to the auth service |

**MCP client config with API key:**

```json
{
  "mcpServers": {
    "UnityMCP": {
      "url": "http://your-server:8080/mcp",
      "headers": {
        "X-API-Key": "<your-api-key>"
      }
    }
  }
}
```

For full details, see the [Remote Server Auth Guide](https://github.com/CoplayDev/unity-mcp/blob/main/docs/guides/REMOTE_SERVER_AUTH.md).

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

For complete documentation, troubleshooting, and advanced usage, please visit the GitHub repository:

📖 **[Full Documentation](https://github.com/CoplayDev/unity-mcp#readme)**

---

## License

MIT License - See [LICENSE](https://github.com/CoplayDev/unity-mcp/blob/main/LICENSE)
