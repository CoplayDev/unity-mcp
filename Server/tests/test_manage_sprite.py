"""Tests for the manage_sprite tool.

These cover the Python side only: the action list and the argument checks that run
before anything is sent to Unity. The behaviour of the slicing, clip and controller
builders is covered by the EditMode tests in TestProjects, because it only means
anything against a real AssetDatabase.
"""
import asyncio
from unittest.mock import AsyncMock, MagicMock, patch

from services.tools.manage_sprite import VALID_ACTIONS


class TestActionList:
    def test_actions_are_the_documented_five(self):
        assert set(VALID_ACTIONS) == {
            "get_info", "slice_sheet", "setup_clips",
            "setup_controller", "full_setup",
        }

    def test_no_duplicate_actions(self):
        assert len(VALID_ACTIONS) == len(set(VALID_ACTIONS))


class TestManageSpriteValidation:
    """Every case here must fail before a Unity round-trip is attempted."""

    def _run(self, coro):
        return asyncio.run(coro)

    def _ctx(self):
        ctx = MagicMock()
        ctx.get_state = AsyncMock(return_value=None)
        return ctx

    def _call(self, **kwargs):
        from services.tools.manage_sprite import manage_sprite
        return self._run(manage_sprite(self._ctx(), **kwargs))

    def test_unknown_action_returns_error(self):
        result = self._call(action="nonexistent")
        assert result["success"] is False
        # The message has to name the alternatives, or the caller has nowhere to go.
        assert "get_info" in result["message"]

    def test_get_info_requires_path(self):
        result = self._call(action="get_info", path=None)
        assert result["success"] is False
        assert "path" in result["message"]

    def test_slice_sheet_requires_path(self):
        result = self._call(action="slice_sheet", path=None)
        assert result["success"] is False
        assert "path" in result["message"]

    def test_slice_sheet_requires_cols_or_frame_width(self):
        result = self._call(action="slice_sheet", path="Assets/hero.png")
        assert result["success"] is False
        assert "cols" in result["message"]

    def test_slice_sheet_accepts_frame_width_instead_of_cols(self):
        # frame_width is the documented alternative to cols; rejecting it would make
        # the error message above a lie.
        with patch("services.tools.manage_sprite.get_unity_instance_from_context",
                   new=AsyncMock(return_value=None)), \
             patch("services.tools.manage_sprite.send_with_unity_instance",
                   new=AsyncMock(return_value={"success": True})) as sent:
            result = self._call(action="slice_sheet", path="Assets/hero.png", frame_width=32)

        assert result["success"] is True
        assert sent.await_count == 1

    def test_setup_clips_requires_path(self):
        result = self._call(action="setup_clips", path=None)
        assert result["success"] is False
        assert "path" in result["message"]

    def test_clip_name_with_a_separator_is_refused(self):
        result = self._call(action="setup_clips", path="Assets/hero.png",
                            clips=[{"name": "nested/walk"}])
        assert result["success"] is False
        assert "separator" in result["message"]

    def test_non_string_clip_name_is_refused_not_raised(self):
        # clips is typed list[dict[str, Any]], so a JSON number reaches the name check.
        result = self._call(action="setup_clips", path="Assets/hero.png", clips=[{"name": 7}])
        assert result["success"] is False
        assert "must be a string" in result["message"]

    def test_setup_controller_requires_controller_path(self):
        result = self._call(action="setup_controller", clips=[{"name": "walk", "path": "a.anim"}])
        assert result["success"] is False
        assert "controller_path" in result["message"]

    def test_full_setup_requires_path(self):
        result = self._call(action="full_setup", path=None)
        assert result["success"] is False
        assert "path" in result["message"]

    def test_full_setup_requires_cols_or_frame_width(self):
        result = self._call(action="full_setup", path="Assets/hero.png")
        assert result["success"] is False
        # Asserting on success alone would pass for the wrong reason: with the check
        # removed the call reaches an absent Unity and fails there instead.
        assert "cols" in result["message"]


class TestParameterForwarding:
    def _ctx(self):
        ctx = MagicMock()
        ctx.get_state = AsyncMock(return_value=None)
        return ctx

    def test_only_supplied_parameters_are_forwarded(self):
        """Unset optional arguments must not reach Unity as nulls.

        The C# side reads `@params["rows"]?.ToObject<int>() ?? 1`, so an explicitly
        forwarded null and a missing key behave the same - but forwarding every
        argument would still bury the real ones in noise on the wire.
        """
        from services.tools.manage_sprite import manage_sprite

        with patch("services.tools.manage_sprite.get_unity_instance_from_context",
                   new=AsyncMock(return_value=None)), \
             patch("services.tools.manage_sprite.send_with_unity_instance",
                   new=AsyncMock(return_value={"success": True})) as sent:
            asyncio.run(manage_sprite(self._ctx(), action="slice_sheet",
                                      path="Assets/hero.png", cols=4))

        params = sent.await_args.args[3]
        assert params == {"action": "slice_sheet", "path": "Assets/hero.png", "cols": 4}

    def test_paging_arguments_reach_unity_only_when_asked_for(self):
        """page_size and cursor are get_info's, and absent means "use the default".

        Forwarding cursor=0 unasked would be harmless, but forwarding page_size=0
        would not: the C# side refuses anything below 1, so a null that turned into
        a zero on the wire would break every plain get_info call.
        """
        from services.tools.manage_sprite import manage_sprite

        with patch("services.tools.manage_sprite.get_unity_instance_from_context",
                   new=AsyncMock(return_value=None)), \
             patch("services.tools.manage_sprite.send_with_unity_instance",
                   new=AsyncMock(return_value={"success": True})) as sent:
            asyncio.run(manage_sprite(self._ctx(), action="get_info",
                                      path="Assets/atlas.png"))
            plain = sent.await_args.args[3]

            asyncio.run(manage_sprite(self._ctx(), action="get_info",
                                      path="Assets/atlas.png",
                                      page_size=100, cursor=200))
            paged = sent.await_args.args[3]

        assert plain == {"action": "get_info", "path": "Assets/atlas.png"}
        assert paged["page_size"] == 100
        assert paged["cursor"] == 200
