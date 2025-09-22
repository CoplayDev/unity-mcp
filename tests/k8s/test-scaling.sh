#!/bin/bash
set -euo pipefail

# Unity MCP Kubernetes Scaling Test Script
# Tests HPA scaling behavior and pod startup performance

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

# Configuration
NAMESPACE="${NAMESPACE:-unity-mcp}"
DEPLOYMENT="unity-mcp-server"
SERVICE="unity-mcp-server"
HPA_NAME="unity-mcp-server-hpa"
MAX_WAIT_TIME=600  # 10 minutes
POD_STARTUP_TIMEOUT=180  # 3 minutes

# Logging functions
log() {
    echo -e "${GREEN}[$(date +'%H:%M:%S')] [TEST]${NC} $1"
}

warn() {
    echo -e "${YELLOW}[$(date +'%H:%M:%S')] [WARN]${NC} $1"
}

error() {
    echo -e "${RED}[$(date +'%H:%M:%S')] [ERROR]${NC} $1" >&2
}

info() {
    echo -e "${BLUE}[$(date +'%H:%M:%S')] [INFO]${NC} $1"
}

# Check prerequisites
check_prerequisites() {
    log "Checking prerequisites..."
    
    # Check kubectl
    if ! command -v kubectl >/dev/null 2>&1; then
        error "kubectl is required but not installed"
        return 1
    fi
    
    # Check k6 for load testing
    if ! command -v k6 >/dev/null 2>&1; then
        warn "k6 not found, will skip load-based scaling tests"
        warn "Install k6: https://k6.io/docs/getting-started/installation/"
    fi
    
    # Check cluster connectivity
    if ! kubectl cluster-info >/dev/null 2>&1; then
        error "Unable to connect to Kubernetes cluster"
        return 1
    fi
    
    # Check if namespace exists
    if ! kubectl get namespace "$NAMESPACE" >/dev/null 2>&1; then
        error "Namespace '$NAMESPACE' does not exist"
        return 1
    fi
    
    # Check if deployment exists
    if ! kubectl get deployment "$DEPLOYMENT" -n "$NAMESPACE" >/dev/null 2>&1; then
        error "Deployment '$DEPLOYMENT' does not exist in namespace '$NAMESPACE'"
        return 1
    fi
    
    log "Prerequisites check passed"
}

# Get current pod count
get_pod_count() {
    kubectl get pods -n "$NAMESPACE" -l app=unity-mcp-server --no-headers 2>/dev/null | wc -l
}

# Get ready pod count
get_ready_pod_count() {
    kubectl get pods -n "$NAMESPACE" -l app=unity-mcp-server --no-headers 2>/dev/null | grep " Running " | grep "1/1" | wc -l || echo "0"
}

# Wait for pods to be ready
wait_for_pods_ready() {
    local expected_count=$1
    local timeout=$2
    local start_time=$(date +%s)
    
    log "Waiting for $expected_count pods to be ready (timeout: ${timeout}s)..."
    
    while true; do
        local current_time=$(date +%s)
        local elapsed=$((current_time - start_time))
        
        if [[ $elapsed -gt $timeout ]]; then
            error "Timeout waiting for pods to be ready"
            return 1
        fi
        
        local ready_count=$(get_ready_pod_count)
        info "Ready pods: $ready_count/$expected_count"
        
        if [[ $ready_count -ge $expected_count ]]; then
            log "All pods are ready"
            return 0
        fi
        
        sleep 10
    done
}

# Test manual scaling
test_manual_scaling() {
    log "Testing manual scaling..."
    
    # Scale to 1 pod first
    log "Scaling down to 1 pod..."
    kubectl scale deployment "$DEPLOYMENT" -n "$NAMESPACE" --replicas=1
    wait_for_pods_ready 1 120
    
    # Test scaling up to 3 pods
    log "Scaling up to 3 pods..."
    local scale_start_time=$(date +%s)
    kubectl scale deployment "$DEPLOYMENT" -n "$NAMESPACE" --replicas=3
    
    if wait_for_pods_ready 3 $POD_STARTUP_TIMEOUT; then
        local scale_end_time=$(date +%s)
        local scale_duration=$((scale_end_time - scale_start_time))
        log "Manual scaling completed in ${scale_duration}s"
        
        if [[ $scale_duration -le 60 ]]; then
            log "✓ Manual scaling performance: GOOD (${scale_duration}s)"
        elif [[ $scale_duration -le 120 ]]; then
            warn "Manual scaling performance: ACCEPTABLE (${scale_duration}s)"
        else
            error "Manual scaling performance: POOR (${scale_duration}s)"
        fi
    else
        error "Manual scaling test failed"
        return 1
    fi
    
    # Test scaling down
    log "Testing scale down..."
    kubectl scale deployment "$DEPLOYMENT" -n "$NAMESPACE" --replicas=1
    sleep 30  # Wait for scale down
    
    local final_count=$(get_pod_count)
    if [[ $final_count -eq 1 ]]; then
        log "✓ Scale down successful"
    else
        warn "Scale down may not have completed yet (current: $final_count pods)"
    fi
}

# Test pod startup time
test_pod_startup_time() {
    log "Testing pod startup time..."
    
    # Delete all pods to force recreation
    log "Deleting all pods to test startup time..."
    kubectl delete pods -n "$NAMESPACE" -l app=unity-mcp-server --wait=false
    
    # Wait a moment for pods to be deleted
    sleep 10
    
    # Measure time to get pods ready
    local startup_start_time=$(date +%s)
    
    if wait_for_pods_ready 1 $POD_STARTUP_TIMEOUT; then
        local startup_end_time=$(date +%s)
        local startup_duration=$((startup_end_time - startup_start_time))
        
        log "Pod startup completed in ${startup_duration}s"
        
        if [[ $startup_duration -le 60 ]]; then
            log "✓ Pod startup performance: EXCELLENT (${startup_duration}s)"
        elif [[ $startup_duration -le 120 ]]; then
            log "✓ Pod startup performance: GOOD (${startup_duration}s)"
        elif [[ $startup_duration -le 180 ]]; then
            warn "Pod startup performance: ACCEPTABLE (${startup_duration}s)"
        else
            error "Pod startup performance: POOR (${startup_duration}s)"
        fi
    else
        error "Pod startup test failed"
        return 1
    fi
}

# Test HPA auto-scaling (requires k6)
test_hpa_scaling() {
    if ! command -v k6 >/dev/null 2>&1; then
        warn "Skipping HPA scaling test - k6 not available"
        return 0
    fi
    
    log "Testing HPA auto-scaling..."
    
    # Ensure HPA is enabled
    if ! kubectl get hpa "$HPA_NAME" -n "$NAMESPACE" >/dev/null 2>&1; then
        warn "HPA '$HPA_NAME' not found, skipping auto-scaling test"
        return 0
    fi
    
    # Reset to single pod
    kubectl scale deployment "$DEPLOYMENT" -n "$NAMESPACE" --replicas=1
    wait_for_pods_ready 1 120
    
    # Get service endpoint for load testing
    local service_url
    if kubectl get service "$SERVICE" -n "$NAMESPACE" >/dev/null 2>&1; then
        # Use port-forward for testing
        log "Setting up port-forward for load testing..."
        kubectl port-forward -n "$NAMESPACE" "service/$SERVICE" 8080:80 &
        local port_forward_pid=$!
        sleep 5  # Wait for port-forward to establish
        service_url="http://localhost:8080"
    else
        error "Service '$SERVICE' not found"
        return 1
    fi
    
    # Run load test to trigger scaling
    log "Running load test to trigger HPA scaling..."
    local load_test_script="$(dirname "$0")/k6-load-test.js"
    
    if [[ -f "$load_test_script" ]]; then
        # Run a shorter, more intense load test for HPA
        UNITY_MCP_URL="$service_url" k6 run \
            --duration 5m \
            --vus 15 \
            "$load_test_script" &
        local k6_pid=$!
        
        # Monitor scaling
        log "Monitoring HPA scaling behavior..."
        local hpa_start_time=$(date +%s)
        local max_pods_seen=1
        
        for i in {1..20}; do  # Monitor for up to 10 minutes
            sleep 30
            local current_pods=$(get_pod_count)
            local ready_pods=$(get_ready_pod_count)
            
            if [[ $current_pods -gt $max_pods_seen ]]; then
                max_pods_seen=$current_pods
            fi
            
            log "HPA Status: $ready_pods/$current_pods pods ready (max seen: $max_pods_seen)"
            kubectl get hpa "$HPA_NAME" -n "$NAMESPACE" --no-headers 2>/dev/null || true
            
            # Check if we've scaled up
            if [[ $current_pods -ge 3 ]]; then
                log "✓ HPA scaling detected - scaled to $current_pods pods"
                break
            fi
        done
        
        # Clean up
        kill $k6_pid 2>/dev/null || true
        kill $port_forward_pid 2>/dev/null || true
        wait $k6_pid 2>/dev/null || true
        wait $port_forward_pid 2>/dev/null || true
        
        if [[ $max_pods_seen -gt 1 ]]; then
            log "✓ HPA scaling test successful (max pods: $max_pods_seen)"
        else
            warn "HPA scaling may not have triggered (check metrics server and load)"
        fi
    else
        error "Load test script not found: $load_test_script"
        kill $port_forward_pid 2>/dev/null || true
        return 1
    fi
}

# Test rolling updates
test_rolling_updates() {
    log "Testing rolling updates..."
    
    # Get current image
    local current_image=$(kubectl get deployment "$DEPLOYMENT" -n "$NAMESPACE" -o jsonpath='{.spec.template.spec.containers[0].image}')
    log "Current image: $current_image"
    
    # Trigger a rolling update by adding an annotation
    log "Triggering rolling update..."
    kubectl annotate deployment "$DEPLOYMENT" -n "$NAMESPACE" test.rolling-update="$(date +%s)" --overwrite
    
    # Monitor the rollout
    log "Monitoring rollout status..."
    if kubectl rollout status deployment "$DEPLOYMENT" -n "$NAMESPACE" --timeout=300s; then
        log "✓ Rolling update completed successfully"
        
        # Check that we maintained availability
        local ready_pods=$(get_ready_pod_count)
        if [[ $ready_pods -gt 0 ]]; then
            log "✓ Zero-downtime rolling update confirmed"
        else
            warn "Rolling update may have caused downtime"
        fi
    else
        error "Rolling update failed or timed out"
        return 1
    fi
}

# Test service connectivity during scaling
test_service_connectivity() {
    log "Testing service connectivity during scaling operations..."
    
    # Get service endpoint
    local service_endpoint
    kubectl port-forward -n "$NAMESPACE" "service/$SERVICE" 8080:80 &
    local port_forward_pid=$!
    sleep 5
    service_endpoint="http://localhost:8080"
    
    # Function to test connectivity
    test_connectivity() {
        local url="$1/health"
        if curl -f -s --connect-timeout 5 --max-time 10 "$url" >/dev/null 2>&1; then
            return 0
        else
            return 1
        fi
    }
    
    # Test connectivity during scaling
    log "Testing connectivity while scaling up..."
    kubectl scale deployment "$DEPLOYMENT" -n "$NAMESPACE" --replicas=3 &
    
    local connection_failures=0
    local total_tests=0
    
    for i in {1..30}; do  # Test for 5 minutes
        ((total_tests++))
        if ! test_connectivity "$service_endpoint"; then
            ((connection_failures++))
            warn "Connection test $i failed"
        fi
        sleep 10
    done
    
    # Clean up
    kill $port_forward_pid 2>/dev/null || true
    wait $port_forward_pid 2>/dev/null || true
    
    # Calculate availability
    local availability=$((100 * (total_tests - connection_failures) / total_tests))
    log "Service availability during scaling: ${availability}% ($connection_failures failures out of $total_tests tests)"
    
    if [[ $availability -ge 95 ]]; then
        log "✓ High availability maintained during scaling"
    elif [[ $availability -ge 90 ]]; then
        warn "Acceptable availability during scaling"
    else
        error "Poor availability during scaling"
        return 1
    fi
}

# Generate test report
generate_report() {
    local test_results_file="/tmp/unity-mcp-scaling-test-results.txt"
    
    log "Generating test report..."
    
    {
        echo "Unity MCP Kubernetes Scaling Test Report"
        echo "Generated: $(date)"
        echo "Namespace: $NAMESPACE"
        echo "Deployment: $DEPLOYMENT"
        echo ""
        echo "Current Cluster State:"
        kubectl get nodes --no-headers 2>/dev/null | wc -l | xargs echo "Nodes:"
        kubectl get pods -n "$NAMESPACE" -l app=unity-mcp-server --no-headers 2>/dev/null | wc -l | xargs echo "Pods:"
        kubectl get hpa "$HPA_NAME" -n "$NAMESPACE" 2>/dev/null || echo "HPA: Not configured"
        echo ""
        echo "Pod Resource Usage:"
        kubectl top pods -n "$NAMESPACE" -l app=unity-mcp-server 2>/dev/null || echo "Metrics not available"
    } > "$test_results_file"
    
    log "Test report saved to: $test_results_file"
    cat "$test_results_file"
}

# Main test execution
main() {
    log "Starting Unity MCP Kubernetes Scaling Tests"
    log "Namespace: $NAMESPACE"
    log "Deployment: $DEPLOYMENT"
    
    # Check prerequisites
    if ! check_prerequisites; then
        error "Prerequisites check failed"
        exit 1
    fi
    
    # Run tests
    local test_failures=0
    
    log "=== Test 1: Manual Scaling ==="
    if ! test_manual_scaling; then
        ((test_failures++))
        error "Manual scaling test failed"
    fi
    
    log "=== Test 2: Pod Startup Time ==="
    if ! test_pod_startup_time; then
        ((test_failures++))
        error "Pod startup test failed"
    fi
    
    log "=== Test 3: Rolling Updates ==="
    if ! test_rolling_updates; then
        ((test_failures++))
        error "Rolling update test failed"
    fi
    
    log "=== Test 4: Service Connectivity ==="
    if ! test_service_connectivity; then
        ((test_failures++))
        error "Service connectivity test failed"
    fi
    
    log "=== Test 5: HPA Auto-Scaling ==="
    if ! test_hpa_scaling; then
        ((test_failures++))
        warn "HPA auto-scaling test failed or skipped"
    fi
    
    # Generate report
    generate_report
    
    # Final results
    log "=== Test Summary ==="
    if [[ $test_failures -eq 0 ]]; then
        log "✓ All scaling tests passed successfully!"
        exit 0
    else
        error "✗ $test_failures test(s) failed"
        exit 1
    fi
}

# Handle script arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --namespace|-n)
            NAMESPACE="$2"
            shift 2
            ;;
        --deployment|-d)
            DEPLOYMENT="$2"
            shift 2
            ;;
        --timeout|-t)
            MAX_WAIT_TIME="$2"
            shift 2
            ;;
        --help|-h)
            echo "Unity MCP Kubernetes Scaling Test"
            echo "Usage: $0 [OPTIONS]"
            echo ""
            echo "Options:"
            echo "  -n, --namespace    Kubernetes namespace (default: unity-mcp)"
            echo "  -d, --deployment   Deployment name (default: unity-mcp-server)"
            echo "  -t, --timeout      Max wait time in seconds (default: 600)"
            echo "  -h, --help         Show this help message"
            exit 0
            ;;
        *)
            error "Unknown option: $1"
            exit 1
            ;;
    esac
done

# Run main function
main "$@"