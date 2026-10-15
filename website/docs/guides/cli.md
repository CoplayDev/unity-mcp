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
| `-v, --verbose` | — | Print each command sent to Unity and its raw response to stderr |

## Command Reference

### Instance Management

```bash
# List connected Unity instances
unity-mcp instance list

# Target one instance: per call with --instance, or for a whole shell with UNITY_MCP_INSTANCE
unity-mcp --instance "ProjectName@abc123" editor play

# Show current instance
unity-mcp instance current
```

### Scene Operations

```bash
# Get scene hierarchy
unity-mcp scene hierarchy
unity-mcp scene hierarchy --limit 20 --max-depth 3

# Get active scene info
unity-mcp scene active

# Load/save scenes
unity-mcp scene load "Assets/Scenes/Main.unity"
unity-mcp scene save

# Screenshots (use camera command)
unity-mcp camera screenshot
unity-mcp camera screenshot --file-name "level_preview"
unity-mcp camera screenshot --camera-ref "SecondCamera" --include-image
unity-mcp camera screenshot --batch surround --max-resolution 256
unity-mcp camera screenshot --batch orbit --view-target "Player"
unity-mcp camera screenshot --capture-source scene_view --view-target "Canvas" --include-image
unity-mcp camera screenshot-multiview --view-target "Player" --max-resolution 480
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

# List custom tools for the active Unity project
unity-mcp tool list
unity-mcp custom_tool list
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

Note: VFX Graph tooling is tested against com.unity.visualeffectgraph 12.1.13. Install VFX Graph and use URP/HDRP (set the Render Pipeline Asset) to avoid Unity warnings; other versions may be unsupported.

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

### ProBuilder Operations

Note: Requires com.unity.probuilder package installed in your Unity project.

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
unity-mcp prefab create "Player" "Assets/Prefabs/Player.prefab"

# Modify prefab contents (headless, no UI)
unity-mcp prefab modify "Assets/Prefabs/Player.prefab" --target Weapon --position "0,1,2"
unity-mcp prefab modify "Assets/Prefabs/Player.prefab" --delete-child Child1 --delete-child "Turret/Barrel"
unity-mcp prefab modify "Assets/Prefabs/Player.prefab" --set-property "Rigidbody.mass=5"
unity-mcp prefab modify "Assets/Prefabs/Player.prefab" --add-component BoxCollider
unity-mcp prefab modify "Assets/Prefabs/Player.prefab" --create-child '{"name":"Spawn","primitive_type":"Sphere"}'
```

### Asset Operations

```bash
# Search assets
unity-mcp asset search "*.mat" --path "Assets/Materials"

# Get asset info
unity-mcp asset info "Assets/Materials/Red.mat"

# Create folder
unity-mcp asset mkdir "Assets/NewFolder"

# Move/rename
unity-mcp asset move "Assets/Old.mat" "Assets/Materials/"
```

### Animation Operations

```bash
# Animator on a GameObject
unity-mcp animation animator info "Player"
unity-mcp animation animator play "Player" "Run"
unity-mcp animation animator set-parameter "Player" "Speed" 1.5
unity-mcp animation animator set-parameter "Player" "IsRunning" true --type bool

# Clips and controllers
unity-mcp animation clip create "Assets/Animations/Bounce.anim" --length 2.0 --loop
unity-mcp animation clip info "Assets/Animations/Walk.anim"
unity-mcp animation controller info "Assets/Animations/Player.controller"
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

### Camera Operations

```bash
unity-mcp camera ping                                       # Check Cinemachine availability
unity-mcp camera list                                       # List all cameras
unity-mcp camera create --name "Cam" --preset follow --follow "Player"
unity-mcp camera set-target "Cam" --follow "Player" --look-at "Enemy"
unity-mcp camera set-lens "Cam" --fov 60 --near 0.1 --far 1000
unity-mcp camera set-priority "Cam" --priority 15
unity-mcp camera set-body "Cam" --body-type "CinemachineFollow"
unity-mcp camera set-aim "Cam" --aim-type "CinemachineRotationComposer"
unity-mcp camera set-noise "Cam" --amplitude 1.5 --frequency 0.5
unity-mcp camera add-extension "Cam" CinemachineConfiner3D
unity-mcp camera ensure-brain --blend-style "EaseInOut" --blend-duration 1.5
unity-mcp camera brain-status
unity-mcp camera force "Cam"                                # Force Brain to use camera
unity-mcp camera release                                    # Release override
unity-mcp camera screenshot --file-name "capture" --super-size 2
unity-mcp camera screenshot --batch orbit --view-target "Player" --max-resolution 256
unity-mcp camera screenshot --capture-source scene_view --view-target "Canvas" --include-image
unity-mcp camera screenshot-multiview --view-target "Player" --max-resolution 480
```

#### Screenshot Options

`unity-mcp camera screenshot` options (from `--help`):

| Option | Type | Description |
|--------|------|-------------|
| `--camera-ref` | string | Camera name/path/ID. Omit to capture through the ScreenCapture API |
| `--file-name` | string | Output file name (default: timestamp-based) |
| `--super-size` | int | Resolution multiplier for the saved file |
| `--include-image / --no-include-image` | flag | Also return the PNG inline as base64 |
| `--max-resolution` | int | Longest edge of the inline image in pixels (default 640; 480 per tile with `--batch`) |
| `--capture-source` | string | `game_view` (default) or `scene_view` (editor viewport) |
| `--batch` | string | `surround` (6 angles) or `orbit` (grid around the target) |
| `--view-target` | string | GameObject name/path/ID, or a `[x,y,z]` position. Aims the camera (game_view) or frames the Scene View (scene_view) |
| `--output-folder` | string | Save folder, project-relative or absolute inside the project (default: Editor preference, then `Assets/Screenshots`) |

`camera screenshot-multiview` takes `--max-resolution`, `--view-target` and `--output-folder`. The MCP tool's `view_position`, `view_rotation` and `orbit_*` settings need `unity-mcp raw manage_camera '{"action": "screenshot", ...}'`.

### Graphics Operations

```bash
# Volumes
unity-mcp graphics volume-create --name "PostFX" --global
unity-mcp graphics volume-add-effect --target "PostFX" --effect "Bloom"
unity-mcp graphics volume-set-effect --target "PostFX" --effect "Bloom" -p intensity 1.5
unity-mcp graphics volume-info --target "PostFX"
unity-mcp graphics volume-list-effects

# Render Pipeline
unity-mcp graphics pipeline-info
unity-mcp graphics pipeline-set-quality --level "High"
unity-mcp graphics pipeline-set-settings -s renderScale 1.5

# Light Baking
unity-mcp graphics bake-start [--sync]
unity-mcp graphics bake-status
unity-mcp graphics bake-cancel
unity-mcp graphics bake-settings
unity-mcp graphics bake-create-probes --spacing 5
unity-mcp graphics bake-create-reflection --resolution 512

# Stats & Debug
unity-mcp graphics stats
unity-mcp graphics stats-memory
unity-mcp graphics stats-debug-mode --mode "Wireframe"

# URP Renderer Features
unity-mcp graphics feature-list
unity-mcp graphics feature-add --type "ScreenSpaceAmbientOcclusion"
unity-mcp graphics feature-configure --name "SSAO" -p Intensity 1.5
unity-mcp graphics feature-toggle --name "SSAO" --active|--inactive

# Skybox & Environment
unity-mcp graphics skybox-info
unity-mcp graphics skybox-set-material --material "Assets/Materials/Sky.mat"
unity-mcp graphics skybox-set-ambient --mode Flat --color "0.2,0.2,0.3"
unity-mcp graphics skybox-set-fog --enable --mode ExponentialSquared --density 0.02
unity-mcp graphics skybox-set-reflection --intensity 1.0 --bounces 2
unity-mcp graphics skybox-set-sun --target "DirectionalLight"
```

### Package Operations

```bash
unity-mcp packages ping                                     # Check package manager
unity-mcp packages list                                     # List installed packages
unity-mcp packages search "cinemachine"                     # Search registry
unity-mcp packages info "com.unity.cinemachine"             # Package details
unity-mcp packages add "com.unity.cinemachine"              # Install package
unity-mcp packages add "com.unity.cinemachine@4.1.1"        # Specific version
unity-mcp packages remove "com.unity.cinemachine" [--force]
unity-mcp packages embed "com.unity.cinemachine"            # Embed for local editing
unity-mcp packages resolve                                  # Force re-resolution
unity-mcp packages status <job_id>                          # Check async op
unity-mcp packages list-registries
unity-mcp packages add-registry "Name" --url URL -s "com.example"
unity-mcp packages remove-registry "Name"
```

### Texture Operations

```bash
unity-mcp texture create "Assets/Textures/Red.png" --color "1,0,0,1"
unity-mcp texture create "Assets/Textures/Check.png" --pattern checkerboard --width 256 --height 256
unity-mcp texture create "Assets/Textures/Img.png" --image-path "/path/to/source.png"
unity-mcp texture sprite "Assets/Sprites/Player.png" --width 32 --height 32 --ppu 16
unity-mcp texture modify "Assets/Textures/Img.png" --set-pixels '{"x":0,"y":0,"width":16,"height":16,"color":[1,0,0,1]}'
unity-mcp texture delete "Assets/Textures/Old.png" [--force]
# Patterns: checkerboard, stripes, stripes_h, stripes_v, stripes_diag, dots, grid, brick
```

### Sprite Animation

```bash
unity-mcp sprite info "Assets/Sprites/Hero.png"                      # Size, import settings, slices
unity-mcp sprite slice "Assets/Sprites/Hero.png" --cols 6 --rows 4   # Or --frame-width/--frame-height
unity-mcp sprite slice "Assets/Sprites/Painted.png" --cols 8 --filter-mode bilinear   # Default point, for pixel art
unity-mcp sprite setup-clips "Assets/Sprites/Hero.png" --clips '[{"name": "walk", "start_frame": 0, "end_frame": 5}]'
unity-mcp sprite setup-controller "Assets/Animators/Hero.controller" --clips '[{"name": "walk", "path": "Assets/Sprites/walk.anim"}]'
unity-mcp sprite full-setup "Assets/Sprites/Coin.png" --cols 8 --clips '[{"name": "spin", "start_frame": 0, "end_frame": 7, "loop": true}]'
```

### Build Operations

```bash
unity-mcp build platform                                    # Read the active platform
unity-mcp build platform android                            # Switch platform
unity-mcp build scenes                                      # Read the build scene list
unity-mcp build run --target windows64 --development
unity-mcp build batch --targets windows64,linux64,webgl
unity-mcp build status                                      # Last build report
unity-mcp build settings product_name --value "My Game"
```

### Physics Operations

```bash
unity-mcp physics ping                                      # Physics system status
unity-mcp physics get-settings
unity-mcp physics raycast --origin "0,5,0" --direction "0,-1,0" --max-distance 10
unity-mcp physics overlap --shape sphere --position "0,0,0" --size 2
unity-mcp physics get-rigidbody "Player"
unity-mcp physics simulate --steps 10                       # Step physics in edit mode
unity-mcp physics validate                                  # Check the scene for common mistakes
```

### Profiler Operations

```bash
unity-mcp profiler start                                    # Optionally --log-file to record a .raw
unity-mcp profiler status
unity-mcp profiler frame-timing
unity-mcp profiler get-counters --category Render
unity-mcp profiler memory-snapshot                          # Requires com.unity.memoryprofiler
unity-mcp profiler stop
```

### Reflection and Docs

```bash
unity-mcp reflect search NavMesh                            # Find Unity types by name
unity-mcp reflect type NavMeshAgent                         # Member summary
unity-mcp reflect member Physics Raycast                    # One member in detail
unity-mcp docs get Physics Raycast                          # Unity documentation page
```

### Asset Generation and Blender

```bash
unity-mcp asset-gen list-models --kind image
unity-mcp asset-gen generate-image --provider fal --prompt "a stone texture"
unity-mcp asset-gen generate-model --provider tripo --mode text --prompt "a red chair"
unity-mcp asset-gen status --job-id abc123
unity-mcp asset-gen import-model-file --source-path "C:/exports/house.fbx" --output-folder Assets/Models
unity-mcp blender status                                    # Is Blender reachable?
unity-mcp blender import-model --selection-only --target-size 2
```

### Raw Commands

For any MCP tool not covered by dedicated commands:

```bash
unity-mcp raw manage_scene '{"action": "get_hierarchy", "max_nodes": 100}'
unity-mcp raw read_console '{"count": 20}'
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
