import asyncio
import os
import time

import pytest

from .test_helpers import DummyContext


@pytest.mark.asyncio
async def test_wait_for_compilation_returns_immediately_when_ready(monkeypatch):
    """If compilation is already done, returns immediately with waited_seconds ~0."""
    from services.tools import manage_editor as mod
    from services.tools import refresh_unity as refresh_mod

    monkeypatch.delenv("PYTEST_CURRENT_TEST", raising=False)

    async def fake_get_editor_state(ctx):
        return {"data": {"advice": {"ready_for_tools": True, "blocking_reasons": []}}}

    monkeypatch.setattr(refresh_mod.editor_state, "get_editor_state", fake_get_editor_state)

    ctx = DummyContext()
    result = await mod._wait_for_compilation(ctx, timeout=10)
    assert result["success"] is True
    assert result["data"]["ready"] is True
    assert result["data"]["waited_seconds"] < 2.0


@pytest.mark.asyncio
async def test_wait_for_compilation_polls_until_ready(monkeypatch):
    """Waits while compiling, returns success when compilation finishes."""
    from services.tools import manage_editor as mod
    from services.tools import refresh_unity as refresh_mod

    monkeypatch.delenv("PYTEST_CURRENT_TEST", raising=False)

    call_count = 0

    async def fake_get_editor_state(ctx):
        nonlocal call_count
        call_count += 1
        if call_count < 3:
            return {"data": {"advice": {"ready_for_tools": False, "blocking_reasons": ["compiling"]}}}
        return {"data": {"advice": {"ready_for_tools": True, "blocking_reasons": []}}}

    monkeypatch.setattr(refresh_mod.editor_state, "get_editor_state", fake_get_editor_state)

    ctx = DummyContext()
    result = await mod._wait_for_compilation(ctx, timeout=10)
    assert result["success"] is True
    assert result["data"]["ready"] is True
    assert call_count >= 3


@pytest.mark.asyncio
async def test_wait_for_compilation_timeout(monkeypatch):
    """Returns failure when compilation doesn't finish within timeout."""
    from services.tools import manage_editor as mod
    from services.tools import refresh_unity as refresh_mod

    monkeypatch.delenv("PYTEST_CURRENT_TEST", raising=False)

    async def fake_get_editor_state(ctx):
        return {"data": {"advice": {"ready_for_tools": False, "blocking_reasons": ["compiling"]}}}

    monkeypatch.setattr(refresh_mod.editor_state, "get_editor_state", fake_get_editor_state)

    ctx = DummyContext()
    result = await mod._wait_for_compilation(ctx, timeout=1)
    assert result["success"] is False
    assert result["data"]["ready"] is False
    assert result["data"]["timeout_seconds"] == 1.0


@pytest.mark.asyncio
async def test_wait_for_compilation_default_timeout(monkeypatch):
    """None timeout defaults to 30s (clamped)."""
    from services.tools import manage_editor as mod
    from services.tools import refresh_unity as refresh_mod

    monkeypatch.delenv("PYTEST_CURRENT_TEST", raising=False)

    async def fake_get_editor_state(ctx):
        return {"data": {"advice": {"ready_for_tools": True, "blocking_reasons": []}}}

    monkeypatch.setattr(refresh_mod.editor_state, "get_editor_state", fake_get_editor_state)

    ctx = DummyContext()
    result = await mod._wait_for_compilation(ctx, timeout=None)
    assert result["success"] is True


@pytest.mark.asyncio
async def test_wait_for_compilation_via_manage_editor(monkeypatch):
    """The action is routed correctly through the main manage_editor function."""
    from services.tools import manage_editor as mod
    from services.tools import refresh_unity as refresh_mod

    monkeypatch.delenv("PYTEST_CURRENT_TEST", raising=False)

    async def fake_get_editor_state(ctx):
        return {"data": {"advice": {"ready_for_tools": True, "blocking_reasons": []}}}

    monkeypatch.setattr(refresh_mod.editor_state, "get_editor_state", fake_get_editor_state)

    ctx = DummyContext()
    result = await mod.manage_editor(ctx, action="wait_for_compilation", timeout=5)
    assert result["success"] is True
    assert result["data"]["ready"] is True


@pytest.mark.asyncio
async def test_wait_for_compilation_domain_reload(monkeypatch):
    """Waits through domain_reload blocking reason too."""
    from services.tools import manage_editor as mod
    from services.tools import refresh_unity as refresh_mod

    monkeypatch.delenv("PYTEST_CURRENT_TEST", raising=False)

    call_count = 0

    async def fake_get_editor_state(ctx):
        nonlocal call_count
        call_count += 1
        if call_count == 1:
            return {"data": {"advice": {"ready_for_tools": False, "blocking_reasons": ["compiling"]}}}
        if call_count == 2:
            return {"data": {"advice": {"ready_for_tools": False, "blocking_reasons": ["domain_reload"]}}}
        return {"data": {"advice": {"ready_for_tools": True, "blocking_reasons": []}}}

    monkeypatch.setattr(refresh_mod.editor_state, "get_editor_state", fake_get_editor_state)

    ctx = DummyContext()
    result = await mod._wait_for_compilation(ctx, timeout=10)
    assert result["success"] is True
    assert call_count >= 3
