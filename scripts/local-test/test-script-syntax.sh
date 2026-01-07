#!/usr/bin/env bash
# Test script syntax

echo "Checking script syntax..."

# Check main script
if bash -n scripts/local-test/run-nl-suite-local.sh 2>&1; then
    echo "✓ run-nl-suite-local.sh: syntax OK"
else
    echo "✗ run-nl-suite-local.sh: syntax ERROR"
    bash -n scripts/local-test/run-nl-suite-local.sh
    exit 1
fi

# Check quick test script
if bash -n scripts/local-test/quick-test.sh 2>&1; then
    echo "✓ quick-test.sh: syntax OK"
else
    echo "✗ quick-test.sh: syntax ERROR"
    bash -n scripts/local-test/quick-test.sh
    exit 1
fi

echo ""
echo "All scripts have valid syntax!"
