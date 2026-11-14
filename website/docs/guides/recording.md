---
title: Motion and video recording
sidebar_label: Recording
---

# Motion and video recording

`manage_recording` combines target discovery, source-time addressing, playback control and view capture.
A recording is one asynchronous Editor job. No Recorder package, external encoder or LLM API key is required.
The Python MCP interface, CLI and Unity C# implementation use the same specification.

## From natural language to an explicit recording

For a request such as “record Intro from frame 30 for three seconds at 1080p and 60 FPS”:

1. Query `capabilities`, then `discover` to resolve the actual Director, clip, Animator state or adapter.
   Use the returned component instance ID when names are ambiguous. `discover` is paginated.
2. Use the source's reported frame rate. Output FPS never substitutes for an unknown source frame rate.
   Ask for the missing source rate when addressing DOTween or a source without an intrinsic frame rate.
3. Call `prepare` with the resolved specification. It returns the source time range, expected output frames
   and unique output path without changing playback, entering Play Mode or creating output files.
   The plan binds the actual source, clip/tween and capture camera. Component targets in the returned plan
   are canonical instance IDs. Renaming an object or reusing its name does not redirect the recording.
   If a bound object is destroyed or its selected motion/controller is replaced, `start` fails before
   creating output files; prepare a new plan for the replacement. Scene View capture keeps the camera
   selected at preparation even if a different Scene view becomes active.
4. Call `start` with that `recording_id` only. Playback preparation and capture arming happen inside Unity.
5. Poll `status` until the job is terminal. Only `ready: true` denotes a successfully finalized artifact.

Example specification (the target is illustrative; discover the actual one):

```json
{
  "action": "prepare",
  "capture_source": "game_view",
  "playback": {"type": "timeline", "target": "Canvas/Intro"},
  "start_frame": 30,
  "duration_seconds": 3,
  "fps": 60,
  "width": 1920,
  "height": 1080,
  "format": "mp4"
}
```

For a 30 FPS source this selects source time 1.0 seconds and writes 180 output frames. Their source times are
`1 + k / 60`, for `k = 0..179`. Frame indices are **zero based**. `end_frame` is **exclusive** and replaces
`duration_seconds`; `start_time` replaces `start_frame`. The selected duration must contain an integer number
of output frames. Unrepresentable requests fail explicitly instead of being rounded silently.

## Capture sources and clocks

- `game_view` records the composited Game view after rendering, including Screen Space Overlay UI.
  It requires an unpaused graphical Editor in Play Mode. The recorder opens the Game tab, enables background
  running temporarily and restores it afterwards. A view that stops rendering fails after a bounded timeout.
- `camera` renders the explicitly selected camera and works in Edit Mode. Overlay canvases are excluded.
- `scene_view` renders the active Scene view's camera, without editor chrome, selection outlines or gizmos.
  Open a Scene view before preparing this source.

After entering/exiting Play Mode, wait for `mcpforunity://editor/state` to report readiness and rediscover
component instance IDs: IDs resolved before a scene/domain reload may no longer identify the same object.

The requested output size **fits and letterboxes** the source pixels. It does not resize the Game view or
reflow UI. Status reports both source and output dimensions, so clients can distinguish scaling from native
render resolution. Camera capture follows the screenshot infrastructure's render-pipeline support.

`realtime` samples actual wall time and duplicates frames when rendering falls behind the requested output
rate. Status exposes captured/encoded/duplicated frame counts. This preserves video duration, not missing
motion. `fixed_step` samples the selected source at exact output time intervals. In Play Mode it temporarily
sets `Time.captureDeltaTime`. It does **not** guarantee deterministic physics, arbitrary scripts, audio,
particle history or callback side effects. There is no audio track in this implementation.

## Playback sources

| Type | Selection and behavior |
| --- | --- |
| `none` | Record the currently running view, with no playback target. |
| `timeline` | Select a `PlayableDirector`. Its Timeline editor frame rate is discovered when available. The recorder owns its manual clock and evaluates each sample. |
| `animation_clip` | Supply a scene target and `clip_path`, plus `clip_name` for assets containing multiple clips. Edit Mode uses reversible AnimationMode sampling; Play Mode uses a temporary manual PlayableGraph. This samples the clip, not its Animator transitions or prior events. |
| `animator` | Requires Play Mode. Select a full state path and layer, with optional bool/int/float parameters. The source's Animator graph is manually advanced, so transitions can occur. Trigger-driven project workflows belong in a custom adapter. |
| `dotween` | Optional, no compile-time package dependency. Requires Play Mode and a unique string `tween_id` assigned with `SetId`. Query that ID with `discover`. The selected root Tween/Sequence is paused and sampled through `Goto`; unrelated tweens keep running. Specify `source_fps` for frame-based requests. |
| `custom` | A MonoBehaviour implementing `IRecordingPlaybackSource`, selected by component ID, hierarchy path or unique `RecordingId`. Declared states are discoverable. |

Playback clock ownership is always released after completion, cancellation or failure. With `restore_state`
(default true), the recorder also restores captured source settings/time/state. AnimationClip sampling requires
restoration. Animator transition blends and arbitrary event/Signal/script side effects cannot be rolled back;
use an isolated demo or a project adapter with a suitable state snapshot when those effects matter.

## Project state machines and tween engines

Implement `MCPForUnity.Runtime.Recording.IRecordingPlaybackSource` on a MonoBehaviour:

- Give it a stable `RecordingId`, declared `States`, `FrameRate` (or zero if unknown) and `DurationSeconds`
  (or zero if unbounded).
- `CaptureState` returns an opaque snapshot. `Prepare(state)` resets/selects the requested state and takes
  exclusive ownership of its playback clock. Disable normal automatic ticking while the recorder owns it.
- A seekable source implements `Seek(timeSeconds)`. A non-seekable source implements `Advance(deltaSeconds)`.
  Non-seekable sources are pre-rolled from zero to the recording offset in bounded batches on Editor updates;
  those frames are not captured. Pre-roll can run callbacks and is limited to 300 source seconds.
- `RestoreState(snapshot)` releases playback ownership and restores the snapshot. A null snapshot means
  release ownership while preserving the final visual state (`restore_state=false`).

Import **Motion recording demo** from the package Samples section (requires Unity UI; Timeline is optional).
Its `RecordingDemoSource` is a working adapter for Open/Close/Pulse states, including clock ownership and
snapshot restoration. Use **Window > MCP for Unity > Create Recording Demo** to generate a separate scene
with UI, Animator, optional Timeline and the custom source. No existing scene is overwritten.

## Job lifecycle and artifacts

States are `prepared`, optional `warming_up`, `recording`, `finalizing`, and one of `completed`, `cancelled`,
`failed`, `interrupted`. `stop` finalizes an active recording early; `cancel` leaves partial output unready.
Repeated `start` on a job ID returns that same job. A start `request_id` also deduplicates transport retries.
Only one recording runs per Editor. Up to 32 recent jobs survive assembly reloads in Editor SessionState.
Prepared and active jobs keep their IDs until they finish or are cancelled. Capacity pressure evicts the
oldest terminal history first. If all 32 slots hold prepared/active jobs, preparing another job returns an
explicit capacity error; cancel unused prepared jobs to free slots. Active jobs remain queryable, stoppable
and cancellable, and their retry keys remain valid.
Reload, Play Mode exit and Editor quit interrupt active recordings and close their encoders.

Files default to `Library/MCPForUnity/Recordings/`. The caller may choose another project-relative folder
outside Assets, Packages and ProjectSettings. Path traversal and existing symlink/junction ancestors are
rejected. Unique names prevent overwriting files. A `.partial` artifact is published to its final name only
on successful finalization. A JSON manifest records the resolved plan, frame counts, timing and errors.
Responses return paths and metadata, not video bytes; remote clients need access to the Editor machine's files.

MP4 and WebM use the Editor's `MediaEncoder`. Codec support depends on the installed Editor/platform. MP4
is rejected on Linux; use WebM or portable `png_sequence`. Limits: 300 output seconds, 18,000 frames, 1–120 FPS,
even dimensions, at most 3840 pixels per edge and 3840×2160 total pixels. Encoding failure is reported; formats
are never silently changed.

CLI uses the same validation, for example:

```bash
unity-mcp recording run capabilities
unity-mcp recording run discover --spec '{"query":"Intro"}'
unity-mcp recording run start --recording-id <prepared-id>
unity-mcp recording run status --recording-id <job-id>
```
