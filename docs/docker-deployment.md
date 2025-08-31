# Unity MCP Docker Deployment Guide

This guide covers the complete Docker deployment process for Unity MCP, including production deployment, development setup, and troubleshooting.

## 📋 Table of Contents

- [Quick Start](#quick-start)
- [Production Deployment](#production-deployment)
- [Development Setup](#development-setup)
- [Configuration](#configuration)
- [Monitoring & Troubleshooting](#monitoring--troubleshooting)
- [Security](#security)
- [Performance Optimization](#performance-optimization)

## 🚀 Quick Start

### Prerequisites

- Docker 20.10+ and Docker Compose 2.0+
- Unity License (Personal, Plus, or Pro)
- 4GB+ available RAM
- 10GB+ available disk space

### Basic Deployment

```bash
# Clone the repository
git clone https://github.com/justinpbarnett/unity-mcp.git
cd unity-mcp

# Build the production image
docker build -f docker/Dockerfile.production -t unity-mcp:latest .

# Run with docker-compose
docker-compose -f docker-compose.production.yml up -d

# Check health
curl http://localhost:8080/health
```

## 🏭 Production Deployment

### Building Production Image

```bash
# Build optimized production image
docker build \
  -f docker/Dockerfile.production \
  -t unity-mcp:production \
  --target production \
  --build-arg UNITY_VERSION=2022.3.45f1 \
  --build-arg UNITY_CHANGESET=63b2b3067b8e \
  .

# Verify image size (should be < 2GB)
docker image ls unity-mcp:production
```

### Docker Compose Production Setup

```yaml
# docker-compose.production.yml
version: '3.8'
services:
  unity-mcp:
    image: unity-mcp:production
    ports:
      - "8080:8080"
      - "6400:6400"
    environment:
      - UNITY_LICENSE_FILE=/secrets/unity.ulf
      - LOG_LEVEL=INFO
    volumes:
      - ./unity-projects:/app/unity-projects:ro
      - ./builds:/app/builds
      - unity-license:/secrets:ro
    restart: unless-stopped
    healthcheck:
      test: ["/app/healthcheck.sh"]
      interval: 30s
      timeout: 10s
      retries: 3
      start_period: 120s

volumes:
  unity-license:
    external: true
```

### Kubernetes Deployment

For Kubernetes deployment (Milestone 3), basic manifests:

```yaml
# k8s/deployment.yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: unity-mcp-headless
spec:
  replicas: 2
  selector:
    matchLabels:
      app: unity-mcp-headless
  template:
    metadata:
      labels:
        app: unity-mcp-headless
    spec:
      containers:
      - name: unity-mcp
        image: unity-mcp:production
        ports:
        - containerPort: 8080
        - containerPort: 6400
        env:
        - name: LOG_LEVEL
          value: "INFO"
        - name: UNITY_LICENSE_FILE
          value: "/secrets/unity.ulf"
        volumeMounts:
        - name: unity-license
          mountPath: /secrets
          readOnly: true
        livenessProbe:
          httpGet:
            path: /health
            port: 8080
          initialDelaySeconds: 120
          periodSeconds: 30
        readinessProbe:
          httpGet:
            path: /health
            port: 8080
          initialDelaySeconds: 30
          periodSeconds: 10
      volumes:
      - name: unity-license
        secret:
          secretName: unity-license
```

## 🔧 Development Setup

### Development Image

```bash
# Build development image with debugging tools
docker build -f docker/Dockerfile.dev -t unity-mcp:dev .

# Run with development overrides
docker-compose -f docker-compose.yml -f docker-compose.dev.yml up -d
```

### Hot Reload Development

```bash
# Mount source code for live editing
docker run -d \
  --name unity-mcp-dev \
  -p 8080:8080 \
  -p 6400:6400 \
  -v $(pwd)/UnityMcpBridge/UnityMcpServer~/src:/app/server \
  -v $(pwd)/tests:/app/tests \
  -e LOG_LEVEL=DEBUG \
  unity-mcp:dev

# Access container shell
docker exec -it unity-mcp-dev /bin/bash
```

## ⚙️ Configuration

### Environment Variables

| Variable | Description | Default | Required |
|----------|-------------|---------|----------|
| `UNITY_LICENSE_FILE` | Path to Unity license file | | ✅ |
| `UNITY_USERNAME` | Unity account username | | * |
| `UNITY_PASSWORD` | Unity account password | | * |
| `UNITY_SERIAL` | Unity license serial | | * |
| `UNITY_PROJECT_PATH` | Path to Unity project | | |
| `HTTP_PORT` | HTTP API port | 8080 | |
| `UNITY_MCP_PORT` | Unity MCP bridge port | 6400 | |
| `LOG_LEVEL` | Logging level | INFO | |
| `MAX_CONCURRENT_COMMANDS` | Max concurrent commands | 5 | |

*Either `UNITY_LICENSE_FILE` or `UNITY_USERNAME`/`UNITY_PASSWORD`/`UNITY_SERIAL` required.

### Unity License Setup

#### Option 1: License File

```bash
# Create license volume
docker volume create unity-license

# Copy license file to volume
docker run --rm -v unity-license:/data -v $(pwd):/src alpine cp /src/Unity_v2022.x.ulf /data/

# Use in container
docker run -d \
  -v unity-license:/secrets:ro \
  -e UNITY_LICENSE_FILE=/secrets/Unity_v2022.x.ulf \
  unity-mcp:production
```

#### Option 2: Credentials

```bash
# Set environment variables
export UNITY_USERNAME="your-unity-email"
export UNITY_PASSWORD="your-unity-password"
export UNITY_SERIAL="your-license-serial"

# Run container
docker run -d \
  -e UNITY_USERNAME \
  -e UNITY_PASSWORD \
  -e UNITY_SERIAL \
  unity-mcp:production
```

### Network Configuration

#### Port Mapping

- **8080**: HTTP API endpoint
- **6400**: Unity MCP bridge communication

#### Security Groups / Firewall

```bash
# Allow HTTP API access
ufw allow 8080/tcp

# Allow Unity MCP bridge (internal only)
ufw allow from 172.0.0.0/8 to any port 6400
```

### Volume Mounts

| Host Path | Container Path | Purpose |
|-----------|----------------|---------|
| `./unity-projects` | `/app/unity-projects` | Unity projects (read-only) |
| `./builds` | `/app/builds` | Build outputs |
| `./logs` | `/tmp/unity-logs` | Log files |
| Unity license | `/secrets` | License files |

## 📊 Monitoring & Troubleshooting

### Health Checks

```bash
# Basic health check
curl http://localhost:8080/health

# Detailed status
curl http://localhost:8080/status

# Expected response
{
  "status": "healthy",
  "unity_connected": true,
  "active_commands": 0,
  "total_commands": 150,
  "timestamp": "2025-01-15T10:30:00Z"
}
```

### Log Analysis

```bash
# Container logs
docker logs unity-mcp-headless

# Follow logs in real-time
docker logs -f unity-mcp-headless

# Unity-specific logs
docker exec unity-mcp-headless cat /tmp/unity-logs/unity-editor.log

# HTTP server logs
docker exec unity-mcp-headless cat /tmp/unity-logs/unity-mcp.log
```

### Common Issues

#### Container Won't Start

```bash
# Check image exists
docker image ls unity-mcp

# Check container status
docker ps -a

# Inspect failed container
docker logs <container-id>

# Common fixes:
# 1. Unity license not properly mounted
# 2. Insufficient disk space
# 3. Port already in use
```

#### Unity License Issues

```bash
# Check license file exists
docker exec unity-mcp-headless ls -la /secrets/

# Check Unity license activation logs
docker exec unity-mcp-headless cat /tmp/unity-logs/license-activation.log

# Manual license activation
docker exec -it unity-mcp-headless \
  /opt/unity/editors/2022.3.45f1/Editor/Unity \
  -batchmode -quit -username USER -password PASS -serial SERIAL
```

#### Performance Issues

```bash
# Check resource usage
docker stats unity-mcp-headless

# Check disk usage
docker exec unity-mcp-headless df -h

# Check memory usage
docker exec unity-mcp-headless free -m
```

### Metrics Collection

#### Prometheus Integration

```yaml
# prometheus.yml
global:
  scrape_interval: 15s

scrape_configs:
  - job_name: 'unity-mcp'
    static_configs:
      - targets: ['unity-mcp-headless:8080']
    metrics_path: '/metrics'
    scrape_interval: 30s
```

#### Custom Metrics

The Unity MCP server exposes metrics at `/status`:

- `total_commands`: Total commands processed
- `successful_commands`: Successful command count
- `failed_commands`: Failed command count
- `active_commands`: Currently active commands
- `avg_execution_time`: Average command execution time

## 🔒 Security

### Security Hardening

The production Docker image includes:

- **Non-root user**: Runs as `unity` user (UID 1000)
- **Read-only filesystem**: Root filesystem is read-only
- **Minimal attack surface**: Only required packages installed
- **Security scanning**: Regular Trivy scans for vulnerabilities

### Security Scanning

```bash
# Run security scan
./docker/scripts/security-scan.sh unity-mcp:production

# Manual Trivy scan
trivy image --severity HIGH,CRITICAL unity-mcp:production
```

### Runtime Security

```bash
# Run with security options
docker run -d \
  --name unity-mcp-secure \
  --read-only \
  --tmpfs /tmp:size=1G \
  --tmpfs /home/unity/.config:size=100M \
  --security-opt no-new-privileges \
  --cap-drop ALL \
  unity-mcp:production
```

### Network Security

```bash
# Create custom network
docker network create unity-network --driver bridge

# Run with custom network
docker run -d \
  --network unity-network \
  --name unity-mcp-secure \
  unity-mcp:production
```

## ⚡ Performance Optimization

### Resource Limits

```yaml
services:
  unity-mcp:
    image: unity-mcp:production
    deploy:
      resources:
        limits:
          cpus: '2.0'
          memory: 4G
        reservations:
          cpus: '0.5'
          memory: 1G
```

### Build Optimization

```bash
# Multi-stage build with cache
docker build \
  --cache-from unity-mcp:latest \
  --target production \
  -t unity-mcp:optimized \
  .

# Use BuildKit for faster builds
DOCKER_BUILDKIT=1 docker build \
  -f docker/Dockerfile.production \
  -t unity-mcp:buildkit \
  .
```

### Storage Optimization

```bash
# Use volumes for persistent data
docker volume create unity-builds
docker volume create unity-logs

# Clean up unused images
docker image prune -a

# Clean up build cache
docker builder prune
```

## 🧪 Testing

### Running Tests

```bash
# Run comprehensive test suite
python3 tests/docker/run_docker_tests.py

# Run specific tests
python3 tests/docker/test_container_size.py
python3 tests/docker/test_container_startup.py

# Run with verbose output
python3 tests/docker/run_docker_tests.py --verbose
```

### Load Testing

```bash
# Basic load test
python3 load_test.py --url http://localhost:8080 --concurrent 5

# Extended load test
python3 load_test.py --url http://localhost:8080 --concurrent 10 --duration 300
```

### Integration Testing

```bash
# Test with sample Unity project
docker run -d \
  --name unity-mcp-test \
  -p 8080:8080 \
  -v $(pwd)/test-project:/app/unity-projects/test:ro \
  -e UNITY_PROJECT_PATH=/app/unity-projects/test \
  unity-mcp:production

# Execute test commands
curl -X POST http://localhost:8080/execute-command \
  -H "Content-Type: application/json" \
  -d '{"action":"ping","params":{}}'
```

## 📈 Scaling

### Horizontal Scaling

```yaml
# docker-compose.scale.yml
version: '3.8'
services:
  unity-mcp:
    image: unity-mcp:production
    deploy:
      replicas: 3
  
  nginx:
    image: nginx:alpine
    ports:
      - "80:80"
    depends_on:
      - unity-mcp
    volumes:
      - ./nginx.conf:/etc/nginx/nginx.conf
```

### Load Balancing

```nginx
# nginx.conf
upstream unity_mcp {
    server unity-mcp_1:8080;
    server unity-mcp_2:8080;
    server unity-mcp_3:8080;
}

server {
    listen 80;
    location / {
        proxy_pass http://unity_mcp;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
    }
}
```

## 📚 Additional Resources

- [Unity Command Line Documentation](https://docs.unity3d.com/Manual/CommandLineArguments.html)
- [Docker Best Practices](https://docs.docker.com/develop/dev-best-practices/)
- [Unity Licensing Guide](https://docs.unity3d.com/Manual/ManagingYourUnityLicense.html)
- [Kubernetes Deployment Guide](docs/kubernetes-deployment.md) *(Coming in Milestone 3)*

## 🆘 Support

If you encounter issues:

1. Check the [troubleshooting section](#common-issues)
2. Review container logs: `docker logs <container-name>`
3. Run health checks: `curl http://localhost:8080/health`
4. Check [GitHub Issues](https://github.com/justinpbarnett/unity-mcp/issues)
5. Join the [Discord community](https://discord.gg/y4p8KfzrN4)

---

*This guide covers Milestone 2: Dockerize Unity for Containerized Deployment. For advanced Kubernetes scaling (Milestone 3+), see the Kubernetes deployment documentation.*