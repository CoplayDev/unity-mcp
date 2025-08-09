import subprocess
import sys
import time
import signal
import os
from pathlib import Path

def signal_handler(sig, frame):
    print("Shutting down Unity MCP services...")
    sys.exit(0)

signal.signal(signal.SIGINT, signal_handler)

# Start MCP Server in background
server_process = subprocess.Popen([
    sys.executable, "server.py"
], stdout=open("../logs/mcp-server.log", "w"), stderr=subprocess.STDOUT)

print("Unity MCP Server started on localhost:6500")
time.sleep(2)  # Give server time to start

# Start Claude Code Bridge in foreground
print("Starting Claude Code Bridge on localhost:6501...")
print()
print("================================================================")
print("                    SYSTEM READY")
print("================================================================")
print()
print("Services running:")
print("  Unity MCP Server:    http://localhost:6500 ^(Claude Desktop^)")
print("  Claude Code Bridge:  http://localhost:6501 ^(Claude Code^)")
print()
print("Unity MCP Tools available in Claude Code:")
print("  Health check:  curl http://localhost:6501/health")
print("  Scene info:    curl http://localhost:6501/unity/scene")
print("  GameObjects:   curl http://localhost:6501/unity/gameobject")
print("  Console logs:  curl http://localhost:6501/unity/console")
print("  Editor info:   curl http://localhost:6501/unity/editor")
print()
print("WSL/Linux users: Use network IP if localhost fails")
print("  Find your IP in the Flask startup messages below")
print()
print("Logs directory: logs\\")
print()
print("================================================================")
print("       Press Ctrl+C to stop all Unity MCP services")
print("================================================================")
print()

try:
    # Start Claude Code Bridge (runs in foreground)
    subprocess.run([sys.executable, "claude_code_bridge.py"])
except KeyboardInterrupt:
    pass
finally:
    # Clean up background server
    try:
        server_process.terminate()
        server_process.wait(timeout=5)
    except:
        server_process.kill()
    print("Unity MCP services stopped")
