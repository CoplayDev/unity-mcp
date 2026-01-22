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
unity-mcp scene screenshot

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
| `--verbose` | `-v` | Enable verbose output | `false` |

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

# By instance ID (use --search-method)
unity-mcp gameobject modify "-81840" --search-method by_id --position 0 0 0

# By path
unity-mcp gameobject modify "/Canvas/Panel/Button" --search-method by_path --active

# By tag
unity-mcp gameobject find "Player" --search-method by_tag
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

# Take screenshot
unity-mcp scene screenshot
unity-mcp scene screenshot --filename "my_screenshot" --supersize 2
```

### GameObject Commands

```bash
# Find GameObjects
unity-mcp gameobject find "Player"
unity-mcp gameobject find "Enemy" --method by_tag
unity-mcp gameobject find "-81840" --method by_id
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
unity-mcp editor console --types error,warning  # Filter by type

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
# Control Animator (target must have Animator component)
unity-mcp animation play "Character" "Walk"
unity-mcp animation set-parameter "Character" "Speed" 1.5 --type float
unity-mcp animation set-parameter "Character" "IsRunning" true --type bool
unity-mcp animation set-parameter "Character" "Jump" "" --type trigger
```

### Code Commands

```bash
# Read source files
unity-mcp code read "Assets/Scripts/Player.cs"
unity-mcp code read "Assets/Scripts/Player.cs" --start-line 10 --line-count 20
```

### Raw Commands

For advanced usage, send raw tool calls:

```bash
# Send any MCP tool directly
unity-mcp raw manage_scene '{"action": "get_active"}'
unity-mcp raw manage_gameobject '{"action": "create", "name": "Test"}'
unity-mcp raw manage_components '{"action": "add", "target": "Test", "componentType": "Rigidbody"}'
unity-mcp raw manage_editor '{"action": "play"}'
```

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

2. **Use verbose mode** to see what's happening:

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
