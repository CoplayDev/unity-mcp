#!/bin/bash
# Unity MCP Backup Script
# Creates backups of Unity projects, configurations, and logs

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

# Configuration
BACKUP_BASE_DIR="/opt/unity-mcp/backups"
TIMESTAMP=$(date +%Y%m%d_%H%M%S)
BACKUP_NAME="unity-mcp-backup-$TIMESTAMP"
BACKUP_DIR="$BACKUP_BASE_DIR/$BACKUP_NAME"
KEEP_BACKUPS=7  # Keep last 7 backups

# GCS bucket for remote backup (optional)
GCS_BUCKET="${GCS_BACKUP_BUCKET:-}"
AWS_S3_BUCKET="${AWS_S3_BACKUP_BUCKET:-}"

# Create backup directory
mkdir -p "$BACKUP_DIR"

log "🗄️  Starting Unity MCP backup: $BACKUP_NAME"

# Function to backup Unity projects
backup_projects() {
    log "📁 Backing up Unity projects..."
    
    if [ -d "/opt/unity-mcp/projects" ]; then
        tar -czf "$BACKUP_DIR/projects.tar.gz" \
            -C /opt/unity-mcp projects/ \
            --exclude="projects/*/Library" \
            --exclude="projects/*/Temp" \
            --exclude="projects/*/obj" \
            --exclude="projects/*/Logs" \
            2>/dev/null
        
        PROJECT_SIZE=$(du -sh "$BACKUP_DIR/projects.tar.gz" | cut -f1)
        log "✅ Projects backup completed: $PROJECT_SIZE"
    else
        warn "Projects directory not found, skipping"
    fi
}

# Function to backup configurations
backup_configurations() {
    log "⚙️  Backing up configurations..."
    
    # Configuration files
    mkdir -p "$BACKUP_DIR/config"
    
    # Copy Unity MCP config
    if [ -d "/opt/unity-mcp/config" ]; then
        cp -r /opt/unity-mcp/config/* "$BACKUP_DIR/config/" 2>/dev/null || true
    fi
    
    # Copy system configuration
    if [ -d "/etc/unity-mcp" ]; then
        cp -r /etc/unity-mcp "$BACKUP_DIR/config/system" 2>/dev/null || true
    fi
    
    # Copy systemd service file
    if [ -f "/etc/systemd/system/unity-mcp.service" ]; then
        cp /etc/systemd/system/unity-mcp.service "$BACKUP_DIR/config/" 2>/dev/null || true
    fi
    
    # Copy nginx configuration
    if [ -f "/etc/nginx/sites-available/unity-mcp" ]; then
        cp /etc/nginx/sites-available/unity-mcp "$BACKUP_DIR/config/" 2>/dev/null || true
    fi
    
    # Create archive
    if [ -d "$BACKUP_DIR/config" ] && [ "$(ls -A $BACKUP_DIR/config)" ]; then
        tar -czf "$BACKUP_DIR/config.tar.gz" -C "$BACKUP_DIR" config/
        rm -rf "$BACKUP_DIR/config"
        
        CONFIG_SIZE=$(du -sh "$BACKUP_DIR/config.tar.gz" | cut -f1)
        log "✅ Configuration backup completed: $CONFIG_SIZE"
    else
        warn "No configuration files found, skipping"
    fi
}

# Function to backup server code
backup_server_code() {
    log "🐍 Backing up server code..."
    
    if [ -d "/opt/unity-mcp/server" ]; then
        tar -czf "$BACKUP_DIR/server.tar.gz" \
            -C /opt/unity-mcp server/ \
            --exclude="server/__pycache__" \
            --exclude="server/*.pyc" \
            --exclude="server/venv" \
            2>/dev/null
        
        SERVER_SIZE=$(du -sh "$BACKUP_DIR/server.tar.gz" | cut -f1)
        log "✅ Server code backup completed: $SERVER_SIZE"
    else
        warn "Server directory not found, skipping"
    fi
}

# Function to backup Unity MCP Bridge
backup_unity_bridge() {
    log "🎮 Backing up Unity MCP Bridge..."
    
    if [ -d "/opt/unity-mcp/UnityMcpBridge" ]; then
        tar -czf "$BACKUP_DIR/unity-bridge.tar.gz" \
            -C /opt/unity-mcp UnityMcpBridge/ \
            2>/dev/null
        
        BRIDGE_SIZE=$(du -sh "$BACKUP_DIR/unity-bridge.tar.gz" | cut -f1)
        log "✅ Unity Bridge backup completed: $BRIDGE_SIZE"
    else
        warn "Unity Bridge directory not found, skipping"
    fi
}

# Function to backup logs
backup_logs() {
    log "📋 Backing up logs..."
    
    # Only backup recent logs (last 7 days)
    if [ -d "/opt/unity-mcp/logs" ]; then
        # Create temporary directory for recent logs
        TEMP_LOG_DIR="/tmp/unity-mcp-logs-$TIMESTAMP"
        mkdir -p "$TEMP_LOG_DIR"
        
        # Copy recent log files
        find /opt/unity-mcp/logs -name "*.log" -mtime -7 -exec cp {} "$TEMP_LOG_DIR/" \; 2>/dev/null || true
        find /var/log/unity-mcp -name "*.log" -mtime -7 -exec cp {} "$TEMP_LOG_DIR/" \; 2>/dev/null || true
        
        if [ "$(ls -A $TEMP_LOG_DIR 2>/dev/null)" ]; then
            tar -czf "$BACKUP_DIR/logs.tar.gz" -C /tmp "unity-mcp-logs-$TIMESTAMP/" 2>/dev/null
            rm -rf "$TEMP_LOG_DIR"
            
            LOGS_SIZE=$(du -sh "$BACKUP_DIR/logs.tar.gz" | cut -f1)
            log "✅ Logs backup completed: $LOGS_SIZE"
        else
            warn "No recent log files found, skipping"
            rm -rf "$TEMP_LOG_DIR"
        fi
    else
        warn "Logs directory not found, skipping"
    fi
}

# Function to backup database (if any)
backup_database() {
    log "💾 Checking for databases..."
    
    # Check for SQLite databases
    SQLITE_FILES=$(find /opt/unity-mcp -name "*.db" -o -name "*.sqlite" -o -name "*.sqlite3" 2>/dev/null || true)
    
    if [ -n "$SQLITE_FILES" ]; then
        mkdir -p "$BACKUP_DIR/database"
        echo "$SQLITE_FILES" | while read -r db_file; do
            if [ -f "$db_file" ]; then
                cp "$db_file" "$BACKUP_DIR/database/" 2>/dev/null || true
                log "✅ Backed up database: $(basename "$db_file")"
            fi
        done
        
        if [ "$(ls -A $BACKUP_DIR/database 2>/dev/null)" ]; then
            tar -czf "$BACKUP_DIR/database.tar.gz" -C "$BACKUP_DIR" database/
            rm -rf "$BACKUP_DIR/database"
            
            DB_SIZE=$(du -sh "$BACKUP_DIR/database.tar.gz" | cut -f1)
            log "✅ Database backup completed: $DB_SIZE"
        fi
    else
        info "No databases found"
    fi
}

# Function to create system info snapshot
create_system_info() {
    log "📊 Creating system information snapshot..."
    
    cat > "$BACKUP_DIR/system-info.txt" << EOF
Unity MCP System Information Snapshot
====================================
Backup Date: $(date)
Hostname: $(hostname)
System: $(uname -a)
Uptime: $(uptime)

=== Unity MCP Service Status ===
$(systemctl status unity-mcp --no-pager -l 2>/dev/null || echo "Service status unavailable")

=== Disk Usage ===
$(df -h)

=== Memory Usage ===
$(free -h)

=== Network Configuration ===
$(ip addr show 2>/dev/null || ifconfig 2>/dev/null || echo "Network info unavailable")

=== Unity Process ===
$(ps aux | grep -i unity | grep -v grep || echo "No Unity processes found")

=== Running Services ===
$(systemctl list-units --type=service --state=running --no-pager 2>/dev/null || echo "Service list unavailable")

=== Installed Packages (Unity-related) ===
$(dpkg -l | grep -i unity 2>/dev/null || echo "No Unity packages found")

=== Environment Variables ===
$(env | grep -i unity 2>/dev/null || echo "No Unity environment variables")

=== Unity MCP Configuration ===
$(cat /etc/unity-mcp/environment 2>/dev/null || echo "Configuration file not found")

=== Recent Log Entries ===
$(tail -50 /opt/unity-mcp/logs/server.log 2>/dev/null || echo "Server log not found")
EOF
    
    SYSINFO_SIZE=$(du -sh "$BACKUP_DIR/system-info.txt" | cut -f1)
    log "✅ System info snapshot created: $SYSINFO_SIZE"
}

# Function to create backup manifest
create_manifest() {
    log "📋 Creating backup manifest..."
    
    cat > "$BACKUP_DIR/MANIFEST.txt" << EOF
Unity MCP Backup Manifest
========================
Backup Name: $BACKUP_NAME
Created: $(date)
Hostname: $(hostname)
Script Version: 1.0

Contents:
EOF
    
    for file in "$BACKUP_DIR"/*.tar.gz "$BACKUP_DIR"/*.txt; do
        if [ -f "$file" ]; then
            filename=$(basename "$file")
            filesize=$(du -sh "$file" | cut -f1)
            echo "  - $filename ($filesize)" >> "$BACKUP_DIR/MANIFEST.txt"
        fi
    done
    
    echo "" >> "$BACKUP_DIR/MANIFEST.txt"
    echo "Total backup size: $(du -sh "$BACKUP_DIR" | cut -f1)" >> "$BACKUP_DIR/MANIFEST.txt"
    
    log "✅ Backup manifest created"
}

# Function to upload to cloud storage
upload_to_cloud() {
    # Create final backup archive
    FINAL_BACKUP="$BACKUP_BASE_DIR/$BACKUP_NAME.tar.gz"
    log "📦 Creating final backup archive..."
    tar -czf "$FINAL_BACKUP" -C "$BACKUP_BASE_DIR" "$BACKUP_NAME/"
    
    FINAL_SIZE=$(du -sh "$FINAL_BACKUP" | cut -f1)
    log "✅ Final backup archive created: $FINAL_SIZE"
    
    # Upload to Google Cloud Storage
    if [ -n "$GCS_BUCKET" ] && command -v gsutil >/dev/null 2>&1; then
        log "☁️  Uploading to Google Cloud Storage..."
        if gsutil cp "$FINAL_BACKUP" "gs://$GCS_BUCKET/unity-mcp/"; then
            log "✅ Uploaded to GCS: gs://$GCS_BUCKET/unity-mcp/$BACKUP_NAME.tar.gz"
        else
            warn "Failed to upload to GCS"
        fi
    fi
    
    # Upload to AWS S3
    if [ -n "$AWS_S3_BUCKET" ] && command -v aws >/dev/null 2>&1; then
        log "☁️  Uploading to AWS S3..."
        if aws s3 cp "$FINAL_BACKUP" "s3://$AWS_S3_BUCKET/unity-mcp/"; then
            log "✅ Uploaded to S3: s3://$AWS_S3_BUCKET/unity-mcp/$BACKUP_NAME.tar.gz"
        else
            warn "Failed to upload to S3"
        fi
    fi
    
    # Clean up local directory backup (keep archive)
    rm -rf "$BACKUP_DIR"
}

# Function to clean old backups
cleanup_old_backups() {
    log "🧹 Cleaning up old backups..."
    
    # Remove old backup directories
    find "$BACKUP_BASE_DIR" -maxdepth 1 -type d -name "unity-mcp-backup-*" -mtime +$KEEP_BACKUPS -exec rm -rf {} \; 2>/dev/null || true
    
    # Remove old backup archives
    find "$BACKUP_BASE_DIR" -maxdepth 1 -type f -name "unity-mcp-backup-*.tar.gz" -mtime +$KEEP_BACKUPS -exec rm -f {} \; 2>/dev/null || true
    
    # Clean up cloud storage (if enabled)
    if [ -n "$GCS_BUCKET" ] && command -v gsutil >/dev/null 2>&1; then
        # List and remove old backups from GCS (keep last 14)
        gsutil ls "gs://$GCS_BUCKET/unity-mcp/unity-mcp-backup-*.tar.gz" 2>/dev/null | \
        sort -r | tail -n +15 | \
        xargs -r gsutil rm 2>/dev/null || true
    fi
    
    REMAINING_BACKUPS=$(find "$BACKUP_BASE_DIR" -maxdepth 1 -name "unity-mcp-backup-*" | wc -l)
    log "✅ Cleanup completed. $REMAINING_BACKUPS local backups remaining"
}

# Function to send notification
send_notification() {
    local status="$1"
    local message="$2"
    
    # Log to syslog
    logger -t unity-mcp-backup "$status: $message"
    
    # Send email notification (if configured)
    if [ -n "${BACKUP_EMAIL:-}" ] && command -v mail >/dev/null 2>&1; then
        echo "$message" | mail -s "Unity MCP Backup $status" "$BACKUP_EMAIL" 2>/dev/null || true
    fi
    
    # Send webhook notification (if configured)
    if [ -n "${BACKUP_WEBHOOK:-}" ] && command -v curl >/dev/null 2>&1; then
        curl -X POST "$BACKUP_WEBHOOK" \
            -H "Content-Type: application/json" \
            -d "{\"status\":\"$status\",\"message\":\"$message\",\"timestamp\":\"$(date -Iseconds)\"}" \
            2>/dev/null || true
    fi
}

# Main backup function
main() {
    local start_time=$(date +%s)
    
    # Check if backup directory is writable
    if [ ! -w "$BACKUP_BASE_DIR" ]; then
        error "Backup directory $BACKUP_BASE_DIR is not writable"
    fi
    
    # Check available disk space (need at least 2GB)
    AVAILABLE_SPACE=$(df /opt/unity-mcp --output=avail | tail -1)
    REQUIRED_SPACE=2097152  # 2GB in KB
    
    if [ "$AVAILABLE_SPACE" -lt "$REQUIRED_SPACE" ]; then
        error "Insufficient disk space for backup. Need 2GB, available: $((AVAILABLE_SPACE/1024/1024))GB"
    fi
    
    # Perform backup steps
    backup_projects
    backup_configurations
    backup_server_code
    backup_unity_bridge
    backup_logs
    backup_database
    create_system_info
    create_manifest
    
    # Calculate backup time
    local end_time=$(date +%s)
    local duration=$((end_time - start_time))
    
    # Get total backup size
    local total_size=$(du -sh "$BACKUP_DIR" | cut -f1)
    
    log "✅ Local backup completed in ${duration}s (${total_size})"
    
    # Upload to cloud and cleanup
    upload_to_cloud
    cleanup_old_backups
    
    # Send success notification
    send_notification "SUCCESS" "Unity MCP backup completed successfully. Size: $total_size, Duration: ${duration}s"
    
    log "🎉 Unity MCP backup process completed successfully!"
}

# Error handling
trap 'error "Backup failed due to an error"' ERR

# Check if script is being executed directly
if [[ "${BASH_SOURCE[0]}" == "${0}" ]]; then
    main "$@"
fi