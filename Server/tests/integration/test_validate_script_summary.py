import pytest

from .test_helpers import DummyContext, setup_script_tools


@pytest.mark.asyncio
async def test_validate_script_returns_counts(monkeypatch):
    tools = setup_script_tools()
    validate_script = tools["validate_script"]

    async def fake_send(cmd, params, **kwargs):
        return {
            "success": True,
            "data": {
                "diagnostics": [
                    {"severity": "warning"},
                    {"severity": "error"},
                    {"severity": "fatal"},
                ]
            },
        }

    # Patch the send_command_with_retry function at the module level where it's imported
    import transport.legacy.unity_connection
    monkeypatch.setattr(transport.legacy.unity_connection,
                        "async_send_command_with_retry", fake_send)
    # No need to patch tools.manage_script; it now calls unity_connection.send_command_with_retry

    resp = await validate_script(DummyContext(), uri="mcpforunity://path/Assets/Scripts/A.cs")
    assert resp == {"success": True, "data": {"warnings": 1, "errors": 2}}
