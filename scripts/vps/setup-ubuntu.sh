#!/bin/bash
# Unity MCP VPS Setup Script for Ubuntu 22.04
# Prepares the server for Unity MCP multi-client deployment

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
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

# Check if running as root
if [[ $EUID -eq 0 ]]; then
   error "This script should not be run as root. Please run as a regular user with sudo privileges."
fi

log "Starting Unity MCP VPS setup on Ubuntu 22.04..."

# Update system packages
log "Updating system packages..."
sudo apt-get update
sudo apt-get upgrade -y

# Install base dependencies
log "Installing base dependencies..."
sudo apt-get install -y \
    build-essential \
    curl \
    wget \
    git \
    python3.11 \
    python3.11-dev \
    python3-pip \
    python3-venv \
    software-properties-common \
    apt-transport-https \
    ca-certificates \
    gnupg \
    lsb-release \
    unzip \
    htop \
    tree \
    vim \
    nano \
    jq \
    supervisor \
    nginx \
    ufw \
    fail2ban

# Install Unity-specific dependencies
log "Installing Unity runtime dependencies..."
sudo apt-get install -y \
    xvfb \
    libglu1-mesa \
    libxi6 \
    libxrender1 \
    libxtst6 \
    libfreetype6 \
    libfontconfig1 \
    libgtk-3-0 \
    libnss3 \
    libasound2 \
    libxss1 \
    libgconf-2-4 \
    libxtst6 \
    libatspi2.0-0 \
    libgbm1 \
    libxrandr2 \
    libasound2-dev \
    libpangocairo-1.0-0 \
    libatk1.0-0 \
    libcairo-gobject2 \
    libgtk-3-0 \
    libgdk-pixbuf2.0-0

# Install Node.js (for monitoring tools)
log "Installing Node.js..."
curl -fsSL https://deb.nodesource.com/setup_18.x | sudo -E bash -
sudo apt-get install -y nodejs

# Install Docker (optional, for containerized monitoring)
log "Installing Docker..."
curl -fsSL https://download.docker.com/linux/ubuntu/gpg | sudo gpg --dearmor -o /usr/share/keyrings/docker-archive-keyring.gpg
echo "deb [arch=amd64 signed-by=/usr/share/keyrings/docker-archive-keyring.gpg] https://download.docker.com/linux/ubuntu $(lsb_release -cs) stable" | sudo tee /etc/apt/sources.list.d/docker.list > /dev/null
sudo apt-get update
sudo apt-get install -y docker-ce docker-ce-cli containerd.io docker-compose-plugin

# Add current user to docker group
sudo usermod -aG docker $USER

# Create unity user for running Unity processes
log "Creating unity user..."
if ! id "unity" &>/dev/null; then
    sudo useradd -m -s /bin/bash -G sudo unity
    log "Unity user created"
else
    log "Unity user already exists"
fi

# Create directory structure
log "Creating directory structure..."
sudo mkdir -p /opt/unity-mcp/{server,projects,builds,logs,config,scripts,backups}
sudo mkdir -p /opt/unity-mcp/projects/{shared,client1,client2,client3,client4,client5}
sudo mkdir -p /var/log/unity-mcp
sudo mkdir -p /etc/unity-mcp

# Create logs directory structure
sudo mkdir -p /opt/unity-mcp/logs/{unity,server,nginx,system}

# Set ownership and permissions
sudo chown -R unity:unity /opt/unity-mcp
sudo chown -R unity:unity /var/log/unity-mcp
sudo chmod -R 755 /opt/unity-mcp
sudo chmod -R 755 /var/log/unity-mcp

# Create symbolic link for easier access
sudo ln -sf /opt/unity-mcp /home/unity/unity-mcp
sudo chown -h unity:unity /home/unity/unity-mcp

# Install Python virtual environment for Unity MCP
log "Setting up Python environment..."
sudo -u unity python3.11 -m venv /opt/unity-mcp/venv
sudo -u unity /opt/unity-mcp/venv/bin/pip install --upgrade pip setuptools wheel

# Install uv for faster package management
sudo -u unity /opt/unity-mcp/venv/bin/pip install uv

# Configure firewall
log "Configuring firewall..."
sudo ufw --force reset
sudo ufw default deny incoming
sudo ufw default allow outgoing

# Allow SSH (current session)
sudo ufw allow 22/tcp

# Allow HTTP/HTTPS
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp

# Allow Unity MCP ports
sudo ufw allow 8080/tcp comment 'Unity MCP HTTP API'
sudo ufw allow 6400/tcp comment 'Unity MCP Bridge'

# Enable firewall
sudo ufw --force enable

# Configure fail2ban
log "Configuring fail2ban..."
sudo systemctl enable fail2ban
sudo systemctl start fail2ban

# Configure nginx
log "Configuring nginx..."
sudo systemctl enable nginx
sudo systemctl stop nginx  # We'll configure it later

# Create basic nginx configuration
sudo tee /etc/nginx/sites-available/default > /dev/null << 'EOF'
server {
    listen 80 default_server;
    listen [::]:80 default_server;
    
    root /var/www/html;
    index index.html index.htm index.nginx-debian.html;
    
    server_name _;
    
    location / {
        return 444;  # Close connection for undefined hosts
    }
    
    location /health {
        return 200 'Unity MCP VPS Health Check\n';
        add_header Content-Type text/plain;
    }
}
EOF

sudo systemctl start nginx

# Setup log rotation
log "Configuring log rotation..."
sudo tee /etc/logrotate.d/unity-mcp > /dev/null << 'EOF'
/opt/unity-mcp/logs/*.log {
    daily
    missingok
    rotate 30
    compress
    delaycompress
    notifempty
    create 644 unity unity
    postrotate
        systemctl reload unity-mcp || true
    endscript
}

/var/log/unity-mcp/*.log {
    daily
    missingok
    rotate 30
    compress
    delaycompress
    notifempty
    create 644 unity unity
}
EOF

# Install system monitoring tools
log "Installing monitoring tools..."
sudo apt-get install -y prometheus-node-exporter
sudo systemctl enable prometheus-node-exporter
sudo systemctl start prometheus-node-exporter

# Create basic health check script
log "Creating health check scripts..."
sudo tee /opt/unity-mcp/scripts/health-check.sh > /dev/null << 'EOF'
#!/bin/bash
# Unity MCP Health Check Script

# Check Unity process
UNITY_PID=$(pgrep -f "Unity.*batchmode" || echo "0")
if [ "$UNITY_PID" != "0" ]; then
    echo "Unity: Running (PID: $UNITY_PID)"
    UNITY_STATUS=0
else
    echo "Unity: Not running"
    UNITY_STATUS=1
fi

# Check server health
HTTP_STATUS=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:8080/health || echo "000")
if [ "$HTTP_STATUS" = "200" ]; then
    echo "Server: Healthy"
    SERVER_STATUS=0
else
    echo "Server: Unhealthy (HTTP $HTTP_STATUS)"
    SERVER_STATUS=1
fi

# Check disk space
DISK_USAGE=$(df /opt/unity-mcp | awk 'NR==2 {print $5}' | sed 's/%//')
if [ "$DISK_USAGE" -lt 90 ]; then
    echo "Disk: OK ($DISK_USAGE%)"
    DISK_STATUS=0
else
    echo "Disk: Warning ($DISK_USAGE%)"
    DISK_STATUS=1
fi

# Overall status
OVERALL_STATUS=$((UNITY_STATUS + SERVER_STATUS + DISK_STATUS))
if [ "$OVERALL_STATUS" -eq 0 ]; then
    echo "Overall: Healthy"
    exit 0
else
    echo "Overall: Issues detected"
    exit 1
fi
EOF

sudo chmod +x /opt/unity-mcp/scripts/health-check.sh
sudo chown unity:unity /opt/unity-mcp/scripts/health-check.sh

# Create system info script
sudo tee /opt/unity-mcp/scripts/system-info.sh > /dev/null << 'EOF'
#!/bin/bash
# System Information Script

echo "=== Unity MCP VPS System Information ==="
echo "Date: $(date)"
echo "Hostname: $(hostname)"
echo "Uptime: $(uptime -p)"
echo ""

echo "=== System Resources ==="
echo "CPU Usage: $(top -bn1 | grep "Cpu(s)" | awk '{print $2}' | cut -d'%' -f1)%"
echo "Memory Usage: $(free -m | awk 'NR==2{printf "%.1f%%", $3*100/$2}')"
echo "Disk Usage: $(df -h /opt/unity-mcp | awk 'NR==2 {print $5}')"
echo "Load Average: $(uptime | awk -F'load average:' '{print $2}')"
echo ""

echo "=== Unity MCP Status ==="
if [ -f /opt/unity-mcp/scripts/health-check.sh ]; then
    /opt/unity-mcp/scripts/health-check.sh
fi
echo ""

echo "=== Network Information ==="
echo "External IP: $(curl -s ifconfig.me || echo 'Unknown')"
echo "Internal IP: $(hostname -I | awk '{print $1}')"
echo ""

echo "=== Disk Space ==="
df -h
EOF

sudo chmod +x /opt/unity-mcp/scripts/system-info.sh
sudo chown unity:unity /opt/unity-mcp/scripts/system-info.sh

# Setup cron jobs for maintenance
log "Setting up maintenance cron jobs..."
sudo -u unity crontab -l 2>/dev/null | {
    cat
    echo "# Unity MCP Maintenance Jobs"
    echo "0 2 * * * /opt/unity-mcp/scripts/backup.sh >> /opt/unity-mcp/logs/backup.log 2>&1"
    echo "*/5 * * * * /opt/unity-mcp/scripts/health-check.sh >> /opt/unity-mcp/logs/health.log 2>&1"
    echo "0 3 * * 0 /opt/unity-mcp/scripts/cleanup.sh >> /opt/unity-mcp/logs/cleanup.log 2>&1"
} | sudo -u unity crontab -

# Create environment file
log "Creating environment configuration..."
sudo tee /etc/unity-mcp/environment > /dev/null << 'EOF'
# Unity MCP Environment Configuration
UNITY_VERSION=6000.0.3f1
UNITY_PATH=/opt/unity/editors/6000.0.3f1/Editor/Unity
UNITY_PROJECT_PATH=/opt/unity-mcp/projects/shared
UNITY_HEADLESS=true
UNITY_MCP_AUTOSTART=true
UNITY_MCP_PORT=6400
UNITY_MCP_LOG_PATH=/opt/unity-mcp/logs/unity-mcp.log
LOG_LEVEL=INFO
MAX_CLIENTS=10
HOST=0.0.0.0
PORT=8080
DISPLAY=:99
HOME=/home/unity

# Build Service Configuration
BUILD_SERVICE_API_KEY=your-secure-api-key-here
BASE_GAME_URL=https://your-domain.com/games
MAX_CONCURRENT_BUILDS=3
EOF

sudo chown unity:unity /etc/unity-mcp/environment
sudo chmod 644 /etc/unity-mcp/environment

# Create initial configuration files
log "Creating configuration files..."
sudo tee /opt/unity-mcp/config/server.conf > /dev/null << 'EOF'
# Unity MCP Server Configuration
[server]
host = 0.0.0.0
port = 8080
max_clients = 10
unity_project_path = /opt/unity-mcp/projects/shared

[unity]
version = 6000.0.3f1
path = /opt/unity/editors/6000.0.3f1/Editor/Unity
headless = true
display = :99

[logging]
level = INFO
format = %(asctime)s - %(name)s - %(levelname)s - %(message)s
file = /opt/unity-mcp/logs/server.log

[security]
enable_cors = true
allowed_origins = *
api_key_required = false

[resources]
max_memory_per_client = 2048
max_assets_per_client = 1000
cleanup_idle_minutes = 30
EOF

sudo chown unity:unity /opt/unity-mcp/config/server.conf

# Install and configure Xvfb for headless display
log "Configuring virtual display..."
sudo tee /etc/systemd/system/xvfb.service > /dev/null << 'EOF'
[Unit]
Description=X Virtual Framebuffer
After=network.target

[Service]
Type=simple
User=unity
Group=unity
ExecStart=/usr/bin/Xvfb :99 -screen 0 1024x768x24 -ac +extension GLX +render -noreset
ExecStop=/usr/bin/pkill Xvfb
Restart=always
RestartSec=5

[Install]
WantedBy=multi-user.target
EOF

sudo systemctl enable xvfb
sudo systemctl start xvfb

# Final system optimizations
log "Applying system optimizations..."

# Increase file limits
echo "unity soft nofile 65536" | sudo tee -a /etc/security/limits.conf
echo "unity hard nofile 65536" | sudo tee -a /etc/security/limits.conf

# Configure kernel parameters
echo "net.core.somaxconn = 1024" | sudo tee -a /etc/sysctl.conf
echo "net.ipv4.tcp_max_syn_backlog = 1024" | sudo tee -a /etc/sysctl.conf
sudo sysctl -p

# Create swap file if needed (for systems with less than 8GB RAM)
TOTAL_MEM=$(free -m | awk 'NR==2{print $2}')
if [ "$TOTAL_MEM" -lt 8192 ] && [ ! -f /swapfile ]; then
    log "Creating swap file (system has less than 8GB RAM)..."
    sudo fallocate -l 4G /swapfile
    sudo chmod 600 /swapfile
    sudo mkswap /swapfile
    sudo swapon /swapfile
    echo '/swapfile none swap sw 0 0' | sudo tee -a /etc/fstab
fi

# Clean up
log "Cleaning up..."
sudo apt-get autoremove -y
sudo apt-get autoclean

# Create welcome message
sudo tee /opt/unity-mcp/README.txt > /dev/null << 'EOF'
Unity MCP VPS Setup Complete!

Directory Structure:
/opt/unity-mcp/
├── server/          # Python server code
├── projects/        # Unity projects (client isolation)
├── builds/          # Build outputs
├── logs/           # Log files
├── config/         # Configuration files
├── scripts/        # Maintenance scripts
└── backups/        # Backup files

Key Commands:
- Check status: /opt/unity-mcp/scripts/system-info.sh
- Health check: /opt/unity-mcp/scripts/health-check.sh
- View logs: tail -f /opt/unity-mcp/logs/server.log

Next Steps:
1. Install Unity Editor: ./scripts/vps/install-unity.sh
2. Deploy Unity MCP: ./scripts/vps/deploy.sh
3. Configure SSL: certbot --nginx -d your-domain.com

For support, check the logs in /opt/unity-mcp/logs/
EOF

sudo chown unity:unity /opt/unity-mcp/README.txt

log "✅ Unity MCP VPS setup completed successfully!"
log ""
log "Next steps:"
log "1. Install Unity Editor: run install-unity.sh"
log "2. Upload Unity MCP server code"
log "3. Configure SSL certificates"
log "4. Start the Unity MCP service"
log ""
log "System information script: /opt/unity-mcp/scripts/system-info.sh"
log "Health check script: /opt/unity-mcp/scripts/health-check.sh"
log ""
log "⚠️  Please reboot the system to ensure all changes take effect:"
log "   sudo reboot"