"""manifest.json (the MCP bundle manifest) must list every registered tool, and only those.

The six asset_gen tools shipped without entries because nothing compared the two lists.
"""
import json
from pathlib import Path

import services.tools as tools_package
from services.registry import get_registered_tools
from utils.module_discovery import discover_modules


MANIFEST = Path(__file__).resolve().parents[2] / "manifest.json"


def test_manifest_lists_exactly_the_registered_tools():
    # Import every tool module so its @mcp_for_unity_tool decorator runs (see test_tool_annotations).
    list(discover_modules(Path(tools_package.__file__).parent, tools_package.__package__))
    registered = {tool["name"] for tool in get_registered_tools()}
    listed = [tool["name"] for tool in json.loads(MANIFEST.read_text(encoding="utf-8"))["tools"]]

    assert len(listed) == len(set(listed)), f"manifest.json lists a tool twice: {sorted(listed)}"
    assert sorted(registered - set(listed)) == [], "registered tools that manifest.json does not list"
    assert sorted(set(listed) - registered) == [], "manifest.json lists tools that are not registered"
