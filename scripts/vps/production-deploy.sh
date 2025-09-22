#!/bin/bash
# Production-ready Unity MCP VPS Deployment Script
# Enhanced deployment with security, monitoring, and production checks

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
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
SERVER_HOST="${1:-unity-mcp-server}"
SERVER_ZONE="${2:-us-central1-a}"
DOMAIN="${3:-}"
API_KEY="${4:-}"

# Production checks
check_production_readiness() {
    log "🔍 Checking production readiness..."
    
    # Check required parameters
    if [[ -z "$DOMAIN" ]]; then
        error "Domain name is required for production deployment"
    fi
    
    if [[ -z "$API_KEY" ]]; then
        error "API key is required for production deployment"
    fi
    
    if [[ "$API_KEY" == "default-api-key" ]]; then
        error "Default API key not allowed in production"
    fi
    
    if [[ ${#API_KEY} -lt 32 ]]; then
        error "API key must be at least 32 characters for production"
    fi
    
    # Check domain format
    if [[ ! "$DOMAIN" =~ ^[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$ ]]; then
        error "Invalid domain format: $DOMAIN"
    fi
    
    log "✅ Production readiness checks passed"
}

# Security configuration
configure_production_security() {
    log "🔒 Configuring production security..."
    
    # Generate additional security configurations
    gcloud compute ssh unity@${SERVER_HOST} --zone=${SERVER_ZONE} --command="
        # Configure firewall
        sudo ufw --force enable
        sudo ufw default deny incoming
        sudo ufw default allow outgoing
        sudo ufw allow ssh
        sudo ufw allow 80/tcp
        sudo ufw allow 443/tcp
        
        # Secure SSH
        sudo sed -i 's/#PasswordAuthentication yes/PasswordAuthentication no/' /etc/ssh/sshd_config
        sudo sed -i 's/#PermitRootLogin yes/PermitRootLogin no/' /etc/ssh/sshd_config
        sudo systemctl restart ssh
        
        # Configure log rotation
        sudo tee /etc/logrotate.d/unity-mcp > /dev/null <<EOF
/opt/unity-mcp/logs/*.log {
    daily
    rotate 30
    compress
    delaycompress
    missingok
    notifempty
    create 0644 unity unity
    postrotate
        /bin/systemctl reload unity-mcp || true
    endscript
}
EOF
        
        # Set secure permissions
        sudo chmod 750 /opt/unity-mcp
        sudo chmod 700 /opt/unity-mcp/logs
        sudo chmod 600 /opt/unity-mcp/.env
    "
    
    log "✅ Production security configured"
}

# Performance optimization
optimize_performance() {
    log "⚡ Optimizing performance..."
    
    gcloud compute ssh unity@${SERVER_HOST} --zone=${SERVER_ZONE} --command="
        # Kernel optimization
        sudo tee -a /etc/sysctl.conf > /dev/null <<EOF

# Unity MCP Performance Optimizations
net.core.somaxconn = 65535
net.core.netdev_max_backlog = 5000
net.ipv4.tcp_max_syn_backlog = 65535
net.ipv4.tcp_keepalive_time = 300
net.ipv4.tcp_keepalive_probes = 3
net.ipv4.tcp_keepalive_intvl = 30
vm.swappiness = 10
fs.file-max = 1000000
EOF
        
        sudo sysctl -p
        
        # Increase file limits
        sudo tee -a /etc/security/limits.conf > /dev/null <<EOF
unity soft nofile 65535
unity hard nofile 65535
unity soft nproc 32768
unity hard nproc 32768
EOF
        
        # Configure systemd limits
        sudo mkdir -p /etc/systemd/system/unity-mcp.service.d
        sudo tee /etc/systemd/system/unity-mcp.service.d/limits.conf > /dev/null <<EOF
[Service]
LimitNOFILE=65535
LimitNPROC=32768
EOF
        
        sudo systemctl daemon-reload
    "
    
    log "✅ Performance optimization complete"
}

# Monitoring setup
setup_monitoring() {
    log "📊 Setting up monitoring..."
    
    gcloud compute ssh unity@${SERVER_HOST} --zone=${SERVER_ZONE} --command="
        # Install monitoring tools
        sudo apt-get update
        sudo apt-get install -y htop iotop nethogs fail2ban
        
        # Configure fail2ban
        sudo tee /etc/fail2ban/jail.local > /dev/null <<EOF
[DEFAULT]
bantime = 3600
findtime = 600
maxretry = 3

[sshd]
enabled = true
port = ssh
logpath = /var/log/auth.log

[nginx-http-auth]
enabled = true
port = http,https
logpath = /var/log/nginx/error.log

[nginx-limit-req]
enabled = true
port = http,https
logpath = /var/log/nginx/error.log
maxretry = 10
EOF
        
        sudo systemctl enable fail2ban
        sudo systemctl start fail2ban
        
        # Create monitoring script
        sudo tee /opt/unity-mcp/bin/monitor.sh > /dev/null <<'EOF'
#!/bin/bash
# Unity MCP Monitoring Script

LOG_DIR=\"/opt/unity-mcp/logs\"
ALERT_LOG=\"\$LOG_DIR/alerts.log\"
DATE=\$(date '+%Y-%m-%d %H:%M:%S')

# Check disk space
DISK_USAGE=\$(df /opt/unity-mcp | tail -1 | awk '{print \$5}' | sed 's/%//')
if [ \$DISK_USAGE -gt 85 ]; then
    echo \"\$DATE - ALERT: High disk usage: \${DISK_USAGE}%\" >> \$ALERT_LOG
fi

# Check memory usage
MEM_USAGE=\$(free | grep Mem | awk '{printf \"%.0f\", \$3/\$2 * 100.0}')
if [ \$MEM_USAGE -gt 90 ]; then
    echo \"\$DATE - ALERT: High memory usage: \${MEM_USAGE}%\" >> \$ALERT_LOG
fi

# Check service status
if ! systemctl is-active --quiet unity-mcp; then
    echo \"\$DATE - CRITICAL: Unity MCP service is down\" >> \$ALERT_LOG
    # Attempt restart
    sudo systemctl start unity-mcp
fi

# Check log errors
ERROR_COUNT=\$(tail -1000 \$LOG_DIR/build-service.log | grep -c ERROR || echo 0)
if [ \$ERROR_COUNT -gt 10 ]; then
    echo \"\$DATE - ALERT: High error rate: \$ERROR_COUNT errors in last 1000 log lines\" >> \$ALERT_LOG
fi
EOF
        
        sudo chmod +x /opt/unity-mcp/bin/monitor.sh
        
        # Add monitoring cron job
        echo '*/5 * * * * /opt/unity-mcp/bin/monitor.sh' | sudo crontab -u unity -
    "
    
    log "✅ Monitoring setup complete"
}

# Health check endpoint
setup_health_checks() {
    log "🏥 Setting up health checks..."
    
    # Add health check endpoint to nginx
    gcloud compute ssh unity@${SERVER_HOST} --zone=${SERVER_ZONE} --command="
        # Update nginx configuration for health checks
        sudo tee -a /etc/nginx/sites-available/unity-mcp > /dev/null <<'EOF'

    # Health check endpoint
    location /health {
        proxy_pass http://127.0.0.1:8080/health;
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        
        # Health check specific settings
        proxy_connect_timeout 5s;
        proxy_send_timeout 5s;
        proxy_read_timeout 5s;
        
        # No authentication required for health checks
        allow all;
    }
    
    # Metrics endpoint (restrict access)
    location /metrics {
        proxy_pass http://127.0.0.1:8080/api/admin/build-stats;
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        
        # Restrict to monitoring systems
        allow 127.0.0.1;
        allow 10.0.0.0/8;
        deny all;
    }
EOF
        
        sudo systemctl reload nginx
    "
    
    log "✅ Health checks configured"
}

# Backup configuration
setup_backups() {
    log "💾 Setting up backup system..."
    
    gcloud compute ssh unity@${SERVER_HOST} --zone=${SERVER_ZONE} --command="
        # Create backup script
        sudo tee /opt/unity-mcp/bin/backup.sh > /dev/null <<'EOF'
#!/bin/bash
# Unity MCP Backup Script

BACKUP_DIR=\"/opt/unity-mcp/backups\"
DATE=\$(date '+%Y%m%d_%H%M%S')
BACKUP_NAME=\"unity-mcp-backup-\$DATE\"

mkdir -p \$BACKUP_DIR

# Backup configuration
tar -czf \$BACKUP_DIR/\$BACKUP_NAME-config.tar.gz \
    /opt/unity-mcp/.env \
    /etc/nginx/sites-available/unity-mcp \
    /etc/systemd/system/unity-mcp.service

# Backup logs (last 7 days)
find /opt/unity-mcp/logs -name \"*.log\" -mtime -7 | \
    tar -czf \$BACKUP_DIR/\$BACKUP_NAME-logs.tar.gz -T -

# Clean old backups (keep 30 days)
find \$BACKUP_DIR -name \"*.tar.gz\" -mtime +30 -delete

echo \"Backup completed: \$BACKUP_NAME\"
EOF
        
        sudo chmod +x /opt/unity-mcp/bin/backup.sh
        
        # Schedule daily backups
        echo '0 2 * * * /opt/unity-mcp/bin/backup.sh' | sudo crontab -u unity -
    "
    
    log "✅ Backup system configured"
}

# SSL/TLS hardening
harden_ssl() {
    log "🔐 Hardening SSL/TLS configuration..."
    
    gcloud compute ssh unity@${SERVER_HOST} --zone=${SERVER_ZONE} --command="
        # Generate strong DH parameters
        sudo openssl dhparam -out /etc/ssl/certs/dhparam.pem 2048
        
        # Create SSL configuration
        sudo tee /etc/nginx/snippets/ssl-params.conf > /dev/null <<'EOF'
# SSL Configuration
ssl_protocols TLSv1.2 TLSv1.3;
ssl_prefer_server_ciphers on;
ssl_dhparam /etc/ssl/certs/dhparam.pem;
ssl_ciphers ECDHE-RSA-AES256-GCM-SHA512:DHE-RSA-AES256-GCM-SHA512:ECDHE-RSA-AES256-GCM-SHA384:DHE-RSA-AES256-GCM-SHA384:ECDHE-RSA-AES256-SHA384;
ssl_ecdh_curve secp384r1;
ssl_session_timeout  10m;
ssl_session_cache shared:SSL:10m;
ssl_session_tickets off;
ssl_stapling on;
ssl_stapling_verify on;
resolver 8.8.8.8 8.8.4.4 valid=300s;
resolver_timeout 5s;

# Security headers
add_header Strict-Transport-Security \"max-age=63072000; includeSubDomains; preload\";
add_header X-Frame-Options DENY;
add_header X-Content-Type-Options nosniff;
add_header X-XSS-Protection \"1; mode=block\";
add_header Referrer-Policy \"strict-origin-when-cross-origin\";
EOF
    "
    
    log "✅ SSL/TLS hardening complete"
}

# Main deployment function
main() {
    log "🚀 Starting production Unity MCP deployment..."
    
    # Production checks
    check_production_readiness
    
    # Run base deployment
    log "📦 Running base deployment..."
    bash "${SCRIPT_DIR}/deploy.sh" "$SERVER_HOST" "$SERVER_ZONE" "$DOMAIN"
    
    # Production enhancements
    configure_production_security
    optimize_performance
    setup_monitoring
    setup_health_checks
    setup_backups
    harden_ssl
    
    # Configure production environment
    log "🔧 Configuring production environment..."
    gcloud compute ssh unity@${SERVER_HOST} --zone=${SERVER_ZONE} --command="
        # Update environment with production settings
        sudo tee -a /opt/unity-mcp/.env > /dev/null <<EOF

# Production Configuration
BUILD_SERVICE_API_KEY=$API_KEY
BASE_GAME_URL=https://$DOMAIN/games
MAX_CONCURRENT_BUILDS=5
MAX_ASSETS_PER_BUILD=50
MAX_ASSET_SIZE_MB=100
ENABLE_SECURITY_AUDIT=true
ALLOW_PRIVATE_URLS=false
BUILD_RETENTION_HOURS=48
RATE_LIMIT_REQUESTS=20
RATE_LIMIT_WINDOW=60
EOF
        
        # Restart services with new configuration
        sudo systemctl restart unity-mcp
        sudo systemctl restart nginx
        
        # Wait for services to start
        sleep 10
        
        # Verify services are running
        if ! systemctl is-active --quiet unity-mcp; then
            echo 'ERROR: Unity MCP service failed to start'
            exit 1
        fi
        
        if ! systemctl is-active --quiet nginx; then
            echo 'ERROR: Nginx service failed to start'
            exit 1
        fi
    "
    
    # Final verification
    log "🔍 Performing final verification..."
    
    # Test health endpoint
    if curl -f -s "https://$DOMAIN/health" > /dev/null; then
        log "✅ Health check endpoint is responding"
    else
        warn "⚠️  Health check endpoint not responding - check logs"
    fi
    
    # Display deployment summary
    log "🎉 Production deployment complete!"
    echo
    info "=== DEPLOYMENT SUMMARY ==="
    info "Server: $SERVER_HOST"
    info "Domain: https://$DOMAIN"
    info "API Endpoints:"
    info "  - POST https://$DOMAIN/build"
    info "  - GET  https://$DOMAIN/build/{id}/status"
    info "  - PUT  https://$DOMAIN/build/{id}/stop"
    info "Health Check: https://$DOMAIN/health"
    info "Games URL: https://$DOMAIN/games/{build_id}/"
    echo
    info "=== SECURITY NOTES ==="
    info "- API key is configured (keep it secure!)"
    info "- Firewall is enabled with restricted ports"
    info "- SSL/TLS is hardened with modern ciphers"
    info "- Fail2ban is active for intrusion prevention"
    info "- Monitoring and alerting are configured"
    echo
    info "=== MAINTENANCE ==="
    info "- Automatic backups run daily at 2 AM"
    info "- Log rotation is configured"
    info "- System monitoring runs every 5 minutes"
    info "- Check alerts: /opt/unity-mcp/logs/alerts.log"
    echo
    warn "Remember to:"
    warn "1. Keep your API key secure"
    warn "2. Monitor the alerts log regularly"
    warn "3. Update the system regularly"
    warn "4. Test your build service with a sample request"
}

# Run main function
main "$@"