# Docker Deployment Guide

This guide covers deploying Unity MCP using Docker containers for scalable headless Unity servers.

## Quick Start

### Single Container

```bash
# Build the image
docker build -t unity-mcp .

# Run headless Unity server
docker run -p 8080:8080 unity-mcp:latest
```

### Docker Compose

```bash
# Start complete stack
docker-compose up -d

# Scale Unity servers
docker-compose up -d --scale unity-server=3
```

## Configuration

### Environment Variables

Configure your deployment with environment variables:

```bash
# Unity settings
UNITY_LICENSE_FILE=/opt/unity/license.ulf
UNITY_USERNAME=your-unity-username
UNITY_PASSWORD=your-unity-password

# MCP settings
MCP_PORT=8080
MAX_CONCURRENT_CLIENTS=5

# Build service settings  
BUILD_SERVICE_API_KEY=your-api-key
MAX_CONCURRENT_BUILDS=3
```

### Volume Mounts

Mount important directories:

```yaml
volumes:
  - ./unity-projects:/opt/unity/projects
  - ./unity-license:/opt/unity/license
  - ./builds:/opt/unity/builds
  - ./logs:/opt/unity/logs
```

## Production Deployment

### Docker Compose Production

```yaml
version: '3.8'
services:
  unity-mcp:
    image: unity-mcp:latest
    ports:
      - "8080:8080"
    environment:
      - BUILD_SERVICE_API_KEY=${API_KEY}
      - MAX_CONCURRENT_BUILDS=5
    volumes:
      - unity-builds:/opt/unity/builds
      - unity-logs:/opt/unity/logs
    restart: unless-stopped
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/health"]
      interval: 30s
      timeout: 10s
      retries: 3
      
  nginx:
    image: nginx:alpine
    ports:
      - "80:80"
      - "443:443"
    volumes:
      - ./nginx.conf:/etc/nginx/nginx.conf
      - ./ssl:/etc/ssl
    depends_on:
      - unity-mcp
    restart: unless-stopped

volumes:
  unity-builds:
  unity-logs:
```

### Kubernetes Deployment

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: unity-mcp
spec:
  replicas: 3
  selector:
    matchLabels:
      app: unity-mcp
  template:
    metadata:
      labels:
        app: unity-mcp
    spec:
      containers:
      - name: unity-mcp
        image: unity-mcp:latest
        ports:
        - containerPort: 8080
        env:
        - name: BUILD_SERVICE_API_KEY
          valueFrom:
            secretKeyRef:
              name: unity-mcp-secrets
              key: api-key
        resources:
          requests:
            memory: "4Gi"
            cpu: "2"
          limits:
            memory: "8Gi"
            cpu: "4"
        livenessProbe:
          httpGet:
            path: /health
            port: 8080
          initialDelaySeconds: 30
          periodSeconds: 10
```

## Unity License Setup

### For Docker

Mount your Unity license file:

```bash
docker run -v /path/to/license.ulf:/opt/unity/license.ulf unity-mcp:latest
```

### License Server (Enterprise)

Configure Unity license server:

```bash
# License server environment
UNITY_LICENSE_SERVER=http://your-license-server:8080
UNITY_LICENSE_SERVER_TOKEN=your-server-token
```

## Monitoring

### Health Checks

Built-in health check endpoint:

```bash
curl http://localhost:8080/health
```

### Logging

Access container logs:

```bash
# Follow logs
docker logs -f unity-mcp-container

# Export logs
docker logs unity-mcp-container > unity-mcp.log
```

### Metrics

Monitor container metrics:

```bash
# Container stats
docker stats unity-mcp-container

# Detailed metrics
curl http://localhost:8080/metrics
```

## Scaling

### Horizontal Scaling

Scale Unity MCP containers:

```bash
# Docker Compose
docker-compose up -d --scale unity-mcp=5

# Kubernetes
kubectl scale deployment unity-mcp --replicas=5
```

### Load Balancing

Configure nginx for load balancing:

```nginx
upstream unity_mcp {
    server unity-mcp-1:8080;
    server unity-mcp-2:8080;
    server unity-mcp-3:8080;
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

## Troubleshooting

### Common Issues

**Container won't start:**
- Check Unity license configuration
- Verify volume mounts and permissions
- Review container logs for errors

**High memory usage:**
- Reduce MAX_CONCURRENT_BUILDS
- Optimize Unity project settings
- Monitor build queue length

**Build failures:**
- Check Unity Editor logs in container
- Verify asset accessibility
- Monitor disk space usage

### Performance Optimization

**Container resources:**
```yaml
resources:
  requests:
    memory: "4Gi"
    cpu: "2"
  limits:
    memory: "8Gi" 
    cpu: "4"
```

**Unity settings:**
```bash
# Optimize for headless
UNITY_BATCH_MODE=1
UNITY_QUIT_AFTER_BUILD=1
UNITY_NO_GRAPHICS=1
```

## Security

### Image Security

Scan images for vulnerabilities:

```bash
# Using Docker Scout
docker scout cves unity-mcp:latest

# Using Trivy  
trivy image unity-mcp:latest
```

### Network Security

Configure secure networking:

```yaml
networks:
  unity-network:
    driver: bridge
    ipam:
      config:
        - subnet: 172.20.0.0/16
```

### Secrets Management

Use Docker secrets or Kubernetes secrets:

```bash
# Docker secrets
echo "your-api-key" | docker secret create unity-api-key -

# Kubernetes secrets
kubectl create secret generic unity-mcp-secrets \
  --from-literal=api-key=your-api-key
```

## Next Steps

- [Production Deployment](production-deployment.md) - VPS deployment for cost optimization
- [Unity License Setup](unity-license.md) - Detailed Unity licensing guide
- [API Reference](api-reference.md) - Complete API documentation