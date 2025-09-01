#!/bin/bash
set -euo pipefail

# Unity MCP Production Entrypoint Script
# Handles Unity license activation, service startup, and graceful shutdown

# Color codes for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Logging function
log() {
    echo -e "${GREEN}[$(date +'%Y-%m-%d %H:%M:%S')] [ENTRYPOINT]${NC} $1"
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

# Global variables for process tracking
UNITY_PID=""
SERVER_PID=""

# Cleanup function for graceful shutdown
cleanup() {
    log "Received shutdown signal, cleaning up..."
    
    if [[ -n "$SERVER_PID" ]] && kill -0 "$SERVER_PID" 2>/dev/null; then
        log "Stopping headless server (PID: $SERVER_PID)..."
        kill -TERM "$SERVER_PID" 2>/dev/null || true
        wait "$SERVER_PID" 2>/dev/null || true
        log "Headless server stopped"
    fi
    
    if [[ -n "$UNITY_PID" ]] && kill -0 "$UNITY_PID" 2>/dev/null; then
        log "Stopping Unity Editor (PID: $UNITY_PID)..."
        kill -TERM "$UNITY_PID" 2>/dev/null || true
        # Give Unity time to clean up
        sleep 5
        if kill -0 "$UNITY_PID" 2>/dev/null; then
            warn "Unity didn't stop gracefully, sending SIGKILL..."
            kill -KILL "$UNITY_PID" 2>/dev/null || true
        fi
        log "Unity Editor stopped"
    fi
    
    log "Cleanup completed"
    exit 0
}

# Set up signal handlers
trap cleanup SIGTERM SIGINT SIGHUP

# Validate environment
validate_environment() {
    log "Validating environment..."
    
    # Check if CI mode
    if [[ "${CI_MODE:-false}" == "true" ]]; then
        log "Running in CI mode - skipping Unity validation"
        return 0
    fi
    
    # Check if Unity executable exists
    if [[ ! -f "$UNITY_PATH" ]]; then
        error "Unity Editor not found at $UNITY_PATH"
        exit 1
    fi
    
    # Check Unity license configuration
    if [[ -z "$UNITY_LICENSE_FILE" && -z "$UNITY_USERNAME" && -z "$UNITY_SERIAL" ]]; then
        warn "No Unity license configuration found. Unity may fail to start."
        warn "Set UNITY_LICENSE_FILE, or UNITY_USERNAME/UNITY_PASSWORD/UNITY_SERIAL"
    fi
    
    # Check headless server files
    if [[ ! -f "/app/server/headless_server.py" ]]; then
        error "Headless server script not found"
        exit 1
    fi
    
    log "Environment validation passed"
}

# Activate Unity license
activate_unity_license() {
    log "Configuring Unity license..."
    
    if [[ -n "$UNITY_LICENSE_FILE" && -f "$UNITY_LICENSE_FILE" ]]; then
        log "Using Unity license file: $UNITY_LICENSE_FILE"
        return 0
    fi
    
    if [[ -n "$UNITY_USERNAME" && -n "$UNITY_PASSWORD" && -n "$UNITY_SERIAL" ]]; then
        log "Attempting Unity license activation with credentials..."
        
        # Attempt to activate license
        "$UNITY_PATH" \
            -batchmode \
            -quit \
            -username "$UNITY_USERNAME" \
            -password "$UNITY_PASSWORD" \
            -serial "$UNITY_SERIAL" \
            -logFile /tmp/unity-logs/license-activation.log \
            || warn "License activation may have failed, check logs"
        
        log "License activation attempted"
        return 0
    fi
    
    warn "No license credentials provided, using personal license if available"
    return 0
}

# Start Unity Editor in headless mode
start_unity() {
    log "Starting Unity Editor in headless mode..."
    
    # Create log directory
    mkdir -p /tmp/unity-logs
    
    # Prepare Unity command
    local unity_args=(
        -batchmode
        -nographics
        -quit
    )
    
    # Add project path if specified
    if [[ -n "${UNITY_PROJECT_PATH:-}" ]]; then
        if [[ -d "$UNITY_PROJECT_PATH" ]]; then
            unity_args+=(-projectPath "$UNITY_PROJECT_PATH")
            log "Using Unity project: $UNITY_PROJECT_PATH"
        else
            warn "Unity project path not found: $UNITY_PROJECT_PATH"
        fi
    fi
    
    # Add MCP configuration
    unity_args+=(
        -executeMethod "UnityMcpBridge.UnityMcpBridgeHeadless.StartHeadlessMode"
        -logFile "/tmp/unity-logs/unity-editor.log"
    )
    
    # Set additional environment variables
    export UNITY_MCP_AUTOSTART="${UNITY_MCP_AUTOSTART:-true}"
    export UNITY_MCP_PORT="${UNITY_MCP_PORT:-6400}"
    
    # Start Unity Editor
    info "Unity command: $UNITY_PATH ${unity_args[*]}"
    
    # Use xvfb-run to provide a virtual display for Unity
    xvfb-run -a --server-args="-screen 0 1024x768x24 -ac +extension GLX +render -noreset" \
        "$UNITY_PATH" "${unity_args[@]}" &
    
    UNITY_PID=$!
    log "Unity Editor started with PID: $UNITY_PID"
    
    # Wait for Unity to initialize
    local timeout=60
    local elapsed=0
    
    while [[ $elapsed -lt $timeout ]]; do
        if kill -0 "$UNITY_PID" 2>/dev/null; then
            # Check if MCP bridge is ready by checking the log
            if [[ -f "/tmp/unity-logs/unity-editor.log" ]] && \
               grep -q "Unity MCP Bridge started" "/tmp/unity-logs/unity-editor.log" 2>/dev/null; then
                log "Unity MCP Bridge is ready"
                return 0
            fi
            sleep 2
            elapsed=$((elapsed + 2))
        else
            error "Unity Editor process died during startup"
            return 1
        fi
    done
    
    warn "Unity Editor startup timeout reached, continuing anyway..."
    return 0
}

# Start the headless HTTP server
start_headless_server() {
    log "Starting Unity MCP Headless HTTP Server..."
    
    cd /app/server
    
    # Server configuration
    local server_args=(
        --host "0.0.0.0"
        --port "${HTTP_PORT:-8080}"
        --unity-port "${UNITY_MCP_PORT:-6400}"
        --log-level "${LOG_LEVEL:-INFO}"
        --max-concurrent "${MAX_CONCURRENT_COMMANDS:-5}"
    )
    
    # Use mock server in CI mode
    if [[ "${CI_MODE:-false}" == "true" ]]; then
        info "CI Mode: Using mock server"
        if [[ -f "/app/mock_headless_server.py" ]]; then
            python3 /app/mock_headless_server.py "${server_args[@]}" &
        else
            error "Mock server not found at /app/mock_headless_server.py"
            return 1
        fi
    else
        info "Server command: python3 headless_server.py ${server_args[*]}"
        # Start the real server
        python3 headless_server.py "${server_args[@]}" &
    fi
    
    SERVER_PID=$!
    
    log "Headless server started with PID: $SERVER_PID"
    
    # Wait for server to be ready
    local timeout=30
    local elapsed=0
    
    while [[ $elapsed -lt $timeout ]]; do
        if kill -0 "$SERVER_PID" 2>/dev/null; then
            # Check if server is responding
            if curl -f -s http://localhost:${HTTP_PORT:-8080}/health >/dev/null 2>&1; then
                log "Headless server is ready and responding"
                return 0
            fi
            sleep 2
            elapsed=$((elapsed + 2))
        else
            error "Headless server process died during startup"
            return 1
        fi
    done
    
    warn "Headless server health check timeout, but process is running"
    return 0
}

# Main execution
main() {
    log "Unity MCP Headless Server starting up..."
    log "Container user: $(whoami)"
    log "Unity version: ${UNITY_VERSION:-unknown}"
    log "Unity path: $UNITY_PATH"
    
    # Validate environment
    validate_environment
    
    # Check if CI mode
    if [[ "${CI_MODE:-false}" == "true" ]]; then
        log "Running in CI mode - skipping Unity operations"
        
        # Start headless server only
        if ! start_headless_server; then
            error "Failed to start headless server"
            exit 1
        fi
    else
        # Activate Unity license
        activate_unity_license
        
        # Start Unity Editor
        if ! start_unity; then
            error "Failed to start Unity Editor"
            exit 1
        fi
        
        # Start headless server
        if ! start_headless_server; then
            error "Failed to start headless server"
            exit 1
        fi
    fi
    
    log "All services started successfully"
    log "Headless API available at http://localhost:${HTTP_PORT:-8080}"
    log "Health check: http://localhost:${HTTP_PORT:-8080}/health"
    
    # Wait for processes and handle signals
    while true; do
        # Check if Unity process is still running
        if [[ -n "$UNITY_PID" ]] && ! kill -0 "$UNITY_PID" 2>/dev/null; then
            error "Unity Editor process died unexpectedly"
            cleanup
            exit 1
        fi
        
        # Check if server process is still running
        if [[ -n "$SERVER_PID" ]] && ! kill -0 "$SERVER_PID" 2>/dev/null; then
            error "Headless server process died unexpectedly"
            cleanup
            exit 1
        fi
        
        sleep 5
    done
}

# Run main function
main "$@"