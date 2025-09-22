#!/bin/bash
# Unity MCP Monitoring Script
# Monitors system health, Unity status, and service metrics

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Configuration
UNITY_MCP_API="http://localhost:8080"
LOG_FILE="/opt/unity-mcp/logs/monitor.log"
ALERT_THRESHOLD_CPU=80
ALERT_THRESHOLD_MEMORY=85
ALERT_THRESHOLD_DISK=90

log() {
    echo -e "${GREEN}[$(date +'%Y-%m-%d %H:%M:%S')] $1${NC}"
    echo "[$(date +'%Y-%m-%d %H:%M:%S')] $1" >> "$LOG_FILE"
}

warn() {
    echo -e "${YELLOW}[$(date +'%Y-%m-%d %H:%M:%S')] WARNING: $1${NC}"
    echo "[$(date +'%Y-%m-%d %H:%M:%S')] WARNING: $1" >> "$LOG_FILE"
}

error() {
    echo -e "${RED}[$(date +'%Y-%m-%d %H:%M:%S')] ERROR: $1${NC}"
    echo "[$(date +'%Y-%m-%d %H:%M:%S')] ERROR: $1" >> "$LOG_FILE"
}

info() {
    echo -e "${BLUE}[$(date +'%Y-%m-%d %H:%M:%S')] INFO: $1${NC}"
}

# Function to check if command exists
command_exists() {
    command -v "$1" >/dev/null 2>&1
}

# Function to get system metrics
get_system_metrics() {
    # CPU Usage
    if command_exists top; then
        CPU_USAGE=$(top -bn1 | grep "Cpu(s)" | awk '{print $2}' | cut -d'%' -f1 | cut -d'u' -f1)
    else
        CPU_USAGE="N/A"
    fi
    
    # Memory Usage
    if command_exists free; then
        MEMORY_USAGE=$(free | awk 'NR==2{printf "%.1f", $3*100/$2}')
    else
        MEMORY_USAGE="N/A"
    fi
    
    # Disk Usage
    if command_exists df; then
        DISK_USAGE=$(df /opt/unity-mcp | awk 'NR==2 {print $5}' | sed 's/%//')
    else
        DISK_USAGE="N/A"
    fi
    
    # Load Average
    if [ -f /proc/loadavg ]; then
        LOAD_AVG=$(cat /proc/loadavg | awk '{print $1, $2, $3}')
    else
        LOAD_AVG="N/A"
    fi
}

# Function to check Unity process
check_unity_process() {
    UNITY_PID=$(pgrep -f "Unity.*batchmode" || echo "")
    if [ -n "$UNITY_PID" ]; then
        UNITY_STATUS="Running"
        UNITY_PID_INFO="(PID: $UNITY_PID)"
        
        # Get Unity memory usage
        if command_exists ps; then
            UNITY_MEMORY=$(ps -p $UNITY_PID -o %mem --no-headers 2>/dev/null || echo "N/A")
            UNITY_CPU=$(ps -p $UNITY_PID -o %cpu --no-headers 2>/dev/null || echo "N/A")
        else
            UNITY_MEMORY="N/A"
            UNITY_CPU="N/A"
        fi
    else
        UNITY_STATUS="Not Running"
        UNITY_PID_INFO=""
        UNITY_MEMORY="0"
        UNITY_CPU="0"
    fi
}

# Function to check Unity MCP service
check_unity_mcp_service() {
    if command_exists systemctl; then
        if systemctl is-active --quiet unity-mcp; then
            MCP_SERVICE_STATUS="Active"
        else
            MCP_SERVICE_STATUS="Inactive"
        fi
    else
        MCP_SERVICE_STATUS="Unknown"
    fi
    
    # Check if service is listening on port 8080
    if command_exists netstat; then
        if netstat -tlpn | grep -q ":8080.*LISTEN"; then
            MCP_PORT_STATUS="Listening"
        else
            MCP_PORT_STATUS="Not Listening"
        fi
    elif command_exists ss; then
        if ss -tlpn | grep -q ":8080.*LISTEN"; then
            MCP_PORT_STATUS="Listening"
        else
            MCP_PORT_STATUS="Not Listening"
        fi
    else
        MCP_PORT_STATUS="Unknown"
    fi
}

# Function to check API health
check_api_health() {
    if command_exists curl; then
        # Health endpoint
        HEALTH_RESPONSE=$(curl -s -w "%{http_code}" -o /tmp/health_response "${UNITY_MCP_API}/health" 2>/dev/null || echo "000")
        HEALTH_HTTP_CODE="${HEALTH_RESPONSE: -3}"
        
        if [ "$HEALTH_HTTP_CODE" = "200" ]; then
            API_HEALTH="Healthy"
            # Parse response for additional info
            if [ -f /tmp/health_response ]; then
                ACTIVE_CLIENTS=$(grep -o '"active_clients":[0-9]*' /tmp/health_response 2>/dev/null | cut -d':' -f2 || echo "N/A")
                API_UPTIME=$(grep -o '"uptime":[0-9.]*' /tmp/health_response 2>/dev/null | cut -d':' -f2 || echo "N/A")
            else
                ACTIVE_CLIENTS="N/A"
                API_UPTIME="N/A"
            fi
        else
            API_HEALTH="Unhealthy (HTTP $HEALTH_HTTP_CODE)"
            ACTIVE_CLIENTS="N/A"
            API_UPTIME="N/A"
        fi
        
        # Status endpoint
        STATUS_RESPONSE=$(curl -s -w "%{http_code}" -o /tmp/status_response "${UNITY_MCP_API}/status" 2>/dev/null || echo "000")
        STATUS_HTTP_CODE="${STATUS_RESPONSE: -3}"
        
        if [ "$STATUS_HTTP_CODE" = "200" ] && [ -f /tmp/status_response ]; then
            TOTAL_COMMANDS=$(grep -o '"total_commands":[0-9]*' /tmp/status_response 2>/dev/null | cut -d':' -f2 || echo "N/A")
            SUCCESSFUL_COMMANDS=$(grep -o '"successful_commands":[0-9]*' /tmp/status_response 2>/dev/null | cut -d':' -f2 || echo "N/A")
        else
            TOTAL_COMMANDS="N/A"
            SUCCESSFUL_COMMANDS="N/A"
        fi
        
        # Clean up temp files
        rm -f /tmp/health_response /tmp/status_response
    else
        API_HEALTH="Cannot check (curl not available)"
        ACTIVE_CLIENTS="N/A"
        API_UPTIME="N/A"
        TOTAL_COMMANDS="N/A"
        SUCCESSFUL_COMMANDS="N/A"
    fi
}

# Function to check disk space
check_disk_space() {
    if command_exists df; then
        # Check main Unity MCP directory
        UNITY_MCP_DISK=$(df /opt/unity-mcp | awk 'NR==2 {print $5}' | sed 's/%//')
        UNITY_MCP_AVAIL=$(df -h /opt/unity-mcp | awk 'NR==2 {print $4}')
        
        # Check root filesystem
        ROOT_DISK=$(df / | awk 'NR==2 {print $5}' | sed 's/%//')
        ROOT_AVAIL=$(df -h / | awk 'NR==2 {print $4}')
    else
        UNITY_MCP_DISK="N/A"
        UNITY_MCP_AVAIL="N/A"
        ROOT_DISK="N/A"
        ROOT_AVAIL="N/A"
    fi
}

# Function to check network connectivity
check_network() {
    if command_exists ping; then
        if ping -c 1 -W 5 8.8.8.8 >/dev/null 2>&1; then
            NETWORK_STATUS="Connected"
        else
            NETWORK_STATUS="No Internet"
        fi
    else
        NETWORK_STATUS="Cannot check"
    fi
}

# Function to generate alerts
check_alerts() {
    ALERTS=()
    
    # CPU alert
    if [ "$CPU_USAGE" != "N/A" ] && [ "$(echo "$CPU_USAGE > $ALERT_THRESHOLD_CPU" | bc -l 2>/dev/null || echo 0)" = "1" ]; then
        ALERTS+=("High CPU usage: ${CPU_USAGE}%")
    fi
    
    # Memory alert
    if [ "$MEMORY_USAGE" != "N/A" ] && [ "$(echo "$MEMORY_USAGE > $ALERT_THRESHOLD_MEMORY" | bc -l 2>/dev/null || echo 0)" = "1" ]; then
        ALERTS+=("High memory usage: ${MEMORY_USAGE}%")
    fi
    
    # Disk alert
    if [ "$DISK_USAGE" != "N/A" ] && [ "$DISK_USAGE" -gt "$ALERT_THRESHOLD_DISK" ] 2>/dev/null; then
        ALERTS+=("High disk usage: ${DISK_USAGE}%")
    fi
    
    # Unity process alert
    if [ "$UNITY_STATUS" = "Not Running" ]; then
        ALERTS+=("Unity process not running")
    fi
    
    # Service alert
    if [ "$MCP_SERVICE_STATUS" = "Inactive" ]; then
        ALERTS+=("Unity MCP service not active")
    fi
    
    # API alert
    if [[ "$API_HEALTH" == *"Unhealthy"* ]]; then
        ALERTS+=("Unity MCP API unhealthy")
    fi
    
    # Network alert
    if [ "$NETWORK_STATUS" = "No Internet" ]; then
        ALERTS+=("No internet connectivity")
    fi
}

# Main monitoring function
main() {
    info "Starting Unity MCP monitoring check..."
    
    # Gather all metrics
    get_system_metrics
    check_unity_process
    check_unity_mcp_service
    check_api_health
    check_disk_space
    check_network
    check_alerts
    
    # Display monitoring report
    echo ""
    echo "=========================================="
    echo "Unity MCP System Monitoring Report"
    echo "=========================================="
    echo "Timestamp: $(date)"
    echo ""
    
    echo "=== System Resources ==="
    echo "CPU Usage: $CPU_USAGE%"
    echo "Memory Usage: $MEMORY_USAGE%"
    echo "Load Average: $LOAD_AVG"
    echo "Network: $NETWORK_STATUS"
    echo ""
    
    echo "=== Disk Usage ==="
    echo "Unity MCP: $UNITY_MCP_DISK% used ($UNITY_MCP_AVAIL available)"
    echo "Root: $ROOT_DISK% used ($ROOT_AVAIL available)"
    echo ""
    
    echo "=== Unity Status ==="
    echo "Unity Process: $UNITY_STATUS $UNITY_PID_INFO"
    echo "Unity Memory: $UNITY_MEMORY%"
    echo "Unity CPU: $UNITY_CPU%"
    echo ""
    
    echo "=== Unity MCP Service ==="
    echo "Service Status: $MCP_SERVICE_STATUS"
    echo "Port 8080: $MCP_PORT_STATUS"
    echo "API Health: $API_HEALTH"
    echo "Active Clients: $ACTIVE_CLIENTS"
    echo "API Uptime: $API_UPTIME seconds"
    echo "Total Commands: $TOTAL_COMMANDS"
    echo "Successful Commands: $SUCCESSFUL_COMMANDS"
    echo ""
    
    # Display alerts
    if [ ${#ALERTS[@]} -gt 0 ]; then
        echo "=== ALERTS ==="
        for alert in "${ALERTS[@]}"; do
            warn "$alert"
        done
        echo ""
    else
        log "No alerts - All systems operating normally"
        echo ""
    fi
    
    echo "=========================================="
    
    # Log summary
    log "Monitoring check completed. CPU: $CPU_USAGE%, Memory: $MEMORY_USAGE%, Disk: $DISK_USAGE%, Unity: $UNITY_STATUS, API: $API_HEALTH"
    
    # Return appropriate exit code
    if [ ${#ALERTS[@]} -gt 0 ]; then
        return 1
    else
        return 0
    fi
}

# Check if script is being sourced or executed
if [[ "${BASH_SOURCE[0]}" == "${0}" ]]; then
    # Script is being executed directly
    main "$@"
else
    # Script is being sourced - make functions available
    info "Unity MCP monitoring functions loaded"
fi