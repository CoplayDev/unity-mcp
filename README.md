<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="docs/images/logo-header-dark.png">
    <img alt="MCP for Unity" src="docs/images/logo-header-light.png" width="400">
  </picture>
</p>

<div align="center">

[English](README.md) &nbsp;·&nbsp; [简体中文](docs/i18n/README-zh.md)

#### Proudly sponsored and maintained by [Aura](https://www.tryaura.dev/) — the AI assistant for Unreal & Unity.
##### And don't miss [Godot AI](https://github.com/hi-godot/godot-ai), the new open source project from the makers of MCP for Unity.

[![Docs](https://img.shields.io/badge/Docs-unity--mcp-4f46e5)](https://coplaydev.github.io/unity-mcp/)
[![Discord](https://img.shields.io/badge/discord-join-red.svg?logo=discord&logoColor=white)](https://discord.gg/y4p8KfzrN4)
[![](https://img.shields.io/badge/Unity-000000?style=flat&logo=unity&logoColor=blue 'Unity')](https://unity.com/releases/editor/archive)
[![python](https://img.shields.io/badge/Python-3.10+-3776AB.svg?style=flat&logo=python&logoColor=white)](https://www.python.org)
[![](https://badge.mcpx.dev?status=on 'MCP Enabled')](https://modelcontextprotocol.io/introduction)
[![](https://img.shields.io/badge/License-MIT-red.svg 'MIT License')](https://opensource.org/licenses/MIT)
[![Downloads](https://static.pepy.tech/badge/mcpforunityserver)](https://pepy.tech/project/mcpforunityserver)
[![Release](https://img.shields.io/github/v/release/CoplayDev/unity-mcp)](https://github.com/CoplayDev/unity-mcp/releases)
[![CI](https://img.shields.io/github/actions/workflow/status/CoplayDev/unity-mcp/python-tests.yml?branch=beta&label=tests)](https://github.com/CoplayDev/unity-mcp/actions/workflows/python-tests.yml)
[![OpenUPM](https://img.shields.io/npm/v/com.coplaydev.unity-mcp?label=OpenUPM&registry_uri=https://package.openupm.com)](https://openupm.com/packages/com.coplaydev.unity-mcp/)
[![Stars](https://img.shields.io/github/stars/CoplayDev/unity-mcp?style=flat)](https://github.com/CoplayDev/unity-mcp/stargazers)

</div>

<p align="center"><b>Create your Unity apps with LLMs.</b> MCP for Unity bridges AI assistants — Claude, Codex, VS Code, local LLMs, and more — with your Unity Editor via the <a href="https://modelcontextprotocol.io/introduction">Model Context Protocol</a>. Give your LLM the tools to manage assets, control scenes, edit scripts, run tests, and automate workflows.</p>

<p align="center">
  <img alt="MCP for Unity building a scene" src="docs/images/building_scene.gif">
</p>

---

## What it does

Control the Unity Editor in natural language from any MCP client. Describe what
you want — "add a player with a rigidbody", "write a save system", "run the
tests" — and the assistant drives the Editor through 40+ focused tools.

- **Scenes & GameObjects** — create, query, modify hierarchies, components, prefabs
- **Scripts** — create/edit C# with Roslyn validation
- **Assets & rendering** — materials, shaders, textures, VFX
- **Build, test & profile** — run EditMode/PlayMode tests, the profiler, builds
- **Any MCP client** — Claude, Cursor, VS Code, Windsurf, Gemini CLI, and more
- **Free & MIT**, multi-instance ready

<details>
<summary><b>Full tool catalog (40+ tools across 9 groups)</b></summary>

See the [tool reference](https://coplaydev.github.io/unity-mcp/reference/tools/) for
every tool: **core** (scenes, GameObjects, scripts, assets, prefabs, components,
editor control, console, menus), **scripting_ext**, **vfx** (shaders, textures),
**ui**, **animation**, **testing**, **probuilder**, **profiling**, **docs**.
</details>

---

## Supported clients & versions

Works with **any MCP client** — including Claude Desktop & Claude Code, Cursor, VS Code (Copilot), Windsurf, Cline, Gemini CLI, Qwen Code, Copilot CLI, OpenClaw, and Antigravity. One step sets them all up: **Window → MCP for Unity → Configure All Detected Clients**.

**Requirements:** Unity **2021.3 LTS → 6.x** · Python **3.10+** (managed via [`uv`](https://docs.astral.sh/uv/)).

---

## 60-second quickstart

**Prerequisites:** Unity 2021.3 LTS+, an MCP client, and [uv](https://docs.astral.sh/uv/) (auto-installed if missing).

1. **Install the package** (Unity → Package Manager → Add from git URL):
   `https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#beta`
2. **Configure your client:** `Window → MCP for Unity → Configure All Detected Clients`.
3. **Prompt it:** *"Create a cube at the origin and add a Rigidbody."*
   You should see the cube appear in the scene within a couple of seconds.

<details>
<summary>Alternative installs (Asset Store, OpenUPM)</summary>

- **OpenUPM:** `openupm add com.coplaydev.unity-mcp`
- **Asset Store:** [MCP for Unity](https://assetstore.unity.com/packages/slug/329908)
</details>

---

## How it works

Your prompt → **MCP client** (Claude, Cursor, …) → **MCP for Unity server** → **Unity Editor plugin** → your **scenes, assets & scripts**.

See the [architecture overview](https://coplaydev.github.io/unity-mcp/architecture/transports) for transports, multi-instance routing, and internals.

---

<!-- recent-updates:start -->
<details>
<summary><strong>Recent Updates</strong></summary>

* **[v9.7.0](https://github.com/CoplayDev/unity-mcp/releases/tag/v9.7.0)** (2026-05-22)
* **[v9.6.8](https://github.com/CoplayDev/unity-mcp/releases/tag/v9.6.8)** (2026-04-27)
* **[v9.6.6](https://github.com/CoplayDev/unity-mcp/releases/tag/v9.6.6)** (2026-04-07)
* **[v9.6.5](https://github.com/CoplayDev/unity-mcp/releases/tag/v9.6.5)** (2026-04-03)
* **[v9.6.4](https://github.com/CoplayDev/unity-mcp/releases/tag/v9.6.4)** (2026-03-31)

Full history: [Release Notes](https://coplaydev.github.io/unity-mcp/releases).

</details>
<!-- recent-updates:end -->

---

## Community

- [Discord](https://discord.gg/y4p8KfzrN4) — chat with maintainers and other contributors
- [Issues](https://github.com/CoplayDev/unity-mcp/issues) — bugs and feature requests
- [Discussions](https://github.com/CoplayDev/unity-mcp/discussions) — design ideas and broader questions
- Security: see [SECURITY.md](SECURITY.md) for private reporting

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Branch off `beta`, not `main`. The full dev setup, testing, and release process live in the [Contributing](https://coplaydev.github.io/unity-mcp/contributing/dev-setup) docs.

## Advanced

- **Multiple Unity instances** — [Multi-Instance Routing](https://coplaydev.github.io/unity-mcp/guides/multi-instance)
- **Tool groups (vfx / animation / ui / testing / etc.)** — [Tool Groups](https://coplaydev.github.io/unity-mcp/guides/tool-groups)
- **Roslyn script validation** — [Roslyn Validation](https://coplaydev.github.io/unity-mcp/guides/roslyn)
- **Remote-hosted server with auth** — [Remote Server Auth](https://coplaydev.github.io/unity-mcp/guides/remote-server-auth)

## Star History

[![Star History Chart](https://api.star-history.com/svg?repos=CoplayDev/unity-mcp&type=Date)](https://www.star-history.com/#CoplayDev/unity-mcp&Date)

## Citation

If MCP for Unity helped your research, please cite it.

```bibtex
@inproceedings{wu2025mcpunity,
  author    = {Wu, Shutong and Barnett, Justin P.},
  title     = {{MCP-Unity}: {Protocol-Driven} Framework for Interactive {3D} Authoring},
  year      = {2025},
  isbn      = {9798400721366},
  publisher = {Association for Computing Machinery},
  address   = {New York, NY, USA},
  url       = {https://doi.org/10.1145/3757376.3771417},
  doi       = {10.1145/3757376.3771417},
  series    = {SA Technical Communications '25}
}
```

## Unity AI Tools by Aura

Aura offers 2 AI tools for Unity:
- **MCP for Unity** is available freely under the MIT license.
- **Aura for Unity** is a premium Unity/Unreal AI assistant built for game devs.

## Disclaimer

This project is a free and open-source tool for the Unity Editor, and is not affiliated with Unity Technologies.

---

**License:** MIT — see [LICENSE](LICENSE).
