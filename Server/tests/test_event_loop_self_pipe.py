"""Tests for recovering the selector event loop's self-pipe after a reset (#1511)."""
import asyncio
import socket
import struct
import sys
import threading
import time

import pytest


@pytest.fixture
def recover():
    # Imported lazily: importing main reconfigures logging, which breaks caplog in other modules.
    from main import _recover_reset_self_pipe
    return _recover_reset_self_pipe


@pytest.fixture
def selector_loop():
    loop = asyncio.SelectorEventLoop()
    yield loop
    loop.close()


def _read_from_self_reset_context(loop: asyncio.AbstractEventLoop) -> dict:
    return {
        "message": "Exception in callback BaseSelectorEventLoop._read_from_self()",
        "exception": ConnectionResetError(10054, "An existing connection was forcibly closed by the remote host"),
        "handle": asyncio.Handle(loop._read_from_self, (), loop),
    }


def _assert_cross_thread_wakeup(loop: asyncio.AbstractEventLoop) -> None:
    woke = loop.create_future()
    threading.Timer(0.1, loop.call_soon_threadsafe, args=(woke.set_result, True)).start()
    start = time.perf_counter()
    loop.run_until_complete(asyncio.wait_for(woke, timeout=5))
    assert time.perf_counter() - start < 2


def test_reset_self_pipe_is_recreated(recover, selector_loop):
    old_ssock, old_csock = selector_loop._ssock, selector_loop._csock

    recover(selector_loop, _read_from_self_reset_context(selector_loop))

    assert selector_loop._ssock is not old_ssock
    assert old_ssock.fileno() == -1
    assert old_csock.fileno() == -1
    _assert_cross_thread_wakeup(selector_loop)


def test_other_errors_go_to_default_handler(recover, selector_loop, monkeypatch):
    seen = []
    monkeypatch.setattr(selector_loop, "default_exception_handler", seen.append)
    old_ssock = selector_loop._ssock
    contexts = [
        {"message": "boom", "exception": RuntimeError("boom")},
        {**_read_from_self_reset_context(selector_loop), "exception": OSError("not a reset")},
        {"message": "reset elsewhere", "exception": ConnectionResetError(),
         "handle": asyncio.Handle(print, (), selector_loop)},
    ]

    for context in contexts:
        recover(selector_loop, context)

    assert seen == contexts
    assert selector_loop._ssock is old_ssock


@pytest.mark.skipif(sys.platform != "win32", reason="the self-pipe is a TCP socket pair only on Windows")
def test_real_self_pipe_reset_stops_error_loop(recover, selector_loop, monkeypatch):
    seen = []
    monkeypatch.setattr(selector_loop, "default_exception_handler", seen.append)
    selector_loop.set_exception_handler(recover)
    # Abortive close sends RST, so recv() on the loop's end raises WinError 10054.
    selector_loop._csock.setsockopt(socket.SOL_SOCKET, socket.SO_LINGER, struct.pack("hh", 1, 0))
    selector_loop._csock.close()

    selector_loop.run_until_complete(asyncio.sleep(0.2))

    assert seen == []
    _assert_cross_thread_wakeup(selector_loop)
