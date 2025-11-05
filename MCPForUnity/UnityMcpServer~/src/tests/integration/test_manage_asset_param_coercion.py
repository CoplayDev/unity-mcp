import asyncio

from .test_helpers import DummyContext
import tools.manage_asset as manage_asset_mod


def test_manage_asset_pagination_coercion(monkeypatch):
    captured = {}

    async def fake_async_send(cmd, params, **kwargs):
        captured["params"] = params
        return {"success": True, "data": {}}

    monkeypatch.setattr(manage_asset_mod, "async_send_command_with_retry", fake_async_send)

    result = asyncio.run(
        manage_asset_mod.manage_asset(
            ctx=DummyContext(),
            action="search",
            path="Assets",
            page_size="50",
            page_number="2",
        )
    )

    assert result == {"success": True, "data": {}}
    assert captured["params"]["pageSize"] == 50
    assert captured["params"]["pageNumber"] == 2






