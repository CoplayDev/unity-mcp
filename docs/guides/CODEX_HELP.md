### Codex: MCP for Unity setup

Codex can use MCP for Unity through the Codex CLI or by reading `~/.codex/config.toml`. The Unity Editor window uses the CLI first for the common local cases and falls back to TOML when needed.

## Quick setup

1. Install Codex and confirm the CLI is available:

```bash
codex --version
```

2. Open Unity and start MCP for Unity:
   - `Window > MCP for Unity`
   - Click `Start Server`
   - Select `Codex`
   - Click `Configure`

3. Start a new Codex session and check the server:

```bash
codex mcp list
codex mcp get unityMCP --json
```

For local HTTP, the generated entry should point at:

```text
http://127.0.0.1:8080/mcp
```

`127.0.0.1` is preferred over `localhost` because some Codex HTTP clients resolve `localhost` to IPv6 first, while the local Unity MCP server binds to IPv4.

## Manual local HTTP setup

If the Unity Editor setup is not available, register the server from a terminal:

```bash
codex mcp add unityMCP --url http://127.0.0.1:8080/mcp
codex mcp list
```

To remove it:

```bash
codex mcp remove unityMCP
```

## Manual stdio setup

Use stdio when you want Codex to launch the MCP server process itself:

```bash
codex mcp add unityMCP -- uvx --from mcpforunityserver mcp-for-unity --transport stdio
```

On Windows, if Codex cannot launch the stdio server, add `SystemRoot`:

```bash
codex mcp add unityMCP --env "SystemRoot=C:\Windows" -- uvx --from mcpforunityserver mcp-for-unity --transport stdio
```

If `uvx` is not on Codex's PATH, use the absolute `uvx` path shown in the MCP for Unity window. Quote paths that contain spaces.

## Remote-hosted auth

Codex supports HTTP MCP servers, but the Codex CLI does not currently expose a flag for arbitrary HTTP headers such as `X-API-Key`. For remote-hosted MCP for Unity servers that require this header, configure `~/.codex/config.toml` directly:

```toml
[mcp_servers.unityMCP]
url = "https://your-server.example/mcp"
http_headers = { "X-API-Key" = "your-api-key" }
```

The Unity Editor's `Configure` action writes this TOML form automatically when remote-hosted mode has an API key configured.

## Troubleshooting

If Codex does not show Unity tools:

1. Confirm the MCP for Unity server is running in Unity.
2. Run `codex mcp list` and check that `unityMCP` is enabled.
3. Run `codex mcp get unityMCP --json` and verify the URL or stdio command.
4. Restart the Codex session after changing MCP configuration.
5. If local HTTP fails with `localhost`, use `http://127.0.0.1:8080/mcp`.
