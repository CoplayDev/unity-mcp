# Unity MCP CLI Usage Guide

> **For AI Assistants and Developers**: This document explains the correct syntax and common pitfalls when using the Unity MCP CLI.

## Table of Contents

1. [Installation](#installation)
2. [Quick Start](#quick-start)
3. [Command Structure](#command-structure)
4. [Global Options](#global-options)
5. [Argument vs Option Syntax](#argument-vs-option-syntax)
6. [Common Mistakes and Corrections](#common-mistakes-and-corrections)
7. [Output Formats](#output-formats)
8. [Command Reference by Category](#command-reference-by-category)
9. [Complete Command Reference](#complete-command-reference)

---

## Installation

### Prerequisites

- **Python 3.10+** installed
- **Unity Editor** running with the MCP plugin enabled
- **MCP Server** running (HTTP transport on port 8080)

### Install via pip (from source)

```bash
# Navigate to the Server directory
cd /path/to/unity-mcp/Server

# Install in development mode
pip install -e .

# Or install with uv (recommended)
uv pip install -e .
```

### Install via uv tool

```bash
# Run directly without installing
uvx --from /path/to/unity-mcp/Server unity-mcp --help

# Or install as a tool
uv tool install /path/to/unity-mcp/Server
```

### Verify Installation

```bash
# Check version
unity-mcp --version

# Check help
unity-mcp --help

# Test connection to Unity
unity-mcp status
```

---

## Quick Start

### 1. Start the MCP Server

Make sure the Unity MCP server is running with HTTP transport:

```bash
# The server is typically started via the Unity-MCP window, select HTTP local, and start server, or try this manually:
cd /path/to/unity-mcp/Server
uv run mcp-for-unity --transport http --http-url http://localhost:8080
```

### 2. Verify Connection

```bash
unity-mcp status
```

Expected output:
```
Checking connection to 127.0.0.1:8080...
✅ Connected to Unity MCP server at 127.0.0.1:8080

Connected Unity instances:
  • MyProject (Unity 6000.2.10f1) [09abcc51]
```

### 3. Run Your First Commands

```bash
# Get scene hierarchy
unity-mcp scene hierarchy

# Create a cube
unity-mcp gameobject create "MyCube" --primitive Cube

# Move the cube
unity-mcp gameobject modify "MyCube" --position 0 2 0

# Take a screenshot
unity-mcp camera screenshot

# Enter play mode
unity-mcp editor play
```

### 4. Get Help on Any Command

```bash
# List all commands
unity-mcp --help

# Help for a command group
unity-mcp gameobject --help

# Help for a specific command
unity-mcp gameobject create --help
```

---

## Command Structure

The CLI follows this general pattern:

```
unity-mcp [GLOBAL_OPTIONS] COMMAND_GROUP [SUBCOMMAND] [ARGUMENTS] [OPTIONS]
```

**Example breakdown:**
```bash
unity-mcp -f json gameobject create "MyCube" --primitive Cube --position 0 1 0
#         ^^^^^^^ ^^^^^^^^^^^ ^^^^^^ ^^^^^^^^ ^^^^^^^^^^^^^^^ ^^^^^^^^^^^^^^^^
#         global  cmd group   subcmd argument option          multi-value option
```

---

## Global Options

Global options come **BEFORE** the command group:

| Option | Short | Description | Default |
|--------|-------|-------------|---------|
| `--host` | `-h` | MCP server host | `127.0.0.1` |
| `--port` | `-p` | MCP server port | `8080` |
| `--format` | `-f` | Output format: `text`, `json`, `table` | `text` |
| `--timeout` | `-t` | Command timeout in seconds | `30` |
| `--instance` | `-i` | Target Unity instance (hash or Name@hash) | auto |
| `--verbose` | `-v` | Print each command sent to Unity and its raw response to stderr | `false` |

**✅ Correct:**
```bash
unity-mcp -f json scene hierarchy
unity-mcp --format json --timeout 60 gameobject find "Player"
```

**❌ Wrong:**
```bash
unity-mcp scene hierarchy -f json  # Global option after command
```

---

## Argument vs Option Syntax

### Arguments (Positional)
Arguments are **required values** that come in a specific order, **without** flags.

```bash
unity-mcp gameobject find "Player"
#                         ^^^^^^^^ This is an ARGUMENT (positional)
```

### Options (Named)
Options use `--name` or `-n` flags and can appear in any order after arguments.

```bash
unity-mcp gameobject create "MyCube" --primitive Cube
#                                    ^^^^^^^^^^^ ^^^^ This is an OPTION with value
```

### Multi-Value Options
Some options accept multiple values. **Do NOT use commas** - use spaces:

**✅ Correct:**
```bash
unity-mcp gameobject modify "Cube" --position 1 2 3
unity-mcp gameobject modify "Cube" --rotation 0 45 0
unity-mcp gameobject modify "Cube" --scale 2 2 2
```

**❌ Wrong:**
```bash
unity-mcp gameobject modify "Cube" --position "1,2,3"   # Wrong: comma-separated string
unity-mcp gameobject modify "Cube" --position 1,2,3    # Wrong: comma-separated
unity-mcp gameobject modify "Cube" -pos "1 2 3"        # Wrong: quoted as single string
```

---

## Common Mistakes and Corrections

### 1. Multi-Value Options (Position, Rotation, Scale, Color)

These options expect **separate float arguments**, not comma-separated strings:

| Option | ❌ Wrong | ✅ Correct |
|--------|----------|-----------|
| `--position` | `--position "2,1,0"` | `--position 2 1 0` |
| `--rotation` | `--rotation "0,45,0"` | `--rotation 0 45 0` |
| `--scale` | `--scale "1,1,1"` | `--scale 1 1 1` |
| Color args | `1,0,0,1` | `1 0 0 1` |

**Example - Moving a GameObject:**
```bash
# Wrong - will error "requires 3 arguments"
unity-mcp gameobject modify "Cube" --position "2,1,0"

# Correct
unity-mcp gameobject modify "Cube" --position 2 1 0
```

**Example - Setting material color:**
```bash
# Wrong
unity-mcp material set-color "Assets/Mat.mat" 1,0,0,1

# Correct (R G B or R G B A as separate args)
unity-mcp material set-color "Assets/Mat.mat" 1 0 0
unity-mcp material set-color "Assets/Mat.mat" 1 0 0 1
```

### 2. Argument Order Matters

Some commands have multiple positional arguments. Check `--help` to see the order:

**Material assign:**
```bash
# Wrong - arguments in wrong order
unity-mcp material assign "TestCube" "Assets/Materials/Red.mat"

# ✅ Correct - MATERIAL_PATH comes before TARGET
unity-mcp material assign "Assets/Materials/Red.mat" "TestCube"
```

**Prefab create:**
```bash
# Wrong - using --path option that doesn't exist
unity-mcp prefab create "Cube" --path "Assets/Prefabs/Cube.prefab"

# Correct - PATH is a positional argument
unity-mcp prefab create "Cube" "Assets/Prefabs/Cube.prefab"
```

### 3. Using Options That Don't Exist

Always check `--help` before assuming an option exists:

```bash
# Check available options for any command
unity-mcp gameobject modify --help
unity-mcp material assign --help
unity-mcp prefab create --help
```

### 4. Property Names for Materials

Different shaders use different property names. Use `material info` to discover them:

```bash
# First, check what properties exist
unity-mcp material info "Assets/Materials/MyMat.mat"

# Then use the correct property name
# For URP shaders, often "_BaseColor" instead of "_Color"
unity-mcp material set-color "Assets/Mat.mat" 1 0 0 --property "_BaseColor"
```

### 5. Search Methods

When targeting GameObjects, specify how to search:

```bash
# By name (default)
unity-mcp gameobject modify "Player" --position 0 0 0

# By instance ID (use --search-method). IDs are often negative, and a bare -81840
# reads as an option ("No such option: -8"): put options first, then -- before the ID
unity-mcp gameobject modify --search-method by_id --position 0 0 0 -- -81840

# By path
unity-mcp gameobject modify "/Canvas/Panel/Button" --search-method by_path --active

# By tag (gameobject find takes --method, not --search-method)
unity-mcp gameobject find "Player" --method by_tag
```

---

## Output Formats

### Text (Default)
Human-readable nested format:
```bash
unity-mcp scene active
# Output:
# status: success
# result:
#   name: New Scene
#   path: Assets/Scenes/New Scene.unity
#   ...
```

### JSON
Machine-readable JSON:
```bash
unity-mcp -f json scene active
# Output: {"status": "success", "result": {...}}
```

### Table
Key-value table format:
```bash
unity-mcp -f table scene active
# Output:
# Key    | Value
# -------+------
# status | success
# ...
```

---

## Command Reference by Category

### Status & Connection

```bash
# Check server connection and Unity instances
unity-mcp status

# List connected Unity instances
unity-mcp instances
```

### Instance Commands

```bash
# List connected Unity instances
unity-mcp instance list

# Target one instance: per call with --instance, or for a whole shell with UNITY_MCP_INSTANCE
unity-mcp --instance "ProjectName@abc123" editor play

# Show current instance
unity-mcp instance current
```

### Scene Commands

```bash
# Get scene hierarchy
unity-mcp scene hierarchy

# Get active scene info
unity-mcp scene active

# Get build settings
unity-mcp scene build-settings

# Create new scene
unity-mcp scene create "MyScene"

# Load scene
unity-mcp scene load "Assets/Scenes/MyScene.unity"

# Save current scene
unity-mcp scene save

# Take screenshot (use camera command)
unity-mcp camera screenshot
unity-mcp camera screenshot --file-name "my_screenshot" --super-size 2
```

### GameObject Commands

```bash
# Find GameObjects
unity-mcp gameobject find "Player"
unity-mcp gameobject find "Enemy" --method by_tag
unity-mcp gameobject find --method by_id -- -81840  # -- before a negative ID
unity-mcp gameobject find "Rigidbody" --method by_component

# Create GameObject
unity-mcp gameobject create "Empty"                    # Empty object
unity-mcp gameobject create "MyCube" --primitive Cube  # Primitive
unity-mcp gameobject create "MyObj" --position 0 5 0   # With position
unity-mcp gameobject create "Player" --components "Rigidbody,BoxCollider"  # With components

# Modify GameObject
unity-mcp gameobject modify "Cube" --position 1 2 3
unity-mcp gameobject modify "Cube" --rotation 0 45 0
unity-mcp gameobject modify "Cube" --scale 2 2 2
unity-mcp gameobject modify "Cube" --name "NewName"
unity-mcp gameobject modify "Cube" --active           # Enable
unity-mcp gameobject modify "Cube" --inactive         # Disable
unity-mcp gameobject modify "Cube" --tag "Player"
unity-mcp gameobject modify "Cube" --parent "Parent"

# Delete GameObject
unity-mcp gameobject delete "Cube"
unity-mcp gameobject delete "Cube" --force            # Skip confirmation

# Duplicate GameObject
unity-mcp gameobject duplicate "Cube"

# Move relative to another object
unity-mcp gameobject move "Cube" --reference "Player" --direction up --distance 2
```

### Component Commands

```bash
# Add component
unity-mcp component add "Cube" Rigidbody
unity-mcp component add "Cube" BoxCollider

# Remove component
unity-mcp component remove "Cube" Rigidbody
unity-mcp component remove "Cube" Rigidbody --force  # Skip confirmation

# Set single property
unity-mcp component set "Cube" Rigidbody mass 5
unity-mcp component set "Cube" Rigidbody useGravity false
unity-mcp component set "Cube" Light intensity 2.5

# Set multiple properties at once
unity-mcp component modify "Cube" Rigidbody --properties '{"mass": 5, "drag": 0.5}'
```

### Asset Commands

```bash
# Search assets
unity-mcp asset search "Player"
unity-mcp asset search "t:Material"        # By type
unity-mcp asset search "t:Prefab Player"   # Combined

# Get asset info
unity-mcp asset info "Assets/Materials/Red.mat"

# Create asset
unity-mcp asset create "Assets/Materials/New.mat" Material

# Delete asset
unity-mcp asset delete "Assets/Materials/Old.mat"
unity-mcp asset delete "Assets/Materials/Old.mat" --force  # Skip confirmation

# Move/Rename asset
unity-mcp asset move "Assets/Old/Mat.mat" "Assets/New/Mat.mat"
unity-mcp asset rename "Assets/Materials/Old.mat" "New"

# Create folder
unity-mcp asset mkdir "Assets/NewFolder"

# Import/reimport
unity-mcp asset import "Assets/Textures/image.png"
```

### Script Commands

```bash
# Create script
unity-mcp script create "MyScript" --path "Assets/Scripts"
unity-mcp script create "MyScript" --path "Assets/Scripts" --type MonoBehaviour

# Read script
unity-mcp script read "Assets/Scripts/MyScript.cs"

# Delete script
unity-mcp script delete "Assets/Scripts/MyScript.cs"

# Validate script
unity-mcp script validate "Assets/Scripts/MyScript.cs"
```

### Shader Commands

```bash
# Create shader
unity-mcp shader create "MyShader" --path "Assets/Shaders"

# Read shader
unity-mcp shader read "Assets/Shaders/Custom.shader"

# Update from file
unity-mcp shader update "Assets/Shaders/Custom.shader" --file local.shader

# Delete shader
unity-mcp shader delete "Assets/Shaders/Old.shader" --force
```

### Material Commands

```bash
# Create material
unity-mcp material create "Assets/Materials/New.mat"
unity-mcp material create "Assets/Materials/New.mat" --shader "Standard"

# Get material info
unity-mcp material info "Assets/Materials/Mat.mat"

# Set color (R G B or R G B A)
unity-mcp material set-color "Assets/Materials/Mat.mat" 1 0 0
unity-mcp material set-color "Assets/Materials/Mat.mat" 1 0 0 --property "_BaseColor"

# Set shader property
unity-mcp material set-property "Assets/Materials/Mat.mat" "_Metallic" 0.5

# Assign to GameObject
unity-mcp material assign "Assets/Materials/Mat.mat" "Cube"
unity-mcp material assign "Assets/Materials/Mat.mat" "Cube" --slot 1

# Set renderer color directly
unity-mcp material set-renderer-color "Cube" 1 0 0 1
```

### Editor Commands

```bash
# Play mode control
unity-mcp editor play
unity-mcp editor pause
unity-mcp editor stop

# Console
unity-mcp editor console                    # Read console
unity-mcp editor console --count 20         # Last 20 entries
unity-mcp editor console --clear            # Clear console
unity-mcp editor console --type error --type warning  # Filter: error, warning, log or all

# Menu items
unity-mcp editor menu "Edit/Preferences"
unity-mcp editor menu "GameObject/Create Empty"

# Tags and Layers
unity-mcp editor add-tag "Enemy"
unity-mcp editor remove-tag "Enemy"
unity-mcp editor add-layer "Interactable"
unity-mcp editor remove-layer "Interactable"

# Editor tool
unity-mcp editor tool View
unity-mcp editor tool Move
unity-mcp editor tool Rotate

# Run tests
unity-mcp editor tests
unity-mcp editor tests --mode PlayMode
```

### Custom Tools

```bash
# List custom tools / default tools for the active Unity project
unity-mcp tool list
unity-mcp custom_tool list

# Execute a custom tool by name
unity-mcp editor custom-tool "MyBuildTool"
unity-mcp editor custom-tool "Deploy" --params '{"target": "Android"}'
```

### VFX Commands

VFX Graph tooling is tested against com.unity.visualeffectgraph 12.1.13 with URP/HDRP; other versions may be unsupported.

```bash
# Particle systems
unity-mcp vfx particle info "Fire"
unity-mcp vfx particle play "Fire" --with-children
unity-mcp vfx particle stop "Fire"

# Line renderers
unity-mcp vfx line info "LaserBeam"
unity-mcp vfx line create-line "Line" --start 0 0 0 --end 10 5 0
unity-mcp vfx line create-circle "Circle" --radius 5

# Trail renderers
unity-mcp vfx trail info "PlayerTrail"
unity-mcp vfx trail set-time "Trail" 2.0

# Raw VFX actions (access all 60+ actions)
unity-mcp vfx raw particle_set_main "Fire" --params '{"duration": 5}'
```

### ProBuilder Commands

Requires the com.unity.probuilder package in your Unity project.

```bash
# Create shapes
unity-mcp probuilder create-shape Cube
unity-mcp probuilder create-shape Torus --name "MyTorus" --params '{"rows": 16, "columns": 16}'
unity-mcp probuilder create-shape Stair --position 0 0 5 --params '{"steps": 10}'

# Create from polygon footprint
unity-mcp probuilder create-poly --points "[[0,0,0],[5,0,0],[5,0,5],[0,0,5]]" --height 3

# Get mesh info
unity-mcp probuilder info "MyCube"

# Raw ProBuilder actions
unity-mcp probuilder raw extrude_faces "MyCube" --params '{"faceIndices": [0], "distance": 1.0}'
unity-mcp probuilder raw bevel_edges "MyCube" --params '{"edgeIndices": [0,1], "amount": 0.2}'
unity-mcp probuilder raw set_face_material "MyCube" --params '{"faceIndices": [0], "materialPath": "Assets/Materials/Red.mat"}'
```

### Batch Commands

```bash
# Execute from JSON file
unity-mcp batch run commands.json
unity-mcp batch run commands.json --parallel --fail-fast

# Execute inline JSON
unity-mcp batch inline '[{"tool": "manage_scene", "params": {"action": "get_active"}}]'

# Generate template
unity-mcp batch template > my_commands.json
```

### Prefab Commands

```bash
# Create prefab from scene object
unity-mcp prefab create "Cube" "Assets/Prefabs/Cube.prefab"
unity-mcp prefab create "Cube" "Assets/Prefabs/Cube.prefab" --overwrite

# Open prefab for editing
unity-mcp prefab open "Assets/Prefabs/Player.prefab"

# Save open prefab
unity-mcp prefab save

# Close prefab stage
unity-mcp prefab close

# Modify prefab contents (headless)
unity-mcp prefab modify "Assets/Prefabs/Player.prefab" --target Weapon --position "0,1,2"
unity-mcp prefab modify "Assets/Prefabs/Player.prefab" --delete-child Child1 --delete-child "Turret/Barrel"
unity-mcp prefab modify "Assets/Prefabs/Player.prefab" --set-property "Rigidbody.mass=5"
unity-mcp prefab modify "Assets/Prefabs/Player.prefab" --add-component BoxCollider --remove-component SphereCollider
unity-mcp prefab modify "Assets/Prefabs/Player.prefab" --create-child '{"name":"Spawn","primitive_type":"Sphere"}'
unity-mcp prefab modify "Assets/Prefabs/Player.prefab" --name NewName --tag Player --layer UI
unity-mcp prefab modify "Assets/Prefabs/Player.prefab" --inactive
```

### UI Commands

```bash
# Create a Canvas (adds Canvas, CanvasScaler, GraphicRaycaster)
unity-mcp ui create-canvas "MainCanvas"
unity-mcp ui create-canvas "WorldUI" --render-mode WorldSpace

# Create UI elements (must have a parent Canvas)
unity-mcp ui create-text "TitleText" --parent "MainCanvas" --text "Hello World"
unity-mcp ui create-button "StartButton" --parent "MainCanvas" --text "Click Me"
unity-mcp ui create-image "Background" --parent "MainCanvas"
```

### Lighting Commands

```bash
# Create lights with type, color, intensity
unity-mcp lighting create "Sun" --type Directional
unity-mcp lighting create "Lamp" --type Point --intensity 2 --position 0 5 0
unity-mcp lighting create "Spot" --type Spot --color 1 0 0 --intensity 3
unity-mcp lighting create "GreenLight" --type Point --color 0 1 0
```

### Audio Commands

```bash
# Control AudioSource (target must have AudioSource component)
unity-mcp audio play "MusicPlayer"
unity-mcp audio stop "MusicPlayer"
unity-mcp audio volume "MusicPlayer" 0.5
```

### Animation Commands

```bash
# Control Animator (target or one of its children must have an Animator component)
unity-mcp animation animator play "Character" "Walk"
unity-mcp animation animator set-parameter "Character" "Speed" 1.5 --type float
unity-mcp animation animator set-parameter "Character" "IsRunning" true --type bool
unity-mcp animation animator set-parameter "Character" "Jump" "" --type trigger

# Clips and controllers
unity-mcp animation clip create "Assets/Animations/Bounce.anim" --length 2.0 --loop
unity-mcp animation clip info "Assets/Animations/Walk.anim"
unity-mcp animation controller info "Assets/Animations/Player.controller"
```

### Camera Commands

```bash
# Check Cinemachine availability
unity-mcp camera ping

# List all cameras in scene
unity-mcp camera list

# Create cameras (plain or with Cinemachine presets)
unity-mcp camera create                                     # Basic camera
unity-mcp camera create --name "FollowCam" --preset follow --follow "Player" --look-at "Player"
unity-mcp camera create --preset third_person --follow "Player" --fov 50
unity-mcp camera create --preset dolly --look-at "Player"
unity-mcp camera create --preset top_down --follow "Player"
unity-mcp camera create --preset side_scroller --follow "Player"
unity-mcp camera create --preset static --fov 40

# Set targets on existing camera
unity-mcp camera set-target "FollowCam" --follow "Player" --look-at "Enemy"

# Lens settings
unity-mcp camera set-lens "MainCam" --fov 60 --near 0.1 --far 1000
unity-mcp camera set-lens "OrthoCamera" --ortho-size 10

# Priority (higher = preferred by CinemachineBrain)
unity-mcp camera set-priority "FollowCam" --priority 15

# Cinemachine Body/Aim/Noise configuration
unity-mcp camera set-body "FollowCam" --body-type "CinemachineFollow"
unity-mcp camera set-body "FollowCam" --body-type "CinemachineFollow" --props '{"TrackerSettings": {"BindingMode": 1}}'
unity-mcp camera set-aim "FollowCam" --aim-type "CinemachineRotationComposer"
unity-mcp camera set-noise "FollowCam" --amplitude 1.5 --frequency 0.5

# Extensions
unity-mcp camera add-extension "FollowCam" CinemachineConfiner3D
unity-mcp camera remove-extension "FollowCam" CinemachineConfiner3D

# Brain (ensure Brain exists on main camera, set default blend)
unity-mcp camera ensure-brain
unity-mcp camera ensure-brain --blend-style "EaseInOut" --blend-duration 1.5
unity-mcp camera brain-status
unity-mcp camera set-blend --style "Cut" --duration 0

# Force/release camera override
unity-mcp camera force "FollowCam"
unity-mcp camera release

# Screenshots
unity-mcp camera screenshot
unity-mcp camera screenshot --file-name "my_capture" --super-size 2
unity-mcp camera screenshot --camera-ref "SecondCamera" --include-image
unity-mcp camera screenshot --max-resolution 256
unity-mcp camera screenshot --batch surround --max-resolution 256
unity-mcp camera screenshot --batch orbit --view-target "Player"
unity-mcp camera screenshot --capture-source scene_view --view-target "Canvas" --include-image
unity-mcp camera screenshot-multiview --view-target "Player" --max-resolution 480
```

### Graphics Commands

```bash
# Check graphics system status
unity-mcp graphics ping

# --- Volumes ---
# Create a Volume (global or local)
unity-mcp graphics volume-create --name "PostProcessing" --global
unity-mcp graphics volume-create --name "LocalFog" --local --weight 0.8 --priority 1

# Add/remove/configure effects on a Volume
unity-mcp graphics volume-add-effect --target "PostProcessing" --effect "Bloom"
unity-mcp graphics volume-set-effect --target "PostProcessing" --effect "Bloom" -p intensity 1.5 -p threshold 0.9
unity-mcp graphics volume-remove-effect --target "PostProcessing" --effect "Bloom"
unity-mcp graphics volume-info --target "PostProcessing"
unity-mcp graphics volume-set-properties --target "PostProcessing" --weight 0.5 --priority 2 --local
unity-mcp graphics volume-list-effects
unity-mcp graphics volume-create-profile --path "Assets/Profiles/MyProfile.asset" --name "MyProfile"

# --- Render Pipeline ---
unity-mcp graphics pipeline-info
unity-mcp graphics pipeline-settings
unity-mcp graphics pipeline-set-quality --level "High"
unity-mcp graphics pipeline-set-settings -s renderScale 1.5 -s msaaSampleCount 4

# --- Light Baking ---
unity-mcp graphics bake-start
unity-mcp graphics bake-start --sync               # Wait for completion
unity-mcp graphics bake-status
unity-mcp graphics bake-cancel
unity-mcp graphics bake-clear
unity-mcp graphics bake-settings
unity-mcp graphics bake-set-settings -s lightmapResolution 64 -s directSamples 32
unity-mcp graphics bake-reflection-probe --target "ReflectionProbe1"
unity-mcp graphics bake-create-probes --name "LightProbes" --spacing 5
unity-mcp graphics bake-create-reflection --name "ReflProbe" --resolution 512 --mode Realtime

# --- Rendering Stats ---
unity-mcp graphics stats
unity-mcp graphics stats-memory
unity-mcp graphics stats-debug-mode --mode "Wireframe"

# --- URP Renderer Features ---
unity-mcp graphics feature-list
unity-mcp graphics feature-add --type "ScreenSpaceAmbientOcclusion" --name "SSAO"
unity-mcp graphics feature-remove --name "SSAO"
unity-mcp graphics feature-configure --name "SSAO" -p Intensity 1.5 -p Radius 0.3
unity-mcp graphics feature-reorder --order "0,2,1,3"
unity-mcp graphics feature-toggle --name "SSAO" --active
unity-mcp graphics feature-toggle --name "SSAO" --inactive

# --- Skybox & Environment ---
unity-mcp graphics skybox-info
unity-mcp graphics skybox-set-material --material "Assets/Materials/NightSky.mat"
unity-mcp graphics skybox-set-properties -p _Tint "0.5,0.5,1,1" -p _Exposure 1.2
unity-mcp graphics skybox-set-ambient --mode Flat --color "0.2,0.2,0.3"
unity-mcp graphics skybox-set-ambient --mode Trilight --color "0.4,0.6,0.8" --equator-color "0.3,0.3,0.3" --ground-color "0.1,0.1,0.1"
unity-mcp graphics skybox-set-fog --enable --mode ExponentialSquared --color "0.7,0.8,0.9" --density 0.02
unity-mcp graphics skybox-set-fog --disable
unity-mcp graphics skybox-set-reflection --intensity 1.0 --bounces 2 --mode Custom --resolution 256
unity-mcp graphics skybox-set-sun --target "DirectionalLight"
```

### Package Commands

```bash
# Check package manager status
unity-mcp packages ping

# List installed packages
unity-mcp packages list

# Search Unity registry
unity-mcp packages search "cinemachine"
unity-mcp packages search "probuilder"

# Get package details
unity-mcp packages info "com.unity.cinemachine"

# Install / remove packages
unity-mcp packages add "com.unity.cinemachine"
unity-mcp packages add "com.unity.cinemachine@4.1.1"
unity-mcp packages remove "com.unity.cinemachine"
unity-mcp packages remove "com.unity.cinemachine" --force    # Skip confirmation

# Embed package for local editing
unity-mcp packages embed "com.unity.cinemachine"

# Force package re-resolution
unity-mcp packages resolve

# Check async operation status
unity-mcp packages status <job_id>

# Scoped registries
unity-mcp packages list-registries
unity-mcp packages add-registry "My Registry" --url "https://registry.example.com" -s "com.example"
unity-mcp packages remove-registry "My Registry"
```

### Texture Commands

```bash
# Create procedural textures
unity-mcp texture create "Assets/Textures/Red.png" --width 128 --height 128 --color "1,0,0,1"
unity-mcp texture create "Assets/Textures/Check.png" --pattern checkerboard --palette "1,0,0,1;0,0,1,1"
unity-mcp texture create "Assets/Textures/Brick.png" --width 256 --height 256 --pattern brick
unity-mcp texture create "Assets/Textures/Grid.png" --pattern grid --width 512 --height 512

# Available patterns: checkerboard, stripes, stripes_h, stripes_v, stripes_diag, dots, grid, brick

# Create from image file
unity-mcp texture create "Assets/Textures/Photo.png" --image-path "/path/to/source.png"

# Create with custom import settings
unity-mcp texture create "Assets/Textures/Normal.png" --import-settings '{"textureType": "NormalMap", "filterMode": "Trilinear"}'

# Create sprites (auto-configures import settings for 2D)
unity-mcp texture sprite "Assets/Sprites/Player.png" --width 32 --height 32 --color "0,0.5,1,1"
unity-mcp texture sprite "Assets/Sprites/Tile.png" --pattern checkerboard --ppu 16 --pivot "0.5,0"

# Modify existing texture pixels
unity-mcp texture modify "Assets/Textures/Existing.png" --set-pixels '{"x":0,"y":0,"width":16,"height":16,"color":[1,0,0,1]}'

# Delete texture
unity-mcp texture delete "Assets/Textures/Old.png"
unity-mcp texture delete "Assets/Textures/Old.png" --force
```

### Sprite Commands

```bash
# Read a sheet's size, import settings and slices (the image itself is the file)
unity-mcp sprite info "Assets/Sprites/Hero.png"

# Slice into a grid: --cols/--rows, or --frame-width/--frame-height
unity-mcp sprite slice "Assets/Sprites/Hero.png" --cols 6 --rows 4

# High-resolution art: --filter-mode bilinear or trilinear (the default, point, suits pixel art)
unity-mcp sprite slice "Assets/Sprites/Painted.png" --cols 8 --filter-mode bilinear

# Clips from the slices, then a controller from the clips
unity-mcp sprite setup-clips "Assets/Sprites/Hero.png" --clips '[{"name": "walk", "start_frame": 0, "end_frame": 5}]'
unity-mcp sprite setup-controller "Assets/Animators/Hero.controller" --clips '[{"name": "walk", "path": "Assets/Sprites/walk.anim"}]'

# All of it in one step (--add-to-scene needs --scene-target, an existing GameObject)
unity-mcp sprite full-setup "Assets/Sprites/Hero.png" --cols 6 --rows 4 --clips '[{"name": "idle", "start_frame": 0, "end_frame": 5}, {"name": "walk", "start_frame": 6, "end_frame": 11}]' --controller-path "Assets/Animators/Hero.controller" --add-to-scene --scene-target Hero
```

### Code Commands

```bash
# Read source files
unity-mcp code read "Assets/Scripts/Player.cs"
unity-mcp code read "Assets/Scripts/Player.cs" --start-line 10 --line-count 20

# Search with regex
unity-mcp code search "class.*Player" "Assets/Scripts/Player.cs"
unity-mcp code search "TODO|FIXME" "Assets/Scripts/Utils.cs"
unity-mcp code search "void Update" "Assets/Scripts/Game.cs" --max-results 20
```

### Build Commands

```bash
unity-mcp build platform                       # Read the active platform
unity-mcp build platform android               # Switch platform
unity-mcp build scenes                         # Read the build scene list
unity-mcp build run --target windows64 --development
unity-mcp build batch --targets windows64,linux64,webgl
unity-mcp build status                         # Last build report
unity-mcp build settings product_name --value "My Game"
```

### Physics Commands

```bash
unity-mcp physics ping                         # Physics system status
unity-mcp physics get-settings
unity-mcp physics raycast --origin "0,5,0" --direction "0,-1,0" --max-distance 10
unity-mcp physics overlap --shape sphere --position "0,0,0" --size 2
unity-mcp physics get-rigidbody "Player"
unity-mcp physics simulate --steps 10          # Step physics in edit mode
unity-mcp physics validate                     # Check the scene for common mistakes
```

### Profiler Commands

```bash
unity-mcp profiler start                       # Optionally --log-file to record a .raw
unity-mcp profiler status
unity-mcp profiler frame-timing
unity-mcp profiler get-counters --category Render
unity-mcp profiler memory-snapshot             # Requires com.unity.memoryprofiler
unity-mcp profiler stop
```

### Reflection and Docs Commands

```bash
unity-mcp reflect search NavMesh               # Find Unity types by name
unity-mcp reflect type NavMeshAgent            # Member summary
unity-mcp reflect member Physics Raycast       # One member in detail
unity-mcp docs get Physics Raycast             # Unity documentation page
```

### Asset Generation and Blender Commands

```bash
unity-mcp asset-gen list-models --kind image
unity-mcp asset-gen generate-image --provider fal --prompt "a stone texture"
unity-mcp asset-gen generate-model --provider tripo --mode text --prompt "a red chair"
unity-mcp asset-gen status --job-id abc123
unity-mcp asset-gen import-model-file --source-path "C:/exports/house.fbx" --output-folder Assets/Models
unity-mcp blender status                       # Is Blender reachable?
unity-mcp blender import-model --selection-only --target-size 2
```

### Raw Commands

For advanced usage, send raw tool calls:

```bash
# Send any MCP tool directly
unity-mcp raw manage_scene '{"action": "get_active"}'
unity-mcp raw manage_gameobject '{"action": "create", "name": "Test"}'
unity-mcp raw manage_components '{"action": "add", "target": "Test", "componentType": "Rigidbody"}'
unity-mcp raw manage_editor '{"action": "play"}'
unity-mcp raw manage_camera '{"action": "screenshot", "include_image": true}'
unity-mcp raw manage_graphics '{"action": "volume_get_info", "target": "PostProcessing"}'
unity-mcp raw manage_packages '{"action": "list_packages"}'
```

---

## Complete Command Reference

| Group | Subcommands |
|-------|-------------|
| (top level) | `instances`, `raw`, `status` |
| `animation` | `raw` |
| `animation animator` | `crossfade`, `get-parameter`, `info`, `play`, `set-enabled`, `set-parameter`, `set-speed` |
| `animation clip` | `add-curve`, `add-event`, `assign`, `create`, `create-preset`, `info`, `remove-event`, `set-curve`, `set-vector-curve` |
| `animation controller` | `add-blend-tree-child`, `add-layer`, `add-parameter`, `add-state`, `add-transition`, `assign`, `create`, `create-blend-tree-1d`, `create-blend-tree-2d`, `info`, `remove-layer`, `set-layer-weight` |
| `asset` | `create`, `delete`, `duplicate`, `import`, `info`, `mkdir`, `move`, `rename`, `search` |
| `asset-gen` | `generate-audio`, `generate-image`, `generate-model`, `import-model`, `import-model-file`, `list-models`, `status` |
| `audio` | `play`, `stop`, `volume` |
| `batch` | `inline`, `run`, `template` |
| `blender` | `check-updates`, `compare-screenshot`, `import-model`, `object-info`, `run-python`, `scene-info`, `screenshot`, `setup-bloom`, `status`, `sync-addon` |
| `build` | `batch`, `cancel`, `platform`, `profiles`, `run`, `scenes`, `settings`, `status` |
| `camera` | `add-extension`, `brain-status`, `create`, `ensure-brain`, `force`, `list`, `ping`, `release`, `remove-extension`, `screenshot`, `screenshot-multiview`, `set-aim`, `set-blend`, `set-body`, `set-lens`, `set-noise`, `set-priority`, `set-target` |
| `code` | `clear-history`, `execute`, `history`, `read`, `replay`, `search` |
| `component` | `add`, `modify`, `remove`, `set` |
| `custom_tool` | `list` |
| `docs` | `get` |
| `editor` | `add-layer`, `add-tag`, `console`, `custom-tool`, `deploy`, `menu`, `pause`, `play`, `poll-test`, `redo`, `refresh`, `remove-layer`, `remove-tag`, `restore`, `stop`, `tests`, `tool`, `undo` |
| `gameobject` | `create`, `delete`, `duplicate`, `find`, `modify`, `move` |
| `graphics` | `bake-cancel`, `bake-clear`, `bake-create-probes`, `bake-create-reflection`, `bake-reflection-probe`, `bake-set-settings`, `bake-settings`, `bake-start`, `bake-status`, `feature-add`, `feature-configure`, `feature-list`, `feature-remove`, `feature-reorder`, `feature-toggle`, `ping`, `pipeline-info`, `pipeline-set-quality`, `pipeline-set-settings`, `pipeline-settings`, `skybox-info`, `skybox-set-ambient`, `skybox-set-fog`, `skybox-set-material`, `skybox-set-properties`, `skybox-set-reflection`, `skybox-set-sun`, `stats`, `stats-debug-mode`, `stats-memory`, `volume-add-effect`, `volume-create`, `volume-create-profile`, `volume-info`, `volume-list-effects`, `volume-remove-effect`, `volume-set-effect`, `volume-set-properties` |
| `instance` | `current`, `list` |
| `lighting` | `create` |
| `material` | `assign`, `create`, `info`, `set-color`, `set-property`, `set-renderer-color` |
| `packages` | `add`, `add-registry`, `embed`, `info`, `list`, `list-registries`, `ping`, `remove`, `remove-registry`, `resolve`, `search`, `status` |
| `physics` | `add-joint`, `apply-force`, `assign-material`, `configure-joint`, `configure-material`, `configure-rigidbody`, `create-material`, `get-collision-matrix`, `get-rigidbody`, `get-settings`, `linecast`, `overlap`, `ping`, `raycast`, `raycast-all`, `remove-joint`, `set-collision-matrix`, `set-settings`, `shapecast`, `simulate`, `validate` |
| `prefab` | `close`, `create`, `hierarchy`, `info`, `modify`, `open`, `save` |
| `probuilder` | `auto-smooth`, `bevel-edges`, `center-pivot`, `create-poly`, `create-shape`, `delete-faces`, `extrude-edges`, `extrude-faces`, `freeze-transform`, `info`, `move-vertices`, `raw`, `repair`, `select-faces`, `set-material`, `set-pivot`, `set-smoothing`, `subdivide`, `validate`, `weld-vertices` |
| `profiler` | `frame-debugger-disable`, `frame-debugger-enable`, `frame-debugger-events`, `frame-timing`, `get-counters`, `memory-compare`, `memory-list`, `memory-snapshot`, `object-memory`, `set-areas`, `start`, `status`, `stop` |
| `reflect` | `member`, `search`, `type` |
| `scene` | `active`, `build-settings`, `close`, `create`, `hierarchy`, `load`, `loaded`, `move-to`, `open-additive`, `save`, `set-active`, `validate` |
| `script` | `create`, `delete`, `edit`, `read`, `validate` |
| `shader` | `create`, `delete`, `read`, `update` |
| `sprite` | `full-setup`, `info`, `setup-clips`, `setup-controller`, `slice` |
| `texture` | `create`, `delete`, `modify`, `set-import-settings`, `sprite` |
| `tool` | `list` |
| `ui` | `create-button`, `create-canvas`, `create-image`, `create-text` |
| `vfx` | `raw` |
| `vfx line` | `clear`, `create-circle`, `create-line`, `info`, `set-positions` |
| `vfx particle` | `clear`, `info`, `pause`, `play`, `restart`, `stop` |
| `vfx trail` | `clear`, `info`, `set-time` |

---

## Known Behaviors

### Component Creation

When creating GameObjects with components, the CLI creates the object first, then adds components separately. This is the correct workflow for Unity MCP.

```bash
# This works correctly - creates object then adds components
unity-mcp gameobject create "Player" --components "Rigidbody,BoxCollider"

# Equivalent to:
unity-mcp gameobject create "Player"
unity-mcp component add "Player" Rigidbody
unity-mcp component add "Player" BoxCollider
```

### Light Creation

The `lighting create` command creates a complete light with the specified type, color, and intensity:

```bash
# Creates Point light with green color and intensity 5
unity-mcp lighting create "GreenLight" --type Point --color 0 1 0 --intensity 5
```

### UI Element Creation

UI commands automatically add the required components:

```bash
# create-canvas adds: Canvas, CanvasScaler, GraphicRaycaster
unity-mcp ui create-canvas "MainUI"

# create-button adds: Image, Button
unity-mcp ui create-button "MyButton" --parent "MainUI"
```

---

## Quick Reference Card

### Multi-Value Syntax

```bash
--position X Y Z      # not "X,Y,Z"
--rotation X Y Z      # not "X,Y,Z"
--scale X Y Z         # not "X,Y,Z"
--color R G B         # not "R,G,B"
```

### Argument Order (check --help)

```bash
material assign MATERIAL_PATH TARGET
prefab create TARGET PATH
component set TARGET COMPONENT PROPERTY VALUE
```

### Search Methods

```bash
--method by_name      # default for gameobject find
--method by_id
--method by_path
--method by_tag
--method by_component
```

### Global Options Position

```bash
unity-mcp [GLOBAL_OPTIONS] command subcommand [ARGS] [OPTIONS]
#         ^^^^^^^^^^^^^^^^
#         Must come BEFORE command!
```

---

## Debugging Tips

1. **Always check `--help`** for any command:

   ```bash
   unity-mcp gameobject --help
   unity-mcp gameobject modify --help
   ```

2. **Use verbose mode** to see each command sent to Unity and its raw response (on stderr):

   ```bash
   unity-mcp -v scene hierarchy
   ```

3. **Use JSON output** for programmatic parsing:

   ```bash
   unity-mcp -f json gameobject find "Player" | jq '.result'
   ```

4. **Check connection first**:

   ```bash
   unity-mcp status
   ```

5. **When in doubt about properties**, use info commands:

   ```bash
   unity-mcp material info "Assets/Materials/Mat.mat"
   unity-mcp asset info "Assets/Prefabs/Player.prefab"
   ```
