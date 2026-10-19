"""Search Unity documentation and fetch a page using an anonymous MCP connection."""

import argparse
import asyncio
import json
from pathlib import Path
from uuid import uuid4

from fastmcp import Client


async def research(
    config_path: Path, objective: str, queries: list[str], urls: list[str]
) -> dict[str, list[str]]:
    config = json.loads(config_path.read_text(encoding="utf-8"))
    session_id = str(uuid4())
    async with Client(config, timeout=60, init_timeout=30) as client:
        available = {tool.name for tool in await client.list_tools()}
        if not {"web_search", "web_fetch"} <= available:
            raise RuntimeError(
                "The configured server must expose web_search and web_fetch"
            )
        search = await client.call_tool(
            "web_search",
            {
                "objective": objective,
                "search_queries": queries,
                "session_id": session_id,
            },
        )
        fetch = await client.call_tool(
            "web_fetch",
            {"urls": urls, "search_queries": queries, "session_id": session_id},
        )
        output = {
            "search": [item.text for item in search.content if item.type == "text"],
            "fetch": [item.text for item in fetch.content if item.type == "text"],
        }
        for name, blocks in output.items():
            if not any(json.loads(block).get("results") for block in blocks):
                raise RuntimeError(f"{name} returned no results: {blocks}")
        return output


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--config", type=Path, default=Path(__file__).with_name("mcp.json")
    )
    parser.add_argument(
        "--objective", default="Find Unity 6 Rigidbody velocity API documentation"
    )
    parser.add_argument(
        "--query", action="append", help="Repeat for related keyword queries"
    )
    parser.add_argument("--url", action="append", help="Repeat for pages to fetch")
    args = parser.parse_args()
    result = asyncio.run(
        research(
            args.config,
            args.objective,
            args.query or ["Unity 6 Rigidbody linearVelocity"],
            args.url
            or [
                "https://docs.unity.com/en-us/engine/6000.6/script-reference/unityengine/rigidbody/linearvelocity"
            ],
        )
    )
    print(json.dumps(result, indent=2, ensure_ascii=False))


if __name__ == "__main__":
    main()
