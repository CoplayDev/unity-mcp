#!/bin/bash
set -euo pipefail

# Unity MCP Enhanced Health Check Script
# Used by Docker HEALTHCHECK and Kubernetes probes
# Supports both health and readiness checks

# Configuration
HEALTH_URL="${HEALTH_URL:-http://localhost:${HTTP_PORT:-8080}/health}"
READY_URL="${READY_URL:-http://localhost:${HTTP_PORT:-8080}/ready}"
METRICS_URL="${METRICS_URL:-http://localhost:${HTTP_PORT:-8080}/metrics}"
TIMEOUT="${TIMEOUT:-10}"
MAX_RETRIES="${MAX_RETRIES:-3}"
CHECK_TYPE="${CHECK_TYPE:-health}"

# Thresholds
MEMORY_THRESHOLD="${MEMORY_LIMIT_MB:-3584}"
CPU_THRESHOLD="${CPU_THROTTLE_THRESHOLD:-80}"
DISK_THRESHOLD="${DISK_USAGE_THRESHOLD:-85}"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

log() {
    echo -e "${GREEN}[HEALTHCHECK]${NC} $1"
}

warn() {
    echo -e "${YELLOW}[HEALTHCHECK]${NC} $1"
}

error() {
    echo -e "${RED}[HEALTHCHECK]${NC} $1" >&2
}

info() {
    echo -e "${BLUE}[HEALTHCHECK]${NC} $1"
}

# Basic HTTP health check
http_check() {
    local url="$1"
    local timeout="$2"
    local description="$3"
    
    if curl -f -s --connect-timeout "$timeout" --max-time "$timeout" "$url" >/dev/null 2>&1; then
        log "$description check passed"
        return 0
    else
        error "$description check failed"
        return 1
    fi
}

# Enhanced readiness check
readiness_check() {
    local health_ok=true
    local ready_response=""
    
    # Check if health endpoint is responding
    if ! http_check "$HEALTH_URL" "$TIMEOUT" "Basic health"; then
        return 1
    fi
    
    # Check readiness endpoint if available
    if ready_response=$(curl -f -s --connect-timeout "$TIMEOUT" --max-time "$TIMEOUT" "$READY_URL" 2>/dev/null); then
        log "Readiness endpoint accessible"
        
        # Parse JSON response for detailed status
        if command -v jq >/dev/null 2>&1; then
            local unity_ready=$(echo "$ready_response" | jq -r '.unity_ready // false' 2>/dev/null || echo "false")
            local server_ready=$(echo "$ready_response" | jq -r '.server_ready // false' 2>/dev/null || echo "false")
            
            if [[ "$unity_ready" != "true" ]]; then
                warn "Unity is not ready"
                health_ok=false
            fi
            
            if [[ "$server_ready" != "true" ]]; then
                warn "Server is not ready"
                health_ok=false
            fi
        fi
    else
        # If no dedicated readiness endpoint, check health endpoint response
        local health_response=""
        if health_response=$(curl -f -s --connect-timeout "$TIMEOUT" --max-time "$TIMEOUT" "$HEALTH_URL" 2>/dev/null); then
            if command -v jq >/dev/null 2>&1; then
                local status=$(echo "$health_response" | jq -r '.status // "unknown"' 2>/dev/null || echo "unknown")
                if [[ "$status" != "healthy" ]]; then
                    warn "Service status: $status"
                    health_ok=false
                fi
            else
                # Fallback to grep if jq not available
                if ! echo "$health_response" | grep -q '"status".*"healthy"'; then
                    warn "Service not in healthy state"
                    health_ok=false
                fi
            fi
        else
            error "Unable to get health status"
            health_ok=false
        fi
    fi
    
    # Check system resources
    check_system_resources || health_ok=false
    
    # Check Unity process if running in production mode
    if [[ "${CI_MODE:-false}" != "true" ]]; then
        check_unity_process || health_ok=false
    fi
    
    if [[ "$health_ok" == true ]]; then
        log "Readiness check passed"
        return 0
    else
        error "Readiness check failed"
        return 1
    fi
}

# Check system resources
check_system_resources() {
    local resources_ok=true
    
    # Check memory usage
    if command -v free >/dev/null 2>&1; then
        local memory_used=$(free -m | awk 'NR==2{printf "%.0f", $3}')
        local memory_total=$(free -m | awk 'NR==2{printf "%.0f", $2}')
        local memory_percent=$((memory_used * 100 / memory_total))
        
        local memory_threshold_percent=$((MEMORY_THRESHOLD * 100 / memory_total))
        if [[ $memory_percent -gt $memory_threshold_percent ]]; then
            warn "High memory usage: ${memory_percent}%"
            resources_ok=false
        else
            info "Memory usage: ${memory_percent}%"
        fi
    fi
    
    # Check disk usage
    local disk_usage=$(df /tmp 2>/dev/null | awk 'NR==2{print $5}' | sed 's/%//' || echo "0")
    if [[ $disk_usage -gt $DISK_THRESHOLD ]]; then
        warn "High disk usage: ${disk_usage}%"
        resources_ok=false
    else
        info "Disk usage: ${disk_usage}%"
    fi
    
    return $([ "$resources_ok" == "true" ])
}

# Check Unity process
check_unity_process() {
    # Check if Unity process is running
    if pgrep -f "Unity.*-batchmode" >/dev/null 2>&1; then
        log "Unity process is running"
        
        # Check Unity log for errors
        if [[ -f "/tmp/unity-logs/unity-editor.log" ]]; then
            local recent_errors=$(tail -50 "/tmp/unity-logs/unity-editor.log" 2>/dev/null | grep -c "ERROR\|FATAL\|Exception" || echo "0")
            if [[ $recent_errors -gt 5 ]]; then
                warn "Unity log contains $recent_errors recent errors"
                return 1
            fi
        fi
        
        return 0
    else
        warn "Unity process not found"
        return 1
    fi
}

# Get detailed metrics
get_metrics() {
    if curl -f -s --connect-timeout "$TIMEOUT" --max-time "$TIMEOUT" "$METRICS_URL" >/dev/null 2>&1; then
        log "Metrics endpoint accessible"
        return 0
    else
        warn "Metrics endpoint not available"
        return 1
    fi
}

# Legacy health check for compatibility
legacy_health_check() {
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
                    log "Health check passed (CI mode)"
                    return 0
                elif echo "$response" | grep -q '"unity_connected".*true'; then
                    log "Health check passed"
                    return 0
                else
                    warn "Unity not connected (may be expected)"
                    return 0  # Don't fail on Unity connection for health check
                fi
            else
                error "Health check failed: unhealthy status"
                return 1
            fi
        fi
        
        warn "Health check attempt $attempt/$MAX_RETRIES failed"
        ((attempt++))
        
        if [[ $attempt -le $MAX_RETRIES ]]; then
            sleep 2
        fi
    done
    
    error "Health check failed after $MAX_RETRIES attempts"
    return 1
}

# Check critical processes
check_processes() {
    local processes_ok=true
    
    # In CI mode, we don't expect Unity to be running
    if [[ "${CI_MODE:-false}" != "true" ]]; then
        # Check for Unity process
        if ! pgrep -f "Unity.*-batchmode" >/dev/null 2>&1; then
            warn "Unity process not found"
            processes_ok=false
        fi
    fi
    
    # Check for Python server process (either real or mock)
    if ! pgrep -f "headless_server.py" >/dev/null 2>&1 && ! pgrep -f "mock_headless_server.py" >/dev/null 2>&1; then
        error "Headless server process not found"
        processes_ok=false
    fi
    
    if [[ "$processes_ok" == true ]]; then
        info "All required processes are running"
        return 0
    else
        return 1
    fi
}

# Main health check with retries
main() {
    local check_function="legacy_health_check"
    
    case "$CHECK_TYPE" in
        "health")
            check_function="legacy_health_check"
            ;;
        "ready"|"readiness")
            check_function="readiness_check"
            ;;
        "metrics")
            check_function="get_metrics"
            ;;
        "processes")
            check_function="check_processes"
            ;;
        *)
            error "Unknown check type: $CHECK_TYPE"
            error "Valid types: health, ready, metrics, processes"
            exit 1
            ;;
    esac
    
    local retries=0
    
    while [[ $retries -lt $MAX_RETRIES ]]; do
        case "$check_function" in
            "legacy_health_check")
                if legacy_health_check; then
                    exit 0
                fi
                ;;
            "readiness_check")
                if readiness_check; then
                    exit 0
                fi
                ;;
            "get_metrics")
                if get_metrics; then
                    exit 0
                fi
                ;;
            "check_processes")
                if check_processes; then
                    exit 0
                fi
                ;;
        esac
        
        retries=$((retries + 1))
        if [[ $retries -lt $MAX_RETRIES ]]; then
            warn "Check failed, retrying ($retries/$MAX_RETRIES)..."
            sleep 2
        fi
    done
    
    error "Check failed after $MAX_RETRIES attempts"
    exit 1
}

# Check for command line arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --type)
            CHECK_TYPE="$2"
            shift 2
            ;;
        --url)
            HEALTH_URL="$2"
            shift 2
            ;;
        --timeout)
            TIMEOUT="$2"
            shift 2
            ;;
        --retries)
            MAX_RETRIES="$2"
            shift 2
            ;;
        *)
            # If argument doesn't match known options, treat as check type
            CHECK_TYPE="$1"
            shift
            ;;
    esac
done

# Run health check
main "$@"