# Unity License Management in Kubernetes

This guide covers managing Unity licenses in Kubernetes environments, specifically for the Unity MCP headless server deployment.

## Table of Contents
1. [License Types and Requirements](#license-types-and-requirements)
2. [Kubernetes Secret Management](#kubernetes-secret-management)
3. [License Activation Methods](#license-activation-methods)
4. [Automated License Management](#automated-license-management)
5. [Multi-Tenant Considerations](#multi-tenant-considerations)
6. [Troubleshooting](#troubleshooting)
7. [Best Practices](#best-practices)

## License Types and Requirements

### Unity License Types
- **Personal License**: Free, limited to personal use, not suitable for production
- **Pro License**: Commercial license, suitable for production use
- **Enterprise License**: Volume licensing for large organizations

### Requirements for Headless Operation
Unity headless operation in containers requires:
- Valid Unity license file (`.ulf` format) or activation credentials
- Proper license activation within the container
- License return mechanism for clean shutdown (optional but recommended)

### License File Generation
To generate a license request file:

```bash
# On a machine with Unity installed
Unity -batchmode -quit -createManualActivationFile

# This creates Unity_v[VERSION].alf file
# Upload this file to https://license.unity3d.com/manual
# Download the resulting .ulf file
```

## Kubernetes Secret Management

### Method 1: License File Secret (Recommended)
Store the Unity license file directly in a Kubernetes secret:

```bash
# Create secret from license file
kubectl create secret generic unity-license \
  --from-file=license.ulf=/path/to/Unity_lic.ulf \
  --namespace=unity-mcp

# Verify secret
kubectl get secret unity-license -n unity-mcp -o yaml
```

### Method 2: Base64 Encoded Content
Store the license content as base64 encoded string:

```bash
# Encode license file
LICENSE_CONTENT=$(base64 -w 0 /path/to/Unity_lic.ulf)

# Create secret
kubectl create secret generic unity-license \
  --from-literal=license.ulf="$LICENSE_CONTENT" \
  --namespace=unity-mcp

# Or use environment variable
kubectl create secret generic unity-license \
  --from-literal=UNITY_LICENSE_CONTENT="$LICENSE_CONTENT" \
  --namespace=unity-mcp
```

### Method 3: Unity Hub Credentials
Store Unity account credentials for automatic activation:

```bash
# Create secret with Unity credentials
kubectl create secret generic unity-license \
  --from-literal=UNITY_USERNAME=your-email@example.com \
  --from-literal=UNITY_PASSWORD=your-password \
  --from-literal=UNITY_SERIAL=your-serial-key \
  --namespace=unity-mcp
```

### Secret Mounting in Pods
The deployment automatically mounts the license secret:

```yaml
# In deployment.yaml
volumes:
- name: unity-license-secret
  secret:
    secretName: unity-license
    optional: true

volumeMounts:
- name: unity-license-secret
  mountPath: /var/secrets/unity
  readOnly: true
```

## License Activation Methods

The Unity MCP container supports multiple license activation methods, processed in order of preference:

### Priority Order
1. **Kubernetes Secret Mount** (`/var/secrets/unity/license.ulf`)
2. **License File Path** (`UNITY_LICENSE_FILE` environment variable)
3. **Unity Hub Credentials** (`UNITY_USERNAME`, `UNITY_PASSWORD`, `UNITY_SERIAL`)
4. **Base64 Content** (`UNITY_LICENSE_CONTENT` environment variable)
5. **Personal License Fallback** (existing license in user directory)

### Activation Process
The license activation is handled by the init container:

```yaml
# In deployment.yaml
initContainers:
- name: unity-license-setup
  image: gcr.io/PROJECT_ID/unity-mcp:production
  command: ["/app/unity-license-activator.sh", "activate"]
  env:
  - name: UNITY_PATH
    value: "/opt/unity/editors/6000.0.3f1/Editor/Unity"
  envFrom:
  - secretRef:
      name: unity-license
      optional: true
  volumeMounts:
  - name: unity-home
    mountPath: /home/unity
  - name: unity-license-secret
    mountPath: /var/secrets/unity
    readOnly: true
```

### Manual License Activation
For troubleshooting, you can activate licenses manually:

```bash
# Execute into a running pod
kubectl exec -it -n unity-mcp deployment/unity-mcp-server -- bash

# Run license activator
/app/unity-license-activator.sh activate

# Check license status
/app/unity-license-activator.sh info

# Verify Unity can use the license
/app/unity-license-activator.sh verify
```

## Automated License Management

### License Rotation
For production environments, implement license rotation:

```bash
#!/bin/bash
# license-rotation.sh

# Update license secret
kubectl create secret generic unity-license-new \
  --from-file=license.ulf=/path/to/new/Unity_lic.ulf \
  --namespace=unity-mcp

# Update deployment to use new secret
kubectl patch deployment unity-mcp-server -n unity-mcp \
  -p '{"spec":{"template":{"spec":{"volumes":[{"name":"unity-license-secret","secret":{"secretName":"unity-license-new"}}]}}}}'

# Wait for rollout
kubectl rollout status deployment/unity-mcp-server -n unity-mcp

# Delete old secret
kubectl delete secret unity-license -n unity-mcp

# Rename new secret
kubectl create secret generic unity-license \
  --from-file=license.ulf=/path/to/new/Unity_lic.ulf \
  --namespace=unity-mcp
kubectl delete secret unity-license-new -n unity-mcp
```

### License Monitoring
Monitor license usage and expiration:

```bash
# Check license status in pods
kubectl exec -n unity-mcp deployment/unity-mcp-server -- \
  /app/unity-license-activator.sh info

# Check for license-related errors in logs
kubectl logs -n unity-mcp -l app=unity-mcp-server | grep -i license
```

### Automated License Return
Implement graceful license return on pod termination:

```yaml
# In deployment.yaml
containers:
- name: unity-mcp-server
  lifecycle:
    preStop:
      exec:
        command: ["/app/unity-license-activator.sh", "return"]
```

## Multi-Tenant Considerations

### Separate Licenses per Tenant
For multi-tenant deployments, use separate namespaces and licenses:

```bash
# Create tenant-specific namespace
kubectl create namespace unity-mcp-tenant-a

# Create tenant-specific license secret
kubectl create secret generic unity-license \
  --from-file=license.ulf=/path/to/tenant-a/Unity_lic.ulf \
  --namespace=unity-mcp-tenant-a

# Deploy with tenant-specific configuration
helm install unity-mcp-tenant-a ./helm-chart \
  --namespace=unity-mcp-tenant-a \
  --set license.secretName=unity-license
```

### License Pooling
For shared license pools, implement license checkout/checkin:

```yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: license-pool-config
  namespace: unity-mcp
data:
  pool_size: "10"
  checkout_timeout: "3600"  # 1 hour
  licenses: |
    license1.ulf
    license2.ulf
    license3.ulf
```

### Resource Quotas
Limit resource usage per tenant to prevent license exhaustion:

```yaml
apiVersion: v1
kind: ResourceQuota
metadata:
  name: unity-mcp-quota
  namespace: unity-mcp-tenant-a
spec:
  hard:
    requests.cpu: "10"
    requests.memory: 20Gi
    pods: "5"  # Limit number of Unity instances
```

## Troubleshooting

### Common Issues

#### 1. License Activation Failed
```bash
# Check license secret exists
kubectl get secret unity-license -n unity-mcp

# Check secret content
kubectl get secret unity-license -n unity-mcp -o jsonpath='{.data.license\.ulf}' | base64 -d | head -5

# Check pod logs
kubectl logs -n unity-mcp deployment/unity-mcp-server -c unity-license-setup
```

#### 2. Unity License Not Found
```bash
# Check if license is properly mounted
kubectl exec -n unity-mcp deployment/unity-mcp-server -- ls -la /var/secrets/unity/

# Check Unity home directory
kubectl exec -n unity-mcp deployment/unity-mcp-server -- ls -la /home/unity/.config/unity3d/
```

#### 3. License Server Connection Issues
```bash
# Test network connectivity from pod
kubectl exec -n unity-mcp deployment/unity-mcp-server -- \
  curl -v https://license.unity3d.com

# Check firewall rules
gcloud compute firewall-rules list --filter="direction:EGRESS"
```

#### 4. License Expired or Invalid
```bash
# Check license validity
kubectl exec -n unity-mcp deployment/unity-mcp-server -- \
  /app/unity-license-activator.sh verify

# Check Unity logs for license errors
kubectl logs -n unity-mcp deployment/unity-mcp-server | grep -A5 -B5 "license"
```

### Debug Commands

#### License Status Check
```bash
# Get detailed license information
kubectl exec -n unity-mcp deployment/unity-mcp-server -- \
  /app/unity-license-activator.sh info

# Check Unity activation status
kubectl exec -n unity-mcp deployment/unity-mcp-server -- \
  cat /tmp/unity-logs/license-activation.log
```

#### Secret Debugging
```bash
# Decode and inspect license file
kubectl get secret unity-license -n unity-mcp -o jsonpath='{.data.license\.ulf}' | \
  base64 -d > /tmp/extracted-license.ulf
file /tmp/extracted-license.ulf
head -20 /tmp/extracted-license.ulf
```

#### Network Debugging
```bash
# Test DNS resolution
kubectl exec -n unity-mcp deployment/unity-mcp-server -- \
  nslookup license.unity3d.com

# Test HTTPS connectivity
kubectl exec -n unity-mcp deployment/unity-mcp-server -- \
  openssl s_client -connect license.unity3d.com:443 -servername license.unity3d.com
```

## Best Practices

### Security
- **Never commit license files to version control**
- **Use Kubernetes secrets for license storage**
- **Implement RBAC to restrict secret access**
- **Rotate licenses regularly**
- **Monitor license usage and audit access**

### High Availability
- **Store backup licenses in separate secrets**
- **Implement automatic failover mechanisms**
- **Use multiple license servers if available**
- **Monitor license server connectivity**

### Cost Optimization
- **Return licenses on pod termination**
- **Implement license pooling for shared environments**
- **Monitor license utilization metrics**
- **Use smaller instance types for license-only operations**

### Monitoring
- **Set up alerts for license failures**
- **Track license usage metrics**
- **Monitor license expiration dates**
- **Log all license operations for auditing**

### Example Monitoring ConfigMap
```yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: license-monitoring
  namespace: unity-mcp
data:
  check-script.sh: |
    #!/bin/bash
    # License health check script
    LICENSE_STATUS=$(/app/unity-license-activator.sh info)
    if echo "$LICENSE_STATUS" | grep -q "ERROR"; then
      echo "CRITICAL: Unity license issue detected"
      exit 2
    fi
    echo "OK: Unity license is valid"
    exit 0
```

### Environment-Specific Configurations

#### Development
```yaml
# Use personal licenses or shared dev licenses
env:
- name: CI_MODE
  value: "true"  # Skip Unity license requirement
```

#### Staging
```yaml
# Use production-like licenses but with monitoring
env:
- name: LICENSE_MONITORING_ENABLED
  value: "true"
```

#### Production
```yaml
# Use dedicated production licenses with full monitoring
env:
- name: LICENSE_RETURN_ON_SHUTDOWN
  value: "true"
- name: LICENSE_MONITORING_ENABLED
  value: "true"
- name: LICENSE_FAILURE_ALERT
  value: "true"
```

## Integration with External Systems

### Unity Cloud Build
If using Unity Cloud Build alongside Kubernetes deployment:

```bash
# Set up shared license pool
kubectl create configmap unity-cloud-licenses \
  --from-file=licenses=/path/to/license/directory \
  --namespace=unity-mcp
```

### License Management APIs
For programmatic license management:

```python
# Example Python script for license management
import base64
import subprocess
from kubernetes import client, config

def update_unity_license(namespace, license_file_path):
    config.load_incluster_config()  # or load_kube_config() for local testing
    v1 = client.CoreV1Api()
    
    # Read license file
    with open(license_file_path, 'rb') as f:
        license_content = base64.b64encode(f.read()).decode('utf-8')
    
    # Create secret
    secret = client.V1Secret(
        metadata=client.V1ObjectMeta(name="unity-license"),
        data={"license.ulf": license_content}
    )
    
    # Update or create secret
    try:
        v1.replace_namespaced_secret("unity-license", namespace, secret)
        print("License updated successfully")
    except client.exceptions.ApiException as e:
        if e.status == 404:
            v1.create_namespaced_secret(namespace, secret)
            print("License created successfully")
        else:
            raise
```

This comprehensive guide should help you manage Unity licenses effectively in your Kubernetes environment. For additional support, refer to the Unity documentation and Kubernetes best practices guides.