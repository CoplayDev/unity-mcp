# Unity MCP CLI Usage Guide

The Unity MCP CLI provides command-line access to control the Unity Editor through the Model Context Protocol. It currently only supports local HTTP.

Note: Some tools are still experimental and might fail under some circumstances. Please submit an issue to help us make it better.

## Installation

```bash
cd Server
pip install -e .
# Or with uv:
uv pip install -e .
```

## Quick Start

```bash
# Check connection
unity-mcp status

# List Unity instances
unity-mcp instance list

# Get scene hierarchy
unity-mcp scene hierarchy

# Find a GameObject
unity-mcp gameobject find "Player"
```

## Global Options

| Option | Env Variable | Description |
|--------|--------------|-------------|
| `-h, --host` | `UNITY_MCP_HOST` | Server host (default: 127.0.0.1) |
| `-p, --port` | `UNITY_MCP_HTTP_PORT` | Server port (default: 8080) |
| `-t, --timeout` | `UNITY_MCP_TIMEOUT` | Timeout in seconds (default: 30) |
| `-f, --format` | `UNITY_MCP_FORMAT` | Output format: text, json, table |
| `-i, --instance` | `UNITY_MCP_INSTANCE` | Target Unity instance |

## Command Reference

### Instance Management

```bash
# List connected Unity instances
unity-mcp instance list

# Set active instance
unity-mcp instance set "ProjectName@abc123"

# Show current instance
unity-mcp instance current
```

### Scene Operations

```bash
# Get scene hierarchy
unity-mcp scene hierarchy
unity-mcp scene hierarchy --limit 20 --depth 3

# Get active scene info
unity-mcp scene active

# Load/save scenes
unity-mcp scene load "Assets/Scenes/Main.unity"
unity-mcp scene save

# Take screenshot
unity-mcp scene screenshot --name "capture"
```

### GameObject Operations

```bash
# Find GameObjects
unity-mcp gameobject find "Player"
unity-mcp gameobject find "Enemy" --method by_tag

# Create GameObjects
unity-mcp gameobject create "NewCube" --primitive Cube
unity-mcp gameobject create "Empty" --position 0 5 0

# Modify GameObjects
unity-mcp gameobject modify "Cube" --position 1 2 3 --rotation 0 45 0

# Delete/duplicate
unity-mcp gameobject delete "OldObject" --force
unity-mcp gameobject duplicate "Template"
```

### Component Operations

```bash
# Add component
unity-mcp component add "Player" Rigidbody

# Remove component
unity-mcp component remove "Player" Rigidbody

# Set property
unity-mcp component set "Player" Rigidbody mass 10
```

### Script Operations

```bash
# Create script
unity-mcp script create "PlayerController" --path "Assets/Scripts"

# Read script
unity-mcp script read "Assets/Scripts/Player.cs"

# Delete script
unity-mcp script delete "Assets/Scripts/Old.cs" --force
```

### Code Search

```bash
# Search with regex
unity-mcp code search "class.*Player" "Assets/Scripts/Player.cs"
unity-mcp code search "TODO|FIXME" "Assets/Scripts/Utils.cs"
unity-mcp code search "void Update" "Assets/Scripts/Game.cs" --max-results 20
```

### Shader Operations

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

### Editor Controls

```bash
# Play mode
unity-mcp editor play
unity-mcp editor pause
unity-mcp editor stop

# Refresh assets
unity-mcp editor refresh
unity-mcp editor refresh --compile

# Console
unity-mcp editor console
unity-mcp editor console --clear

# Tags and layers
unity-mcp editor add-tag "Enemy"
unity-mcp editor add-layer "Projectiles"

# Menu items
unity-mcp editor menu "Edit/Project Settings..."

# Custom tools
unity-mcp editor custom-tool "MyBuildTool"
unity-mcp editor custom-tool "Deploy" --params '{"target": "Android"}'
```

### Testing

```bash
# Run tests synchronously
unity-mcp editor tests --mode EditMode

# Run tests asynchronously
unity-mcp editor tests --mode PlayMode --async

# Poll test job
unity-mcp editor poll-test <job_id>
unity-mcp editor poll-test <job_id> --wait 60 --details
```

### Material Operations

```bash
# Create material
unity-mcp material create "Assets/Materials/Red.mat"

# Set color
unity-mcp material set-color "Assets/Materials/Red.mat" 1 0 0

# Assign to object
unity-mcp material assign "Assets/Materials/Red.mat" "Cube"
```

### VFX Operations

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

### Batch Operations

```bash
# Execute from JSON file
unity-mcp batch run commands.json
unity-mcp batch run commands.json --parallel --fail-fast

# Execute inline JSON
unity-mcp batch inline '[{"tool": "manage_scene", "params": {"action": "get_active"}}]'

# Generate template
unity-mcp batch template > my_commands.json
```

### Prefab Operations

```bash
# Open prefab for editing
unity-mcp prefab open "Assets/Prefabs/Player.prefab"

# Save and close
unity-mcp prefab save
unity-mcp prefab close

# Create from GameObject
unity-mcp prefab create "Player" --path "Assets/Prefabs"
```

### Asset Operations

```bash
# Search assets
unity-mcp asset search --pattern "*.mat" --path "Assets/Materials"

# Get asset info
unity-mcp asset info "Assets/Materials/Red.mat"

# Create folder
unity-mcp asset mkdir "Assets/NewFolder"

# Move/rename
unity-mcp asset move "Assets/Old.mat" "Assets/Materials/"
```

### Animation Operations

```bash
# Play animation state
unity-mcp animation play "Player" "Run"

# Set animator parameter
unity-mcp animation set-parameter "Player" Speed 1.5
unity-mcp animation set-parameter "Player" IsRunning true
```

### Audio Operations

```bash
# Play audio
unity-mcp audio play "AudioPlayer"

# Stop audio
unity-mcp audio stop "AudioPlayer"

# Set volume
unity-mcp audio volume "AudioPlayer" 0.5
```

### Lighting Operations

```bash
# Create light
unity-mcp lighting create "NewLight" --type Point --position 0 5 0
unity-mcp lighting create "Spotlight" --type Spot --intensity 2
```

### UI Operations

```bash
# Create canvas
unity-mcp ui create-canvas "MainCanvas"

# Create text
unity-mcp ui create-text "Title" --parent "MainCanvas" --text "Hello World"

# Create button
unity-mcp ui create-button "StartBtn" --parent "MainCanvas" --text "Start"

# Create image
unity-mcp ui create-image "Background" --parent "MainCanvas"
```

### Raw Commands

For any MCP tool not covered by dedicated commands:

```bash
unity-mcp raw manage_scene '{"action": "get_hierarchy", "max_nodes": 100}'
unity-mcp raw read_console '{"count": 20}'
```

---

## Complete Command Reference

| Group | Subcommands |
|-------|-------------|
| `instance` | `list`, `set`, `current` |
| `scene` | `hierarchy`, `active`, `load`, `save`, `create`, `screenshot`, `build-settings` |
| `gameobject` | `find`, `create`, `modify`, `delete`, `duplicate`, `move` |
| `component` | `add`, `remove`, `set`, `modify` |
| `script` | `create`, `read`, `delete`, `edit`, `validate` |
| `code` | `read`, `search` |
| `shader` | `create`, `read`, `update`, `delete` |
| `editor` | `play`, `pause`, `stop`, `refresh`, `console`, `menu`, `tool`, `add-tag`, `remove-tag`, `add-layer`, `remove-layer`, `tests`, `poll-test`, `custom-tool` |
| `asset` | `search`, `info`, `create`, `delete`, `duplicate`, `move`, `rename`, `import`, `mkdir` |
| `prefab` | `open`, `close`, `save`, `create` |
| `material` | `info`, `create`, `set-color`, `set-property`, `assign`, `set-renderer-color` |
| `vfx particle` | `info`, `play`, `stop`, `pause`, `restart`, `clear` |
| `vfx line` | `info`, `set-positions`, `create-line`, `create-circle`, `clear` |
| `vfx trail` | `info`, `set-time`, `clear` |
| `vfx` | `raw` (access all 60+ actions) |
| `batch` | `run`, `inline`, `template` |
| `animation` | `play`, `set-parameter` |
| `audio` | `play`, `stop`, `volume` |
| `lighting` | `create` |
| `ui` | `create-canvas`, `create-text`, `create-button`, `create-image` |

---

## Output Formats

```bash
# Text (default) - human readable
unity-mcp scene hierarchy

# JSON - for scripting
unity-mcp --format json scene hierarchy

# Table - structured display
unity-mcp --format table instance list
```

## Environment Variables

Set defaults via environment:

```bash
export UNITY_MCP_HOST=192.168.1.100
export UNITY_MCP_HTTP_PORT=8080
export UNITY_MCP_FORMAT=json
export UNITY_MCP_INSTANCE=MyProject@abc123
```

## Troubleshooting

### Connection Issues

```bash
# Check server status
unity-mcp status

# Verify Unity is running with MCP plugin
# Check Unity console for MCP connection messages
```

### Common Errors

| Error | Solution |
|-------|----------|
| Cannot connect to server | Ensure Unity MCP server is running |
| Unknown command type | Unity plugin may not support this tool |
| Timeout | Increase timeout with `-t 60` |
