<img width="676" height="380" alt="MCP for Unity" src="https://github.com/user-attachments/assets/b712e41d-273c-48b2-9041-82bd17ace267" />

#### Proudly sponsored and maintained by [Coplay](https://www.coplay.dev/?ref=unity-mcp) -- the best AI assistant for Unity. [Read the backstory here.](https://www.coplay.dev/blog/coplay-and-open-source-unity-mcp-join-forces)

[![Discord](https://img.shields.io/badge/discord-join-red.svg?logo=discord&logoColor=white)](https://discord.gg/y4p8KfzrN4)
[![](https://img.shields.io/badge/Unity-000000?style=flat&logo=unity&logoColor=blue 'Unity')](https://unity.com/releases/editor/archive)
[![python](https://img.shields.io/badge/Python-3.12-3776AB.svg?style=flat&logo=python&logoColor=white)](https://www.python.org)
[![](https://badge.mcpx.dev?status=on 'MCP Enabled')](https://modelcontextprotocol.io/introduction)
![GitHub commit activity](https://img.shields.io/github/commit-activity/w/CoplayDev/unity-mcp)
![GitHub Issues or Pull Requests](https://img.shields.io/github/issues/CoplayDev/unity-mcp)
[![](https://img.shields.io/badge/License-MIT-red.svg 'MIT License')](https://opensource.org/licenses/MIT)
[![](https://img.shields.io/badge/Sponsor-Coplay-red.svg 'Coplay')](https://www.coplay.dev/?ref=unity-mcp)

**Create your Unity apps with LLMs!**

MCP for Unity acts as a bridge, allowing AI assistants (like Claude, Cursor) to interact directly with your Unity Editor via a local **MCP (Model Context Protocol) Client**. Give your LLM tools to manage assets, control scenes, edit scripts, and automate tasks within Unity.

---

### 💬 Join Our [Discord](https://discord.gg/y4p8KfzrN4)

**Get help, share ideas, and collaborate with other MCP for Unity developers!**  

---

## Key Features 🚀

*   **🗣️ Natural Language Control:** Instruct your LLM to perform Unity tasks.
*   **🛠️ Powerful Tools:** Manage assets, scenes, materials, scripts, and editor functions.
*   **🤖 Automation:** Automate repetitive Unity workflows.
*   **🧩 Extensible:** Designed to work with various MCP Clients.

<details open>
  <summary><strong> Available Tools </strong></summary>

  Your LLM can use functions like:

  *   `read_console`: Gets messages from or clears the console.
  *   `manage_script`: Manages C# scripts (create, read, update, delete).
  *   `manage_editor`: Controls and queries the editor's state and settings.
  *   `manage_scene`: Manages scenes (load, save, create, get hierarchy, etc.).
  *   `manage_asset`: Performs asset operations (import, create, modify, delete, etc.).
  *   `manage_shader`: Performs shader CRUD operations (create, read, modify, delete).
  *   `manage_gameobject`: Manages GameObjects: create, modify, delete, find, and component operations.
  *   `manage_menu_item`: List Unity Editor menu items; and check for their existence or execute them (e.g., execute "File/Save Project").
  *   `apply_text_edits`: Precise text edits with precondition hashes and atomic multi-edit batches.
  *   `script_apply_edits`: Structured C# method/class edits (insert/replace/delete) with safer boundaries.
  *   `validate_script`: Fast validation (basic/standard) to catch syntax/structure issues before/after writes.
</details>

---

## How It Works 

MCP for Unity connects your tools using two components:

1.  **MCP for Unity Bridge:** A Unity package running inside the Editor. (Installed via Package Manager).
2.  **MCP for Unity Server:** A Python server that runs locally, communicating between the Unity Bridge and your MCP Client. (Installed automatically by the package on first run or via Auto-Setup; manual setup is available as a fallback).

<img width="562" height="121" alt="image" src="https://github.com/user-attachments/assets/9abf9c66-70d1-4b82-9587-658e0d45dc3e" />

---

## Installation ⚙️

### Prerequisites

  *   **Python:** Version 3.12 or newer. [Download Python](https://www.python.org/downloads/)
  *   **Unity Hub & Editor:** Version 2021.3 LTS or newer. [Download Unity](https://unity.com/download)
  *   **uv (Python toolchain manager):**
      ```bash
      # macOS / Linux
      curl -LsSf https://astral.sh/uv/install.sh | sh

      # Windows (PowerShell)
      winget install --id=astral-sh.uv  -e

      # Docs: https://docs.astral.sh/uv/getting-started/installation/
      ```
      
  *   **An MCP Client:** : [Claude Desktop](https://claude.ai/download) | [Claude Code](https://github.com/anthropics/claude-code) | [Cursor](https://www.cursor.com/en/downloads) | [Visual Studio Code Copilot](https://code.visualstudio.com/docs/copilot/overview) | [Windsurf](https://windsurf.com) | Others work with manual config

 *    <details> <summary><strong>[Optional] Roslyn for Advanced Script Validation</strong></summary>

        For **Strict** validation level that catches undefined namespaces, types, and methods: 

        **Method 1: NuGet for Unity (Recommended)**
        1. Install [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity)
        2. Go to `Window > NuGet Package Manager`
        3. Search for `Microsoft.CodeAnalysis.CSharp`, select version 3.11.0 and install the package
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

#### To install via Git URL

1.  Open your Unity project.
2.  Go to `Window > Package Manager`.
3.  Click `+` -> `Add package from git URL...`.
4.  Enter:
    ```
    https://github.com/CoplayDev/unity-mcp.git?path=/UnityMcpBridge
    ```
5.  Click `Add`.
6. The MCP server is installed automatically by the package on first run or via Auto-Setup. If that fails, use Manual Configuration (below).

#### To install via OpenUPM

1.  Install the [OpenUPM CLI](https://openupm.com/docs/getting-started-cli.html)
2.  Open a terminal (PowerShell, Terminal, etc.) and navigate to your Unity project directory
3.  Run `openupm add com.coplaydev.unity-mcp`

**Note:** If you installed the MCP Server before Coplay's maintenance, you will need to uninstall the old package before re-installing the new one.

### 🛠️ Step 2: Configure Your MCP Client
Connect your MCP Client (Claude, Cursor, etc.) to the Python server set up in Step 1 (auto) or via Manual Configuration (below).

<img width="648" height="599" alt="MCPForUnity-Readme-Image" src="https://github.com/user-attachments/assets/b4a725da-5c43-4bd6-80d6-ee2e3cca9596" />

**Option A: Auto-Setup (Recommended for Claude/Cursor/VSC Copilot)**

1.  In Unity, go to `Window > MCP for Unity`.
2.  Click `Auto-Setup`.
3.  Look for a green status indicator 🟢 and "Connected ✓". *(This attempts to modify the MCP Client's config file automatically).* 

<details><summary><strong>Client-specific troubleshooting</strong></summary>

  - **VSCode**: uses `Code/User/mcp.json` with top-level `servers.unityMCP` and `"type": "stdio"`. On Windows, MCP for Unity writes an absolute `uv.exe` (prefers WinGet Links shim) to avoid PATH issues.
  - **Cursor / Windsurf** [(**help link**)](https://github.com/CoplayDev/unity-mcp/wiki/1.-Fix-Unity-MCP-and-Cursor,-VSCode-&-Windsurf): if `uv` is missing, the MCP for Unity window shows "uv Not Found" with a quick [HELP] link and a "Choose `uv` Install Location" button.
  - **Claude Code** [(**help link**)](https://github.com/CoplayDev/unity-mcp/wiki/2.-Fix-Unity-MCP-and-Claude-Code): if `claude` isn't found, the window shows "Claude Not Found" with [HELP] and a "Choose Claude Location" button. Unregister now updates the UI immediately.</details>


**Option B: Manual Configuration**

If Auto-Setup fails or you use a different client:

1.  **Find your MCP Client's configuration file.** (Check client documentation).
    *   *Claude Example (macOS):* `~/Library/Application Support/Claude/claude_desktop_config.json`
    *   *Claude Example (Windows):* `%APPDATA%\Claude\claude_desktop_config.json`
2.  **Edit the file** to add/update the `mcpServers` section, using the *exact* paths from Step 1.

<details>
<summary><strong>Click for Client-Specific JSON Configuration Snippets...</strong></summary>

  ---
**Claude Code**

If you're using Claude Code, you can register the MCP server using the below commands:
🚨**make sure to run these from your Unity project's home directory**🚨

**macOS:**

```bash
claude mcp add UnityMCP -- uv --directory /Users/USERNAME/Library/AppSupport/UnityMCP/UnityMcpServer/src run server.py
```

**Windows:**

```bash
claude mcp add UnityMCP -- "C:/Users/USERNAME/AppData/Local/Microsoft/WinGet/Links/uv.exe" --directory "C:/Users/USERNAME/AppData/Local/UnityMCP/UnityMcpServer/src" run server.py
```
**VSCode (all OS)**

```json
{
  "servers": {
    "unityMCP": {
      "command": "uv",
      "args": ["--directory","<ABSOLUTE_PATH_TO>/UnityMcpServer/src","run","server.py"],
      "type": "stdio"
    }
  }
}
```

On Windows, set `command` to the absolute shim, e.g. `C:\\Users\\YOU\\AppData\\Local\\Microsoft\\WinGet\\Links\\uv.exe`.

**Windows:**

  ```json
  {
    "mcpServers": {
      "UnityMCP": {
        "command": "uv",
        "args": [
          "run",
          "--directory",
          "C:\\Users\\YOUR_USERNAME\\AppData\\Local\\UnityMCP\\UnityMcpServer\\src",
          "server.py"
        ]
      }
      // ... other servers might be here ...
    }
  }
``` 

(Remember to replace YOUR_USERNAME and use double backslashes \\)

**macOS:**

```json
{
  "mcpServers": {
    "UnityMCP": {
      "command": "uv",
      "args": [
        "run",
        "--directory",
        "/Users/YOUR_USERNAME/Library/AppSupport/UnityMCP/UnityMcpServer/src",
        "server.py"
      ]
    }
    // ... other servers might be here ...
  }
}
```

(Replace YOUR_USERNAME. Note: AppSupport is a symlink to "Application Support" to avoid quoting issues)

**Linux:**

```json
{
  "mcpServers": {
    "UnityMCP": {
      "command": "uv",
      "args": [
        "run",
        "--directory",
        "/home/YOUR_USERNAME/.local/share/UnityMCP/UnityMcpServer/src",
        "server.py"
      ]
    }
    // ... other servers might be here ...
  }
}
```

(Replace YOUR_USERNAME)


</details>

---

## Usage ▶️

1. **Open your Unity Project.** The MCP for Unity package should connect automatically. Check status via Window > MCP for Unity.
    
2. **Start your MCP Client** (Claude, Cursor, etc.). It should automatically launch the MCP for Unity Server (Python) using the configuration from Installation Step 2.
    
3. **Interact!** Unity tools should now be available in your MCP Client.
    
    Example Prompt: `Create a 3D player controller`, `Create a tic-tac-toe game in 3D`, `Create a cool shader and apply to a cube`.

---

## Development & Contributing 🛠️

### For Developers

If you're contributing to MCP for Unity or want to test core changes, we have development tools to streamline your workflow:

- **Development Deployment Scripts**: Quickly deploy and test your changes to MCP for Unity Bridge and Python Server
- **Automatic Backup System**: Safe testing with easy rollback capabilities  
- **Hot Reload Workflow**: Fast iteration cycle for core development

📖 **See [README-DEV.md](README-DEV.md)** for complete development setup and workflow documentation.

### Contributing 🤝

Help make MCP for Unity better!

1. **Fork** the main repository.
2. **Create a branch** (`feature/your-idea` or `bugfix/your-fix`).
3. **Make changes.**
4. **Commit** (feat: Add cool new feature).
5. **Push** your branch.
6. **Open a Pull Request** against the main branch.

---

## 📊 Telemetry & Privacy

Unity MCP includes **privacy-focused, anonymous telemetry** to help us improve the product. We collect usage analytics and performance data, but **never** your code, project names, or personal information.

- **🔒 Anonymous**: Random UUIDs only, no personal data
- **🚫 Easy opt-out**: Set `DISABLE_TELEMETRY=true` environment variable
- **📖 Transparent**: See [TELEMETRY.md](TELEMETRY.md) for full details

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
- **Auto-Configure Failed:**
    - Use the Manual Configuration steps. Auto-configure might lack permissions to write to the MCP client's config file.

</details>  

Still stuck? [Open an Issue](https://github.com/CoplayDev/unity-mcp/issues) or [Join the Discord](https://discord.gg/y4p8KfzrN4)!

---

## License 📜

MIT License. See [LICENSE](LICENSE) file.

---

## Star History

[![Star History Chart](https://api.star-history.com/svg?repos=CoplayDev/unity-mcp&type=Date)](https://www.star-history.com/#CoplayDev/unity-mcp&Date)

## Sponsor

<p align="center">
  <a href="https://www.coplay.dev/?ref=unity-mcp" target="_blank" rel="noopener noreferrer">
    <img src="logo.png" alt="Coplay Logo" width="100%">
  </a>
</p>
