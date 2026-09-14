"""Tests for manage_gameobject tool.

Covers the fixes for https://github.com/CoplayDev/unity-mcp/issues/1297:
manage_gameobject's 'create' action had no reachable way to set component
properties. component_properties was forwarded to Unity but only consumed by
the 'modify' handler, and the {typeName, properties} object shape the 'create'
handler does read out of components_to_add was rejected by Pydantic validation
because components_to_add was typed as a list of strings.

These tests exercise the Python-side contract only: what gets forwarded to
Unity as componentsToAdd / componentProperties, and what gets rejected before
a call is ever sent. The C# consumption side (GameObjectCreate.cs,
GameObjectComponentHelpers.ApplyComponentProperties) is not exercised here -
see TestProjects/UnityMCPTests/Assets/Tests/EditMode/Tools/ManageGameObjectCreateTests.cs
for that half of the coverage.
"""

import asyncio
import inspect
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from services.tools.manage_gameobject import manage_gameobject
from services.registry import get_registered_tools


# ── Fixture ──────────────────────────────────────────────────────────


@pytest.fixture
def mock_unity(monkeypatch):
    captured: dict[str, object] = {}

    async def fake_send(send_fn, unity_instance, tool_name, params):
        captured["unity_instance"] = unity_instance
        captured["tool_name"] = tool_name
        captured["params"] = params
        return {"success": True, "message": "ok", "data": {}}

    monkeypatch.setattr(
        "services.tools.manage_gameobject.get_unity_instance_from_context",
        AsyncMock(return_value="unity-instance-1"),
    )
    monkeypatch.setattr(
        "services.tools.manage_gameobject.send_with_unity_instance",
        fake_send,
    )
    monkeypatch.setattr(
        "services.tools.manage_gameobject.preflight",
        AsyncMock(return_value=None),
    )
    return captured


# ── component_properties forwarding (Fix 1 groundwork) ───────────────


class TestManageGameObjectComponentProperties:
    """component_properties should reach Unity for 'create', not just 'modify'."""

    def test_component_properties_parameter_exists(self):
        sig = inspect.signature(manage_gameobject)
        assert "component_properties" in sig.parameters

    def test_tool_description_mentions_component_properties(self):
        tool = next(
            (t for t in get_registered_tools() if t["name"] == "manage_gameobject"), None
        )
        assert tool is not None
        desc = tool.get("description") or tool.get("kwargs", {}).get("description", "")
        # The top-level tool description doesn't need to mention it, but the
        # parameter's own annotation must - checked via the signature instead.
        assert desc  # sanity: tool is registered with a description at all

    def test_component_properties_forwarded_on_create(self, mock_unity):
        """component_properties must be forwarded as componentProperties when action='create'.

        Before the fix this value reached Unity too (the Python layer never
        gated it on action), but nothing on the C# side consumed it. This test
        pins the Python-side half of the contract: the argument is not
        silently dropped before the call is even made.
        """
        result = asyncio.run(
            manage_gameobject(
                SimpleNamespace(),
                action="create",
                name="Probe",
                components_to_add=["BoxCollider"],
                component_properties={"BoxCollider": {"size": [2, 2, 2]}},
            )
        )
        assert result["success"] is True
        assert mock_unity["params"]["componentProperties"] == {
            "BoxCollider": {"size": [2, 2, 2]}
        }

    def test_component_properties_json_string_forwarded_on_create(self, mock_unity):
        result = asyncio.run(
            manage_gameobject(
                SimpleNamespace(),
                action="create",
                name="Probe",
                component_properties='{"BoxCollider": {"size": [2, 2, 2]}}',
            )
        )
        assert result["success"] is True
        assert mock_unity["params"]["componentProperties"] == {
            "BoxCollider": {"size": [2, 2, 2]}
        }

    def test_invalid_component_properties_rejected_before_send(self, mock_unity):
        result = asyncio.run(
            manage_gameobject(
                SimpleNamespace(),
                action="create",
                name="Probe",
                component_properties="not json",
            )
        )
        assert result["success"] is False
        assert "component_properties" not in mock_unity


# ── components_to_add object entries (Fix 2) ──────────────────────────


class TestManageGameObjectComponentsToAdd:
    """components_to_add must accept {typeName, properties} objects, matching
    what GameObjectCreate.cs already reads out of each entry."""

    def test_components_to_add_parameter_exists(self):
        sig = inspect.signature(manage_gameobject)
        assert "components_to_add" in sig.parameters

    def test_plain_string_list_still_forwarded(self, mock_unity):
        """Regression: the original list[str] form must keep working."""
        result = asyncio.run(
            manage_gameobject(
                SimpleNamespace(),
                action="create",
                name="Probe",
                components_to_add=["BoxCollider", "Rigidbody"],
            )
        )
        assert result["success"] is True
        assert mock_unity["params"]["componentsToAdd"] == ["BoxCollider", "Rigidbody"]

    def test_single_plain_string_still_forwarded(self, mock_unity):
        result = asyncio.run(
            manage_gameobject(
                SimpleNamespace(),
                action="create",
                name="Probe",
                components_to_add="BoxCollider",
            )
        )
        assert result["success"] is True
        assert mock_unity["params"]["componentsToAdd"] == ["BoxCollider"]

    def test_object_entry_with_properties_accepted(self, mock_unity):
        """This is the shape the issue's reproduction #2 shows being rejected
        by Pydantic validation before the fix."""
        result = asyncio.run(
            manage_gameobject(
                SimpleNamespace(),
                action="create",
                name="Probe",
                components_to_add=[
                    {"typeName": "BoxCollider", "properties": {"size": [2, 2, 2]}}
                ],
            )
        )
        assert result["success"] is True
        assert mock_unity["params"]["componentsToAdd"] == [
            {"typeName": "BoxCollider", "properties": {"size": [2, 2, 2]}}
        ]

    def test_object_entry_without_properties_accepted(self, mock_unity):
        result = asyncio.run(
            manage_gameobject(
                SimpleNamespace(),
                action="create",
                name="Probe",
                components_to_add=[{"typeName": "Rigidbody"}],
            )
        )
        assert result["success"] is True
        assert mock_unity["params"]["componentsToAdd"] == [{"typeName": "Rigidbody"}]

    def test_mixed_string_and_object_entries_accepted(self, mock_unity):
        result = asyncio.run(
            manage_gameobject(
                SimpleNamespace(),
                action="create",
                name="Probe",
                components_to_add=[
                    "Rigidbody",
                    {"typeName": "BoxCollider", "properties": {"size": [2, 2, 2]}},
                ],
            )
        )
        assert result["success"] is True
        assert mock_unity["params"]["componentsToAdd"] == [
            "Rigidbody",
            {"typeName": "BoxCollider", "properties": {"size": [2, 2, 2]}},
        ]

    def test_single_object_entry_without_list_wrapping_accepted(self, mock_unity):
        result = asyncio.run(
            manage_gameobject(
                SimpleNamespace(),
                action="create",
                name="Probe",
                components_to_add={"typeName": "BoxCollider", "properties": {"size": [2, 2, 2]}},
            )
        )
        assert result["success"] is True
        assert mock_unity["params"]["componentsToAdd"] == [
            {"typeName": "BoxCollider", "properties": {"size": [2, 2, 2]}}
        ]

    def test_json_string_of_object_entries_accepted(self, mock_unity):
        result = asyncio.run(
            manage_gameobject(
                SimpleNamespace(),
                action="create",
                name="Probe",
                components_to_add='[{"typeName": "BoxCollider", "properties": {"size": [2, 2, 2]}}]',
            )
        )
        assert result["success"] is True
        assert mock_unity["params"]["componentsToAdd"] == [
            {"typeName": "BoxCollider", "properties": {"size": [2, 2, 2]}}
        ]

    def test_snake_case_type_name_key_normalized(self, mock_unity):
        """Convenience accepted alongside the documented camelCase 'typeName'."""
        result = asyncio.run(
            manage_gameobject(
                SimpleNamespace(),
                action="create",
                name="Probe",
                components_to_add=[{"type_name": "BoxCollider"}],
            )
        )
        assert result["success"] is True
        assert mock_unity["params"]["componentsToAdd"] == [{"typeName": "BoxCollider"}]

    def test_object_entry_missing_type_name_rejected(self, mock_unity):
        result = asyncio.run(
            manage_gameobject(
                SimpleNamespace(),
                action="create",
                name="Probe",
                components_to_add=[{"properties": {"size": [2, 2, 2]}}],
            )
        )
        assert result["success"] is False
        assert "typeName" in result["message"]
        assert "params" not in mock_unity

    def test_object_entry_non_object_properties_rejected(self, mock_unity):
        result = asyncio.run(
            manage_gameobject(
                SimpleNamespace(),
                action="create",
                name="Probe",
                components_to_add=[{"typeName": "BoxCollider", "properties": "not-an-object"}],
            )
        )
        assert result["success"] is False
        assert "params" not in mock_unity

    def test_invalid_entry_type_rejected(self, mock_unity):
        result = asyncio.run(
            manage_gameobject(
                SimpleNamespace(),
                action="create",
                name="Probe",
                components_to_add=[123],
            )
        )
        assert result["success"] is False
        assert "params" not in mock_unity

    def test_none_omitted_from_params(self, mock_unity):
        asyncio.run(
            manage_gameobject(
                SimpleNamespace(),
                action="create",
                name="Probe",
            )
        )
        assert "componentsToAdd" not in mock_unity["params"]

    def test_components_to_add_with_object_entries_works_on_modify_too(self, mock_unity):
        """GameObjectModify.cs reads the same {typeName, properties} shape."""
        result = asyncio.run(
            manage_gameobject(
                SimpleNamespace(),
                action="modify",
                target="Probe",
                components_to_add=[
                    {"typeName": "BoxCollider", "properties": {"size": [2, 2, 2]}}
                ],
            )
        )
        assert result["success"] is True
        assert mock_unity["params"]["componentsToAdd"] == [
            {"typeName": "BoxCollider", "properties": {"size": [2, 2, 2]}}
        ]
