#!/usr/bin/env bash
# Fix permissions for local test scripts
chmod +x "$(dirname "$0")"/*.sh
echo "Permissions fixed! You can now run the scripts."
