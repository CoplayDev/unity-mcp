"""Tests for the manage_sprite tool and its CLI commands.

These cover the Python side only: the action list, the argument checks that run
before anything is sent to Unity, and the parameters each CLI command sends. The
behaviour of the slicing, clip and controller builders is covered by the EditMode
tests in TestProjects, because it only means anything against a real AssetDatabase.
"""
import asyncio
import inspect
import json
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

import pytest
from click.testing import CliRunner
from fastmcp.server.server import ToolResult
from mcp.types import ImageContent, TextContent

from cli.commands.sprite import sprite
from cli.utils.config import CLIConfig
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


@pytest.fixture
def mock_sprite_reply(mock_unity, monkeypatch):
    sender = AsyncMock()
    monkeypatch.setattr("services.tools.manage_sprite.send_with_unity_instance", sender)
    return sender


class TestSpriteImageContent:
    def test_png_image_is_returned_after_json_metadata(self, mock_sprite_reply):
        reply = {
            "success": True,
            "path": "Assets/hero.png",
            "width": 128,
            "height": 64,
            "sprite_mode": "Multiple",
            "pixels_per_unit": 100,
            "filter_mode": "Point",
            "slice_count": 8,
            "slices": [{"name": "hero_0"}],
            "next_cursor": 1,
            "image_base64": "data:image/png;base64,c3ByaXRl",
            "image_omitted_reason": None,
        }
        original = reply.copy()
        mock_sprite_reply.return_value = reply

        result = call(action="get_info", path="Assets/hero.png")

        assert isinstance(result, ToolResult)
        assert len(result.content) == 2
        text, image = result.content
        assert isinstance(text, TextContent)
        assert text.type == "text"
        assert isinstance(image, ImageContent)
        assert image.type == "image"
        assert image.data == "c3ByaXRl"
        assert image.mimeType == "image/png"
        meta = {key: value for key, value in reply.items() if key != "image_base64"}
        assert json.loads(text.text) == meta
        assert text.text == json.dumps(meta)
        assert reply == original

    @pytest.mark.parametrize("encoded, mime, payload", [
        ("data:image/jpeg;base64,c3ByaXRl", "image/jpeg", "c3ByaXRl"),
        ("c3ByaXRl", "image/png", "c3ByaXRl"),
        ("data:;base64,c3ByaXRl", "image/png", "c3ByaXRl"),
        ("data:image/png,c3ByaXRl", "image/png", "data:image/png,c3ByaXRl"),
        ("plain;base64,c3ByaXRl", "image/png", "plain;base64,c3ByaXRl"),
        ("data:image/png;base64,first;base64,second", "image/png", "first;base64,second"),
    ])
    def test_image_prefix_parsing(self, mock_sprite_reply, encoded, mime, payload):
        mock_sprite_reply.return_value = {"success": True, "image_base64": encoded}

        result = call(action="get_info", path="Assets/hero.png")

        assert isinstance(result, ToolResult)
        assert len(result.content) == 2
        assert isinstance(result.content[0], TextContent)
        assert json.loads(result.content[0].text) == {"success": True}
        image = result.content[1]
        assert isinstance(image, ImageContent)
        assert image.data == payload
        assert image.mimeType == mime

    @pytest.mark.parametrize("reply", [
        {"success": True, "image_base64": None, "image_omitted_reason": "File exceeds 4 MB"},
        {"success": True, "image_base64": None, "next_cursor": 512},
        {"success": True},
        {"success": True, "image_base64": ""},
        {"success": True, "image_base64": 123},
        {"success": True, "image_base64": ["c3ByaXRl"]},
        {"success": False, "image_base64": "data:image/png;base64,c3ByaXRl"},
        {"image_base64": "data:image/png;base64,c3ByaXRl"},
        {"success": 1, "image_base64": "data:image/png;base64,c3ByaXRl"},
    ])
    def test_replies_without_a_successful_image_are_unchanged(self, mock_sprite_reply, reply):
        original = reply.copy()
        mock_sprite_reply.return_value = reply

        result = call(action="get_info", path="Assets/hero.png")

        assert result is reply
        assert result == original

    @pytest.mark.parametrize("action", ["slice_sheet", "setup_clips", "setup_controller", "full_setup"])
    def test_other_actions_keep_images_in_the_reply(self, mock_sprite_reply, action):
        reply = {"success": True, "image_base64": "data:image/png;base64,c3ByaXRl"}
        original = reply.copy()
        mock_sprite_reply.return_value = reply

        result = call(
            action=action, path="Assets/hero.png", cols=4,
            controller_path="Assets/hero.controller",
        )

        assert result is reply
        assert result == original


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
    def test_a_bridge_reply_that_is_not_a_dict_becomes_a_failure(self, monkeypatch):
        async def fake_send(send_fn, unity_instance, tool_name, params):
            return "connection dropped"

        monkeypatch.setattr(
            "services.tools.manage_sprite.get_unity_instance_from_context",
            AsyncMock(return_value=None),
        )
        monkeypatch.setattr("services.tools.manage_sprite.send_with_unity_instance", fake_send)

        result = call(action="get_info", path="Assets/hero.png")
        assert result == {"success": False, "message": "connection dropped"}

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
            "frame_height": 1, "base_name": "b", "filter_mode": "bilinear", "clips": [{"name": "walk"}],
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


# =============================================================================
# CLI commands
# =============================================================================

@pytest.fixture
def run_cli():
    """Invoke a sprite CLI command against a mocked Unity reply.

    Returns (result, mock_run); the format is json so a test can read the output back.
    """
    config = CLIConfig(host="127.0.0.1", port=8080, timeout=30, format="json", unity_instance=None)

    def _invoke(args, reply=None):
        with patch("cli.commands.sprite.get_config", return_value=config):
            with patch("cli.commands.sprite.run_command", return_value=reply or {"success": True}) as mock_run:
                return CliRunner().invoke(sprite, args), mock_run
    return _invoke


class TestSpriteCLICommands:
    @pytest.mark.parametrize("args, expected", [
        (["info", "Assets/hero.png"],
         {"action": "get_info", "path": "Assets/hero.png"}),
        (["info", "Assets/atlas.png", "--page-size", "100", "--cursor", "200"],
         {"action": "get_info", "path": "Assets/atlas.png", "page_size": 100, "cursor": 200}),
        (["slice", "Assets/hero.png", "--cols", "4"],
         {"action": "slice_sheet", "path": "Assets/hero.png", "cols": 4}),
        (["slice", "Assets/hero.png", "--frame-width", "32", "--frame-height", "16", "--base-name", "hero",
          "--filter-mode", "bilinear"],
         {"action": "slice_sheet", "path": "Assets/hero.png", "frame_width": 32, "frame_height": 16,
          "base_name": "hero", "filter_mode": "bilinear"}),
        (["setup-clips", "Assets/hero.png", "--clips", '[{"name": "walk", "start_frame": 0, "end_frame": 5}]'],
         {"action": "setup_clips", "path": "Assets/hero.png",
          "clips": [{"name": "walk", "start_frame": 0, "end_frame": 5}]}),
        (["setup-clips", "Assets/hero.png", "--clips", "[]", "--output-dir", "Assets/Anim", "--overwrite"],
         {"action": "setup_clips", "path": "Assets/hero.png", "clips": [], "output_dir": "Assets/Anim",
          "overwrite": True}),
        (["setup-controller", "Assets/Hero.controller", "--clips", '[{"name": "idle", "path": "Assets/idle.anim"}]'],
         {"action": "setup_controller", "controller_path": "Assets/Hero.controller",
          "clips": [{"name": "idle", "path": "Assets/idle.anim"}]}),
        (["full-setup", "Assets/coin.png", "--cols", "8", "--animation-name", "spin"],
         {"action": "full_setup", "path": "Assets/coin.png", "cols": 8, "animation_name": "spin"}),
    ])
    def test_each_command_sends_its_action_and_only_the_options_given(self, run_cli, args, expected):
        result, mock_run = run_cli(args)

        assert result.exit_code == 0, result.output
        mock_run.assert_called_once()
        assert mock_run.call_args.args[0] == "manage_sprite"
        assert mock_run.call_args.args[1] == expected

    def test_every_tool_parameter_can_be_sent_from_the_cli(self, run_cli):
        """A parameter added to the MCP tool and not to the CLI is a CLI that cannot do it."""
        sent = set()
        for args in (
            ["info", "Assets/a.png", "--page-size", "1", "--cursor", "1"],
            ["setup-controller", "Assets/a.controller", "--clips", "[]", "--overwrite"],
            ["full-setup", "Assets/a.png", "--cols", "1", "--rows", "1", "--frame-width", "1",
             "--frame-height", "1", "--base-name", "b", "--clips", "[]", "--animation-name", "walk",
             "--output-dir", "Assets/out", "--controller-path", "Assets/a.controller", "--overwrite",
             "--add-to-scene", "--scene-target", "Hero", "--filter-mode", "point"],
        ):
            result, mock_run = run_cli(args)
            assert result.exit_code == 0, result.output
            sent |= set(mock_run.call_args.args[1])

        fn = getattr(manage_sprite, "fn", manage_sprite)
        assert sent == set(inspect.signature(fn).parameters) - {"ctx"}

    # /api/command hands back the reply as Unity sent it: TransportCommandDispatcher wraps
    # the tool's own object in {"status", "result"}, and nothing on the CLI path unwraps it.
    def test_info_leaves_the_image_out_and_says_where_it_is(self, run_cli):
        reply = {"status": "success", "result": {
            "success": True, "path": "Assets/hero.png", "width": 128, "height": 64,
            "image_base64": "data:image/png;base64,c3ByaXRl", "image_omitted_reason": None}}

        result, _ = run_cli(["info", "Assets/hero.png"], reply=reply)

        assert result.exit_code == 0, result.output
        assert "c3ByaXRl" not in result.output
        shown = json.loads(result.output)["result"]
        assert shown["image_base64"] is None
        assert "Assets/hero.png" in shown["image_omitted_reason"]
        assert shown["width"] == 128 and shown["height"] == 64

    def test_info_keeps_the_reason_unity_gave_for_sending_no_image(self, run_cli):
        reply = {"status": "success", "result": {
            "success": True, "path": "Assets/hero.tga", "image_base64": None,
            "image_omitted_reason": "The source is a '.tga' file; only PNG and JPEG sources are sent inline."}}
        expected = json.loads(json.dumps(reply))

        result, _ = run_cli(["info", "Assets/hero.tga"], reply=reply)

        assert json.loads(result.output) == expected

    @pytest.mark.parametrize("clips", ["{not json", '{"name": "walk"}'])
    def test_clips_that_are_not_a_json_list_stop_before_unity(self, run_cli, clips):
        result, mock_run = run_cli(["setup-clips", "Assets/hero.png", "--clips", clips])

        assert result.exit_code != 0
        mock_run.assert_not_called()

    def test_sprite_group_is_registered_on_the_root_cli(self):
        from cli.main import cli

        assert "sprite" in cli.commands
