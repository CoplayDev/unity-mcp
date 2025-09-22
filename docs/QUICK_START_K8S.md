# Unity MCP Kubernetes Quick Start Guide

Get Unity MCP running on Google Kubernetes Engine in 15 minutes.

## Prerequisites
- Google Cloud Project with billing enabled
- Unity license file (`.ulf` format)
- Local tools: `gcloud`, `kubectl`, `docker`

## Step 1: Setup Environment
```bash
# Set your project ID
export PROJECT_ID="your-project-id"
export UNITY_LICENSE_FILE="/path/to/your/Unity_lic.ulf"

# Authenticate with Google Cloud
gcloud auth login
gcloud config set project $PROJECT_ID
gcloud auth configure-docker
```

## Step 2: Create GKE Cluster
```bash
# Run automated cluster setup (takes 5-10 minutes)
./scripts/gke/setup-cluster.sh --project=$PROJECT_ID
```

This creates:
- GKE cluster with auto-scaling
- Dedicated Unity worker node pool
- Network configuration
- Service accounts and RBAC

## Step 3: Build and Push Docker Image
```bash
# Build and push Unity MCP image to Google Container Registry
./scripts/gke/configure-gcr.sh \
  --project=$PROJECT_ID \
  --unity-version=6000.0.3f1
```

## Step 4: Deploy Unity MCP
```bash
# Deploy to Kubernetes
./k8s/scripts/deploy.sh \
  --project=$PROJECT_ID \
  --license=$UNITY_LICENSE_FILE
```

This will:
- Create the `unity-mcp` namespace
- Set up Unity license secret
- Deploy Unity MCP server with auto-scaling
- Configure health checks and monitoring

## Step 5: Verify Deployment
```bash
# Check pod status
kubectl get pods -n unity-mcp

# Test the service
kubectl port-forward -n unity-mcp service/unity-mcp-server 8080:80 &

# Health check
curl http://localhost:8080/health

# Test Unity command
curl -X POST http://localhost:8080/execute-command \
  -H "Content-Type: application/json" \
  -d '{"action":"create_gameobject","params":{"name":"TestObject"}}'
```

## Step 6: Load Testing (Optional)
```bash
# Run scaling tests
./tests/k8s/test-scaling.sh --namespace=unity-mcp

# Or simple load test
./tests/k8s/apache-bench-test.sh --namespace=unity-mcp
```

## What's Created

### GKE Cluster Resources
- **Cluster**: `unity-mcp-cluster` in `us-central1-b`
- **Node Pools**: 
  - `system-pool` (1-3 nodes, e2-medium)
  - `unity-workers` (1-10 nodes, n1-standard-4)
- **Network**: `unity-mcp-network` with custom subnets

### Kubernetes Resources
- **Namespace**: `unity-mcp`
- **Deployment**: `unity-mcp-server` (1-10 replicas)
- **Service**: Load balancer with health checks
- **HPA**: Auto-scaling based on CPU/memory
- **Secrets**: Unity license storage

### Access Points
- **Internal**: `unity-mcp-server.unity-mcp.svc.cluster.local`
- **External**: Static IP (check with `gcloud compute addresses list`)
- **Port Forward**: `kubectl port-forward -n unity-mcp service/unity-mcp-server 8080:80`

## API Endpoints

Once deployed, Unity MCP provides these endpoints:

### Health & Status
```bash
GET /health         # Basic health check
GET /ready          # Kubernetes readiness check
GET /status         # Detailed server status
GET /metrics        # Prometheus metrics
```

### Unity Commands
```bash
POST /execute-command    # Execute Unity operations
GET /command/{id}        # Check command status
```

### Example Commands
```bash
# Create GameObject
curl -X POST http://localhost:8080/execute-command \
  -H "Content-Type: application/json" \
  -d '{
    "action": "create_gameobject",
    "params": {
      "name": "MyObject",
      "position": {"x": 0, "y": 0, "z": 0}
    }
  }'

# Create Scene
curl -X POST http://localhost:8080/execute-command \
  -H "Content-Type: application/json" \
  -d '{
    "action": "create_scene",
    "params": {
      "sceneName": "NewScene"
    }
  }'
```

## Monitoring

### Basic Monitoring
```bash
# Watch pods
kubectl get pods -n unity-mcp -w

# View logs
kubectl logs -n unity-mcp -l app=unity-mcp-server -f

# Check resource usage
kubectl top pods -n unity-mcp
```

### Scaling Behavior
```bash
# Check HPA status
kubectl get hpa -n unity-mcp

# Manual scaling
kubectl scale deployment unity-mcp-server --replicas=3 -n unity-mcp
```

## Troubleshooting

### Common Issues

**Pods not starting?**
```bash
kubectl describe pod -n unity-mcp
kubectl logs -n unity-mcp deployment/unity-mcp-server
```

**Unity license issues?**
```bash
kubectl get secret unity-license -n unity-mcp
kubectl logs -n unity-mcp deployment/unity-mcp-server | grep -i license
```

**Service not responding?**
```bash
kubectl get endpoints -n unity-mcp
kubectl exec -n unity-mcp deployment/unity-mcp-server -- curl localhost:8080/health
```

### Debug Commands
```bash
# Get into a pod
kubectl exec -it -n unity-mcp deployment/unity-mcp-server -- bash

# Check Unity processes
kubectl exec -n unity-mcp deployment/unity-mcp-server -- ps aux | grep Unity

# Test license activation
kubectl exec -n unity-mcp deployment/unity-mcp-server -- /app/unity-license-activator.sh verify
```

## Next Steps

1. **Set up DNS**: Point your domain to the static IP
2. **Configure SSL**: Add SSL certificates for HTTPS
3. **Monitor**: Set up Prometheus/Grafana monitoring
4. **Scale**: Adjust HPA settings based on load
5. **CI/CD**: Integrate with your deployment pipeline

## Clean Up

To remove all resources:
```bash
# Delete Kubernetes resources
kubectl delete namespace unity-mcp

# Delete GKE cluster
gcloud container clusters delete unity-mcp-cluster \
  --zone=us-central1-b --project=$PROJECT_ID

# Delete network resources
gcloud compute networks delete unity-mcp-network --project=$PROJECT_ID

# Delete container images
gcloud container images delete gcr.io/$PROJECT_ID/unity-mcp --force-delete-tags
```

## Cost Estimation

Typical daily costs (us-central1):
- **GKE cluster management**: ~$2.50/day
- **System nodes** (1x e2-medium): ~$1.00/day  
- **Unity workers** (2x n1-standard-4): ~$6.00/day
- **Load balancer**: ~$0.60/day
- **Storage/networking**: ~$0.50/day

**Total**: ~$10.60/day for a production-ready setup

Reduce costs by:
- Using preemptible nodes
- Scaling down during off-hours
- Using smaller instance types for dev/test

## Support

- **Documentation**: See `docs/kubernetes-deployment.md` for detailed guide
- **Issues**: Create GitHub issues for bugs
- **Community**: Join discussions in project repository
- **GKE Support**: Use Google Cloud support for infrastructure issues

This quick start gets you a fully functional Unity MCP deployment on GKE. For production use, review the detailed documentation and implement additional monitoring, security, and backup strategies.