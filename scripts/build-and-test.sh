#!/bin/bash
# Unity MCP Docker Build and Test Script
# Milestone 2: Complete build, test, and validation pipeline

set -euo pipefail

# Configuration
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
IMAGE_NAME="unity-mcp"
BUILD_TARGET="${1:-production}"
RUN_TESTS="${2:-true}"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

log() {
    echo -e "${GREEN}[$(date +'%H:%M:%S')] [BUILD]${NC} $1"
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

# Main execution
main() {
    log "Starting Unity MCP Docker build and test pipeline"
    log "Build target: $BUILD_TARGET"
    log "Run tests: $RUN_TESTS"
    
    cd "$PROJECT_ROOT"
    
    # Step 1: Build Docker image
    log "Building Docker image..."
    
    if [[ "$BUILD_TARGET" == "production" ]]; then
        DOCKERFILE="docker/Dockerfile.production"
    elif [[ "$BUILD_TARGET" == "dev" ]]; then
        DOCKERFILE="docker/Dockerfile.dev"
    else
        error "Invalid build target: $BUILD_TARGET (use 'production' or 'dev')"
        exit 1
    fi
    
    if [[ ! -f "$DOCKERFILE" ]]; then
        error "Dockerfile not found: $DOCKERFILE"
        exit 1
    fi
    
    build_start=$(date +%s)
    
    docker build \
        -f "$DOCKERFILE" \
        -t "${IMAGE_NAME}:${BUILD_TARGET}" \
        -t "${IMAGE_NAME}:latest" \
        --target "$BUILD_TARGET" \
        --build-arg UNITY_VERSION="2022.3.45f1" \
        --build-arg UNITY_CHANGESET="63b2b3067b8e" \
        --build-arg PYTHON_VERSION="3.11" \
        .
    
    build_duration=$(($(date +%s) - build_start))
    log "✅ Docker build completed in ${build_duration}s"
    
    # Step 2: Validate image
    log "Validating Docker image..."
    
    # Check image exists
    if ! docker image inspect "${IMAGE_NAME}:${BUILD_TARGET}" >/dev/null 2>&1; then
        error "Built image not found"
        exit 1
    fi
    
    # Check image size
    size_bytes=$(docker image inspect "${IMAGE_NAME}:${BUILD_TARGET}" --format='{{.Size}}')
    size_gb=$(echo "scale=2; $size_bytes / 1024 / 1024 / 1024" | bc -l)
    
    info "Image size: ${size_gb} GB"
    
    if (( $(echo "$size_gb > 2.0" | bc -l) )); then
        error "❌ Image size (${size_gb}GB) exceeds 2GB limit"
        exit 1
    else
        log "✅ Image size requirement met: ${size_gb}GB < 2GB"
    fi
    
    # Step 3: Run tests (if requested)
    if [[ "$RUN_TESTS" == "true" ]]; then
        log "Running comprehensive test suite..."
        
        test_start=$(date +%s)
        
        if python3 tests/docker/run_docker_tests.py; then
            test_duration=$(($(date +%s) - test_start))
            log "✅ All tests passed in ${test_duration}s"
        else
            test_duration=$(($(date +%s) - test_start))
            error "❌ Tests failed after ${test_duration}s"
            exit 1
        fi
    else
        warn "Skipping tests (RUN_TESTS=false)"
    fi
    
    # Step 4: Security scan (if tools available)
    if command -v trivy >/dev/null 2>&1; then
        log "Running security scan..."
        
        mkdir -p security-reports
        
        if trivy image \
            --format table \
            --severity HIGH,CRITICAL \
            --ignore-unfixed \
            "${IMAGE_NAME}:${BUILD_TARGET}"; then
            log "✅ Security scan completed"
        else
            warn "⚠️  Security scan found issues"
        fi
    else
        warn "Trivy not found, skipping security scan"
        info "Install with: curl -sfL https://raw.githubusercontent.com/aquasecurity/trivy/main/contrib/install.sh | sh -s -- -b /usr/local/bin"
    fi
    
    # Step 5: Summary
    total_duration=$(($(date +%s) - $(date -d '1 minute ago' +%s)))
    
    echo ""
    echo "🎉 MILESTONE 2 BUILD COMPLETED SUCCESSFULLY"
    echo "==========================================="
    echo "Build target: $BUILD_TARGET"
    echo "Image: ${IMAGE_NAME}:${BUILD_TARGET}"
    echo "Size: ${size_gb} GB (< 2GB ✅)"
    echo "Build time: ${build_duration}s"
    if [[ "$RUN_TESTS" == "true" ]]; then
        echo "Test time: ${test_duration}s"
    fi
    echo ""
    echo "🚀 Ready for deployment!"
    echo ""
    echo "Quick start commands:"
    echo "  docker run -d -p 8080:8080 ${IMAGE_NAME}:${BUILD_TARGET}"
    echo "  curl http://localhost:8080/health"
    echo ""
    echo "Next steps:"
    echo "  - Deploy with docker-compose: docker-compose -f docker-compose.production.yml up -d"
    echo "  - Run load tests: python3 load_test.py"
    echo "  - Proceed to Milestone 3: Kubernetes Setup"
}

# Help function
show_help() {
    cat << EOF
Unity MCP Docker Build and Test Script

Usage: $0 [BUILD_TARGET] [RUN_TESTS]

Arguments:
  BUILD_TARGET    Docker build target (production|dev) [default: production]
  RUN_TESTS       Whether to run tests (true|false) [default: true]

Examples:
  $0                          # Build production image and run tests
  $0 production true          # Same as above
  $0 dev false                # Build dev image, skip tests
  $0 production false         # Build production, skip tests

Requirements:
  - Docker 20.10+
  - Python 3.11+
  - bc (for calculations)
  - curl (for tests)

Optional:
  - trivy (for security scanning)

EOF
}

# Handle help flag
if [[ "${1:-}" == "--help" ]] || [[ "${1:-}" == "-h" ]]; then
    show_help
    exit 0
fi

# Check requirements
for cmd in docker python3 bc curl; do
    if ! command -v "$cmd" >/dev/null 2>&1; then
        error "Required command not found: $cmd"
        exit 1
    fi
done

# Run main function
main "$@"