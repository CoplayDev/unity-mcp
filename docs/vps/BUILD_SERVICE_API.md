# Unity Build Service API

The Unity Build Service is integrated into the Unity MCP VPS deployment and provides a REST API for building Unity games from user-generated assets.

## Overview

The service enables users to:
- Upload game assets via URLs
- Build complete Unity games automatically 
- Deploy games as playable WebGL builds
- Track build progress and status
- Cancel running builds

## Architecture

```
[Client] -> [API Gateway/Nginx] -> [Unity MCP Multi-Client Server]
                                        |
                                        v
                                  [Build Service]
                                        |
                                        v
                                  [Unity Headless] -> [WebGL Build] -> [Game Deployment]
```

## Authentication

All API endpoints require authentication via Bearer token:

```
Authorization: Bearer YOUR_API_KEY
```

Configure your API key in the environment:
```bash
BUILD_SERVICE_API_KEY=your-secure-api-key-here
```

## API Endpoints

### 1. Create Build

**`POST /build`**

Starts a new build process for a game.

#### Request

**Headers:**
```
Authorization: Bearer YOUR_API_KEY
Content-Type: application/json
```

**Body:**
```json
{
   "user_id": "uuid-string",
   "game_id": "game-identifier", 
   "game_name": "My Awesome Game",
   "game_type": "platformer",
   "asset_set": "basic_assets_v1",
   "assets": [
       ["https://example.com/sprite1.png", "https://example.com/sprite2.png"],
       ["https://example.com/background.jpg"],
       ["https://example.com/sound.wav"]
   ]
}
```

**Field Descriptions:**
- `user_id`: Unique identifier for the user
- `game_id`: Unique identifier for the game
- `game_name`: Display name for the game
- `game_type`: Type of game (used for template selection)
- `asset_set`: Asset set version/configuration
- `assets`: Array of asset slots, each containing URLs to assets

#### Response

**Success (200 OK):**
```json
{
   "url": "/build/abc123-def456-ghi789/status"
}
```

**Error Responses:**
- `403 Forbidden`: Invalid or missing API key
- `400 Bad Request`: Invalid request data
- `502 Internal Server Error`: Build system error

#### Example

```bash
curl -X POST https://your-domain.com/build \
  -H "Authorization: Bearer your-api-key" \
  -H "Content-Type: application/json" \
  -d '{
    "user_id": "user-123",
    "game_id": "game-456", 
    "game_name": "Space Adventure",
    "game_type": "platformer",
    "asset_set": "v1",
    "assets": [
      ["https://cdn.example.com/player.png"],
      ["https://cdn.example.com/background.jpg"],
      ["https://cdn.example.com/music.mp3"]
    ]
  }'
```

### 2. Get Build Status

**`GET /build/{build_id}/status`**

Returns the current status of a build.

#### Request

**Headers:**
```
Authorization: Bearer YOUR_API_KEY
```

#### Response

**Success (200 OK):**
```json
{
  "game_id": "game-456",
  "status": "completed",
  "queue_position": 0,
  "game_url": "https://your-domain.com/games/abc123-def456-ghi789",
  "error_message": null
}
```

**Status Values:**
- `pending`: Build is queued and waiting
- `building`: Build is actively being processed
- `deploying`: Game is being deployed
- `completed`: Build finished successfully
- `failed`: Build encountered an error

**Error Responses:**
- `403 Forbidden`: Invalid or missing API key
- `404 Not Found`: Build ID not found
- `502 Internal Server Error`: System error

#### Example

```bash
curl -H "Authorization: Bearer your-api-key" \
  https://your-domain.com/build/abc123-def456-ghi789/status
```

### 3. Stop Build

**`PUT /build/{build_id}/stop`**

Cancels a running or queued build.

#### Request

**Headers:**
```
Authorization: Bearer YOUR_API_KEY
```

#### Response

**Success (200 OK):**
```json
{
  "status": "Build stopped"
}
```

**Error Responses:**
- `403 Forbidden`: Invalid or missing API key  
- `404 Not Found`: Build ID not found
- `502 Internal Server Error`: System error

#### Example

```bash
curl -X PUT \
  -H "Authorization: Bearer your-api-key" \
  https://your-domain.com/build/abc123-def456-ghi789/stop
```

## Build Process Flow

1. **Asset Download**: Service downloads all assets from provided URLs
2. **Unity Project Creation**: Creates isolated Unity project for the build
3. **Asset Import**: Imports downloaded assets into Unity project
4. **Game Assembly**: Creates game objects based on game type and assets
5. **WebGL Build**: Compiles project to WebGL format
6. **Deployment**: Deploys build to web-accessible location
7. **URL Generation**: Returns playable game URL

## Configuration

### Environment Variables

```bash
# Build service settings
BUILD_SERVICE_API_KEY=your-secure-api-key
BASE_GAME_URL=https://your-domain.com/games
MAX_CONCURRENT_BUILDS=3

# Build directories
BUILD_BASE_DIR=/opt/unity-mcp/builds
GAME_DEPLOY_DIR=/var/www/html/games
```

### Resource Limits

- **Max concurrent builds**: 3 (configurable)
- **Asset download timeout**: 5 minutes per asset
- **Build timeout**: 30 minutes per build
- **Max asset size**: 100MB per asset
- **Max total assets**: 50 per build

## Asset Requirements

### Supported Formats

- **Images**: PNG, JPG, GIF, BMP
- **Audio**: WAV, MP3, OGG  
- **3D Models**: FBX, OBJ
- **Textures**: PNG, JPG, TGA

### Asset Slots

Assets are organized in slots representing different game elements:

```json
{
  "assets": [
    ["player_sprite.png"],           // Slot 0: Player character
    ["background1.jpg", "bg2.jpg"],  // Slot 1: Backgrounds
    ["jump_sound.wav"],              // Slot 2: Sound effects
    ["theme_music.mp3"]              // Slot 3: Background music
  ]
}
```

## Game Types

The service supports different game templates:

- **`platformer`**: 2D platform game with physics
- **`shooter`**: Top-down or side-scrolling shooter
- **`puzzle`**: Puzzle/matching game
- **`runner`**: Endless runner game
- **`rpg`**: Simple RPG with inventory
- **`custom`**: Generic template

## Error Handling

### Common Error Codes

**400 Bad Request:**
```json
{
  "error": "Missing required field: game_name"
}
```

**403 Forbidden:**
```json
{
  "error": "Unauthorized"
}
```

**404 Not Found:**
```json
{
  "error": "Build not found"
}
```

**502 Internal Server Error:**
```json
{
  "error": "Internal server error"
}
```

### Build Failures

When builds fail, the status endpoint returns error details:

```json
{
  "game_id": "game-456",
  "status": "failed",
  "queue_position": 0,
  "game_url": null,
  "error_message": "Failed to download asset: Connection timeout"
}
```

## Monitoring

### Admin Endpoints

**`GET /api/admin/build-stats`**

Returns build service statistics:

```json
{
  "total_builds": 150,
  "completed_builds": 140,
  "failed_builds": 8,
  "active_builds": 2,
  "queued_builds": 5,
  "success_rate": 93.3,
  "max_concurrent_builds": 3
}
```

### Logging

Build service logs are available at:
- **Service logs**: `/opt/unity-mcp/logs/multi-client-server.log`
- **Build logs**: `/opt/unity-mcp/logs/builds/`
- **Unity logs**: `/opt/unity-mcp/logs/unity.log`

## WebGL Deployment

Built games are deployed as WebGL builds with:

- **CORS headers**: Enabled for cross-origin access
- **Compression**: Gzip compression for assets
- **MIME types**: Proper WebGL MIME type configuration
- **Caching**: 1-hour cache for game assets

### Game URL Structure

```
https://your-domain.com/games/{build_id}/
├── index.html          # Game entry point
├── Build/
│   ├── game.wasm      # WebAssembly binary
│   ├── game.data      # Game data
│   ├── game.js        # Unity loader
│   └── game.symbols.json
└── TemplateData/
    └── style.css      # Game styling
```

## Development & Testing

### Local Testing

```bash
# Start the server
python multi_client_server.py

# Test build creation
curl -X POST http://localhost:8080/build \
  -H "Authorization: Bearer default-api-key" \
  -H "Content-Type: application/json" \
  -d @test_build.json

# Monitor build status
curl -H "Authorization: Bearer default-api-key" \
  http://localhost:8080/build/{build_id}/status
```

### Mock Assets

For testing, you can use placeholder assets:

```json
{
  "assets": [
    ["https://via.placeholder.com/64x64.png"],
    ["https://via.placeholder.com/800x600.jpg"],
    ["https://www.soundjay.com/misc/sounds/beep-07a.wav"]
  ]
}
```

## Performance Optimization

### Scaling Recommendations

| Concurrent Builds | Server Specs | Expected Throughput |
|------------------|--------------|-------------------|
| 1-2 builds | 4 vCPUs, 16GB RAM | 2-4 builds/hour |
| 3-5 builds | 8 vCPUs, 32GB RAM | 6-10 builds/hour |
| 6+ builds | 16 vCPUs, 64GB RAM | 12+ builds/hour |

### Build Time Factors

- **Asset count**: More assets = longer download/import time
- **Asset size**: Larger assets = longer processing time  
- **Game complexity**: More game objects = longer build time
- **Server load**: Higher concurrent builds = slower individual builds

### Optimization Tips

1. **Asset optimization**: Compress images and audio before upload
2. **Asset caching**: Reuse common assets across builds
3. **Build queuing**: Queue builds during high-traffic periods
4. **Resource monitoring**: Monitor CPU/memory during builds

## Security Considerations

1. **API Key Management**: Use strong, unique API keys
2. **Asset Validation**: Validate asset URLs and file types
3. **Resource Limits**: Enforce limits on asset size and count
4. **Build Isolation**: Each build runs in isolated Unity client
5. **Output Sanitization**: Validate build outputs before deployment

## Troubleshooting

### Common Issues

**Build stuck in "pending":**
- Check server capacity (`/api/admin/build-stats`)
- Verify Unity service is running
- Check build queue length

**Asset download failures:**
- Verify asset URLs are publicly accessible
- Check network connectivity
- Validate asset file formats

**Build failures:**
- Check Unity logs for compilation errors
- Verify asset compatibility
- Check disk space availability

**Game won't load:**
- Verify WebGL MIME types in nginx
- Check CORS headers
- Validate game URL accessibility

### Log Analysis

```bash
# Check recent build activity
tail -f /opt/unity-mcp/logs/multi-client-server.log | grep "build"

# Monitor Unity build process
tail -f /opt/unity-mcp/logs/unity.log | grep "Build"

# Check nginx access logs
tail -f /var/log/nginx/unity-mcp-access.log | grep "/games/"
```

This Unity Build Service API provides a complete solution for automated game building and deployment using the Unity MCP VPS infrastructure.