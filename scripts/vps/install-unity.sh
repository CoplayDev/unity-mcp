#!/bin/bash
# Unity Editor Installation Script for VPS
# Installs Unity Editor with required modules for headless operation

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

log() {
    echo -e "${GREEN}[$(date +'%Y-%m-%d %H:%M:%S')] $1${NC}"
}

warn() {
    echo -e "${YELLOW}[$(date +'%Y-%m-%d %H:%M:%S')] WARNING: $1${NC}"
}

error() {
    echo -e "${RED}[$(date +'%Y-%m-%d %H:%M:%S')] ERROR: $1${NC}"
    exit 1
}

info() {
    echo -e "${BLUE}[$(date +'%Y-%m-%d %H:%M:%S')] INFO: $1${NC}"
}

# Configuration
UNITY_VERSION=${1:-"6000.0.3f1"}
UNITY_CHANGESET=${2:-"6cd387ce4ddd"}
UNITY_DOWNLOAD_ASSISTANT_URL="https://download.unity3d.com/download_unity/${UNITY_CHANGESET}/UnitySetup-${UNITY_VERSION}"
UNITY_HUB_URL="https://public-cdn.cloud.unity3d.com/hub/prod/UnityHub.AppImage"

# Check if running as root
if [[ $EUID -eq 0 ]]; then
   error "This script should not be run as root. Please run as a regular user with sudo privileges."
fi

log "Starting Unity ${UNITY_VERSION} installation..."

# Verify system requirements
log "Checking system requirements..."

# Check available disk space (need at least 10GB)
AVAILABLE_SPACE=$(df /opt --output=avail | tail -1)
REQUIRED_SPACE=10485760  # 10GB in KB

if [ "$AVAILABLE_SPACE" -lt "$REQUIRED_SPACE" ]; then
    error "Insufficient disk space. Need at least 10GB, available: $((AVAILABLE_SPACE/1024/1024))GB"
fi

# Check if unity user exists
if ! id "unity" &>/dev/null; then
    error "Unity user not found. Please run setup-ubuntu.sh first."
fi

# Check if Xvfb is running
if ! pgrep -x "Xvfb" > /dev/null; then
    warn "Xvfb not running. Starting virtual display..."
    sudo systemctl start xvfb || error "Failed to start Xvfb"
fi

# Create Unity installation directory
log "Creating Unity installation directories..."
sudo mkdir -p /opt/unity/editors
sudo mkdir -p /opt/unity/hub
sudo mkdir -p /tmp/unity-install
sudo chown -R unity:unity /opt/unity

# Download Unity Hub
log "Downloading Unity Hub..."
cd /tmp/unity-install

if [ ! -f "UnityHub.AppImage" ]; then
    wget -O UnityHub.AppImage "$UNITY_HUB_URL" || error "Failed to download Unity Hub"
    chmod +x UnityHub.AppImage
    log "Unity Hub downloaded successfully"
else
    log "Unity Hub already downloaded"
fi

# Install Unity Hub
log "Installing Unity Hub..."
sudo mv UnityHub.AppImage /opt/unity/hub/
sudo chown unity:unity /opt/unity/hub/UnityHub.AppImage

# Create Unity Hub wrapper script
sudo tee /usr/local/bin/unityhub > /dev/null << 'EOF'
#!/bin/bash
export DISPLAY=:99
cd /opt/unity/hub
./UnityHub.AppImage --no-sandbox "$@"
EOF

sudo chmod +x /usr/local/bin/unityhub

# Check for Unity license
log "Checking for Unity license..."
UNITY_LICENSE_FILE=""

# Check for license file in various locations
if [ -f "/tmp/unity.ulf" ]; then
    UNITY_LICENSE_FILE="/tmp/unity.ulf"
elif [ -f "/opt/unity-mcp/config/unity.ulf" ]; then
    UNITY_LICENSE_FILE="/opt/unity-mcp/config/unity.ulf"
elif [ -f "/home/unity/unity.ulf" ]; then
    UNITY_LICENSE_FILE="/home/unity/unity.ulf"
fi

if [ -n "$UNITY_LICENSE_FILE" ]; then
    log "Found Unity license file: $UNITY_LICENSE_FILE"
    sudo cp "$UNITY_LICENSE_FILE" /opt/unity-mcp/config/unity.ulf
    sudo chown unity:unity /opt/unity-mcp/config/unity.ulf
else
    warn "No Unity license file found. You'll need to provide credentials or license file."
    warn "Expected locations:"
    warn "  - /tmp/unity.ulf"
    warn "  - /opt/unity-mcp/config/unity.ulf"
    warn "  - /home/unity/unity.ulf"
fi

# Create Unity license activation script
log "Creating Unity license activation script..."
sudo tee /opt/unity-mcp/scripts/activate-license.sh > /dev/null << 'EOF'
#!/bin/bash
# Unity License Activation Script

set -e

UNITY_PATH="/opt/unity/editors/6000.0.3f1/Editor/Unity"
UNITY_LICENSE_FILE="/opt/unity-mcp/config/unity.ulf"

if [ ! -f "$UNITY_PATH" ]; then
    echo "Unity not found at $UNITY_PATH"
    exit 1
fi

export DISPLAY=:99

if [ -f "$UNITY_LICENSE_FILE" ]; then
    echo "Activating Unity license from file..."
    "$UNITY_PATH" -batchmode -quit -manualLicenseFile "$UNITY_LICENSE_FILE" -logfile /tmp/unity-license.log
    
    if [ $? -eq 0 ]; then
        echo "License activated successfully"
    else
        echo "License activation failed. Check /tmp/unity-license.log"
        exit 1
    fi
elif [ -n "$UNITY_USERNAME" ] && [ -n "$UNITY_PASSWORD" ]; then
    echo "Activating Unity license with credentials..."
    "$UNITY_PATH" -batchmode -quit -username "$UNITY_USERNAME" -password "$UNITY_PASSWORD" -logfile /tmp/unity-license.log
    
    if [ $? -eq 0 ]; then
        echo "License activated successfully"
    else
        echo "License activation failed. Check /tmp/unity-license.log"
        exit 1
    fi
else
    echo "No license file or credentials provided"
    echo "Set UNITY_USERNAME and UNITY_PASSWORD environment variables, or"
    echo "Place unity.ulf file in /opt/unity-mcp/config/"
    exit 1
fi
EOF

sudo chmod +x /opt/unity-mcp/scripts/activate-license.sh
sudo chown unity:unity /opt/unity-mcp/scripts/activate-license.sh

# Function to download Unity Editor directly
download_unity_editor() {
    local version=$1
    local changeset=$2
    
    log "Downloading Unity Editor ${version} directly..."
    
    # Unity download URLs
    local base_url="https://download.unity3d.com/download_unity/${changeset}"
    local editor_url="${base_url}/LinuxEditorInstaller/Unity.tar.xz"
    
    cd /tmp/unity-install
    
    if [ ! -f "Unity.tar.xz" ]; then
        wget -O Unity.tar.xz "$editor_url" || {
            error "Failed to download Unity Editor. Check version and changeset."
        }
        log "Unity Editor downloaded successfully"
    else
        log "Unity Editor archive already exists"
    fi
    
    log "Extracting Unity Editor..."
    sudo mkdir -p "/opt/unity/editors/${version}"
    sudo tar -xf Unity.tar.xz -C "/opt/unity/editors/${version}" --strip-components=1
    sudo chown -R unity:unity "/opt/unity/editors/${version}"
    
    # Verify installation
    if [ -f "/opt/unity/editors/${version}/Editor/Unity" ]; then
        log "Unity Editor extracted successfully"
        return 0
    else
        error "Unity Editor extraction failed"
    fi
}

# Try to install Unity Editor via Hub first, fallback to direct download
log "Attempting to install Unity Editor ${UNITY_VERSION}..."

# Set display for Unity Hub
export DISPLAY=:99

# Try Unity Hub installation (may require license)
log "Trying Unity Hub installation method..."
sudo -u unity -E unityhub --headless install \
    --version "$UNITY_VERSION" \
    --changeset "$UNITY_CHANGESET" \
    --module linux-il2cpp \
    --module linux-server 2>/dev/null || {
    
    warn "Unity Hub installation failed. Trying direct download..."
    download_unity_editor "$UNITY_VERSION" "$UNITY_CHANGESET"
}

# Verify Unity installation
UNITY_EXECUTABLE="/opt/unity/editors/${UNITY_VERSION}/Editor/Unity"

if [ ! -f "$UNITY_EXECUTABLE" ]; then
    error "Unity installation verification failed. Unity executable not found at $UNITY_EXECUTABLE"
fi

log "Verifying Unity installation..."
sudo -u unity DISPLAY=:99 "$UNITY_EXECUTABLE" -version 2>/dev/null | head -5 || {
    warn "Could not get Unity version (may require license activation)"
}

# Create Unity startup script
log "Creating Unity startup script..."
sudo tee /opt/unity-mcp/scripts/start-unity.sh > /dev/null << EOF
#!/bin/bash
# Unity Headless Startup Script

export DISPLAY=:99
export UNITY_VERSION=${UNITY_VERSION}
export UNITY_PATH="/opt/unity/editors/${UNITY_VERSION}/Editor/Unity"

# Activate license if needed
if [ -f /opt/unity-mcp/scripts/activate-license.sh ]; then
    /opt/unity-mcp/scripts/activate-license.sh
fi

# Start Unity in headless mode
"\$UNITY_PATH" \\
    -batchmode \\
    -nographics \\
    -projectPath /opt/unity-mcp/projects/shared \\
    -logFile /opt/unity-mcp/logs/unity.log \\
    -executeMethod UnityMcpBridge.StartHeadlessServer
EOF

sudo chmod +x /opt/unity-mcp/scripts/start-unity.sh
sudo chown unity:unity /opt/unity-mcp/scripts/start-unity.sh

# Create Unity project initialization script
log "Creating Unity project initialization script..."
sudo tee /opt/unity-mcp/scripts/init-project.sh > /dev/null << EOF
#!/bin/bash
# Initialize Unity project for MCP

export DISPLAY=:99
export UNITY_PATH="/opt/unity/editors/${UNITY_VERSION}/Editor/Unity"
PROJECT_PATH="/opt/unity-mcp/projects/shared"

# Create project structure
mkdir -p "\$PROJECT_PATH"/{Assets,ProjectSettings,Packages}

# Create basic project files
cat > "\$PROJECT_PATH/ProjectSettings/ProjectVersion.txt" << EOL
m_EditorVersion: ${UNITY_VERSION}
m_EditorVersionWithRevision: ${UNITY_VERSION} (${UNITY_CHANGESET})
EOL

# Create package manifest
cat > "\$PROJECT_PATH/Packages/manifest.json" << EOL
{
  "dependencies": {
    "com.unity.collab-proxy": "2.4.4",
    "com.unity.feature.development": "1.0.3",
    "com.unity.textmeshpro": "3.2.0-pre.4",
    "com.unity.timeline": "1.8.7",
    "com.unity.ugui": "2.0.0",
    "com.unity.visualscripting": "1.9.4",
    "com.unity.modules.ai": "1.0.0",
    "com.unity.modules.animation": "1.0.0",
    "com.unity.modules.assetbundle": "1.0.0",
    "com.unity.modules.audio": "1.0.0",
    "com.unity.modules.director": "1.0.0",
    "com.unity.modules.imageconversion": "1.0.0",
    "com.unity.modules.imgui": "1.0.0",
    "com.unity.modules.jsonserialize": "1.0.0",
    "com.unity.modules.particlesystem": "1.0.0",
    "com.unity.modules.physics": "1.0.0",
    "com.unity.modules.physics2d": "1.0.0",
    "com.unity.modules.screencapture": "1.0.0",
    "com.unity.modules.terrain": "1.0.0",
    "com.unity.modules.terrainphysics": "1.0.0",
    "com.unity.modules.tilemap": "1.0.0",
    "com.unity.modules.ui": "1.0.0",
    "com.unity.modules.uielements": "1.0.0",
    "com.unity.modules.umbra": "1.0.0",
    "com.unity.modules.unityanalytics": "1.0.0",
    "com.unity.modules.unitywebrequest": "1.0.0",
    "com.unity.modules.unitywebrequestassetbundle": "1.0.0",
    "com.unity.modules.unitywebrequestaudio": "1.0.0",
    "com.unity.modules.unitywebrequesttexture": "1.0.0",
    "com.unity.modules.unitywebrequestwww": "1.0.0",
    "com.unity.modules.vehicles": "1.0.0",
    "com.unity.modules.video": "1.0.0",
    "com.unity.modules.vr": "1.0.0",
    "com.unity.modules.wind": "1.0.0",
    "com.unity.modules.xr": "1.0.0"
  }
}
EOL

echo "Unity project initialized at \$PROJECT_PATH"
EOF

sudo chmod +x /opt/unity-mcp/scripts/init-project.sh
sudo chown unity:unity /opt/unity-mcp/scripts/init-project.sh

# Initialize the shared project
log "Initializing Unity shared project..."
sudo -u unity /opt/unity-mcp/scripts/init-project.sh

# Update environment file with Unity paths
log "Updating environment configuration..."
sudo sed -i "s|UNITY_PATH=.*|UNITY_PATH=/opt/unity/editors/${UNITY_VERSION}/Editor/Unity|" /etc/unity-mcp/environment
sudo sed -i "s|UNITY_VERSION=.*|UNITY_VERSION=${UNITY_VERSION}|" /etc/unity-mcp/environment

# Clean up temporary files
log "Cleaning up..."
rm -rf /tmp/unity-install

# Create Unity status check script
sudo tee /opt/unity-mcp/scripts/unity-status.sh > /dev/null << 'EOF'
#!/bin/bash
# Unity Status Check Script

UNITY_PATH="/opt/unity/editors/6000.0.3f1/Editor/Unity"

echo "=== Unity Installation Status ==="
echo "Unity Path: $UNITY_PATH"

if [ -f "$UNITY_PATH" ]; then
    echo "Unity Installed: ✓ Yes"
    echo "Unity Executable: $(ls -lh "$UNITY_PATH" | awk '{print $5}')"
    echo "Installation Date: $(stat -c %y "$UNITY_PATH" | cut -d' ' -f1)"
    
    # Check if Unity is running
    UNITY_PID=$(pgrep -f "Unity.*batchmode" || echo "")
    if [ -n "$UNITY_PID" ]; then
        echo "Unity Process: ✓ Running (PID: $UNITY_PID)"
    else
        echo "Unity Process: ✗ Not running"
    fi
    
    # Check license status
    if [ -f "/opt/unity-mcp/config/unity.ulf" ]; then
        echo "License File: ✓ Found"
    else
        echo "License File: ✗ Not found"
    fi
    
    # Test Unity execution
    echo ""
    echo "Testing Unity execution..."
    export DISPLAY=:99
    timeout 10s "$UNITY_PATH" -version 2>/dev/null && echo "Unity Test: ✓ Success" || echo "Unity Test: ✗ Failed (license needed?)"
    
else
    echo "Unity Installed: ✗ No"
    echo "Please run install-unity.sh to install Unity"
fi

echo ""
echo "=== System Resources ==="
echo "Disk Space: $(df -h /opt/unity | tail -1 | awk '{print $4}') available"
echo "Memory: $(free -h | awk '/^Mem/ {print $7}') available"
echo "Display: $DISPLAY"

# Check Xvfb
if pgrep -x "Xvfb" > /dev/null; then
    echo "Virtual Display: ✓ Running"
else
    echo "Virtual Display: ✗ Not running"
fi
EOF

sudo chmod +x /opt/unity-mcp/scripts/unity-status.sh
sudo chown unity:unity /opt/unity-mcp/scripts/unity-status.sh

log "✅ Unity ${UNITY_VERSION} installation completed!"
log ""
log "Installation Summary:"
log "• Unity Version: ${UNITY_VERSION}"
log "• Unity Path: /opt/unity/editors/${UNITY_VERSION}/Editor/Unity"
log "• Project Path: /opt/unity-mcp/projects/shared"
log "• License File: /opt/unity-mcp/config/unity.ulf"
log ""
log "Next Steps:"
log "1. Provide Unity license:"
log "   - Copy your unity.ulf file to /opt/unity-mcp/config/unity.ulf, OR"
log "   - Set UNITY_USERNAME and UNITY_PASSWORD environment variables"
log "2. Activate license: /opt/unity-mcp/scripts/activate-license.sh"
log "3. Check status: /opt/unity-mcp/scripts/unity-status.sh"
log "4. Deploy Unity MCP server code"
log ""
log "⚠️  If you encounter licensing issues:"
log "   - Ensure you have a valid Unity license"
log "   - Check /tmp/unity-license.log for error details"
log "   - Contact Unity support for license-related problems"