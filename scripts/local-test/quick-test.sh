#!/usr/bin/env bash
# Quick test runner - Run a single test or small subset for rapid iteration
# Much faster than running the full suite

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
log_error() { echo -e "${RED}[ERROR]${NC} $*"; }

# Parse arguments
TEST_ID="${1:-}"
if [ -z "$TEST_ID" ]; then
  cat <<EOF
Usage: $0 TEST_ID [CUSTOM_PROMPT]

Run a single test from the suite for rapid iteration.

Arguments:
  TEST_ID        Test to run (NL-0, NL-1, T-A, etc.)
  CUSTOM_PROMPT  Optional: custom prompt override

Examples:
  $0 NL-0                    # Run baseline test
  $0 T-F                     # Run atomic multi-edit test
  $0 NL-1 "custom test"      # Run with custom prompt

Available tests:
  NL-0  Baseline State Capture
  NL-1  Core Method Operations
  NL-2  Anchor Comment Insertion
  NL-3  End-of-Class Content
  NL-4  Console State Verification
  T-A   Temporary Helper Lifecycle
  T-B   Method Body Interior Edit
  T-C   Different Method Interior Edit
  T-D   End-of-Class Helper
  T-E   Method Evolution Lifecycle
  T-F   Atomic Multi-Edit
  T-G   Path Normalization Test
  T-H   Validation on Modified File
  T-I   Failure Surface Testing
  T-J   Idempotency on Modified File
  GO-0  Hierarchy with ComponentTypes
  GO-1  Find GameObjects Tool
  GO-2  GameObject Resource Read
  GO-3  Components Resource Read
  GO-4  Manage Components Tool - Add/Set
  GO-5  Find GameObjects by Name
  GO-6  Find GameObjects by Tag
  GO-7  Single Component Resource Read
  GO-8  Remove Component
  GO-9  Find with Pagination
  GO-10 Deprecation Warnings
EOF
  exit 1
fi

CUSTOM_PROMPT="${2:-}"

# Validate test ID
case "$TEST_ID" in
  NL-[0-4])
    SUITE="NL"
    PROMPT_FILE=".claude/prompts/nl-unity-suite-nl.md"
    ;;
  T-[A-J])
    SUITE="T"
    PROMPT_FILE=".claude/prompts/nl-unity-suite-t.md"
    ;;
  GO-[0-9]|GO-10)
    SUITE="GO"
    PROMPT_FILE=".claude/prompts/nl-gameobject-suite.md"
    ;;
  *)
    log_error "Invalid test ID: $TEST_ID"
    log_error "Must be NL-0 through NL-4, T-A through T-J, or GO-0 through GO-10"
    exit 1
    ;;
esac

log_info "Running single test: $TEST_ID ($SUITE suite)"

# Check Unity is running - try HTTP first, then stdio
HTTP_URL="http://localhost:8080"
USE_HTTP_MODE=false

log_info "Checking for Unity HTTP bridge at $HTTP_URL..."
if command -v curl &> /dev/null && curl -s --max-time 2 "$HTTP_URL" > /dev/null 2>&1; then
  log_success "Found Unity HTTP bridge at $HTTP_URL"
  USE_HTTP_MODE=true
  export UNITY_MCP_TRANSPORT=http
  export UNITY_MCP_HTTP_URL="$HTTP_URL"
else
  log_info "HTTP bridge not responding, checking for stdio mode..."

  MCP_STATUS_DIRS=(
    "$PROJECT_ROOT/.unity-mcp"
    "$HOME/.unity-mcp"
  )

  FOUND_STATUS_DIR=""
  for dir in "${MCP_STATUS_DIRS[@]}"; do
    if [ -d "$dir" ] && [ -n "$(ls -A "$dir"/unity-mcp-status-*.json 2>/dev/null)" ]; then
      FOUND_STATUS_DIR="$dir"
      log_success "Found Unity status in: $dir"
      break
    fi
  done

  if [ -z "$FOUND_STATUS_DIR" ]; then
    log_error "Neither HTTP nor stdio mode detected!"
    log_error "Make sure Unity Editor is running with the test project."
    log_error "Checked HTTP: $HTTP_URL"
    log_error "Checked stdio: ${MCP_STATUS_DIRS[*]}"
    exit 1
  fi
fi

# Set up environment
export PYTHONUNBUFFERED=1
export MCP_LOG_LEVEL=debug
if [ "$USE_HTTP_MODE" = true ]; then
  log_info "Using HTTP transport mode"
else
  export UNITY_PROJECT_ROOT="$PROJECT_ROOT/TestProjects/UnityMCPTests"
  export UNITY_MCP_STATUS_DIR="$FOUND_STATUS_DIR"
  export UNITY_MCP_HOST=127.0.0.1
  log_info "Using stdio transport mode"
fi

# Prepare reports dir
mkdir -p reports

# Determine the prompt to use
if [ -n "$CUSTOM_PROMPT" ]; then
  PROMPT="$CUSTOM_PROMPT"
  log_info "Using custom prompt: $PROMPT"
else
  # Create a focused prompt that runs only this test
  PROMPT="Run ONLY test $TEST_ID from the $SUITE suite. Follow all the rules in $PROMPT_FILE but execute only the single test $TEST_ID. Write the result to reports/${TEST_ID}_results.xml."
  log_info "Using auto-generated focused prompt"
fi

# Ensure MCP config exists
if [ ! -f .claude/local/mcp.json ]; then
  log_info "MCP config not found, running setup..."
  "$SCRIPT_DIR/run-nl-suite-local.sh" --skip-setup --nl-only --keep-reports || true
fi

# Run the test
log_info "Executing test via Claude Code..."
echo ""

if [ -n "$CUSTOM_PROMPT" ]; then
  # Use custom prompt directly
  echo "$PROMPT" | claude \
    --print \
    --mcp-config .claude/local/mcp.json \
    --settings .claude/settings.json \
    --model claude-haiku-4-5-20251001 \
    2>&1 | tee "reports/${TEST_ID}_quick.log"
else
  # Use base prompt file with focused instruction
  (cat "$PROMPT_FILE"; echo ""; echo "$PROMPT") | claude \
    --print \
    --mcp-config .claude/local/mcp.json \
    --settings .claude/settings.json \
    --model claude-haiku-4-5-20251001 \
    2>&1 | tee "reports/${TEST_ID}_quick.log"
fi

EXIT_CODE=$?

echo ""
log_info "========================================"

# Check result
if [ -f "reports/${TEST_ID}_results.xml" ]; then
  log_success "Test $TEST_ID completed!"
  log_info "Result: reports/${TEST_ID}_results.xml"
  log_info "Log: reports/${TEST_ID}_quick.log"

  # Try to show a preview of the result
  if command -v xmllint &> /dev/null; then
    echo ""
    log_info "Result preview:"
    xmllint --format "reports/${TEST_ID}_results.xml" 2>/dev/null | head -20 || cat "reports/${TEST_ID}_results.xml"
  fi

  exit 0
else
  log_error "Test $TEST_ID did not produce a result file!"
  log_error "Check reports/${TEST_ID}_quick.log for details"
  exit ${EXIT_CODE:-1}
fi
