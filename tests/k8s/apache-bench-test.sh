#!/bin/bash
set -euo pipefail

# Simple Apache Bench Load Test for Unity MCP
# Alternative to k6 for basic load testing

# Configuration
NAMESPACE="${NAMESPACE:-unity-mcp}"
SERVICE="${SERVICE:-unity-mcp-server}"
CONCURRENT_USERS="${CONCURRENT_USERS:-10}"
TOTAL_REQUESTS="${TOTAL_REQUESTS:-1000}"
TIMEOUT="${TIMEOUT:-30}"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

log() {
    echo -e "${GREEN}[$(date +'%H:%M:%S')]${NC} $1"
}

error() {
    echo -e "${RED}[$(date +'%H:%M:%S')]${NC} $1" >&2
}

info() {
    echo -e "${BLUE}[$(date +'%H:%M:%S')]${NC} $1"
}

# Check prerequisites
check_prerequisites() {
    if ! command -v ab >/dev/null 2>&1; then
        error "Apache Bench (ab) is required but not installed"
        error "Install with: apt-get install apache2-utils"
        return 1
    fi
    
    if ! command -v kubectl >/dev/null 2>&1; then
        error "kubectl is required but not installed"
        return 1
    fi
    
    return 0
}

# Set up port forwarding
setup_port_forward() {
    log "Setting up port forward to service..."
    kubectl port-forward -n "$NAMESPACE" "service/$SERVICE" 8080:80 &
    local port_forward_pid=$!
    sleep 5
    
    # Test connectivity
    if curl -f -s http://localhost:8080/health >/dev/null 2>&1; then
        log "Port forward established successfully"
        echo $port_forward_pid
    else
        kill $port_forward_pid 2>/dev/null || true
        error "Failed to establish connection through port forward"
        return 1
    fi
}

# Run health endpoint load test
test_health_endpoint() {
    local url="http://localhost:8080/health"
    
    log "Testing health endpoint: $url"
    log "Concurrent users: $CONCURRENT_USERS"
    log "Total requests: $TOTAL_REQUESTS"
    
    ab -n "$TOTAL_REQUESTS" -c "$CONCURRENT_USERS" -s "$TIMEOUT" -r "$url" | tee /tmp/ab-health-results.txt
    
    # Parse results
    local requests_per_sec=$(grep "Requests per second" /tmp/ab-health-results.txt | awk '{print $4}')
    local failed_requests=$(grep "Failed requests" /tmp/ab-health-results.txt | awk '{print $3}')
    local time_per_request=$(grep "Time per request.*mean" /tmp/ab-health-results.txt | head -1 | awk '{print $4}')
    
    info "Results Summary:"
    info "  Requests per second: $requests_per_sec"
    info "  Failed requests: $failed_requests"
    info "  Time per request: ${time_per_request}ms"
    
    # Validate results
    if [[ "$failed_requests" -eq 0 ]]; then
        log "✓ Health endpoint test passed - no failed requests"
    else
        error "✗ Health endpoint test failed - $failed_requests failed requests"
        return 1
    fi
}

# Run command execution load test
test_command_endpoint() {
    local url="http://localhost:8080/execute-command"
    local post_data='{"action":"create_gameobject","params":{"name":"LoadTestObject","position":{"x":0,"y":0,"z":0}}}'
    
    log "Testing command endpoint: $url"
    
    # Create temporary file with POST data
    echo "$post_data" > /tmp/post-data.json
    
    ab -n 50 -c 5 -s "$TIMEOUT" -p /tmp/post-data.json -T "application/json" "$url" | tee /tmp/ab-command-results.txt
    
    # Parse results
    local requests_per_sec=$(grep "Requests per second" /tmp/ab-command-results.txt | awk '{print $4}')
    local failed_requests=$(grep "Failed requests" /tmp/ab-command-results.txt | awk '{print $3}')
    local time_per_request=$(grep "Time per request.*mean" /tmp/ab-command-results.txt | head -1 | awk '{print $4}')
    
    info "Command Endpoint Results:"
    info "  Requests per second: $requests_per_sec"
    info "  Failed requests: $failed_requests"
    info "  Time per request: ${time_per_request}ms"
    
    # Clean up
    rm -f /tmp/post-data.json
    
    # Validate results (more lenient for command endpoint)
    if [[ "$failed_requests" -lt 5 ]]; then
        log "✓ Command endpoint test passed"
    else
        error "✗ Command endpoint test failed - too many failed requests: $failed_requests"
        return 1
    fi
}

# Main execution
main() {
    log "Starting Apache Bench Load Test for Unity MCP"
    
    if ! check_prerequisites; then
        exit 1
    fi
    
    # Set up port forwarding
    local port_forward_pid
    if port_forward_pid=$(setup_port_forward); then
        log "Port forward PID: $port_forward_pid"
    else
        exit 1
    fi
    
    # Ensure cleanup on exit
    trap "kill $port_forward_pid 2>/dev/null || true" EXIT
    
    local test_failures=0
    
    # Test health endpoint
    if ! test_health_endpoint; then
        ((test_failures++))
    fi
    
    # Test command endpoint
    if ! test_command_endpoint; then
        ((test_failures++))
    fi
    
    # Results
    if [[ $test_failures -eq 0 ]]; then
        log "✓ All load tests passed!"
    else
        error "✗ $test_failures test(s) failed"
        exit 1
    fi
}

# Handle arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --namespace|-n)
            NAMESPACE="$2"
            shift 2
            ;;
        --service|-s)
            SERVICE="$2"
            shift 2
            ;;
        --concurrent|-c)
            CONCURRENT_USERS="$2"
            shift 2
            ;;
        --requests|-r)
            TOTAL_REQUESTS="$2"
            shift 2
            ;;
        --help|-h)
            echo "Apache Bench Load Test for Unity MCP"
            echo "Usage: $0 [OPTIONS]"
            echo ""
            echo "Options:"
            echo "  -n, --namespace    Kubernetes namespace (default: unity-mcp)"
            echo "  -s, --service      Service name (default: unity-mcp-server)"
            echo "  -c, --concurrent   Concurrent users (default: 10)"
            echo "  -r, --requests     Total requests (default: 1000)"
            echo "  -h, --help         Show this help message"
            exit 0
            ;;
        *)
            error "Unknown option: $1"
            exit 1
            ;;
    esac
done

main "$@"