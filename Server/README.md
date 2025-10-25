# MCP for Unity Server

[![MCP](https://badge.mcpx.dev?status=on 'MCP Enabled')](https://modelcontextprotocol.io/introduction)
[![python](https://img.shields.io/badge/Python-3.11+-3776AB.svg?style=flat&logo=python&logoColor=white)](https://www.python.org)
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
uvx --from git+https://github.com/CoplayDev/unity-mcp@v6.3.0#subdirectory=Server mcp-for-unity
```

**MCP Client Configuration:**

```json
{
  "mcpServers": {
    "UnityMCP": {
      "command": "uvx",
      "args": [
        "--from",
        "git+https://github.com/CoplayDev/unity-mcp@v6.3.0#subdirectory=Server",
        "mcp-for-unity"
      ]
    }
  }
}
```

### Option 2: Using uv (Local Installation)

For local development or custom installations:

```bash
# Clone the repository
git clone https://github.com/CoplayDev/unity-mcp.git
cd unity-mcp/Server

# Run with uv
uv run server.py
```

**MCP Client Configuration:**

**Windows:**
```json
{
  "mcpServers": {
    "UnityMCP": {
      "command": "uv",
      "args": [
        "run",
        "--directory",
        "C:\\path\\to\\unity-mcp\\Server",
        "server.py"
      ]
    }
  }
}
```

**macOS/Linux:**
```json
{
  "mcpServers": {
    "UnityMCP": {
      "command": "uv",
      "args": [
        "run",
        "--directory",
        "/path/to/unity-mcp/Server",
        "server.py"
      ]
    }
  }
}
```

### Option 3: Using Docker

```bash
docker build -t unity-mcp-server .
docker run unity-mcp-server
```

**MCP Client Configuration:**

```json
{
  "mcpServers": {
    "UnityMCP": {
      "command": "docker",
      "args": ["run", "-i", "unity-mcp-server"]
    }
  }
}
```

---

## Configuration

The server connects to Unity Editor automatically when both are running. No additional configuration needed.

**Environment Variables:**

- `DISABLE_TELEMETRY=true` - Opt out of anonymous usage analytics
- `LOG_LEVEL=DEBUG` - Enable detailed logging (default: INFO)

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

- **Python:** 3.11 or newer
- **Unity Editor:** 2021.3 LTS or newer
- **uv:** Python package manager ([Installation Guide](https://docs.astral.sh/uv/getting-started/installation/))

---

## License

MIT License - See [LICENSE](https://github.com/CoplayDev/unity-mcp/blob/main/LICENSE)
