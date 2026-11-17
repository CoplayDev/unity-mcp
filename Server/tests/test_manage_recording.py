"""Recording contract, validation, dispatch and CLI tests (no Editor required)."""
import asyncio
from unittest.mock import AsyncMock, MagicMock, patch
import pytest
from click.testing import CliRunner
from pydantic import ValidationError
from cli.commands.recording import recording
from services.tools.manage_recording import build_recording_params, manage_recording


@pytest.mark.parametrize("values", [
    {"start_time": 1, "start_frame": 30}, {"duration_seconds": 1, "end_frame": 60},
    {"start_frame": -1}, {"start_frame": 2.5}, {"start_frame": True},
    {"fps": 0}, {"fps": 121}, {"fps": 29.97}, {"width": 1919}, {"height": 1},
    {"width": 3840, "height": 3840}, {"duration_seconds": 301}, {"duration_seconds": 0},
    {"duration_seconds": float("nan")}, {"source_fps": float("inf")},
    {"capture_source": "camera"}, {"start_frame": 30, "end_frame": 29},
    {"playback": {"type": "timeline"}}, {"playback": {"type": "animator", "target": "Player"}},
    {"playback": {"type": "animation_clip", "target": "Player"}}, {"playback": {"type": "dotween"}},
    {"playback": {"type": "custom", "target": "UI", "invoke_method": "Run"}},
    {"recording_id": "prepared", "fps": 60}, {"format": "avi"},
])
def test_rejects_invalid_specification(values):
    with pytest.raises(ValidationError):
        build_recording_params({"action": "start", **values})


@pytest.mark.parametrize("action", ["stop", "cancel"])
def test_mutations_require_explicit_job(action):
    with pytest.raises(ValidationError):
        build_recording_params({"action": action})


def test_source_rate_is_not_guessed_from_output_rate():
    params = build_recording_params({"action": "prepare", "start_frame": 30, "fps": 60,
                                   "playback": {"type": "timeline", "target": "123"}})
    assert "source_fps" not in params
    assert params["start_frame"] == 30 and params["fps"] == 60


def test_preserves_exclusive_range_and_parameters():
    result = build_recording_params({"action": "prepare", "start_frame": 30, "end_frame": 120,
                                    "source_fps": 30, "fps": 60, "playback": {"type": "animator", "target": "UI", "state": "Base Layer.Open", "parameters": {"Visible": True}}})
    assert result["end_frame"] == 120
    assert result["playback"]["parameters"] == {"Visible": True}
    assert "request_id" not in result


def test_start_key_is_generated_once_and_can_be_supplied():
    first = build_recording_params({"action": "start"})
    assert len(first["request_id"]) == 32
    assert build_recording_params(first)["request_id"] == first["request_id"]
    assert build_recording_params({"action": "start", "recording_id": "p", "request_id": "same"})["request_id"] == "same"


def test_dispatch_uses_selected_instance_and_validated_params():
    ctx = MagicMock()
    with patch("services.tools.manage_recording.get_unity_instance_from_context", new=AsyncMock(return_value="Project@hash")), patch("services.tools.manage_recording.send_with_unity_instance", new=AsyncMock(return_value={"success": True})) as send:
        result = asyncio.run(manage_recording(ctx, "start", capture_source="camera", camera="42", duration_seconds=3, fps=60))
    assert result["success"] is True
    args = send.call_args.args
    assert args[1:3] == ("Project@hash", "manage_recording")
    assert args[3]["fps"] == 60 and args[3]["duration_seconds"] == 3
    assert "request_id" in args[3]


def test_validation_failure_does_not_contact_unity():
    with patch("services.tools.manage_recording.send_with_unity_instance", new=AsyncMock()) as send:
        result = asyncio.run(manage_recording(MagicMock(), "start", width=1919))
    assert result["success"] is False
    send.assert_not_called()


def test_cli_validates_before_network_access():
    with patch("cli.commands.recording.run_command") as send:
        result = CliRunner().invoke(recording, ["run", "start", "--spec", '{"fps":121}'])
    assert result.exit_code != 0
    send.assert_not_called()


def test_cli_forwards_job_id():
    with patch("cli.commands.recording.get_config", return_value=MagicMock(format="json")), patch("cli.commands.recording.run_command", return_value={"success": True}) as send:
        result = CliRunner().invoke(recording, ["run", "status", "--recording-id", "job"])
    assert result.exit_code == 0, result.output
    assert send.call_args.args[0:2] == ("manage_recording", {"action": "status", "recording_id": "job"})


def test_imported_clip_selector_is_preserved():
    params = build_recording_params({"action": "prepare", "playback": {"type": "animation_clip", "target": "Player", "clip_path": "Assets/Player.fbx", "clip_name": "Walk"}})
    assert params["playback"]["clip_name"] == "Walk"
