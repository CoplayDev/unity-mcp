"""
Test event loop configuration for Windows.

This module verifies that the correct asyncio loop factory is selected
on different platforms to prevent WinError 64 on Windows.

WinError 64 occurs when using ProactorEventLoop with concurrent
WebSocket and HTTP connections. The fix is to use SelectorEventLoop
on Windows.

Related fix: Server/src/main.py
"""

import sys
import asyncio
from functools import partial
import pytest


@pytest.mark.skipif(sys.platform != "win32", reason="Windows-specific")
def test_windows_uses_selector_event_loop_factory():
    """
    Verify that Windows uses SelectorEventLoop via loop_factory.

    This prevents WinError 64 when handling concurrent WebSocket and HTTP connections.

    Regression test for Windows asyncio bug where ProactorEventLoop's IOCP
    has race conditions with rapid connection changes.

    The fix is applied in Server/src/main.py
    """
    import importlib
    import main  # type: ignore[import] - conftest.py adds src to sys.path
    importlib.reload(main)

    loop_factory = main._get_asyncio_loop_factory()
    assert loop_factory is asyncio.SelectorEventLoop, (
        "Expected SelectorEventLoop for Windows loop_factory"
    )


@pytest.mark.skipif(sys.platform == "win32", reason="Non-Windows only")
def test_non_windows_uses_default_loop_factory():
    """
    Verify that non-Windows platforms keep their default event loop behavior.

    SelectorEventLoop should only be used on Windows to avoid the IOCP bug.
    Other platforms should use their optimal default event loop.
    """
    import importlib
    import main  # type: ignore[import] - conftest.py adds src to sys.path
    importlib.reload(main)

    loop_factory = main._get_asyncio_loop_factory()
    assert loop_factory is None, "Non-Windows platforms should not set a loop_factory"


@pytest.mark.asyncio
async def test_async_operations_use_correct_event_loop():
    """
    Smoke test to verify async operations work with the configured event loop.

    This test creates a simple async operation to ensure the event loop
    is functional. It doesn't test WinError 64 directly (which is a
    timing-dependent race condition), but confirms the basic async
    infrastructure works with the policy configured in main.py.
    """
    # Import main to ensure the event loop policy is configured
    import importlib
    import main  # type: ignore[import] - conftest.py adds src to sys.path
    importlib.reload(main)

    # Simple async operation
    async def simple_task():
        await asyncio.sleep(0.01)
        return "success"

    # Should complete without errors
    result = await simple_task()
    assert result == "success"

    # Verify we're using the expected event loop
    # Use get_running_loop() as we're in an async context
    loop = asyncio.get_running_loop()
    assert loop is not None, "Event loop should be running"
    assert loop.is_running(), "Event loop should be in running state"


def test_run_mcp_uses_fastmcp_run_when_no_loop_factory(monkeypatch: pytest.MonkeyPatch):
    """When no loop factory is needed, _run_mcp should delegate to FastMCP.run."""
    import importlib
    import main  # type: ignore[import] - conftest.py adds src to sys.path
    importlib.reload(main)

    class DummyMCP:
        def __init__(self) -> None:
            self.run_called = False
            self.run_kwargs = {}

        def run(self, **kwargs):
            self.run_called = True
            self.run_kwargs = kwargs

    monkeypatch.setattr(main, "_get_asyncio_loop_factory", lambda: None)

    mcp = DummyMCP()
    main._run_mcp(mcp, transport="stdio")

    assert mcp.run_called
    assert mcp.run_kwargs["transport"] == "stdio"


def test_run_mcp_uses_anyio_with_loop_factory(monkeypatch: pytest.MonkeyPatch):
    """When loop factory exists, _run_mcp should use anyio.run with backend options."""
    import importlib
    import main  # type: ignore[import] - conftest.py adds src to sys.path
    importlib.reload(main)

    class DummyMCP:
        async def run_async(self, transport, show_banner=True, **kwargs):
            return None

    captured = {}

    def fake_anyio_run(func, *args, **kwargs):
        captured["func"] = func
        captured["args"] = args
        captured["kwargs"] = kwargs
        return None

    monkeypatch.setattr(main, "anyio", type("AnyIOStub", (), {"run": staticmethod(fake_anyio_run)}))
    monkeypatch.setattr(main, "_get_asyncio_loop_factory", lambda: asyncio.SelectorEventLoop)

    mcp = DummyMCP()
    main._run_mcp(mcp, transport="http", host="localhost", port=8080)

    assert isinstance(captured["func"], partial)
    assert captured["func"].args[0] == "http"
    assert captured["func"].keywords["show_banner"] is True
    assert captured["func"].keywords["host"] == "localhost"
    assert captured["func"].keywords["port"] == 8080
    assert captured["kwargs"]["backend_options"]["loop_factory"] is asyncio.SelectorEventLoop


@pytest.mark.skipif(sys.platform != "win32", reason="Windows-specific")
def test_windows_loop_factory_prevents_proactor():
    """
    Verify that Windows loop_factory explicitly avoids ProactorEventLoop.

    ProactorEventLoop uses IOCP which has known issues with WinError 64
    when handling rapid connection changes. This test confirms we're
    not using ProactorEventLoop.
    """
    import importlib
    import main  # type: ignore[import] - conftest.py adds src to sys.path
    importlib.reload(main)

    loop_factory = main._get_asyncio_loop_factory()
    assert loop_factory is asyncio.SelectorEventLoop, (
        "SelectorEventLoop should be used on Windows (prevents WinError 64)"
    )
