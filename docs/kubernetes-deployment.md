# Unity MCP Kubernetes Deployment Guide

This guide covers deploying Unity MCP to Google Kubernetes Engine (GKE) for production use.

## Table of Contents
1. [Prerequisites](#prerequisites)
2. [GKE Cluster Setup](#gke-cluster-setup)
3. [Docker Image Build](#docker-image-build)
4. [Unity License Configuration](#unity-license-configuration)
5. [Kubernetes Deployment](#kubernetes-deployment)
6. [Load Testing and Scaling](#load-testing-and-scaling)
7. [Monitoring and Troubleshooting](#monitoring-and-troubleshooting)
8. [Security Best Practices](#security-best-practices)

## Prerequisites

### Required Tools
- `gcloud` CLI (Google Cloud SDK)
- `kubectl` (Kubernetes client)
- `docker` (Container runtime)
- Unity license file (`.ulf` format)

### Google Cloud Project Setup
```bash
# Set your project ID
export PROJECT_ID="your-project-id"
gcloud config set project $PROJECT_ID

# Authenticate
gcloud auth login
gcloud auth configure-docker
```

### Required APIs
The setup scripts will enable these automatically, but you can enable them manually:
```bash
gcloud services enable container.googleapis.com
gcloud services enable compute.googleapis.com
gcloud services enable containerregistry.googleapis.com
gcloud services enable cloudbuild.googleapis.com
```

## GKE Cluster Setup

### Automated Cluster Setup
Use the provided script for complete cluster setup:

```bash
# Run the automated setup
./scripts/gke/setup-cluster.sh --project=$PROJECT_ID

# Or with custom settings
./scripts/gke/setup-cluster.sh \
  --project=$PROJECT_ID \
  --cluster=unity-mcp-prod \
  --region=us-central1 \
  --zone=us-central1-b
```

### Manual Cluster Setup
If you prefer manual setup:

```bash
# Create VPC network
gcloud compute networks create unity-mcp-network \
  --subnet-mode=custom \
  --project=$PROJECT_ID

# Create subnet
gcloud compute networks subnets create unity-mcp-subnet \
  --network=unity-mcp-network \
  --range=10.0.0.0/16 \
  --secondary-range=pods=10.1.0.0/16,services=10.2.0.0/16 \
  --region=us-central1 \
  --project=$PROJECT_ID

# Create GKE cluster
gcloud container clusters create unity-mcp-cluster \
  --project=$PROJECT_ID \
  --zone=us-central1-b \
  --network=unity-mcp-network \
  --subnetwork=unity-mcp-subnet \
  --enable-ip-alias \
  --cluster-secondary-range-name=pods \
  --services-secondary-range-name=services \
  --enable-autoscaling \
  --min-nodes=1 \
  --max-nodes=3 \
  --machine-type=e2-medium \
  --enable-network-policy \
  --enable-cloud-logging \
  --enable-cloud-monitoring
```

### Configure kubectl
```bash
gcloud container clusters get-credentials unity-mcp-cluster \
  --zone=us-central1-b \
  --project=$PROJECT_ID
```

## Docker Image Build

### Automated Image Build and Push
```bash
# Build and push to Google Container Registry
./scripts/gke/configure-gcr.sh \
  --project=$PROJECT_ID \
  --unity-version=6000.0.3f1

# Verify image
docker images gcr.io/$PROJECT_ID/unity-mcp
```

### Manual Build
```bash
# Build production image
docker build -f docker/Dockerfile.production \
  -t gcr.io/$PROJECT_ID/unity-mcp:production .

# Push to registry
docker push gcr.io/$PROJECT_ID/unity-mcp:production
```

## Unity License Configuration

### Option 1: License File (Recommended)
```bash
# Create Kubernetes secret from license file
kubectl create secret generic unity-license \
  --from-file=license.ulf=/path/to/your/Unity_lic.ulf \
  -n unity-mcp
```

### Option 2: License Content (Base64)
```bash
# Encode license file to base64
LICENSE_CONTENT=$(base64 -w 0 /path/to/your/Unity_lic.ulf)

# Create secret
kubectl create secret generic unity-license \
  --from-literal=license.ulf="$LICENSE_CONTENT" \
  -n unity-mcp
```

### Option 3: Unity Hub Credentials
```bash
# Create secret with Unity credentials
kubectl create secret generic unity-license \
  --from-literal=username=your-unity-email \
  --from-literal=password=your-unity-password \
  --from-literal=serial=your-unity-serial \
  -n unity-mcp
```

## Kubernetes Deployment

### Update Configuration
First, update the project ID in the Kustomization file:

```bash
# Update project ID in GKE overlay
sed -i "s/YOUR_PROJECT_ID/$PROJECT_ID/g" k8s/overlays/gke/kustomization.yaml
```

### Deploy with Automated Script
```bash
# Deploy to GKE
./k8s/scripts/deploy.sh \
  --project=$PROJECT_ID \
  --license=/path/to/Unity_lic.ulf

# Or dry run first
./k8s/scripts/deploy.sh --dry-run
```

### Manual Deployment
```bash
# Create namespace
kubectl create namespace unity-mcp

# Deploy manifests
kubectl apply -k k8s/overlays/gke/

# Wait for deployment
kubectl wait --for=condition=available \
  --timeout=600s deployment/unity-mcp-server \
  -n unity-mcp
```

### Verify Deployment
```bash
# Check pod status
kubectl get pods -n unity-mcp

# Check service status
kubectl get service -n unity-mcp

# Test health endpoint
kubectl port-forward -n unity-mcp service/unity-mcp-server 8080:80 &
curl http://localhost:8080/health
```

## Load Testing and Scaling

### Manual Scaling Test
```bash
# Scale up
kubectl scale deployment unity-mcp-server --replicas=3 -n unity-mcp

# Wait for pods to be ready
kubectl wait --for=condition=available \
  deployment/unity-mcp-server -n unity-mcp

# Check HPA status
kubectl get hpa -n unity-mcp
```

### Automated Scaling Tests
```bash
# Run comprehensive scaling tests
./tests/k8s/test-scaling.sh --namespace=unity-mcp

# Run load tests with k6 (if installed)
cd tests/k8s
k6 run --duration=5m --vus=10 k6-load-test.js

# Or use Apache Bench
./tests/k8s/apache-bench-test.sh --namespace=unity-mcp
```

### Load Test Results
Expected performance metrics:
- **Pod startup time**: < 3 minutes
- **Health endpoint**: < 1s response time
- **Command execution**: 5-30s depending on complexity
- **Concurrent users**: 10-20 per pod
- **Auto-scaling**: Triggers at 70% CPU usage

## Monitoring and Troubleshooting

### Basic Monitoring
```bash
# Watch pods
kubectl get pods -n unity-mcp -w

# View logs
kubectl logs -n unity-mcp -l app=unity-mcp-server -f

# Check resource usage
kubectl top pods -n unity-mcp
kubectl top nodes
```

### Health Checks
```bash
# Port forward and test endpoints
kubectl port-forward -n unity-mcp service/unity-mcp-server 8080:80 &

# Health check
curl http://localhost:8080/health

# Readiness check
curl http://localhost:8080/ready

# Execute test command
curl -X POST http://localhost:8080/execute-command \
  -H "Content-Type: application/json" \
  -d '{"action":"create_gameobject","params":{"name":"TestObject"}}'
```

### Common Issues and Solutions

#### 1. Pod Stuck in Pending State
```bash
# Check node resources
kubectl describe pod -n unity-mcp

# Check node selector and taints
kubectl get nodes -l workload-type=unity
```

#### 2. Unity License Issues
```bash
# Check license secret
kubectl get secret unity-license -n unity-mcp -o yaml

# Check pod logs for license errors
kubectl logs -n unity-mcp deployment/unity-mcp-server | grep -i license
```

#### 3. HPA Not Scaling
```bash
# Check metrics server
kubectl get deployment metrics-server -n kube-system

# Check HPA status
kubectl describe hpa unity-mcp-server-hpa -n unity-mcp

# Check resource requests/limits
kubectl get deployment unity-mcp-server -n unity-mcp -o yaml | grep -A10 resources
```

#### 4. Service Not Responding
```bash
# Check service endpoints
kubectl get endpoints -n unity-mcp

# Check network policies
kubectl get networkpolicy -n unity-mcp

# Test direct pod connection
kubectl exec -it -n unity-mcp deployment/unity-mcp-server -- curl localhost:8080/health
```

### Performance Tuning

#### Resource Optimization
```yaml
# Adjust in k8s/base/deployment.yaml
resources:
  requests:
    cpu: 1500m      # Increase for better performance
    memory: 3Gi     # Unity requires significant memory
  limits:
    cpu: 3000m      # Allow burst capacity
    memory: 6Gi     # Prevent OOM kills
```

#### HPA Configuration
```yaml
# Adjust in k8s/base/hpa.yaml
spec:
  minReplicas: 2    # Increase for high availability
  maxReplicas: 20   # Scale for high load
  targetCPUUtilizationPercentage: 60  # Lower threshold for faster scaling
```

### Advanced Monitoring with Prometheus

If you have Prometheus installed:

```bash
# Check metrics endpoint
curl http://localhost:8080/metrics

# Example Prometheus queries
unity_mcp_requests_total
unity_mcp_uptime_seconds
rate(unity_mcp_requests_total[5m])
```

## Security Best Practices

### Network Security
- Use Network Policies to restrict traffic
- Configure firewall rules for specific ports only
- Use private GKE clusters for production

### Container Security
- Run containers as non-root user (already configured)
- Use read-only root filesystems where possible
- Enable Pod Security Policies or Pod Security Standards

### Secrets Management
- Store Unity licenses in Kubernetes secrets
- Use Google Secret Manager for production
- Rotate secrets regularly
- Never commit secrets to version control

### RBAC
- Use minimal required permissions
- Create service accounts for specific workloads
- Review and audit RBAC policies regularly

### Example Network Policy
```yaml
apiVersion: networking.k8s.io/v1
kind: NetworkPolicy
metadata:
  name: unity-mcp-network-policy
  namespace: unity-mcp
spec:
  podSelector:
    matchLabels:
      app: unity-mcp-server
  policyTypes:
  - Ingress
  - Egress
  ingress:
  - from:
    - namespaceSelector:
        matchLabels:
          name: kube-system
    ports:
    - protocol: TCP
      port: 8080
```

## Next Steps

1. **Set up CI/CD pipeline** for automated deployments
2. **Configure monitoring** with Prometheus and Grafana
3. **Implement backup strategies** for persistent data
4. **Set up disaster recovery** procedures
5. **Performance testing** under expected production load

## Support and Resources

- [Unity MCP Documentation](../README.md)
- [GKE Documentation](https://cloud.google.com/kubernetes-engine/docs)
- [Kubernetes Documentation](https://kubernetes.io/docs/)
- [Unity Container Documentation](https://docs.unity3d.com/Manual/CommandLineArguments.html)

For issues and questions, please check the project issues on GitHub or create a new issue with detailed logs and configuration.