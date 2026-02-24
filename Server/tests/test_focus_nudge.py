"""Tests for focus_nudge utility — should_nudge() logic and nudge_unity_focus() gating."""

import time
from unittest.mock import patch, AsyncMock

import pytest

from utils.focus_nudge import (
    should_nudge,
    reset_nudge_backoff,
    nudge_unity_focus,
    _is_available,
)


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
