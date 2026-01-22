# MCP Client Configurators

This guide explains how MCP client configurators work in this repo and how to add a new one.

It covers:

- **Typical JSON-file clients** (Cursor, VSCode GitHub Copilot, VSCode Insiders, Windsurf, Kiro, Trae, Antigravity, etc.).
- **Special clients** like **Claude CLI** and **Codex** that require custom logic.
- **How to add a new configurator class** so it shows up automatically in the MCP for Unity window.

## Quick example: JSON-file configurator

For most clients you just need a small class like this:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using MCPForUnity.Editor.Models;

namespace MCPForUnity.Editor.Clients.Configurators
{
    public class MyClientConfigurator : JsonFileMcpConfigurator
    {
        public MyClientConfigurator() : base(new McpClient
        {
            name = "My Client",
            windowsConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".myclient", "mcp.json"),
            macConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".myclient", "mcp.json"),
            linuxConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".myclient", "mcp.json"),
        })
        { }

        public override IList<string> GetInstallationSteps() => new List<string>
        {
            "Open My Client and go to MCP settings",
            "Open or create the mcp.json file at the path above",
            "Click Configure in MCP for Unity (or paste the manual JSON snippet)",
            "Restart My Client"
        };
    }
}
```

---

## How the configurator system works

At a high level:

- **`IMcpClientConfigurator`** (`MCPForUnity/Editor/Clients/IMcpClientConfigurator.cs`)
  - Contract for all MCP client configurators.
  - Handles status detection, auto-configure, manual snippet, and installation steps.

- **Base classes** (`MCPForUnity/Editor/Clients/McpClientConfiguratorBase.cs`)
  - **`McpClientConfiguratorBase`**
    - Common properties and helpers.
  - **`JsonFileMcpConfigurator`**
    - For JSON-based config files (most clients).
    - Implements `CheckStatus`, `Configure`, and `GetManualSnippet` using `ConfigJsonBuilder`.
  - **`CodexMcpConfigurator`**
    - For Codex-style TOML config files.
  - **`ClaudeCliMcpConfigurator`**
    - For CLI-driven clients like Claude Code (register/unregister via CLI, not JSON files).

- **`McpClient` model** (`MCPForUnity/Editor/Models/McpClient.cs`)
  - Holds the per-client configuration:
    - `name`
    - `windowsConfigPath`, `macConfigPath`, `linuxConfigPath`
    - Status and several **JSON-config flags** (used by `JsonFileMcpConfigurator`):
      - `IsVsCodeLayout` – VS Code-style layout (`servers` root, `type` field, etc.).
      - `SupportsHttpTransport` – whether the client supports HTTP transport.
      - `EnsureEnvObject` – ensure an `env` object exists.
      - `StripEnvWhenNotRequired` – remove `env` when not needed.
      - `HttpUrlProperty` – which property holds the HTTP URL (e.g. `"url"` vs `"serverUrl"`).
      - `DefaultUnityFields` – key/value pairs like `{ "disabled": false }` applied when missing.

- **Auto-discovery** (`McpClientRegistry`)
  - `McpClientRegistry.All` uses `TypeCache.GetTypesDerivedFrom<IMcpClientConfigurator>()` to find configurators.
  - A configurator appears automatically if:
    - It is a **public, non-abstract class**.
    - It has a **public parameterless constructor**.
  - No extra registration list is required.

---

## Typical JSON-file clients

Most MCP clients use a JSON config file that defines one or more MCP servers. Examples:

- **Cursor** – `JsonFileMcpConfigurator` (global `~/.cursor/mcp.json`).
- **VSCode GitHub Copilot** – `JsonFileMcpConfigurator` with `IsVsCodeLayout = true`.
- **VSCode Insiders GitHub Copilot** – `JsonFileMcpConfigurator` with `IsVsCodeLayout = true` and Insider-specific `Code - Insiders/User/mcp.json` paths.
- **Windsurf** – `JsonFileMcpConfigurator` with Windsurf-specific flags (`HttpUrlProperty = "serverUrl"`, `DefaultUnityFields["disabled"] = false`, etc.).
- **Kiro**, **Trae**, **Antigravity (Gemini)** – JSON configs with project-specific paths and flags.

All of these follow the same pattern:

1. **Subclass `JsonFileMcpConfigurator`.**
2. **Provide a `McpClient` instance** in the constructor with:
   - A user-friendly `name`.
   - OS-specific config paths.
   - Any JSON behavior flags as needed.
3. **Override `GetInstallationSteps`** to describe how users open or edit the config.
4. Rely on **base implementations** for:
   - `CheckStatus` – reads and validates the JSON config; can auto-rewrite to match Unity MCP.
   - `Configure` – writes/rewrites the config file.
   - `GetManualSnippet` – builds a JSON snippet using `ConfigJsonBuilder`.

### JSON behavior controlled by `McpClient`

`JsonFileMcpConfigurator` relies on the fields on `McpClient`:

- **HTTP vs stdio**
  - `SupportsHttpTransport` + `EditorPrefs.UseHttpTransport` decide whether to configure
    - `url` / `serverUrl` (HTTP), or
    - `command` + `args` (stdio with `uvx`).
- **URL property name**
  - `HttpUrlProperty` (default `"url"`) selects which JSON property to use for HTTP urls.
  - Example: Windsurf and Antigravity use `"serverUrl"`.
- **VS Code layout**
  - `IsVsCodeLayout = true` switches config structure to a VS Code compatible layout.
- **Env object and default fields**
  - `EnsureEnvObject` / `StripEnvWhenNotRequired` control an `env` block.
  - `DefaultUnityFields` adds client-specific fields if they are missing (e.g. `disabled: false`).

All of this logic is centralized in **`ConfigJsonBuilder`**, so most JSON-based clients **do not need to override** `GetManualSnippet`.

---

## Special clients

Some clients cannot be handled by the generic JSON configurator alone.

### Codex (TOML-based)

- Uses **`CodexMcpConfigurator`**.
- Reads and writes a **TOML** config (usually `~/.codex/config.toml`).
- Uses `CodexConfigHelper` to:
  - Parse the existing TOML.
  - Check for a matching Unity MCP server configuration.
  - Write/patch the Codex server block.
- The `CodexConfigurator` class:
  - Only needs to supply a `McpClient` with TOML config paths.
  - Inherits the Codex-specific status and configure behavior from `CodexMcpConfigurator`.

### Claude Code (CLI-based)

- Uses **`ClaudeCliMcpConfigurator`**.
- Configuration is stored **internally by the Claude CLI**, not in a JSON file.
- `CheckStatus` and `Configure` are implemented in the base class using `claude mcp ...` commands:
  - `CheckStatus` calls `claude mcp list` to detect if `UnityMCP` is registered.
  - `Configure` toggles register/unregister via `claude mcp add/remove UnityMCP`.
- The `ClaudeCodeConfigurator` class:
  - Only needs a `McpClient` with a `name`.
  - Overrides `GetInstallationSteps` with CLI-specific instructions.

### Claude Desktop (JSON with restrictions)

- Uses **`JsonFileMcpConfigurator`**, but only supports **stdio transport**.
- `ClaudeDesktopConfigurator`:
  - Sets `SupportsHttpTransport = false` in `McpClient`.
  - Overrides `Configure` / `GetManualSnippet` to:
    - Guard against HTTP mode.
    - Provide clear error text if HTTP is enabled.

---

## Adding a new MCP client (typical JSON case)

This is the most common scenario: your MCP client uses a JSON file to configure servers.

### 1. Choose the base class

- Use **`JsonFileMcpConfigurator`** if your client reads a JSON config file.
- Consider **`CodexMcpConfigurator`** only if you are integrating a TOML-based client like Codex.
- Consider **`ClaudeCliMcpConfigurator`** only if your client exposes a CLI command to manage MCP servers.

### 2. Create the configurator class

Create a new file under:

```text
MCPForUnity/Editor/Clients/Configurators
```

Name it something like:

```text
MyClientConfigurator.cs
```

Inside, follow the existing pattern (e.g. `CursorConfigurator`, `WindsurfConfigurator`, `KiroConfigurator`):

- **Namespace** must be:
  - `MCPForUnity.Editor.Clients.Configurators`
- **Class**:
  - `public class MyClientConfigurator : JsonFileMcpConfigurator`
- **Constructor**:
  - Public, **parameterless**, and call `base(new McpClient { ... })`.
  - Set at least:
    - `name = "My Client"`
    - `windowsConfigPath = ...`
    - `macConfigPath = ...`
    - `linuxConfigPath = ...`
  - Optionally set flags:
    - `IsVsCodeLayout = true` for VS Code-style config.
    - `HttpUrlProperty = "serverUrl"` if your client expects `serverUrl`.
    - `EnsureEnvObject` / `StripEnvWhenNotRequired` based on env handling.
    - `DefaultUnityFields = { { "disabled", false }, ... }` for client-specific defaults.

Because the constructor is parameterless and public, **`McpClientRegistry` will auto-discover this configurator** with no extra registration.

### 3. Add installation steps

Override `GetInstallationSteps` to tell users how to configure the client:

- Where to find or create the JSON config file.
- Which menu path opens the MCP settings.
- Whether they should rely on the **Configure** button or copy-paste the manual JSON.

Look at `CursorConfigurator`, `VSCodeConfigurator`, `VSCodeInsidersConfigurator`, `KiroConfigurator`, `TraeConfigurator`, or `AntigravityConfigurator` for phrasing.

### 4. Rely on the base JSON logic

Unless your client has very unusual behavior, you typically **do not need to override**:

- `CheckStatus`
- `Configure`
- `GetManualSnippet`

The base `JsonFileMcpConfigurator`:

- Detects missing or mismatched config.
- Optionally rewrites config to match Unity MCP.
- Builds a JSON snippet with **correct HTTP vs stdio settings**, using `ConfigJsonBuilder`.

Only override these methods if your client has constraints that cannot be expressed via `McpClient` flags.

### 5. Verify in Unity

After adding your configurator class:

1. Open Unity and the **MCP for Unity** window.
2. Your client should appear in the list, sorted by display name (`McpClient.name`).
3. Use **Check Status** to verify:
   - Missing config files show as `Not Configured`.
   - Existing files with matching server settings show as `Configured`.
4. Click **Configure** to auto-write the config file.
5. Restart your MCP client and confirm it connects to Unity.

---

## Adding a custom (non-JSON) client

If your MCP client doesnt store configuration as a JSON file, you likely need a custom base class.

### Codex-style TOML client

- Subclass **`CodexMcpConfigurator`**.
- Provide TOML paths via `McpClient` (similar to `CodexConfigurator`).
- Override `GetInstallationSteps` to describe how to open/edit the TOML.

The Codex-specific status and configure logic is already implemented in the base class.

### CLI-managed client (Claude-style)

- Subclass **`ClaudeCliMcpConfigurator`**.
- Provide a `McpClient` with a `name`.
- Override `GetInstallationSteps` with the CLI flow.

The base class:

- Locates the CLI binary using `MCPServiceLocator.Paths`.
- Uses `ExecPath.TryRun` to call `mcp list`, `mcp add`, and `mcp remove`.
- Implements `Configure` as a toggle between register and unregister.

Use this only if the client exposes an official CLI for managing MCP servers.

---

## Summary

- **For most MCP clients**, you only need to:
  - Create a `JsonFileMcpConfigurator` subclass in `Editor/Clients/Configurators`.
  - Provide a `McpClient` with paths and flags.
  - Override `GetInstallationSteps`.
- **Special cases** like Codex (TOML) and Claude Code (CLI) have dedicated base classes.
- **No manual registration** is needed: `McpClientRegistry` auto-discovers all configurators with a public parameterless constructor.

Following these patterns keeps all MCP client integrations consistent and lets users configure everything from the MCP for Unity window with minimal friction.
