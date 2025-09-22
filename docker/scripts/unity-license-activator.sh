#!/bin/bash
set -euo pipefail

# Unity License Activation Script for Kubernetes
# Handles Unity license activation from various sources (files, secrets, env vars)

# Color codes for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Logging functions
log() {
    echo -e "${GREEN}[$(date +'%Y-%m-%d %H:%M:%S')] [LICENSE]${NC} $1"
}

warn() {
    echo -e "${YELLOW}[$(date +'%Y-%m-%d %H:%M:%S')] [WARN]${NC} $1"
}

error() {
    echo -e "${RED}[$(date +'%Y-%m-%d %H:%M:%S')] [ERROR]${NC} $1" >&2
}

info() {
    echo -e "${BLUE}[$(date +'%Y-%m-%d %H:%M:%S')] [INFO]${NC} $1"
}

# Unity license activation function
activate_unity_license() {
    local activation_method=""
    local license_status=false
    
    log "Starting Unity license activation process..."
    
    # Method 1: Unity License File (.ulf)
    if [[ -n "${UNITY_LICENSE_FILE:-}" && -f "$UNITY_LICENSE_FILE" ]]; then
        log "Using Unity license file: $UNITY_LICENSE_FILE"
        activation_method="license_file"
        
        # Create Unity license directory if it doesn't exist
        mkdir -p "$HOME/.config/unity3d"
        
        # Copy license file to Unity's expected location
        if cp "$UNITY_LICENSE_FILE" "$HOME/.config/unity3d/Unity_lic.ulf"; then
            log "Unity license file copied successfully"
            license_status=true
        else
            error "Failed to copy Unity license file"
            return 1
        fi
    
    # Method 2: Kubernetes Secret Mount (preferred for K8s)
    elif [[ -f "/var/secrets/unity/license.ulf" ]]; then
        log "Using Unity license from Kubernetes secret mount"
        activation_method="k8s_secret"
        
        mkdir -p "$HOME/.config/unity3d"
        
        if cp "/var/secrets/unity/license.ulf" "$HOME/.config/unity3d/Unity_lic.ulf"; then
            log "Unity license from K8s secret copied successfully"
            license_status=true
        else
            error "Failed to copy Unity license from K8s secret"
            return 1
        fi
    
    # Method 3: Unity Hub Credentials (username/password/serial)
    elif [[ -n "${UNITY_USERNAME:-}" && -n "${UNITY_PASSWORD:-}" && -n "${UNITY_SERIAL:-}" ]]; then
        log "Attempting Unity license activation with Hub credentials..."
        activation_method="hub_credentials"
        
        # Create a temporary project for license activation
        local temp_project="/tmp/unity-license-activation"
        mkdir -p "$temp_project"
        
        # Attempt license activation
        if timeout 300 "$UNITY_PATH" \
            -batchmode \
            -quit \
            -projectPath "$temp_project" \
            -username "$UNITY_USERNAME" \
            -password "$UNITY_PASSWORD" \
            -serial "$UNITY_SERIAL" \
            -logFile "/tmp/unity-logs/license-activation.log"; then
            
            log "Unity license activation with credentials completed"
            license_status=true
            
            # Clean up temp project
            rm -rf "$temp_project"
        else
            error "Unity license activation with credentials failed"
            if [[ -f "/tmp/unity-logs/license-activation.log" ]]; then
                error "Unity activation log:"
                tail -20 "/tmp/unity-logs/license-activation.log" >&2
            fi
            return 1
        fi
    
    # Method 4: Environment Variable License Content
    elif [[ -n "${UNITY_LICENSE_CONTENT:-}" ]]; then
        log "Using Unity license from environment variable"
        activation_method="env_content"
        
        mkdir -p "$HOME/.config/unity3d"
        
        if echo "$UNITY_LICENSE_CONTENT" | base64 -d > "$HOME/.config/unity3d/Unity_lic.ulf"; then
            log "Unity license from environment variable decoded and saved"
            license_status=true
        else
            error "Failed to decode Unity license from environment variable"
            return 1
        fi
    
    # Method 5: Try personal license (fallback)
    else
        warn "No Unity license credentials provided"
        warn "Attempting to use existing personal license if available..."
        activation_method="personal_fallback"
        
        # Check if personal license already exists
        if [[ -f "$HOME/.config/unity3d/Unity_lic.ulf" ]]; then
            log "Found existing personal Unity license"
            license_status=true
        else
            error "No Unity license found and no activation credentials provided"
            error "Please provide one of the following:"
            error "  - UNITY_LICENSE_FILE: Path to .ulf license file"
            error "  - K8s secret mount at /var/secrets/unity/license.ulf"
            error "  - UNITY_USERNAME, UNITY_PASSWORD, UNITY_SERIAL for activation"
            error "  - UNITY_LICENSE_CONTENT: Base64 encoded license content"
            return 1
        fi
    fi
    
    # Verify license is valid
    if [[ "$license_status" == true ]]; then
        if verify_unity_license; then
            log "Unity license activation successful using method: $activation_method"
            export UNITY_LICENSE_ACTIVATED=true
            return 0
        else
            error "Unity license verification failed"
            return 1
        fi
    fi
    
    return 1
}

# Verify Unity license is valid
verify_unity_license() {
    log "Verifying Unity license..."
    
    # Create a minimal test project for verification
    local test_project="/tmp/unity-license-test"
    mkdir -p "$test_project"
    
    # Try to run Unity with the license
    if timeout 120 "$UNITY_PATH" \
        -batchmode \
        -quit \
        -projectPath "$test_project" \
        -logFile "/tmp/unity-logs/license-verification.log" \
        -executeMethod "UnityEditor.EditorApplication.Exit"; then
        
        log "Unity license verification successful"
        rm -rf "$test_project"
        return 0
    else
        error "Unity license verification failed"
        if [[ -f "/tmp/unity-logs/license-verification.log" ]]; then
            error "Unity verification log:"
            tail -10 "/tmp/unity-logs/license-verification.log" >&2
        fi
        rm -rf "$test_project"
        return 1
    fi
}

# Get license information
get_license_info() {
    local license_file="$HOME/.config/unity3d/Unity_lic.ulf"
    
    if [[ -f "$license_file" ]]; then
        info "Unity License Information:"
        
        # Extract license type and expiry if available
        if command -v xmllint >/dev/null 2>&1; then
            local license_type=$(xmllint --xpath "//License/@type" "$license_file" 2>/dev/null | sed 's/.*type="\([^"]*\)".*/\1/' || echo "Unknown")
            local license_version=$(xmllint --xpath "//License/@version" "$license_file" 2>/dev/null | sed 's/.*version="\([^"]*\)".*/\1/' || echo "Unknown")
            
            info "  Type: $license_type"
            info "  Version: $license_version"
        else
            info "  File exists: $license_file"
            info "  Size: $(stat -c%s "$license_file" 2>/dev/null || echo "Unknown") bytes"
        fi
    else
        warn "No Unity license file found at $license_file"
    fi
}

# Return Unity license to the license server (for cleanup)
return_unity_license() {
    log "Returning Unity license..."
    
    if [[ -n "${UNITY_USERNAME:-}" && -n "${UNITY_PASSWORD:-}" ]]; then
        "$UNITY_PATH" \
            -batchmode \
            -quit \
            -username "$UNITY_USERNAME" \
            -password "$UNITY_PASSWORD" \
            -returnlicense \
            -logFile "/tmp/unity-logs/license-return.log" \
            || warn "License return may have failed (this is often normal)"
    else
        info "No credentials available for license return"
    fi
    
    # Remove local license file
    rm -f "$HOME/.config/unity3d/Unity_lic.ulf"
    log "Local Unity license file removed"
}

# Main execution
main() {
    local action="${1:-activate}"
    
    case "$action" in
        activate)
            activate_unity_license
            get_license_info
            ;;
        verify)
            verify_unity_license
            ;;
        info)
            get_license_info
            ;;
        return)
            return_unity_license
            ;;
        *)
            error "Usage: $0 {activate|verify|info|return}"
            error "  activate: Activate Unity license using available methods"
            error "  verify:   Verify current Unity license is valid"
            error "  info:     Show current license information"
            error "  return:   Return license to Unity license server"
            exit 1
            ;;
    esac
}

# Ensure Unity path is set
if [[ -z "${UNITY_PATH:-}" ]]; then
    error "UNITY_PATH environment variable not set"
    exit 1
fi

# Ensure Unity path exists
if [[ ! -f "$UNITY_PATH" ]]; then
    error "Unity executable not found at: $UNITY_PATH"
    exit 1
fi

# Create log directory
mkdir -p /tmp/unity-logs

# Run main function with all arguments
main "$@"