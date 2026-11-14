"""Record a viewport while driving an explicitly selected Unity motion source."""
from typing import Annotated, Any, Literal
from uuid import uuid4

from fastmcp import Context
from mcp.types import ToolAnnotations
from pydantic import BaseModel, ConfigDict, Field, model_validator

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.legacy.unity_connection import async_send_command_with_retry
from transport.unity_transport import send_with_unity_instance

RecordingAction = Literal["capabilities", "discover", "prepare", "start", "status", "stop", "cancel"]


class RecordingPlaybackSpec(BaseModel):
    """A target resolved from discover, never an arbitrary method to invoke."""
    model_config = ConfigDict(extra="forbid")
    type: Literal["none", "timeline", "animation_clip", "animator", "dotween", "custom"] = "none"
    target: str | None = Field(default=None, description="Unique component instance ID or hierarchy path from discover.")
    clip_path: str | None = Field(default=None, description="AnimationClip asset path for animation_clip.")
    clip_name: str | None = Field(default=None, description="AnimationClip name, required to disambiguate multiple clips in an imported asset.")
    state: str | None = Field(default=None, description="Full Animator state path or a custom adapter's declared state.")
    layer: int = Field(default=0, ge=0)
    parameters: dict[str, Any] | None = Field(default=None, description="Animator bool/int/float values. Triggers require a custom adapter.")
    tween_id: str | None = Field(default=None, description="Unique string SetId of a live DOTween root Tween/Sequence.")

    @model_validator(mode="after")
    def check_target(self) -> "RecordingPlaybackSpec":
        """Require the selectors needed to resolve the chosen source unambiguously."""
        if self.type in {"timeline", "animation_clip", "animator", "custom"} and not self.target:
            raise ValueError("playback.target is required; resolve an unambiguous target with discover")
        if self.type == "animation_clip" and not self.clip_path:
            raise ValueError("animation_clip requires clip_path")
        if self.type == "animator" and not self.state:
            raise ValueError("animator requires a state from discover")
        if self.type == "dotween" and not self.tween_id:
            raise ValueError("dotween requires tween_id")
        return self


class RecordingRequest(BaseModel):
    """Shared MCP/CLI request contract; Unity resolves source-dependent timing and identities."""
    model_config = ConfigDict(extra="forbid", allow_inf_nan=False)
    action: RecordingAction
    recording_id: str | None = None
    request_id: str | None = None
    query: str | None = None
    page_size: int | None = Field(default=None, ge=1, le=100)
    cursor: int | None = Field(default=None, ge=0)
    capture_source: Literal["game_view", "camera", "scene_view"] | None = None
    camera: str | None = None
    playback: RecordingPlaybackSpec | None = None
    mode: Literal["realtime", "fixed_step"] | None = None
    duration_seconds: float | None = Field(default=None, gt=0, le=300)
    start_time: float | None = Field(default=None, ge=0)
    start_frame: int | None = Field(default=None, ge=0, strict=True)
    end_frame: int | None = Field(default=None, gt=0, strict=True)
    source_fps: float | None = Field(default=None, gt=0, le=1000)
    fps: int | None = Field(default=None, ge=1, le=120, strict=True)
    width: int | None = Field(default=None, ge=16, le=3840, strict=True)
    height: int | None = Field(default=None, ge=16, le=3840, strict=True)
    format: Literal["mp4", "webm", "png_sequence"] | None = None
    output_folder: str | None = None
    file_name: str | None = None
    restore_state: bool | None = None

    @model_validator(mode="after")
    def check_range(self) -> "RecordingRequest":
        """Reject conflicting ranges and spec changes to an already prepared recording."""
        if self.action in {"stop", "cancel"} and not self.recording_id:
            raise ValueError("recording_id is required for stop/cancel")
        if self.start_time is not None and self.start_frame is not None:
            raise ValueError("Use start_time or start_frame, not both")
        if self.end_frame is not None and self.duration_seconds is not None:
            raise ValueError("Use end_frame (exclusive) or duration_seconds, not both")
        if self.end_frame is not None and self.start_frame is not None and self.end_frame <= self.start_frame:
            raise ValueError("end_frame must be greater than start_frame")
        if any(v is not None and v % 2 for v in (self.width, self.height)):
            raise ValueError("width and height must be even")
        if (self.width or 1280) * (self.height or 720) > 3840 * 2160:
            raise ValueError("Resolution cannot exceed 3840x2160 pixels")
        if self.capture_source == "camera" and not self.camera:
            raise ValueError("camera is required for camera capture")
        if self.recording_id and self.action == "start":
            allowed = {"action", "recording_id", "request_id"}
            if any(v is not None for k, v in self.model_dump().items() if k not in allowed):
                raise ValueError("Start a prepared recording with recording_id only; prepare a new job to change its specification")
        return self


def build_recording_params(values: dict[str, Any]) -> dict[str, Any]:
    """Validate transport parameters and allocate a retry key once before dispatching start."""
    request = RecordingRequest.model_validate(values)
    params = request.model_dump(exclude_none=True)
    if request.action == "start" and not request.request_id:
        params["request_id"] = uuid4().hex
    return params


@mcp_for_unity_tool(
    group="core",
    description=(
        "Record a view while controlling Timeline, AnimationClip, Animator, DOTween or a custom state machine. "
        "Discover targets/source FPS first; resolve ambiguous names. prepare validates without changing playback; "
        "start returns immediately. Poll status until state is completed, failed, cancelled or interrupted. "
        "Use the artifact only when ready=true; otherwise report error when present. "
        "stop finalizes early, cancel preserves partial output. "
        "Frame indices are zero-based, end_frame is exclusive. source_fps addresses the source; fps is output FPS. "
        "game_view includes Overlay UI and requires unpaused graphical Play Mode. camera/scene_view work in Edit Mode "
        "but exclude Overlay UI/editor gizmos. fixed_step controls the selected source, not deterministic replay of "
        "arbitrary game logic. No audio. Resolve natural-language requirements into explicit parameters; never guess "
        "source FPS or state names. Use capabilities for platform limitations."
    ),
    annotations=ToolAnnotations(title="Manage Recording", destructiveHint=True),
)
async def manage_recording(
    ctx: Context,
    action: Annotated[RecordingAction, "Operation to perform."],
    recording_id: Annotated[str | None, "Prepared/running job ID; required for stop/cancel. Omit for status to list jobs."] = None,
    request_id: Annotated[str | None, "Optional start idempotency key; generated automatically if omitted."] = None,
    query: Annotated[str | None, "Filter discovered paths/custom IDs, or query an exact DOTween SetId string."] = None,
    page_size: Annotated[int | None, "Discovery page size, 1..100."] = None,
    cursor: Annotated[int | None, "Discovery offset from next_cursor."] = None,
    capture_source: Annotated[Literal["game_view", "camera", "scene_view"] | None, "Viewport to record; default game_view."] = None,
    camera: Annotated[str | None, "Unique camera component ID/path; required for camera capture."] = None,
    playback: Annotated[RecordingPlaybackSpec | None, "Playback source and state from discover."] = None,
    mode: Annotated[Literal["realtime", "fixed_step"] | None, "Defaults to realtime without playback, fixed_step otherwise."] = None,
    duration_seconds: Annotated[float | None, "Output duration, at most 300s, containing whole output frames; default 5s."] = None,
    start_time: Annotated[float | None, "Source start seconds; mutually exclusive with start_frame."] = None,
    start_frame: Annotated[int | None, "Zero-based source start frame. Requires discovered or explicit source_fps."] = None,
    end_frame: Annotated[int | None, "Exclusive source end frame; mutually exclusive with duration_seconds."] = None,
    source_fps: Annotated[float | None, "Override for source FPS. Never substitute output FPS for unknown source FPS."] = None,
    fps: Annotated[int | None, "Output FPS, 1..120; default 30."] = None,
    width: Annotated[int | None, "Even output width; default 1280. Fits/letterboxes the existing viewport."] = None,
    height: Annotated[int | None, "Even output height; default 720. Maximum 3840x2160 pixels."] = None,
    format: Annotated[Literal["mp4", "webm", "png_sequence"] | None, "Default mp4; webm on Linux; png_sequence is the portable fallback."] = None,
    output_folder: Annotated[str | None, "Project-relative folder outside Assets/Packages/ProjectSettings. Default Library/MCPForUnity/Recordings."] = None,
    file_name: Annotated[str | None, "Base name without extension. Unique suffix prevents overwrites."] = None,
    restore_state: Annotated[bool | None, "Restore captured playback state/settings; default true. Cannot undo arbitrary callback side effects."] = None,
) -> dict[str, Any]:
    """Validate a lifecycle action and dispatch it to the client-selected Unity instance.

    Recording jobs return immediately. Clients stop polling on any terminal state and
    use artifacts only when the returned job explicitly reports ready=true.
    """
    values = {k: v for k, v in locals().items() if k != "ctx" and v is not None}
    try:
        params = build_recording_params(values)
    except ValueError as exc:
        return {"success": False, "message": str(exc)}
    instance = await get_unity_instance_from_context(ctx)
    return await send_with_unity_instance(async_send_command_with_retry, instance, "manage_recording", params)
