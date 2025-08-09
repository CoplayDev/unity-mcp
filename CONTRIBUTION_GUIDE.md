# Contributing Unity MCP One-Click Setup

## Quick Contribution Steps

### 1. Fork and Clone
```bash
# Fork https://github.com/justinpbarnett/unity-mcp on GitHub
git clone https://github.com/YOUR_USERNAME/unity-mcp.git
cd unity-mcp
```

### 2. Create Feature Branch
```bash
git checkout -b feature/one-click-launcher
```

### 3. Add Files
Copy the `one-click-setup/` folder to the root of the unity-mcp repository:

```
unity-mcp/
├── README.md
├── UnityMcpBridge/
├── UnityMcpServer/
└── one-click-setup/           ← New contribution
    ├── README.md
    └── ClaudeCodeBridge/
        ├── README.md
        ├── start-unity-mcp-universal.bat
        └── unity-mcp/UnityMcpServer/
```

### 4. Update Unity Bridge (Optional Enhancement)
If you want to include the smart fallback system, update:
- `UnityMcpBridge/Editor/Helpers/ServerInstaller.cs`

The changes add smart detection for bundled vs git-based installations.

### 5. Commit and Push
```bash
git add .
git commit -m "Add one-click setup for Unity MCP

- Complete self-contained distribution package
- Universal launcher with combined server + bridge
- Smart Git fallback system (works with or without Git)
- Auto Python environment setup
- WSL/Linux compatibility with network IP detection
- Reduces setup time from 30 minutes to 30 seconds
- Suitable for public project distribution

Fixes setup friction and enables wider Unity MCP adoption."

git push origin feature/one-click-launcher
```

### 6. Create Pull Request
1. Go to your forked repository on GitHub
2. Click **"Compare & pull request"**
3. Use the title: **"Add Unity MCP One-Click Setup for Public Distribution"**
4. Copy the content from `PULL_REQUEST_TEMPLATE.md` as the description
5. Submit the pull request

## Key Value Propositions

When submitting, emphasize:

### **Problem Solved**
- Current setup is complex (30+ minutes, requires Git knowledge)
- Barriers prevent Unity MCP adoption
- Not suitable for public project distribution

### **Solution Provided**
- True one-click setup (30 seconds)
- No Git dependencies required
- Self-contained for public distribution
- Maintains backward compatibility

### **Impact**
- **Lower barrier to entry** → More users
- **Better public distribution** → Project sharing
- **Reduced support overhead** → Fewer setup issues
- **Educational friendly** → Demos and tutorials

## Files Included

### Core Contribution:
- `one-click-setup/ClaudeCodeBridge/start-unity-mcp-universal.bat` - Universal launcher
- `one-click-setup/ClaudeCodeBridge/unity-mcp/UnityMcpServer/` - Bundled server
- `one-click-setup/ClaudeCodeBridge/README.md` - User instructions

### Documentation:
- `one-click-setup/README.md` - Technical overview
- This contribution guide

### Optional Enhancement:
- Updated `ServerInstaller.cs` with smart fallback system

## Testing Verification

Before submitting, verify:
- ✅ Works on fresh Windows install (no Git)
- ✅ WSL/Linux compatibility (network IP detection)
- ✅ Unity integration (GameObject creation)
- ✅ Claude Code HTTP API (all endpoints responding)
- ✅ Error handling (clear messages for missing requirements)

## Contribution Benefits

This enhancement:
- **Solves real user pain** - Setup friction is #1 adoption barrier
- **Enables new use cases** - Public project distribution
- **Maintains compatibility** - No breaking changes
- **Professional quality** - Production-ready implementation

## Support

If you need help with the contribution:
1. Check the README files for technical details
2. Test the setup on a fresh system
3. Verify all files are included in the package
4. Ensure the pull request template is complete

**This contribution significantly improves Unity MCP accessibility and adoption potential!** 🚀