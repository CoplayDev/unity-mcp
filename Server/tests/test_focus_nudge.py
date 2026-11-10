"""Tests for focus_nudge utility — should_nudge() logic and nudge_unity_focus() gating."""

import asyncio
import json
import subprocess
import sys
import time
from unittest.mock import patch, AsyncMock

import pytest

from utils import focus_nudge
from utils.focus_nudge import (
    should_nudge,
    reset_nudge_backoff,
    nudge_unity_focus,
    _is_available,
)


@pytest.fixture(autouse=True)
def isolate_nudge_state(monkeypatch):
    import utils.focus_nudge as fn
    monkeypatch.delenv("UNITY_MCP_DISABLE_FOCUS_NUDGE", raising=False)
    for name in ("_last_nudge_time", "_consecutive_nudges", "_last_progress_time"):
        monkeypatch.setattr(fn, name, 0)
    monkeypatch.setattr(fn, "_nudge_in_flight", False)


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
             patch("utils.focus_nudge.platform.system", return_value="Darwin"), \
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


@pytest.mark.parametrize("value", ["1", "true", " YES ", "On"])
@pytest.mark.asyncio
async def test_opt_out_prevents_even_forced_nudge_and_stall_decision(monkeypatch, value):
    monkeypatch.setenv("UNITY_MCP_DISABLE_FOCUS_NUDGE", value)
    with patch("utils.focus_nudge.subprocess.run") as run, \
         patch("utils.focus_nudge._get_frontmost_app") as frontmost:
        assert not should_nudge("running", False, None)
        assert not await nudge_unity_focus(force=True)
    run.assert_not_called()
    frontmost.assert_not_called()


@pytest.mark.parametrize("value", ["0", "false", "off", ""])
def test_opt_out_false_values_preserve_stall_decision(monkeypatch, value):
    monkeypatch.setenv("UNITY_MCP_DISABLE_FOCUS_NUDGE", value)
    assert should_nudge("running", False, None)


class TestWindowsFocus:
    def test_capture_preserves_hwnd_and_title_as_separate_values(self):
        from utils.focus_nudge import _get_frontmost_app_windows
        payload = {"window_handle": 54321, "name": "Terminal: Unity notes"}
        with patch("utils.focus_nudge.subprocess.run", return_value=subprocess.CompletedProcess(
            [], 0, json.dumps(payload), "",
        )) as run:
            info = _get_frontmost_app_windows()
        assert info.name == payload["name"]
        assert info.window_handle == 54321
        assert "[void][Win32]::GetWindowText" in run.call_args.args[0][-1]
        assert run.call_args.kwargs["encoding"] == "utf-8"
        assert run.call_args.kwargs["creationflags"] == getattr(subprocess, "CREATE_NO_WINDOW", 0)

    @pytest.mark.parametrize("output", ["27\nTerminal", "{}", '{"window_handle": 0}', '{"window_handle": true}'])
    def test_bad_foreground_capture_cannot_be_restored_by_title(self, output):
        from utils.focus_nudge import _get_frontmost_app_windows
        with patch("utils.focus_nudge.subprocess.run", return_value=subprocess.CompletedProcess([], 0, output, "")):
            assert _get_frontmost_app_windows() is None

    @pytest.mark.parametrize("project_args", [
        ["-projectPath", r"C:\Projects\My Game"],
        ["-PROJECTPATH", "c:/projects/my game/"],
        [r"-projectPath=C:\Projects\My Game"],
    ])
    def test_resolves_exact_project_among_multiple_editors(self, project_args):
        from utils.focus_nudge import _find_unity_pid_by_project_path_windows
        processes = [
            {"ProcessId": 1, "Arguments": ["Unity.exe", "-projectPath", r"C:\Projects\My Game-other"]},
            {"ProcessId": 2, "Arguments": ["Unity.exe", "-projectPath", r"C:\Worktrees\My Game"]},
            {"ProcessId": 3, "Arguments": [r"C:\Unity\Editor\Unity.exe", *project_args, "-logFile", "editor.log"]},
            {"ProcessId": 4, "Arguments": []},
        ]
        with patch("utils.focus_nudge.subprocess.run", return_value=subprocess.CompletedProcess([], 0, json.dumps(processes), "")):
            assert _find_unity_pid_by_project_path_windows(r"C:\Projects\My Game") == 3

    @pytest.mark.parametrize("arguments", [
        ["Unity.exe", "-comment", r"literal -projectPath C:\Project", "-projectPath", r"C:\Other"],
        ["Unity.exe", "-comment", r"literal -projectPath=C:\Project"],
        ["Unity.exe", "-projectPath", r"C:\Project", "-projectPath", r"C:\Other"],
        ["Unity.exe", "-projectPath", r"C:\Project", r"-projectPath=C:\Project"],
        ["Unity.exe", "-projectPath"],
    ])
    def test_quoted_flag_text_and_duplicate_or_missing_project_values_do_not_match(self, arguments):
        from utils.focus_nudge import _find_unity_pid_by_project_path_windows
        processes = [{"ProcessId": 123, "Arguments": arguments}]
        with patch("utils.focus_nudge.subprocess.run", return_value=subprocess.CompletedProcess([], 0, json.dumps(processes), "")):
            assert _find_unity_pid_by_project_path_windows(r"C:\Project") is None

    @pytest.mark.parametrize("project_path", ["My Game", "Projects/My Game", r"\Projects\My Game"])
    def test_rejects_non_absolute_project_identity(self, project_path):
        from utils.focus_nudge import _find_unity_pid_by_project_path_windows
        with patch("utils.focus_nudge.subprocess.run") as run:
            assert _find_unity_pid_by_project_path_windows(project_path) is None
        run.assert_not_called()

    @pytest.mark.parametrize("processes", [
        [],
        [{"ProcessId": 1, "Arguments": ["Unity.exe", "-projectPath", r"C:\Other"]}],
        [
            {"ProcessId": 1, "Arguments": ["Unity.exe", "-projectPath", r"C:\Project"]},
            {"ProcessId": 2, "Arguments": ["Unity.exe", "-projectPath", r"C:\Project"]},
        ],
    ])
    def test_missing_or_ambiguous_match_never_activates_an_editor(self, processes):
        from utils.focus_nudge import _focus_app_windows
        with patch("utils.focus_nudge.subprocess.run", return_value=subprocess.CompletedProcess([], 0, json.dumps(processes), "")) as run:
            assert not _focus_app_windows("Unity", r"C:\Project")
        assert run.call_count == 1  # Process query only; no activation command.

    def test_missing_project_and_title_only_restore_never_activate(self):
        from utils.focus_nudge import _focus_app_windows
        with patch("utils.focus_nudge.subprocess.run") as run:
            assert not _focus_app_windows("Unity")
            assert not _focus_app_windows("Terminal")
        run.assert_not_called()

    @pytest.mark.parametrize("returncode", [0, 1])
    def test_restore_uses_saved_hwnd_and_checks_native_activation(self, returncode):
        from utils.focus_nudge import _FrontmostAppInfo, _focus_app
        original = _FrontmostAppInfo(name="Old title: ' $() `", window_handle=54321)
        with patch("utils.focus_nudge.platform.system", return_value="Windows"), \
             patch("utils.focus_nudge.subprocess.run", return_value=subprocess.CompletedProcess([], returncode, "", "")) as run:
            assert _focus_app(original) is (returncode == 0)
        script = run.call_args.args[0][-1]
        assert "$targetHwnd = [IntPtr]54321" in script
        assert run.call_args.kwargs["creationflags"] == getattr(subprocess, "CREATE_NO_WINDOW", 0)
        assert original.name not in script
        assert "if (-not [Win32]::SetForegroundWindow($targetHwnd)) { exit 1 }" in script
        assert "if ([Win32]::GetForegroundWindow() -ne $targetHwnd) { exit 1 }" in script

    def test_routes_project_identity_and_uses_only_selected_process(self):
        from utils.focus_nudge import _focus_app
        with patch("utils.focus_nudge.platform.system", return_value="Windows"), \
             patch("utils.focus_nudge._find_unity_pid_by_project_path_windows", return_value=234) as resolve, \
             patch("utils.focus_nudge.subprocess.run", return_value=subprocess.CompletedProcess([], 0, "", "")) as run:
            assert _focus_app("Unity", r"C:\Project")
        resolve.assert_called_once_with(r"C:\Project")
        assert "Get-Process -Id 234" in run.call_args.args[0][-1]
        assert "MainWindowTitle" not in run.call_args.args[0][-1]


@pytest.mark.asyncio
async def test_nudge_restores_saved_window_even_if_title_contains_unity():
    from utils.focus_nudge import _FrontmostAppInfo
    original = _FrontmostAppInfo(name="Unity bug report - browser", window_handle=123)
    with patch("utils.focus_nudge.platform.system", return_value="Windows"), \
         patch("utils.focus_nudge._is_available", return_value=True), \
         patch("utils.focus_nudge._get_frontmost_app", return_value=original), \
         patch("utils.focus_nudge._focus_app", return_value=True) as focus, \
         patch("utils.focus_nudge.asyncio.sleep", new_callable=AsyncMock):
        assert await nudge_unity_focus(force=True, unity_project_path=r"C:\Project")
    assert focus.call_args_list[0].args == ("Unity", r"C:\Project")
    assert focus.call_args_list[-1].args == (original,)


@pytest.mark.asyncio
async def test_cancelled_nudge_restores_original_window():
    from utils.focus_nudge import _FrontmostAppInfo
    import utils.focus_nudge as fn
    original = _FrontmostAppInfo(name="Terminal", window_handle=123)
    with patch("utils.focus_nudge.platform.system", return_value="Windows"), \
         patch("utils.focus_nudge._is_available", return_value=True), \
         patch("utils.focus_nudge._get_frontmost_app", return_value=original), \
         patch("utils.focus_nudge._focus_app", return_value=True) as focus, \
         patch("utils.focus_nudge.asyncio.sleep", new_callable=AsyncMock, side_effect=asyncio.CancelledError):
        with pytest.raises(asyncio.CancelledError):
            await nudge_unity_focus(force=True, unity_project_path=r"C:\Project")
    assert focus.call_args_list[-1].args == (original,)
    assert not fn._nudge_in_flight


@pytest.mark.asyncio
async def test_activation_failure_restores_focus_without_advancing_backoff_or_waiting():
    import utils.focus_nudge as fn
    original = fn._FrontmostAppInfo(name="Terminal", window_handle=123)
    with patch("utils.focus_nudge._is_available", return_value=True), \
         patch("utils.focus_nudge._get_frontmost_app", return_value=original), \
         patch("utils.focus_nudge._focus_app", side_effect=[False, True]) as focus, \
         patch("utils.focus_nudge.asyncio.sleep", new_callable=AsyncMock) as sleep:
        assert not await nudge_unity_focus(force=True, unity_project_path=r"C:\Project")
    assert fn._consecutive_nudges == 0
    assert focus.call_args_list[-1].args == (original,)
    assert not fn._nudge_in_flight
    sleep.assert_not_called()


@pytest.mark.parametrize("other_project", [r"C:\Project", r"C:\OtherWorktree"])
@pytest.mark.asyncio
async def test_overlapping_nudge_does_not_capture_or_restore_unity_as_original(other_project):
    import utils.focus_nudge as fn
    original = fn._FrontmostAppInfo(name="Browser", window_handle=123)
    unity = fn._FrontmostAppInfo(name="Unity", window_handle=456)
    first_wait_started = asyncio.Event()
    finish_first_wait = asyncio.Event()

    async def sleep_during_activation(duration):
        if duration == 0.5:
            first_wait_started.set()
            await finish_first_wait.wait()

    with patch("utils.focus_nudge.platform.system", return_value="Windows"), \
         patch("utils.focus_nudge._is_available", return_value=True), \
         patch("utils.focus_nudge._get_frontmost_app", side_effect=[original, unity]) as capture, \
         patch("utils.focus_nudge._focus_app", return_value=True) as focus, \
         patch("utils.focus_nudge.asyncio.sleep", side_effect=sleep_during_activation):
        first = asyncio.create_task(nudge_unity_focus(force=True, unity_project_path=r"C:\Project"))
        try:
            await asyncio.wait_for(first_wait_started.wait(), timeout=1)
            assert not await nudge_unity_focus(force=True, unity_project_path=other_project)
            capture.assert_called_once()
            assert focus.call_count == 1
        finally:
            finish_first_wait.set()
            await first
        assert first.result() is True
        assert focus.call_count == 2
        assert focus.call_args_list[-1].args == (original,)
    assert not fn._nudge_in_flight


@pytest.fixture
def windows_api_double(monkeypatch):
    """Execute the real PowerShell scripts against fake Win32 calls, without moving windows."""
    if sys.platform != "win32":
        pytest.skip("PowerShell/Win32 script integration requires Windows")

    # Replacing Add-Type after declaring the double lets PowerShell execute the
    # production scripts, including their stdout and exit-code handling.
    bootstrap = r'''
Add-Type @"
using System;
using System.Text;
public class Win32 {
    static string Title { get { return Environment.GetEnvironmentVariable("NUDGE_TEST_TITLE") ?? ""; } }
    public static IntPtr GetForegroundWindow() {
        return Environment.GetEnvironmentVariable("NUDGE_TEST_NO_FOREGROUND") == "1"
            ? IntPtr.Zero : new IntPtr(
                Environment.GetEnvironmentVariable("NUDGE_TEST_FOREGROUND_MISMATCH") == "1"
                    ? 654321 : 123456);
    }
    public static int GetWindowTextLengthW(IntPtr hwnd) { return Title.Length; }
    public static int GetWindowTextW(IntPtr hwnd, StringBuilder text, int count) {
        int length = Math.Min(Title.Length, count - 1);
        text.Append(Title.Substring(0, length));
        return length;
    }
    public static int GetWindowText(IntPtr hwnd, StringBuilder text, int count) {
        return GetWindowTextW(hwnd, text, count);
    }
    public static bool IsWindow(IntPtr hwnd) {
        return hwnd.ToInt64() == 123456
            && Environment.GetEnvironmentVariable("NUDGE_TEST_CLOSED") != "1";
    }
    public static bool IsIconic(IntPtr hwnd) {
        return Environment.GetEnvironmentVariable("NUDGE_TEST_MINIMIZED") == "1";
    }
    static bool Restored;
    public static bool ShowWindow(IntPtr hwnd, int command) {
        if (!IsIconic(hwnd) || command != 9) throw new Exception("Unexpected window resize");
        Restored = true;
        return true;
    }
    public static bool SetForegroundWindow(IntPtr hwnd) {
        return IsWindow(hwnd) && (!IsIconic(hwnd) || Restored)
            && Environment.GetEnvironmentVariable("NUDGE_TEST_DENY") != "1";
    }
}
"@
function Add-Type {
    param([string] $TypeDefinition)
    if ($TypeDefinition -notmatch 'public class Win32') {
        Microsoft.PowerShell.Utility\Add-Type -TypeDefinition $TypeDefinition
    }
}
function Get-CimInstance {
    [pscustomobject]@{
        ProcessId = 321
        CommandLine = ('Unity.exe -projectPath "{0}"' -f $env:NUDGE_TEST_PROJECT)
    }
}
function Get-Process {
    if ($env:NUDGE_TEST_NO_MATCH -ne '1') {
        [pscustomobject]@{ MainWindowTitle = $env:NUDGE_TEST_TITLE; MainWindowHandle = [IntPtr]::new(123456) }
    }
}
'''
    real_run = subprocess.run

    def run_with_fake_api(args, **kwargs):
        return real_run([*args[:-1], bootstrap + args[-1]], **kwargs)

    for key in (
        "NUDGE_TEST_NO_FOREGROUND", "NUDGE_TEST_CLOSED", "NUDGE_TEST_MINIMIZED",
        "NUDGE_TEST_DENY", "NUDGE_TEST_NO_MATCH", "NUDGE_TEST_FOREGROUND_MISMATCH",
    ):
        monkeypatch.delenv(key, raising=False)
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

    @pytest.mark.parametrize("window_state", ["normal", "maximized", "minimized"])
    def test_restores_by_handle_after_title_changes(self, monkeypatch, window_state):
        app = focus_nudge._get_frontmost_app_windows()
        assert app is not None
        monkeypatch.setenv("NUDGE_TEST_TITLE", "Different title")
        monkeypatch.setenv("NUDGE_TEST_MINIMIZED", "1" if window_state == "minimized" else "0")
        assert focus_nudge._focus_app(app) is True

    @pytest.mark.parametrize("failure", ["NUDGE_TEST_CLOSED", "NUDGE_TEST_DENY"])
    def test_reports_closed_window_or_denied_activation(self, monkeypatch, failure):
        app = focus_nudge._get_frontmost_app_windows()
        assert app is not None
        monkeypatch.setenv(failure, "1")
        assert focus_nudge._focus_app(app) is False

    @pytest.mark.parametrize("title", ["Unity", "Terminal"])
    def test_rejects_title_only_activation(self, monkeypatch, title):
        monkeypatch.setenv("NUDGE_TEST_NO_MATCH", "1")
        assert focus_nudge._focus_app_windows(title) is False

    @pytest.mark.parametrize("project_path", [r"C:\Projects\My Game", r"C:\项目\한글"])
    def test_exact_project_activation_still_works(self, monkeypatch, project_path):
        monkeypatch.setenv("NUDGE_TEST_PROJECT", project_path)
        assert focus_nudge._find_unity_pid_by_project_path_windows(project_path) == 321
        assert focus_nudge._focus_app_windows("Unity", unity_project_path=project_path) is True

    def test_rejects_successful_native_call_that_did_not_change_foreground(self, monkeypatch):
        app = focus_nudge._get_frontmost_app_windows()
        assert app is not None
        monkeypatch.setenv("NUDGE_TEST_FOREGROUND_MISMATCH", "1")
        assert focus_nudge._focus_app(app) is False


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

        focus.assert_called_once_with("Old title", None, 123456)

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
            assert await nudge_unity_focus(force=True, unity_project_path=r"C:\Project") is True

        assert focus.call_args_list[-1].args == ("Original title", None, 123456)
        assert focus.call_count == 2
