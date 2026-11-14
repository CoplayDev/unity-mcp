"""Structured error fields (code, exception type, file/line) survive normalization; compile errors."""
from __future__ import annotations

import pytest

from models import MCPResponse
from models.unity_response import normalize_unity_response
import services.tools.read_console as read_console_mod
import services.tools.refresh_unity as refresh_mod
from tests.integration.test_helpers import DummyContext


def test_error_envelope_keeps_structured_fields():
    raw = {
        "status": "error",
        "error": "Object reference not set",
        "command": "manage_gameobject",
        "exception_type": "System.NullReferenceException",
        "file": "Packages/com.coplaydev.unity-mcp/Editor/Tools/X.cs",
        "line": 42,
        "stackTrace": "at X.Y () in X.cs:42",
    }
    out = normalize_unity_response(raw)
    assert out["success"] is False
    assert out["error"] == "Object reference not set"
    assert out["data"] == {
        "command": "manage_gameobject",
        "exception_type": "System.NullReferenceException",
        "file": "Packages/com.coplaydev.unity-mcp/Editor/Tools/X.cs",
        "line": 42,
    }


def test_outcome_unknown_code_reaches_the_client():
    raw = {"status": "error", "error": "interrupted", "code": "outcome_unknown", "replayed": True}
    assert normalize_unity_response(raw)["data"] == {"code": "outcome_unknown", "replayed": True}


def test_plain_error_envelope_has_no_data():
    assert normalize_unity_response({"status": "error", "error": "boom"})["data"] is None


@pytest.mark.asyncio
async def test_read_console_get_compile_errors(monkeypatch):
    captured = {}

    async def fake_send(send_fn, unity_instance, command_type, params, **kwargs):
        captured.update(command=command_type, params=params)
        return {"success": True, "data": {"error_count": 0, "messages": []}}

    monkeypatch.setattr(read_console_mod, "send_with_unity_instance", fake_send)
    await read_console_mod.read_console(DummyContext(), action="get_compile_errors")
    assert captured == {"command": "read_console",
                        "params": {"action": "get_compile_errors", "includeWarnings": False}}

    await read_console_mod.read_console(DummyContext(), action="get_compile_errors", types=["error", "warning"])
    assert captured["params"]["includeWarnings"] is True


ERR = {"severity": "error", "code": "CS0103", "message": "The name 'foo' does not exist",
       "file": "Assets/Scripts/Foo.cs", "line": 7, "column": 9}


def test_refresh_attaches_errors_of_new_compilation():
    before = {"error_count": 0, "last_compile_finished_unix_ms": 100, "messages": []}
    after = {"error_count": 1, "last_compile_finished_unix_ms": 200, "messages": [ERR]}
    out = refresh_mod._with_compile_errors(MCPResponse(success=True, message="Refresh requested."), before, after)
    assert out.success is True
    assert out.data["compilation_failed"] is True
    assert out.data["compile_errors"] == [ERR]
    assert "Assets/Scripts/Foo.cs:7 CS0103" in out.message


def test_refresh_ignores_errors_older_than_the_refresh():
    snap = {"error_count": 1, "last_compile_finished_unix_ms": 100, "messages": [ERR]}
    original = MCPResponse(success=True, message="Refresh requested.")
    assert refresh_mod._with_compile_errors(original, snap, dict(snap)) is original


def test_refresh_without_errors_is_unchanged():
    original = MCPResponse(success=True, message="ok", data={"x": 1})
    after = {"error_count": 0, "last_compile_finished_unix_ms": 300, "messages": []}
    assert refresh_mod._with_compile_errors(original, None, after) is original
