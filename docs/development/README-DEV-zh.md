# MCP for Unity - 开发者指南

| [English](README-DEV.md) | [简体中文](README-DEV-zh.md) |
|---------------------------|------------------------------|

## 贡献代码

**从 `beta` 分支创建 PR**。`main` 分支仅用于稳定版本发布。

在提出重大新功能之前，请先联系讨论——可能已有人在开发，或者该功能曾被讨论过。请通过 issue 或 discussion 进行协调。

## 本地开发环境设置

### 1. 将 Unity 指向本地 Server

开发 Python server 时，最快的迭代方式：

1. 打开 Unity，进入 **Window > MCP for Unity**
2. 打开 **Settings > Advanced Settings**
3. 将 **Server Source Override** 设置为本地 `Server/` 目录路径
4. 启用 **Dev Mode (Force fresh server install)** - 这会在 uvx 命令中添加 `--refresh`，确保每次启动 server 时都使用最新代码

### 2. 切换包源

使用 `mcp_source.py` 快速切换 Unity 项目的 MCP 包源：

```bash
python mcp_source.py
```

选项：
1. **Upstream main** - 稳定版本 (CoplayDev/unity-mcp)
2. **Upstream beta** - 开发分支 (CoplayDev/unity-mcp#beta)
3. **Remote branch** - 你的 fork 当前分支
4. **Local workspace** - 指向本地 MCPForUnity 文件夹的 file: URL

切换后，在 Unity 中打开 Package Manager 并 Refresh 以重新解析依赖。

## 运行测试

所有新功能都应包含测试覆盖。

### Python 测试 (502 个测试)

位于 `Server/tests/`：

```bash
cd Server
uv run pytest tests/ -v
```

### Unity C# 测试 (605 个测试)

位于 `TestProjects/UnityMCPTests/Assets/Tests/`。

**使用 CLI**（需要 Unity 运行且 MCP bridge 已连接）：

```bash
cd Server

# 运行 EditMode 测试（默认）
uv run python -m cli.main editor tests

# 运行 PlayMode 测试
uv run python -m cli.main editor tests --mode PlayMode

# 异步运行并轮询结果（适用于长时间测试）
uv run python -m cli.main editor tests --async
uv run python -m cli.main editor poll-test <job_id> --wait 60

# 仅显示失败的测试
uv run python -m cli.main editor tests --failed-only
```

**直接使用 MCP 工具**（从任意 MCP 客户端）：

```
run_tests(mode="EditMode")
get_test_job(job_id="<id>", wait_timeout=60)
```

### 代码覆盖率

```bash
cd Server
uv run pytest tests/ --cov --cov-report=html
open htmlcov/index.html
```
