#!/bin/bash
# Unity MCP Headless Server Health Check Script

set -euo pipefail

# Configuration
HEALTH_URL="http://localhost:${HTTP_PORT:-8080}/health"
TIMEOUT=10
MAX_RETRIES=3

# Health check function
check_health() {
    local attempt=1
    
    while [[ $attempt -le $MAX_RETRIES ]]; do
        # Check if the health endpoint responds
        if curl -f -s --max-time $TIMEOUT "$HEALTH_URL" >/dev/null 2>&1; then
            # Get the health status
            local response=$(curl -s --max-time $TIMEOUT "$HEALTH_URL" 2>/dev/null || echo "{}")
            
            # Check if response contains expected fields
            if echo "$response" | grep -q '"status".*"healthy"'; then
                # In CI mode, we don't expect Unity to be connected
                if [[ "${CI_MODE:-false}" == "true" ]]; then
                    echo "Health check passed (CI mode)"
                    return 0
                elif echo "$response" | grep -q '"unity_connected".*true'; then
                    echo "Health check passed"
                    return 0
                else
                    echo "Health check failed: Unity not connected"
                    echo "Response: $response"
                    return 1
                fi
            else
                echo "Health check failed: unhealthy status"
                echo "Response: $response"
                return 1
            fi
        fi
        
        echo "Health check attempt $attempt/$MAX_RETRIES failed"
        ((attempt++))
        
        if [[ $attempt -le $MAX_RETRIES ]]; then
            sleep 2
        fi
    done
    
    echo "Health check failed after $MAX_RETRIES attempts"
    return 1
}

# Additional process checks
check_processes() {
    # Check if critical processes are running
    local unity_running=false
    local server_running=false
    
    # In CI mode, we don't expect Unity to be running
    if [[ "${CI_MODE:-false}" != "true" ]]; then
        # Check for Unity process
        if pgrep -f "Unity.*-batchmode" >/dev/null 2>&1; then
            unity_running=true
        fi
        
        if [[ "$unity_running" == "false" ]]; then
            echo "Unity process not found"
            return 1
        fi
    fi
    
    # Check for Python server process (either real or mock)
    if pgrep -f "headless_server.py" >/dev/null 2>&1 || pgrep -f "mock_headless_server.py" >/dev/null 2>&1; then
        server_running=true
    fi
    
    if [[ "$server_running" == "false" ]]; then
        echo "Headless server process not found"
        return 1
    fi
    
    echo "All required processes are running"
    return 0
}

# Main health check
main() {
    # First check if processes are running
    if ! check_processes; then
        return 1
    fi
    
    # Then check HTTP health endpoint
    if ! check_health; then
        return 1
    fi
    
    return 0
}

# Run health check
if main; then
    exit 0
else
    exit 1
fi