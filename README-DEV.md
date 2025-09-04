# MCP for Unity Development Tools

Welcome to the MCP for Unity development environment! This directory contains tools and utilities to streamline MCP for Unity core development.

## 🚀 Available Development Features

### ✅ Development Deployment Scripts
Quick deployment and testing tools for MCP for Unity core changes.

### 🔄 Coming Soon
- **Development Mode Toggle**: Built-in Unity editor development features
- **Hot Reload System**: Real-time code updates without Unity restarts  
- **Plugin Development Kit**: Tools for creating custom MCP for Unity extensions
- **Automated Testing Suite**: Comprehensive testing framework for contributions
- **Debug Dashboard**: Advanced debugging and monitoring tools

---

## Development Deployment Scripts

These deployment scripts help you quickly test changes to MCP for Unity core code.

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

**Note:** Dev deploy skips `.venv`, `__pycache__`, `.pytest_cache`, `.mypy_cache`, `.git`; reduces churn and avoids copying virtualenvs.

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
2. Select "MCP for Unity" package
3. Right click the package and choose "Show in Explorer"
4. That opens the exact cache folder Unity is using for your project

Note: In recent builds, the Python server sources are also bundled inside the package under `UnityMcpServer~/src`. This is handy for local testing or pointing MCP clients directly at the packaged server.

## CI Test Workflow (GitHub Actions)

We provide a CI job to run a Natural Language Editing mini-suite against the Unity test project. It spins up a headless Unity container and connects via the MCP bridge.

- Trigger: Workflow dispatch (`Claude NL suite (Unity live)`).
- Image: `UNITY_IMAGE` (UnityCI) pulled by tag; the job resolves a digest at runtime. Logs are sanitized.
- Reports: JUnit at `reports/junit-nl-suite.xml`, Markdown at `reports/junit-nl-suite.md`.
- Publishing: JUnit is normalized to `reports/junit-for-actions.xml` and published; artifacts upload all files under `reports/`.

### Test target script
- The repo includes a long, standalone C# script used to exercise larger edits and windows:
  - `TestProjects/UnityMCPTests/Assets/Scripts/LongUnityScriptClaudeTest.cs`
  Use this file locally and in CI to validate multi-edit batches, anchor inserts, and windowed reads on a sizable script.

### Add a new NL test
- Edit `.claude/prompts/nl-unity-claude-tests-mini.md` (or `nl-unity-suite-full.md` for the larger suite).
- Follow the conventions: single `<testsuite>` root, one `<testcase>` per sub-test, end system-out with `VERDICT: PASS|FAIL`.
- Keep edits minimal and reversible; include evidence windows and compact diffs.

### Run the suite
1) Push your branch, then manually run the workflow from the Actions tab.
2) The job writes reports into `reports/` and uploads artifacts.
3) The “JUnit Test Report” check summarizes results; open the Job Summary for full markdown.

### View results
- Job Summary: inline markdown summary of the run on the Actions tab in GitHub
- Check: “JUnit Test Report” on the PR/commit.
- Artifacts: `claude-nl-suite-artifacts` includes XML and MD.


### MCP Connection Debugging
- *Enable debug logs* in the Unity MCP window (inside the Editor) to view connection status, auto-setup results, and MCP client paths. It shows:
  - bridge startup/port, client connections, strict framing negotiation, and parsed frames
  - auto-config path detection (Windows/macOS/Linux), uv/claude resolution, and surfaced errors
- In CI, the job tails Unity logs (redacted for serial/license/password/token) and prints socket/status JSON diagnostics if startup fails.
## Workflow

1. **Make changes** to your source code in this directory
2. **Deploy** using `deploy-dev.bat`
3. **Test** in Unity (restart Unity Editor first)
4. **Iterate** - repeat steps 1-3 as needed
5. **Restore** original files when done using `restore-dev.bat`


## Switching MCP package sources quickly

Use `mcp_source.py` to quickly switch between different MCP for Unity package sources:

**Usage:**
```bash
python mcp_source.py [--manifest /path/to/manifest.json] [--repo /path/to/unity-mcp] [--choice 1|2|3]
```

**Options:**
- **1** Upstream main (CoplayDev/unity-mcp)
- **2** Remote current branch (origin + branch)
- **3** Local workspace (file: UnityMcpBridge)

After switching, open Package Manager and Refresh to re-resolve packages.


## Troubleshooting

### "Path not found" errors running the .bat file
- Verify Unity package cache path is correct
- Check that MCP for Unity package is actually installed
- Ensure server is installed via MCP client

### "Permission denied" errors
- Run cmd as Administrator
- Close Unity Editor before deploying
- Close any MCP clients before deploying

### "Backup not found" errors
- Run `deploy-dev.bat` first to create initial backup
- Check backup directory permissions
- Verify backup directory path is correct

### Windows uv path issues
- On Windows, when testing GUI clients, prefer the WinGet Links `uv.exe`; if multiple `uv.exe` exist, use "Choose `uv` Install Location" to pin the Links shim.