# Security Policy

## Reporting a Vulnerability

**Please do not report security vulnerabilities through public GitHub issues.**

Instead, email **security@coplay.dev** with:

- A clear description of the issue
- Steps to reproduce or a proof-of-concept
- The version of MCP for Unity affected (UPM package + Python server)
- Your OS, Unity Editor version, and MCP client
- Optional: a suggested fix

We aim to acknowledge reports within **3 business days** and to share an initial assessment within **10 business days**. Critical fixes are released as patch versions on both `main` and the beta channel.

## Supported Versions

| Version | Supported |
|---------|-----------|
| latest (`main`) | Yes |
| latest beta (`beta`) | Yes |
| older releases | No — please upgrade |

## Network Defaults (Safe by Default)

MCP for Unity is intentionally fail-closed:

- **HTTP Local** binds to loopback only by default (`127.0.0.1`, `localhost`, `::1`). LAN bind (`0.0.0.0`, `::`) requires explicit opt-in via **Allow LAN Bind (HTTP Local)** in Advanced Settings.
- **HTTP Remote** requires `https://` by default. Plaintext `http://` for remote endpoints requires explicit opt-in via **Allow Insecure Remote HTTP**.
- Remote-hosted mode requires API key authentication. See [Remote Server Auth](https://coplaydev.github.io/unity-mcp/guides/remote-server-auth).

If you find a way to bypass any of these guards, that qualifies as a security vulnerability and warrants a private report.

## What Counts as a Security Issue

- Remote code execution via crafted MCP messages
- Auth bypass on remote-hosted server
- Filesystem read/write outside the intended Unity project root
- Network requests that escape the configured allow-list
- Credential or API-key leakage in logs, telemetry, or error responses

## What Doesn't Count

- Tool actions that intentionally modify the Unity project (that's the product)
- Issues that require an attacker to already have shell access to the host
- Vulnerabilities in third-party dependencies — please report those upstream first; we'll bump our pins after the upstream fix lands

## Disclosure Timeline

Once a fix is shipped, we publish a security advisory on the GitHub Security tab and credit the reporter (unless they prefer anonymity).
