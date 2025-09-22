# Unity MCP Build Service - Production Deployment Guide

## 🚀 Production-Ready Unity Build Service

This guide covers the production deployment of the Unity MCP Build Service, which provides a complete API for building Unity games from user-generated assets and deploying them as playable WebGL games.

## 📋 Prerequisites

### System Requirements
- **Google Cloud Platform account** with billing enabled
- **Domain name** with DNS control
- **Unity license** (Pro or Enterprise)
- **Local machine** with gcloud CLI installed

### Minimum Server Specifications
- **CPU**: 8 vCPUs (16 vCPUs recommended for high load)
- **RAM**: 32GB (64GB recommended for high load)
- **Storage**: 200GB SSD (500GB+ for production)
- **OS**: Ubuntu 22.04 LTS

## 🔧 Quick Production Deployment

```bash
# 1. Clone the repository
git clone <repository-url>
cd unity-mcp

# 2. Generate a secure API key
API_KEY=$(openssl rand -hex 32)

# 3. Deploy to production
./scripts/vps/production-deploy.sh \
    your-server-name \
    us-central1-a \
    yourdomain.com \
    $API_KEY
```

## 📊 API Specification Compliance

Our implementation fully complies with the provided Unity Build Service API specification:

### ✅ Endpoints Implemented
- `POST /build` - Create new builds from asset URLs
- `GET /build/{build_id}/status` - Track build progress
- `PUT /build/{build_id}/stop` - Cancel running builds

### ✅ Status Codes
- `200 OK` - Successful operations
- `403 FORBIDDEN` - Authentication failures
- `502 INTERNAL SERVER ERROR` - System errors (per spec)

### ✅ Response Formats
All responses match the exact format specified in the API documentation.

## 🔒 Security Features

### Authentication
- **Bearer token authentication** for all endpoints
- **Strong API key requirement** (32+ characters)
- **Rate limiting** (configurable, default: 10 requests/minute)

### Input Validation
- **URL validation** with scheme and domain checking
- **Asset size limits** (default: 100MB per asset)
- **Asset count limits** (default: 100 assets per build)
- **Path traversal prevention**
- **XSS and injection protection**

### Network Security
- **UFW firewall** with minimal open ports
- **Fail2ban** for intrusion prevention
- **SSL/TLS hardening** with modern ciphers
- **Security headers** (HSTS, CSP, etc.)

## 📈 Production Features

### Performance
- **Concurrent build processing** (configurable limit)
- **Asset download optimization** with size validation
- **Resource monitoring** and automatic cleanup
- **Build queue management** with position tracking

### Monitoring
- **Health check endpoint** (`/health`)
- **Metrics endpoint** (`/metrics`)
- **Comprehensive logging** with rotation
- **Automated alerting** for critical issues
- **Performance monitoring** with thresholds

### Reliability
- **Automatic service restart** on failure
- **Build timeout handling**
- **Graceful error recovery**
- **Asset download retry logic**
- **Database cleanup automation**

### Maintenance
- **Automated backups** (daily)
- **Log rotation** (30 days retention)
- **Old build cleanup** (configurable retention)
- **System optimization** scripts

## 🧪 Testing & Quality Assurance

### Automated Test Suite
```bash
# Run API compliance tests
python -m pytest tests/test_api_compliance.py -v

# Run production readiness tests
python -m pytest tests/test_build_service_production.py -v
```

### Test Coverage
- ✅ **API specification compliance**
- ✅ **Authentication and authorization**
- ✅ **Input validation and sanitization**
- ✅ **Error handling scenarios**
- ✅ **Security vulnerability tests**
- ✅ **Performance and load testing**
- ✅ **Build process integration**

### Manual Testing
```bash
# Test build creation
curl -X POST https://yourdomain.com/build \
  -H "Authorization: Bearer your-api-key" \
  -H "Content-Type: application/json" \
  -d '{
    "user_id": "test-user",
    "game_id": "test-game",
    "game_name": "My Test Game",
    "game_type": "platformer",
    "asset_set": "v1",
    "assets": [
      ["https://via.placeholder.com/64x64.png"],
      ["https://via.placeholder.com/800x600.jpg"]
    ]
  }'

# Check build status
curl -H "Authorization: Bearer your-api-key" \
  https://yourdomain.com/build/{build_id}/status

# Stop build
curl -X PUT -H "Authorization: Bearer your-api-key" \
  https://yourdomain.com/build/{build_id}/stop
```

## 📊 Monitoring & Alerting

### Health Checks
The service provides comprehensive health monitoring:

```bash
# Check service health
curl https://yourdomain.com/health

# Response format:
{
  "status": "healthy|warning|critical",
  "uptime_seconds": 86400,
  "build_stats": {
    "total_builds": 150,
    "completed_builds": 140,
    "failed_builds": 8,
    "active_builds": 2,
    "success_rate": 93.3
  },
  "issues": []
}
```

### Metrics
Production metrics available at `/metrics` endpoint (restricted access):

```json
{
  "builds_total": 150,
  "builds_completed": 140,
  "builds_failed": 8,
  "builds_active": 2,
  "builds_queued": 3,
  "success_rate_percent": 93.3,
  "builds_per_hour": 12.5
}
```

### Log Files
- **Service logs**: `/opt/unity-mcp/logs/build-service.log`
- **Security audit**: `/opt/unity-mcp/logs/security-audit.log`
- **Build process**: `/opt/unity-mcp/logs/build-process.log`
- **Alerts**: `/opt/unity-mcp/logs/alerts.log`

## ⚙️ Configuration

### Environment Variables
```bash
# Required Production Settings
BUILD_SERVICE_API_KEY=your-secure-32-char-api-key
BASE_GAME_URL=https://yourdomain.com/games
DOMAIN=yourdomain.com

# Resource Limits
MAX_CONCURRENT_BUILDS=5
MAX_ASSETS_PER_BUILD=50
MAX_ASSET_SIZE_MB=100
MAX_ASSETS_PER_SLOT=20

# Timeouts
ASSET_DOWNLOAD_TIMEOUT=300
BUILD_TIMEOUT=1800

# Security
ENABLE_SECURITY_AUDIT=true
ALLOW_PRIVATE_URLS=false
RATE_LIMIT_REQUESTS=20
RATE_LIMIT_WINDOW=60

# Maintenance
BUILD_RETENTION_HOURS=48
```

### Scaling Recommendations

| Concurrent Builds | Server Specs | Expected Throughput |
|------------------|--------------|-------------------|
| 1-3 builds | 8 vCPUs, 32GB RAM | 5-10 builds/hour |
| 4-6 builds | 16 vCPUs, 64GB RAM | 15-25 builds/hour |
| 7+ builds | 32 vCPUs, 128GB RAM | 30+ builds/hour |

## 🔄 Build Process Flow

1. **Request Validation** - Authenticate and validate all input data
2. **Asset Download** - Download assets with size and security validation
3. **Unity Project Creation** - Create isolated Unity project with assets
4. **Game Compilation** - Build to WebGL with error handling
5. **Deployment** - Deploy to web-accessible location
6. **Cleanup** - Clean up temporary files and Unity client

## 🚨 Troubleshooting

### Common Issues

**Service won't start:**
```bash
# Check logs
sudo journalctl -u unity-mcp -f

# Check configuration
sudo -u unity cat /opt/unity-mcp/.env

# Test configuration
cd /opt/unity-mcp && sudo -u unity python -c "from production_config import production_config; production_config.validate()"
```

**High error rate:**
```bash
# Check build logs
tail -f /opt/unity-mcp/logs/build-process.log

# Check Unity logs
tail -f /opt/unity-mcp/logs/unity.log

# Monitor resource usage
htop
```

**SSL certificate issues:**
```bash
# Renew Let's Encrypt certificate
sudo certbot renew --nginx

# Test SSL configuration
sudo nginx -t
```

### Performance Tuning

**Optimize for high throughput:**
```bash
# Increase concurrent builds
export MAX_CONCURRENT_BUILDS=8

# Optimize asset download
export ASSET_DOWNLOAD_TIMEOUT=180

# Reduce build retention
export BUILD_RETENTION_HOURS=24
```

**Optimize for reliability:**
```bash
# Reduce concurrent builds
export MAX_CONCURRENT_BUILDS=3

# Increase timeouts
export BUILD_TIMEOUT=3600

# Enable all security features
export ENABLE_SECURITY_AUDIT=true
```

## 🔐 Security Checklist

- [ ] **API key is 32+ characters and unique**
- [ ] **Domain has valid SSL certificate**
- [ ] **Firewall is configured with minimal ports**
- [ ] **Regular security updates are applied**
- [ ] **Log monitoring is active**
- [ ] **Backups are tested and verified**
- [ ] **Rate limiting is configured**
- [ ] **Security headers are enabled**

## 📞 Support & Maintenance

### Regular Maintenance Tasks
- **Weekly**: Review alert logs and system performance
- **Monthly**: Test backup restoration and security updates
- **Quarterly**: Review and optimize resource usage
- **Annually**: Security audit and configuration review

### Backup & Recovery
```bash
# Manual backup
sudo -u unity /opt/unity-mcp/bin/backup.sh

# Restore from backup
tar -xzf /opt/unity-mcp/backups/unity-mcp-backup-YYYYMMDD_HHMMSS-config.tar.gz -C /
```

### Updating the Service
```bash
# Update code
git pull origin main

# Restart service
sudo systemctl restart unity-mcp

# Verify health
curl https://yourdomain.com/health
```

## 💰 Cost Optimization

### Current Architecture Savings
- **Before**: Kubernetes with 5 Unity pods = $10,700/month
- **After**: Single VPS with multi-client Unity = $240/month
- **Savings**: 96% cost reduction while supporting more clients

### Optimization Tips
1. **Use committed use discounts** for long-term deployments
2. **Monitor and clean up** old builds regularly
3. **Optimize asset caching** to reduce bandwidth
4. **Use regional persistent disks** for cost savings

---

## 🎯 Production Deployment Summary

This Unity MCP Build Service provides a **production-ready**, **secure**, and **scalable** solution for automated Unity game building. With comprehensive API compliance, robust security features, monitoring, and testing, it's ready for enterprise deployment.

**Key Benefits:**
- ✅ **96% cost savings** vs Kubernetes approach
- ✅ **Full API specification compliance**
- ✅ **Enterprise-grade security**
- ✅ **Comprehensive monitoring**
- ✅ **Automated testing and validation**
- ✅ **Production-ready deployment scripts**

Ready to deploy! 🚀