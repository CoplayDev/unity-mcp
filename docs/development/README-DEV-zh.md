# MCP for Unity 开发工具

| [English](README-DEV.md) | [简体中文](README-DEV-zh.md) |
|---------------------------|------------------------------|

欢迎来到 MCP for Unity 开发环境！此目录包含简化 MCP for Unity 核心开发的工具和实用程序。

## 🛠️ 开发环境搭建

### 安装开发依赖

如果你想贡献代码或运行测试，需要使用 `uv` 安装开发依赖：

```bash
# 进入 server 源码目录
cd Server

# 以 editable 模式安装，并包含 dev 依赖
uv pip install -e ".[dev]"
```

这会安装：

- **运行时依赖**：`httpx`, `fastmcp`, `mcp`, `pydantic`, `tomli`
- **开发依赖**：`pytest`, `pytest-asyncio`

### 运行测试

```bash
# 在 server 目录下
cd Server
uv run pytest tests/ -v
```

或者从仓库根目录执行：

```bash
# 使用 server 目录中的 uv
cd Server && uv run pytest tests/ -v
```

只运行集成测试：

```bash
uv run pytest tests/ -v -m integration
```

只运行单元测试：

```bash
uv run pytest tests/ -v -m unit
```

### 代码覆盖率

运行带覆盖率追踪的测试：

```bash
cd Server
uv run pytest tests/ --cov --cov-report=html --cov-report=term

# 查看 HTML 报告
open htmlcov/index.html  # macOS
xdg-open htmlcov/index.html  # Linux
```

覆盖率配置位于 `Server/pyproject.toml` 的 `[tool.coverage.*]` 部分。

## 🚀 可用的开发特性

### ✅ 开发部署脚本

用于快速部署与测试 MCP for Unity 核心更改的工具。

**Development Mode Toggle**：内置 Unity 编辑器开发特性（现在作为 Advanced Setting 提供）

**Hot Reload System**：无需重启 Unity 的实时更新（Roslyn Runtime_Compilation Custom Tools）

**Plugin Development Kit**：用于创建 MCP for Unity 扩展的工具（Custom Tools）

### 🔄 即将推出

- **自动化测试套件**：为贡献提供更完整的测试框架
- **调试面板**：更高级的调试与监控工具

---

## Advanced Settings（编辑器窗口）

使用 MCP for Unity 编辑器窗口（Window > MCP for Unity），在 Settings 选项卡内打开 **Advanced Settings**，可以在开发期间覆盖工具路径、切换 server 源、并将本地包部署到项目中。

![Advanced Settings](./images/advanced-setting.png)

- **UV/UVX Path Override**：当系统 PATH 解析不正确时，可在 UI 中指定 `uv`/`uvx` 可执行文件路径（例如使用自定义安装）。清空后会回退到自动发现。
- **Server Source Override**：为 Python server（`uvx --from <url> mcp-for-unity`）设置本地文件夹或 git URL。清空后使用默认打包版本。
- **Dev Mode（强制全新安装 server）**：启用后，生成的 `uvx` 命令会在启动前添加 `--no-cache --refresh`。会更慢，但可避免在迭代 `Server/` 时误用旧缓存构建。
- **Local Package Deployment**：选择本地 `MCPForUnity` 文件夹（必须包含 `Editor/` 与 `Runtime/`），点击 **Deploy to Project** 后会将其复制到当前已安装的 package 路径（来自 `Packages/manifest.json` / Package Manager）。会在 `Library/MCPForUnityDeployBackups` 下保存带时间戳的备份，点击 **Restore Last Backup** 可回滚最近一次部署。

提示：

- 部署/回滚后，Unity 会自动刷新脚本；若不确定，可重新打开 MCP window 并在 Advanced Settings 里确认目标路径标签。
- 保持 source 与 target 不要混用（不要把 source 指向已经安装的 `PackageCache` 文件夹）。
- 推荐使用被 gitignore 的工作目录进行快速迭代；部署流程只会复制 `Editor` 与 `Runtime`。

## 快速切换 MCP 包源

从 unity-mcp 仓库运行，而不是从游戏的根目录。使用 `mcp_source.py` 可以在不同的 MCP for Unity 包源之间快速切换：

**用法：**

```bash
python mcp_source.py [--manifest /path/to/manifest.json] [--repo /path/to/unity-mcp] [--choice 1|2|3]
```

**选项：**

- **1** 上游 main（CoplayDev/unity-mcp）
- **2** 远程当前分支（origin + branch）
- **3** 本地工作区（file: MCPForUnity）

切换后，打开 Package Manager 并 Refresh 以重新解析依赖。

## Development Deployment Scripts

这些部署脚本帮助你快速测试 MCP for Unity 核心代码的更改。

## Scripts

### `deploy-dev.bat`

将你的开发代码部署到实际安装位置以便测试。

**它会做什么：**

1. 将原始文件备份到一个带时间戳的文件夹
2. 将 Unity Bridge 代码复制到 Unity 的 package cache
3. 将 Python Server 代码复制到 MCP 安装目录

**用法：**

1. 运行 `deploy-dev.bat`
2. 输入 Unity package cache 路径（脚本会给出示例）
3. 输入 server 路径（或使用默认：`%LOCALAPPDATA%\Programs\UnityMCP\UnityMcpServer\src`）
4. 输入备份位置（或使用默认：`%USERPROFILE%\Desktop\unity-mcp-backup`）

**注意：** Dev deploy 会跳过 `.venv`, `__pycache__`, `.pytest_cache`, `.mypy_cache`, `.git`；减少变动并避免复制虚拟环境。

### `restore-dev.bat`

从备份恢复原始文件。

**它会做什么：**

1. 列出所有带时间戳的备份
2. 让你选择要恢复的备份
3. 同时恢复 Unity Bridge 与 Python Server 文件

### `prune_tool_results.py`

将对话 JSON 中体积很大的 `tool_result` 内容压缩为简洁的一行摘要。

**用法：**

```bash
python3 prune_tool_results.py < reports/claude-execution-output.json > reports/claude-execution-output.pruned.json
```

脚本从 `stdin` 读取对话并将裁剪版本输出到 `stdout`，使日志更容易检查或存档。

这些默认策略可以显著降低 token 使用量，同时保留关键的信息。

## 查找 Unity Package Cache 路径

Unity 会把 Git 包存储在一个“版本号或哈希”的文件夹下，例如：

```
X:\UnityProject\Library\PackageCache\com.coplaydev.unity-mcp@<version-or-hash>
```

示例（哈希）：

```
X:\UnityProject\Library\PackageCache\com.coplaydev.unity-mcp@272123cfd97e

```

可靠的查找方式：

1. 打开 Unity Package Manager
2. 选择 “MCP for Unity” package
3. 右键 package 并选择 “Show in Explorer”
4. Unity 会打开该项目实际使用的 cache 文件夹

注意：在近期版本中，Python server 的源码也会打包在该 package 内的 `Server` 目录下。这对本地测试或让 MCP client 直接指向打包 server 很有用。

## Payload 大小与分页默认值（推荐）

某些 Unity 工具调用可能返回*非常大的* JSON payload（例如深层级场景、带完整序列化属性的组件）。为避免 MCP 响应过大、以及 Unity 卡死/崩溃，建议优先使用 **分页 + 先摘要后细节** 的读法，仅在需要时再拉取完整属性。

### `manage_scene(action="get_hierarchy")`

- **默认行为**：返回根 GameObject（无 `parent`）或指定 `parent` 的直接子节点的 **分页摘要**。不会内联完整递归子树。
- **分页参数**：
  - **`page_size`**：默认 **50**，限制 **1..500**
  - **`cursor`**：默认 **0**
  - **`next_cursor`**：当还有更多结果时返回 **字符串**；完成时为 `null`
- **其他安全阀**：
  - **`max_nodes`**：默认 **1000**，限制 **1..5000**
  - **`include_transform`**：默认 **false**

### `manage_scene(action="screenshot")`

- 将 PNG 保存到 `Assets/Screenshots/`。
- Unity **2022.1+**：通过 `ScreenCapture.CaptureScreenshot` 捕获 **Game View**，因此包含 `Screen Space - Overlay` UI。注意该写入是 **异步** 的，文件/导入可能会稍后出现。
- Unity **2021.3**：回退为将可用的 `Camera` 渲染到 `RenderTexture`（仅相机输出；不包含 `Screen Space - Overlay` UI）。

### `manage_gameobject(action="get_components")`

- **默认行为**：仅返回 **分页的组件元数据**（`typeName`, `instanceID`）。
- **分页参数**：
  - **`page_size`**：默认 **25**，限制 **1..200**
  - **`cursor`**：默认 **0**
  - **`max_components`**：默认 **50**，限制 **1..500**
  - **`next_cursor`**：当还有更多结果时返回 **字符串**；完成时为 `null`
- **按需读取属性**：
  - **`include_properties`** 默认 **false**
  - 当 `include_properties=true` 时，会启用保守的响应大小预算（约 **~250KB** JSON 文本），返回条数可能少于 `page_size`；使用 `next_cursor` 继续。

### 实用默认值（我们在 prompts/tests 中的推荐）

- **Hierarchy roots**：从 `page_size=50` 开始，根据 `next_cursor` 继续（大场景通常 1–2 次调用）。
- **Children**：按 `parent` 分页，`page_size=10..50`（根据预期广度）。
- **Components**：
  - 先用 `include_properties=false` 且 `page_size=10..25`
  - 需要完整属性时，用 `include_properties=true` 且保持较小 `page_size`（例如 **3..10**）来控制峰值 payload。

## MCP Bridge 压力测试

一个按需的压力测试工具会用多个并发客户端测试 MCP bridge，同时通过“立即脚本编辑”触发真实的脚本 reload（无需菜单调用）。

### 脚本

- `tools/stress_mcp.py`

### 它做什么

- 对 MCP for Unity bridge 启动 N 个 TCP 客户端（默认端口从 `~/.unity-mcp/unity-mcp-status-*.json` 自动发现）。
- 发送轻量 framed `ping` 维持并发。
- 同时，使用 `manage_script.apply_text_edits` 对目标 C# 文件在 EOF 追加唯一标记注释，并设置：
  - `options.refresh = "immediate"` 来立即触发 import/compile（会引发 domain reload），以及
  - 从当前文件内容计算 `precondition_sha256` 来避免漂移。
- 使用 EOF 插入避免头部/`using` guard 的编辑。

### 用法（本地）

```bash
# 推荐：使用测试项目中包含的大型脚本
python3 tools/stress_mcp.py \
  --duration 60 \
  --clients 8 \
  --unity-file "TestProjects/UnityMCPTests/Assets/Scripts/LongUnityScriptClaudeTest.cs"
```

### Flags

- `--project` Unity 项目路径（默认自动检测到仓库自带的测试项目）
- `--unity-file` 要编辑的 C# 文件（默认为长测试脚本）
- `--clients` 并发客户端数量（默认 10）
- `--duration` 运行秒数（默认 60）

### 预期结果

- Unity Editor 在 reload churn 下不崩溃
- 每次应用编辑后立即 reload（无需 `Assets/Refresh` 菜单调用）
- 在 domain reload 期间可能会有少量短暂断线或失败调用；工具会重试并继续
- 最后输出 JSON 汇总，例如：
  - `{"port": 6400, "stats": {"pings": 28566, "applies": 69, "disconnects": 0, "errors": 0}}`

### 说明与排障

- Immediate vs debounced：
  - 工具设置 `options.refresh = "immediate"` 让每次改动都立刻编译。如果你只想测试 churn（不关心每次确认），可以改成 debounced 来减少中途失败。
- 需要 precondition：
  - 对较大文件，`apply_text_edits` 需要 `precondition_sha256`。工具会先读文件计算 SHA。
- 编辑位置：
  - 为避免头部 guards 或复杂范围，工具每轮都在 EOF 追加一行 marker。
- Read API：
  - bridge 当前支持 `manage_script.read` 用于读文件。可能会看到弃用提示；对该内部工具无影响。
- 瞬时失败：
  - 偶尔出现 `apply_errors` 往往意味着连接在回包时发生 reload。通常编辑仍然已应用；循环会继续下一轮。

### CI 指导

- 由于 Unity/editor 依赖与运行时波动，不建议把它放进默认 PR CI。
- 可选择作为手动 workflow 或 nightly job 在支持 Unity 的 runner 上运行。

## CI 测试工作流（GitHub Actions）

我们提供 CI 作业来对 Unity 测试项目运行自然语言编辑套件：它会启动 headless Unity 容器并通过 MCP bridge 连接。要在 fork 上运行，你需要以下 GitHub Secrets：`ANTHROPIC_API_KEY` 以及 Unity 凭据（通常为 `UNITY_EMAIL` + `UNITY_PASSWORD` 或 `UNITY_LICENSE` / `UNITY_SERIAL`）。这些会在日志中被脱敏，因此不会泄露。

***如何运行***

- 触发：在 GitHub Actions 中手动触发 `workflow dispatch`（`Claude NL/T Full Suite (Unity live)`）。
- 镜像：`UNITY_IMAGE`（UnityCI）使用 tag 拉取；作业会在运行时解析 digest。日志会被清理。
- 执行：单次执行，每个测试生成一个片段（严格：每个文件只允许一个 `<testcase>`）。若任何片段只是裸 ID，会被占位符 guard 快速判失败。暂存目录（`reports/_staging`）会被提升到 `reports/` 以减少部分写入。
- 报告：JUnit 输出到 `reports/junit-nl-suite.xml`，Markdown 输出到 `reports/junit-nl-suite.md`。
- 发布：JUnit 会被规范化为 `reports/junit-for-actions.xml` 并发布；Artifacts 会上传 `reports/` 下的全部文件。

### 测试目标脚本

- 仓库包含一个很长且独立的 C# 脚本，用于验证大文件编辑与窗口读取：
  - `TestProjects/UnityMCPTests/Assets/Scripts/LongUnityScriptClaudeTest.cs`
  本地与 CI 都建议用它来测试多编辑批次、anchor insert、windowed read 等。

### 调整 tests / prompts

- 修改 `.claude/prompts/nl-unity-suite-t.md` 来调整 NL/T 步骤。遵循约定：每个测试在 `reports/<TESTID>_results.xml` 下生成一个 XML 片段，且每个片段恰好包含一个 `<testcase>`，其 `name` 必须以 test ID 开头。不要包含 prologue/epilogue 或代码围栏。
- 保持改动最小、可回滚，并给出简洁证据。

### 运行套件

1) 推送你的分支，然后在 Actions 标签页手动运行 workflow。
2) 作业把 reports 写入 `reports/` 并上传 artifacts。
3) “JUnit Test Report” check 会汇总结果；打开 Job Summary 查看完整 Markdown。

### 查看结果

- Job Summary：Actions 中的内联 Markdown 汇总
- Check：“JUnit Test Report”
- Artifacts：`claude-nl-suite-artifacts`，包含 XML 与 MD

### MCP 连接调试

- 在 MCP for Unity 窗口（Editor 内）*启用 debug logs*，可以看到连接状态、auto-setup 结果与 MCP client 路径，包括：
  - bridge 启动/端口、client 连接、strict framing 协商、解析后的 frame
  - auto-config 路径检测（Windows/macOS/Linux）、uv/claude 解析与错误提示
- CI 中如启动失败，作业会 tail Unity 日志（serial/license/password/token 已脱敏），并打印 socket/status JSON 诊断。

## Workflow

1. **修改** 此目录中的源码
2. **Deploy** 使用 `deploy-dev.bat`
3. **在 Unity 中测试**（先重启 Unity Editor）
4. **迭代** - 按需重复 1-3
5. **Restore** 完成后用 `restore-dev.bat` 恢复原始文件

## 重要说明

### 更新工具和 Manifest

在 Unity 包中添加或修改 MCP 工具时：
- 工具定义位于仓库根目录的 manifest.json 文件中
- 在发布过程中，manifest.json 版本会自动与 MCPForUnity/package.json 保持同步
- 如果在发布过程之外手动更新工具，请确保相应更新 manifest.json 版本
- 使用综合版本更新脚本：`python3 tools/update_versions.py` 来同步项目中所有版本引用

`update_versions.py` 脚本会更新：
- MCPForUnity/package.json（Unity 包版本）
- manifest.json（MCP bundle manifest）
- Server/pyproject.toml（Python 包版本）
- Server/README.md（版本引用）
- README.md（固定版本示例）
- docs/i18n/README-zh.md（固定版本示例）

使用示例：
```bash
# 更新所有文件以匹配 package.json 版本
python3 tools/update_versions.py

# 更新所有文件到指定版本
python3 tools/update_versions.py --version 9.2.0

# 干运行以查看将要更新的内容
python3 tools/update_versions.py --dry-run
```

## Troubleshooting

### 运行 .bat 时出现 "Path not found"

- 确认 Unity package cache 路径正确
- 确认 MCP for Unity package 已安装
- 确认 server 已通过 MCP client 安装

### 出现 "Permission denied"

- 用管理员权限运行 cmd
- 部署前关闭 Unity Editor
- 部署前关闭所有 MCP client

### 出现 "Backup not found"

- 先运行 `deploy-dev.bat` 生成初始备份
- 检查备份目录权限
- 确认备份路径正确

### Windows uv 路径问题

- 在 Windows 上测试 GUI client 时，优先使用 WinGet Links 下的 `uv.exe`；若存在多个 `uv.exe`，可用 “Choose `uv` Install Location” 固定 Links shim。

### Unity 退到后台时 Domain Reload Tests 卡住

在测试过程中触发脚本编译（例如 `DomainReloadResilienceTests`）时，如果 Unity 不是前台窗口，测试可能会卡住。这是操作系统层面的限制——macOS 会限制后台应用的主线程，从而阻止编译完成。

**Workarounds：**

- 运行 domain reload tests 时保持 Unity 在前台
- 在测试套件最开始运行它们（在 Unity 被切到后台之前）
- 使用 `[Explicit]` 属性将其从默认运行中排除

**注意：** MCP workflow 本身不受影响——socket 消息会给 Unity 提供外部刺激，使其即使在后台也保持响应。该限制主要影响 Unity 内部测试协程的等待。
