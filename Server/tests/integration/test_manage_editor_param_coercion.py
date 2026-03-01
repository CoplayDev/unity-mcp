import asyncio

from .test_helpers import DummyContext
import services.tools.manage_editor as manage_editor_mod


class NotifyContext(DummyContext):
    def __init__(self, **meta):
        super().__init__(**meta)
        self.tool_list_changed_calls = 0

    async def send_tool_list_changed(self):
        self.tool_list_changed_calls += 1


def test_manage_editor_set_mcp_tool_enabled_string_coercion(monkeypatch):
    captured = {}

    async def fake_send(_func, _instance, _tool_name, params, **_kwargs):
        captured["params"] = params
        return {"success": True, "message": "ok"}

    monkeypatch.setattr(manage_editor_mod, "send_with_unity_instance", fake_send)

    result = asyncio.run(
        manage_editor_mod.manage_editor(
            ctx=DummyContext(),
            action="set_mcp_tool_enabled",
            tool_name="manage_scene",
            enabled="false",
        )
    )

    assert result["success"] is True
    assert captured["params"]["enabled"] is False


def test_manage_editor_set_mcp_tool_enabled_invalid_boolean(monkeypatch):
    async def fake_send(_func, _instance, _tool_name, params, **_kwargs):
        return {"success": True, "message": "ok"}

    monkeypatch.setattr(manage_editor_mod, "send_with_unity_instance", fake_send)

    result = asyncio.run(
        manage_editor_mod.manage_editor(
            ctx=DummyContext(),
            action="set_mcp_tool_enabled",
            tool_name="manage_scene",
            enabled="invalid-bool",
        )
    )

    assert result["success"] is False
    assert "enabled" in result["message"]


def test_manage_editor_set_mcp_tool_enabled_sends_tool_list_changed(monkeypatch):
    async def fake_send(_func, _instance, _tool_name, params, **_kwargs):
        return {"success": True, "message": "ok"}

    monkeypatch.setattr(manage_editor_mod, "send_with_unity_instance", fake_send)
    ctx = NotifyContext()
    result = asyncio.run(
        manage_editor_mod.manage_editor(
            ctx=ctx,
            action="set_mcp_tool_enabled",
            tool_name="manage_scene",
            enabled=True,
        )
    )

    assert result["success"] is True
    assert ctx.tool_list_changed_calls == 1
