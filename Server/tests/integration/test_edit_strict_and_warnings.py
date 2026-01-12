import pytest

from .test_helpers import DummyContext, setup_script_tools


@pytest.mark.asyncio
async def test_explicit_zero_based_normalized_warning(monkeypatch):
    tools = setup_script_tools()
    apply_edits = tools["apply_text_edits"]

    async def fake_send(cmd, params, **kwargs):
        # Simulate Unity path returning minimal success
        return {"success": True}

    import transport.legacy.unity_connection
    monkeypatch.setattr(
        transport.legacy.unity_connection,
        "async_send_command_with_retry",
        fake_send,
    )

    # Explicit fields given as 0-based (invalid); SDK should normalize and warn
    edits = [{"startLine": 0, "startCol": 0,
              "endLine": 0, "endCol": 0, "newText": "//x"}]
    resp = await apply_edits(
        DummyContext(),
        uri="mcpforunity://path/Assets/Scripts/F.cs",
        edits=edits,
        precondition_sha256="sha",
    )

    assert resp["success"] is True
    data = resp.get("data", {})
    assert "normalizedEdits" in data
    assert any(
        w == "zero_based_explicit_fields_normalized" for w in data.get("warnings", []))
    ne = data["normalizedEdits"][0]
    assert ne["startLine"] == 1 and ne["startCol"] == 1 and ne["endLine"] == 1 and ne["endCol"] == 1


@pytest.mark.asyncio
async def test_strict_zero_based_error(monkeypatch):
    tools = setup_script_tools()
    apply_edits = tools["apply_text_edits"]

    async def fake_send(cmd, params, **kwargs):
        return {"success": True}

    import transport.legacy.unity_connection
    monkeypatch.setattr(
        transport.legacy.unity_connection,
        "async_send_command_with_retry",
        fake_send,
    )

    edits = [{"startLine": 0, "startCol": 0,
              "endLine": 0, "endCol": 0, "newText": "//x"}]
    resp = await apply_edits(
        DummyContext(),
        uri="mcpforunity://path/Assets/Scripts/F.cs",
        edits=edits,
        precondition_sha256="sha",
        strict=True,
    )
    assert resp["success"] is False
    assert resp.get("code") == "zero_based_explicit_fields"
