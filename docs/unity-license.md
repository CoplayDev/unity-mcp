# Unity License Setup for Docker

This guide explains how to configure Unity licensing for the Unity MCP Docker build.

## Prerequisites

- Unity Hub installed locally
- Unity account (free or paid)
- Unity 6000.0.3f1 installed (or matching version in Dockerfile)

## Getting Your Unity License File

### Method 1: From Unity Hub (Recommended)

1. Open Unity Hub
2. Sign in with your Unity account
3. Go to Preferences → Licenses
4. Click "Manual Activation"
5. Click "Save License Request"
6. Go to https://license.unity3d.com/manual
7. Upload the license request file
8. Download the `.ulf` license file

### Method 2: From Command Line

```bash
# Generate license request
/path/to/Unity -batchmode -createManualActivationFile -logFile -

# This creates Unity_v6000.x.alf
# Upload this file at https://license.unity3d.com/manual
# Download the resulting .ulf file
```

## Building with Unity License

### Using Docker BuildKit (Recommended)

```bash
# Ensure BuildKit is enabled
export DOCKER_BUILDKIT=1

# Build with license file
docker build \
  --secret id=unity_license,src=/path/to/Unity_v6000.x.ulf \
  -f docker/Dockerfile.production \
  -t unity-mcp:production \
  --build-arg UNITY_VERSION=6000.0.3f1 \
  .
```

### Using Docker Compose

Create a `.env` file:
```env
UNITY_LICENSE_FILE=/path/to/Unity_v6000.x.ulf
UNITY_VERSION=6000.0.3f1
```

Then use the production compose file:
```bash
docker compose -f docker-compose.production.yml build
```

## Running the Container

After building, run with:

```bash
docker run -d \
  --name unity-mcp \
  -p 8080:8080 \
  -p 6400:6400 \
  -e UNITY_LICENSE_FILE=/tmp/unity.ulf \
  -v /path/to/Unity_v6000.x.ulf:/tmp/unity.ulf:ro \
  -v /path/to/unity-project:/app/unity-projects/my-project \
  unity-mcp:production
```

## License Types

### Personal License (Free)
- Good for revenue under $100k/year
- Requires periodic online activation
- May need reactivation every few days

### Plus/Pro License
- Use serial key method
- More stable for production use
- Supports offline activation

## Troubleshooting

### License Activation Failed
- Check Unity version matches exactly
- Ensure license file is for correct Unity version
- Verify network access for activation

### Unity Installation Hangs
- Unity download can take 10-30 minutes
- Check Docker build logs for progress
- Ensure stable internet connection

### Permission Errors
- License file must be readable
- Use `chmod 644 Unity_v2022.x.ulf`

## Security Notes

- Never commit license files to git
- Use Docker secrets for CI/CD
- Rotate credentials regularly
- Consider using Unity Cloud Build for CI/CD

## CI/CD Integration

For GitHub Actions or other CI systems:

1. Store license file as a secret
2. Write secret to file during build
3. Pass as Docker build secret

Example GitHub Actions:
```yaml
- name: Build Docker image
  env:
    UNITY_LICENSE: ${{ secrets.UNITY_LICENSE_FILE }}
  run: |
    echo "$UNITY_LICENSE" > unity.ulf
    docker build \
      --secret id=unity_license,src=unity.ulf \
      -f docker/Dockerfile.production \
      -t unity-mcp:production \
      .
    rm unity.ulf
```