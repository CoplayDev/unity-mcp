#!/usr/bin/env bash
# Setup script for local test environment
# Run this once to prepare your environment for local testing

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
cd "$PROJECT_ROOT"

# Colors
BLUE='\033[0;34m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m'

log_info() { echo -e "${BLUE}[INFO]${NC} $*"; }
log_success() { echo -e "${GREEN}[SUCCESS]${NC} $*"; }
log_warning() { echo -e "${YELLOW}[WARNING]${NC} $*"; }
log_error() { echo -e "${RED}[ERROR]${NC} $*"; }

echo "========================================"
echo "Unity MCP Local Test Environment Setup"
echo "========================================"
echo ""

# Step 1: Check prerequisites
log_info "Checking prerequisites..."

MISSING_DEPS=0

# Check Python/uv
if command -v uv &> /dev/null; then
  log_success "uv found: $(uv --version)"
else
  log_error "uv not found. Install from: https://docs.astral.sh/uv/"
  MISSING_DEPS=1
fi

# Check jq
if command -v jq &> /dev/null; then
  log_success "jq found: $(jq --version)"
else
  log_warning "jq not found (optional but recommended)"
  log_info "Install: brew install jq"
fi

# Check claude CLI
if command -v claude &> /dev/null; then
  log_success "claude CLI found: $(which claude) - $(claude --version)"
else
  log_error "claude CLI not found"
  log_error ""
  log_error "The local test runner requires the Claude CLI."
  log_error ""
  log_error "Installation: https://claude.ai/download"
  log_error ""
  log_error "After installation, restart your shell and run setup again."
  MISSING_DEPS=1
fi

# Check Unity (optional - will prompt later)
if [ -d "/Applications/Unity/Hub/Editor" ] || [ -d "/Applications/Unity" ]; then
  log_success "Unity installation detected"
else
  log_warning "Unity not found in standard location"
  log_info "Make sure Unity 2021.3.x is installed"
fi

echo ""

if [ $MISSING_DEPS -eq 1 ]; then
  log_error "Missing required dependencies. Please install them first."
  exit 1
fi

# Step 2: Install MCP server dependencies
log_info "Installing MCP server dependencies..."

if [ -f "Server/pyproject.toml" ]; then
  cd Server
  if uv venv 2>&1 | grep -q "already exists"; then
    log_info "Virtual environment already exists"
  else
    log_info "Creating virtual environment..."
    uv venv
  fi

  log_info "Installing MCP server..."
  uv pip install -e .
  cd "$PROJECT_ROOT"
  log_success "MCP server installed"
elif [ -f "Server/requirements.txt" ]; then
  cd Server
  uv venv
  uv pip install -r requirements.txt
  cd "$PROJECT_ROOT"
  log_success "MCP server dependencies installed"
else
  log_error "No Server/pyproject.toml or Server/requirements.txt found"
  exit 1
fi

# Step 3: Make scripts executable
log_info "Making scripts executable..."
chmod +x "$SCRIPT_DIR"/*.sh
log_success "Scripts are now executable"

# Step 4: Create directories
log_info "Creating directories..."
mkdir -p reports reports/_snapshots reports/_staging
mkdir -p .claude/local
mkdir -p .unity-mcp
log_success "Directories created"

# Step 5: Verify Unity project
log_info "Verifying Unity project..."
if [ -d "TestProjects/UnityMCPTests" ]; then
  log_success "Test project found"
else
  log_error "Test project not found at TestProjects/UnityMCPTests"
  exit 1
fi

# Step 6: Instructions
echo ""
log_success "Setup complete! ✓"
echo ""
log_info "========================================"
log_info "Next steps:"
log_info "========================================"
echo ""
echo "1. Open Unity and load the project:"
echo "   TestProjects/UnityMCPTests"
echo ""
echo "2. Ensure the MCP bridge auto-starts in Unity"
echo "   (should happen automatically when Unity opens)"
echo ""
echo "3. Run the test suite:"
echo "   ./scripts/local-test/run-nl-suite-local.sh"
echo ""
echo "Or run a single test quickly:"
echo "   ./scripts/local-test/quick-test.sh NL-0"
echo ""
log_info "See scripts/local-test/README.md for more details"
echo ""
