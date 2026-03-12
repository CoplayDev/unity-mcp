"""
Tests for the manage_behavior tool.
Validates parameter routing for Unity Behavior (AI) operations.
"""
import pytest

from .test_helpers import DummyContext
import services.tools.manage_behavior as behavior_mod


def _fake_send_factory(captured: dict, response: dict = None):
    if response is None:
        response = {"success": True, "message": "OK", "data": {}}

    async def fake_send(cmd, params, **kwargs):
        captured["cmd"] = cmd
        captured["params"] = params
        return response

    return fake_send


@pytest.mark.asyncio
async def test_list_agents(monkeypatch):
    captured = {}
    monkeypatch.setattr(behavior_mod, "async_send_command_with_retry", _fake_send_factory(captured))
    resp = await behavior_mod.manage_behavior(ctx=DummyContext(), action="list_agents")
    assert resp["success"] is True
    assert captured["cmd"] == "manage_behavior"


@pytest.mark.asyncio
async def test_get_agent(monkeypatch):
    captured = {}
    monkeypatch.setattr(behavior_mod, "async_send_command_with_retry", _fake_send_factory(captured))
    resp = await behavior_mod.manage_behavior(
        ctx=DummyContext(), action="get_agent", target="EnemyAI",
    )
    assert resp["success"] is True
    assert captured["params"]["target"] == "EnemyAI"


@pytest.mark.asyncio
async def test_get_variable(monkeypatch):
    captured = {}
    monkeypatch.setattr(behavior_mod, "async_send_command_with_retry", _fake_send_factory(captured))
    resp = await behavior_mod.manage_behavior(
        ctx=DummyContext(), action="get_variable", target="EnemyAI", variable_name="health",
    )
    assert resp["success"] is True
    assert captured["params"]["variable_name"] == "health"


@pytest.mark.asyncio
async def test_set_variable(monkeypatch):
    captured = {}
    monkeypatch.setattr(behavior_mod, "async_send_command_with_retry", _fake_send_factory(captured))
    resp = await behavior_mod.manage_behavior(
        ctx=DummyContext(), action="set_variable", target="EnemyAI",
        variable_name="health", value="100",
    )
    assert resp["success"] is True
    assert captured["params"]["value"] == "100"


@pytest.mark.asyncio
async def test_none_params_stripped(monkeypatch):
    captured = {}
    monkeypatch.setattr(behavior_mod, "async_send_command_with_retry", _fake_send_factory(captured))
    await behavior_mod.manage_behavior(ctx=DummyContext(), action="list_agents")
    assert set(captured["params"].keys()) == {"action"}
