---
id: http-client-sessions
slug: /guides/http-client-sessions
title: HTTP Client Sessions
sidebar_label: HTTP Client Sessions
description: Initialize a Streamable HTTP session and reuse its headers with curl or other HTTP clients.
---

# HTTP Client Sessions

A local server in HTTP mode accepts MCP JSON-RPC requests at `/mcp`. Its
initialization response includes `MCP-Session-Id`. Save that value and send it
with every later request, together with the negotiated `MCP-Protocol-Version`.
HTTP connections can close between requests; the session state remains on the server.

The [Streamable HTTP specification](https://modelcontextprotocol.io/specification/2025-11-25/basic/transports#session-management)
defines these headers. After initialization, send
[`notifications/initialized`](https://modelcontextprotocol.io/specification/2025-11-25/basic/lifecycle#initialization)
without an `id`, then make tool or resource requests.

## Try it with curl

Start the server in HTTP mode first. This Bash example needs curl, awk, and
Python 3; use Bash on macOS/Linux or Git Bash/WSL on Windows. Set `MCP_URL` if your
server uses a different port. Each curl invocation opens a separate connection.
The example accepts protocol `2025-11-25` and stops if a different version is negotiated.

```bash
set -eu
MCP_URL=${MCP_URL:-http://127.0.0.1:8080/mcp}
headers_file=$(mktemp)
reply_file=$(mktemp)
trap 'rm -f "$headers_file" "$reply_file"' EXIT

curl --fail --silent --show-error --max-time 15 \
  --dump-header "$headers_file" --output "$reply_file" \
  --header 'Content-Type: application/json' \
  --header 'Accept: application/json, text/event-stream' \
  --data '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"curl-example","version":"1.0.0"}}}' \
  "$MCP_URL"

session_id=$(awk 'tolower($1)=="mcp-session-id:" {gsub("\r", "", $2); print $2}' "$headers_file")
[ -n "$session_id" ] || { echo 'No MCP-Session-Id returned' >&2; exit 1; }
protocol_version=$(python3 - "$reply_file" <<'PYTHON'
import json, sys
from pathlib import Path
text = Path(sys.argv[1]).read_text(encoding="utf-8")
try:
    reply = json.loads(text)
except json.JSONDecodeError:
    reply = next(json.loads(line[5:].strip()) for line in text.splitlines()
                 if line.startswith("data:") and line[5:].strip())
print(reply["result"]["protocolVersion"])
PYTHON
)
[ "$protocol_version" = '2025-11-25' ] || { echo 'Unsupported negotiated protocol version' >&2; exit 1; }

mcp_post() {
  curl --fail --silent --show-error --max-time 15 \
    --header 'Content-Type: application/json' \
    --header 'Accept: application/json, text/event-stream' \
    --header "MCP-Session-Id: $session_id" \
    --header "MCP-Protocol-Version: $protocol_version" \
    --data "$1" "$MCP_URL"
}

mcp_post '{"jsonrpc":"2.0","method":"notifications/initialized"}'
mcp_post '{"jsonrpc":"2.0","id":2,"method":"tools/list"}'
mcp_post '{"jsonrpc":"2.0","id":3,"method":"resources/read","params":{"uri":"mcpforunity://instances"}}'

curl --fail --silent --show-error --max-time 15 --request DELETE \
  --header "MCP-Session-Id: $session_id" \
  --header "MCP-Protocol-Version: $protocol_version" "$MCP_URL"
```

The initialized notification returns HTTP `202` with an empty body. Request
responses can contain JSON or SSE messages (`data: {...}`); parse the response's
content type. These calls do not need a separate, long-lived GET/SSE connection.
An empty instance list means the HTTP session works but no Unity Editor is connected.
For tool calls, use the names and argument schemas returned by `tools/list` and
choose an Editor as described in [Multi-Instance Routing](/guides/multi-instance).

## Recover from session errors

- **400, Missing session ID:** include the returned `MCP-Session-Id` on the notification and every later request.
- **400, unsupported protocol version:** send the version negotiated in `InitializeResult`.
- **404, session no longer exists:** initialize again without the old session header, then send a new initialized notification.

## Local commands without an MCP session

The [CLI](/guides/cli) sends local Editor commands through `/api/command` without
an MCP session. Those CLI routes are disabled in
[remote-hosted mode](/guides/remote-server-auth).
