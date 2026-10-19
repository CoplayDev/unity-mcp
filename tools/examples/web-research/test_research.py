"""Exercise the configuration loader and HTTP MCP requests without network access."""

import asyncio
import json
from pathlib import Path
from typing import Any
from uuid import UUID

import httpx
import pytest

from research import research


@pytest.mark.parametrize("failure", [None, "missing_tool", "tool_error", "empty_fetch"])
def test_configured_http_requests(
    monkeypatch: pytest.MonkeyPatch, failure: str | None
) -> None:
    config_path = Path(__file__).with_name("mcp.json")
    original = config_path.read_bytes()
    requests = []

    async def send(
        client: httpx.AsyncClient, request: httpx.Request, **kwargs: Any
    ) -> httpx.Response:
        requests.append(request)
        assert str(request.url) == "https://search.parallel.ai/mcp"
        assert request.headers["User-Agent"] == "unity-mcp-web-research"
        assert "Authorization" not in request.headers
        if request.method != "POST":
            return httpx.Response(405, request=request)
        body = json.loads(request.content)
        if "id" not in body:
            return httpx.Response(202, request=request)
        method = body["method"]
        if method == "initialize":
            result = {
                "protocolVersion": body["params"]["protocolVersion"],
                "capabilities": {"tools": {}},
                "serverInfo": {"name": "research-fixture", "version": "1"},
            }
        elif method == "tools/list":
            names = (
                ["web_search"]
                if failure == "missing_tool"
                else ["web_search", "web_fetch"]
            )
            result = {
                "tools": [
                    {"name": name, "inputSchema": {"type": "object"}} for name in names
                ]
            }
        elif method == "tools/call":
            result = {
                "content": [
                    {
                        "type": "text",
                        "text": "fixture failure"
                        if failure == "tool_error"
                        else json.dumps(
                            {
                                "results": []
                                if failure == "empty_fetch"
                                and body["params"]["name"] == "web_fetch"
                                else [
                                    {
                                        "url": "https://docs.unity3d.com/",
                                        "excerpts": ["Unity API excerpt"],
                                    }
                                ]
                            }
                        ),
                    }
                ],
                "isError": failure == "tool_error",
            }
        else:
            raise AssertionError(f"Unexpected MCP method: {method}")
        return httpx.Response(
            200,
            json={"jsonrpc": "2.0", "id": body["id"], "result": result},
            request=request,
        )

    monkeypatch.setattr(httpx.AsyncClient, "send", send)
    run = research(
        config_path,
        "Find Unity API",
        ["Unity 6 Rigidbody velocity"],
        ["https://docs.unity3d.com/"],
    )
    if failure:
        with pytest.raises(
            Exception,
            match={
                "missing_tool": "must expose",
                "tool_error": "fixture failure",
                "empty_fetch": "fetch returned no results",
            }[failure],
        ):
            asyncio.run(run)
    else:
        result = asyncio.run(run)
        assert json.loads(result["search"][0])["results"][0]["excerpts"] == [
            "Unity API excerpt"
        ]
        assert result["search"] == result["fetch"]
        calls = [
            json.loads(r.content)["params"]
            for r in requests
            if r.method == "POST"
            and json.loads(r.content).get("method") == "tools/call"
        ]
        assert [call["name"] for call in calls] == ["web_search", "web_fetch"]
        search, fetch = [call["arguments"] for call in calls]
        UUID(search["session_id"])
        assert search["session_id"] == fetch["session_id"]
        assert search["objective"] == "Find Unity API"
        assert (
            search["search_queries"]
            == fetch["search_queries"]
            == ["Unity 6 Rigidbody velocity"]
        )
        assert fetch["urls"] == ["https://docs.unity3d.com/"]
    assert config_path.read_bytes() == original
