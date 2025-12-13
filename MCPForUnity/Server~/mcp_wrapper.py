"""
MCP Wrapper for Antigravity on Windows
Fixes the "invalid trailing data" error by removing \r characters from stdout.

This wrapper is needed because Windows adds \r\n line endings which cause
JSON-RPC parsing errors in Antigravity's MCP client.

Usage in mcp_config.json:
{
  "mcpServers": {
    "unity-mcp": {
      "disabled": false,
      "command": "python",
      "args": [
        "C:\\path\\to\\this\\file\\mcp_wrapper.py"
      ]
    }
  }
}

Credits: Solution by @gajzzs from https://github.com/CoplayDev/unity-mcp/issues/430
"""

import sys
import os
import subprocess
import threading

# Set binary mode for stdin/stdout to handle raw bytes
import msvcrt
msvcrt.setmode(sys.stdin.fileno(), os.O_BINARY)
msvcrt.setmode(sys.stdout.fileno(), os.O_BINARY)


def forward_stdin(proc):
    """Forward stdin from Antigravity to the MCP server process."""
    try:
        while True:
            line = sys.stdin.buffer.readline()
            if not line:
                break
            proc.stdin.write(line)
            proc.stdin.flush()
    except:
        pass


def convert_stdout(proc):
    """
    Read stdout from MCP server and remove \r characters before forwarding to Antigravity.
    This fixes the "invalid trailing data" error caused by Windows line endings.
    """
    try:
        while True:
            line = proc.stdout.readline()
            if not line:
                break
            # Convert \r\n to \n by removing all \r
            cleaned = line.replace(b'\r', b'')
            sys.stdout.buffer.write(cleaned)
            sys.stdout.buffer.flush()
    except:
        pass


# IMPORTANT: Update this path to match your uvx installation
# You can find the correct path by running: where uvx
# Common locations:
# - WinGet: C:\Users\<username>\AppData\Local\Microsoft\WinGet\Packages\astral-sh.uv_Microsoft.Winget.Source_8wekyb3d8bbwe\uvx.exe
# - Manual install: C:\Users\<username>\.cargo\bin\uvx.exe
# - System-wide: C:\Program Files\uv\uvx.exe

# Try to detect uvx path automatically
username = os.environ.get('USERNAME')
uvx_path = None

if username:
    # Try WinGet installation path (most common on Windows)
    winget_path = rf"C:\Users\{username}\AppData\Local\Microsoft\WinGet\Packages\astral-sh.uv_Microsoft.Winget.Source_8wekyb3d8bbwe\uvx.exe"
    if os.path.exists(winget_path):
        uvx_path = winget_path

# Fallback to 'uvx' command (assumes it's in PATH)
if not uvx_path:
    uvx_path = "uvx"

# Allow custom Git repository URL via environment variable
# This makes it easy to use your own fork when pushing to Git
# Set UNITY_MCP_GIT_URL environment variable to override
# Example: UNITY_MCP_GIT_URL=git+https://github.com/YourUsername/unity-mcp@main#subdirectory=Server
default_git_url = "git+https://github.com/choej2303/unity-mcp-gg@versionUp#subdirectory=Server"
git_url = os.environ.get('UNITY_MCP_GIT_URL', default_git_url)

# Support command-line argument for git URL (overrides environment variable)
# Usage: python mcp_wrapper.py [git_url]
if len(sys.argv) > 1:
    git_url = sys.argv[1]

cmd = [
    uvx_path,
    "--from", git_url,
    "mcp-for-unity", "--transport", "stdio"
]

proc = subprocess.Popen(
    cmd,
    stdin=subprocess.PIPE,
    stdout=subprocess.PIPE,
    stderr=subprocess.DEVNULL,
    creationflags=subprocess.CREATE_NO_WINDOW if sys.platform == 'win32' else 0
)

t1 = threading.Thread(target=forward_stdin, args=(proc,), daemon=True)
t2 = threading.Thread(target=convert_stdout, args=(proc,), daemon=True)
t1.start()
t2.start()

try:
    sys.exit(proc.wait())
except:
    proc.terminate()
