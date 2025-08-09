# Unity MCP Development Tools

Welcome to the Unity MCP development environment! This directory contains tools and utilities to streamline Unity MCP core development.

## 🚀 Available Development Features

### ✅ Development Deployment Scripts
Quick deployment and testing tools for Unity MCP core changes.

### 🔄 Coming Soon
- **Development Mode Toggle**: Built-in Unity editor development features
- **Hot Reload System**: Real-time code updates without Unity restarts  
- **Plugin Development Kit**: Tools for creating custom Unity MCP extensions
- **Automated Testing Suite**: Comprehensive testing framework for contributions
- **Debug Dashboard**: Advanced debugging and monitoring tools

---

## Development Deployment Scripts

These deployment scripts help you quickly test changes to Unity MCP core code.

## Scripts

### `deploy-dev.bat`
Deploys your development code to the actual installation locations for testing.

**What it does:**
1. Backs up original files to a timestamped folder
2. Copies Unity Bridge code to Unity's package cache
3. Copies Python Server code to the MCP installation folder

**Usage:**
1. Run `deploy-dev.bat`
2. Enter Unity package cache path (example provided)
3. Enter server path (or use default: `%LOCALAPPDATA%\Programs\UnityMCP\UnityMcpServer\src`)
4. Enter backup location (or use default: `%USERPROFILE%\Desktop\unity-mcp-backup`)

### `restore-dev.bat`
Restores original files from backup.

**What it does:**
1. Lists available backups with timestamps
2. Allows you to select which backup to restore
3. Restores both Unity Bridge and Python Server files

## Finding Unity Package Cache Path

Unity stores Git packages under a version-or-hash folder. Expect something like:
```
X:\UnityProject\Library\PackageCache\com.coplaydev.unity-mcp@<version-or-hash>
```
Example (hash):
```
X:\UnityProject\Library\PackageCache\com.coplaydev.unity-mcp@272123cfd97e
```

To find it reliably:
1. Open Unity Package Manager
2. Select "Unity MCP" package
3. Right click the package and choose "Show in Explorer"
4. That opens the exact cache folder Unity is using for your project

Note: In recent builds, the Python server sources are also bundled inside the package under `UnityMcpServer~/src`. This is handy for local testing or pointing MCP clients directly at the packaged server.

## Workflow

1. **Make changes** to your source code in this directory
2. **Deploy** using `deploy-dev.bat`
3. **Test** in Unity (restart Unity Editor first)
4. **Iterate** - repeat steps 1-3 as needed
5. **Restore** original files when done using `restore-dev.bat`


## Troubleshooting

### "Path not found" errors running the .bat file
- Verify Unity package cache path is correct
- Check that Unity MCP package is actually installed
- Ensure server is installed via MCP client

### "Permission denied" errors
- Run cmd as Administrator
- Close Unity Editor before deploying
- Close any MCP clients before deploying

### "Backup not found" errors
- Run `deploy-dev.bat` first to create initial backup
- Check backup directory permissions
- Verify backup directory path is correct