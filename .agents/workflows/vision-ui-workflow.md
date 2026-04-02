---
description: How to recreate a UI from a reference image using Unity UGUI and existing assets.
---

# Workflow: Vision-to-UI Recreation

Follow these steps when a user provides a reference image and asks to recreate it in Unity using UGUI.

## 1. Scene & Asset Analysis
Before building, understand the target environment and available tools:
- **List UI Assets**: Search for sprites and fonts that match the visual style.
  - `manage_asset action="search" search_pattern="t:Sprite"`
  - `manage_asset action="search" search_pattern="t:Font"`
- **Check Canvas**: Ensure a Canvas exists with a proper `CanvasScaler` (1920x1080 is default).
  - `manage_ugui action="ensure_canvas"`

## 2. Visual Decomposition
Analyze the reference image and identify the hierarchy:
- **Background/Container**: Large panels or full-screen images.
- **Layout Groups**: Items arranged in rows (Horizontal) or columns (Vertical).
- **Core Elements**: Buttons, Icons (Image), Labels (Text), Input Fields.
- **Properties**: Estimate colors (Hex), font sizes, and anchor positions.

## 3. Implementation (Bottom-Up or Top-Down)
Use the enhanced `manage_ugui` tool to build the hierarchy. **Combine properties into single calls** for efficiency.

### Step A: Create the Root Container
```json
{
  "action": "create_element",
  "type": "Panel",
  "name": "MainContainer",
  "color": "#2D2D2D",
  "anchor_preset": "middle_center",
  "size_delta": {"x": 800, "y": 600}
}
```

### Step B: Add Visual Elements with Assets
When you see an asset in the image, map it to a project asset:
```json
{
  "action": "create_element",
  "type": "Image",
  "name": "HeaderIcon",
  "parent": "MainContainer",
  "sprite": "Assets/UI/Icons/Search_Icon.png",
  "anchor_preset": "top_left",
  "anchored_position": {"x": 20, "y": -20},
  "size_delta": {"x": 40, "y": 40}
}
```

### Step C: Functional Styling
```json
{
  "action": "create_element",
  "type": "Text",
  "name": "Title",
  "parent": "MainContainer",
  "text": "SETTINGS",
  "color": "white",
  "fontSize": 24,
  "alignment": "MiddleCenter",
  "anchor_preset": "horiz_stretch_top",
  "size_delta": {"x": 0, "y": 50}
}
```

## 4. Refinement
- **Iterative Tweaks**: Use `action="modify_element"` to adjust spacing or colors after the initial creation.
- **Layout Components**: Add `VerticalLayoutGroup` or `HorizontalLayoutGroup` using `manage_components` if the list of items is dynamic.
- **Validation**: Use `manage_camera action="screenshot"` to verify the result against the reference.

## 5. Tips for Success
- **Slicing**: If an image looks stretched, check if the Sprite has a Border (9-slicing) set in its import settings.
- **Hierarchy**: Keep the hierarchy clean. Use "Empty" UI types for grouping if no visual component is needed.
- **Scale**: UGUI elements should almost always have a Scale of (1, 1, 1). Use `size_delta` for dimensions.
