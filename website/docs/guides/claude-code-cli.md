---
id: claude-code-cli
slug: /guides/claude-code-cli
title: Install or Repair Claude Code CLI
sidebar_label: Claude Code CLI
description: Install or repair the Claude Code CLI (claude) so MCP for Unity can launch it.
---

# Install or Repair Claude Code CLI

You need the Claude Code CLI (`claude`) available on your system.

:::caution Switching transport requires a restart
If you change from `http` to `stdio` (or vice versa) in the MCP for Unity window, **restart Claude Code** for it to pick up the change.
:::

## Recommended (native installers)

**macOS / Linux / WSL:**

```bash
curl -fsSL https://claude.ai/install.sh | bash
claude doctor
```

**Windows PowerShell:**

```powershell
irm https://claude.ai/install.ps1 | iex
claude doctor
```

## Alternative: npm via NVM (installs under `~/.nvm`)

```bash
# Install / select a Node version
nvm install v21.7.1
nvm use v21.7.1

# Install Claude Code CLI into this Node's global prefix
npm install -g @anthropic-ai/claude-code

# Verify it's under NVM
which claude
claude --version
```

## Alternative: npm with system / Homebrew Node

```bash
# If you don't have Node yet (macOS):
brew install node

# Install Claude Code CLI globally
npm install -g @anthropic-ai/claude-code

# Verify it's on PATH (typical: /opt/homebrew/bin/claude or /usr/local/bin/claude)
which claude
claude --version
```

## macOS PATH gotcha

On macOS, Unity launched from Finder / Hub may not inherit your shell PATH. If `claude` isn't found:

- **Either** launch Hub from Terminal (so PATH propagates),
- **or** use the MCP for Unity window's **"Choose Claude Install Location"** to set the absolute path.

## Related troubleshooting

- macOS dyld ICU library errors: see [Common Setup Problems → macOS Claude CLI dyld error](/guides/troubleshooting#macos-claude-cli-fails-to-start-dyld-icu-library-not-loaded)
- "Claude Not Found" in the Register button: see the FAQ in [Common Setup Problems](/guides/troubleshooting#faq--claude-code)
