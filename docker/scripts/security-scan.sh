#!/bin/bash
# Security scanning script for Unity MCP Docker images
# Uses Trivy for vulnerability scanning and basic security checks

set -euo pipefail

# Configuration
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
IMAGE_NAME="${1:-unity-mcp:latest}"
SCAN_OUTPUT_DIR="${PROJECT_ROOT}/security-reports"
TIMESTAMP=$(date +%Y%m%d_%H%M%S)

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

log() {
    echo -e "${GREEN}[$(date +'%H:%M:%S')] [SECURITY]${NC} $1"
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

# Check if required tools are installed
check_tools() {
    log "Checking required security tools..."
    
    # Check for Trivy
    if ! command -v trivy >/dev/null 2>&1; then
        warn "Trivy not found, installing..."
        
        # Install Trivy based on OS
        if [[ "$OSTYPE" == "linux-gnu"* ]]; then
            curl -sfL https://raw.githubusercontent.com/aquasecurity/trivy/main/contrib/install.sh | sudo sh -s -- -b /usr/local/bin
        elif [[ "$OSTYPE" == "darwin"* ]]; then
            if command -v brew >/dev/null 2>&1; then
                brew install trivy
            else
                error "Please install Trivy manually on macOS"
                exit 1
            fi
        else
            error "Unsupported OS for automatic Trivy installation"
            exit 1
        fi
    fi
    
    # Check for Docker
    if ! command -v docker >/dev/null 2>&1; then
        error "Docker not found"
        exit 1
    fi
    
    log "Security tools check passed"
}

# Create output directory
prepare_output() {
    mkdir -p "$SCAN_OUTPUT_DIR"
    log "Security reports will be saved to: $SCAN_OUTPUT_DIR"
}

# Check if image exists
check_image_exists() {
    if ! docker image inspect "$IMAGE_NAME" >/dev/null 2>&1; then
        error "Docker image '$IMAGE_NAME' not found"
        error "Please build the image first: docker build -f docker/Dockerfile.production -t unity-mcp:latest ."
        exit 1
    fi
    log "Docker image '$IMAGE_NAME' found"
}

# Trivy vulnerability scan
run_trivy_scan() {
    log "Running Trivy vulnerability scan..."
    
    local output_file="$SCAN_OUTPUT_DIR/trivy-scan-$TIMESTAMP.json"
    local html_file="$SCAN_OUTPUT_DIR/trivy-scan-$TIMESTAMP.html"
    
    # Run comprehensive Trivy scan
    trivy image \
        --format json \
        --output "$output_file" \
        --severity HIGH,CRITICAL \
        --ignore-unfixed \
        "$IMAGE_NAME"
    
    # Generate HTML report
    trivy image \
        --format template \
        --template '@/contrib/html.tpl' \
        --output "$html_file" \
        --severity HIGH,CRITICAL \
        --ignore-unfixed \
        "$IMAGE_NAME" 2>/dev/null || true
    
    # Analyze results
    local critical_count=$(jq -r '[.Results[]?.Vulnerabilities[]? | select(.Severity == "CRITICAL")] | length' "$output_file" 2>/dev/null || echo "0")
    local high_count=$(jq -r '[.Results[]?.Vulnerabilities[]? | select(.Severity == "HIGH")] | length' "$output_file" 2>/dev/null || echo "0")
    
    log "Vulnerability scan completed"
    info "Critical vulnerabilities: $critical_count"
    info "High vulnerabilities: $high_count"
    info "Detailed report: $output_file"
    
    # Check if we meet security requirements
    if [[ $critical_count -gt 0 ]]; then
        error "SECURITY REQUIREMENT FAILED: Found $critical_count critical vulnerabilities"
        return 1
    else
        log "✅ No critical vulnerabilities found"
        return 0
    fi
}

# Check Docker image configuration
check_image_config() {
    log "Checking Docker image security configuration..."
    
    local config_file="$SCAN_OUTPUT_DIR/image-config-$TIMESTAMP.json"
    
    # Extract image configuration
    docker image inspect "$IMAGE_NAME" > "$config_file"
    
    local user=$(jq -r '.[0].Config.User // "root"' "$config_file")
    local exposed_ports=$(jq -r '.[0].Config.ExposedPorts // {} | keys | join(",")' "$config_file")
    local volumes=$(jq -r '.[0].Config.Volumes // {} | keys | join(",")' "$config_file")
    
    info "Container user: $user"
    info "Exposed ports: ${exposed_ports:-none}"
    info "Volumes: ${volumes:-none}"
    
    # Security checks
    local security_issues=0
    
    # Check if running as non-root
    if [[ "$user" == "root" || "$user" == "" ]]; then
        error "❌ SECURITY ISSUE: Container runs as root user"
        ((security_issues++))
    else
        log "✅ Container runs as non-root user: $user"
    fi
    
    # Check for unnecessary exposed ports
    if [[ "$exposed_ports" == *"22"* ]]; then
        warn "⚠️  SSH port 22 is exposed - consider if this is necessary"
    fi
    
    return $security_issues
}

# Check image size
check_image_size() {
    log "Checking image size requirements..."
    
    local size_bytes=$(docker image inspect "$IMAGE_NAME" --format='{{.Size}}')
    local size_gb=$(echo "scale=2; $size_bytes / 1024 / 1024 / 1024" | bc -l)
    local size_gb_int=$(echo "$size_gb" | cut -d. -f1)
    
    info "Image size: ${size_gb} GB"
    
    # Check 2GB requirement
    if (( $(echo "$size_gb > 2.0" | bc -l) )); then
        error "❌ SIZE REQUIREMENT FAILED: Image size (${size_gb}GB) exceeds 2GB limit"
        return 1
    else
        log "✅ Image size requirement met: ${size_gb}GB < 2GB"
        return 0
    fi
}

# Run container security check
check_container_security() {
    log "Running container security check..."
    
    local container_name="unity-mcp-security-test-$$"
    local security_issues=0
    
    # Start container for testing
    if docker run -d --name "$container_name" --rm "$IMAGE_NAME" tail -f /dev/null >/dev/null 2>&1; then
        log "Test container started: $container_name"
        
        # Check if running as non-root inside container
        local container_user=$(docker exec "$container_name" whoami 2>/dev/null || echo "unknown")
        if [[ "$container_user" == "root" ]]; then
            error "❌ Container process runs as root"
            ((security_issues++))
        else
            log "✅ Container process runs as: $container_user"
        fi
        
        # Check file permissions
        local home_perms=$(docker exec "$container_name" stat -c "%a" /home/unity 2>/dev/null || echo "000")
        if [[ "$home_perms" == "755" || "$home_perms" == "750" ]]; then
            log "✅ Home directory permissions are secure: $home_perms"
        else
            warn "⚠️  Home directory permissions may be too open: $home_perms"
        fi
        
        # Cleanup
        docker stop "$container_name" >/dev/null 2>&1 || true
    else
        error "Failed to start test container"
        ((security_issues++))
    fi
    
    return $security_issues
}

# Generate security report summary
generate_summary() {
    log "Generating security scan summary..."
    
    local summary_file="$SCAN_OUTPUT_DIR/security-summary-$TIMESTAMP.md"
    
    cat > "$summary_file" << EOF
# Unity MCP Docker Security Scan Report

**Scan Date:** $(date)  
**Image:** $IMAGE_NAME  
**Timestamp:** $TIMESTAMP  

## Summary

$(if [[ $OVERALL_RESULT -eq 0 ]]; then
    echo "✅ **SECURITY SCAN PASSED** - All requirements met"
else
    echo "❌ **SECURITY SCAN FAILED** - Issues found"
fi)

## Test Results

$(if [[ ${TRIVY_RESULT:-1} -eq 0 ]]; then
    echo "- ✅ Vulnerability scan: PASSED (No critical vulnerabilities)"
else
    echo "- ❌ Vulnerability scan: FAILED (Critical vulnerabilities found)"
fi)

$(if [[ ${CONFIG_RESULT:-1} -eq 0 ]]; then
    echo "- ✅ Image configuration: PASSED (Secure configuration)"
else
    echo "- ❌ Image configuration: FAILED (Security issues in configuration)"
fi)

$(if [[ ${SIZE_RESULT:-1} -eq 0 ]]; then
    echo "- ✅ Image size: PASSED (Under 2GB limit)"
else
    echo "- ❌ Image size: FAILED (Exceeds 2GB limit)"
fi)

$(if [[ ${CONTAINER_RESULT:-1} -eq 0 ]]; then
    echo "- ✅ Container security: PASSED (Secure runtime configuration)"
else
    echo "- ❌ Container security: FAILED (Runtime security issues)"
fi)

## Detailed Reports

- Trivy JSON Report: \`trivy-scan-$TIMESTAMP.json\`
- Image Configuration: \`image-config-$TIMESTAMP.json\`

## Next Steps

$(if [[ $OVERALL_RESULT -eq 0 ]]; then
    echo "The Docker image meets all security requirements and is ready for production use."
else
    echo "Please address the security issues above before deploying to production."
fi)
EOF

    info "Security summary saved to: $summary_file"
}

# Main execution
main() {
    log "Starting security scan for Docker image: $IMAGE_NAME"
    
    # Check prerequisites
    check_tools
    prepare_output
    check_image_exists
    
    # Run security checks
    TRIVY_RESULT=0
    CONFIG_RESULT=0
    SIZE_RESULT=0
    CONTAINER_RESULT=0
    
    run_trivy_scan || TRIVY_RESULT=$?
    check_image_config || CONFIG_RESULT=$?
    check_image_size || SIZE_RESULT=$?
    check_container_security || CONTAINER_RESULT=$?
    
    # Calculate overall result
    OVERALL_RESULT=$((TRIVY_RESULT + CONFIG_RESULT + SIZE_RESULT + CONTAINER_RESULT))
    
    # Generate summary
    generate_summary
    
    # Final result
    if [[ $OVERALL_RESULT -eq 0 ]]; then
        log "🎉 SECURITY SCAN COMPLETED SUCCESSFULLY"
        log "All security requirements met - image is ready for production"
    else
        error "💥 SECURITY SCAN FAILED"
        error "Found $OVERALL_RESULT security issues - please review and fix"
    fi
    
    return $OVERALL_RESULT
}

# Check if bc is available for calculations
if ! command -v bc >/dev/null 2>&1; then
    # Install bc if not available
    if command -v apt-get >/dev/null 2>&1; then
        sudo apt-get update && sudo apt-get install -y bc
    elif command -v yum >/dev/null 2>&1; then
        sudo yum install -y bc
    elif command -v brew >/dev/null 2>&1; then
        brew install bc
    fi
fi

# Run main function
main "$@"