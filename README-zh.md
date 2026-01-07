<img width="676" height="380" alt="MCP for Unity" src="docs/images/logo.png" />

| [English](README.md) | [简体中文](README-zh.md) |
|----------------------|---------------------------------|

#### 由 [Coplay](https://www.coplay.dev/?ref=unity-mcp) 荣誉赞助和维护 -- Unity 最好的 AI 助手。

[![Discord](https://img.shields.io/badge/discord-join-red.svg?logo=discord&logoColor=white)](https://discord.gg/y4p8KfzrN4)
[![](https://img.shields.io/badge/Website-Visit-purple)](https://www.coplay.dev/?ref=unity-mcp)
[![](https://img.shields.io/badge/Unity-000000?style=flat&logo=unity&logoColor=blue 'Unity')](https://unity.com/releases/editor/archive)
[![Unity Asset Store](https://img.shields.io/badge/Unity%20Asset%20Store-Get%20Package-FF6A00?style=flat&logo=unity&logoColor=white)](https://assetstore.unity.com/packages/tools/generative-ai/mcp-for-unity-ai-driven-development-329908)
[![python](https://img.shields.io/badge/Python-3.10+-3776AB.svg?style=flat&logo=python&logoColor=white)](https://www.python.org)
[![](https://badge.mcpx.dev?status=on 'MCP Enabled')](https://modelcontextprotocol.io/introduction)
![GitHub commit activity](https://img.shields.io/github/commit-activity/w/CoplayDev/unity-mcp)
![GitHub Issues or Pull Requests](https://img.shields.io/github/issues/CoplayDev/unity-mcp)
[![](https://img.shields.io/badge/License-MIT-red.svg 'MIT License')](https://opensource.org/licenses/MIT)

**使用大语言模型创建您的 Unity 应用！**

MCP for Unity 作为桥梁，允许 AI 助手（如 Claude、Cursor）通过本地 **MCP（模型上下文协议）客户端** 直接与您的 Unity 编辑器交互。为您的大语言模型提供管理资源、控制场景、编辑脚本和自动化 Unity 任务的工具。

<img alt="MCP for Unity building a scene" src="docs/images/building_scene.gif">

### 💬 加入我们的 [Discord](https://discord.gg/y4p8KfzrN4)

**获得帮助、分享想法，与其他 MCP for Unity 开发者协作！**

---

## 主要功能 🚀

* **🗣️ 自然语言操控：** 指示您的大语言模型执行 Unity 任务。
* **🛠️ 强大工具：** 管理资源、场景、材质、脚本和编辑器功能。
* **🤖 自动化：** 自动化重复的 Unity 工作流程。
* **🧩 可扩展：** 设计为与各种 MCP 客户端协作。
* **🌐 HTTP 优先传输：** 默认启用 HTTP 连接（stdio 仍可作为备选方案）。

<details open>
  <summary><strong>工具</strong></summary>

  您的大语言模型可以使用以下功能：

* `manage_asset`: 执行资源操作（导入、创建、修改、删除、搜索等）。
* `manage_editor`: 控制编辑器状态（播放模式、活动工具、标签、层）。
* `manage_gameobject`: 管理 GameObject（创建、修改、删除、查找、复制、移动）。
* `manage_components`: 管理 GameObject 上的组件（添加、移除、设置属性）。
* `manage_material`: 管理材质（创建、设置属性/颜色、分配给渲染器）。
* `manage_prefabs`: 预制体操作（打开/关闭 Stage、保存、从 GameObject 创建）。
* `manage_scene`: 场景管理（加载、保存、创建、获取层级、截图）。
* `manage_script`: 传统脚本操作（创建、读取、删除）。编辑建议使用 `apply_text_edits` 或 `script_apply_edits`。
* `manage_scriptable_object`: 创建并修改 ScriptableObject 资产。
* `manage_shader`: Shader CRUD（创建、读取、更新、删除）。
* `manage_vfx`: VFX 操作（ParticleSystem / LineRenderer / TrailRenderer / VisualEffectGraph 等）。
* `batch_execute`: ⚡ **推荐** - 批量执行多条命令（10-100x 性能提升）。
* `find_gameobjects`: 按 name/tag/layer/component/path/id 搜索 GameObject（分页）。
* `find_in_file`: 使用正则搜索 C# 脚本并返回匹配的行号与片段。
* `read_console`: 获取或清除 Unity Console 日志。
* `refresh_unity`: 请求刷新资产数据库，并可选触发编译。
* `run_tests`: 异步启动测试，返回 job_id。
* `get_test_job`: 轮询异步测试任务的进度和结果。
* `debug_request_context`: 返回当前请求上下文（client_id、session_id、meta）。
* `execute_custom_tool`: 执行由 Unity 注册的项目级自定义工具。
* `execute_menu_item`: 执行 Unity 编辑器菜单项（例如 "File/Save Project"）。
* `set_active_instance`: 将后续工具调用路由到特定 Unity 实例（从 `unity_instances` 获取 `Name@hash`）。
* `apply_text_edits`: 使用行/列范围进行精确文本编辑（支持前置条件哈希）。
* `script_apply_edits`: 结构化 C# 方法/类编辑（insert/replace/delete），边界更安全。
* `validate_script`: 快速验证（basic/standard），用于捕获语法/结构问题。
* `create_script`: 在指定项目路径创建新的 C# 脚本。
* `delete_script`: 通过 URI 或 Assets 相对路径删除 C# 脚本。
* `get_sha`: 获取 Unity C# 脚本的 SHA256 与元数据（不返回内容）。
</details>


<details open>
  <summary><strong>资源</strong></summary>

  您的大语言模型可以检索以下资源：

* `custom_tools` [`mcpforunity://custom-tools`]: 列出活动 Unity 项目可用的自定义工具。
* `unity_instances` [`mcpforunity://instances`]: 列出所有正在运行的 Unity 编辑器实例及其详细信息。
* `menu_items` [`mcpforunity://menu-items`]: Unity 编辑器中所有可用菜单项。
* `get_tests` [`mcpforunity://tests`]: Unity 编辑器中所有可用测试（EditMode + PlayMode）。
* `get_tests_for_mode` [`mcpforunity://tests/{mode}`]: 指定模式（EditMode 或 PlayMode）的测试列表。
* `gameobject_api` [`mcpforunity://scene/gameobject-api`]: GameObject 资源用法说明（先用 `find_gameobjects` 获取 instance ID）。
* `gameobject` [`mcpforunity://scene/gameobject/{instance_id}`]: 读取单个 GameObject 信息（不含完整组件序列化）。
* `gameobject_components` [`mcpforunity://scene/gameobject/{instance_id}/components`]: 读取某 GameObject 的全部组件（支持分页，可选包含属性）。
* `gameobject_component` [`mcpforunity://scene/gameobject/{instance_id}/component/{component_name}`]: 读取某 GameObject 上指定组件的完整属性。
* `editor_active_tool` [`mcpforunity://editor/active-tool`]: 当前活动工具（Move/Rotate/Scale 等）与变换手柄设置。
* `editor_prefab_stage` [`mcpforunity://editor/prefab-stage`]: 当前 Prefab Stage 上下文（若未打开则 isOpen=false）。
* `editor_selection` [`mcpforunity://editor/selection`]: 编辑器当前选中对象的详细信息。
* `editor_state` [`mcpforunity://editor/state`]: 编辑器就绪状态快照（包含建议与 staleness）。
* `editor_windows` [`mcpforunity://editor/windows`]: 当前打开的编辑器窗口列表（标题、类型、位置、焦点）。
* `project_info` [`mcpforunity://project/info`]: 静态项目信息（根路径、Unity 版本、平台）。
* `project_layers` [`mcpforunity://project/layers`]: 项目层（0-31）及名称。
* `project_tags` [`mcpforunity://project/tags`]: 项目 Tag 列表。
</details>

---

## 工作原理

MCP for Unity 使用两个组件连接您的工具：

1. **MCP for Unity Bridge：** 在编辑器内运行的 Unity 包。（通过包管理器安装）。
2. **MCP for Unity Server：** 本地运行的 Python 服务器（从终端窗口运行），通过 HTTP/JSON-RPC 与您的 MCP 客户端通信。Unity 窗口默认以 HTTP 模式为您启动它；如果您切换传输方式，stdio 仍然可用。

<img width="562" height="121" alt="image" src="https://github.com/user-attachments/assets/9abf9c66-70d1-4b82-9587-658e0d45dc3e" />

---

## 安装 ⚙️

### 前置要求

如果你**不是**通过 Unity Asset Store 安装，则还需要安装以下内容：

  * **Python：** 版本 3.10 或更新。[下载 Python](https://www.python.org/downloads/)
  * **uv（Python 工具链管理器）：**
      ```bash
      # macOS / Linux
      curl -LsSf https://astral.sh/uv/install.sh | sh

      # Windows (PowerShell)
      winget install --id=astral-sh.uv  -e

      # 文档: https://docs.astral.sh/uv/getting-started/installation/
      ```

所有安装方式都需要以下内容：

  * **Unity Hub 和编辑器：** 版本 2021.3 LTS 或更新。[下载 Unity](https://unity.com/download)
  * **MCP 客户端：** [Claude Desktop](https://claude.ai/download) | [Claude Code](https://github.com/anthropics/claude-code) | [Cursor](https://www.cursor.com/en/downloads) | [Visual Studio Code Copilot](https://code.visualstudio.com/docs/copilot/overview) | [Windsurf](https://windsurf.com) | 其他客户端可通过手动配置使用

<details> <summary><strong>[可选] Roslyn 用于高级脚本验证</strong></summary>

  对于捕获未定义命名空间、类型和方法的**严格**验证级别：

  **方法 1：Unity 的 NuGet（推荐）**
  1. 安装 [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity)
  2. 前往 `Window > NuGet Package Manager`
  3. 搜索 `Microsoft.CodeAnalysis`，选择版本 4.14.0 并安装包
  4. 同时安装包 `SQLitePCLRaw.core` 和 `SQLitePCLRaw.bundle_e_sqlite3`。
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

#### 通过 Unity Asset Store 安装

1. 在浏览器中打开：https://assetstore.unity.com/packages/tools/generative-ai/mcp-for-unity-ai-driven-development-329908
2. 点击 `Add to My Assets`。
3. 在 Unity 编辑器中，前往 `Window > Package Manager`。
4. 将该资源下载并导入到你的项目中

#### 通过 Git URL 安装

1. 打开您的 Unity 项目。
2. 前往 `Window > Package Manager`。
3. 点击 `+` -> `Add package from git URL...`。
4. 输入：
    ```
    https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity
    ```
5. 点击 `Add`。

**需要锁定版本？** 使用带标签的 URL（更新时需卸载并重新安装）：
```
https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#v8.6.0
```

#### 通过 OpenUPM 安装

1. 安装 [OpenUPM CLI](https://openupm.com/docs/getting-started-cli.html)
2. 打开终端（PowerShell、Terminal 等）并导航到您的 Unity 项目目录
3. 运行 `openupm add com.coplaydev.unity-mcp`

**注意：** 如果您在 Coplay 维护之前安装了 MCP 服务器，您需要在重新安装新版本之前卸载旧包。

### ⚡️ 步骤 2：启动本地 HTTP 服务器（默认）

HTTP 传输默认启用。Unity 窗口可以为您启动 FastMCP 服务器：

1. 打开 `Window > MCP for Unity`。
2. 确保 **Transport** 下拉菜单设置为 `HTTP Local`（默认），并将 **HTTP URL** 设置为你想要的地址（默认为 `http://localhost:8080`）。
3. 点击 **Start Server**。Unity 会生成一个新的系统终端窗口，运行 `uv ... server.py --transport http`。
4. 在你工作时保持该终端窗口打开；关闭它会停止服务器。如果你需要干净地关闭它，请使用 Unity 窗口中的 **Stop Session** 按钮。

> 更喜欢 stdio？将传输下拉菜单更改为 `Stdio`，Unity 将回退到嵌入式 TCP 桥接器，而不是启动 HTTP 服务器。

**手动启动（可选）**

您也可以从终端自己启动服务器——对 CI 或当您想查看原始日志时很有用：

```bash
uvx --from "git+https://github.com/CoplayDev/unity-mcp@v8.6.0#subdirectory=Server" mcp-for-unity --transport http --http-url http://localhost:8080
```

在客户端连接时保持进程运行。

### 🛠️ 步骤 3：配置您的 MCP 客户端
将你的 MCP 客户端（Claude、Cursor 等）连接到步骤 2 启动的 HTTP 服务器（自动）或使用下方的手动配置。

对于 **Claude Desktop** 用户，可以尝试下载并上传 `claude_skill_unity.zip`（Unity_Skills），参见这个链接：https://www.claude.com/blog/skills

**选项 A：配置按钮（推荐用于 Claude/Cursor/VSC Copilot）**

1. 在 Unity 中，前往 `Window > MCP for Unity`。
2. 从下拉菜单选择你的 Client/IDE。
3. 点击 `Configure` 按钮。（或点击 `Configure All Detected Clients` 自动尝试配置所有检测到的客户端，但会更慢。）
4. 寻找绿色状态指示器 🟢 和 "Connected ✓"。*（这会写入指向你在步骤 2 中启动的服务器的 HTTP `url`）。*

<details><summary><strong>客户端特定故障排除</strong></summary>

  - **VSCode**：使用 `Code/User/mcp.json` 和顶级 `servers.unityMCP`、`"type": "http"` 以及步骤 2 中的 URL。在 Windows 上，当您切换回 stdio 时，MCP for Unity 仍然偏好绝对 `uv.exe` 路径。
  - **Cursor / Windsurf** [(**帮助链接**)](https://github.com/CoplayDev/unity-mcp/wiki/1.-Fix-Unity-MCP-and-Cursor,-VSCode-&-Windsurf)：如果缺少 `uv`，MCP for Unity 窗口会显示"uv Not Found"和快速 [HELP] 链接以及"Choose `uv` Install Location"按钮。
  - **Claude Code** [(**帮助链接**)](https://github.com/CoplayDev/unity-mcp/wiki/2.-Fix-Unity-MCP-and-Claude-Code)：如果找不到 `claude`，窗口会显示"Claude Not Found"和 [HELP] 以及"Choose Claude Location"按钮。注销现在会立即更新 UI。
</details>

**选项 B：手动配置**

如果自动设置失败或您使用不同的客户端：

1. **找到您的 MCP 客户端配置文件。**（查看客户端文档）。
    * *Claude 示例（macOS）：* `~/Library/Application Support/Claude/claude_desktop_config.json`
    * *Claude 示例（Windows）：* `%APPDATA%\Claude\claude_desktop_config.json`
2. **编辑文件** 以添加/更新 `mcpServers` 部分，使其指向步骤 2 中的 HTTP 端点。

<details>
<summary><strong>点击查看客户端特定的 JSON 配置片段...</strong></summary>

---
**Claude Code**

如果您正在使用 Claude Code，您可以使用以下命令注册 MCP 服务器：

**macOS：**

```bash
claude mcp add --scope user UnityMCP -- uv --directory /Users/USERNAME/Library/AppSupport/UnityMCP/UnityMcpServer/src run server.py
```

**Windows：**

```bash
claude mcp add --scope user UnityMCP -- "C:/Users/USERNAME/AppData/Local/Microsoft/WinGet/Links/uv.exe" --directory "C:/Users/USERNAME/AppData/Local/UnityMCP/UnityMcpServer/src" run server.py
```
**VSCode（所有操作系统 – HTTP 默认）**

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

**macOS / Windows / Linux（Claude Desktop、Cursor、Claude Code、Windsurf 等 – HTTP 默认）**

```json
{
  "mcpServers": {
    "unityMCP": {
      "url": "http://localhost:8080/mcp"
    }
  }
}
```

将 URL 设置为与您在 Unity 窗口中输入的内容匹配（包括 `/mcp`）。

#### Stdio 配置示例（传统 / 可选）

将 Unity 传输下拉菜单切换到 `Stdio`，然后使用以下 `command`/`args` 块之一。

**VSCode（stdio）**

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

**macOS / Linux（stdio）**

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

**Windows（stdio）**

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

根据您的平台需要替换 `YOUR_USERNAME` 和 `AppSupport` 路径段。

</details>

---

## 使用方法 ▶️

1. **打开你的 Unity 项目** 并确认 HTTP 服务器正在运行（Window > MCP for Unity > Start Server）。服务器启动后，指示器应显示 "Session Active"。
    
2. **启动您的 MCP 客户端**（Claude、Cursor 等）。它连接到步骤 3 中配置的 HTTP 端点——客户端不会生成额外的终端。
    
3. **交互！** Unity 工具现在应该在您的 MCP 客户端中可用。

    示例提示：`创建一个 3D 玩家控制器`，`创建一个 3D 井字游戏`，`创建一个酷炫的着色器并应用到立方体上`。

### 💡 性能提示：使用 `batch_execute`

当你需要执行多个操作时，请使用 `batch_execute` 而不是逐个调用工具。这可以显著降低延迟和 token 成本（单次最多 25 条命令）：

```text
❌ 慢：创建 5 个立方体 → 5 次 manage_gameobject 调用
✅ 快：创建 5 个立方体 → 1 次 batch_execute（包含 5 条 manage_gameobject 命令）

❌ 慢：先查找对象，再逐个加组件 → N+M 次调用
✅ 快：查找 + 批量加组件 → 1 次 find + 1 次 batch_execute（包含 M 条 manage_components 命令）
```

### 使用多个 Unity 实例

MCP for Unity 同时支持多个 Unity 编辑器实例。每个实例在每个 MCP 客户端会话中是隔离的。

**要将工具调用定向到特定实例：**

1. 列出可用实例：要求你的大语言模型检查 `unity_instances` 资源
2. 设置活动实例：使用 `set_active_instance`，并传入 `unity_instances` 返回的精确 `Name@hash`（例如 `MyProject@abc123`）
3. 后续所有工具都会路由到该实例，直到你再次更改。如果存在多个实例且未设置活动实例，服务器会报错并提示选择实例。

**示例：**
```
用户: "列出所有 Unity 实例"
大语言模型: [显示 ProjectA@abc123 和 ProjectB@def456]

用户: "将活动实例设置为 ProjectA@abc123"
大语言模型: [调用 set_active_instance("ProjectA@abc123")]

用户: "创建一个红色立方体"
大语言模型: [在 ProjectA 中创建立方体]
```

---

## 开发和贡献 🛠️

### 开发设置和指南

查看 [README-DEV.md](docs/README-DEV.md) 获取完整的开发设置和工作流程文档。

### 添加自定义工具

MCP for Unity 使用与 Unity 的 C# 脚本绑定的 Python MCP 服务器来实现工具功能。如果您想使用自己的工具扩展功能，请参阅 **[CUSTOM_TOOLS.md](docs/CUSTOM_TOOLS.md)** 了解如何操作。

### 如何贡献

1. **Fork** 主仓库。
2. **创建问题** 讨论您的想法或错误。
3. **创建分支**（`feature/your-idea` 或 `bugfix/your-fix`）。
4. **进行更改。**
5. **提交**（feat: Add cool new feature）。
6. **推送** 您的分支。
7. **对主分支开启拉取请求**，引用您之前创建的问题。

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
    - 确保本地 HTTP 服务器正在运行（Window > MCP for Unity > Start Server）。保持生成的终端窗口打开。
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
- **配置失败：**
    - 使用手动配置步骤。插件可能缺乏写入 MCP 客户端配置文件的权限。

</details>  

仍然卡住？[开启问题](https://github.com/CoplayDev/unity-mcp/issues) 或 [加入 Discord](https://discord.gg/y4p8KfzrN4)！

---

## 许可证 📜

MIT 许可证。查看 [LICENSE](LICENSE) 文件。

---

## Star 历史

[![Star History Chart](https://api.star-history.com/svg?repos=CoplayDev/unity-mcp&type=Date)](https://www.star-history.com/#CoplayDev/unity-mcp&Date)

## Unity AI 工具由 Coplay 提供

Coplay 提供 2 个 Unity AI 工具
- **MCP for Unity** 在 MIT 许可证下免费提供。
- **Coplay** 是一个高级 Unity AI 助手，位于 Unity 内部，功能比 MCP for Unity 更多。

（这些工具有不同的技术栈。查看这篇博客文章[比较 Coplay 和 MCP for Unity](https://www.coplay.dev/blog/comparing-coplay-and-unity-mcp)。）

<img alt="Coplay" src="docs/images/coplay-logo.png" />

## 免责声明

本项目是一个免费开源的 Unity 编辑器工具，与 Unity Technologies 无关。
