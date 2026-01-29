# MCP for Unity - Developer Guide

| [English](README-DEV.md) | [简体中文](README-DEV-zh.md) |
|---------------------------|------------------------------|

## Contributing

**Branch off `beta`** to create PRs. The `main` branch is reserved for stable releases.

Before proposing major new features, please reach out to discuss - someone may already be working on it or it may have been considered previously. Open an issue or discussion to coordinate.

## Local Development Setup

### 1. Point Unity to Your Local Server

For the fastest iteration when working on the Python server:

1. Open Unity and go to **Window > MCP for Unity**
2. Open **Settings > Advanced Settings**
3. Set **Server Source Override** to your local `Server/` directory path
4. Enable **Dev Mode (Force fresh server install)** - this adds `--refresh` to uvx commands so your changes are picked up on every server start

### 2. Switch Package Sources

You may want to use the `mcp_source.py` script to quickly switch your Unity project between different MCP package sources [allows you to quickly point your personal project to your local or remote unity-mcp repo, or the live upstream (Coplay) versions of the unity-mcp package]:

```bash
python mcp_source.py
```

Options:
1. **Upstream main** - stable release (CoplayDev/unity-mcp)
2. **Upstream beta** - development branch (CoplayDev/unity-mcp#beta)
3. **Remote branch** - your fork's current branch
4. **Local workspace** - file: URL to your local MCPForUnity folder

After switching, open Package Manager in Unity and Refresh to re-resolve packages.

## Running Tests

All major new features (and some minor ones) must include test coverage. It's so easy to get LLMs to write tests, ya gotta do it!

### Python Tests 

Located in `Server/tests/`:

```bash
cd Server
uv run pytest tests/ -v
```

### Unity C# Tests

Located in `TestProjects/UnityMCPTests/Assets/Tests/`.

**Using the CLI** (requires Unity running with MCP bridge connected):

```bash
cd Server

# Run EditMode tests (default)
uv run python -m cli.main editor tests

# Run PlayMode tests
uv run python -m cli.main editor tests --mode PlayMode

# Run async and poll for results (useful for long test runs)
uv run python -m cli.main editor tests --async
uv run python -m cli.main editor poll-test <job_id> --wait 60

# Show only failed tests
uv run python -m cli.main editor tests --failed-only
```

**Using MCP tools directly** (from any MCP client):

```
run_tests(mode="EditMode")
get_test_job(job_id="<id>", wait_timeout=60)
```

### Code Coverage

```bash
cd Server
uv run pytest tests/ --cov --cov-report=html
open htmlcov/index.html
```

