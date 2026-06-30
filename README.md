<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="docs/images/logo-header-dark.png">
    <img alt="MCP for Unity" src="docs/images/logo-header-light.png" width="400">
  </picture>
</p>

<div align="center">

[English](README.md) <img src="docs/images/connector.svg" alt="↔" height="14"> [简体中文](docs/i18n/README-zh.md) &nbsp;&nbsp;&nbsp;|&nbsp;&nbsp;&nbsp; [Discord](https://discord.gg/y4p8KfzrN4) <img src="docs/images/connector.svg" alt="↔" height="14"> [Wiki](https://coplaydev.github.io/unity-mcp/)

#### Proudly sponsored and maintained by [Aura](https://www.tryaura.dev/) — the AI assistant for Unreal & Unity.
##### And don't miss [Godot AI](https://github.com/hi-godot/godot-ai), the new open source project from the makers of MCP for Unity.

</div>

<p align="center"><b>Create your Unity apps with LLMs.</b> MCP for Unity bridges AI assistants — Claude, Codex, VS Code, local LLMs, and more — with your Unity Editor via <a href="https://modelcontextprotocol.io/introduction">Model Context Protocol</a>. Give your LLM the tools to manage assets, control scenes, edit scripts, run tests, and automate your game dev workflows.</p>

<p align="center">
  <img alt="MCP for Unity building a scene" src="docs/images/building_scene.gif">
</p>

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

## What it does

Control the Unity Editor in natural language from any MCP client — create scenes & GameObjects, edit C# scripts, manage assets, run tests, profile, and build. 40+ focused tools, any client, free & MIT.

**[Browse the full tool catalog →](https://coplaydev.github.io/unity-mcp/reference/tools/)**

---

## Quickstart

**Requirements:** Unity **2021.3 LTS → 6.x** · Python **3.10+** (via [`uv`](https://docs.astral.sh/uv/)). Works with **any MCP client** — Claude Desktop & Code, Cursor, VS Code, Windsurf, Cline, Gemini CLI, and more.

1. **Install** — Unity → Package Manager → Add from git URL:
   `https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#beta` &nbsp;_(or `openupm add com.coplaydev.unity-mcp`)_
2. **Configure** — `Window → MCP for Unity → Configure All Detected Clients`.
3. **Prompt** — *"Create a cube at the origin and add a Rigidbody."* The cube appears in seconds.

---

## AI Asset Generation

Bring-your-own-key generation of **3D models** (text→3D and image→3D), **2D images**, and **Sketchfab** model import — straight into your project. Generated assets land under `Assets/Generated/`.

**Providers**

- **3D generation:** Tripo (default), Meshy
- **3D import:** Sketchfab
- **2D image:** fal.ai (default), OpenRouter

**Setup**

1. Open **Window → MCP for Unity → Asset Gen** tab and enter your provider API key(s). Keys are stored in your **OS secure store** (macOS Keychain / Windows Credential Manager / Linux libsecret) — never in the project or `EditorPrefs`.
2. Enable the tools with `manage_tools` — the `asset_gen` group is off by default.
3. For GLB output/import, install **glTFast** from the **Dependencies** tab (FBX needs no extra package).

**Usage**

Generation runs through the MCP tools (or the `asset-gen` CLI), never from the GUI. Long-running jobs are async: the tool returns a `job_id`, then you call `action="status"` with that `job_id` until it completes. Both 3D and 2D accept **text** or **image** input — pass `image_url` (a hosted URL) or `image_path` (a local file, e.g. under `Assets/`).

```text
generate_model  action=generate provider=tripo mode=text  prompt="a low-poly oak tree" format=fbx     → then action=status with job_id
generate_model  action=generate provider=meshy mode=image image_path=Assets/refs/chair.png           → image→3D from a local image
generate_image  action=generate provider=fal   mode=text  prompt="a pixel-art coin"
import_model    action=search query="wooden chair"   → action=import uid=<from search>
```

**Notes**

- **Local images** (`image_path`) are supported by Meshy (image→3D) and fal / OpenRouter (image→image); they're sent inline as base64. Tripo image→3D currently needs a hosted `image_url`.
- **Transparency / size:** `transparent` only sets the Unity texture import flag — fal/FLUX has no transparent-background *generation*. `width`/`height` are forwarded to fal only (OpenRouter's chat API has no size control).

Your API keys never leave the Editor and never cross the MCP bridge.

**Blender handoff:** With BlenderMCP connected, use the `blender-to-unity` skill to export
the current Blender model and import it via `import_model_file` (defaults to FBX). BlenderMCP
handles modeling/generation; MCP for Unity handles import + scene placement. The **Asset Gen**
tab shows a best-effort "Blender app detected" status (BlenderMCP itself is configured in your
AI client and isn't detectable from Unity).

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
