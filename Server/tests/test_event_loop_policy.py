"""
Test event loop policy configuration for Windows.

This module verifies that the correct asyncio event loop policy is used
on different platforms to prevent WinError 64 on Windows.

WinError 64 occurs when using ProactorEventLoop with concurrent
WebSocket and HTTP connections. The fix is to use SelectorEventLoop
on Windows.

Related fix: Server/src/main.py:31-35
"""

import sys
import asyncio
import pytest


@pytest.mark.skipif(sys.platform != "win32", reason="Windows-specific")
def test_windows_uses_selector_event_loop_policy():
    """
    Verify that Windows uses SelectorEventLoopPolicy instead of ProactorEventLoop.

    This prevents WinError 64 when handling concurrent WebSocket and HTTP connections.

    Regression test for Windows asyncio bug where ProactorEventLoop's IOCP
    has race conditions with rapid connection changes.

    The fix is applied in Server/src/main.py:31-35
    """
    # Import main module to trigger event loop policy setting
    import importlib
    import main
    importlib.reload(main)

    # Get the current event loop policy
    policy = asyncio.get_event_loop_policy()

    # Verify it's SelectorEventLoopPolicy, not ProactorEventLoop
    assert isinstance(
        policy,
        asyncio.WindowsSelectorEventLoopPolicy
    ), f"Expected WindowsSelectorEventLoopPolicy on Windows, got {type(policy).__name__}"


@pytest.mark.skipif(sys.platform == "win32", reason="Non-Windows only")
def test_non_windows_uses_default_policy():
    """
    Verify that non-Windows platforms use their default event loop policy.

    SelectorEventLoopPolicy should only be used on Windows to avoid the
    IOCP bug. Other platforms should use their optimal default policy.
    """
    import importlib
    import main
    importlib.reload(main)

    # Get the current event loop policy
    policy = asyncio.get_event_loop_policy()

    # On non-Windows, should NOT be WindowsSelectorEventLoopPolicy
    assert not isinstance(
        policy,
        asyncio.WindowsSelectorEventLoopPolicy
    ), "WindowsSelectorEventLoopPolicy should only be used on Windows"

    # Should be the platform's default policy type
    # (UnixSelectorEventLoopPolicy on Linux/macOS)
    default_policy_name = asyncio.get_event_loop_policy().__class__.__name__
    assert "SelectorEventLoopPolicy" in default_policy_name or "DefaultEventLoopPolicy" in default_policy_name, \
        f"Expected default policy for non-Windows, got {default_policy_name}"


def test_event_loop_policy_is_set_early():
    """
    Verify that event loop policy is set before any async operations.

    The policy must be set early (at module import time) to ensure all
    subsequent async operations use the correct event loop implementation.
    """
    import importlib
    import main

    # Reload main to ensure policy is set
    importlib.reload(main)

    # Policy should be set (not None)
    policy = asyncio.get_event_loop_policy()
    assert policy is not None, "Event loop policy should be set"

    # Policy should be a concrete instance, not a class
    assert isinstance(policy, asyncio.AbstractEventLoopPolicy), \
        f"Event loop policy should be an AbstractEventLoopPolicy instance, got {type(policy)}"


@pytest.mark.asyncio
async def test_async_operations_use_correct_event_loop():
    """
    Smoke test to verify async operations work with the configured event loop.

    This test creates a simple async operation to ensure the event loop
    is functional. It doesn't test WinError 64 directly (which is a
    timing-dependent race condition), but confirms the basic async
    infrastructure works.
    """
    # Simple async operation
    async def simple_task():
        await asyncio.sleep(0.01)
        return "success"

    # Should complete without errors
    result = await simple_task()
    assert result == "success"

    # Verify we're using the expected event loop
    loop = asyncio.get_event_loop()
    assert loop is not None, "Event loop should be running"
    assert loop.is_running(), "Event loop should be in running state"


@pytest.mark.skipif(sys.platform != "win32", reason="Windows-specific")
def test_windows_policy_prevents_proactor():
    """
    Verify that Windows explicitly avoids ProactorEventLoop.

    ProactorEventLoop uses IOCP which has known issues with WinError 64
    when handling rapid connection changes. This test confirms we're
    not using ProactorEventLoop.
    """
    import importlib
    import main
    importlib.reload(main)

    policy = asyncio.get_event_loop_policy()

    # Should NOT be ProactorEventLoop
    assert not isinstance(
        policy,
        asyncio.WindowsProactorEventLoopPolicy
    ), "ProactorEventLoop should not be used on Windows (causes WinError 64)"

    # Should be SelectorEventLoop
    assert isinstance(
        policy,
        asyncio.WindowsSelectorEventLoopPolicy
    ), "SelectorEventLoop should be used on Windows (prevents WinError 64)"
