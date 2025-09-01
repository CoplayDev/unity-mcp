#!/bin/bash
# Build Unity MCP Production Docker Image
# This script helps build the production Docker image with Unity licensing

set -euo pipefail

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

# Default values
UNITY_VERSION="2022.3.45f1"
UNITY_CHANGESET="63b2b3067b8e"
IMAGE_TAG="unity-mcp:production"
DOCKERFILE="docker/Dockerfile.production"

# Function to print colored output
log() {
    echo -e "${GREEN}[BUILD]${NC} $1"
}

error() {
    echo -e "${RED}[ERROR]${NC} $1" >&2
}

warn() {
    echo -e "${YELLOW}[WARN]${NC} $1"
}

info() {
    echo -e "${BLUE}[INFO]${NC} $1"
}

# Function to check if Docker BuildKit is enabled
check_buildkit() {
    if [[ "${DOCKER_BUILDKIT:-0}" != "1" ]]; then
        warn "Docker BuildKit is not enabled. Enabling it for this build..."
        export DOCKER_BUILDKIT=1
    fi
}

# Function to find Unity license file
find_license_file() {
    local license_file=""
    
    # Check common locations
    local possible_locations=(
        "$HOME/Unity_v${UNITY_VERSION}.ulf"
        "$HOME/Unity_v2022.x.ulf"
        "$HOME/.local/share/unity3d/Unity/Unity_lic.ulf"
        "./Unity_v2022.x.ulf"
        "./unity.ulf"
    )
    
    for location in "${possible_locations[@]}"; do
        if [[ -f "$location" ]]; then
            license_file="$location"
            break
        fi
    done
    
    echo "$license_file"
}

# Parse command line arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --license)
            LICENSE_FILE="$2"
            shift 2
            ;;
        --username)
            UNITY_USERNAME="$2"
            shift 2
            ;;
        --password)
            UNITY_PASSWORD="$2"
            shift 2
            ;;
        --serial)
            UNITY_SERIAL="$2"
            shift 2
            ;;
        --tag)
            IMAGE_TAG="$2"
            shift 2
            ;;
        --unity-version)
            UNITY_VERSION="$2"
            shift 2
            ;;
        --help)
            echo "Usage: $0 [options]"
            echo "Options:"
            echo "  --license FILE       Path to Unity license file (.ulf)"
            echo "  --username USER      Unity account username"
            echo "  --password PASS      Unity account password"
            echo "  --serial KEY         Unity serial key (Pro/Plus)"
            echo "  --tag TAG           Docker image tag (default: unity-mcp:production)"
            echo "  --unity-version VER  Unity version (default: 2022.3.45f1)"
            echo "  --help              Show this help message"
            exit 0
            ;;
        *)
            error "Unknown option: $1"
            exit 1
            ;;
    esac
done

# Main build process
main() {
    log "Unity MCP Docker Production Build"
    log "================================="
    info "Unity Version: $UNITY_VERSION"
    info "Image Tag: $IMAGE_TAG"
    
    # Check BuildKit
    check_buildkit
    
    # Determine authentication method
    local auth_method=""
    local build_args=()
    
    if [[ -n "${LICENSE_FILE:-}" ]]; then
        if [[ ! -f "$LICENSE_FILE" ]]; then
            error "License file not found: $LICENSE_FILE"
            exit 1
        fi
        auth_method="license"
        build_args+=("--secret" "id=unity_license,src=$LICENSE_FILE")
        log "Using Unity license file: $LICENSE_FILE"
    elif [[ -n "${UNITY_USERNAME:-}" ]] && [[ -n "${UNITY_PASSWORD:-}" ]]; then
        auth_method="credentials"
        # Create temporary files for secrets
        echo "$UNITY_USERNAME" > /tmp/unity_username.txt
        echo "$UNITY_PASSWORD" > /tmp/unity_password.txt
        build_args+=("--secret" "id=unity_username,src=/tmp/unity_username.txt")
        build_args+=("--secret" "id=unity_password,src=/tmp/unity_password.txt")
        log "Using Unity Hub credentials"
    elif [[ -n "${UNITY_SERIAL:-}" ]]; then
        auth_method="serial"
        echo "$UNITY_SERIAL" > /tmp/unity_serial.txt
        build_args+=("--secret" "id=unity_serial,src=/tmp/unity_serial.txt")
        log "Using Unity serial key"
    else
        # Try to find license file automatically
        local found_license=$(find_license_file)
        if [[ -n "$found_license" ]]; then
            LICENSE_FILE="$found_license"
            auth_method="license"
            build_args+=("--secret" "id=unity_license,src=$LICENSE_FILE")
            log "Found Unity license file: $LICENSE_FILE"
        else
            error "No Unity authentication method provided!"
            echo ""
            echo "Please provide one of the following:"
            echo "  1. Unity license file: --license /path/to/Unity_v2022.x.ulf"
            echo "  2. Unity credentials: --username YOUR_EMAIL --password YOUR_PASSWORD"
            echo "  3. Unity serial: --serial YOUR-SERIAL-KEY"
            echo ""
            echo "To get a license file:"
            echo "  1. Open Unity Hub → Preferences → Licenses → Manual Activation"
            echo "  2. Save the request file and upload at https://license.unity3d.com/manual"
            echo "  3. Download the .ulf file"
            exit 1
        fi
    fi
    
    # Build the image
    log "Starting Docker build..."
    
    docker build \
        "${build_args[@]}" \
        -f "$DOCKERFILE" \
        -t "$IMAGE_TAG" \
        --target production \
        --build-arg UNITY_VERSION="$UNITY_VERSION" \
        --build-arg UNITY_CHANGESET="$UNITY_CHANGESET" \
        --build-arg PYTHON_VERSION=3.11 \
        .
    
    # Clean up temporary files
    if [[ "$auth_method" == "credentials" ]]; then
        rm -f /tmp/unity_username.txt /tmp/unity_password.txt
    elif [[ "$auth_method" == "serial" ]]; then
        rm -f /tmp/unity_serial.txt
    fi
    
    if [[ $? -eq 0 ]]; then
        log "✅ Build completed successfully!"
        info "Image tagged as: $IMAGE_TAG"
        echo ""
        echo "To run the container:"
        echo "  docker run -d \\"
        echo "    --name unity-mcp \\"
        echo "    -p 8080:8080 \\"
        echo "    -p 6400:6400 \\"
        echo "    -v /path/to/unity-project:/app/unity-projects/my-project \\"
        echo "    $IMAGE_TAG"
    else
        error "Build failed!"
        exit 1
    fi
}

# Run main function
main