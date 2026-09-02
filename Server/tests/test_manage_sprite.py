"""Tests for the manage_sprite tool.

These cover the Python side only: the action list and the argument checks that run
before anything is sent to Unity. The behaviour of the slicing, clip and controller
builders is covered by the EditMode tests in TestProjects, because it only means
anything against a real AssetDatabase.
"""
import asyncio
import inspect
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from services.tools.manage_sprite import VALID_ACTIONS, manage_sprite


@pytest.fixture
def mock_unity(monkeypatch):
    captured = {}

    async def fake_send(send_fn, unity_instance, tool_name, params):
        captured["params"] = params
        captured["calls"] = captured.get("calls", 0) + 1
        return {"success": True}

    monkeypatch.setattr(
        "services.tools.manage_sprite.get_unity_instance_from_context",
        AsyncMock(return_value=None),
    )
    monkeypatch.setattr("services.tools.manage_sprite.send_with_unity_instance", fake_send)
    return captured


def call(**kwargs):
    return asyncio.run(manage_sprite(SimpleNamespace(), **kwargs))


def test_actions_are_the_documented_five():
    assert set(VALID_ACTIONS) == {
        "get_info", "slice_sheet", "setup_clips",
        "setup_controller", "full_setup",
    }


class TestManageSpriteValidation:
    """Every case here must fail before a Unity round-trip is attempted."""

    def test_unknown_action_returns_error(self):
        result = call(action="nonexistent")
        assert result["success"] is False
        # The message has to name the alternatives, or the caller has nowhere to go.
        assert "get_info" in result["message"]

    @pytest.mark.parametrize("action", ["get_info", "slice_sheet", "setup_clips", "full_setup"])
    def test_path_is_required(self, action):
        result = call(action=action, path=None)
        assert result["success"] is False
        assert "path" in result["message"]

    @pytest.mark.parametrize("action", ["slice_sheet", "full_setup"])
    def test_cols_or_frame_width_is_required(self, action):
        result = call(action=action, path="Assets/hero.png")
        assert result["success"] is False
        # Asserting on success alone would pass for the wrong reason: with the check
        # removed the call reaches an absent Unity and fails there instead.
        assert "cols" in result["message"]

    def test_slice_sheet_accepts_frame_width_instead_of_cols(self, mock_unity):
        # frame_width is the documented alternative to cols; rejecting it would make
        # the error message above a lie.
        result = call(action="slice_sheet", path="Assets/hero.png", frame_width=32)
        assert result["success"] is True
        assert mock_unity["calls"] == 1

    def test_explicit_zero_cols_is_forwarded_not_reported_missing(self, mock_unity):
        # `not cols` read an explicit 0 as an omitted parameter and told the caller to
        # supply what they had supplied; the C# side is the one that names a bad value.
        call(action="slice_sheet", path="Assets/hero.png", cols=0)
        assert mock_unity["calls"] == 1
        assert mock_unity["params"]["cols"] == 0

    def test_setup_controller_requires_controller_path(self):
        result = call(action="setup_controller", clips=[{"name": "walk", "path": "a.anim"}])
        assert result["success"] is False
        assert "controller_path" in result["message"]


class TestParameterForwarding:
    def test_only_supplied_parameters_are_forwarded(self, mock_unity):
        """Unset optional arguments must not reach Unity as nulls.

        C# reads a forwarded null and a missing key the same way, so this is about
        keeping the wire readable rather than about a broken call.
        """
        call(action="slice_sheet", path="Assets/hero.png", cols=4)
        assert mock_unity["params"] == {"action": "slice_sheet", "path": "Assets/hero.png", "cols": 4}

    def test_paging_arguments_reach_unity_only_when_asked_for(self, mock_unity):
        """page_size and cursor are get_info's, and absent means "use the default"."""
        call(action="get_info", path="Assets/atlas.png")
        plain = mock_unity["params"]

        call(action="get_info", path="Assets/atlas.png", page_size=100, cursor=200)
        paged = mock_unity["params"]

        assert plain == {"action": "get_info", "path": "Assets/atlas.png"}
        assert paged["page_size"] == 100
        assert paged["cursor"] == 200

    def test_every_optional_argument_has_a_forwarding_branch(self, mock_unity):
        """A parameter accepted at the surface but dropped before the bridge is silent.

        Limitation: this drives every parameter through one action, so it assumes the
        forwarder stays action-agnostic. Scoping any entry to its own action means
        scoping this test with it.
        """
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

        call(action="full_setup", **{k: sample[k] for k in optional})

        forwarded = mock_unity["params"]
        dropped = [n for n in optional if n not in forwarded]
        assert not dropped, f"accepted at the surface but never sent to Unity: {dropped}"
        # The value too, not only the key. An audit reproduced a branch that kept the key
        # and replaced the caller's value; membership alone stayed green for it.
        changed = {n: (sample[n], forwarded[n]) for n in optional if forwarded[n] != sample[n]}
        assert not changed, f"forwarded under a different value than the caller sent: {changed}"
