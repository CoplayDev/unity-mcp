#!/usr/bin/env bash
# Local test runner for Unity MCP NL/T Suite
# Runs tests against a locally running Unity Editor (much faster than CI)

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
cd "$PROJECT_ROOT"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

log_info() { echo -e "${BLUE}[INFO]${NC} $*"; }
log_success() { echo -e "${GREEN}[SUCCESS]${NC} $*"; }
log_warning() { echo -e "${YELLOW}[WARNING]${NC} $*"; }
log_error() { echo -e "${RED}[ERROR]${NC} $*"; }

# Parse arguments
RUN_NL=true
RUN_T=true
RUN_GO=false
SKIP_SETUP=false
KEEP_REPORTS=false

while [[ $# -gt 0 ]]; do
  case $1 in
    --nl-only)
      RUN_T=false
      RUN_GO=false
      shift
      ;;
    --t-only)
      RUN_NL=false
      RUN_GO=false
      shift
      ;;
    --go-only)
      RUN_NL=false
      RUN_T=false
      RUN_GO=true
      shift
      ;;
    --with-go)
      RUN_GO=true
      shift
      ;;
    --skip-setup)
      SKIP_SETUP=true
      shift
      ;;
    --keep-reports)
      KEEP_REPORTS=true
      shift
      ;;
    --help)
      cat <<EOF
Usage: $0 [OPTIONS]

Run the Unity MCP NL/T test suite locally.

Options:
  --nl-only       Run only NL pass (tests NL-0 to NL-4)
  --t-only        Run only T pass (tests T-A to T-J)
  --go-only       Run only GameObject pass (tests GO-0 to GO-10)
  --with-go       Include GameObject tests with NL+T suite
  --skip-setup    Skip Unity readiness check and MCP config generation
  --keep-reports  Keep existing reports (don't clean before run)
  --help          Show this help message

Examples:
  $0                    # Run NL+T suite
  $0 --with-go          # Run NL+T+GO suite
  $0 --nl-only          # Run only NL tests
  $0 --go-only          # Run only GameObject tests
  $0 --t-only --skip-setup  # Run only T tests, skip setup

Prerequisites:
  1. Unity Editor must be open with TestProjects/UnityMCPTests
  2. MCP bridge should be running in Unity (auto-starts)
  3. Python environment with uv installed
EOF
      exit 0
      ;;
    *)
      log_error "Unknown option: $1"
      echo "Use --help for usage information"
      exit 1
      ;;
  esac
done

# Step 1: Check Unity is running
if [ "$SKIP_SETUP" = false ]; then
  log_info "Checking Unity Editor status..."

  # First, try to detect HTTP mode (Unity's default for local GUI)
  HTTP_URL="http://localhost:8080"
  USE_HTTP_MODE=false

  log_info "Checking for Unity HTTP bridge at $HTTP_URL..."
  if command -v curl &> /dev/null && curl -s --max-time 2 "$HTTP_URL/health" > /dev/null 2>&1; then
    log_success "Unity HTTP bridge detected at $HTTP_URL"
    USE_HTTP_MODE=true
    export UNITY_MCP_TRANSPORT=http
    export UNITY_MCP_HTTP_URL="$HTTP_URL"
  elif command -v curl &> /dev/null && curl -s --max-time 2 "$HTTP_URL" > /dev/null 2>&1; then
    log_success "Unity HTTP bridge detected at $HTTP_URL (no health endpoint)"
    USE_HTTP_MODE=true
    export UNITY_MCP_TRANSPORT=http
    export UNITY_MCP_HTTP_URL="$HTTP_URL"
  else
    log_info "HTTP bridge not responding, checking for stdio mode..."
  fi

  # If HTTP mode works, skip stdio checks
  if [ "$USE_HTTP_MODE" = true ]; then
    log_info "Using HTTP transport mode"
    # Initialize variables for HTTP mode (no status file needed)
    STATUS_FILE=""
    PROJECT_NAME=""
    UNITY_PORT=""
  else
    # Fall back to stdio mode: check for status files
    # 1. Project-local .unity-mcp/ (if UNITY_MCP_STATUS_DIR was set when Unity started)
    # 2. User home ~/.unity-mcp/ (default when Unity runs without env var)
    log_info "Checking for stdio mode status files..."
    MCP_STATUS_DIRS=(
      "$PROJECT_ROOT/.unity-mcp"
      "$HOME/.unity-mcp"
    )

    FOUND_STATUS_DIR=""
    for dir in "${MCP_STATUS_DIRS[@]}"; do
      if [ -d "$dir" ] && [ -n "$(ls -A "$dir"/unity-mcp-status-*.json 2>/dev/null)" ]; then
        FOUND_STATUS_DIR="$dir"
        break
      fi
    done

    if [ -z "$FOUND_STATUS_DIR" ]; then
      log_warning "Unity MCP status not found in:"
      for dir in "${MCP_STATUS_DIRS[@]}"; do
        log_info "  - $dir"
      done
      echo ""
      log_info "Please ensure:"
      log_info "  1. Unity Editor is open"
      log_info "  2. Project 'TestProjects/UnityMCPTests' is loaded"
      log_info "  3. MCP bridge is running (should auto-start)"
      log_info ""
      log_info "Note: Unity defaults to HTTP mode. Either:"
      log_info "  - Let the script use HTTP mode (automatic), or"
      log_info "  - Switch Unity to stdio mode in MCP settings"
      echo ""
      read -p "Press Enter when Unity is ready, or Ctrl+C to abort... "

      # Wait a bit for status file to appear
      for i in {1..10}; do
        for dir in "${MCP_STATUS_DIRS[@]}"; do
          if [ -d "$dir" ] && [ -n "$(ls -A "$dir"/unity-mcp-status-*.json 2>/dev/null)" ]; then
            FOUND_STATUS_DIR="$dir"
            break 2
          fi
        done
        sleep 1
      done
    fi

    # Verify status file exists now
    if [ -z "$FOUND_STATUS_DIR" ]; then
      log_error "Neither HTTP nor stdio mode detected!"
      log_error "HTTP bridge not responding at: $HTTP_URL"
      log_error "Stdio status files not found in:"
      for dir in "${MCP_STATUS_DIRS[@]}"; do
        log_error "  - $dir"
      done
      log_error ""
      log_error "Make sure Unity Editor with MCP bridge is running."
      exit 1
    fi

    log_success "Found Unity MCP status in: $FOUND_STATUS_DIR"

    # Use the found directory for status files
    shopt -s nullglob
    STATUS_FILES=("$FOUND_STATUS_DIR"/unity-mcp-status-*.json)
    if [ ${#STATUS_FILES[@]} -eq 0 ]; then
      log_error "Status directory exists but no status files found."
      exit 1
    fi

    # Find a status file with a responsive port
    ACTIVE_STATUS_FILE=""
    UNITY_PORT=""
    PROJECT_NAME=""

    log_info "Checking for active Unity instance..."

    for status_file in "${STATUS_FILES[@]}"; do
      port=$(jq -r '.unity_port // empty' "$status_file" 2>/dev/null || echo "")
      proj=$(jq -r '.project_name // empty' "$status_file" 2>/dev/null || echo "")
      heartbeat=$(jq -r '.last_heartbeat // empty' "$status_file" 2>/dev/null || echo "")

      if [ -n "$port" ]; then
        # Test if this port is responsive
        if timeout 2 bash -c "exec 3<>/dev/tcp/127.0.0.1/$port" 2>/dev/null; then
          log_success "Found active Unity instance: $proj on port $port"
          ACTIVE_STATUS_FILE="$status_file"
          UNITY_PORT="$port"
          PROJECT_NAME="$proj"
          break
        else
          log_info "  Skipping $proj (port $port not responding)"
        fi
      fi
    done

    if [ -z "$ACTIVE_STATUS_FILE" ]; then
      log_error "No active Unity MCP bridge found!"
      log_error "Found status files, but none are responding."
      log_error ""
      log_error "Please:"
      log_error "  1. Open Unity Editor"
      log_error "  2. Load project: TestProjects/UnityMCPTests"
      log_error "  3. Wait for MCP bridge to start (check Unity console)"
      log_error ""
      log_error "Or clean old status files: rm ~/.unity-mcp/unity-mcp-status-*.json"
      exit 1
    fi

    STATUS_FILE="$ACTIVE_STATUS_FILE"
    log_success "Using Unity instance: $PROJECT_NAME on port $UNITY_PORT"
  fi
fi

# Step 2: Set up environment
log_info "Setting up environment..."

export PYTHONUNBUFFERED=1
export MCP_LOG_LEVEL=debug
export UNITY_PROJECT_ROOT="$PROJECT_ROOT/TestProjects/UnityMCPTests"
export UNITY_MCP_STATUS_DIR="${FOUND_STATUS_DIR:-$PROJECT_ROOT/.unity-mcp}"
export UNITY_MCP_HOST=127.0.0.1

log_info "Using status directory: $UNITY_MCP_STATUS_DIR"

# Extract default instance name from the active status file (only in stdio mode)
if [ -n "${STATUS_FILE:-}" ] && [ -f "$STATUS_FILE" ]; then
  HASH_PART=$(basename "$STATUS_FILE" .json | sed 's/unity-mcp-status-//')
  # PROJECT_NAME already set from the active check above, but re-read to be safe
  if [ -z "${PROJECT_NAME:-}" ]; then
    PROJECT_NAME=$(jq -r '.project_name // empty' "$STATUS_FILE" 2>/dev/null || echo "")
  fi
  if [ -n "$PROJECT_NAME" ] && [ -n "$HASH_PART" ]; then
    export UNITY_MCP_DEFAULT_INSTANCE="${PROJECT_NAME}@${HASH_PART}"
    log_info "Default instance: $UNITY_MCP_DEFAULT_INSTANCE"
  fi
elif [ "$SKIP_SETUP" = false ]; then
  log_warning "No active status file found, UNITY_MCP_DEFAULT_INSTANCE not set"
fi

# Step 3: Clean/prepare reports directory
if [ "$KEEP_REPORTS" = false ]; then
  log_info "Cleaning reports directory..."
  rm -rf reports
fi

mkdir -p reports reports/_snapshots reports/_staging

# Create skeleton files
cat > reports/junit-nl-suite.xml <<'XML'
<?xml version="1.0" encoding="UTF-8"?>
<testsuites><testsuite name="UnityMCP.NL-T" tests="1" failures="1" errors="0" skipped="0" time="0">
  <testcase name="NL-Suite.Bootstrap" classname="UnityMCP.NL-T">
    <failure message="bootstrap">Bootstrap placeholder; suite will append real tests.</failure>
  </testcase>
</testsuite></testsuites>
XML

printf '# Unity NL/T Editing Suite Test Results\n\n' > reports/junit-nl-suite.md

# Step 4: Create local MCP config
if [ "$SKIP_SETUP" = false ]; then
  log_info "Creating MCP config..."

  mkdir -p .claude/local

  if [ "${USE_HTTP_MODE:-false}" = true ]; then
    # HTTP mode config
    cat > .claude/local/mcp.json <<EOF
{
  "mcpServers": {
    "unity": {
      "command": "uv",
      "args": [
        "run",
        "--active",
        "--directory",
        "Server",
        "mcp-for-unity",
        "--transport",
        "http",
        "--http-url",
        "$HTTP_URL"
      ],
      "transport": {"type": "stdio"},
      "env": {
        "PYTHONUNBUFFERED": "1",
        "MCP_LOG_LEVEL": "debug",
        "UNITY_MCP_TRANSPORT": "http",
        "UNITY_MCP_HTTP_URL": "$HTTP_URL"
      }
    }
  }
}
EOF
    log_success "MCP config created at .claude/local/mcp.json (HTTP mode)"
  else
    # Stdio mode config
    cat > .claude/local/mcp.json <<EOF
{
  "mcpServers": {
    "unity": {
      "command": "uv",
      "args": [
        "run",
        "--active",
        "--directory",
        "Server",
        "mcp-for-unity",
        "--transport",
        "stdio"
      ],
      "transport": {"type": "stdio"},
      "env": {
        "PYTHONUNBUFFERED": "1",
        "MCP_LOG_LEVEL": "debug",
        "UNITY_PROJECT_ROOT": "$PROJECT_ROOT/TestProjects/UnityMCPTests",
        "UNITY_MCP_STATUS_DIR": "${FOUND_STATUS_DIR:-$PROJECT_ROOT/.unity-mcp}",
        "UNITY_MCP_HOST": "127.0.0.1"
      }
    }
  }
}
EOF

    # Add default instance if available
    if [ -n "${UNITY_MCP_DEFAULT_INSTANCE:-}" ]; then
      jq --arg inst "$UNITY_MCP_DEFAULT_INSTANCE" \
        '.mcpServers.unity.env.UNITY_MCP_DEFAULT_INSTANCE = $inst |
         .mcpServers.unity.args += ["--default-instance", $inst]' \
        .claude/local/mcp.json > .claude/local/mcp.json.tmp
      mv .claude/local/mcp.json.tmp .claude/local/mcp.json
    fi

    log_success "MCP config created at .claude/local/mcp.json (stdio mode)"
  fi
fi

# Step 5: Verify MCP server can start
log_info "Verifying MCP server..."
if ! uv run --active --directory Server mcp-for-unity --transport stdio --help > /tmp/mcp-preflight.log 2>&1; then
  log_error "MCP server failed to start. Check logs:"
  cat /tmp/mcp-preflight.log
  exit 1
fi
log_success "MCP server verified"

# Step 6: Run NL pass
if [ "$RUN_NL" = true ]; then
  log_info "========================================"
  log_info "Running NL Pass (tests NL-0 to NL-4)..."
  log_info "========================================"

  # Check if claude CLI is available
  if ! command -v claude &> /dev/null; then
    log_error "claude CLI not found. Please install it first."
    log_info "See: https://github.com/anthropics/claude-code"
    exit 1
  fi

  # Run with claude CLI
  if cat .claude/prompts/nl-unity-suite-nl.md | claude \
    --print \
    --mcp-config .claude/local/mcp.json \
    --settings .claude/settings.json \
    --permission-mode bypassPermissions \
    --model claude-haiku-4-5-20251001 \
    2>&1 | tee reports/nl-pass.log; then
    log_success "NL pass completed"
  else
    log_error "NL pass failed (exit code: $?)"
    log_info "Check reports/nl-pass.log for details"
  fi

  # Show results
  echo ""
  log_info "NL Pass Results:"
  for id in NL-0 NL-1 NL-2 NL-3 NL-4; do
    if [ -f "reports/${id}_results.xml" ]; then
      echo -e "  ${GREEN}✓${NC} $id"
    else
      echo -e "  ${RED}✗${NC} $id (missing)"
    fi
  done
  echo ""
fi

# Step 7: Run GameObject pass
if [ "$RUN_GO" = true ]; then
  log_info "========================================"
  log_info "Running GameObject Pass (tests GO-0 to GO-10)..."
  log_info "========================================"

  if cat .claude/prompts/nl-gameobject-suite.md | claude \
    --print \
    --mcp-config .claude/local/mcp.json \
    --settings .claude/settings.json \
    --permission-mode bypassPermissions \
    --model claude-haiku-4-5-20251001 \
    2>&1 | tee reports/go-pass.log; then
    log_success "GameObject pass completed"
  else
    log_error "GameObject pass failed (exit code: $?)"
    log_info "Check reports/go-pass.log for details"
  fi

  # Show results
  echo ""
  log_info "GameObject Pass Results:"
  for id in GO-0 GO-1 GO-2 GO-3 GO-4 GO-5 GO-6 GO-7 GO-8 GO-9 GO-10; do
    if [ -f "reports/${id}_results.xml" ]; then
      echo -e "  ${GREEN}✓${NC} $id"
    else
      echo -e "  ${RED}✗${NC} $id (missing)"
    fi
  done
  echo ""
fi

# Step 8: Run T pass
if [ "$RUN_T" = true ]; then
  log_info "========================================"
  log_info "Running T Pass (tests T-A to T-J)..."
  log_info "========================================"

  if cat .claude/prompts/nl-unity-suite-t.md | claude \
    --print \
    --mcp-config .claude/local/mcp.json \
    --settings .claude/settings.json \
    --permission-mode bypassPermissions \
    --model claude-haiku-4-5-20251001 \
    2>&1 | tee reports/t-pass.log; then
    log_success "T pass completed"
  else
    log_error "T pass failed (exit code: $?)"
    log_info "Check reports/t-pass.log for details"
  fi

  # Show results
  echo ""
  log_info "T Pass Results:"
  for id in T-A T-B T-C T-D T-E T-F T-G T-H T-I T-J; do
    if [ -f "reports/${id}_results.xml" ]; then
      echo -e "  ${GREEN}✓${NC} $id"
    else
      echo -e "  ${RED}✗${NC} $id (missing)"
    fi
  done
  echo ""
fi

# Step 9: Generate summary
log_info "========================================"
log_info "Test Summary"
log_info "========================================"

TOTAL_EXPECTED=0
TOTAL_FOUND=0

if [ "$RUN_NL" = true ]; then
  TOTAL_EXPECTED=$((TOTAL_EXPECTED + 5))
  for id in NL-0 NL-1 NL-2 NL-3 NL-4; do
    [ -f "reports/${id}_results.xml" ] && TOTAL_FOUND=$((TOTAL_FOUND + 1))
  done
fi

if [ "$RUN_T" = true ]; then
  TOTAL_EXPECTED=$((TOTAL_EXPECTED + 10))
  for id in T-A T-B T-C T-D T-E T-F T-G T-H T-I T-J; do
    [ -f "reports/${id}_results.xml" ] && TOTAL_FOUND=$((TOTAL_FOUND + 1))
  done
fi

if [ "$RUN_GO" = true ]; then
  TOTAL_EXPECTED=$((TOTAL_EXPECTED + 11))
  for id in GO-0 GO-1 GO-2 GO-3 GO-4 GO-5 GO-6 GO-7 GO-8 GO-9 GO-10; do
    [ -f "reports/${id}_results.xml" ] && TOTAL_FOUND=$((TOTAL_FOUND + 1))
  done
fi

echo ""
log_info "Results: $TOTAL_FOUND/$TOTAL_EXPECTED tests completed"
log_info "Reports available in: $PROJECT_ROOT/reports/"
echo ""

if [ $TOTAL_FOUND -eq $TOTAL_EXPECTED ]; then
  log_success "All tests completed! 🎉"
  exit 0
else
  log_warning "Some tests are missing. Check logs for details."
  exit 1
fi
