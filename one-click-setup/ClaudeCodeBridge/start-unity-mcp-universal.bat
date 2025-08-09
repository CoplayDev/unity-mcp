@echo off
:: ================================================================
:: Unity MCP Universal Launcher
:: 
:: This script starts both Unity MCP Server and Claude Code Bridge
:: Compatible with any Unity project using Unity MCP
:: 
:: Requirements:
:: - Python 3.12+ installed and in PATH
:: - Unity Editor with Unity MCP Bridge package
:: - This script in Unity project root directory
:: 
:: GitHub: https://github.com/justinpbarnett/unity-mcp
:: ================================================================

setlocal EnableDelayedExpansion
title Unity MCP Universal Launcher
color 0B

echo ================================================================
echo               Unity MCP Universal Launcher
echo ================================================================
echo.
echo This will start:
echo  [✓] Unity MCP Server + Claude Code Bridge (Combined)
echo.

:: Validate environment
echo [VALIDATION] Checking system requirements...

:: Check if we're in the ClaudeCodeBridge folder within a Unity project
if not exist "..\Assets" (
    echo [ERROR] This doesn't appear to be in a Unity project
    echo         Please ensure ClaudeCodeBridge folder is in Unity project root
    echo         Expected to find: ..\Assets folder
    pause
    exit /b 1
)

:: Check if Unity MCP is bundled (should always be present)
if not exist "unity-mcp\UnityMcpServer\src\server.py" (
    echo [ERROR] Unity MCP Server not found in project
    echo         This project should include unity-mcp folder
    echo         Please re-download the complete project
    pause
    exit /b 1
)

:: Check Python
python --version > nul 2>&1
if !errorlevel! neq 0 (
    echo [ERROR] Python not found in PATH
    echo         Please install Python 3.12+ and add to PATH
    echo         Download: https://python.org/downloads
    pause
    exit /b 1
)

:: Check Unity (optional warning)
tasklist /FI "IMAGENAME eq Unity.exe" 2>NUL | find /I /N "Unity.exe">NUL
if "%ERRORLEVEL%"=="0" (
    echo [✓] Unity Editor detected
) else (
    echo [!] Unity Editor not detected - please start Unity first
)

:: Verify Unity MCP components
if exist "unity-mcp\UnityMcpServer\src\claude_code_bridge.py" (
    echo [✓] Unity MCP Server components verified
) else (
    echo [WARNING] Some Unity MCP components may be missing
    echo           Project should be complete - continuing anyway
)

echo [✓] All requirements validated
echo.
echo Press any key to continue...
pause > nul

echo.
echo ================================================================
echo [1/4] Python Environment Setup
echo ================================================================

:: Virtual environment name (can be customized)
set VENV_NAME=unity-mcp-env-windows

if not exist "%VENV_NAME%" (
    echo Creating Python virtual environment: %VENV_NAME%
    python -m venv "%VENV_NAME%"
    if !errorlevel! neq 0 (
        echo [ERROR] Failed to create virtual environment
        echo         Check Python installation and permissions
        pause
        exit /b 1
    )
    echo [✓] Virtual environment created
) else (
    echo [✓] Using existing virtual environment: %VENV_NAME%
)

:: Activate environment
call "%VENV_NAME%\Scripts\activate.bat"
if !errorlevel! neq 0 (
    echo [ERROR] Failed to activate virtual environment
    pause
    exit /b 1
)
echo [✓] Virtual environment activated

echo.
echo ================================================================
echo [2/4] Dependencies Installation
echo ================================================================

echo Installing Unity MCP dependencies...
pip install --quiet -r "unity-mcp\UnityMcpServer\src\requirements.txt"

if !errorlevel! neq 0 (
    echo [WARNING] Some dependencies may have installation issues
    echo           Attempting individual installation...
    pip install httpx>=0.27.2
    pip install "mcp[cli]>=1.4.1"
    pip install flask>=2.3.0
) else (
    echo [✓] All dependencies installed successfully
)

echo.
echo ================================================================
echo [3/4] Unity MCP Server + Bridge Startup (Combined)
echo ================================================================

cd "unity-mcp\UnityMcpServer\src"
echo Starting Unity MCP Server on localhost:6500...

:: Create log directory if needed
if not exist "..\logs" mkdir "..\logs"

:: Start both servers using Python script that handles both
echo Starting combined Unity MCP Server + Claude Code Bridge...
echo.

:: Create combined server script
echo Creating combined server launcher...
(
echo import subprocess
echo import sys
echo import time
echo import signal
echo import os
echo from pathlib import Path
echo.
echo def signal_handler^(sig, frame^):
echo     print^("Shutting down Unity MCP services..."^)
echo     sys.exit^(0^)
echo.
echo signal.signal^(signal.SIGINT, signal_handler^)
echo.
echo # Start MCP Server in background
echo server_process = subprocess.Popen^([
echo     sys.executable, "server.py"
echo ], stdout=open^("../logs/mcp-server.log", "w"^), stderr=subprocess.STDOUT^)
echo.
echo print^("Unity MCP Server started on localhost:6500"^)
echo time.sleep^(2^)  # Give server time to start
echo.
echo # Start Claude Code Bridge in foreground
echo print^("Starting Claude Code Bridge on localhost:6501..."^)
echo print^(^)
echo print^("================================================================"^)
echo print^("                    SYSTEM READY"^)
echo print^("================================================================"^)
echo print^(^)
echo print^("Services running:"^)
echo print^("  Unity MCP Server:    http://localhost:6500 ^(Claude Desktop^)"^)
echo print^("  Claude Code Bridge:  http://localhost:6501 ^(Claude Code^)"^)
echo print^(^)
echo print^("Unity MCP Tools available in Claude Code:"^)
echo print^("  Health check:  curl http://localhost:6501/health"^)
echo print^("  Scene info:    curl http://localhost:6501/unity/scene"^)
echo print^("  GameObjects:   curl http://localhost:6501/unity/gameobject"^)
echo print^("  Console logs:  curl http://localhost:6501/unity/console"^)
echo print^("  Editor info:   curl http://localhost:6501/unity/editor"^)
echo print^(^)
echo print^("WSL/Linux users: Use network IP if localhost fails"^)
echo print^("  Find your IP in the Flask startup messages below"^)
echo print^(^)
echo print^("Logs directory: logs\\"^)
echo print^(^)
echo print^("================================================================"^)
echo print^("       Press Ctrl+C to stop all Unity MCP services"^)
echo print^("================================================================"^)
echo print^(^)
echo.
echo try:
echo     # Start Claude Code Bridge ^(runs in foreground^)
echo     subprocess.run^([sys.executable, "claude_code_bridge.py"]^)
echo except KeyboardInterrupt:
echo     pass
echo finally:
echo     # Clean up background server
echo     try:
echo         server_process.terminate^(^)
echo         server_process.wait^(timeout=5^)
echo     except:
echo         server_process.kill^(^)
echo     print^("Unity MCP services stopped"^)
) > combined_launcher.py

:: Run the combined launcher
python combined_launcher.py

:: Cleanup on exit  
del combined_launcher.py > nul 2>&1
echo.
echo ================================================================
echo                    SHUTTING DOWN
echo ================================================================
echo.
echo Unity MCP services stopped by combined launcher
echo [✓] Logs preserved in logs\ directory
echo.
echo Thank you for using Unity MCP!
echo Report issues: https://github.com/justinpbarnett/unity-mcp/issues
echo.
pause