#!/bin/bash
# Unity MCP Client Limit Configuration Script
# Usage: ./configure-clients.sh [number_of_clients]

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

log() {
    echo -e "${GREEN}[$(date +'%Y-%m-%d %H:%M:%S')] $1${NC}"
}

warn() {
    echo -e "${YELLOW}[$(date +'%Y-%m-%d %H:%M:%S')] WARNING: $1${NC}"
}

error() {
    echo -e "${RED}[$(date +'%Y-%m-%d %H:%M:%S')] ERROR: $1${NC}"
    exit 1
}

info() {
    echo -e "${BLUE}[$(date +'%Y-%m-%d %H:%M:%S')] INFO: $1${NC}"
}

# Check if running as root or with sudo
if [[ $EUID -eq 0 ]]; then
    SUDO=""
else
    SUDO="sudo"
fi

# Display current configuration
show_current_config() {
    echo ""
    echo "=========================================="
    echo "Current Unity MCP Configuration"
    echo "=========================================="
    
    # Get current max clients from environment
    if [ -f "/etc/unity-mcp/environment" ]; then
        CURRENT_MAX=$(grep "MAX_CLIENTS=" /etc/unity-mcp/environment | cut -d'=' -f2)
        echo "Max Clients: $CURRENT_MAX"
    else
        echo "Max Clients: Not configured"
    fi
    
    # Get current status from API
    if command -v curl >/dev/null 2>&1; then
        STATUS=$(curl -s http://localhost:8080/status 2>/dev/null || echo "")
        if [ -n "$STATUS" ]; then
            ACTIVE_CLIENTS=$(echo "$STATUS" | grep -o '"active":[0-9]*' | cut -d':' -f2)
            echo "Active Clients: $ACTIVE_CLIENTS"
        else
            echo "Active Clients: Server not responding"
        fi
    fi
    
    # Show resource recommendations
    echo ""
    echo "Resource Recommendations:"
    echo "• 1-5 clients: 4 vCPUs, 16GB RAM"
    echo "• 6-15 clients: 8 vCPUs, 32GB RAM"
    echo "• 16-30 clients: 16 vCPUs, 64GB RAM"
    echo "• 30+ clients: 32+ vCPUs, 128GB+ RAM"
    echo "=========================================="
    echo ""
}

# Configure client limits
configure_limits() {
    local max_clients=$1
    
    log "Configuring Unity MCP for $max_clients clients..."
    
    # Validate input
    if ! [[ "$max_clients" =~ ^[0-9]+$ ]]; then
        error "Invalid input. Please provide a number (0 for unlimited)."
    fi
    
    # Calculate recommended resource limits based on client count
    if [ "$max_clients" -eq 0 ]; then
        MEMORY_PER_CLIENT=1024  # Conservative for unlimited
        ASSETS_PER_CLIENT=500
        IDLE_TIMEOUT=10
        log "Configuring for UNLIMITED clients (resource-limited)"
    elif [ "$max_clients" -le 5 ]; then
        MEMORY_PER_CLIENT=3072  # 3GB per client
        ASSETS_PER_CLIENT=1500
        IDLE_TIMEOUT=60
        log "Configuring for SMALL scale ($max_clients clients)"
    elif [ "$max_clients" -le 15 ]; then
        MEMORY_PER_CLIENT=2048  # 2GB per client
        ASSETS_PER_CLIENT=1000
        IDLE_TIMEOUT=30
        log "Configuring for MEDIUM scale ($max_clients clients)"
    elif [ "$max_clients" -le 30 ]; then
        MEMORY_PER_CLIENT=1536  # 1.5GB per client
        ASSETS_PER_CLIENT=750
        IDLE_TIMEOUT=20
        log "Configuring for LARGE scale ($max_clients clients)"
    else
        MEMORY_PER_CLIENT=1024  # 1GB per client
        ASSETS_PER_CLIENT=500
        IDLE_TIMEOUT=15
        log "Configuring for ENTERPRISE scale ($max_clients clients)"
    fi
    
    # Update environment file
    if [ -f "/etc/unity-mcp/environment" ]; then
        log "Updating environment configuration..."
        $SUDO sed -i "s/MAX_CLIENTS=.*/MAX_CLIENTS=$max_clients/" /etc/unity-mcp/environment
        
        # Add or update other settings
        if grep -q "MAX_MEMORY_PER_CLIENT=" /etc/unity-mcp/environment; then
            $SUDO sed -i "s/MAX_MEMORY_PER_CLIENT=.*/MAX_MEMORY_PER_CLIENT=$MEMORY_PER_CLIENT/" /etc/unity-mcp/environment
        else
            echo "MAX_MEMORY_PER_CLIENT=$MEMORY_PER_CLIENT" | $SUDO tee -a /etc/unity-mcp/environment
        fi
        
        if grep -q "MAX_ASSETS_PER_CLIENT=" /etc/unity-mcp/environment; then
            $SUDO sed -i "s/MAX_ASSETS_PER_CLIENT=.*/MAX_ASSETS_PER_CLIENT=$ASSETS_PER_CLIENT/" /etc/unity-mcp/environment
        else
            echo "MAX_ASSETS_PER_CLIENT=$ASSETS_PER_CLIENT" | $SUDO tee -a /etc/unity-mcp/environment
        fi
        
        if grep -q "CLEANUP_IDLE_MINUTES=" /etc/unity-mcp/environment; then
            $SUDO sed -i "s/CLEANUP_IDLE_MINUTES=.*/CLEANUP_IDLE_MINUTES=$IDLE_TIMEOUT/" /etc/unity-mcp/environment
        else
            echo "CLEANUP_IDLE_MINUTES=$IDLE_TIMEOUT" | $SUDO tee -a /etc/unity-mcp/environment
        fi
    else
        error "Environment file not found. Please run setup-ubuntu.sh first."
    fi
    
    # Update server configuration
    if [ -f "/opt/unity-mcp/config/server.conf" ]; then
        log "Updating server configuration..."
        $SUDO sed -i "s/max_clients = .*/max_clients = $max_clients/" /opt/unity-mcp/config/server.conf
        $SUDO sed -i "s/max_memory_per_client = .*/max_memory_per_client = $MEMORY_PER_CLIENT/" /opt/unity-mcp/config/server.conf
        $SUDO sed -i "s/max_assets_per_client = .*/max_assets_per_client = $ASSETS_PER_CLIENT/" /opt/unity-mcp/config/server.conf
        $SUDO sed -i "s/cleanup_idle_minutes = .*/cleanup_idle_minutes = $IDLE_TIMEOUT/" /opt/unity-mcp/config/server.conf
    fi
    
    # Restart service
    log "Restarting Unity MCP service..."
    if command -v systemctl >/dev/null 2>&1; then
        $SUDO systemctl restart unity-mcp
        sleep 5
        
        # Check service status
        if $SUDO systemctl is-active --quiet unity-mcp; then
            log "✅ Unity MCP service restarted successfully"
        else
            error "❌ Failed to restart Unity MCP service"
        fi
    else
        warn "systemctl not available. Please restart the service manually."
    fi
    
    # Display new configuration
    echo ""
    echo "=========================================="
    echo "New Configuration Applied"
    echo "=========================================="
    echo "Max Clients: $max_clients"
    echo "Memory per Client: ${MEMORY_PER_CLIENT}MB"
    echo "Assets per Client: $ASSETS_PER_CLIENT"
    echo "Idle Timeout: ${IDLE_TIMEOUT} minutes"
    echo "=========================================="
    
    # Calculate resource requirements
    if [ "$max_clients" -gt 0 ]; then
        TOTAL_MEMORY=$((6 + (max_clients * MEMORY_PER_CLIENT / 1024)))
        RECOMMENDED_CPUS=$((4 + (max_clients / 3)))
        
        echo ""
        echo "Resource Requirements:"
        echo "• Minimum RAM: ${TOTAL_MEMORY}GB"
        echo "• Recommended CPUs: $RECOMMENDED_CPUS cores"
        
        if [ "$TOTAL_MEMORY" -gt 32 ]; then
            warn "⚠️  High memory requirements detected. Consider using multiple instances."
        fi
        
        if [ "$max_clients" -gt 20 ]; then
            warn "⚠️  High client count. Monitor performance closely."
        fi
    fi
    
    # Test the configuration
    echo ""
    log "Testing new configuration..."
    sleep 3
    
    if command -v curl >/dev/null 2>&1; then
        if curl -f -s http://localhost:8080/health >/dev/null; then
            log "✅ Health check passed"
        else
            error "❌ Health check failed"
        fi
        
        # Show current status
        STATUS=$(curl -s http://localhost:8080/status 2>/dev/null || echo "")
        if [ -n "$STATUS" ]; then
            CURRENT_ACTIVE=$(echo "$STATUS" | grep -o '"active":[0-9]*' | cut -d':' -f2)
            log "Current active clients: $CURRENT_ACTIVE"
        fi
    fi
    
    echo ""
    log "🎉 Configuration update completed successfully!"
    echo ""
    echo "You can now register up to $max_clients clients (or unlimited if set to 0)."
    echo "Monitor performance with: /opt/unity-mcp/scripts/monitor.sh"
}

# Show usage
show_usage() {
    echo "Unity MCP Client Configuration Tool"
    echo ""
    echo "Usage:"
    echo "  $0 [number_of_clients]"
    echo ""
    echo "Examples:"
    echo "  $0 5        # Configure for exactly 5 clients"
    echo "  $0 15       # Configure for exactly 15 clients"
    echo "  $0 0        # Configure for unlimited clients"
    echo "  $0          # Show current configuration"
    echo ""
    echo "Predefined Configurations:"
    echo "  • 1-5 clients: Small team/development"
    echo "  • 6-15 clients: Medium workshop/training"
    echo "  • 16-30 clients: Large demo/exhibition"
    echo "  • 0 clients: Unlimited (resource-limited)"
}

# Interactive configuration
interactive_config() {
    echo ""
    echo "=========================================="
    echo "Unity MCP Interactive Configuration"
    echo "=========================================="
    echo ""
    echo "Select your use case:"
    echo "1) Small team (1-5 developers)"
    echo "2) Workshop/Training (6-15 participants)"
    echo "3) Demo/Exhibition (16-30 users)"
    echo "4) Production API (unlimited users)"
    echo "5) Custom configuration"
    echo "6) Show current status only"
    echo ""
    
    read -p "Enter your choice (1-6): " choice
    
    case $choice in
        1)
            echo "Configuring for small team (5 clients max)..."
            configure_limits 5
            ;;
        2)
            echo "Configuring for workshop/training (15 clients max)..."
            configure_limits 15
            ;;
        3)
            echo "Configuring for demo/exhibition (30 clients max)..."
            configure_limits 30
            ;;
        4)
            echo "Configuring for production API (unlimited clients)..."
            configure_limits 0
            ;;
        5)
            read -p "Enter maximum number of clients (0 for unlimited): " custom_limit
            configure_limits "$custom_limit"
            ;;
        6)
            show_current_config
            ;;
        *)
            error "Invalid choice. Please select 1-6."
            ;;
    esac
}

# Main script logic
main() {
    if [ $# -eq 0 ]; then
        # No arguments - show current config and offer interactive setup
        show_current_config
        echo ""
        read -p "Would you like to configure client limits? (y/N): " configure
        if [[ $configure =~ ^[Yy]$ ]]; then
            interactive_config
        fi
    elif [ "$1" = "-h" ] || [ "$1" = "--help" ]; then
        show_usage
    elif [ "$1" = "status" ]; then
        show_current_config
    elif [ "$1" = "interactive" ]; then
        interactive_config
    else
        # Direct configuration
        configure_limits "$1"
    fi
}

# Run main function
main "$@"