# Pinned MCP Server Version (hardening, R11)

## What is pinned

The local launch path starts the Python MCP server with `uvx`, which fetches the
package from PyPI at launch time. To ensure **the code that runs is the code that
was audited**, this fork pins the server to an exact PyPI version rather than a
floating `latest` / open prerelease range.

| Item | Value |
|------|-------|
| Package | `mcpforunityserver` |
| Pinned version | **`9.7.3`** |
| Source of truth | `MCPForUnity/Editor/Helpers/AssetPathUtility.cs` → `PinnedServerVersion` |
| Mirrors | `Server/pyproject.toml` `version` |

`AssetPathUtility.GetMcpServerPackageSource()` returns `mcpforunityserver==9.7.3`
when no explicit Server Source Override is set, so the launch command is
`uvx --from mcpforunityserver==9.7.3 mcp-for-unity ...` — an exact pin with no
floating resolution.

## Why

The previous logic derived the spec from `package.json`:

- unknown version → `mcpforunityserver` (floating *latest*)
- prerelease version → `mcpforunityserver>=0.0.0a0` (floating prerelease *range*)

Either form means a compromised or simply newer PyPI release would run on the
next launch without review. Pinning removes that drift.

The companion supply-chain fix (R1) disabled the Roslyn DLL auto-installer, which
fetched unverified DLLs from NuGet. If that ever returns, add hash/signature
verification before load.

## How to re-pin (deliberately)

1. Review the target server version's source.
2. Update `Server/pyproject.toml` `version` (if releasing) and
   `AssetPathUtility.PinnedServerVersion` to the exact PyPI version.
3. Update the table above.
4. Commit as a `harden(R11): ...` change so the bump is auditable.

> A local **Server Source Override** (Advanced Settings) still takes precedence
> over the pin — use it to point at a reviewed local checkout during development.
