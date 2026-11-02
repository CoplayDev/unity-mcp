import asyncio
from unittest.mock import AsyncMock

import pytest
from pydantic import TypeAdapter, ValidationError
from typing import get_type_hints

import services.tools.manage_asset as module
from .test_helpers import DummyContext


@pytest.mark.parametrize("action", ["list_asset_bundles", "get_asset_bundle_info"])
def test_bundle_actions_forward_paging_without_refresh_or_search_normalization(monkeypatch, action):
    captured = {}

    async def send(command, params, **kwargs):
        captured.update(command=command, params=params)
        return {"success": True, "data": {"entries": []}}

    gate = AsyncMock(return_value=None)
    monkeypatch.setattr(module, "preflight", gate)
    monkeypatch.setattr(module, "async_send_command_with_retry", send)
    response = asyncio.run(module.manage_asset(
        DummyContext(), action, "Builds/AssetBundles/characters",
        page_size="2", page_number="3",
    ))

    assert response["success"]
    assert captured["command"] == "manage_asset"
    assert captured["params"]["action"] == action
    assert captured["params"]["path"] == "Builds/AssetBundles/characters"
    assert captured["params"]["pageSize"] == 2
    assert captured["params"]["pageNumber"] == 3
    assert gate.await_args.kwargs == {"wait_for_no_compile": True, "refresh_if_dirty": False}


def test_bundle_actions_are_exposed_by_the_declared_schema():
    # The repository's test fixtures stub FastMCP; validate the actual annotation.
    action = TypeAdapter(get_type_hints(module.manage_asset, include_extras=True)["action"])
    assert action.validate_python("list_asset_bundles") == "list_asset_bundles"
    assert action.validate_python("get_asset_bundle_info") == "get_asset_bundle_info"
    with pytest.raises(ValidationError):
        action.validate_python("build_asset_bundles")


def test_bundle_actions_preserve_preflight_errors(monkeypatch):
    from models import MCPResponse
    gate = MCPResponse(success=False, error="compiling")
    monkeypatch.setattr(module, "preflight", AsyncMock(return_value=gate))
    send = AsyncMock()
    monkeypatch.setattr(module, "send_with_unity_instance", send)

    response = asyncio.run(module.manage_asset(DummyContext(), "list_asset_bundles", "Builds"))

    assert response["success"] is False
    send.assert_not_awaited()
