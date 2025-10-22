<img width="676" height="380" alt="MCP for Unity" src="logo.png" />

| [English](README.md) | [简体中文](README-zh.md) |
|----------------------|---------------------------------|

#### 由 [Coplay](https://www.coplay.dev/?ref=unity-mcp) 荣誉赞助和维护 -- Unity 最好的 AI 助手。[在此阅读背景故事。](https://www.coplay.dev/blog/coplay-and-open-source-unity-mcp-join-forces)

[![Discord](https://img.shields.io/badge/discord-join-red.svg?logo=discord&logoColor=white)](https://discord.gg/y4p8KfzrN4)
[![](https://img.shields.io/badge/Unity-000000?style=flat&logo=unity&logoColor=blue 'Unity')](https://unity.com/releases/editor/archive)
[![python](https://img.shields.io/badge/Python-3.12-3776AB.svg?style=flat&logo=python&logoColor=white)](https://www.python.org)
[![](https://badge.mcpx.dev?status=on 'MCP Enabled')](https://modelcontextprotocol.io/introduction)
![GitHub commit activity](https://img.shields.io/github/commit-activity/w/CoplayDev/unity-mcp)
![GitHub Issues or Pull Requests](https://img.shields.io/github/issues/CoplayDev/unity-mcp)
[![](https://img.shields.io/badge/License-MIT-red.svg 'MIT License')](https://opensource.org/licenses/MIT)
[![](https://img.shields.io/badge/Sponsor-Coplay-red.svg 'Coplay')](https://www.coplay.dev/?ref=unity-mcp)

**使用大语言模型创建您的 Unity 应用！**

MCP for Unity 作为桥梁，允许 AI 助手（如 Claude、Cursor）通过本地 **MCP（模型上下文协议）客户端** 直接与您的 Unity 编辑器交互。为您的大语言模型提供管理资源、控制场景、编辑脚本和自动化 Unity 任务的工具。

---

### 💬 加入我们的 [Discord](https://discord.gg/y4p8KfzrN4)

**获得帮助、分享想法，与其他 MCP for Unity 开发者协作！**

---

## 主要功能 🚀

* **🗣️ 自然语言操控：** 指示您的大语言模型执行 Unity 任务。
* **🛠️ 强大工具：** 管理资源、场景、材质、脚本和编辑器功能。
* **🤖 自动化：** 自动化重复的 Unity 工作流程。
* **🧩 可扩展：** 设计为与各种 MCP 客户端协作。

<details open>
  <summary><strong> 可用工具 </strong></summary>

  您的大语言模型可以使用以下功能：

  * `read_console`: 获取控制台消息或清除控制台。
  * `manage_script`: 管理 C# 脚本（创建、读取、更新、删除）。
  * `manage_editor`: 控制和查询编辑器的状态和设置。
  * `manage_scene`: 管理场景（加载、保存、创建、获取层次结构等）。
  * `manage_asset`: 执行资源操作（导入、创建、修改、删除等）。
  * `manage_shader`: 执行着色器 CRUD 操作（创建、读取、修改、删除）。
  * `manage_gameobject`: 管理游戏对象：创建、修改、删除、查找和组件操作。
  * `execute_menu_item`: 执行 Unity 编辑器菜单项（例如，执行"File/Save Project"）。
  * `apply_text_edits`: 具有前置条件哈希和原子多编辑批次的精确文本编辑。
  * `script_apply_edits`: 结构化 C# 方法/类编辑（插入/替换/删除），具有更安全的边界。
  * `validate_script`: 快速验证（基本/标准）以在写入前后捕获语法/结构问题。
</details>

---

## 工作原理

MCP for Unity 使用两个组件连接您的工具：

1. **MCP for Unity Bridge：** 在编辑器内运行的 Unity 包。（通过包管理器安装）。
2. **MCP for Unity Server：** 本地运行的 Python 服务器，在 Unity Bridge 和您的 MCP 客户端之间进行通信。（首次运行时由包自动安装或通过自动设置；手动设置作为备选方案）。

<img width="562" height="121" alt="image" src="https://github.com/user-attachments/assets/9abf9c66-70d1-4b82-9587-658e0d45dc3e" />

---

## 安装 ⚙️

### 前置要求

* **Python：** 版本 3.12 或更新。[下载 Python](https://www.python.org/downloads/)
* **Unity Hub 和编辑器：** 版本 2021.3 LTS 或更新。[下载 Unity](https://unity.com/download)
* **uv（Python 工具链管理器）：**
    ```bash
    # macOS / Linux
    curl -LsSf https://astral.sh/uv/install.sh | sh

    # Windows (PowerShell)
    winget install --id=astral-sh.uv  -e

    # 文档: https://docs.astral.sh/uv/getting-started/installation/
    ```

* **MCP 客户端：** [Claude Desktop](https://claude.ai/download) | [Claude Code](https://github.com/anthropics/claude-code) | [Cursor](https://www.cursor.com/en/downloads) | [Visual Studio Code Copilot](https://code.visualstudio.com/docs/copilot/overview) | [Windsurf](https://windsurf.com) | 其他客户端可通过手动配置使用

* <details> <summary><strong>[可选] Roslyn 用于高级脚本验证</strong></summary>

    对于捕获未定义命名空间、类型和方法的**严格**验证级别：

    **方法 1：Unity 的 NuGet（推荐）**
    1. 安装 [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity)
    2. 前往 `Window > NuGet Package Manager`
    3. 搜索 `Microsoft.CodeAnalysis.CSharp`，选择版本 3.11.0 并安装包
    5. 前往 `Player Settings > Scripting Define Symbols`
    6. 添加 `USE_ROSLYN`
    7. 重启 Unity

    **方法 2：手动 DLL 安装**
    1. 从 [NuGet](https://www.nuget.org/packages/Microsoft.CodeAnalysis.CSharp/) 下载 Microsoft.CodeAnalysis.CSharp.dll 和依赖项
    2. 将 DLL 放置在 `Assets/Plugins/` 文件夹中
    3. 确保 .NET 兼容性设置正确
    4. 将 `USE_ROSLYN` 添加到脚本定义符号
    5. 重启 Unity

    **注意：** 没有 Roslyn 时，脚本验证会回退到基本结构检查。Roslyn 启用完整的 C# 编译器诊断和精确错误报告。</details>

---
### 🌟 步骤 1：安装 Unity 包

#### 通过 Git URL 安装

1. 打开您的 Unity 项目。
2. 前往 `Window > Package Manager`。
3. 点击 `+` -> `Add package from git URL...`。
4. 输入：
    ```
    https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity
    ```
5. 点击 `Add`。
6. MCP 服务器在首次运行时或通过自动设置由包自动安装。如果失败，请使用手动配置（如下）。

#### 通过 OpenUPM 安装

1. 安装 [OpenUPM CLI](https://openupm.com/docs/getting-started-cli.html)
2. 打开终端（PowerShell、Terminal 等）并导航到您的 Unity 项目目录
3. 运行 `openupm add com.coplaydev.unity-mcp`

**注意：** 如果您在 Coplay 维护之前安装了 MCP 服务器，您需要在重新安装新版本之前卸载旧包。

### 🛠️ 步骤 2：配置您的 MCP 客户端
将您的 MCP 客户端（Claude、Cursor 等）连接到步骤 1（自动）设置的 Python 服务器或通过手动配置（如下）。

<img width="648" height="599" alt="MCPForUnity-Readme-Image" src="https://github.com/user-attachments/assets/b4a725da-5c43-4bd6-80d6-ee2e3cca9596" />

**选项 A：自动设置（推荐用于 Claude/Cursor/VSC Copilot）**

1. 在 Unity 中，前往 `Window > MCP for Unity`。
2. 点击 `Auto-Setup`。
3. 寻找绿色状态指示器 🟢 和"Connected ✓"。*（这会尝试自动修改 MCP 客户端的配置文件）。*

<details><summary><strong>客户端特定故障排除</strong></summary>

- **VSCode**：使用 `Code/User/mcp.json` 和顶级 `servers.unityMCP` 以及 `"type": "stdio"`。在 Windows 上，MCP for Unity 写入绝对路径 `uv.exe`（优先选择 WinGet Links shim）以避免 PATH 问题。
- **Cursor / Windsurf** [(**帮助链接**)](https://github.com/CoplayDev/unity-mcp/wiki/1.-Fix-Unity-MCP-and-Cursor,-VSCode-&-Windsurf)：如果缺少 `uv`，MCP for Unity 窗口会显示"uv Not Found"和快速 [HELP] 链接以及"Choose `uv` Install Location"按钮。
- **Claude Code** [(**帮助链接**)](https://github.com/CoplayDev/unity-mcp/wiki/2.-Fix-Unity-MCP-and-Claude-Code)：如果找不到 `claude`，窗口会显示"Claude Not Found"和 [HELP] 以及"Choose Claude Location"按钮。注销现在会立即更新 UI。</details>

**选项 B：手动配置**

如果自动设置失败或您使用不同的客户端：

1. **找到您的 MCP 客户端配置文件。**（查看客户端文档）。
    * *Claude 示例（macOS）：* `~/Library/Application Support/Claude/claude_desktop_config.json`
    * *Claude 示例（Windows）：* `%APPDATA%\Claude\claude_desktop_config.json`
2. **编辑文件** 以添加/更新 `mcpServers` 部分，使用步骤 1 中的*精确*路径。

<details>
<summary><strong>点击查看客户端特定的 JSON 配置片段...</strong></summary>

---
**Claude Code**

如果您正在使用 Claude Code，您可以使用以下命令注册 MCP 服务器：
🚨**确保从您的 Unity 项目主目录运行这些命令**🚨

**macOS：**

```bash
claude mcp add UnityMCP -- uv --directory /Users/USERNAME/Library/AppSupport/UnityMCP/UnityMcpServer/src run server.py
```

**Windows：**

```bash
claude mcp add UnityMCP -- "C:/Users/USERNAME/AppData/Local/Microsoft/WinGet/Links/uv.exe" --directory "C:/Users/USERNAME/AppData/Local/UnityMCP/UnityMcpServer/src" run server.py
```
**VSCode（所有操作系统）**

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

在 Windows 上，将 `command` 设置为绝对 shim，例如 `C:\\Users\\YOU\\AppData\\Local\\Microsoft\\WinGet\\Links\\uv.exe`。

**Windows：**

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
    // ... 其他服务器可能在这里 ...
  }
}
```

（记得替换 YOUR_USERNAME 并使用双反斜杠 \\）

**macOS：**

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
    // ... 其他服务器可能在这里 ...
  }
}
```

（替换 YOUR_USERNAME。注意：AppSupport 是"Application Support"的符号链接，以避免引号问题）

**Linux：**

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
    // ... 其他服务器可能在这里 ...
  }
}
```

（替换 YOUR_USERNAME）

</details>

---

## 使用方法 ▶️

1. **打开您的 Unity 项目。** MCP for Unity 包应该自动连接。通过 Window > MCP for Unity 检查状态。

2. **启动您的 MCP 客户端**（Claude、Cursor 等）。它应该使用安装步骤 2 中的配置自动启动 MCP for Unity 服务器（Python）。

3. **交互！** Unity 工具现在应该在您的 MCP 客户端中可用。

    示例提示：`创建一个 3D 玩家控制器`，`创建一个 3D 井字游戏`，`创建一个酷炫的着色器并应用到立方体上`。

---

## 开发和贡献 🛠️

### 添加自定义工具

MCP for Unity 使用与 Unity 的 C# 脚本绑定的 Python MCP 服务器来实现工具功能。如果您想使用自己的工具扩展功能，请参阅 **[CUSTOM_TOOLS.md](docs/CUSTOM_TOOLS.md)** 了解如何操作。

### 贡献项目

如果您正在为 MCP for Unity 做贡献或想要测试核心更改，我们有开发工具来简化您的工作流程：

- **开发部署脚本**：快速部署和测试您对 MCP for Unity Bridge 和 Python 服务器的更改
- **自动备份系统**：具有简单回滚功能的安全测试
- **热重载工作流程**：核心开发的快速迭代周期

📖 **查看 [README-DEV.md](docs/README-DEV.md)** 获取完整的开发设置和工作流程文档。

### 贡献 🤝

帮助改进 MCP for Unity！

1. **Fork** 主仓库。
2. **创建分支**（`feature/your-idea` 或 `bugfix/your-fix`）。
3. **进行更改。**
4. **提交**（feat: Add cool new feature）。
5. **推送** 您的分支。
6. **对主分支开启拉取请求**。

---

## 📊 遥测和隐私

MCP for Unity 包含**注重隐私的匿名遥测**来帮助我们改进产品。我们收集使用分析和性能数据，但**绝不**收集您的代码、项目名称或个人信息。

- **🔒 匿名**：仅随机 UUID，无个人数据
- **🚫 轻松退出**：设置 `DISABLE_TELEMETRY=true` 环境变量
- **📖 透明**：查看 [TELEMETRY.md](docs/TELEMETRY.md) 获取完整详情

您的隐私对我们很重要。所有遥测都是可选的，旨在尊重您的工作流程。

---

## 故障排除 ❓

<details>
<summary><strong>点击查看常见问题和修复方法...</strong></summary>

- **Unity Bridge 未运行/连接：**
    - 确保 Unity 编辑器已打开。
    - 检查状态窗口：Window > MCP for Unity。
    - 重启 Unity。
- **MCP 客户端未连接/服务器未启动：**
    - **验证服务器路径：** 双重检查您的 MCP 客户端 JSON 配置中的 --directory 路径。它必须完全匹配安装位置：
      - **Windows：** `%USERPROFILE%\AppData\Local\UnityMCP\UnityMcpServer\src`
      - **macOS：** `~/Library/AppSupport/UnityMCP/UnityMcpServer\src`
      - **Linux：** `~/.local/share/UnityMCP/UnityMcpServer\src`
    - **验证 uv：** 确保 `uv` 已安装并正常工作（`uv --version`）。
    - **手动运行：** 尝试直接从终端运行服务器以查看错误：
      ```bash
      cd /path/to/your/UnityMCP/UnityMcpServer/src
      uv run server.py
      ```
- **自动配置失败：**
    - 使用手动配置步骤。自动配置可能缺乏写入 MCP 客户端配置文件的权限。

</details>

仍然卡住？[开启问题](https://github.com/CoplayDev/unity-mcp/issues) 或 [加入 Discord](https://discord.gg/y4p8KfzrN4)！

---

## 许可证 📜

MIT 许可证。查看 [LICENSE](LICENSE) 文件。

---

## Star历史

[![Star History Chart](https://api.star-history.com/svg?repos=CoplayDev/unity-mcp&type=Date)](https://www.star-history.com/#CoplayDev/unity-mcp&Date)

## 赞助

<p align="center">
  <a href="https://www.coplay.dev/?ref=unity-mcp" target="_blank" rel="noopener noreferrer">
    <img src="logo.png" alt="Coplay Logo" width="100%">
  </a>
</p>