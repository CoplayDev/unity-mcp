---
id: web-research
slug: /guides/web-research
title: Web research alongside Unity
sidebar_label: Web research
description: Add optional keyless web search and page fetching alongside your Unity MCP tools.
---

# Web research alongside Unity

When a Unity task needs current package documentation, migration notes, or
troubleshooting sources, add [Parallel Search MCP](https://docs.parallel.ai/integrations/mcp/search-mcp)
as a separate server in your MCP client. It offers free web search powered by
Fast mode and page fetching without a Parallel account or API key. Anonymous
access is rate-limited.

Unity tools still come from `unityMCP`. Parallel's `web_search` finds sources and
returns excerpts; `web_fetch` reads specific URLs when you need more detail.
For Unity's built-in documentation lookup, see [unity_docs](/reference/tools/docs/unity_docs).

## Add the HTTP server

First [connect your client to Unity](/getting-started/install). In an HTTP-capable
client using `mcpServers`, merge this entry into the existing object, keeping your
`unityMCP` entry and other settings:

```json
{
  "mcpServers": {
    "parallel": {
      "transport": "http",
      "url": "https://search.parallel.ai/mcp",
      "headers": {
        "User-Agent": "unity-mcp-web-research"
      }
    }
  }
}
```

This is the configuration used by the runnable example below. Client configuration
formats vary; use your client's HTTP server settings and preserve the URL and
headers. For example, VS Code uses `servers` instead of `mcpServers` and `type`
instead of `transport`:

```json
{
  "servers": {
    "parallel": {
      "type": "http",
      "url": "https://search.parallel.ai/mcp",
      "headers": {
        "User-Agent": "unity-mcp-web-research"
      }
    }
  }
}
```

Restart or reload your client, approve the server if prompted, and check that
`web_search` and `web_fetch` appear in its tool list. Enable only the tools you
want available. To remove this integration, delete the `parallel` entry.

## Run search and fetch directly

The repository includes a standalone FastMCP client example. It loads `mcp.json`,
discovers the tools, searches for Unity 6's Rigidbody velocity API, and fetches
its reference page. It prints source URLs and excerpts as JSON, makes no Editor
changes, and needs neither a Unity instance nor an LLM connection.

From a clone of this repository, with [uv installed](/guides/uv-setup):

```bash
cd tools/examples/web-research
uv sync
uv run python research.py
```

Customize the search and page to read:

```bash
uv run python research.py \
  --objective "Find Unity 6 Rigidbody velocity API documentation" \
  --query "Unity 6 Rigidbody linearVelocity" \
  --url "https://docs.unity.com/en-us/engine/6000.6/script-reference/unityengine/rigidbody/linearvelocity"
```

Repeat `--query` for related searches or `--url` for additional pages. Each run
reuses one session identifier for its search and fetch calls. This example makes
direct tool calls; your MCP client's model decides how to use the tools in a
conversation.

## Use sources in a Unity task

Ask your connected assistant to search for the API or package version used by
your project, inspect the returned sources, and fetch a relevant page when the
excerpts are insufficient. Then ask it to apply the findings with Unity tools.
Check the source's Unity version before accepting a script change.

Search terms and fetched URLs go to Parallel. Keep private project code and
credentials out of research requests. If anonymous access returns a rate limit,
wait for the indicated retry interval rather than retrying continuously.
