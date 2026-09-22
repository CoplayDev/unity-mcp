"""Tests for focus nudging, including Windows window capture and restoration."""

import json
import subprocess
import sys
import time
from unittest.mock import patch, AsyncMock

import pytest

from utils.focus_nudge import (
    should_nudge,
    reset_nudge_backoff,
    nudge_unity_focus,
    _is_available,
)
from utils import focus_nudge


@pytest.fixture
def windows_api_double(monkeypatch):
    """Execute the real PowerShell scripts against fake Win32 calls, without moving windows."""
    if sys.platform != "win32":
        pytest.skip("PowerShell/Win32 script integration requires Windows")

    # Replacing Add-Type after declaring the double lets PowerShell execute the
    # production scripts, including their stdout and exit-code handling.
    bootstrap = '''
Add-Type @"
using System;
using System.Text;
public class Win32 {
    static string Title { get { return Environment.GetEnvironmentVariable("NUDGE_TEST_TITLE") ?? ""; } }
    public static IntPtr GetForegroundWindow() {
        return Environment.GetEnvironmentVariable("NUDGE_TEST_NO_FOREGROUND") == "1"
            ? IntPtr.Zero : new IntPtr(123456);
    }
    public static int GetWindowTextLengthW(IntPtr hwnd) { return Title.Length; }
    public static int GetWindowTextW(IntPtr hwnd, StringBuilder text, int count) {
        text.Append(Title);
        return Title.Length;
    }
    public static bool IsWindow(IntPtr hwnd) {
        return hwnd.ToInt64() == 123456
            && Environment.GetEnvironmentVariable("NUDGE_TEST_CLOSED") != "1";
    }
    public static bool IsIconic(IntPtr hwnd) {
        return Environment.GetEnvironmentVariable("NUDGE_TEST_MINIMIZED") == "1";
    }
    public static bool ShowWindow(IntPtr hwnd, int command) {
        if (!IsIconic(hwnd) || command != 9) throw new Exception("Unexpected window resize");
        return true;
    }
    public static bool SetForegroundWindow(IntPtr hwnd) {
        return IsWindow(hwnd) && Environment.GetEnvironmentVariable("NUDGE_TEST_DENY") != "1";
    }
}
"@
function Add-Type { param([string] $TypeDefinition) }
function Get-Process {
    if ($env:NUDGE_TEST_NO_MATCH -ne '1') {
        [pscustomobject]@{ MainWindowTitle = $env:NUDGE_TEST_TITLE; MainWindowHandle = [IntPtr]::new(123456) }
    }
}
'''
    real_run = subprocess.run

    def run_with_fake_api(args, **kwargs):
        return real_run([*args[:-1], bootstrap + args[-1]], **kwargs)

    monkeypatch.setenv("NUDGE_TEST_TITLE", "Terminal")
    monkeypatch.setattr(focus_nudge.subprocess, "run", run_with_fake_api)


@pytest.mark.usefixtures("windows_api_double")
class TestWindowsFocusScripts:
    @pytest.mark.parametrize("title", ["Terminal", "한글 'title' `test`\nsecond line", "", "x" * 500])
    def test_capture_has_no_native_return_value_in_stdout(self, monkeypatch, title):
        monkeypatch.setenv("NUDGE_TEST_TITLE", title)
        app = focus_nudge._get_frontmost_app_windows()
        assert app is not None
        assert app.name == title
        assert app.window_handle == 123456

    def test_no_foreground_window(self, monkeypatch):
        monkeypatch.setenv("NUDGE_TEST_NO_FOREGROUND", "1")
        assert focus_nudge._get_frontmost_app_windows() is None

    @pytest.mark.parametrize("minimized", ["0", "1"])
    def test_restores_by_handle_after_title_changes(self, monkeypatch, minimized):
        app = focus_nudge._get_frontmost_app_windows()
        assert app is not None
        monkeypatch.setenv("NUDGE_TEST_TITLE", "Different title")
        monkeypatch.setenv("NUDGE_TEST_MINIMIZED", minimized)
        assert focus_nudge._focus_app(app) is True

    @pytest.mark.parametrize("failure", ["NUDGE_TEST_CLOSED", "NUDGE_TEST_DENY"])
    def test_reports_closed_window_or_denied_activation(self, monkeypatch, failure):
        app = focus_nudge._get_frontmost_app_windows()
        assert app is not None
        monkeypatch.setenv(failure, "1")
        assert focus_nudge._focus_app(app) is False

    @pytest.mark.parametrize("title", ["Unity", "Terminal"])
    def test_reports_missing_title_match(self, monkeypatch, title):
        monkeypatch.setenv("NUDGE_TEST_NO_MATCH", "1")
        assert focus_nudge._focus_app_windows(title) is False

    @pytest.mark.parametrize("title", ["Unity", "Terminal's `notes`"])
    def test_title_activation_still_works(self, monkeypatch, title):
        monkeypatch.setenv("NUDGE_TEST_TITLE", title)
        assert focus_nudge._focus_app_windows(title) is True


class TestWindowsFocusRestore:
    """Keep the original window's identity independent of its mutable title."""

    @pytest.mark.parametrize("title", ["Terminal", "notes\nsecond line", "한글 제목", ""])
    def test_captures_window_handle_and_title(self, title):
        result = subprocess.CompletedProcess(
            [], 0, json.dumps({"name": title, "window_handle": 123456}), ""
        )
        with patch("utils.focus_nudge.subprocess.run", return_value=result):
            app = focus_nudge._get_frontmost_app_windows()

        assert app is not None
        assert app.name == title
        assert app.window_handle == 123456

    @pytest.mark.parametrize("output", ["", "139\nTerminal", "{}", '{"name":"Terminal","window_handle":0}'])
    def test_rejects_invalid_window_identity(self, output):
        result = subprocess.CompletedProcess([], 0, output, "")
        with patch("utils.focus_nudge.subprocess.run", return_value=result):
            assert focus_nudge._get_frontmost_app_windows() is None

    def test_dispatches_restore_with_saved_handle(self):
        app = focus_nudge._FrontmostAppInfo(name="Old title", window_handle=123456)
        with patch("utils.focus_nudge.platform.system", return_value="Windows"), \
             patch("utils.focus_nudge._focus_app_windows", return_value=True) as focus:
            assert focus_nudge._focus_app(app) is True

        focus.assert_called_once_with("Old title", window_handle=123456)

    @pytest.mark.asyncio
    async def test_nudge_returns_to_captured_window(self, monkeypatch):
        monkeypatch.setattr(focus_nudge, "_consecutive_nudges", 0)
        monkeypatch.setattr(focus_nudge, "_last_nudge_time", 0.0)
        original = focus_nudge._FrontmostAppInfo(name="Original title", window_handle=123456)
        unity = focus_nudge._FrontmostAppInfo(name="Project - Unity", window_handle=654321)
        with patch("utils.focus_nudge.platform.system", return_value="Windows"), \
             patch("utils.focus_nudge._is_available", return_value=True), \
             patch("utils.focus_nudge._get_frontmost_app_windows", side_effect=[original, unity]), \
             patch("utils.focus_nudge._focus_app_windows", return_value=True) as focus, \
             patch("utils.focus_nudge.asyncio.sleep", new_callable=AsyncMock):
            assert await nudge_unity_focus(force=True) is True

        assert focus.call_args_list[-1].kwargs == {"window_handle": 123456}
        assert focus.call_count == 2


class TestShouldNudge:
    """Tests for should_nudge() decision logic."""

    def test_returns_false_when_not_running(self):
        assert should_nudge(status="succeeded", editor_is_focused=False, last_update_unix_ms=0, current_time_ms=99999) is False

    def test_returns_false_when_focused(self):
        assert should_nudge(status="running", editor_is_focused=True, last_update_unix_ms=0, current_time_ms=99999) is False

    def test_returns_true_when_stalled_and_unfocused(self):
        now_ms = int(time.time() * 1000)
        stale_ms = now_ms - 5000  # 5s ago
        assert should_nudge(status="running", editor_is_focused=False, last_update_unix_ms=stale_ms, current_time_ms=now_ms) is True

    def test_returns_false_when_recently_updated(self):
        now_ms = int(time.time() * 1000)
        recent_ms = now_ms - 1000  # 1s ago (within 3s threshold)
        assert should_nudge(status="running", editor_is_focused=False, last_update_unix_ms=recent_ms, current_time_ms=now_ms) is False

    def test_returns_true_when_no_updates_yet(self):
        """No last_update_unix_ms means tests might be stuck at start."""
        assert should_nudge(status="running", editor_is_focused=False, last_update_unix_ms=None) is True

    def test_custom_stall_threshold(self):
        now_ms = int(time.time() * 1000)
        stale_ms = now_ms - 2000  # 2s ago
        # Default threshold (3s) — not stale yet
        assert should_nudge(status="running", editor_is_focused=False, last_update_unix_ms=stale_ms, current_time_ms=now_ms) is False
        # Custom threshold (1s) — stale
        assert should_nudge(status="running", editor_is_focused=False, last_update_unix_ms=stale_ms, current_time_ms=now_ms, stall_threshold_ms=1000) is True

    def test_returns_false_for_failed_status(self):
        assert should_nudge(status="failed", editor_is_focused=False, last_update_unix_ms=0, current_time_ms=99999) is False

    def test_returns_false_for_cancelled_status(self):
        assert should_nudge(status="cancelled", editor_is_focused=False, last_update_unix_ms=0, current_time_ms=99999) is False


class TestResetNudgeBackoff:
    """Tests for reset_nudge_backoff() state management."""

    def test_resets_consecutive_nudges(self):
        import utils.focus_nudge as fn
        fn._consecutive_nudges = 5
        reset_nudge_backoff()
        assert fn._consecutive_nudges == 0

    def test_updates_last_progress_time(self):
        import utils.focus_nudge as fn
        old_time = fn._last_progress_time
        reset_nudge_backoff()
        assert fn._last_progress_time >= old_time


class TestNudgeUnityFocus:
    """Tests for nudge_unity_focus() gating logic."""

    @pytest.mark.asyncio
    async def test_skips_when_not_available(self):
        with patch("utils.focus_nudge._is_available", return_value=False):
            result = await nudge_unity_focus(force=True)
            assert result is False

    @pytest.mark.asyncio
    async def test_skips_when_unity_already_focused(self):
        from utils.focus_nudge import _FrontmostAppInfo
        with patch("utils.focus_nudge._is_available", return_value=True), \
             patch("utils.focus_nudge._get_frontmost_app", return_value=_FrontmostAppInfo(name="Unity")):
            result = await nudge_unity_focus(force=True)
            assert result is False

    @pytest.mark.asyncio
    async def test_skips_when_frontmost_app_unknown(self):
        with patch("utils.focus_nudge._is_available", return_value=True), \
             patch("utils.focus_nudge._get_frontmost_app", return_value=None):
            result = await nudge_unity_focus(force=True)
            assert result is False

    @pytest.mark.asyncio
    async def test_rate_limited_by_backoff(self):
        import utils.focus_nudge as fn
        from utils.focus_nudge import _FrontmostAppInfo
        # Simulate a very recent nudge
        fn._last_nudge_time = time.monotonic()
        fn._consecutive_nudges = 0
        with patch("utils.focus_nudge._is_available", return_value=True), \
             patch("utils.focus_nudge._get_frontmost_app", return_value=_FrontmostAppInfo(name="Terminal")):
            result = await nudge_unity_focus(force=False)
            assert result is False
