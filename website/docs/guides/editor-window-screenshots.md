---
title: Editor window screenshots
---

# Editor window screenshots

`manage_editor_windows` lets an agent inspect an open Unity Editor tab or
window without mouse or keyboard control. It captures Unity's own image
buffer, so another desktop window can cover Unity. Use `manage_camera`
for camera renders and Scene View viewport screenshots.

Select the Unity instance for the request, then list its windows:

```json
{"action": "list"}
```

The result includes IDs, titles, C# types, positions, keyboard focus and
selected tabs. Inactive docked tabs are included. IDs change when a window
closes or the Editor restarts; get a new list when an ID fails.

Capture a window with its current ID:

```json
{"action": "screenshot", "window_id": 12345, "max_resolution": 1600}
```

You can use an exact `window_title` (case-insensitive) or `window_type`
instead. Use one selector only. Duplicate titles/types require an ID.
Without a selector, capture uses the window with keyboard focus.

The response has an MCP PNG image block and separate metadata. Image bytes
are not repeated in the text. The previous docked tab and keyboard focus
are restored by default. `focus=false` requires an already selected tab;
`restore_focus=false` leaves the target tab selected. Capture does not
replace a later user focus change.

## Model images and client display

Each capture returns one PNG image in the MCP tool result so the model can
inspect its pixels. It uses `annotations.audience=["assistant"]` to identify
the intended audience. This is an optional client hint, not a guarantee that
Claude, Codex or another host hides the image. The host controls thumbnails,
attachments, storage and transcript display. The tool does not create or
upload a separate user-facing artifact.

Image bytes stay in the image block; they are not repeated in text or emitted
as a gallery. Files are saved only when requested. Returning a resource link
instead would require the client to fetch and include the image, and would
still leave display to that client. A path-only result does not give the model
pixels to inspect.

See the MCP [content annotations](https://modelcontextprotocol.io/specification/2025-11-25/schema#annotations)
and [resource interaction model](https://modelcontextprotocol.io/specification/2025-11-25/server/resources#user-interaction-model).

## Files and private data

No screenshot file is saved by default. `save_file=true` saves a unique,
full-size PNG in `Library/McpEditorScreenshots`; `max_resolution` only
limits the inline image. Use `include_image=false, save_file=true` for a
file-only result. Output paths are not supplied by the caller.

Screenshots can contain private source code, asset names, file paths,
Console messages or credentials displayed in Editor windows. The image is
sent to the selected MCP client. Enable and use the tool only with clients
that may access that data. Saved PNGs have no automatic retention cleanup;
delete them when no longer needed and exclude them from public PR evidence.
Existing instance routing, tool visibility and remote authentication apply.

## Capture limits

Capture requires a graphical Editor. Batch mode, minimized windows and
native operating-system dialogs are unsupported. Only open `EditorWindow`
objects are listed. Captures contain the selected tab area and exclude
operating-system borders. An inactive tab must be selected to repaint it.
Another capture is rejected until the pending capture finishes.
Minimized windows may return an old buffer; restore them before capture.

The capture helper uses Unity's internal `GUIView.GrabPixels` API, also
used by Scene View capture. Missing internal APIs return explicit errors;
capture does not fall back to desktop pixels. Capture waits for Editor
updates without `EditorApplication.Step` or a synchronous player-loop pump.
Reload, shutdown or a closed target ends the pending capture. Retry after
the Editor is ready. Capture does not save scenes or change Play Mode state.

## CLI

```shell
unity-mcp --instance MyProject@hash editor windows
unity-mcp --instance MyProject@hash editor screenshot --window-id 12345
```

The CLI saves a PNG and returns its metadata. The MCP tool returns the image
directly through the existing server; no extra adapter or client entry is
required.
