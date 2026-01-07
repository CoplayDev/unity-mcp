#!/usr/bin/env bash
# Find where claude-code is installed

echo "Current shell: $SHELL"
echo "Current PATH: $PATH"
echo ""

echo "Checking common locations..."
for loc in \
  "/usr/local/bin/claude-code" \
  "/opt/homebrew/bin/claude-code" \
  "$HOME/.npm-global/bin/claude-code" \
  "$HOME/.local/bin/claude-code" \
  "$HOME/bin/claude-code" \
  "/usr/bin/claude-code"
do
  if [ -f "$loc" ]; then
    echo "Found: $loc"
    ls -la "$loc"
  fi
done

echo ""
echo "Searching in PATH..."
if command -v claude-code &> /dev/null; then
  echo "✓ claude-code found via 'command -v': $(command -v claude-code)"
  echo "✓ which claude-code: $(which claude-code)"
else
  echo "✗ claude-code not found in PATH"
fi

echo ""
echo "Searching entire system (this may take a moment)..."
find /usr/local /opt /Applications ~/Library ~/.npm-global ~/.local ~/bin -name "*claude*" -type f 2>/dev/null | head -20
