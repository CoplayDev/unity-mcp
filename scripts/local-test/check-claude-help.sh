#!/usr/bin/env bash
echo "=== Claude CLI Help ==="
claude --help

echo ""
echo "=== Checking for prompt options ==="
claude --help | grep -i prompt || echo "No 'prompt' options found"

echo ""
echo "=== Full help output ==="
claude --help 2>&1 | head -50
