#!/bin/bash
# Local Docker Testing Script for Unity MCP
set -euo pipefail

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

log() { echo -e "${GREEN}[TEST]${NC} $1"; }
warn() { echo -e "${YELLOW}[WARN]${NC} $1"; }
error() { echo -e "${RED}[ERROR]${NC} $1"; }
info() { echo -e "${BLUE}[INFO]${NC} $1"; }

CONTAINER_NAME="unity-mcp-local-test"
IMAGE_TAG="unity-mcp:local-test"
HTTP_PORT=8080
UNITY_PORT=6400

cleanup() {
    log "Cleaning up previous test containers..."
    docker stop $CONTAINER_NAME 2>/dev/null || true
    docker rm $CONTAINER_NAME 2>/dev/null || true
}

# Parse command line arguments
PRODUCTION_MODE=false
UNITY_LICENSE_FILE=""

while [[ $# -gt 0 ]]; do
    case $1 in
        --production)
            PRODUCTION_MODE=true
            shift
            ;;
        --license)
            UNITY_LICENSE_FILE="$2"
            shift 2
            ;;
        --help)
            echo "Usage: $0 [options]"
            echo "Options:"
            echo "  --production         Use production build (requires Unity license)"
            echo "  --license FILE       Path to Unity license file (.ulf)"
            echo "  --help              Show this help message"
            exit 0
            ;;
        *)
            error "Unknown option: $1"
            exit 1
            ;;
    esac
done

log "Unity MCP Docker Local Test"
log "============================"

# Cleanup any existing containers
cleanup

if [[ "$PRODUCTION_MODE" == "true" ]]; then
    if [[ -z "$UNITY_LICENSE_FILE" ]]; then
        error "Production mode requires Unity license file!"
        echo "Usage: $0 --production --license /path/to/Unity_v6000.x.ulf"
        exit 1
    fi
    
    if [[ ! -f "$UNITY_LICENSE_FILE" ]]; then
        error "License file not found: $UNITY_LICENSE_FILE"
        exit 1
    fi
    
    log "Building production Docker image..."
    export DOCKER_BUILDKIT=1
    docker build \
        --secret id=unity_license,src="$UNITY_LICENSE_FILE" \
        -f docker/Dockerfile.production \
        -t $IMAGE_TAG \
        --build-arg UNITY_VERSION=6000.0.3f1 \
        .
    
    log "Starting production container..."
    docker run -d \
        --name $CONTAINER_NAME \
        -p $HTTP_PORT:8080 \
        -p $UNITY_PORT:6400 \
        -e LOG_LEVEL=DEBUG \
        -e UNITY_HEADLESS=true \
        -e UNITY_MCP_AUTOSTART=true \
        -v "$UNITY_LICENSE_FILE:/tmp/unity.ulf:ro" \
        -e UNITY_LICENSE_FILE=/tmp/unity.ulf \
        $IMAGE_TAG
else
    log "Building CI test Docker image..."
    docker build \
        -f docker/Dockerfile.ci \
        -t $IMAGE_TAG \
        --build-arg UNITY_VERSION=6000.0.3f1 \
        .
    
    log "Starting CI test container..."
    docker run -d \
        --name $CONTAINER_NAME \
        -p $HTTP_PORT:8080 \
        -p $UNITY_PORT:6400 \
        -e CI_MODE=true \
        -e LOG_LEVEL=DEBUG \
        -e UNITY_PROJECT_PATH="" \
        $IMAGE_TAG
fi

log "Container started. Waiting for services to be ready..."

# Wait for container to be ready
timeout=30
elapsed=0
while [[ $elapsed -lt $timeout ]]; do
    if curl -f -s http://localhost:$HTTP_PORT/health >/dev/null 2>&1; then
        log "✅ Container is ready!"
        break
    fi
    sleep 2
    elapsed=$((elapsed + 2))
done

if [[ $elapsed -ge $timeout ]]; then
    error "❌ Container failed to start within ${timeout}s"
    echo "Container logs:"
    docker logs $CONTAINER_NAME
    cleanup
    exit 1
fi

log "Running tests..."

# Test 1: Health check
info "Test 1: Health endpoint"
health_response=$(curl -s http://localhost:$HTTP_PORT/health)
echo "Response: $health_response"
if echo "$health_response" | grep -q '"status".*"healthy"'; then
    log "✅ Health check passed"
else
    error "❌ Health check failed"
fi

# Test 2: Command execution
info "Test 2: Command execution"
cmd_response=$(curl -s -X POST http://localhost:$HTTP_PORT/execute-command \
    -H "Content-Type: application/json" \
    -d '{
        "action": "ping",
        "params": {"message": "local test"},
        "userId": "local-user"
    }')
echo "Response: $cmd_response"
if echo "$cmd_response" | grep -q '"commandId"'; then
    log "✅ Command execution passed"
else
    error "❌ Command execution failed"
fi

# Test 3: Container processes
info "Test 3: Container processes"
echo "Running processes in container:"
docker exec $CONTAINER_NAME ps aux

# Test 4: Container logs
info "Test 4: Recent container logs"
echo "Last 20 lines of logs:"
docker logs --tail 20 $CONTAINER_NAME

log "Test completed!"
log "Container is running on:"
log "  HTTP API: http://localhost:$HTTP_PORT"
log "  Health:   http://localhost:$HTTP_PORT/health"
log "  Unity:    localhost:$UNITY_PORT (if production mode)"

log "To stop the container: docker stop $CONTAINER_NAME"
log "To view logs: docker logs $CONTAINER_NAME"
log "To access shell: docker exec -it $CONTAINER_NAME bash"