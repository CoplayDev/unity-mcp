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

        The receiver reads an absent key and an explicit JSON null the same way -
        SpriteParams.TryReadWholeNumber names the null case and falls back - so this
        is about the wire staying readable, not about avoiding a broken call. An
        earlier version of this docstring claimed a null would become a zero and
        break plain get_info; that was wrong, and an audit caught it.
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

    def test_every_optional_argument_has_a_forwarding_branch(self):
        """A parameter accepted at the surface but dropped before the bridge is silent.

        The forwarder rebuilds the request by hand, one `if` per parameter, so adding
        an argument to the signature and forgetting its branch produces a tool that
        accepts the value and ignores it - no error anywhere. Fifteen branches are
        fifteen chances for that, and review is the only thing preventing it, which
        is a habit rather than a check. This is the check.

        Known limitation, named rather than fixed: this drives every parameter through
        one action, so it assumes the forwarder stays action-agnostic - which it is
        today, every branch being a plain `is not None`. If a branch is ever scoped to
        the actions that own it (page_size and cursor belong to get_info), this test
        will fail on correct code and must be scoped with it.
        """
        import inspect

        from services.tools.manage_sprite import manage_sprite

        fn = getattr(manage_sprite, "fn", manage_sprite)
        # A value each parameter's annotation accepts. Booleans must be True: the
        # forwarder deliberately omits a False flag, so False would look like a
        # dropped branch and this guard would cry wolf.
        sample = {
            "path": "Assets/a.png", "cols": 1, "rows": 1, "frame_width": 1,
            "frame_height": 1, "base_name": "b", "clips": [{"name": "walk"}],
            "animation_name": "walk", "output_dir": "Assets/out",
            "controller_path": "Assets/a.controller", "overwrite": True,
            "add_to_scene": True, "scene_target": "Hero", "page_size": 1, "cursor": 1,
        }
        optional = [
            name for name, prm in inspect.signature(fn).parameters.items()
            if name not in ("ctx", "action") and prm.default is not inspect.Parameter.empty
        ]
        missing_sample = [n for n in optional if n not in sample]
        assert not missing_sample, (
            f"this test has no sample value for {missing_sample}; add one rather than "
            "narrowing the guard"
        )

        with patch("services.tools.manage_sprite.get_unity_instance_from_context",
                   new=AsyncMock(return_value=None)), \
             patch("services.tools.manage_sprite.send_with_unity_instance",
                   new=AsyncMock(return_value={"success": True})) as sent:
            asyncio.run(manage_sprite(self._ctx(), action="full_setup",
                                      **{k: sample[k] for k in optional}))

        forwarded = sent.await_args.args[3]
        dropped = [n for n in optional if n not in forwarded]
        assert not dropped, f"accepted at the surface but never sent to Unity: {dropped}"
        # The value too, not only the key. An audit reproduced a branch that kept the key
        # and replaced the caller's value; membership alone stayed green for it.
        changed = {n: (sample[n], forwarded[n]) for n in optional if forwarded[n] != sample[n]}
        assert not changed, f"forwarded under a different value than the caller sent: {changed}"
