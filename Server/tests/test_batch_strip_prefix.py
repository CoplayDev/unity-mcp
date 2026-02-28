"""Tests for strip_mcp_prefix in batch_execute."""

import pytest
from services.tools.batch_execute import strip_mcp_prefix


@pytest.mark.parametrize(
    "input_name,expected",
    [
        # Plain names pass through unchanged
        ("manage_gameobject", "manage_gameobject"),
        ("batch_execute", "batch_execute"),
        # Colon format (e.g. Claude Desktop)
        ("UnityMCP:manage_gameobject", "manage_gameobject"),
        ("unityMCP:batch_execute", "batch_execute"),
        # Double-underscore format (e.g. Claude Code)
        ("mcp__UnityMCP__manage_gameobject", "manage_gameobject"),
        ("mcp__UnityMCP__batch_execute", "batch_execute"),
        ("mcp__UnityMCP__manage_scene", "manage_scene"),
        # Tool name itself contains underscores — should be preserved
        ("mcp__UnityMCP__manage_scriptable_object", "manage_scriptable_object"),
        # Edge cases: trailing separators pass through unchanged (match C# behavior)
        (":", ":"),
        ("Server:", "Server:"),
        ("mcp__Server__", "mcp__Server__"),
        # Empty string passes through
        ("", ""),
        # Multiple colons: last segment used
        ("a:b:manage_gameobject", "manage_gameobject"),
    ],
)
def test_strip_mcp_prefix(input_name: str, expected: str):
    assert strip_mcp_prefix(input_name) == expected
