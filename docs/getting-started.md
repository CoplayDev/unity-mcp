# Getting Started with Unity MCP

This guide will help you set up Unity MCP for local development with your favorite AI assistant.

## Prerequisites

- **Unity 2022.3 LTS** or newer
- **Python 3.12+**
- **MCP-compatible client** (Claude Desktop, Cursor, etc.)

## Installation

### 1. Install Unity MCP Package

1. Open your Unity project
2. Open **Window > Package Manager**
3. Click **+** and select **Add package from git URL**
4. Enter: `https://github.com/CoplayDev/unity-mcp.git`

### 2. Configure MCP Client

#### For Claude Desktop

Add to your Claude Desktop configuration:

```json
{
  "mcpServers": {
    "unity-mcp": {
      "command": "python",
      "args": ["/path/to/unity-mcp/server.py"],
      "env": {
        "UNITY_PROJECT_PATH": "/path/to/your/unity/project"
      }
    }
  }
}
```

#### For Cursor

Add to your workspace settings:

```json
{
  "mcp.servers": {
    "unity-mcp": {
      "command": "python /path/to/unity-mcp/server.py",
      "env": {
        "UNITY_PROJECT_PATH": "${workspaceFolder}"
      }
    }
  }
}
```

### 3. Test Your Setup

Ask your AI assistant:

> "Create a simple cube in the Unity scene and add a rotation script to it"

If Unity MCP is working correctly, you should see:
- A new cube object in your scene
- A rotation script attached to the cube
- The cube rotating when you play the scene

## Next Steps

- [Development Guide](development.md) - Learn about Unity MCP tools and capabilities
- [API Reference](api-reference.md) - Complete tool and endpoint documentation
- [Production Deployment](production-deployment.md) - Set up Unity Build Service for production

## Troubleshooting

### Common Issues

**Unity MCP not responding:**
- Check that Unity Editor is open
- Verify the Unity project path in MCP configuration
- Check Python path and dependencies

**Permission errors:**
- Ensure Python has permission to access Unity project files
- Check Unity Editor is not blocking external modifications

**MCP client can't find server:**
- Verify Python and script paths are correct
- Check environment variables are properly set
- Restart your MCP client after configuration changes

### Getting Help

- [Discord Community](https://discord.gg/y4p8KfzrN4)
- [GitHub Issues](https://github.com/CoplayDev/unity-mcp/issues)
- [Documentation](../README.md)