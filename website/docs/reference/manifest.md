---
id: manifest
slug: /reference/manifest
title: manifest.json Reference
sidebar_label: manifest.json
description: The repo-root manifest.json — what it describes, why it ships, and which fields are authoritative for the MCP marketplace bundle.
---

# `manifest.json` Reference

The `manifest.json` at the repo root describes MCP for Unity as a package — independent of Unity's UPM `package.json` (which lives at `MCPForUnity/package.json`). It's used by MCP marketplaces and aggregators to surface the project's metadata, server invocation, and tool catalog.

If you're adding a new MCP tool, update [the tool registry](/architecture/python-layers) and let CI's drift check fail any stale entry — the generator keeps the docs in sync. The `tools` block in `manifest.json` is a separate, hand-maintained surface (see Notes below).

## Top-level fields

| Field | Type | Description |
|---|---|---|
| `manifest_version` | string | Schema version for this manifest (currently `"0.3"`) |
| `name` | string | Display name shown by aggregators |
| `version` | string | Semver of the current release |
| `description` | string | One-line product description |
| `author.name` | string | Maintainer's display name |
| `author.url` | string | Maintainer's website |
| `repository.type` | string | `"git"` |
| `repository.url` | string | Canonical repo URL |
| `homepage` | string | Project homepage |
| `documentation` | string | Docs landing URL |
| `support` | string | Where to file issues |
| `icon` | string | Path to a square icon, relative to the manifest |

## `server`

Tells aggregators how to launch the Python server.

```json
"server": {
  "type": "python",
  "entry_point": "Server/src/main.py",
  "mcp_config": {
    "command": "uvx",
    "args": ["--from", "mcpforunityserver", "mcp-for-unity"],
    "env": {}
  }
}
```

- **`type`** — runtime family. Currently always `"python"`.
- **`entry_point`** — file an aggregator would point a Python interpreter at if it weren't using `uvx`.
- **`mcp_config.command`** — recommended launch command. `uvx` keeps the dependency tree managed without a global install.
- **`mcp_config.args`** — invocation arguments. Default transport is `http`; pass `--transport stdio` to switch.
- **`mcp_config.env`** — environment variables to set before launching (telemetry opt-outs, log levels, etc.).

## `tools`

A flat array of `{ name, description }` entries listing every MCP tool the server exposes. Aggregators use it for search and category surfaces without having to introspect the live registry.

This list is hand-maintained for now. The authoritative count and metadata live in the Python tool registry — see the [Tool reference](/reference/tools) for the generated catalog with full parameter docs.

## Notes

- `manifest.json` is NOT the Unity UPM manifest. That's `MCPForUnity/package.json` (name: `com.coplaydev.unity-mcp`).
- The Python PyPI package metadata lives in `Server/pyproject.toml` (name: `mcpforunityserver`).
- All three — `manifest.json`, `package.json`, `pyproject.toml` — are independent surfaces with overlapping but non-identical fields. A rename touches all three.
- An MCPB bundle is produced from `manifest.json` via [`tools/generate_mcpb.py`](https://github.com/CoplayDev/unity-mcp/blob/beta/tools/generate_mcpb.py).

## Where it ships

The current `manifest.json` is at the repo root: [`manifest.json`](https://github.com/CoplayDev/unity-mcp/blob/beta/manifest.json).
