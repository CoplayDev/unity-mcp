import pytest

from .test_helpers import DummyContext


@pytest.mark.asyncio
async def test_run_tests_returns_busy_when_unity_reports_already_in_progress(monkeypatch):
    """
    Red test (#503): if Unity reports a test run is already in progress, the tool should return a
    structured Busy response quickly (retry hint + retry_after_ms) rather than looking like a generic failure.
    """
    import services.tools.run_tests as run_tests_mod

    async def fake_send_with_unity_instance(send_fn, unity_instance, command_type, params, **kwargs):
        assert command_type == "run_tests"
        # This mirrors the Unity-side exception message thrown by TestRunnerService today.
        return {
            "success": False,
            "error": "A Unity test run is already in progress.",
        }

    monkeypatch.setattr(run_tests_mod, "send_with_unity_instance", fake_send_with_unity_instance)

    result = await run_tests_mod.run_tests(ctx=DummyContext(), mode="EditMode")
    payload = result.model_dump() if hasattr(result, "model_dump") else result

    assert payload.get("success") is False
    # Desired new behavior: provide an explicit retry hint + suggested backoff.
    assert payload.get("hint") == "retry"
    data = payload.get("data") or {}
    assert isinstance(data, dict)
    assert data.get("reason") in {"tests_running", "busy"}
    assert isinstance(data.get("retry_after_ms"), int)
    assert data.get("retry_after_ms") >= 500


