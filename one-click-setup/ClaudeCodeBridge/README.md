# Unity MCP Setup for Claude Code

## 🚀 ONE-CLICK SETUP

This Unity project includes everything needed for Claude Code integration!

### Single Step Required:
```bash
# Double-click this file:
start-unity-mcp-universal.bat
```

**That's it!** This single command:
- ✅ Creates Python environment automatically
- ✅ Installs all dependencies
- ✅ Starts Unity MCP Server (bundled)
- ✅ Starts Claude Code Bridge
- ✅ Connects to Unity Editor

**No Git required!** **No additional downloads!** Everything is bundled.

## What's Included

1. ✅ **Unity MCP Bridge Package** - In Packages/UnityMcpBridge/
2. ✅ **Unity MCP Server** - Bundled in unity-mcp/UnityMcpServer/
3. ✅ **Claude Code Bridge** - HTTP API for Claude Code integration
4. ✅ **Universal Launcher** - One-click setup script

## Available Unity MCP Commands

Once connected, Claude Code can:
- **Manage GameObjects**: Create, modify, delete objects in your scenes
- **Edit Scripts**: Modify your C# scripts directly in Unity
- **Control Scenes**: Switch scenes, create new ones, modify scene settings
- **Manage Assets**: Work with prefabs, materials, and other assets
- **Execute Editor Commands**: Run Unity menu items and editor functions
- **Read Console**: Monitor Unity's console output

## Requirements

- Python 3.12+ (install from https://python.org)
- Unity 6000.0.37f1 or compatible version
- Windows (WSL users: see notes in launcher)

## Troubleshooting

**Unity Bridge not starting?**
- Check Unity Console for "UnityMcpBridge started on port 6400"
- Restart Unity if needed

**Server not responding?**
- Ensure Python 3.12+ is installed
- Run the Universal Launcher as Administrator if needed
- Check logs in the `logs/` directory

**WSL/Linux users:**
- Use the network IP shown in Flask startup messages
- Example: `curl http://YOUR_NETWORK_IP:6501/health`

## Benefits for Your Character Controller Project

With Unity MCP, Claude Code can now:
- **Directly test character controller feel** by adjusting parameters in real-time
- **Instantly iterate on UI/UX** by modifying interfaces and getting immediate feedback  
- **Set up enemy AI quickly** by creating and testing components
- **Optimize performance** by monitoring Unity profiler and making targeted improvements
- **Rapid prototyping** for new features without manual file editing

## Support
- Issues: https://github.com/justinpbarnett/unity-mcp/issues
- Unity MCP Bridge by justinpbarnett