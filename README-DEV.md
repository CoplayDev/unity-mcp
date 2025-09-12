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

## Switching MCP package sources quickly

Run this from the unity-mcp repo, not your game's roote directory. Use `mcp_source.py` to quickly switch between different MCP for Unity package sources:

**Usage:**
```bash
python mcp_source.py [--manifest /path/to/manifest.json] [--repo /path/to/unity-mcp] [--choice 1|2|3]
```

**Options:**
- **1** Upstream main (CoplayDev/unity-mcp)
- **2** Remote current branch (origin + branch)
- **3** Local workspace (file: UnityMcpBridge)

After switching, open Package Manager and Refresh to re-resolve packages.

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

### `prune_tool_results.py`
Compacts large `tool_result` blobs in conversation JSON into concise one-line summaries.

**Usage:**
```bash
python3 prune_tool_results.py < reports/claude-execution-output.json > reports/claude-execution-output.pruned.json
```

The script reads a conversation from `stdin` and writes the pruned version to `stdout`, making logs much easier to inspect or archive.

These defaults dramatically cut token usage without affecting essential information.

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

## MCP Bridge Stress Test

An on-demand stress utility exercises the MCP bridge with multiple concurrent clients while triggering real script reloads via immediate script edits (no menu calls required).

### Script
- `tools/stress_mcp.py`

### What it does
- Starts N TCP clients against the Unity MCP bridge (default port auto-discovered from `~/.unity-mcp/unity-mcp-status-*.json`).
- Sends lightweight framed `ping` keepalives to maintain concurrency.
- In parallel, appends a unique marker comment to a target C# file using `manage_script.apply_text_edits` with:
  - `options.refresh = "immediate"` to force an import/compile immediately (triggers domain reload), and
  - `precondition_sha256` computed from the current file contents to avoid drift.
- Uses EOF insertion to avoid header/`using`-guard edits.

### Usage (local)
```bash
# Recommended: use the included large script in the test project
python3 tools/stress_mcp.py \
  --duration 60 \
  --clients 8 \
  --unity-file "TestProjects/UnityMCPTests/Assets/Scripts/LongUnityScriptClaudeTest.cs"
```

Flags:
- `--project` Unity project path (auto-detected to the included test project by default)
- `--unity-file` C# file to edit (defaults to the long test script)
- `--clients` number of concurrent clients (default 10)
- `--duration` seconds to run (default 60)

### Expected outcome
- No Unity Editor crashes during reload churn
- Immediate reloads after each applied edit (no `Assets/Refresh` menu calls)
- Some transient disconnects or a few failed calls may occur during domain reload; the tool retries and continues
- JSON summary printed at the end, e.g.:
  - `{"port": 6400, "stats": {"pings": 28566, "applies": 69, "disconnects": 0, "errors": 0}}`

### Notes and troubleshooting
- Immediate vs debounced:
  - The tool sets `options.refresh = "immediate"` so changes compile instantly. If you only need churn (not per-edit confirmation), switch to debounced to reduce mid-reload failures.
- Precondition required:
  - `apply_text_edits` requires `precondition_sha256` on larger files. The tool reads the file first to compute the SHA.
- Edit location:
  - To avoid header guards or complex ranges, the tool appends a one-line marker at EOF each cycle.
- Read API:
  - The bridge currently supports `manage_script.read` for file reads. You may see a deprecation warning; it's harmless for this internal tool.
- Transient failures:
  - Occasional `apply_errors` often indicate the connection reloaded mid-reply. Edits still typically apply; the loop continues on the next iteration.

### CI guidance
- Keep this out of default PR CI due to Unity/editor requirements and runtime variability.
- Optionally run it as a manual workflow or nightly job on a Unity-capable runner.

## CI Test Workflow (GitHub Actions)

We provide a CI job to run a Natural Language Editing suite against the Unity test project. It spins up a headless Unity container and connects via the MCP bridge. To run from your fork, you need the following GitHub "secrets": an `ANTHROPIC_API_KEY` and Unity credentials (usually `UNITY_EMAIL` + `UNITY_PASSWORD` or `UNITY_LICENSE` / `UNITY_SERIAL`.) These are redacted in logs so never visible.

***To run it***
 - Trigger: In GitHun "Actions" for the repo, trigger `workflow dispatch` (`Claude NL/T Full Suite (Unity live)`).
 - Image: `UNITY_IMAGE` (UnityCI) pulled by tag; the job resolves a digest at runtime. Logs are sanitized.
 - Execution: single pass with immediate per‑test fragment emissions (strict single `<testcase>` per file). A placeholder guard fails fast if any fragment is a bare ID. Staging (`reports/_staging`) is promoted to `reports/` to reduce partial writes.
 - Reports: JUnit at `reports/junit-nl-suite.xml`, Markdown at `reports/junit-nl-suite.md`.
 - Publishing: JUnit is normalized to `reports/junit-for-actions.xml` and published; artifacts upload all files under `reports/`.

### Test target script
- The repo includes a long, standalone C# script used to exercise larger edits and windows:
  - `TestProjects/UnityMCPTests/Assets/Scripts/LongUnityScriptClaudeTest.cs`
  Use this file locally and in CI to validate multi-edit batches, anchor inserts, and windowed reads on a sizable script.

### Adjust tests / prompts
- Edit `.claude/prompts/nl-unity-suite-t.md` to modify the NL/T steps. Follow the conventions: emit one XML fragment per test under `reports/<TESTID>_results.xml`, each containing exactly one `<testcase>` with a `name` that begins with the test ID. No prologue/epilogue or code fences.
- Keep edits minimal and reversible; include concise evidence.

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