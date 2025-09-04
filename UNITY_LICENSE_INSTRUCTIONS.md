# Unity License Instructions for Docker

## Quick Steps

1. **License Request File**: `Unity_v6000.0.39f1.alf` (already generated)
2. **Get License**: Go to https://license.unity3d.com/manual
3. **Upload**: The .alf file
4. **Download**: The .ulf license file
5. **Test**: `./test-local-docker.sh --production --license /path/to/license.ulf`

## Docker Commands with License

### Build Production Image
```bash
./scripts/build-docker-production.sh --license /path/to/Unity_v6000.x.ulf
```

### Run Production Container
```bash
docker run -d \
  --name unity-mcp-prod \
  -p 8080:8080 \
  -p 6400:6400 \
  -v /path/to/Unity_v6000.x.ulf:/tmp/unity.ulf:ro \
  -e UNITY_LICENSE_FILE=/tmp/unity.ulf \
  -e LOG_LEVEL=DEBUG \
  unity-mcp:production
```

### Test Production Container
```bash
# Health check
curl http://localhost:8080/health

# Should show real Unity connection (not mock)
# "unity_connected": true (in production mode)
```

## Environment Variables for Production

- `UNITY_LICENSE_FILE=/tmp/unity.ulf` - Path to license file inside container
- `UNITY_HEADLESS=true` - Headless mode
- `UNITY_MCP_AUTOSTART=true` - Auto-start MCP bridge
- `LOG_LEVEL=DEBUG` - Verbose logging
- `UNITY_PROJECT_PATH=/app/unity-projects/my-project` - Optional project path

## License File Locations

The license file should be saved as:
- `Unity_v6000.0.39f1.ulf` (matching your Unity version)
- Can be in any directory, just reference the full path

## Troubleshooting

- **License expired**: Generate new .alf file and repeat process
- **Wrong Unity version**: Make sure .ulf matches Unity 6000.0.39f1
- **Personal vs Pro**: Personal licenses may need periodic reactivation