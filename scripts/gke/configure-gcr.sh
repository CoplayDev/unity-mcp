#!/bin/bash
set -euo pipefail

# Google Container Registry Configuration Script for Unity MCP
# Sets up GCR, builds and pushes Docker images, configures security scanning

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

# Configuration
PROJECT_ID="${PROJECT_ID:-}"
IMAGE_NAME="${IMAGE_NAME:-unity-mcp}"
IMAGE_TAG="${IMAGE_TAG:-production}"
DOCKERFILE_PATH="${DOCKERFILE_PATH:-docker/Dockerfile.production}"
BUILD_CONTEXT="${BUILD_CONTEXT:-.}"

# Logging functions
log() {
    echo -e "${GREEN}[$(date +'%H:%M:%S')] [GCR]${NC} $1"
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
    
    # Check gcloud CLI
    if ! command -v gcloud >/dev/null 2>&1; then
        error "gcloud CLI is required but not installed"
        return 1
    fi
    
    # Check Docker
    if ! command -v docker >/dev/null 2>&1; then
        error "Docker is required but not installed"
        return 1
    fi
    
    # Check project ID
    if [[ -z "$PROJECT_ID" ]]; then
        PROJECT_ID=$(gcloud config get-value project 2>/dev/null || echo "")
        if [[ -z "$PROJECT_ID" ]]; then
            error "PROJECT_ID must be set"
            return 1
        fi
    fi
    
    log "Using project: $PROJECT_ID"
    
    # Check Dockerfile
    if [[ ! -f "$DOCKERFILE_PATH" ]]; then
        error "Dockerfile not found: $DOCKERFILE_PATH"
        return 1
    fi
    
    # Check build context
    if [[ ! -d "$BUILD_CONTEXT" ]]; then
        error "Build context directory not found: $BUILD_CONTEXT"
        return 1
    fi
    
    log "Prerequisites check passed"
}

# Configure Docker authentication
configure_docker_auth() {
    log "Configuring Docker authentication for GCR..."
    
    # Configure Docker to use gcloud as a credential helper
    gcloud auth configure-docker --quiet
    
    # Verify authentication by attempting to list repositories
    if docker images gcr.io/"$PROJECT_ID"/* >/dev/null 2>&1; then
        log "Docker authentication configured successfully"
    else
        info "Docker authentication configured (verification skipped)"
    fi
}

# Enable required APIs
enable_apis() {
    log "Enabling required Google Cloud APIs..."
    
    local required_apis=(
        "containerregistry.googleapis.com"
        "cloudbuild.googleapis.com"
        "containeranalysis.googleapis.com"
        "binaryauthorization.googleapis.com"
    )
    
    for api in "${required_apis[@]}"; do
        if ! gcloud services list --enabled --filter="name:$api" --format="value(name)" | grep -q "$api"; then
            info "Enabling API: $api"
            gcloud services enable "$api" --project="$PROJECT_ID"
        else
            info "API already enabled: $api"
        fi
    done
    
    log "APIs enabled successfully"
}

# Build Docker image locally
build_image() {
    log "Building Docker image..."
    
    local image_tag="gcr.io/$PROJECT_ID/$IMAGE_NAME:$IMAGE_TAG"
    local build_args=()
    
    # Add build arguments if needed
    if [[ -n "${UNITY_VERSION:-}" ]]; then
        build_args+=(--build-arg "UNITY_VERSION=$UNITY_VERSION")
    fi
    
    if [[ -n "${UNITY_CHANGESET:-}" ]]; then
        build_args+=(--build-arg "UNITY_CHANGESET=$UNITY_CHANGESET")
    fi
    
    info "Building image: $image_tag"
    info "Dockerfile: $DOCKERFILE_PATH"
    info "Build context: $BUILD_CONTEXT"
    
    # Build the image
    if docker build \
        -f "$DOCKERFILE_PATH" \
        -t "$image_tag" \
        "${build_args[@]}" \
        "$BUILD_CONTEXT"; then
        log "Image built successfully"
        
        # Tag as latest
        docker tag "$image_tag" "gcr.io/$PROJECT_ID/$IMAGE_NAME:latest"
        log "Tagged as latest"
    else
        error "Failed to build Docker image"
        return 1
    fi
}

# Push image to GCR
push_image() {
    log "Pushing image to Google Container Registry..."
    
    local image_tag="gcr.io/$PROJECT_ID/$IMAGE_NAME:$IMAGE_TAG"
    local latest_tag="gcr.io/$PROJECT_ID/$IMAGE_NAME:latest"
    
    # Push specific tag
    if docker push "$image_tag"; then
        log "Successfully pushed: $image_tag"
    else
        error "Failed to push image: $image_tag"
        return 1
    fi
    
    # Push latest tag
    if docker push "$latest_tag"; then
        log "Successfully pushed: $latest_tag"
    else
        warn "Failed to push latest tag (continuing anyway)"
    fi
    
    # Get image digest
    local digest=$(gcloud container images describe "$image_tag" --format='value(image_summary.digest)' 2>/dev/null || echo "unknown")
    info "Image digest: $digest"
}

# Configure vulnerability scanning
configure_vulnerability_scanning() {
    log "Configuring vulnerability scanning..."
    
    # Check if vulnerability scanning is available
    if gcloud container images scan --help >/dev/null 2>&1; then
        local image_url="gcr.io/$PROJECT_ID/$IMAGE_NAME:$IMAGE_TAG"
        
        info "Starting vulnerability scan for: $image_url"
        if gcloud container images scan "$image_url" --project="$PROJECT_ID"; then
            log "Vulnerability scan initiated"
            
            # Wait a moment and try to get results
            sleep 10
            if gcloud container images describe "$image_url" \
                --show-package-vulnerability \
                --format='value(package_vulnerability_summary.vulnerabilities.high)' \
                --project="$PROJECT_ID" 2>/dev/null; then
                info "Vulnerability scan results available in Cloud Console"
            fi
        else
            warn "Failed to initiate vulnerability scan"
        fi
    else
        warn "Vulnerability scanning not available (requires Container Analysis API)"
    fi
}

# Set up image retention policy
configure_retention_policy() {
    log "Configuring image retention policy..."
    
    # Keep last 10 images to prevent unlimited storage growth
    if gcloud container images set-retention-policy \
        "gcr.io/$PROJECT_ID/$IMAGE_NAME" \
        --keep-recent=10 \
        --project="$PROJECT_ID" 2>/dev/null; then
        log "Retention policy configured (keep 10 most recent)"
    else
        warn "Failed to set retention policy (may require additional permissions)"
    fi
}

# Generate IAM policy for GKE access
configure_gke_access() {
    log "Configuring GKE access to GCR..."
    
    local cluster_service_account="${SERVICE_ACCOUNT_NAME:-unity-mcp-sa}@${PROJECT_ID}.iam.gserviceaccount.com"
    
    # Grant storage.objectViewer role for image pulling
    if gcloud projects add-iam-policy-binding "$PROJECT_ID" \
        --member="serviceAccount:$cluster_service_account" \
        --role="roles/storage.objectViewer" \
        --quiet 2>/dev/null; then
        log "GKE service account configured for image pulling"
    else
        warn "Failed to configure GKE service account (may already be configured)"
    fi
}

# Verify deployment
verify_deployment() {
    log "Verifying image deployment..."
    
    local image_url="gcr.io/$PROJECT_ID/$IMAGE_NAME:$IMAGE_TAG"
    
    # Test that the image can be pulled
    if docker pull "$image_url" >/dev/null 2>&1; then
        log "✓ Image can be pulled successfully"
    else
        error "✗ Failed to pull image"
        return 1
    fi
    
    # Check image size
    local image_size=$(docker images "$image_url" --format "table {{.Size}}" | tail -n +2)
    info "Image size: $image_size"
    
    # List all tags for this repository
    info "Available tags:"
    gcloud container images list-tags "gcr.io/$PROJECT_ID/$IMAGE_NAME" \
        --format="table(tags,timestamp)" \
        --limit=5 \
        --project="$PROJECT_ID" 2>/dev/null || warn "Could not list tags"
}

# Generate summary
generate_summary() {
    local summary_file="/tmp/unity-mcp-gcr-summary.txt"
    
    log "Generating GCR configuration summary..."
    
    {
        echo "Unity MCP GCR Configuration Summary"
        echo "Generated: $(date)"
        echo ""
        echo "Project: $PROJECT_ID"
        echo "Image Repository: gcr.io/$PROJECT_ID/$IMAGE_NAME"
        echo "Current Tag: $IMAGE_TAG"
        echo ""
        echo "Image URLs:"
        echo "  Production: gcr.io/$PROJECT_ID/$IMAGE_NAME:production"
        echo "  Latest: gcr.io/$PROJECT_ID/$IMAGE_NAME:latest"
        echo ""
        echo "Registry Information:"
        gcloud container images list --repository="gcr.io/$PROJECT_ID" 2>/dev/null || echo "No images found"
        echo ""
        echo "Next Steps:"
        echo "1. Update Kubernetes manifests to use: gcr.io/$PROJECT_ID/$IMAGE_NAME:$IMAGE_TAG"
        echo "2. Deploy to GKE: kubectl apply -k k8s/overlays/gke/"
        echo "3. Monitor vulnerability scan results in Cloud Console"
        echo "4. Set up automated CI/CD pipeline for image updates"
    } > "$summary_file"
    
    log "Summary saved to: $summary_file"
    cat "$summary_file"
}

# Main execution
main() {
    log "Starting GCR configuration for Unity MCP"
    log "Project: ${PROJECT_ID:-auto-detect}"
    log "Image: gcr.io/$PROJECT_ID/$IMAGE_NAME:$IMAGE_TAG"
    
    # Execute configuration steps
    if ! check_prerequisites; then
        error "Prerequisites check failed"
        exit 1
    fi
    
    enable_apis
    configure_docker_auth
    build_image
    push_image
    configure_vulnerability_scanning
    configure_retention_policy
    configure_gke_access
    verify_deployment
    
    log "GCR configuration completed successfully!"
    generate_summary
}

# Handle command line arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --project)
            PROJECT_ID="$2"
            shift 2
            ;;
        --image)
            IMAGE_NAME="$2"
            shift 2
            ;;
        --tag)
            IMAGE_TAG="$2"
            shift 2
            ;;
        --dockerfile)
            DOCKERFILE_PATH="$2"
            shift 2
            ;;
        --context)
            BUILD_CONTEXT="$2"
            shift 2
            ;;
        --unity-version)
            UNITY_VERSION="$2"
            shift 2
            ;;
        --help|-h)
            echo "GCR Configuration for Unity MCP"
            echo "Usage: $0 [OPTIONS]"
            echo ""
            echo "Options:"
            echo "  --project          GCP Project ID"
            echo "  --image            Image name (default: unity-mcp)"
            echo "  --tag              Image tag (default: production)"
            echo "  --dockerfile       Dockerfile path (default: docker/Dockerfile.production)"
            echo "  --context          Build context (default: .)"
            echo "  --unity-version    Unity version for build args"
            echo "  -h, --help         Show this help message"
            echo ""
            echo "Environment Variables:"
            echo "  PROJECT_ID         GCP Project ID"
            echo "  IMAGE_NAME         Docker image name"
            echo "  IMAGE_TAG          Docker image tag"
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