import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from models.models import MCPResponse
from services import play_mode_startup as module
from services.tools.manage_editor import manage_editor


@pytest.fixture
def clock(monkeypatch):
    state = SimpleNamespace(now=0.0)
    monkeypatch.setattr(module, "time", SimpleNamespace(monotonic=lambda: state.now))
    async def sleep(seconds):
        state.now += seconds
    monkeypatch.setattr(module, "asyncio", SimpleNamespace(sleep=sleep,
        wait_for=asyncio.wait_for, TimeoutError=asyncio.TimeoutError))
    return state


def job(status, **extra):
    return {"success": True, "data": {"job_id": "startup-job", "status": status,
        "unity_instance": "project-a-hash", **extra}}


@pytest.mark.asyncio
async def test_reload_polling_stays_bound_and_never_replays_code(clock):
    send = AsyncMock(side_effect=[job("waiting"), {"success": False, "hint": "retry"},
        job("executing"), job("completed", on_ready_result={"success": True, "data": {"result": 42}})])
    response = await module.run_play_startup(send, {"action": "play", "onReadyCode": "return 42;"})
    assert response["success"] is True
    assert response["data"]["on_ready_result"]["data"]["result"] == 42
    first, *polls = send.await_args_list
    assert first.args[0]["action"] == "play"
    assert first.args[0]["onReadyCode"] == "return 42;"
    assert len(first.args[0]["startupRequestId"]) == 32
    assert first.args[1] is None
    assert all(call.args == ({"action": "get_play_mode_startup", "jobId": "startup-job"}, "project-a-hash") for call in polls)


@pytest.mark.asyncio
@pytest.mark.parametrize("phase", ["scene_loaded", "first_frame"])
async def test_wait_without_code_preserves_requested_milestone(clock, phase):
    send = AsyncMock(side_effect=[job("waiting"), job("completed", wait_until=phase)])
    result = await module.run_play_startup(send, {"action": "play", "waitUntil": phase})
    assert result["success"] is True
    assert send.await_args_list[0].args[0]["waitUntil"] == phase
    assert "onReadyCode" not in send.await_args_list[0].args[0]


@pytest.mark.asyncio
@pytest.mark.parametrize("params", [
    {"waitUntil": "bad"}, {"onReadyCode": " "}, {"onReadyCode": 1},
    {"onReadyCode": "x" * 50_001}, {"waitTimeout": 10},
    *[{"waitUntil": "first_frame", "waitTimeout": value} for value in (0, 301, float("nan"), float("inf"), "bad")],
])
async def test_invalid_startup_options_do_not_touch_unity(params):
    send = AsyncMock()
    assert (await module.run_play_startup(send, {"action": "play", **params}))["success"] is False
    send.assert_not_awaited()


@pytest.mark.asyncio
async def test_timeout_keeps_handle_and_does_not_stop_or_resubmit(clock):
    send = AsyncMock(return_value=job("waiting"))
    response = await module.run_play_startup(send, {"action": "play", "waitUntil": "first_frame", "waitTimeout": 1})
    assert response["success"] is False
    assert response["error"] == "play_startup_timeout"
    assert response["data"]["job_id"] == "startup-job"
    actions = [call.args[0]["action"] for call in send.await_args_list]
    assert actions.count("play") == 1
    assert set(actions) == {"play", "get_play_mode_startup"}


@pytest.mark.asyncio
@pytest.mark.parametrize("failure", [{"success": False, "hint": "retry", "data": None}, ConnectionError("disconnected")])
async def test_lost_start_reply_returns_known_id_without_replay(failure):
    send = AsyncMock(side_effect=[failure])
    response = await module.run_play_startup(send, {"action": "play", "onReadyCode": "return 1;"})
    assert response["success"] is False
    assert response["data"]["job_id"] == send.await_args.args[0]["startupRequestId"]
    assert response["data"]["status"] == "unknown"
    assert send.await_count == 1


@pytest.mark.asyncio
@pytest.mark.parametrize("status", ["waiting", "executing"])
async def test_busy_rejection_preserves_existing_job_without_replay(status):
    data = {"job_id": "existing-job", "status": status}
    send = AsyncMock(return_value={"success": False, "error": "Startup already pending.", "data": data})
    response = await module.run_play_startup(send, {"action": "play", "onReadyCode": "return 1;"})
    assert response["success"] is False
    assert response["data"] == data
    assert response["data"]["job_id"] != send.await_args.args[0]["startupRequestId"]
    assert send.await_count == 1


@pytest.mark.asyncio
async def test_poll_exception_does_not_lose_live_job(clock):
    send = AsyncMock(side_effect=[job("waiting"), ConnectionError("reload"), job("completed")])
    response = await module.run_play_startup(send, {"action": "play", "waitUntil": "first_frame"})
    assert response["success"] is True
    assert send.await_count == 3


@pytest.mark.asyncio
@pytest.mark.parametrize("error", ["Play Mode stopped before startup completed.", "Startup code failed."])
async def test_terminal_failure_preserves_code_diagnostics(clock, error):
    result = {"success": False, "error": "Runtime error", "data": {"exceptionType": "InvalidOperationException"}}
    send = AsyncMock(side_effect=[job("waiting"), job("failed", error=error, on_ready_result=result)])
    response = await module.run_play_startup(send, {"action": "play", "onReadyCode": "throw new System.InvalidOperationException();"})
    assert response["success"] is False
    assert response["message"] == error
    assert response["data"]["on_ready_result"] == result
    assert send.await_count == 2


@pytest.mark.asyncio
async def test_nested_rest_and_model_replies_are_normalized(clock):
    send = AsyncMock(side_effect=[{"status": "success", "result": job("waiting")},
        MCPResponse(success=True, data=job("completed")["data"])])
    assert (await module.run_play_startup(send, {"action": "play", "waitUntil": "scene_loaded"}))["success"] is True


@pytest.mark.asyncio
async def test_old_unity_package_cannot_claim_startup_ready():
    send = AsyncMock(return_value={"success": True, "message": "Entered play mode."})
    response = await module.run_play_startup(send, {"action": "play", "waitUntil": "first_frame"})
    assert response["error"] == "play_startup_unsupported"
    assert send.await_count == 1


@pytest.mark.asyncio
@pytest.mark.parametrize("action,options", [("pause", {"wait_until": "first_frame"}), ("get_play_mode_startup", {})])
async def test_tool_rejects_wrong_action_or_missing_job_before_send(monkeypatch, action, options):
    send = AsyncMock()
    monkeypatch.setattr("services.tools.manage_editor.get_unity_instance_from_context", AsyncMock(return_value="selected-a"))
    monkeypatch.setattr("services.tools.manage_editor.send_with_unity_instance", send)
    assert (await manage_editor(SimpleNamespace(), action=action, **options))["success"] is False
    send.assert_not_awaited()


@pytest.mark.asyncio
async def test_tool_routes_initial_selection_and_then_hash_without_transport_retries(monkeypatch, clock):
    send = AsyncMock(side_effect=[job("waiting"), job("completed")])
    monkeypatch.setattr("services.tools.manage_editor.get_unity_instance_from_context", AsyncMock(return_value="selected-a"))
    monkeypatch.setattr("services.tools.manage_editor.send_with_unity_instance", send)
    result = await manage_editor(SimpleNamespace(), action="play", wait_until="first_frame", wait_timeout=5)
    assert result["success"] is True
    assert [call.args[1] for call in send.await_args_list] == ["selected-a", "project-a-hash"]
    assert all(call.kwargs == {"retry_on_reload": False, "max_retries": 0} for call in send.await_args_list)


@pytest.mark.asyncio
async def test_get_startup_uses_explicit_job_id(monkeypatch):
    send = AsyncMock(return_value=job("completed"))
    monkeypatch.setattr("services.tools.manage_editor.get_unity_instance_from_context", AsyncMock(return_value="selected-a"))
    monkeypatch.setattr("services.tools.manage_editor.send_with_unity_instance", send)
    result = await manage_editor(SimpleNamespace(), action="get_play_mode_startup", job_id="startup-job")
    assert result["success"] is True
    assert send.await_args.args[3] == {"action": "get_play_mode_startup", "jobId": "startup-job"}
