# Unity MCP VPS Configuration Guide

This guide explains how to configure your Unity MCP VPS deployment for different numbers of concurrent clients and resource requirements.

## Client Limit Configuration

The number of concurrent clients is fully configurable and can be adjusted based on your needs and server resources.

### Environment Configuration

The primary way to set client limits is through the environment file:

```bash
# Edit environment configuration
sudo nano /etc/unity-mcp/environment
```

Set the `MAX_CLIENTS` variable:

```bash
# Client limit options:
MAX_CLIENTS=5      # Support exactly 5 clients
MAX_CLIENTS=10     # Support exactly 10 clients (default)
MAX_CLIENTS=25     # Support exactly 25 clients
MAX_CLIENTS=0      # Unlimited clients (resource-limited only)
```

### Server Configuration

You can also configure limits in the server configuration file:

```bash
# Edit server configuration
sudo nano /opt/unity-mcp/config/server.conf
```

```ini
[server]
host = 0.0.0.0
port = 8080
max_clients = 10  # Adjust this value

[resources]
max_memory_per_client = 2048      # MB per client
max_assets_per_client = 1000      # Assets per client
cleanup_idle_minutes = 30         # Idle timeout
```

### Apply Configuration Changes

After changing configuration:

```bash
# Restart the service
sudo systemctl restart unity-mcp

# Verify new settings
curl http://localhost:8080/status | jq '.server.max_clients'
```

## Resource Planning by Client Count

### Hardware Recommendations

| Clients | vCPUs | RAM | Disk | GCP Instance | Monthly Cost* |
|---------|-------|-----|------|--------------|---------------|
| **1-3** | 4 | 16GB | 100GB | n2-standard-4 | ~$120 |
| **4-10** | 8 | 32GB | 200GB | n2-standard-8 | ~$240 |
| **11-20** | 16 | 64GB | 400GB | n2-standard-16 | ~$480 |
| **21-50** | 32 | 128GB | 800GB | n2-standard-32 | ~$960 |
| **50+** | Custom | Custom | Custom | Multiple instances | Variable |

*Estimated costs for us-central1 region

### Memory Calculations

Each client uses approximately:
- **Base Unity overhead**: ~4GB (shared)
- **Per client**: ~2GB (configurable)
- **System overhead**: ~2GB

**Total RAM needed** = 6GB + (2GB × number_of_clients)

Examples:
- **5 clients**: 6GB + (5 × 2GB) = **16GB minimum**
- **10 clients**: 6GB + (10 × 2GB) = **26GB minimum** 
- **20 clients**: 6GB + (20 × 2GB) = **46GB minimum**

### CPU Recommendations

- **Base Unity**: 2-4 cores
- **Per client**: 0.5-1 core during active use
- **Recommended**: 1 core per 2-3 clients + 4 base cores

### Storage Requirements

- **Unity installation**: ~15GB
- **System and logs**: ~10GB
- **Per client project**: 1-5GB (varies by content)
- **Backup space**: 20-50% of total usage

## Performance Tuning by Client Count

### For 1-5 Clients (Small Scale)

```bash
# Environment settings
MAX_CLIENTS=5
MAX_MEMORY_PER_CLIENT=3072    # 3GB per client
MAX_ASSETS_PER_CLIENT=1500
CLEANUP_IDLE_MINUTES=30

# Unity settings
UNITY_BATCH_SIZE=small
UNITY_MEMORY_LIMIT=16G
```

### For 6-15 Clients (Medium Scale)

```bash
# Environment settings  
MAX_CLIENTS=15
MAX_MEMORY_PER_CLIENT=2048    # 2GB per client
MAX_ASSETS_PER_CLIENT=1000
CLEANUP_IDLE_MINUTES=20

# Unity settings
UNITY_BATCH_SIZE=medium
UNITY_MEMORY_LIMIT=32G

# System optimizations
echo 'vm.max_map_count=262144' >> /etc/sysctl.conf
echo 'fs.file-max=2097152' >> /etc/sysctl.conf
```

### For 16+ Clients (Large Scale)

```bash
# Environment settings
MAX_CLIENTS=25
MAX_MEMORY_PER_CLIENT=1536    # 1.5GB per client
MAX_ASSETS_PER_CLIENT=750
CLEANUP_IDLE_MINUTES=15

# System optimizations
echo '* soft nofile 65536' >> /etc/security/limits.conf
echo '* hard nofile 65536' >> /etc/security/limits.conf
echo 'unity soft nproc 32768' >> /etc/security/limits.conf
echo 'unity hard nproc 32768' >> /etc/security/limits.conf

# Kernel parameters
echo 'net.core.somaxconn = 65535' >> /etc/sysctl.conf
echo 'net.ipv4.tcp_max_syn_backlog = 65535' >> /etc/sysctl.conf
echo 'vm.swappiness=10' >> /etc/sysctl.conf
```

### For Unlimited Clients (Resource-Limited)

```bash
# Environment settings
MAX_CLIENTS=0                 # Unlimited
MAX_MEMORY_PER_CLIENT=1024    # 1GB per client (conservative)
MAX_ASSETS_PER_CLIENT=500
CLEANUP_IDLE_MINUTES=10       # Aggressive cleanup

# Enable auto-scaling features
ENABLE_AUTO_CLEANUP=true
ENABLE_MEMORY_PRESSURE_HANDLING=true
ENABLE_DYNAMIC_RESOURCE_ALLOCATION=true
```

## Dynamic Scaling Configuration

### Auto-Scaling Based on Load

Create `/opt/unity-mcp/scripts/auto-scale.sh`:

```bash
#!/bin/bash
# Auto-scaling script based on current load

CURRENT_CLIENTS=$(curl -s http://localhost:8080/status | jq '.clients.active')
CPU_USAGE=$(top -bn1 | grep "Cpu(s)" | awk '{print $2}' | cut -d'%' -f1)
MEMORY_USAGE=$(free | awk 'NR==2{printf "%.1f", $3*100/$2}')

# Scale up conditions
if [[ $CURRENT_CLIENTS -gt 8 && $CPU_USAGE -gt 70 ]]; then
    # Increase per-client memory limits
    sed -i 's/MAX_MEMORY_PER_CLIENT=.*/MAX_MEMORY_PER_CLIENT=1536/' /etc/unity-mcp/environment
    systemctl reload unity-mcp
fi

# Scale down conditions  
if [[ $CURRENT_CLIENTS -lt 3 && $CPU_USAGE -lt 30 ]]; then
    # Restore normal memory limits
    sed -i 's/MAX_MEMORY_PER_CLIENT=.*/MAX_MEMORY_PER_CLIENT=2048/' /etc/unity-mcp/environment
    systemctl reload unity-mcp
fi
```

### Load-Based Client Limits

Create dynamic limits based on server load:

```python
# Add to multi_client_server.py
def get_dynamic_client_limit(self):
    """Calculate dynamic client limit based on current resources"""
    import psutil
    
    # Get current system resources
    cpu_percent = psutil.cpu_percent(interval=1)
    memory_percent = psutil.virtual_memory().percent
    
    # Base limit from configuration
    base_limit = self.max_clients
    if base_limit == 0:  # Unlimited
        base_limit = 50  # Cap at 50 for calculations
    
    # Reduce limit based on resource usage
    if cpu_percent > 80:
        return max(1, base_limit // 2)
    elif cpu_percent > 60:
        return max(1, int(base_limit * 0.75))
    elif memory_percent > 85:
        return max(1, base_limit // 2)
    elif memory_percent > 70:
        return max(1, int(base_limit * 0.8))
    
    return base_limit
```

## Monitoring Different Client Loads

### Metrics to Track

1. **Per-Client Metrics**:
   - Memory usage per client
   - Command execution time
   - Asset count per client
   - Idle time per client

2. **System Metrics**:
   - Overall CPU usage
   - Memory utilization
   - Disk I/O
   - Network throughput

3. **Unity Metrics**:
   - Unity process memory
   - Scene switch time
   - GameObject creation rate
   - Garbage collection frequency

### Monitoring Commands

```bash
# Check current client distribution
curl http://localhost:8080/status | jq '.clients'

# Monitor system resources
/opt/unity-mcp/scripts/monitor.sh

# Check per-client resource usage
curl http://localhost:8080/metrics | grep unity_mcp_client

# Monitor Unity process specifically
ps aux | grep Unity | grep -v grep
top -p $(pgrep Unity)
```

## Troubleshooting by Scale

### Small Scale (1-5 clients)
- Focus on individual client debugging
- Check client isolation
- Verify Unity license activation

### Medium Scale (6-15 clients)
- Monitor resource contention
- Check scene switching performance
- Verify namespace isolation

### Large Scale (16+ clients)
- Monitor system limits (file descriptors, memory)
- Check network connection limits
- Monitor Unity garbage collection
- Verify auto-cleanup effectiveness

### Performance Issues

#### High Memory Usage
```bash
# Check per-client memory
curl http://localhost:8080/api/clients | jq '.[].memory_usage'

# Reduce per-client limits
sudo sed -i 's/MAX_MEMORY_PER_CLIENT=.*/MAX_MEMORY_PER_CLIENT=1024/' /etc/unity-mcp/environment
sudo systemctl restart unity-mcp
```

#### High CPU Usage
```bash
# Check Unity thread usage
top -H -p $(pgrep Unity)

# Enable aggressive cleanup
curl -X POST http://localhost:8080/api/admin/cleanup-idle
```

#### Connection Limits
```bash
# Check connection count
ss -tuln | grep :8080

# Increase system limits
echo 'net.core.somaxconn = 32768' >> /etc/sysctl.conf
sysctl -p
```

## Example Configurations

### Configuration for Small Team (2-5 developers)
```bash
MAX_CLIENTS=5
MAX_MEMORY_PER_CLIENT=3072
MAX_ASSETS_PER_CLIENT=1500
CLEANUP_IDLE_MINUTES=60  # Longer idle time
```

### Configuration for Workshop/Training (10-20 participants)
```bash
MAX_CLIENTS=20
MAX_MEMORY_PER_CLIENT=1536
MAX_ASSETS_PER_CLIENT=500
CLEANUP_IDLE_MINUTES=15  # Quick cleanup
```

### Configuration for Demo/Exhibition (Variable load)
```bash
MAX_CLIENTS=0            # Unlimited
MAX_MEMORY_PER_CLIENT=1024
MAX_ASSETS_PER_CLIENT=300
CLEANUP_IDLE_MINUTES=5   # Very quick cleanup
```

### Configuration for Production API (High reliability)
```bash
MAX_CLIENTS=10           # Conservative limit
MAX_MEMORY_PER_CLIENT=2048
MAX_ASSETS_PER_CLIENT=1000
CLEANUP_IDLE_MINUTES=30
ENABLE_HEALTH_CHECKS=true
ENABLE_CIRCUIT_BREAKER=true
```

## Advanced Configuration

### Multiple Unity Instances

For very high loads, you can run multiple Unity instances:

```bash
# Create additional Unity instance
cp -r /opt/unity-mcp/projects/shared /opt/unity-mcp/projects/shared2

# Start second instance on different port
UNITY_MCP_PORT=6401 UNITY_PROJECT_PATH=/opt/unity-mcp/projects/shared2 \
  python /opt/unity-mcp/server/multi_client_server.py &
```

### Load Balancing

Use nginx for load balancing between multiple instances:

```nginx
upstream unity_mcp_backend {
    server localhost:8080 weight=3;
    server localhost:8081 weight=3;
    server localhost:8082 weight=2;
}
```

### Database Backend

For persistent client data across restarts:

```python
# Add to client_manager.py
import sqlite3

class PersistentClientManager(ClientIsolationManager):
    def __init__(self, db_path="/opt/unity-mcp/data/clients.db"):
        super().__init__()
        self.db_path = db_path
        self.init_database()
    
    def init_database(self):
        conn = sqlite3.connect(self.db_path)
        conn.execute("""
            CREATE TABLE IF NOT EXISTS clients (
                client_id TEXT PRIMARY KEY,
                project_name TEXT,
                created_at TIMESTAMP,
                last_activity TIMESTAMP,
                config JSON
            )
        """)
        conn.close()
```

This configuration guide provides comprehensive options for scaling your Unity MCP VPS deployment from small teams to large-scale productions with hundreds of concurrent clients.