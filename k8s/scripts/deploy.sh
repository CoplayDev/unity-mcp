#!/bin/bash
set -euo pipefail

# Kubernetes Deployment Script for Unity MCP
# Deploys Unity MCP to GKE with proper configuration and validation

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

# Configuration
NAMESPACE="${NAMESPACE:-unity-mcp}"
OVERLAY="${OVERLAY:-gke}"
PROJECT_ID="${PROJECT_ID:-}"
UNITY_LICENSE_FILE="${UNITY_LICENSE_FILE:-}"
DRY_RUN="${DRY_RUN:-false}"
WAIT_TIMEOUT="${WAIT_TIMEOUT:-600}"

# Logging functions
log() {
    echo -e "${GREEN}[$(date +'%H:%M:%S')] [DEPLOY]${NC} $1"
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
    log "Checking deployment prerequisites..."
    
    # Check kubectl
    if ! command -v kubectl >/dev/null 2>&1; then
        error "kubectl is required but not installed"
        return 1
    fi
    
    # Check kustomize (built into kubectl)
    if ! kubectl kustomize --help >/dev/null 2>&1; then
        error "kubectl with kustomize support is required"
        return 1
    fi
    
    # Check cluster connectivity
    if ! kubectl cluster-info >/dev/null 2>&1; then
        error "Unable to connect to Kubernetes cluster"
        error "Run: gcloud container clusters get-credentials CLUSTER_NAME --zone ZONE"
        return 1
    fi
    
    # Check overlay path
    local overlay_path="k8s/overlays/$OVERLAY"
    if [[ ! -d "$overlay_path" ]]; then
        error "Overlay directory not found: $overlay_path"
        return 1
    fi
    
    # Check kustomization file
    if [[ ! -f "$overlay_path/kustomization.yaml" ]]; then
        error "Kustomization file not found: $overlay_path/kustomization.yaml"
        return 1
    fi
    
    # Get project ID if not set
    if [[ -z "$PROJECT_ID" ]] && command -v gcloud >/dev/null 2>&1; then
        PROJECT_ID=$(gcloud config get-value project 2>/dev/null || echo "")
    fi
    
    if [[ -z "$PROJECT_ID" ]]; then
        warn "PROJECT_ID not set - using placeholder values"
    else
        info "Using project: $PROJECT_ID"
    fi
    
    log "Prerequisites check passed"
}

# Create namespace if it doesn't exist
ensure_namespace() {
    log "Ensuring namespace exists: $NAMESPACE"
    
    if ! kubectl get namespace "$NAMESPACE" >/dev/null 2>&1; then
        if [[ "$DRY_RUN" == "true" ]]; then
            info "[DRY RUN] Would create namespace: $NAMESPACE"
        else
            kubectl create namespace "$NAMESPACE"
            kubectl label namespace "$NAMESPACE" name="$NAMESPACE" environment=production platform=gke --overwrite
            log "Namespace created: $NAMESPACE"
        fi
    else
        info "Namespace already exists: $NAMESPACE"
    fi
}

# Create Unity license secret
create_license_secret() {
    if [[ -z "$UNITY_LICENSE_FILE" ]]; then
        warn "UNITY_LICENSE_FILE not specified - Unity license secret not created"
        warn "To create manually: kubectl create secret generic unity-license --from-file=license.ulf=YOUR_LICENSE_FILE -n $NAMESPACE"
        return 0
    fi
    
    if [[ ! -f "$UNITY_LICENSE_FILE" ]]; then
        error "Unity license file not found: $UNITY_LICENSE_FILE"
        return 1
    fi
    
    log "Creating Unity license secret..."
    
    # Check if secret already exists
    if kubectl get secret unity-license -n "$NAMESPACE" >/dev/null 2>&1; then
        warn "Unity license secret already exists - deleting and recreating"
        if [[ "$DRY_RUN" != "true" ]]; then
            kubectl delete secret unity-license -n "$NAMESPACE"
        fi
    fi
    
    if [[ "$DRY_RUN" == "true" ]]; then
        info "[DRY RUN] Would create Unity license secret from: $UNITY_LICENSE_FILE"
    else
        kubectl create secret generic unity-license \
            --from-file=license.ulf="$UNITY_LICENSE_FILE" \
            -n "$NAMESPACE"
        log "Unity license secret created"
    fi
}

# Update kustomization with project ID
update_project_configuration() {
    if [[ -z "$PROJECT_ID" ]]; then
        warn "Skipping project ID configuration (not specified)"
        return 0
    fi
    
    log "Updating configuration for project: $PROJECT_ID"
    
    local overlay_path="k8s/overlays/$OVERLAY"
    local kustomization_file="$overlay_path/kustomization.yaml"
    
    # Create temporary kustomization file with updated project ID
    local temp_kustomization="/tmp/kustomization-$PROJECT_ID.yaml"
    
    if sed "s/YOUR_PROJECT_ID/$PROJECT_ID/g" "$kustomization_file" > "$temp_kustomization"; then
        info "Configuration updated for project: $PROJECT_ID"
    else
        error "Failed to update project configuration"
        return 1
    fi
    
    # Use the temporary file for deployment
    export KUSTOMIZATION_FILE="$temp_kustomization"
}

# Validate manifests
validate_manifests() {
    log "Validating Kubernetes manifests..."
    
    local overlay_path="k8s/overlays/$OVERLAY"
    local kustomization_file="${KUSTOMIZATION_FILE:-$overlay_path/kustomization.yaml}"
    
    # Generate manifests and validate
    if kubectl kustomize "$overlay_path" > /tmp/unity-mcp-manifests.yaml; then
        log "Manifests generated successfully"
        
        # Basic validation
        if kubectl apply --dry-run=client -f /tmp/unity-mcp-manifests.yaml >/dev/null 2>&1; then
            log "✓ Manifest validation passed"
        else
            error "✗ Manifest validation failed"
            kubectl apply --dry-run=client -f /tmp/unity-mcp-manifests.yaml
            return 1
        fi
    else
        error "Failed to generate manifests with kustomize"
        return 1
    fi
}

# Deploy to Kubernetes
deploy_manifests() {
    log "Deploying Unity MCP to Kubernetes..."
    
    local overlay_path="k8s/overlays/$OVERLAY"
    
    if [[ "$DRY_RUN" == "true" ]]; then
        info "[DRY RUN] Would deploy the following manifests:"
        kubectl kustomize "$overlay_path"
        return 0
    fi
    
    # Apply manifests
    if kubectl apply -k "$overlay_path"; then
        log "Manifests applied successfully"
    else
        error "Failed to apply manifests"
        return 1
    fi
    
    # Wait for deployment to be ready
    log "Waiting for deployment to be ready..."
    
    if kubectl wait --for=condition=available \
        --timeout="${WAIT_TIMEOUT}s" \
        deployment/unity-mcp-server \
        -n "$NAMESPACE"; then
        log "✓ Deployment is ready"
    else
        error "✗ Deployment failed to become ready within ${WAIT_TIMEOUT}s"
        show_deployment_status
        return 1
    fi
}

# Show deployment status
show_deployment_status() {
    log "Current deployment status:"
    
    echo ""
    info "Namespace resources:"
    kubectl get all -n "$NAMESPACE" 2>/dev/null || warn "Failed to get namespace resources"
    
    echo ""
    info "Pod status:"
    kubectl get pods -n "$NAMESPACE" -l app=unity-mcp-server -o wide 2>/dev/null || warn "Failed to get pod status"
    
    echo ""
    info "Events (last 10):"
    kubectl get events -n "$NAMESPACE" --sort-by=.metadata.creationTimestamp | tail -10 2>/dev/null || warn "Failed to get events"
    
    echo ""
    info "HPA status:"
    kubectl get hpa -n "$NAMESPACE" 2>/dev/null || warn "HPA not found"
}

# Verify deployment health
verify_deployment() {
    log "Verifying deployment health..."
    
    # Check pod readiness
    local ready_pods=$(kubectl get pods -n "$NAMESPACE" -l app=unity-mcp-server -o jsonpath='{.items[?(@.status.phase=="Running")].metadata.name}' 2>/dev/null | wc -w)
    local total_pods=$(kubectl get pods -n "$NAMESPACE" -l app=unity-mcp-server -o name 2>/dev/null | wc -l)
    
    info "Ready pods: $ready_pods/$total_pods"
    
    if [[ $ready_pods -eq 0 ]]; then
        error "No pods are ready"
        return 1
    fi
    
    # Test service connectivity
    log "Testing service connectivity..."
    
    # Port forward to test
    kubectl port-forward -n "$NAMESPACE" service/unity-mcp-server 8080:80 &
    local port_forward_pid=$!
    sleep 5
    
    # Test health endpoint
    local health_ok=false
    if curl -f -s --max-time 10 http://localhost:8080/health >/dev/null 2>&1; then
        log "✓ Health endpoint responding"
        health_ok=true
    else
        warn "✗ Health endpoint not responding"
    fi
    
    # Clean up port forward
    kill $port_forward_pid 2>/dev/null || true
    wait $port_forward_pid 2>/dev/null || true
    
    if [[ "$health_ok" == true ]]; then
        log "✓ Deployment verification passed"
        return 0
    else
        error "✗ Deployment verification failed"
        return 1
    fi
}

# Get access information
get_access_info() {
    log "Getting access information..."
    
    # Get service information
    local service_ip=""
    local ingress_ip=""
    
    # Check for LoadBalancer service
    if kubectl get service unity-mcp-server -n "$NAMESPACE" -o jsonpath='{.status.loadBalancer.ingress[0].ip}' 2>/dev/null; then
        service_ip=$(kubectl get service unity-mcp-server -n "$NAMESPACE" -o jsonpath='{.status.loadBalancer.ingress[0].ip}' 2>/dev/null || echo "")
    fi
    
    # Check for Ingress
    if kubectl get ingress unity-mcp-ingress -n "$NAMESPACE" -o jsonpath='{.status.loadBalancer.ingress[0].ip}' 2>/dev/null; then
        ingress_ip=$(kubectl get ingress unity-mcp-ingress -n "$NAMESPACE" -o jsonpath='{.status.loadBalancer.ingress[0].ip}' 2>/dev/null || echo "")
    fi
    
    echo ""
    info "Access Information:"
    if [[ -n "$service_ip" ]]; then
        info "  Service IP: $service_ip"
        info "  Health URL: http://$service_ip/health"
    fi
    
    if [[ -n "$ingress_ip" ]]; then
        info "  Ingress IP: $ingress_ip"
        info "  Configure DNS to point your domain to this IP"
    fi
    
    info "  Port Forward: kubectl port-forward -n $NAMESPACE service/unity-mcp-server 8080:80"
    info "  Local URL: http://localhost:8080/health"
    
    echo ""
    info "Monitoring Commands:"
    info "  Watch pods: kubectl get pods -n $NAMESPACE -w"
    info "  View logs: kubectl logs -n $NAMESPACE -l app=unity-mcp-server -f"
    info "  Check HPA: kubectl get hpa -n $NAMESPACE"
}

# Generate deployment summary
generate_summary() {
    local summary_file="/tmp/unity-mcp-deployment-summary.txt"
    
    log "Generating deployment summary..."
    
    {
        echo "Unity MCP Kubernetes Deployment Summary"
        echo "Generated: $(date)"
        echo ""
        echo "Configuration:"
        echo "  Namespace: $NAMESPACE"
        echo "  Overlay: $OVERLAY"
        echo "  Project ID: ${PROJECT_ID:-not set}"
        echo ""
        echo "Deployment Status:"
        kubectl get deployment unity-mcp-server -n "$NAMESPACE" 2>/dev/null || echo "Deployment not found"
        echo ""
        echo "Pod Status:"
        kubectl get pods -n "$NAMESPACE" -l app=unity-mcp-server 2>/dev/null || echo "No pods found"
        echo ""
        echo "Service Status:"
        kubectl get service unity-mcp-server -n "$NAMESPACE" 2>/dev/null || echo "Service not found"
        echo ""
        echo "HPA Status:"
        kubectl get hpa -n "$NAMESPACE" 2>/dev/null || echo "HPA not found"
    } > "$summary_file"
    
    log "Deployment summary saved to: $summary_file"
    cat "$summary_file"
}

# Main execution
main() {
    log "Starting Unity MCP Kubernetes deployment"
    log "Namespace: $NAMESPACE"
    log "Overlay: $OVERLAY"
    log "Dry run: $DRY_RUN"
    
    # Execute deployment steps
    if ! check_prerequisites; then
        error "Prerequisites check failed"
        exit 1
    fi
    
    ensure_namespace
    create_license_secret
    update_project_configuration
    validate_manifests
    deploy_manifests
    
    if [[ "$DRY_RUN" != "true" ]]; then
        show_deployment_status
        
        if verify_deployment; then
            log "✓ Deployment completed successfully!"
            get_access_info
        else
            error "✗ Deployment verification failed"
            show_deployment_status
            exit 1
        fi
    else
        log "Dry run completed - no resources were created"
    fi
    
    generate_summary
}

# Handle command line arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --namespace|-n)
            NAMESPACE="$2"
            shift 2
            ;;
        --overlay|-o)
            OVERLAY="$2"
            shift 2
            ;;
        --project)
            PROJECT_ID="$2"
            shift 2
            ;;
        --license)
            UNITY_LICENSE_FILE="$2"
            shift 2
            ;;
        --dry-run)
            DRY_RUN="true"
            shift
            ;;
        --timeout)
            WAIT_TIMEOUT="$2"
            shift 2
            ;;
        --help|-h)
            echo "Unity MCP Kubernetes Deployment"
            echo "Usage: $0 [OPTIONS]"
            echo ""
            echo "Options:"
            echo "  -n, --namespace    Kubernetes namespace (default: unity-mcp)"
            echo "  -o, --overlay      Kustomize overlay (default: gke)"
            echo "  --project          GCP Project ID"
            echo "  --license          Unity license file path"
            echo "  --dry-run          Preview deployment without applying"
            echo "  --timeout          Deployment timeout in seconds (default: 600)"
            echo "  -h, --help         Show this help message"
            echo ""
            echo "Environment Variables:"
            echo "  PROJECT_ID         GCP Project ID"
            echo "  UNITY_LICENSE_FILE Unity license file path"
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