# WTL Internal Terminal

Internal MCP for Unity editor module that adds a terminal window backed by:

- Unity EditorWindow
- Native IMGUI terminal renderer
- Node.js backend
- xterm/headless terminal emulator
- node-pty shell sessions

Open it from `Window > WTL > Internal Terminal > Open`.

## Location

This is no longer a standalone UPM package. It lives inside the MCP for Unity package at `MCPForUnity/Editor/InternalTerminal`.

## Requirements

- Node.js available on `PATH`
- npm available on `PATH`
- A platform shell available on `PATH`
- The package includes `Sarasa Mono SC` for stable Chinese/English monospace rendering. Optional: enter a different installed font family in preferences.

This plugin is intentionally internal-only: it does not open an external browser and does not embed a native browser child window. Unity draws the terminal text and colors directly inside the EditorWindow.

## First Run

The plugin runs `npm install` automatically inside `MCPForUnity/Editor/InternalTerminal/NodeBackend~` the first time the terminal starts. This installs `ws`, `@xterm/headless`, and `node-pty`.

On Windows, `node-pty` may need the Visual Studio C++ build tools if no prebuilt binary exists for your Node version.

By default the backend uses the platform shell: `pwsh.exe` on Windows when available, then `powershell.exe`, then `cmd.exe`; on macOS/Linux it uses the `SHELL` environment variable.

Rendering uses the font configured in `Preferences > WTL > Internal Terminal > Terminal Font`. The default value `auto` uses the bundled `Sarasa Mono SC` font, avoiding Unity IMGUI's unreliable OS font lookup. You can still enter an installed font family name manually. The backend does not replace terminal characters; use a font with enough glyph coverage to avoid missing symbols and column drift.

## Unity TCP Bridge

When launched from Unity, the terminal ensures Unity's local stdio TCP bridge is running and injects connection details into the shell:

- `UNITY_MCP_INTERNAL_HOST`
- `UNITY_MCP_INTERNAL_PORT`
- `UNITY_MCP_INTERNAL_ROLE`
- `UNITY_MCP_INTERNAL_CLIENT_ID`

The terminal also writes dedicated MCP client configuration and prepends generated wrappers to `PATH`. Running `codex` or `claude` from the internal terminal automatically overrides Unity MCP for that session with the internal stdio bridge, even when the project's normal MCP client configuration uses HTTP.

```bash
codex
claude
```

## Clipboard Images

On Windows, pasting while the terminal is focused first checks the OS clipboard for an image. If one is present, the plugin saves it as a PNG under the Unity project's `Temp/WTLInternalTerminal/ClipboardImages` directory and pastes the generated local file path into the terminal. This lets Claude Code, Codex, or other terminal agents receive the image as an accessible file instead of trying to push binary image data through the pty stream.

Text paste remains unchanged when the clipboard does not contain an image.

## Add to Agent API

Custom Editor UI can call `WTL.InternalTerminal.Editor.InternalTerminalAgentContext` to wire an `Add to Agent` button without depending on the package's menu implementation:

```csharp
using UnityEditor;
using UnityEngine;
using WTL.InternalTerminal.Editor;

public sealed class MyAgentPanel : EditorWindow
{
    private Object target;

    private void OnGUI()
    {
        target = EditorGUILayout.ObjectField(target, typeof(Object), true);

        if (GUILayout.Button("Add to Agent"))
        {
            InternalTerminalAgentContext.AddObjectToAgent(target);
        }
    }
}
```

Useful entry points:

- `AddSelectionToAgent()`
- `AddObjectToAgent(Object item)`
- `AddObjectsToAgent(IEnumerable<Object> items)`
- `AddAssetPathToAgent(string path)`
- `AddConsoleToAgent()`
- `AddConsoleEntryToAgent()`
- `PasteTextToAgent(string text)`

## Notes

- The backend only listens on `127.0.0.1`.
- Each websocket connection gets its own pty.
- Closing the Unity window stops the Node backend process started by this plugin.
