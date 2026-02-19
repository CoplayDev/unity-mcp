# MCP for Unity - Project Roadmap

Welcome to the **MCP for Unity** roadmap! This document outlines our high-level goals, priorities, and planned features. It is a living document and will evolve with community feedback, technical discoveries, and shifting priorities.

**Want to contribute or discuss the roadmap?** We'd love your input! Please see the [How to Contribute or Provide Feedback](#how-to-contribute-or-provide-feedback) section below.

---

## Table of Contents

* [Overall Vision & Goals](#overall-vision--goals)
* [Current Focus / Next Release](#current-focus--next-release)
* [Short-Term Plans](#short-term-plans-post-installation-overhaul)
* [Mid-Term Plans](#mid-term-plans)
* [Long-Term Ideas & Future Directions](#long-term-ideas--future-directions)
* [Maybe / Icebox / Backlog](#maybe--icebox--backlog)
* [Recently Completed](#recently-completed)
* [How to Contribute or Provide Feedback](#how-to-contribute-or-provide-feedback)
* [Disclaimer](#disclaimer)

---

## Overall Vision & Goals

Our primary goal for **MCP for Unity** is to provide a robust, flexible, and easy-to-use bridge and MCP server enabling powerful interactions between the Unity Editor and other MCP clients.

Key objectives driving our development include:

Our primary goal for MCP for Unity is to provide a robust, flexible, and easy-to-use bridge between the Unity Editor and external MCP clients (Claude Desktop, Cursor, and beyond).

Key objectives:

* Goal 1: **Make onboarding effortless** - Setting up the MCP server is a known pain point for the ecosystem and this project, we're going to make it easier to get started
* Goal 2: **Improve speed & efficiency** - Reduce latency and token usage for faster workflows
* Goal 3: **Expand integrations** - More MCP clients, better MCP server discovery, improved auth
* Goal 4: **Improve developer experience** - Clean APIs, clearer docs, and maintainable architecture
* Goal 5: **Align with real user needs** - Prioritize community and customer-driven improvements from feedback

---

## Current Focus / Next Release (v10)

```
Legend: ✨ Feature, 🐛 Bug Fix, 📈 Improvement, 📄 Docs, 🧪 Tests, 🧹 Tech Debt/Refactoring, 🏗️ Architecture
```

v10 is focused on usability, trust, and speed. Our main goal is to ensure the MCP server is more performant i.e. help AI tools choose the right function tools, and call them in the right way. We also want to improve the security of the HTTP server for remote usage.

**Target:** Aiming for completion in **early February 2026**

Key items being actively worked on or planned for this cycle:

* 🧹 Documentation overhaul to improve clarity, onboarding, and discoverability
* ✨ [COMMUNITY] HTTP Server Authentication [#312](https://github.com/CoplayDev/unity-mcp/issues/433)
* ✨ Flag to make custom tools available globally or by project [#416](https://github.com/CoplayDev/unity-mcp/issues/416)
* 🐛 Fix high resource costs when not in use [#577](https://github.com/CoplayDev/unity-mcp/issues/577)
* 📈 Use MCP for Unity via the CLI [#544](https://github.com/CoplayDev/unity-mcp/pull/544)

---

## Short-Term Plans

* 📈 OpenCode Support [#498](https://github.com/CoplayDev/unity-mcp/pull/498)
* 🧪 Consider using OpenCode for evaluations

---

## Mid-Term Plans

These are items we aim to tackle further out. Details and priorities are less defined.

* ✨ Explore Runtime MCP Operation ([#58](https://github.com/CoplayDev/unity-mcp/issues/58)).
* ✨ Explore adding GenAI plugins for 2D and 3D assets
* 🧹 Re-evaluate script editing capabilities and consolidate

---

## Long-Term Ideas & Future Directions

This section captures bigger ideas or major features that are further down the line (>6-9 months) or require significant research/design.

* 🏗️ Dependency Injection to improve testability
* ✨ Add more play mode functionality - support MCP during runtime with custom tools would allow for LLMs to interact with user created games/experiences

---

## Maybe / Icebox / Backlog

These are ideas that have been suggested or considered but are not currently planned for active development. They might be revisited later.

* Visual scripting integration (e.g., Bolt/PlayMaker).
* 🧪 Test coverage for Tools, networking and ideally end-to-end

---

## Recently Completed

For details on past releases and completed work, please see:

* **[Releases Page](https://github.com/CoplayDev/unity-mcp/releases)**

---

## How to Contribute or Provide Feedback

Your feedback and contributions are crucial! Here's how you can get involved:

1. **Discuss Ideas:** Use **[GitHub Discussions](https://github.com/CoplayDev/unity-mcp/discussions)** or open an issue with the `enhancement` or `discussion` label to discuss roadmap items or propose new ideas.
2. **Request Features:** For specific, well-defined feature requests, please **[open a new issue](https://github.com/CoplayDev/unity-mcp/issues/new)** using the "Feature Request" template. **Check existing issues first!**
3. **Report Bugs:** If you find a bug, please **[open a bug report](https://github.com/CoplayDev/unity-mcp/issues/new)**. Provide clear steps to reproduce.
4. **Contribute Code/Docs:** Check our **[README.md](https://github.com/CoplayDev/unity-mcp/blob/master/README.md#Contributing)** guide for details. Look for issues tagged `help wanted` or `good first issue`. Review open **[Pull Requests](https://github.com/CoplayDev/unity-mcp/pulls)**.
5. **Comment on Issues/PRs:** Provide feedback directly on the issues and pull requests linked in the roadmap sections above.

---

## Disclaimer

This roadmap provides a high-level overview of potential future direction for **MCP for Unity**. It is not a commitment or guarantee. Priorities and timelines may change significantly based on various factors including (but not limited to) community feedback, resource availability, technical challenges, and strategic shifts. We will strive to keep this document updated but please refer to specific GitHub Issues and Milestones for the most granular, up-to-date status.

---