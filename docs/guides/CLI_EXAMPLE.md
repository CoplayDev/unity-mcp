## Unity MCP (CLI Mode)

We use Unity MCP via **CLI commands** instead of MCP server connection. This avoids the reconnection issues that occur when Unity restarts.

### Why CLI Instead of MCP Connection?

- MCP connection breaks when Unity restarts
- `/mcp reconnect` requires human intervention
- CLI works directly via HTTP to the MCP server - no persistent connection needed
- Claude can call CLI commands autonomously without reconnection issues

### Installation

```bash
cd Server  # In unity-mcp repo
pip install -e .
# Or with uv:
uv pip install -e .
```

### Global Options

| Option | Description | Default | Env Variable |
|--------|-------------|---------|--------------|
| `-h, --host` | Server host | 127.0.0.1 | `UNITY_MCP_HOST` |
| `-p, --port` | Server port | 8080 | `UNITY_MCP_HTTP_PORT` |
| `-t, --timeout` | Timeout seconds | 30 | `UNITY_MCP_TIMEOUT` |
| `-f, --format` | Output: text, json, table | text | `UNITY_MCP_FORMAT` |
| `-i, --instance` | Target Unity instance | - | `UNITY_MCP_INSTANCE` |

### Core CLI Commands

**Status & Connection**
```bash
unity-mcp status                           # Check server + Unity connection
```

**Instance Management**
```bash
unity-mcp instance list                    # List connected Unity instances
unity-mcp instance set "ProjectName@abc"   # Set active instance
unity-mcp instance current                 # Show current instance
```

**Editor Control**
```bash
unity-mcp editor play|pause|stop           # Control play mode
unity-mcp editor console [--clear]         # Get/clear console logs
unity-mcp editor refresh [--compile]       # Refresh assets
unity-mcp editor menu "Edit/Project Settings..."  # Execute menu item
unity-mcp editor add-tag "TagName"         # Add tag
unity-mcp editor add-layer "LayerName"     # Add layer
unity-mcp editor tests --mode PlayMode [--async]
unity-mcp editor poll-test <job_id> [--wait 60] [--details]
unity-mcp --instance "MyProject@abc123" editor play  # Target a specific instance
```

**Custom Tools**
```bash
unity-mcp tool list
unity-mcp custom_tool list
unity-mcp editor custom-tool "bake_lightmaps"
unity-mcp editor custom-tool "capture_screenshot" --params '{"filename":"shot_01","width":1920,"height":1080}'
```

**Scene Operations**
```bash
unity-mcp scene hierarchy [--limit 20] [--depth 3]
unity-mcp scene active
unity-mcp scene load "Assets/Scenes/Main.unity"
unity-mcp scene save
unity-mcp --format json scene hierarchy
```

**Screenshots** (via `camera` command):
```bash
unity-mcp camera screenshot --file-name "capture"
unity-mcp camera screenshot --camera-ref "MainCam" --include-image --max-resolution 512
unity-mcp camera screenshot --batch surround --max-resolution 256
unity-mcp camera screenshot --batch orbit --view-target "Player"
unity-mcp camera screenshot --capture-source scene_view --view-target "Canvas" --include-image
unity-mcp camera screenshot-multiview --view-target "Player" --max-resolution 480
```

**GameObject Operations**
```bash
unity-mcp gameobject find "Name" [--method by_tag|by_name|by_layer|by_component]
unity-mcp gameobject create "Name" [--primitive Cube] [--position X Y Z]
unity-mcp gameobject modify "Name" [--position X Y Z] [--rotation X Y Z]
unity-mcp gameobject delete "Name" [--force]
unity-mcp gameobject duplicate "Name"
```

**Component Operations**
```bash
unity-mcp component add "GameObject" ComponentType
unity-mcp component remove "GameObject" ComponentType
unity-mcp component set "GameObject" Component property value
```

**Script Operations**
```bash
unity-mcp script create "ScriptName" --path "Assets/Scripts"
unity-mcp script read "Assets/Scripts/File.cs"
unity-mcp script delete "Assets/Scripts/File.cs" [--force]
unity-mcp code search "pattern" "path/to/file.cs" [--max-results 20]
```

**Asset Operations**
```bash
unity-mcp asset search --pattern "*.mat" --path "Assets/Materials"
unity-mcp asset info "Assets/Materials/File.mat"
unity-mcp asset mkdir "Assets/NewFolder"
unity-mcp asset move "Old/Path" "New/Path"
```

**Prefab Operations**
```bash
unity-mcp prefab open "Assets/Prefabs/File.prefab"
unity-mcp prefab save
unity-mcp prefab close
unity-mcp prefab create "GameObject" --path "Assets/Prefabs"
unity-mcp prefab modify "Assets/Prefabs/File.prefab" --delete-child Child1
unity-mcp prefab modify "Assets/Prefabs/File.prefab" --target Weapon --position "0,1,2"
unity-mcp prefab modify "Assets/Prefabs/File.prefab" --set-property "Rigidbody.mass=5"
```

**Material Operations**
```bash
unity-mcp material create "Assets/Materials/File.mat"
unity-mcp material set-color "File.mat" R G B
unity-mcp material assign "File.mat" "GameObject"
```

**Shader Operations**
```bash
unity-mcp shader create "Name" --path "Assets/Shaders"
unity-mcp shader read "Assets/Shaders/Custom.shader"
unity-mcp shader update "Assets/Shaders/Custom.shader" --file local.shader
unity-mcp shader delete "Assets/Shaders/File.shader" [--force]
```

**VFX Operations**
```bash
unity-mcp vfx particle info|play|stop|pause|restart|clear "Name"
unity-mcp vfx line info "Name"
unity-mcp vfx line create-line "Name" --start X Y Z --end X Y Z
unity-mcp vfx line create-circle "Name" --radius N
unity-mcp vfx trail info|set-time|clear "Name" [time]
```

**Camera Operations**
```bash
unity-mcp camera ping                                       # Check Cinemachine
unity-mcp camera list                                       # List all cameras
unity-mcp camera create --name "Cam" --preset follow --follow "Player"
unity-mcp camera set-target "Cam" --follow "Player" --look-at "Enemy"
unity-mcp camera set-lens "Cam" --fov 60 --near 0.1
unity-mcp camera set-priority "Cam" --priority 15
unity-mcp camera set-body "Cam" --body-type "CinemachineFollow"
unity-mcp camera set-aim "Cam" --aim-type "CinemachineRotationComposer"
unity-mcp camera set-noise "Cam" --amplitude 1.5 --frequency 0.5
unity-mcp camera ensure-brain --blend-style "EaseInOut" --blend-duration 1.5
unity-mcp camera force "Cam"                                # Force Brain to use camera
unity-mcp camera release                                    # Release override
```

**Graphics Operations**
```bash
# Volumes
unity-mcp graphics volume-create --name "PostFX" --global
unity-mcp graphics volume-add-effect --target "PostFX" --effect "Bloom"
unity-mcp graphics volume-set-effect --target "PostFX" --effect "Bloom" -p intensity 1.5
unity-mcp graphics volume-info --target "PostFX"
# Pipeline
unity-mcp graphics pipeline-info
unity-mcp graphics pipeline-set-quality --level "High"
# Baking
unity-mcp graphics bake-start [--sync]
unity-mcp graphics bake-status
unity-mcp graphics bake-create-probes --spacing 5
# Stats
unity-mcp graphics stats
unity-mcp graphics stats-memory
# URP Features
unity-mcp graphics feature-list
unity-mcp graphics feature-add --type "ScreenSpaceAmbientOcclusion"
# Skybox
unity-mcp graphics skybox-info
unity-mcp graphics skybox-set-fog --enable --mode ExponentialSquared --density 0.02
unity-mcp graphics skybox-set-sun --target "DirectionalLight"
```

**Package Operations**
```bash
unity-mcp packages list                                     # List installed
unity-mcp packages search "cinemachine"                     # Search registry
unity-mcp packages info "com.unity.cinemachine"             # Details
unity-mcp packages add "com.unity.cinemachine"              # Install
unity-mcp packages add "com.unity.cinemachine@4.1.1"        # Specific version
unity-mcp packages remove "com.unity.cinemachine" [--force]
unity-mcp packages embed "com.unity.cinemachine"            # Local editing
unity-mcp packages resolve                                  # Re-resolve
unity-mcp packages list-registries
unity-mcp packages add-registry "Name" --url URL -s "com.example"
```

**Texture Operations**
```bash
unity-mcp texture create "Assets/Textures/Red.png" --color "1,0,0,1"
unity-mcp texture create "Assets/Textures/Check.png" --pattern checkerboard --width 256 --height 256
unity-mcp texture sprite "Assets/Sprites/Player.png" --width 32 --height 32 --ppu 16
unity-mcp texture modify "Assets/Textures/Img.png" --set-pixels '{"x":0,"y":0,"width":16,"height":16,"color":[1,0,0,1]}'
unity-mcp texture delete "Assets/Textures/Old.png" [--force]
```

**Lighting & UI**
```bash
unity-mcp lighting create "Name" --type Point|Spot [--intensity N] [--position X Y Z]
unity-mcp ui create-canvas "Name"
unity-mcp ui create-text "Name" --parent "Canvas" --text "Content"
unity-mcp ui create-button "Name" --parent "Canvas" --text "Label"
```

**Batch Operations**
```bash
unity-mcp batch run commands.json [--parallel] [--fail-fast]
unity-mcp batch inline '[{"tool": "manage_scene", "params": {...}}]'
unity-mcp batch template > commands.json
```

**Raw Access (Any Tool)**
```bash
unity-mcp raw tool_name 'JSON_params'
unity-mcp raw manage_scene '{"action":"get_active"}'
unity-mcp raw manage_camera '{"action":"screenshot","include_image":true}'
unity-mcp raw manage_graphics '{"action":"volume_get_info","target":"PostProcessing"}'
unity-mcp raw manage_packages '{"action":"list_packages"}'
```

### Note on MCP Server

The MCP HTTP server still needs to be running for CLI to work. Here is an example to run the server manually on Mac:
```bash
/opt/homebrew/bin/uvx --no-cache --refresh --from /XXX/unity-mcp/Server mcp-for-unity --transport http --http-url http://localhost:8080
```
