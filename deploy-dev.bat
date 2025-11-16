@echo off
setlocal enabledelayedexpansion

echo ===============================================
echo MCP for Unity Development Deployment Script
echo ===============================================
echo.

:: Configuration
set "SCRIPT_DIR=%~dp0"
set "BRIDGE_SOURCE=%SCRIPT_DIR%MCPForUnity"
set "SERVER_SOURCE=%SCRIPT_DIR%Server"
set "DEFAULT_BACKUP_DIR=%USERPROFILE%\Desktop\unity-mcp-backup"
set "DEFAULT_SERVER_PATH=%LOCALAPPDATA%\UnityMCP\UnityMcpServer\src"

:: Get user inputs
echo Please provide the following paths:
echo.

:: Package cache location
echo Unity Package Cache Location:
echo Example: X:\Unity\Projects\UnityMCPTestbed2\Library\PackageCache\com.coplaydev.unity-mcp@4c106125b342
set /p "PACKAGE_CACHE_PATH=Enter Unity package cache path: "

if "%PACKAGE_CACHE_PATH%"=="" (
    echo Error: Package cache path cannot be empty!
    pause
    exit /b 1
)

:: Server installation path (with default)
echo.
echo Server Installation Path:
echo Default: %DEFAULT_SERVER_PATH%
set /p "SERVER_PATH=Enter server path (or press Enter for default): "
if "%SERVER_PATH%"=="" set "SERVER_PATH=%DEFAULT_SERVER_PATH%"

:: Backup location (with default)
echo.
echo Backup Location:
echo Default: %DEFAULT_BACKUP_DIR%
set /p "BACKUP_DIR=Enter backup directory (or press Enter for default): "
if "%BACKUP_DIR%"=="" set "BACKUP_DIR=%DEFAULT_BACKUP_DIR%"

:: Validation
echo.
echo ===============================================
echo Validating paths...
echo ===============================================

if not exist "%BRIDGE_SOURCE%" (
    echo Error: Bridge source not found: %BRIDGE_SOURCE%
    pause
    exit /b 1
)

if not exist "%SERVER_SOURCE%" (
    echo Error: Server source not found: %SERVER_SOURCE%
    pause
    exit /b 1
)

if not exist "%PACKAGE_CACHE_PATH%" (
    echo Error: Package cache path not found: %PACKAGE_CACHE_PATH%
    pause
    exit /b 1
)

if not exist "%SERVER_PATH%" (
    echo Error: Server installation path not found: %SERVER_PATH%
    pause
    exit /b 1
)

:: Create backup directory
if not exist "%BACKUP_DIR%" (
    echo Creating backup directory: %BACKUP_DIR%
    mkdir "%BACKUP_DIR%"
)

:: Create timestamped backup subdirectory
set "TIMESTAMP=%date:~-4,4%%date:~-10,2%%date:~-7,2%_%time:~0,2%%time:~3,2%%time:~6,2%"
set "TIMESTAMP=%TIMESTAMP: =0%"
set "TIMESTAMP=%TIMESTAMP::=-%"
set "TIMESTAMP=%TIMESTAMP:/=-%"
set "BACKUP_SUBDIR=%BACKUP_DIR%\backup_%TIMESTAMP%"
mkdir "%BACKUP_SUBDIR%"

echo.
echo ===============================================
echo Starting deployment...
echo ===============================================

:: Backup original files
echo Creating backup of original files...
if exist "%PACKAGE_CACHE_PATH%\Editor" (
    echo Backing up Unity Bridge files...
    xcopy "%PACKAGE_CACHE_PATH%\Editor" "%BACKUP_SUBDIR%\UnityBridge\Editor\" /E /I /Y > nul
    if !errorlevel! neq 0 (
        echo Error: Failed to backup Unity Bridge files
        pause
        exit /b 1
    )
)

if exist "%SERVER_PATH%" (
    echo Backing up Python Server files...
    xcopy "%SERVER_PATH%\*" "%BACKUP_SUBDIR%\PythonServer\" /E /I /Y > nul
    if !errorlevel! neq 0 (
        echo Error: Failed to backup Python Server files
        pause
        exit /b 1
    )
)

:: Deploy Unity Bridge
echo.
echo Deploying Unity Bridge code...
xcopy "%BRIDGE_SOURCE%\Editor\*" "%PACKAGE_CACHE_PATH%\Editor\" /E /Y > nul
if !errorlevel! neq 0 (
    echo Error: Failed to deploy Unity Bridge code
    pause
    exit /b 1
)

:: Deploy Python Server
echo Deploying Python Server code...
xcopy "%SERVER_SOURCE%\*" "%SERVER_PATH%\" /E /Y > nul
if !errorlevel! neq 0 (
    echo Error: Failed to deploy Python Server code
    pause
    exit /b 1
)

:: Success
echo.
echo ===============================================
echo Deployment completed successfully!
echo ===============================================
echo.
echo Backup created at: %BACKUP_SUBDIR%
echo.
echo Next steps:
echo 1. Restart Unity Editor to load new Bridge code
echo 2. Restart any MCP clients to use new Server code
echo 3. Use restore-dev.bat to rollback if needed
echo.
pause