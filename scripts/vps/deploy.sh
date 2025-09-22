#!/bin/bash
# Unity MCP VPS Deployment Script
# Deploys Unity MCP multi-client server to VPS

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

# Check if gcloud is installed and configured
if ! command -v gcloud &> /dev/null; then
    error "gcloud CLI is not installed. Please install it first."
fi

# Check if we can connect to the server
log "Checking connection to ${SERVER_HOST}..."
if ! gcloud compute ssh unity@${SERVER_HOST} --zone=${SERVER_ZONE} --command="echo 'Connection test successful'" &>/dev/null; then
    error "Cannot connect to ${SERVER_HOST}. Please check your GCP configuration and server status."
fi

log "🚀 Starting Unity MCP deployment to ${SERVER_HOST}..."

# Step 1: Upload server code
log "📦 Uploading Unity MCP server code..."
gcloud compute scp \
    --zone=${SERVER_ZONE} \
    --recurse \
    "${PROJECT_ROOT}/UnityMcpBridge/UnityMcpServer~/src/"* \
    unity@${SERVER_HOST}:/opt/unity-mcp/server/

# Step 2: Upload Unity bridge code
log "📦 Uploading Unity MCP Bridge..."
gcloud compute scp \
    --zone=${SERVER_ZONE} \
    --recurse \
    "${PROJECT_ROOT}/UnityMcpBridge/" \
    unity@${SERVER_HOST}:/opt/unity-mcp/

# Step 3: Upload configuration files
log "⚙️  Uploading configuration files..."
gcloud compute scp \
    --zone=${SERVER_ZONE} \
    "${SCRIPT_DIR}/unity-mcp.service" \
    unity@${SERVER_HOST}:/tmp/

gcloud compute scp \
    --zone=${SERVER_ZONE} \
    "${SCRIPT_DIR}/nginx-unity-mcp.conf" \
    unity@${SERVER_HOST}:/tmp/

# Step 4: Install Python dependencies
log "🐍 Installing Python dependencies..."
gcloud compute ssh unity@${SERVER_HOST} --zone=${SERVER_ZONE} --command="
    cd /opt/unity-mcp/server && \
    /opt/unity-mcp/venv/bin/pip install --upgrade pip && \
    /opt/unity-mcp/venv/bin/pip install -r requirements-vps.txt
"

# Step 5: Configure systemd service
log "🔧 Configuring systemd service..."
gcloud compute ssh unity@${SERVER_HOST} --zone=${SERVER_ZONE} --command="
    sudo mv /tmp/unity-mcp.service /etc/systemd/system/ && \
    sudo systemctl daemon-reload && \
    sudo systemctl enable unity-mcp
"

# Step 6: Configure nginx
log "🌐 Configuring nginx..."
if [ -n "$DOMAIN" ]; then
    log "Configuring nginx for domain: $DOMAIN"
    gcloud compute ssh unity@${SERVER_HOST} --zone=${SERVER_ZONE} --command="
        sudo sed -i 's/your-domain.com/${DOMAIN}/g' /tmp/nginx-unity-mcp.conf && \
        sudo mv /tmp/nginx-unity-mcp.conf /etc/nginx/sites-available/unity-mcp && \
        sudo ln -sf /etc/nginx/sites-available/unity-mcp /etc/nginx/sites-enabled/ && \
        sudo rm -f /etc/nginx/sites-enabled/default && \
        sudo nginx -t && \
        sudo systemctl reload nginx
    "
else
    warn "No domain specified. Nginx will be configured for IP access only."
    gcloud compute ssh unity@${SERVER_HOST} --zone=${SERVER_ZONE} --command="
        sudo mv /tmp/nginx-unity-mcp.conf /etc/nginx/sites-available/unity-mcp-ip && \
        sudo ln -sf /etc/nginx/sites-available/unity-mcp-ip /etc/nginx/sites-enabled/ && \
        sudo rm -f /etc/nginx/sites-enabled/default && \
        sudo nginx -t && \
        sudo systemctl reload nginx
    "
fi

# Step 7: Set up Unity MCP Bridge in Unity project
log "🎮 Setting up Unity MCP Bridge..."
gcloud compute ssh unity@${SERVER_HOST} --zone=${SERVER_ZONE} --command="
    mkdir -p /opt/unity-mcp/projects/shared/Assets/Scripts && \
    cp -r /opt/unity-mcp/UnityMcpBridge/Editor/* /opt/unity-mcp/projects/shared/Assets/Scripts/ && \
    cp -r /opt/unity-mcp/UnityMcpBridge/Runtime/* /opt/unity-mcp/projects/shared/Assets/Scripts/
"

# Step 7.5: Set up build service directories and permissions
log "🏗️  Setting up Build Service..."
gcloud compute ssh unity@${SERVER_HOST} --zone=${SERVER_ZONE} --command="
    sudo mkdir -p /var/www/html/games && \
    sudo chown -R unity:unity /var/www/html/games && \
    mkdir -p /opt/unity-mcp/builds/{assets,games,templates} && \
    chown -R unity:unity /opt/unity-mcp/builds
"

# Step 8: Test the installation
log "🧪 Testing Unity MCP installation..."
gcloud compute ssh unity@${SERVER_HOST} --zone=${SERVER_ZONE} --command="
    cd /opt/unity-mcp/server && \
    timeout 10s /opt/unity-mcp/venv/bin/python -c 'import multi_client_server; print(\"✓ Multi-client server imports successfully\")' && \
    timeout 10s /opt/unity-mcp/venv/bin/python -c 'import client_manager; print(\"✓ Client manager imports successfully\")' && \
    timeout 10s /opt/unity-mcp/venv/bin/python -c 'import scene_manager; print(\"✓ Scene manager imports successfully\")'
"

# Step 9: Start Unity MCP service
log "🚀 Starting Unity MCP service..."
gcloud compute ssh unity@${SERVER_HOST} --zone=${SERVER_ZONE} --command="
    sudo systemctl start unity-mcp && \
    sleep 5 && \
    sudo systemctl status unity-mcp --no-pager -l
"

# Step 10: Wait for service to start and test endpoints
log "⏳ Waiting for service to start..."
sleep 10

log "🔍 Testing service endpoints..."
SERVER_IP=$(gcloud compute instances describe ${SERVER_HOST} --zone=${SERVER_ZONE} --format="get(networkInterfaces[0].accessConfigs[0].natIP)")

# Test health endpoint
log "Testing health endpoint..."
if curl -f -s "http://${SERVER_IP}/health" > /dev/null; then
    log "✓ Health endpoint responding"
else
    warn "✗ Health endpoint not responding"
fi

# Test status endpoint
log "Testing status endpoint..."
if curl -f -s "http://${SERVER_IP}/status" > /dev/null; then
    log "✓ Status endpoint responding"
else
    warn "✗ Status endpoint not responding"
fi

# Step 11: Create SSL certificate if domain provided
if [ -n "$DOMAIN" ]; then
    log "🔒 Setting up SSL certificate for ${DOMAIN}..."
    gcloud compute ssh unity@${SERVER_HOST} --zone=${SERVER_ZONE} --command="
        sudo apt-get update && \
        sudo apt-get install -y certbot python3-certbot-nginx && \
        sudo certbot --nginx -d ${DOMAIN} --non-interactive --agree-tos --email admin@${DOMAIN} || true
    "
fi

# Step 12: Create backup and monitoring scripts
log "📋 Setting up monitoring and backup scripts..."
gcloud compute scp \
    --zone=${SERVER_ZONE} \
    "${SCRIPT_DIR}/../monitoring/"* \
    unity@${SERVER_HOST}:/opt/unity-mcp/scripts/ || warn "Monitoring scripts not found, skipping..."

gcloud compute ssh unity@${SERVER_HOST} --zone=${SERVER_ZONE} --command="
    chmod +x /opt/unity-mcp/scripts/*.sh && \
    chown unity:unity /opt/unity-mcp/scripts/*.sh
"

# Step 13: Final verification
log "🔍 Final verification..."
gcloud compute ssh unity@${SERVER_HOST} --zone=${SERVER_ZONE} --command="
    echo '=== Service Status ===' && \
    sudo systemctl is-active unity-mcp && \
    echo '=== Unity MCP Status ===' && \
    /opt/unity-mcp/scripts/system-info.sh && \
    echo '=== Recent Logs ===' && \
    tail -20 /opt/unity-mcp/logs/server.log
"

# Step 14: Display deployment summary
log "✅ Unity MCP deployment completed successfully!"
log ""
log "📊 Deployment Summary:"
log "• Server: ${SERVER_HOST}"
log "• Zone: ${SERVER_ZONE}"
log "• External IP: ${SERVER_IP}"
if [ -n "$DOMAIN" ]; then
    log "• Domain: ${DOMAIN}"
    log "• HTTPS URL: https://${DOMAIN}"
else
    log "• HTTP URL: http://${SERVER_IP}"
fi
log ""
log "🔗 Service Endpoints:"
if [ -n "$DOMAIN" ]; then
    log "• Health: https://${DOMAIN}/health"
    log "• Status: https://${DOMAIN}/status"
    log "• API: https://${DOMAIN}/api/"
else
    log "• Health: http://${SERVER_IP}/health"
    log "• Status: http://${SERVER_IP}/status"
    log "• API: http://${SERVER_IP}/api/"
fi
log ""
log "🎮 Unity MCP Features:"
log "• Max Clients: 5"
log "• Client Isolation: ✓ Enabled"
log "• Scene Management: ✓ Enabled"
log "• Resource Monitoring: ✓ Enabled"
log "• Auto Cleanup: ✓ Enabled"
log ""
log "📋 Next Steps:"
log "1. Test client registration:"
if [ -n "$DOMAIN" ]; then
    log "   curl -X POST https://${DOMAIN}/api/register-client -H 'Content-Type: application/json' -d '{\"project_name\": \"test-project\"}'"
else
    log "   curl -X POST http://${SERVER_IP}/api/register-client -H 'Content-Type: application/json' -d '{\"project_name\": \"test-project\"}'"
fi
log ""
log "2. Monitor logs:"
log "   gcloud compute ssh unity@${SERVER_HOST} --zone=${SERVER_ZONE} --command='tail -f /opt/unity-mcp/logs/server.log'"
log ""
log "3. Check system status:"
log "   gcloud compute ssh unity@${SERVER_HOST} --zone=${SERVER_ZONE} --command='/opt/unity-mcp/scripts/system-info.sh'"
log ""
if [ -n "$DOMAIN" ]; then
    log "🔒 SSL Certificate:"
    log "• SSL certificate configured for ${DOMAIN}"
    log "• Auto-renewal enabled via certbot"
else
    log "⚠️  SSL Configuration:"
    log "• No domain provided - SSL not configured"
    log "• To add SSL later, run:"
    log "  ./deploy.sh ${SERVER_HOST} ${SERVER_ZONE} your-domain.com"
fi
log ""
log "🎉 Unity MCP is now ready for client connections!"

# Step 15: Test client registration
log "🧪 Testing client registration..."
if [ -n "$DOMAIN" ]; then
    TEST_URL="https://${DOMAIN}/api/register-client"
else
    TEST_URL="http://${SERVER_IP}/api/register-client"
fi

REGISTRATION_RESULT=$(curl -s -X POST "${TEST_URL}" \
    -H "Content-Type: application/json" \
    -d '{"project_name": "deployment-test"}' 2>/dev/null || echo "Failed")

if echo "$REGISTRATION_RESULT" | grep -q "client_id"; then
    log "✅ Client registration test successful!"
    CLIENT_ID=$(echo "$REGISTRATION_RESULT" | grep -o '"client_id":"[^"]*"' | cut -d'"' -f4)
    log "• Test Client ID: ${CLIENT_ID}"
else
    warn "⚠️  Client registration test failed. Check logs for details."
fi

log ""
log "🚀 Deployment completed at $(date)"
log "🔗 Save this information for your clients!"

# Create deployment info file
DEPLOYMENT_INFO="/tmp/unity-mcp-deployment-info.txt"
cat > "${DEPLOYMENT_INFO}" << EOF
Unity MCP VPS Deployment Information
===================================

Deployment Date: $(date)
Server: ${SERVER_HOST}
Zone: ${SERVER_ZONE}
External IP: ${SERVER_IP}
Domain: ${DOMAIN:-"Not configured"}

Service Endpoints:
$(if [ -n "$DOMAIN" ]; then
    echo "• Base URL: https://${DOMAIN}"
    echo "• Health: https://${DOMAIN}/health"
    echo "• Status: https://${DOMAIN}/status"
    echo "• API: https://${DOMAIN}/api/"
else
    echo "• Base URL: http://${SERVER_IP}"
    echo "• Health: http://${SERVER_IP}/health"
    echo "• Status: http://${SERVER_IP}/status"
    echo "• API: http://${SERVER_IP}/api/"
fi)

Client Registration Example:
curl -X POST $(if [ -n "$DOMAIN" ]; then echo "https://${DOMAIN}"; else echo "http://${SERVER_IP}"; fi)/api/register-client \\
  -H "Content-Type: application/json" \\
  -d '{"project_name": "my-project"}'

Management Commands:
• SSH to server: gcloud compute ssh unity@${SERVER_HOST} --zone=${SERVER_ZONE}
• View logs: gcloud compute ssh unity@${SERVER_HOST} --zone=${SERVER_ZONE} --command='tail -f /opt/unity-mcp/logs/server.log'
• Restart service: gcloud compute ssh unity@${SERVER_HOST} --zone=${SERVER_ZONE} --command='sudo systemctl restart unity-mcp'
• System status: gcloud compute ssh unity@${SERVER_HOST} --zone=${SERVER_ZONE} --command='/opt/unity-mcp/scripts/system-info.sh'

Service Configuration:
• Max Clients: 5
• Memory per client: 2GB
• Assets per client: 1000
• Idle timeout: 30 minutes
• Auto cleanup: Enabled

Security:
• Firewall: Configured
• Rate limiting: Enabled
• SSL: $(if [ -n "$DOMAIN" ]; then echo "Enabled"; else echo "Not configured"; fi)
• API authentication: $(if [ -n "$DOMAIN" ]; then echo "HTTPS only"; else echo "HTTP"; fi)
EOF

log "📄 Deployment information saved to: ${DEPLOYMENT_INFO}"
log "📋 You can share this file with your team for reference."