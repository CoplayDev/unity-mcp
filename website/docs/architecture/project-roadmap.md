---
id: project-roadmap
slug: /architecture/project-roadmap
title: Project Roadmap
sidebar_label: Project Roadmap
description: Living document — goals, current focus, mid-term and long-term plans for MCP for Unity. Maintained alongside the GitHub wiki.
---

# Project Roadmap

A living document outlining MCP for Unity's high-level goals, priorities, and planned features. It evolves with community feedback, technical discoveries, and shifting priorities.

For the deep-research **2026 Feature Roadmap** (per-tool API coverage analysis), see [Feature Roadmap 2026](/architecture/roadmap).

**Want to contribute or discuss?** See [How to Contribute or Provide Feedback](#how-to-contribute-or-provide-feedback).

## Overall Vision & Goals

The primary goal is to provide a robust, flexible, and easy-to-use bridge between the Unity Editor and external MCP clients (Claude Desktop, Cursor, and beyond).

Key objectives:

1. **Make onboarding effortless** — setting up the MCP server is a known pain point for the ecosystem and this project; we'll make it easier to get started.
2. **Improve speed & efficiency** — reduce latency and token usage for faster workflows.
3. **Expand integrations** — more MCP clients, better MCP server discovery, improved auth.
4. **Improve developer experience** — clean APIs, clearer docs, maintainable architecture.
5. **Align with real user needs** — prioritize community- and customer-driven improvements from feedback.

## Current Focus

Legend: **Feat** = feature, **Fix** = bug fix, **Imp** = improvement, **Doc** = docs, **Test** = tests, **Tech** = tech debt / refactor, **Arch** = architecture.

- **Doc** — Documentation overhaul to improve clarity, onboarding, and discoverability
- **Imp** — Per-call instance routing — target specific Unity instances on any tool call ([#772](https://github.com/CoplayDev/unity-mcp/pull/772))
- **Fix** — Code Coverage dependency guard for fresh installs ([#540](https://github.com/CoplayDev/unity-mcp/issues/540))

## Mid-Term Plans

Items aimed for further out. Details and priorities are less defined.

- **Feat** — Explore Runtime MCP Operation ([Discussion #781](https://github.com/CoplayDev/unity-mcp/discussions/781))
- **Feat** — Explore adding GenAI plugins for 2D and 3D assets ([Discussion #778](https://github.com/CoplayDev/unity-mcp/discussions/778))
- **Tech** — Re-evaluate script editing capabilities and consolidate

## Long-Term Ideas & Future Directions

Bigger ideas or major features further down the line (>6–9 months) or requiring significant research / design.

- **Arch** — Dependency Injection to improve testability
- **Feat** — Add more play mode functionality — support MCP during runtime with custom tools would let LLMs interact with user-created games / experiences

## Maybe / Icebox / Backlog

Suggested or considered but not currently planned. May be revisited later.

- Visual scripting integration (e.g., Bolt / PlayMaker)
- **Test** — Test coverage for Tools, networking, and ideally end-to-end
- **Imp** — Docker support for running the MCP server ([Discussion #776](https://github.com/CoplayDev/unity-mcp/discussions/776))
- **Imp** — Tool search / tool filtering to reduce context ([Discussion #777](https://github.com/CoplayDev/unity-mcp/discussions/777))

## Recently Completed

- **Feat** — Per-call `unity_instance` routing via tool arguments ([#772](https://github.com/CoplayDev/unity-mcp/pull/772)) — supersedes CLI flags approach
- **Feat** — HTTP Server Authentication ([#433](https://github.com/CoplayDev/unity-mcp/issues/433))
- **Feat** — Flag to make custom tools available globally or by project ([#416](https://github.com/CoplayDev/unity-mcp/issues/416))
- **Fix** — High resource costs when not in use ([#577](https://github.com/CoplayDev/unity-mcp/issues/577))
- **Imp** — Use MCP for Unity via the CLI ([#544](https://github.com/CoplayDev/unity-mcp/pull/544))
- **Imp** — OpenCode Support ([#498](https://github.com/CoplayDev/unity-mcp/pull/498))

For details on past releases, see [Release Notes](/releases) or the [GitHub Releases page](https://github.com/CoplayDev/unity-mcp/releases).

## How to Contribute or Provide Feedback

1. **Discuss ideas:** use [GitHub Discussions](https://github.com/CoplayDev/unity-mcp/discussions) to discuss roadmap items or propose new ones.
2. **Request features:** [open a new issue](https://github.com/CoplayDev/unity-mcp/issues/new) using the Feature Request template. **Check existing issues first.**
3. **Report bugs:** [open a bug report](https://github.com/CoplayDev/unity-mcp/issues/new). Provide clear steps to reproduce.
4. **Contribute code / docs:** see [CONTRIBUTING.md](https://github.com/CoplayDev/unity-mcp/blob/beta/CONTRIBUTING.md). Look for issues tagged `help wanted` or `good first issue`. Review open [Pull Requests](https://github.com/CoplayDev/unity-mcp/pulls).
5. **Comment on issues / PRs:** provide feedback directly on the issues and PRs linked above.

## Disclaimer

This roadmap is a high-level overview of potential future direction. It is not a commitment or guarantee. Priorities and timelines may change based on community feedback, resource availability, technical challenges, and strategic shifts. Refer to specific GitHub Issues and Milestones for the most granular, up-to-date status.
