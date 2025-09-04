#!/bin/bash
# Unity License Generation Script
set -euo pipefail

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

log() { echo -e "${GREEN}[UNITY-LICENSE]${NC} $1"; }
warn() { echo -e "${YELLOW}[WARN]${NC} $1"; }
error() { echo -e "${RED}[ERROR]${NC} $1"; }
info() { echo -e "${BLUE}[INFO]${NC} $1"; }

# Check if Unity is installed
UNITY_PATHS=(
    "/opt/unity/editors/*/Editor/Unity"
    "/usr/bin/unity-editor"
    "/snap/unity/current/Editor/Unity"
    "$HOME/Unity/Hub/Editor/*/Editor/Unity"
    "/Applications/Unity/Hub/Editor/*/Unity.app/Contents/MacOS/Unity"
)

UNITY_EXECUTABLE=""

log "Searching for Unity installation..."

for path_pattern in "${UNITY_PATHS[@]}"; do
    # Use find to resolve wildcards
    if [[ "$path_pattern" == *"*"* ]]; then
        # Use find for wildcard paths
        found_paths=$(find ${path_pattern%/*} -name "${path_pattern##*/}" -type f -executable 2>/dev/null || true)
        if [[ -n "$found_paths" ]]; then
            UNITY_EXECUTABLE=$(echo "$found_paths" | head -1)
            log "Found Unity at: $UNITY_EXECUTABLE"
            break
        fi
    else
        if [[ -x "$path_pattern" ]]; then
            UNITY_EXECUTABLE="$path_pattern"
            log "Found Unity at: $path_pattern"
            break
        fi
    fi
done

if [[ -z "$UNITY_EXECUTABLE" ]]; then
    error "Unity installation not found!"
    echo ""
    echo "Please install Unity or specify the path manually:"
    echo "  $0 /path/to/Unity/Editor/Unity"
    exit 1
fi

# Allow manual Unity path override
if [[ $# -gt 0 ]]; then
    UNITY_EXECUTABLE="$1"
    if [[ ! -x "$UNITY_EXECUTABLE" ]]; then
        error "Unity executable not found or not executable: $UNITY_EXECUTABLE"
        exit 1
    fi
    log "Using specified Unity path: $UNITY_EXECUTABLE"
fi

# Create license request file
log "Creating manual activation file..."
LICENSE_REQUEST="Unity_v$(date +%Y%m%d_%H%M%S).alf"

"$UNITY_EXECUTABLE" \
    -batchmode \
    -quit \
    -createManualActivationFile \
    -logFile unity-license-generation.log

# Find the generated .alf file
if [[ ! -f "$LICENSE_REQUEST" ]]; then
    # Unity might create with different naming
    ALF_FILE=$(find . -name "*.alf" -type f -newer /tmp -print -quit 2>/dev/null || echo "")
    if [[ -n "$ALF_FILE" ]]; then
        LICENSE_REQUEST="$ALF_FILE"
    else
        error "Failed to generate license request file!"
        echo "Check unity-license-generation.log for details:"
        cat unity-license-generation.log 2>/dev/null || echo "No log file found"
        exit 1
    fi
fi

log "✅ License request file created: $LICENSE_REQUEST"

# Display next steps
echo ""
info "Next steps to get your Unity license file:"
echo "1. Go to: https://license.unity3d.com/manual"
echo "2. Upload the file: $LICENSE_REQUEST"
echo "3. Download the resulting .ulf license file"
echo ""
log "Once you have the .ulf file, test with Docker:"
echo "./test-local-docker.sh --production --license /path/to/your-license.ulf"
echo ""
log "Or use the production build script:"
echo "./scripts/build-docker-production.sh --license /path/to/your-license.ulf"

# Offer to open browser
if command -v xdg-open >/dev/null 2>&1; then
    echo ""
    read -p "Open Unity license website in browser? (y/n): " -n 1 -r
    echo
    if [[ $REPLY =~ ^[Yy]$ ]]; then
        xdg-open "https://license.unity3d.com/manual"
    fi
fi