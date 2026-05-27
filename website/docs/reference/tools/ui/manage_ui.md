---
title: manage_ui
sidebar_label: manage_ui
description: "Manages Unity UI Toolkit elements (UXML documents, USS stylesheets, UIDocument components)."
---

# `manage_ui`

> **Auto-generated** from the Python tool registry. Do not hand-edit outside `<!-- examples:start --><!-- examples:end -->` blocks — the generator (`tools/generate_docs_reference.py`) will overwrite them.

**Group:** `ui` &nbsp;·&nbsp; **Module:** `services.tools.manage_ui`

## Description

Manages Unity UI Toolkit elements (UXML documents, USS stylesheets, UIDocument components). Read-only actions: ping, read, get_visual_tree, list. Modifying actions: create, update, delete, attach_ui_document, detach_ui_document, create_panel_settings, update_panel_settings, modify_visual_element.
Visual actions: render_ui (captures UI panel to a PNG screenshot for self-evaluation).
Structural actions: link_stylesheet (adds a Style src reference to a UXML file).

UI Toolkit workflow:
1. Use list to discover existing UI assets
2. Create a UXML file (structure, like HTML)
3. Create a USS file (styling, like CSS)
4. Link stylesheet to UXML via link_stylesheet
5. Attach UIDocument to a GameObject with the UXML source
6. Use get_visual_tree to inspect the result
7. Use modify_visual_element to change text, classes, or inline styles on live elements
8. Use render_ui to capture a visual preview for self-evaluation
   - In play mode: first call queues a WaitForEndOfFrame screen capture and returns pending=true;
     call render_ui a second time to retrieve the saved PNG (hasContent will be true).
   - In editor mode: assigns a RenderTexture to PanelSettings (best-effort; may stay blank).
9. Use detach_ui_document to remove UIDocument from a GameObject
10. Use delete to remove .uxml/.uss files

Important: Always use <ui:Style> (with the ui: namespace prefix) in UXML, not bare <Style>. UI Builder will fail to open files that use <Style> without the prefix.

## Parameters

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `action` | `Literal['ping', 'create', 'read', 'update', 'delete', 'attach_ui_document', 'detach_ui_document', 'create_panel_settings', 'update_panel_settings', 'get_visual_tree', 'render_ui', 'link_stylesheet', 'list', 'modify_visual_element']` | yes | Action to perform. |
| `path` | `str \| None` | — | Assets-relative path (e.g., 'Assets/UI/MainMenu.uxml' or 'Assets/UI/Styles.uss'). For render_ui: optional UXML path to render directly without a scene GameObject. |
| `contents` | `str \| None` | — | File content (UXML or USS markup). Plain text - encoding handled automatically. |
| `target` | `str \| None` | — | Target GameObject name or path for attach_ui_document / get_visual_tree / render_ui. |
| `source_asset` | `str \| None` | — | Path to UXML VisualTreeAsset (e.g., 'Assets/UI/MainMenu.uxml'). |
| `panel_settings` | `str \| None` | — | Path to PanelSettings asset. Auto-creates default if omitted. |
| `sort_order` | `int \| None` | — | UIDocument sort order (default 0). |
| `scale_mode` | `Literal['ConstantPixelSize', 'ConstantPhysicalSize', 'ScaleWithScreenSize'] \| None` | — | Panel scale mode. Legacy shorthand; prefer using 'settings' dict. |
| `reference_resolution` | `dict[str, int] \| None` | — | Reference resolution as {width, height}. Legacy shorthand; prefer using 'settings' dict. |
| `settings` | `dict[str, Any] \| None` | — | Generic PanelSettings properties dict for create_panel_settings. Keys: scaleMode (ConstantPixelSize\|ConstantPhysicalSize\|ScaleWithScreenSize), referenceResolution ({width,height}), screenMatchMode (MatchWidthOrHeight\|ShrinkToFit\|ExpandToFill), match (0-1 float), referenceDpi, fallbackDpi, sortingOrder, targetDisplay, clearColor (bool), colorClearValue (#RRGGBB or {r,g,b,a}), clearDepthStencil, themeStyleSheet (asset path), dynamicAtlasSettings ({minAtlasSize,maxAtlasSize,maxSubTextureSize,activeFilters}). |
| `max_depth` | `int \| None` | — | Max depth to traverse visual tree (default 10). |
| `width` | `int \| None` | — | Render width in pixels (default 1920). For render_ui. |
| `height` | `int \| None` | — | Render height in pixels (default 1080). For render_ui. |
| `include_image` | `bool \| None` | — | Return inline base64 PNG in the response (default false). For render_ui. |
| `max_resolution` | `int \| None` | — | Max resolution for inline base64 image (default 640). For render_ui. |
| `screenshot_file_name` | `str \| None` | — | Custom file name for the render output (default: auto-generated). For render_ui. |
| `output_folder` | `str \| None` | — | Optional folder for the render output. Project-relative (e.g. 'Assets/Screenshots' or 'Captures') or absolute path inside the project. Overrides the user's Editor preference. If omitted, falls back to the Editor preference, then to the built-in default (Assets/Screenshots). For render_ui. |
| `stylesheet` | `str \| None` | — | Path to USS stylesheet to link (e.g., 'Assets/UI/Styles.uss'). For link_stylesheet. |
| `filter_type` | `str \| None` | — | Filter UI assets by type: 'uxml', 'uss', 'PanelSettings', or omit for all. For list. |
| `page_size` | `int \| None` | — | Number of results per page (default 50). For list. |
| `page_number` | `int \| None` | — | Page number, 1-based (default 1). For list. |
| `element_name` | `str \| None` | — | Name of the visual element to modify (the 'name' attribute in UXML). For modify_visual_element. |
| `text` | `str \| None` | — | New text content for Label/Button elements. For modify_visual_element. |
| `add_classes` | `list[str] \| None` | — | USS class names to add to the element. For modify_visual_element. |
| `remove_classes` | `list[str] \| None` | — | USS class names to remove from the element. For modify_visual_element. |
| `toggle_classes` | `list[str] \| None` | — | USS class names to toggle on the element. For modify_visual_element. |
| `style` | `dict[str, Any] \| None` | — | Inline styles to set (e.g., {'backgroundColor': '#FF0000', 'fontSize': 24}). For modify_visual_element. |
| `enabled` | `bool \| None` | — | Set element enabled/disabled state. For modify_visual_element. |
| `visible` | `bool \| None` | — | Set element visibility (display: flex/none). For modify_visual_element. |
| `tooltip` | `str \| None` | — | Set element tooltip text. For modify_visual_element. |

## Returns

A `dict` containing the Unity response. The exact shape depends on the action.

## Examples

<!-- examples:start -->
*No examples yet. Add usage examples here — they will be preserved across regenerations.*
<!-- examples:end -->

