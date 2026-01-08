<img width="676" height="380" alt="MCP for Unity" src="docs/images/logo.png" />

| [English](README.md) | [简体中文](README-zh.md) |
|----------------------|---------------------------------|

#### Proudly sponsored and maintained by [Coplay](https://www.coplay.dev/?ref=unity-mcp) -- the best AI assistant for Unity.

[![Discord](https://img.shields.io/badge/discord-join-red.svg?logo=discord&logoColor=white)](https://discord.gg/y4p8KfzrN4)
[![](https://img.shields.io/badge/Website-Visit-purple)](https://www.coplay.dev/?ref=unity-mcp)
[![](https://img.shields.io/badge/Unity-000000?style=flat&logo=unity&logoColor=blue 'Unity')](https://unity.com/releases/editor/archive)
[![Unity Asset Store](https://img.shields.io/badge/Unity%20Asset%20Store-Get%20Package-FF6A00?style=flat&logo=unity&logoColor=white)](https://assetstore.unity.com/packages/tools/generative-ai/mcp-for-unity-ai-driven-development-329908)
[![python](https://img.shields.io/badge/Python-3.10+-3776AB.svg?style=flat&logo=python&logoColor=white)](https://www.python.org)
[![](https://badge.mcpx.dev?status=on 'MCP Enabled')](https://modelcontextprotocol.io/introduction)
![GitHub commit activity](https://img.shields.io/github/commit-activity/w/CoplayDev/unity-mcp)
![GitHub Issues or Pull Requests](https://img.shields.io/github/issues/CoplayDev/unity-mcp)
[![](https://img.shields.io/badge/License-MIT-red.svg 'MIT License')](https://opensource.org/licenses/MIT)

**Create your Unity apps with LLMs!**

MCP for Unity acts as a bridge, allowing AI assistants (Claude, Cursor, Antigravity, VS Code, etc) to interact directly with your Unity Editor via a local **MCP (Model Context Protocol) Client**. Give your LLM tools to manage assets, control scenes, edit scripts, and automate tasks within Unity.

<img alt="MCP for Unity building a scene" src="docs/images/building_scene.gif">

---

### 💬 Join Our [Discord](https://discord.gg/y4p8KfzrN4)

**Get help, share ideas, and collaborate with other MCP for Unity developers!**  

---

## Key Features 🚀

* **🗣️ Natural Language Control:** Instruct your LLM to perform Unity tasks.
* **🛠️ Powerful Tools:** Manage assets, scenes, materials, scripts, and editor functions.
* **🤖 Automation:** Automate repetitive Unity workflows.
* **🧩 Extensible:** Designed to work with various MCP Clients.
* **🌐 HTTP-First Transport:** Ships with HTTP connections enabled by default (stdio is still available as a fallback).

<details open>
  <summary><strong>Tools</strong></summary>

  Your LLM can use functions like:

* `manage_asset`: Performs asset operations (import, create, modify, delete, search, etc.).
* `manage_editor`: Controls editor state (play mode, active tool, tags, layers).
* `manage_gameobject`: Manages GameObjects (create, modify, delete, find, duplicate, move).
* `manage_components`: Manages components on GameObjects (add, remove, set properties).
* `manage_material`: Manages materials (create, set properties, colors, assign to renderers).
* `manage_prefabs`: Performs prefab operations (open/close stage, save, create from GameObject).
* `manage_scene`: Manages scenes (load, save, create, get hierarchy, screenshot).
* `manage_script`: Legacy script operations (create, read, delete). Prefer `apply_text_edits` or `script_apply_edits`.
* `manage_scriptable_object`: Creates and modifies ScriptableObject assets.
* `manage_shader`: Shader CRUD operations (create, read, modify, delete).
* `manage_vfx`: VFX effect operations, including line/trail renderer, particle system, and VisualEffectGraph (in development).
* `batch_execute`: ⚡ **RECOMMENDED** - Executes multiple commands in one batch for 10-100x better performance. Use this for any repetitive operations.
* `find_gameobjects`: Search for GameObjects by name, tag, layer, component, path, or ID (paginated).
* `find_in_file`: Search a C# script with a regex pattern and return matching line numbers and excerpts.
* `read_console`: Gets messages from or clears the Unity console.
* `refresh_unity`: Request asset database refresh and optional compilation.
* `run_tests`: Starts tests asynchronously, returns job_id for polling.
* `get_test_job`: Polls an async test job for progress and results.
* `debug_request_context`: Return the current request context details (client_id, session_id, and meta dump).
* `execute_custom_tool`: Execute project-scoped custom tools registered by Unity.
* `execute_menu_item`: Executes Unity Editor menu items (e.g., "File/Save Project").
* `set_active_instance`: Routes tool calls to a specific Unity instance. Requires `Name@hash` from `unity_instances`.
* `apply_text_edits`: Precise text edits with line/column ranges and precondition hashes.
* `script_apply_edits`: Structured C# method/class edits (insert/replace/delete) with safer boundaries.
* `validate_script`: Fast validation (basic/standard) to catch syntax/structure issues.
* `create_script`: Create a new C# script at the given project path.
* `delete_script`: Delete a C# script by URI or Assets-relative path.
* `get_sha`: Get SHA256 and metadata for a Unity C# script without returning contents.
</details>


<details open>
  <summary><strong>Resources</strong></summary>

  Your LLM can retrieve the following resources:

* `custom_tools` [`mcpforunity://custom-tools`]: Lists custom tools available for the active Unity project.
* `unity_instances` [`mcpforunity://instances`]: Lists all running Unity Editor instances with details (name, path, hash, status, session).
* `menu_items` [`mcpforunity://menu-items`]: All available menu items in the Unity Editor.
* `get_tests` [`mcpforunity://tests`]: All available tests (EditMode + PlayMode) in the Unity Editor.
* `get_tests_for_mode` [`mcpforunity://tests/{mode}`]: All available tests for a specific mode (EditMode or PlayMode).
* `gameobject_api` [`mcpforunity://scene/gameobject-api`]: Documentation for GameObject resources and how to use `find_gameobjects` tool.
* `gameobject` [`mcpforunity://scene/gameobject/{instance_id}`]: Read-only access to GameObject data (name, tag, transform, components, children).
* `gameobject_components` [`mcpforunity://scene/gameobject/{instance_id}/components`]: Read-only access to all components on a GameObject with full property serialization.
* `gameobject_component` [`mcpforunity://scene/gameobject/{instance_id}/component/{component_name}`]: Read-only access to a specific component's properties.
* `editor_active_tool` [`mcpforunity://editor/active-tool`]: Currently active editor tool (Move, Rotate, Scale, etc.) and transform handle settings.
* `editor_prefab_stage` [`mcpforunity://editor/prefab-stage`]: Current prefab editing context if a prefab is open in isolation mode.
* `editor_selection` [`mcpforunity://editor/selection`]: Detailed information about currently selected objects in the editor.
* `editor_state` [`mcpforunity://editor/state`]: Editor readiness snapshot with advice and staleness info.
* `editor_windows` [`mcpforunity://editor/windows`]: All currently open editor windows with titles, types, positions, and focus state.
* `project_info` [`mcpforunity://project/info`]: Static project information (root path, Unity version, platform).
* `project_layers` [`mcpforunity://project/layers`]: All layers defined in TagManager with their indices (0-31).
* `project_tags` [`mcpforunity://project/tags`]: All tags defined in TagManager.
</details>
---

## How It Works 

MCP for Unity connects your tools using two components:

1. **MCP for Unity Bridge:** A Unity package running inside the Editor. (Installed via Package Manager).
2. **MCP for Unity Server:** A Python server that runs locally (from a terminal window) and speaks HTTP/JSON-RPC to your MCP client. The Unity window launches it for you in HTTP mode by default; stdio is still available if you switch transports.

<img width="562" height="121" alt="image" src="https://github.com/user-attachments/assets/9abf9c66-70d1-4b82-9587-658e0d45dc3e" />

---

## Installation ⚙️

### Prerequisites

If you are **not** installing via the Unity Asset Store, you will need to install the following:

  * **Python:** Version 3.10 or newer. [Download Python](https://www.python.org/downloads/)
  * **uv (Python toolchain manager):**
      ```bash
      # macOS / Linux
      curl -LsSf https://astral.sh/uv/install.sh | sh

      # Windows (PowerShell)
      winget install --id=astral-sh.uv  -e

      # Docs: https://docs.astral.sh/uv/getting-started/installation/
      ```

All installations require these:

  * **Unity Hub & Editor:** Version 2021.3 LTS or newer. [Download Unity](https://unity.com/download)
  * **An MCP Client:** : [Claude Desktop](https://claude.ai/download) | [Claude Code](https://github.com/anthropics/claude-code) | [Cursor](https://www.cursor.com/en/downloads) | [Visual Studio Code Copilot](https://code.visualstudio.com/docs/copilot/overview) | [Windsurf](https://windsurf.com) | Others work with manual config

<details> <summary><strong>[Optional] Roslyn for Advanced Script Validation</strong></summary>

  For **Strict** validation level that catches undefined namespaces, types, and methods: 

  **Method 1: NuGet for Unity (Recommended)**
  1. Install [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity)
  2. Go to `Window > NuGet Package Manager`
  3. Search for `Microsoft.CodeAnalysis`, select version 4.14.0, and install the package
  4. Also install package `SQLitePCLRaw.core` and `SQLitePCLRaw.bundle_e_sqlite3`.
  5. Go to `Player Settings > Scripting Define Symbols`
  6. Add `USE_ROSLYN`
  7. Restart Unity

  **Method 2: Manual DLL Installation**
  1. Download Microsoft.CodeAnalysis.CSharp.dll and dependencies from [NuGet](https://www.nuget.org/packages/Microsoft.CodeAnalysis.CSharp/)
  2. Place DLLs in `Assets/Plugins/` folder
  3. Ensure .NET compatibility settings are correct
  4. Add `USE_ROSLYN` to Scripting Define Symbols
  5. Restart Unity

  **Note:** Without Roslyn, script validation falls back to basic structural checks. Roslyn enables full C# compiler diagnostics with precise error reporting.</details>

---
### 🌟 Step 1: Install the Unity Package

#### To install via the Unity Asset Store

1. In your browser, navigate to https://assetstore.unity.com/packages/tools/generative-ai/mcp-for-unity-ai-driven-development-329908
2. Click `Add to My Assets`.
3. In the Unity Editor, go to`Window > Package Manager`.
4. Download and import the asset to your project


#### To install via Git URL

1. Open your Unity project.
2. Go to `Window > Package Manager`.
3. Click `+` -> `Add package from git URL...`.
4. Enter:
    ```
    https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity
    ```
5. Click `Add`.

**Need a stable/fixed version?** Use a tagged URL instead (updates require uninstalling and re-installing):
```
https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#v8.6.0
```

#### To install via OpenUPM

1. Install the [OpenUPM CLI](https://openupm.com/docs/getting-started-cli.html)
2. Open a terminal (PowerShell, Terminal, etc.) and navigate to your Unity project directory
3. Run `openupm add com.coplaydev.unity-mcp`

**Note:** If you installed the MCP Server before Coplay's maintenance, you will need to uninstall the old package before re-installing the new one.

### ⚡️ Step 2: Start the Local HTTP Server (Default)

HTTP transport is enabled out of the box. The Unity window can launch the FastMCP server for you:

1. Open `Window > MCP for Unity`.
2. Make sure the **Transport** dropdown is set to `HTTP Local` (default) and the **HTTP URL** is what you want (defaults to `http://localhost:8080`).
3. Click **Start Server**. Unity spawns a new operating-system terminal running `uv ... server.py --transport http`.
4. Keep that terminal window open while you work; closing it stops the server. Use the **Stop Session** button in the Unity window if you need to tear it down cleanly.

> Prefer stdio? Change the transport dropdown to `Stdio` and Unity will fall back to the embedded TCP bridge instead of launching the HTTP server.

**Manual launch (optional)**

You can also start the server yourself from a terminal—useful for CI or when you want to see raw logs:

```bash
uvx --from "git+https://github.com/CoplayDev/unity-mcp@v8.6.0#subdirectory=Server" mcp-for-unity --transport http --http-url http://localhost:8080
```

Keep the process running while clients are connected.

### 🛠️ Step 3: Configure Your MCP Client
Connect your MCP Client (Claude, Cursor, etc.) to the HTTP server from Step 2 (auto) or via Manual Configuration (below).

For **Claude Desktop** Users, try using our manually scrapped Unity_Skills by downloading and uploading the claude_skill_unity.zip following this [link](https://www.claude.com/blog/skills).

**Option A: Configure Buttons (Recommended for Claude/Cursor/VSC Copilot)**

1. In Unity, go to `Window > MCP for Unity`.
2. Select your Client/IDE from the dropdown.
3. Click the `Configure` Button.  (Or the `Configure All Detected Clients` button will try to configure every client it finds, but takes longer.)
4. Look for a green status indicator 🟢 and "Connected ✓". *(This writes the HTTP `url` pointing at the server you launched in Step 2.)* 

<details><summary><strong>Client-specific troubleshooting</strong></summary>

  - **VSCode**: uses `Code/User/mcp.json` with top-level `servers.unityMCP`, `"type": "http"`, and the URL from Step 2. On Windows, MCP for Unity still prefers an absolute `uv.exe` path when you switch back to stdio.
  - **Cursor / Windsurf** [(**help link**)](https://github.com/CoplayDev/unity-mcp/wiki/1.-Fix-Unity-MCP-and-Cursor,-VSCode-&-Windsurf): if `uv` is missing, the MCP for Unity window shows "uv Not Found" with a quick [HELP] link and a "Choose `uv` Install Location" button.
  - **Claude Code** [(**help link**)](https://github.com/CoplayDev/unity-mcp/wiki/2.-Fix-Unity-MCP-and-Claude-Code): if `claude` isn't found, the window shows "Claude Not Found" with [HELP] and a "Choose Claude Location" button. Unregister now updates the UI immediately.</details>


**Option B: Manual Configuration**

If Auto-Setup fails or you use a different client:

1. **Find your MCP Client's configuration file.** (Check client documentation).
    * *Claude Example (macOS):* `~/Library/Application Support/Claude/claude_desktop_config.json`
    * *Claude Example (Windows):* `%APPDATA%\Claude\claude_desktop_config.json`
2. **Edit the file** to add/update the `mcpServers` section so it points at the HTTP endpoint from Step 2.

<details>
<summary><strong>Click for Client-Specific JSON Configuration Snippets...</strong></summary>

  ---
**Claude Code**

If you're using Claude Code, you can register the MCP server using the below commands:

**macOS:**

```bash
claude mcp add --scope user UnityMCP -- uv --directory /Users/USERNAME/Library/AppSupport/UnityMCP/UnityMcpServer/src run server.py
```

**Windows:**

```bash
claude mcp add --scope user UnityMCP -- "C:/Users/USERNAME/AppData/Local/Microsoft/WinGet/Links/uv.exe" --directory "C:/Users/USERNAME/AppData/Local/UnityMCP/UnityMcpServer/src" run server.py
```
**VSCode (all OS – HTTP default)**

```json
{
  "servers": {
    "unityMCP": {
      "type": "http",
      "url": "http://localhost:8080/mcp"
    }
  }
}
```

**macOS / Windows / Linux (Claude Desktop, Cursor, Claude Code, Windsurf, etc. – HTTP default)**

```json
{
  "mcpServers": {
    "unityMCP": {
      "url": "http://localhost:8080/mcp"
    }
  }
}
```

Set the URL to match whatever you entered in the Unity window (include `/mcp`).

#### Stdio configuration examples (legacy / optional)

Switch the Unity transport dropdown to `Stdio`, then use one of the following `command`/`args` blocks.

**VSCode (stdio)**

```json
{
  "servers": {
    "unityMCP": {
      "type": "stdio",
      "command": "uv",
      "args": [
        "--directory",
        "<ABSOLUTE_PATH_TO>/UnityMcpServer/src",
        "run",
        "server.py",
        "--transport",
        "stdio"
      ]
    }
  }
}
```

**macOS / Linux (stdio)**

```json
{
  "mcpServers": {
    "unityMCP": {
      "command": "uv",
      "args": [
        "run",
        "--directory",
        "/Users/YOUR_USERNAME/Library/AppSupport/UnityMCP/UnityMcpServer/src",
        "server.py",
        "--transport",
        "stdio"
      ]
    }
  }
}
```

**Windows (stdio)**

```json
{
  "mcpServers": {
    "unityMCP": {
      "command": "C:/Users/YOUR_USERNAME/AppData/Local/Microsoft/WinGet/Links/uv.exe",
      "args": [
        "run",
        "--directory",
        "C:/Users/YOUR_USERNAME/AppData/Local/UnityMCP/UnityMcpServer/src",
        "server.py",
        "--transport",
        "stdio"
      ]
    }
  }
}
```

Replace `YOUR_USERNAME` and `AppSupport` path segments as needed for your platform.

</details>

---

## Usage ▶️

1. **Open your Unity Project** and verify the HTTP server is running (Window > MCP for Unity > Start Local HTTP Server). The indicator should show "Session Active" once the server is up.
    
2. **Start your MCP Client** (Claude, Cursor, etc.). It connects to the HTTP endpoint configured in Step 3—no extra terminals will be spawned by the client.
    
3. **Interact!** Unity tools should now be available in your MCP Client.

    Example Prompt: `Create a 3D player controller`, `Create a tic-tac-toe game in 3D`, `Create a cool shader and apply to a cube`.

### 💡 Performance Tip: Use `batch_execute`

When performing multiple operations, use the `batch_execute` tool instead of calling tools one-by-one. This dramatically reduces latency and token costs (supports up to 25 commands per batch):

```text
❌ Slow: Create 5 cubes → 5 separate manage_gameobject calls
✅ Fast: Create 5 cubes → 1 batch_execute call with 5 commands

❌ Slow: Find objects, then add components to each → N+M separate calls  
✅ Fast: Find objects, then add components → 1 find + 1 batch with M component adds
```

**Example prompt:** "Create 10 colored cubes in a grid using batch_execute"

### Working with Multiple Unity Instances

MCP for Unity supports multiple Unity Editor instances simultaneously. Each instance is isolated per MCP client session.

**To direct tool calls to a specific instance:**

1. List available instances: Ask your LLM to check the `unity_instances` resource
2. Set the active instance: Use `set_active_instance` with the exact `Name@hash` shown (e.g., `MyProject@abc123`)
3. All subsequent tools route to that instance until changed. If multiple instances are running and no active instance is set, the server will error and instruct you to select one.

**Example:**
```
User: "List all Unity instances"
LLM: [Shows ProjectA@abc123 and ProjectB@def456]

User: "Set active instance to ProjectA@abc123"
LLM: [Calls set_active_instance("ProjectA@abc123")]

User: "Create a red cube"
LLM: [Creates cube in ProjectA]
```

---

## Development & Contributing 🛠️

### Development Setup and Guidelines

See [README-DEV.md](docs/README-DEV.md) for complete development setup and workflow documentation.

### Adding Custom Tools

MCP for Unity uses a Python MCP Server tied with Unity's C# scripts for tools. If you'd like to extend the functionality with your own tools, learn how to do so in **[CUSTOM_TOOLS.md](docs/CUSTOM_TOOLS.md)**.

### How to Contribute

1. **Fork** the main repository.
2. **Create an issue** to discuss your idea or bug.
3. **Create a branch** (`feature/your-idea` or `bugfix/your-fix`).
4. **Make changes.**
5. **Commit** (feat: Add cool new feature).
6. **Push** your branch.
7. **Open a Pull Request** against the main branch, referencing the issue you created earlier.

---

## 📊 Telemetry & Privacy

MCP for Unity includes **privacy-focused, anonymous telemetry** to help us improve the product. We collect usage analytics and performance data, but **never** your code, project names, or personal information.

- **🔒 Anonymous**: Random UUIDs only, no personal data
- **🚫 Easy opt-out**: Set `DISABLE_TELEMETRY=true` environment variable
- **📖 Transparent**: See [TELEMETRY.md](docs/TELEMETRY.md) for full details

Your privacy matters to us. All telemetry is optional and designed to respect your workflow.

---

## Troubleshooting ❓

<details>  
<summary><strong>Click to view common issues and fixes...</strong></summary>  

- **Unity Bridge Not Running/Connecting:**
    - Ensure Unity Editor is open.
    - Check the status window: Window > MCP for Unity.
    - Restart Unity.
- **MCP Client Not Connecting / Server Not Starting:**
    - Make sure the local HTTP server is running (Window > MCP for Unity > Start Server). Keep the spawned terminal window open.
    - **Verify Server Path:** Double-check the --directory path in your MCP Client's JSON config. It must exactly match the installation location:
      - **Windows:** `%USERPROFILE%\AppData\Local\UnityMCP\UnityMcpServer\src`
      - **macOS:** `~/Library/AppSupport/UnityMCP/UnityMcpServer\src` 
      - **Linux:** `~/.local/share/UnityMCP/UnityMcpServer\src`
    - **Verify uv:** Make sure `uv` is installed and working (`uv --version`).
    - **Run Manually:** Try running the server directly from the terminal to see errors: 
      ```bash
      cd /path/to/your/UnityMCP/UnityMcpServer/src
      uv run server.py
      ```
- **Configuration Failed:**
    - Use the Manual Configuration steps. The plugin may lack permissions to write to the MCP client's config file.

</details>  

Still stuck? [Open an Issue](https://github.com/CoplayDev/unity-mcp/issues) or [Join the Discord](https://discord.gg/y4p8KfzrN4)!

---

## License 📜

MIT License. See [LICENSE](LICENSE) file.

---

## Star History

[![Star History Chart](https://api.star-history.com/svg?repos=CoplayDev/unity-mcp&type=Date)](https://www.star-history.com/#CoplayDev/unity-mcp&Date)

## Unity AI Tools by Coplay

Coplay offers 2 AI tools for Unity
- **MCP for Unity** is available freely under the MIT license.
- **Coplay** is a premium Unity AI assistant that sits within Unity and is more than the MCP for Unity.

(These tools have different tech stacks. See this blog post [comparing Coplay to MCP for Unity](https://www.coplay.dev/blog/comparing-coplay-and-unity-mcp).)

<img alt="Coplay" src="docs/images/coplay-logo.png" />

## Disclaimer

This project is a free and open-source tool for the Unity Editor, and is not affiliated with Unity Technologies.
