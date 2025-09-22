#!/bin/bash
set -euo pipefail

# GKE Cluster Setup Script for Unity MCP
# Creates and configures a GKE cluster optimized for Unity workloads

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

# Default configuration
PROJECT_ID="${PROJECT_ID:-}"
CLUSTER_NAME="${CLUSTER_NAME:-unity-mcp-cluster}"
REGION="${REGION:-us-central1}"
ZONE="${ZONE:-us-central1-b}"
NETWORK_NAME="${NETWORK_NAME:-unity-mcp-network}"
SUBNET_NAME="${SUBNET_NAME:-unity-mcp-subnet}"
SERVICE_ACCOUNT_NAME="${SERVICE_ACCOUNT_NAME:-unity-mcp-sa}"

# Node pool configuration
UNITY_NODE_POOL="unity-workers"
UNITY_MACHINE_TYPE="n1-standard-4"  # 4 vCPU, 15GB RAM
UNITY_DISK_SIZE="100"
UNITY_MIN_NODES="1"
UNITY_MAX_NODES="10"

# System node pool configuration
SYSTEM_NODE_POOL="system-pool"
SYSTEM_MACHINE_TYPE="e2-medium"  # 1 vCPU, 4GB RAM
SYSTEM_DISK_SIZE="50"
SYSTEM_MIN_NODES="1"
SYSTEM_MAX_NODES="3"

# Logging functions
log() {
    echo -e "${GREEN}[$(date +'%H:%M:%S')] [SETUP]${NC} $1"
}

warn() {
    echo -e "${YELLOW}[$(date +'%H:%M:%S')] [WARN]${NC} $1"
}

error() {
    echo -e "${RED}[$(date +'%H:%M:%S')] [ERROR]${NC} $1" >&2
}

info() {
    echo -e "${BLUE}[$(date +'%H:%M:%S')] [INFO]${NC} $1"
}

# Check prerequisites
check_prerequisites() {
    log "Checking prerequisites..."
    
    # Check gcloud CLI
    if ! command -v gcloud >/dev/null 2>&1; then
        error "gcloud CLI is required but not installed"
        error "Install from: https://cloud.google.com/sdk/docs/install"
        return 1
    fi
    
    # Check kubectl
    if ! command -v kubectl >/dev/null 2>&1; then
        error "kubectl is required but not installed"
        error "Install with: gcloud components install kubectl"
        return 1
    fi
    
    # Check project ID
    if [[ -z "$PROJECT_ID" ]]; then
        PROJECT_ID=$(gcloud config get-value project 2>/dev/null || echo "")
        if [[ -z "$PROJECT_ID" ]]; then
            error "PROJECT_ID must be set or gcloud project must be configured"
            error "Set with: gcloud config set project YOUR_PROJECT_ID"
            return 1
        fi
    fi
    
    log "Using project: $PROJECT_ID"
    
    # Verify authentication
    if ! gcloud auth list --filter=status:ACTIVE --format="value(account)" | grep -q .; then
        error "No active gcloud authentication found"
        error "Authenticate with: gcloud auth login"
        return 1
    fi
    
    # Check APIs
    local required_apis=(
        "container.googleapis.com"
        "compute.googleapis.com"
        "iam.googleapis.com"
        "cloudresourcemanager.googleapis.com"
        "monitoring.googleapis.com"
        "logging.googleapis.com"
    )
    
    for api in "${required_apis[@]}"; do
        if ! gcloud services list --enabled --filter="name:$api" --format="value(name)" | grep -q "$api"; then
            warn "Enabling API: $api"
            gcloud services enable "$api" --project="$PROJECT_ID"
        fi
    done
    
    log "Prerequisites check completed"
}

# Create VPC network
create_network() {
    log "Creating VPC network..."
    
    # Check if network already exists
    if gcloud compute networks describe "$NETWORK_NAME" --project="$PROJECT_ID" >/dev/null 2>&1; then
        info "Network '$NETWORK_NAME' already exists"
    else
        log "Creating network: $NETWORK_NAME"
        gcloud compute networks create "$NETWORK_NAME" \
            --subnet-mode=custom \
            --bgp-routing-mode=regional \
            --project="$PROJECT_ID"
    fi
    
    # Check if subnet already exists
    if gcloud compute networks subnets describe "$SUBNET_NAME" --region="$REGION" --project="$PROJECT_ID" >/dev/null 2>&1; then
        info "Subnet '$SUBNET_NAME' already exists"
    else
        log "Creating subnet: $SUBNET_NAME"
        gcloud compute networks subnets create "$SUBNET_NAME" \
            --network="$NETWORK_NAME" \
            --range="10.0.0.0/16" \
            --secondary-range="pods=10.1.0.0/16,services=10.2.0.0/16" \
            --region="$REGION" \
            --project="$PROJECT_ID"
    fi
}

# Create firewall rules
create_firewall_rules() {
    log "Creating firewall rules..."
    
    # Allow internal traffic
    if ! gcloud compute firewall-rules describe "$NETWORK_NAME-allow-internal" --project="$PROJECT_ID" >/dev/null 2>&1; then
        gcloud compute firewall-rules create "$NETWORK_NAME-allow-internal" \
            --network="$NETWORK_NAME" \
            --allow=tcp,udp,icmp \
            --source-ranges="10.0.0.0/8" \
            --project="$PROJECT_ID"
    fi
    
    # Allow SSH
    if ! gcloud compute firewall-rules describe "$NETWORK_NAME-allow-ssh" --project="$PROJECT_ID" >/dev/null 2>&1; then
        gcloud compute firewall-rules create "$NETWORK_NAME-allow-ssh" \
            --network="$NETWORK_NAME" \
            --allow=tcp:22 \
            --source-ranges="0.0.0.0/0" \
            --project="$PROJECT_ID"
    fi
    
    # Allow Unity MCP ports
    if ! gcloud compute firewall-rules describe "$NETWORK_NAME-allow-unity-mcp" --project="$PROJECT_ID" >/dev/null 2>&1; then
        gcloud compute firewall-rules create "$NETWORK_NAME-allow-unity-mcp" \
            --network="$NETWORK_NAME" \
            --allow=tcp:8080,tcp:6400 \
            --source-ranges="0.0.0.0/0" \
            --target-tags="unity-mcp-server" \
            --project="$PROJECT_ID"
    fi
}

# Create service account
create_service_account() {
    log "Creating service account..."
    
    local sa_email="${SERVICE_ACCOUNT_NAME}@${PROJECT_ID}.iam.gserviceaccount.com"
    
    # Check if service account exists
    if gcloud iam service-accounts describe "$sa_email" --project="$PROJECT_ID" >/dev/null 2>&1; then
        info "Service account '$SERVICE_ACCOUNT_NAME' already exists"
    else
        log "Creating service account: $SERVICE_ACCOUNT_NAME"
        gcloud iam service-accounts create "$SERVICE_ACCOUNT_NAME" \
            --display-name="Unity MCP Service Account" \
            --description="Service account for Unity MCP workloads" \
            --project="$PROJECT_ID"
    fi
    
    # Assign necessary roles
    local roles=(
        "roles/monitoring.metricWriter"
        "roles/logging.logWriter"
        "roles/container.developer"
        "roles/storage.objectViewer"
    )
    
    for role in "${roles[@]}"; do
        gcloud projects add-iam-policy-binding "$PROJECT_ID" \
            --member="serviceAccount:$sa_email" \
            --role="$role" \
            --quiet
    done
    
    log "Service account configured with necessary roles"
}

# Create GKE cluster
create_cluster() {
    log "Creating GKE cluster: $CLUSTER_NAME"
    
    # Check if cluster already exists
    if gcloud container clusters describe "$CLUSTER_NAME" --zone="$ZONE" --project="$PROJECT_ID" >/dev/null 2>&1; then
        info "Cluster '$CLUSTER_NAME' already exists"
        return 0
    fi
    
    log "Creating GKE cluster with optimized configuration..."
    
    gcloud container clusters create "$CLUSTER_NAME" \
        --project="$PROJECT_ID" \
        --zone="$ZONE" \
        --network="$NETWORK_NAME" \
        --subnetwork="$SUBNET_NAME" \
        --cluster-secondary-range-name="pods" \
        --services-secondary-range-name="services" \
        --enable-ip-alias \
        --enable-autorepair \
        --enable-autoupgrade \
        --enable-autoscaling \
        --min-nodes="$SYSTEM_MIN_NODES" \
        --max-nodes="$SYSTEM_MAX_NODES" \
        --machine-type="$SYSTEM_MACHINE_TYPE" \
        --disk-type=pd-standard \
        --disk-size="$SYSTEM_DISK_SIZE" \
        --node-pool="$SYSTEM_NODE_POOL" \
        --service-account="${SERVICE_ACCOUNT_NAME}@${PROJECT_ID}.iam.gserviceaccount.com" \
        --enable-network-policy \
        --enable-cloud-logging \
        --enable-cloud-monitoring \
        --logging=SYSTEM,WORKLOAD \
        --monitoring=SYSTEM \
        --enable-stackdriver-kubernetes \
        --addons=HorizontalPodAutoscaling,HttpLoadBalancing,NetworkPolicy \
        --release-channel=regular \
        --enable-shielded-nodes \
        --shielded-secure-boot \
        --shielded-integrity-monitoring \
        --workload-pool="${PROJECT_ID}.svc.id.goog" \
        --enable-master-authorized-networks \
        --master-authorized-networks="0.0.0.0/0"
    
    log "GKE cluster created successfully"
}

# Create Unity-specific node pool
create_unity_node_pool() {
    log "Creating Unity worker node pool..."
    
    # Check if node pool already exists
    if gcloud container node-pools describe "$UNITY_NODE_POOL" --cluster="$CLUSTER_NAME" --zone="$ZONE" --project="$PROJECT_ID" >/dev/null 2>&1; then
        info "Node pool '$UNITY_NODE_POOL' already exists"
        return 0
    fi
    
    gcloud container node-pools create "$UNITY_NODE_POOL" \
        --cluster="$CLUSTER_NAME" \
        --project="$PROJECT_ID" \
        --zone="$ZONE" \
        --machine-type="$UNITY_MACHINE_TYPE" \
        --disk-type=pd-ssd \
        --disk-size="$UNITY_DISK_SIZE" \
        --image-type=cos_containerd \
        --enable-autorepair \
        --enable-autoupgrade \
        --enable-autoscaling \
        --min-nodes="$UNITY_MIN_NODES" \
        --max-nodes="$UNITY_MAX_NODES" \
        --service-account="${SERVICE_ACCOUNT_NAME}@${PROJECT_ID}.iam.gserviceaccount.com" \
        --node-taints="unity-workload=true:NoSchedule" \
        --node-labels="workload-type=unity,node-pool=$UNITY_NODE_POOL" \
        --tags="unity-mcp-server" \
        --enable-shielded-nodes \
        --shielded-secure-boot \
        --shielded-integrity-monitoring \
        --workload-metadata=GKE_METADATA
    
    log "Unity node pool created successfully"
}

# Configure kubectl
configure_kubectl() {
    log "Configuring kubectl..."
    
    gcloud container clusters get-credentials "$CLUSTER_NAME" \
        --zone="$ZONE" \
        --project="$PROJECT_ID"
    
    # Verify connection
    if kubectl cluster-info >/dev/null 2>&1; then
        log "kubectl configured successfully"
        kubectl get nodes
    else
        error "Failed to configure kubectl"
        return 1
    fi
}

# Install cluster add-ons
install_addons() {
    log "Installing cluster add-ons..."
    
    # Install metrics server (if not already installed)
    if ! kubectl get deployment metrics-server -n kube-system >/dev/null 2>&1; then
        log "Installing metrics server..."
        kubectl apply -f https://github.com/kubernetes-sigs/metrics-server/releases/latest/download/components.yaml
    fi
    
    # Wait for metrics server to be ready
    kubectl wait --for=condition=available --timeout=300s deployment/metrics-server -n kube-system || warn "Metrics server may not be fully ready"
}

# Create namespace and RBAC
setup_namespace() {
    log "Setting up Unity MCP namespace..."
    
    # Create namespace
    kubectl create namespace unity-mcp --dry-run=client -o yaml | kubectl apply -f -
    
    # Label namespace
    kubectl label namespace unity-mcp name=unity-mcp environment=production platform=gke --overwrite
    
    # Enable Workload Identity for the namespace
    kubectl annotate serviceaccount --namespace unity-mcp default \
        iam.gke.io/gcp-service-account="${SERVICE_ACCOUNT_NAME}@${PROJECT_ID}.iam.gserviceaccount.com" \
        --overwrite
        
    log "Namespace setup completed"
}

# Reserve static IP
reserve_static_ip() {
    log "Reserving static IP for ingress..."
    
    if gcloud compute addresses describe unity-mcp-ip --global --project="$PROJECT_ID" >/dev/null 2>&1; then
        info "Static IP 'unity-mcp-ip' already exists"
    else
        gcloud compute addresses create unity-mcp-ip \
            --global \
            --project="$PROJECT_ID"
        log "Static IP reserved"
    fi
    
    local static_ip=$(gcloud compute addresses describe unity-mcp-ip --global --project="$PROJECT_ID" --format="value(address)")
    info "Static IP address: $static_ip"
    info "Configure your DNS to point to this IP address"
}

# Generate deployment summary
generate_summary() {
    local summary_file="/tmp/unity-mcp-gke-setup-summary.txt"
    
    log "Generating deployment summary..."
    
    {
        echo "Unity MCP GKE Cluster Setup Summary"
        echo "Generated: $(date)"
        echo ""
        echo "Project: $PROJECT_ID"
        echo "Cluster: $CLUSTER_NAME"
        echo "Zone: $ZONE"
        echo "Network: $NETWORK_NAME"
        echo "Subnet: $SUBNET_NAME"
        echo ""
        echo "Node Pools:"
        echo "  System Pool: $SYSTEM_NODE_POOL ($SYSTEM_MACHINE_TYPE)"
        echo "  Unity Pool: $UNITY_NODE_POOL ($UNITY_MACHINE_TYPE)"
        echo ""
        echo "Service Account: ${SERVICE_ACCOUNT_NAME}@${PROJECT_ID}.iam.gserviceaccount.com"
        echo ""
        echo "Cluster Information:"
        kubectl cluster-info 2>/dev/null || echo "kubectl not configured"
        echo ""
        echo "Static IP:"
        gcloud compute addresses describe unity-mcp-ip --global --project="$PROJECT_ID" --format="value(address)" 2>/dev/null || echo "Not created"
        echo ""
        echo "Next Steps:"
        echo "1. Update k8s/overlays/gke/kustomization.yaml with PROJECT_ID: $PROJECT_ID"
        echo "2. Deploy Unity MCP: kubectl apply -k k8s/overlays/gke/"
        echo "3. Configure DNS to point to the static IP address"
        echo "4. Upload Unity license to Kubernetes secret"
    } > "$summary_file"
    
    log "Setup summary saved to: $summary_file"
    cat "$summary_file"
}

# Main execution
main() {
    log "Starting GKE cluster setup for Unity MCP"
    log "Project: ${PROJECT_ID:-auto-detect}"
    log "Cluster: $CLUSTER_NAME"
    log "Region/Zone: $REGION/$ZONE"
    
    # Execute setup steps
    if ! check_prerequisites; then
        error "Prerequisites check failed"
        exit 1
    fi
    
    create_network
    create_firewall_rules
    create_service_account
    create_cluster
    create_unity_node_pool
    configure_kubectl
    install_addons
    setup_namespace
    reserve_static_ip
    
    log "GKE cluster setup completed successfully!"
    generate_summary
}

# Handle command line arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --project)
            PROJECT_ID="$2"
            shift 2
            ;;
        --cluster)
            CLUSTER_NAME="$2"
            shift 2
            ;;
        --region)
            REGION="$2"
            shift 2
            ;;
        --zone)
            ZONE="$2"
            shift 2
            ;;
        --network)
            NETWORK_NAME="$2"
            shift 2
            ;;
        --help|-h)
            echo "GKE Cluster Setup for Unity MCP"
            echo "Usage: $0 [OPTIONS]"
            echo ""
            echo "Options:"
            echo "  --project      GCP Project ID"
            echo "  --cluster      Cluster name (default: unity-mcp-cluster)"
            echo "  --region       GCP region (default: us-central1)"
            echo "  --zone         GCP zone (default: us-central1-b)"
            echo "  --network      Network name (default: unity-mcp-network)"
            echo "  -h, --help     Show this help message"
            echo ""
            echo "Environment Variables:"
            echo "  PROJECT_ID     GCP Project ID (can be auto-detected)"
            exit 0
            ;;
        *)
            error "Unknown option: $1"
            exit 1
            ;;
    esac
done

# Run main function
main "$@"