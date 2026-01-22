### Cursor/VSCode/Windsurf: UV path issue on Windows (diagnosis and fix)

#### The issue
- Some Windows machines have multiple `uv.exe` locations. Our auto-config sometimes picked a less stable path, causing the MCP client to fail to launch the MCP for Unity Server or for the path to be auto-rewritten on repaint/restart.

#### Typical symptoms
- Cursor shows the MCP for Unity server but never connects or reports it “can’t start.”
- Your `%USERPROFILE%\\.cursor\\mcp.json` flips back to a different `command` path when Unity or the MCP for Unity window refreshes.

#### Real-world example
- Wrong/fragile path (auto-picked):
  - `C:\Users\mrken.local\bin\uv.exe` (malformed, not standard)
  - `C:\Users\mrken\AppData\Local\Microsoft\WinGet\Packages\astral-sh.uv_Microsoft.Winget.Source_8wekyb3d8bbwe\uv.exe`
- Correct/stable path (works with Cursor):
  - `C:\Users\mrken\AppData\Local\Microsoft\WinGet\Links\uv.exe`

#### Quick fix (recommended)
1) In MCP for Unity: `Window > MCP for Unity` → select your MCP client (Cursor or Windsurf)
2) If you see “uv Not Found,” click “Choose `uv` Install Location” and browse to:
   - `C:\Users\<YOU>\AppData\Local\Microsoft\WinGet\Links\uv.exe`
3) If uv is already found but wrong, still click “Choose `uv` Install Location” and select the `Links\uv.exe` path above. This saves a persistent override.
4) Click “Auto Configure” (or re-open the client) and restart Cursor.

This sets an override stored in the Editor (key: `MCPForUnity.UvPath`) so MCP for Unity won’t auto-rewrite the config back to a different `uv.exe` later.

#### Verify the fix
- Confirm global Cursor config is at: `%USERPROFILE%\\.cursor\\mcp.json`
- You should see something like:

```json
{
  "mcpServers": {
    "unityMCP": {
      "command": "C:\\Users\\YOU\\AppData\\Local\\Microsoft\\WinGet\\Links\\uv.exe",
      "args": [
        "--directory",
        "C:\\Users\\YOU\\AppData\\Local\\Programs\\UnityMCP\\UnityMcpServer\\src",
        "run",
        "server.py"
      ]
    }
  }
}
```

- Manually run the same command in PowerShell to confirm it launches:

```powershell
"C:\Users\YOU\AppData\Local\Microsoft\WinGet\Links\uv.exe" --directory "C:\Users\YOU\AppData\Local\Programs\UnityMCP\UnityMcpServer\src" run server.py
```

If that runs without error, restart Cursor and it should connect.

#### Why this happens
- On Windows, multiple `uv.exe` can exist (WinGet Packages path, a WinGet Links shim, Python Scripts, etc.). The Links shim is the most stable target for GUI apps to launch.
- Prior versions of the auto-config could pick the first found path and re-write config on refresh. Choosing a path via the MCP window pins a known‑good absolute path and prevents auto-rewrites.

#### Extra notes
- Restart Cursor after changing `mcp.json`; it doesn’t always hot-reload that file.
- If you also have a project-scoped `.cursor\\mcp.json` in your Unity project folder, that file overrides the global one.


### Why pin the WinGet Links shim (and not the Packages path)

- Windows often has multiple `uv.exe` installs and GUI clients (Cursor/Windsurf/VSCode) may launch with a reduced `PATH`. Using an absolute path is safer than `"command": "uv"`.
- WinGet publishes stable launch shims in these locations:
  - User scope: `%LOCALAPPDATA%\Microsoft\WinGet\Links\uv.exe`
  - Machine scope: `C:\Program Files\WinGet\Links\uv.exe`
  These shims survive upgrades and are intended as the portable entrypoints. See the WinGet notes: [discussion](https://github.com/microsoft/winget-pkgs/discussions/184459) • [how to find installs](https://superuser.com/questions/1739292/how-to-know-where-winget-installed-a-program)
- The `Packages` root is where payloads live and can change across updates, so avoid pointing your config at it.

Recommended practice

- Prefer the WinGet Links shim paths above. If present, select one via “Choose `uv` Install Location”.
- If the unity window keeps rewriting to a different `uv.exe`, pick the Links shim again; MCP for Unity saves a pinned override and will stop auto-rewrites.
- If neither Links path exists, a reasonable fallback is `~/.local/bin/uv.exe` (uv tools bin) or a Scoop shim, but Links is preferred for stability.

References

- WinGet portable Links: [GitHub discussion](https://github.com/microsoft/winget-pkgs/discussions/184459)
- WinGet install locations: [Super User](https://superuser.com/questions/1739292/how-to-know-where-winget-installed-a-program)
- GUI client PATH caveats (Cursor): [Cursor community thread](https://forum.cursor.com/t/mcp-feature-client-closed-fix/54651?page=4)
- uv tools install location (`~/.local/bin`): [Astral docs](https://docs.astral.sh/uv/concepts/tools/)


