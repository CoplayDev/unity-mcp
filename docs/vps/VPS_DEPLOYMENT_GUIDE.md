# Unity MCP VPS Deployment Guide

This guide covers deploying Unity MCP on a Google Cloud Platform Virtual Private Server (VPS) to support up to 5 concurrent clients on a single Unity instance with full isolation.

## Overview

The Unity MCP VPS deployment provides:
- **Configurable multi-client support**: 1 to unlimited concurrent clients
- **Client isolation**: Separate namespaces and resource allocation
- **Scene management**: Individual scenes per client project
- **Resource monitoring**: Memory, CPU, and asset tracking
- **Auto-scaling**: Dynamic resource allocation and cleanup
- **Production-ready**: SSL, monitoring, backups, and security

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    Google Cloud VPS                         │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │                 Nginx (Reverse Proxy)                  │ │
│  │                     SSL/TLS + Rate Limiting            │ │
│  └─────────────────┬───────────────────────────────────────┘ │
│                    │                                         │
│  ┌─────────────────▼───────────────────────────────────────┐ │
│  │            Multi-Client Unity MCP Server               │ │
│  │              (Python FastAPI/aiohttp)                  │ │
│  │                                                         │ │
│  │  ┌─────────────┐ ┌─────────────┐ ┌─────────────────────┐ │ │
│  │  │   Client    │ │   Scene     │ │    Resource         │ │ │
│  │  │  Manager    │ │  Manager    │ │    Monitor          │ │ │
│  │  └─────────────┘ └─────────────┘ └─────────────────────┘ │ │
│  └─────────────────┬───────────────────────────────────────┘ │
│                    │                                         │
│  ┌─────────────────▼───────────────────────────────────────┐ │
│  │                Unity Headless                           │ │
│  │              (Single Instance)                          │ │
│  │                                                         │ │
│  │  ┌─────────────┐ ┌─────────────┐ ┌─────────────────────┐ │ │
│  │  │ Namespace   │ │ Namespace   │ │ Namespace           │ │ │
│  │  │ Client_1    │ │ Client_2    │ │ Client_N            │ │ │
│  │  │ (Isolated)  │ │ (Isolated)  │ │ (Isolated)          │ │ │
│  │  └─────────────┘ └─────────────┘ └─────────────────────┘ │ │
│  └─────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

## Prerequisites

- **GCP Account** with billing enabled
- **Domain name** (optional but recommended for SSL)
- **Unity license** (.ulf file or Unity ID credentials)
- **SSH key pair** for secure access
- **Local machine** with gcloud CLI installed

## Step 1: Create GCP Virtual Machine

### Machine Specifications

Choose based on your expected client count:

| Client Count | Instance Type | vCPUs | RAM | Disk | Est. Cost/Month |
|-------------|---------------|-------|-----|------|-----------------|
| **1-5** | n2-standard-4 | 4 | 16GB | 100GB | ~$120 |
| **6-15** | n2-standard-8 | 8 | 32GB | 200GB | ~$240 |
| **16-30** | n2-standard-16 | 16 | 64GB | 400GB | ~$480 |
| **30+** | n2-standard-32+ | 32+ | 128GB+ | 800GB+ | ~$960+ |

**Recommended for most use cases**: n2-standard-8
- **OS**: Ubuntu 22.04 LTS  
- **Region**: Choose closest to your users

### Using gcloud CLI

```bash
# Create instance
gcloud compute instances create unity-mcp-server \
  --zone=us-central1-a \
  --machine-type=n2-standard-8 \
  --image-family=ubuntu-2204-lts \
  --image-project=ubuntu-os-cloud \
  --boot-disk-size=200GB \
  --boot-disk-type=pd-ssd \
  --tags=unity-mcp,http-server,https-server

# Reserve static IP
gcloud compute addresses create unity-mcp-ip --region=us-central1

# Get the static IP
STATIC_IP=$(gcloud compute addresses describe unity-mcp-ip --region=us-central1 --format="get(address)")

# Assign static IP to instance
gcloud compute instances delete-access-config unity-mcp-server \
  --zone=us-central1-a --access-config-name="external-nat"
  
gcloud compute instances add-access-config unity-mcp-server \
  --zone=us-central1-a --address=$STATIC_IP
```

### Configure Firewall

```bash
# Allow HTTP/HTTPS and Unity MCP ports
gcloud compute firewall-rules create unity-mcp-access \
  --allow tcp:80,tcp:443,tcp:8080,tcp:6400 \
  --source-ranges 0.0.0.0/0 \
  --target-tags unity-mcp

# Restrict SSH to your IP
gcloud compute firewall-rules create unity-mcp-ssh \
  --allow tcp:22 \
  --source-ranges YOUR_IP/32 \
  --target-tags unity-mcp
```

## Step 2: Initial Server Setup

### Connect and Run Setup Script

```bash
# Connect to server
gcloud compute ssh unity@unity-mcp-server --zone=us-central1-a

# Clone the repository
git clone https://github.com/your-org/unity-mcp.git
cd unity-mcp

# Run setup script
chmod +x scripts/vps/setup-ubuntu.sh
./scripts/vps/setup-ubuntu.sh
```

The setup script will:
- Install all required dependencies
- Create unity user and directory structure
- Configure firewall and security
- Set up virtual display (Xvfb)
- Install monitoring tools
- Configure log rotation

### Reboot System

```bash
sudo reboot
```

## Step 3: Install Unity Editor

### Upload Unity License

Before installing Unity, upload your license file:

```bash
# From your local machine
gcloud compute scp unity.ulf unity@unity-mcp-server:/tmp/ --zone=us-central1-a
```

### Install Unity

```bash
# Connect to server
gcloud compute ssh unity@unity-mcp-server --zone=us-central1-a

# Run Unity installation
cd unity-mcp
chmod +x scripts/vps/install-unity.sh
./scripts/vps/install-unity.sh

# Activate license
/opt/unity-mcp/scripts/activate-license.sh

# Verify installation
/opt/unity-mcp/scripts/unity-status.sh
```

## Step 4: Deploy Unity MCP

### From Local Machine

```bash
# Deploy to server (replace with your domain if you have one)
cd unity-mcp
chmod +x scripts/vps/deploy.sh

# Deploy without domain (HTTP only)
./scripts/vps/deploy.sh unity-mcp-server us-central1-a

# OR deploy with domain (enables HTTPS)
./scripts/vps/deploy.sh unity-mcp-server us-central1-a your-domain.com
```

The deployment script will:
- Upload all Unity MCP code
- Install Python dependencies
- Configure systemd service
- Set up Nginx reverse proxy
- Configure SSL (if domain provided)
- Start all services
- Run verification tests

## Step 5: Verify Deployment

### Check Service Status

```bash
# Connect to server
gcloud compute ssh unity@unity-mcp-server --zone=us-central1-a

# Check service status
sudo systemctl status unity-mcp
/opt/unity-mcp/scripts/system-info.sh
```

### Test API Endpoints

```bash
# Get server IP
SERVER_IP=$(gcloud compute instances describe unity-mcp-server --zone=us-central1-a --format="get(networkInterfaces[0].accessConfigs[0].natIP)")

# Test health endpoint
curl http://$SERVER_IP/health

# Test status endpoint
curl http://$SERVER_IP/status | jq

# Register a test client
curl -X POST http://$SERVER_IP/api/register-client \
  -H "Content-Type: application/json" \
  -d '{"project_name": "test-project"}'
```

## Step 6: Client Integration

### Register New Client

```python
import httpx

async def register_client(base_url: str, project_name: str):
    async with httpx.AsyncClient() as client:
        response = await client.post(
            f"{base_url}/api/register-client",
            json={"project_name": project_name}
        )
        return response.json()

# Usage
client_info = await register_client(
    "https://your-domain.com",  # or http://SERVER_IP
    "my-unity-project"
)
print(f"Client ID: {client_info['client_id']}")
print(f"Namespace: {client_info['scene_namespace']}")
```

### Execute Commands

```python
import httpx

class UnityMCPClient:
    def __init__(self, base_url: str, client_id: str):
        self.base_url = base_url
        self.client_id = client_id
        
    async def execute_command(self, action: str, params: dict):
        async with httpx.AsyncClient() as client:
            response = await client.post(
                f"{self.base_url}/api/execute-command",
                json={
                    "client_id": self.client_id,
                    "action": action,
                    "params": params
                }
            )
            return response.json()

# Usage
client = UnityMCPClient("https://your-domain.com", "your-client-id")

# Create a GameObject
result = await client.execute_command(
    "manage_gameobject",
    {"action": "create", "name": "Cube", "type": "Cube"}
)

# Create a scene
result = await client.execute_command(
    "manage_scene",
    {"action": "create", "scene_name": "MyScene"}
)
```

## Step 7: SSL Configuration (Optional)

If you have a domain, SSL is automatically configured during deployment. To add SSL later:

```bash
# Connect to server
gcloud compute ssh unity@unity-mcp-server --zone=us-central1-a

# Install certbot
sudo apt-get update
sudo apt-get install -y certbot python3-certbot-nginx

# Obtain certificate
sudo certbot --nginx -d your-domain.com

# Verify auto-renewal
sudo certbot renew --dry-run
```

## Step 8: Monitoring and Maintenance

### Monitor System Health

```bash
# Run monitoring check
/opt/unity-mcp/scripts/monitor.sh

# View logs
tail -f /opt/unity-mcp/logs/server.log
tail -f /opt/unity-mcp/logs/unity.log

# Check service metrics
curl http://localhost:8080/metrics
```

### Backup Management

```bash
# Manual backup
/opt/unity-mcp/scripts/backup.sh

# View backup status
ls -la /opt/unity-mcp/backups/

# Restore from backup (if needed)
# Backups are stored as tar.gz files with timestamps
```

### Common Management Tasks

```bash
# Restart Unity MCP service
sudo systemctl restart unity-mcp

# Restart Unity process (admin endpoint)
curl -X POST http://localhost:8080/api/admin/restart-unity

# Clean up idle clients
curl -X POST http://localhost:8080/api/admin/cleanup-idle

# View active clients
curl http://localhost:8080/api/clients | jq
```

## API Reference

### Core Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/health` | GET | Health check |
| `/status` | GET | Detailed server status |
| `/metrics` | GET | Prometheus metrics |

### Client Management

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/register-client` | POST | Register new client |
| `/api/clients` | GET | List all clients |
| `/api/clients/{client_id}` | DELETE | Unregister client |
| `/api/clients/{client_id}/status` | GET | Get client status |

### Command Execution

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/execute-command` | POST | Execute Unity command |
| `/api/commands/{command_id}` | GET | Get command status |

### Scene Management

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/clients/{client_id}/scenes` | GET | List client scenes |
| `/api/clients/{client_id}/scenes` | POST | Create new scene |
| `/api/clients/{client_id}/scenes/{scene_name}/load` | POST | Load scene |

### Admin Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/admin/restart-unity` | POST | Restart Unity process |
| `/api/admin/cleanup-idle` | POST | Clean up idle clients |

## Troubleshooting

### Common Issues

1. **Unity Won't Start**
   ```bash
   # Check license
   ls -la /opt/unity-mcp/config/unity.ulf
   
   # Check display
   echo $DISPLAY  # Should be :99
   
   # Check Xvfb
   ps aux | grep Xvfb
   
   # Restart virtual display
   sudo systemctl restart xvfb
   ```

2. **Service Not Starting**
   ```bash
   # Check service status
   sudo systemctl status unity-mcp
   
   # View service logs
   sudo journalctl -u unity-mcp -f
   
   # Check Python dependencies
   /opt/unity-mcp/venv/bin/pip list
   ```

3. **API Not Responding**
   ```bash
   # Check if port is listening
   sudo netstat -tlpn | grep :8080
   
   # Check nginx status
   sudo systemctl status nginx
   
   # Test local API
   curl http://localhost:8080/health
   ```

4. **High Resource Usage**
   ```bash
   # Monitor resources
   htop
   
   # Check Unity memory
   ps aux | grep Unity
   
   # Review client limits
   curl http://localhost:8080/status | jq '.clients'
   ```

### Log Locations

- **Service logs**: `/opt/unity-mcp/logs/server.log`
- **Unity logs**: `/opt/unity-mcp/logs/unity.log`
- **Nginx logs**: `/var/log/nginx/unity-mcp-*.log`
- **System logs**: `sudo journalctl -u unity-mcp`

## Security Best Practices

1. **SSH Security**
   - Use SSH keys only
   - Disable password authentication
   - Restrict SSH to specific IPs

2. **API Security**
   - Use HTTPS in production
   - Implement rate limiting (configured in nginx)
   - Monitor API usage

3. **System Security**
   - Keep system updated
   - Use firewall rules
   - Monitor failed login attempts
   - Regular security scans

4. **Network Security**
   - Restrict admin endpoints to trusted IPs
   - Use strong SSL configuration
   - Enable security headers

## Performance Optimization

### Vertical Scaling
- Upgrade to larger instance type as needed
- Add GPU for graphics-intensive operations
- Increase disk space for large projects

### Resource Limits
Configure per-client limits in `/etc/unity-mcp/environment`:
```bash
MAX_CLIENTS=5
MAX_MEMORY_PER_CLIENT=2048
MAX_ASSETS_PER_CLIENT=1000
CLEANUP_IDLE_MINUTES=30
```

### Monitoring Thresholds
Adjust alert thresholds in monitoring scripts:
```bash
ALERT_THRESHOLD_CPU=80
ALERT_THRESHOLD_MEMORY=85
ALERT_THRESHOLD_DISK=90
```

## Cost Optimization

### Instance Recommendations
- **Development**: n2-standard-4 (4 vCPUs, 16GB RAM) - ~$120/month
- **Production**: n2-standard-8 (8 vCPUs, 32GB RAM) - ~$240/month
- **High-performance**: n2-standard-16 + T4 GPU - ~$400/month

### Cost-Saving Tips
- Use committed use discounts (up to 57% savings)
- Schedule auto-shutdown for development instances
- Monitor and optimize resource usage
- Use preemptible instances for testing

## Support

For issues or questions:

1. **Check logs** first for error details
2. **Review this documentation** for common solutions
3. **Search GitHub issues** for similar problems
4. **Create new issue** with:
   - Error messages from logs
   - Steps to reproduce
   - System information
   - Configuration details

## Example Client Implementation

See [Client Examples](../examples/) for complete client implementations in:
- Python
- JavaScript/Node.js
- C#/.NET
- Unity C# (for Unity-to-Unity communication)