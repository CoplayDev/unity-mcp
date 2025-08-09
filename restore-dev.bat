@echo off
setlocal enabledelayedexpansion

echo ===============================================
echo Unity MCP Development Restore Script
echo ===============================================
echo.
echo Note: The Python server is bundled under UnityMcpBridge\UnityMcpServer~ in the package.
echo       This script restores your installed server path from backups, not the repo copy.
echo.

:: Configuration
set "DEFAULT_BACKUP_DIR=%USERPROFILE%\Desktop\unity-mcp-backup"
set "DEFAULT_SERVER_PATH=%LOCALAPPDATA%\Programs\UnityMCP\UnityMcpServer\src"

:: Get user inputs
echo Please provide the following paths:
echo.

:: Package cache location
echo Unity Package Cache Location:
echo Example: X:\UnityProject\Library\PackageCache\com.coplaydev.unity-mcp@1.0.0
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

:: List available backups
echo.
echo ===============================================
echo Available backups:
echo ===============================================
set "counter=0"
for /d %%d in ("%BACKUP_DIR%\backup_*") do (
    set /a counter+=1
    set "backup!counter!=%%d"
    echo !counter!. %%~nxd
)

if %counter%==0 (
    echo No backups found in %BACKUP_DIR%
    pause
    exit /b 1
)

echo.
set /p "choice=Select backup to restore (1-%counter%): "

:: Validate choice
if "%choice%"=="" goto :invalid_choice
if %choice% lss 1 goto :invalid_choice
if %choice% gtr %counter% goto :invalid_choice

set "SELECTED_BACKUP=!backup%choice%!"
echo.
echo Selected backup: %SELECTED_BACKUP%

:: Validation
echo.
echo ===============================================
echo Validating paths...
echo ===============================================

if not exist "%SELECTED_BACKUP%" (
    echo Error: Selected backup not found: %SELECTED_BACKUP%
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

:: Confirm restore
echo.
echo ===============================================
echo WARNING: This will overwrite current files!
echo ===============================================
echo Restoring from: %SELECTED_BACKUP%
echo Unity Bridge target: %PACKAGE_CACHE_PATH%\Editor
echo Python Server target: %SERVER_PATH%
echo.
set /p "confirm=Continue with restore? (y/N): "
if /i not "%confirm%"=="y" (
    echo Restore cancelled.
    pause
    exit /b 0
)

echo.
echo ===============================================
echo Starting restore...
echo ===============================================

:: Restore Unity Bridge
if exist "%SELECTED_BACKUP%\UnityBridge\Editor" (
    echo Restoring Unity Bridge files...
    rd /s /q "%PACKAGE_CACHE_PATH%\Editor" 2>nul
    xcopy "%SELECTED_BACKUP%\UnityBridge\Editor\*" "%PACKAGE_CACHE_PATH%\Editor\" /E /I /Y > nul
    if !errorlevel! neq 0 (
        echo Error: Failed to restore Unity Bridge files
        pause
        exit /b 1
    )
) else (
    echo Warning: No Unity Bridge backup found, skipping...
)

:: Restore Python Server
if exist "%SELECTED_BACKUP%\PythonServer" (
    echo Restoring Python Server files...
    rd /s /q "%SERVER_PATH%" 2>nul
    mkdir "%SERVER_PATH%"
    xcopy "%SELECTED_BACKUP%\PythonServer\*" "%SERVER_PATH%\" /E /I /Y > nul
    if !errorlevel! neq 0 (
        echo Error: Failed to restore Python Server files
        pause
        exit /b 1
    )
) else (
    echo Warning: No Python Server backup found, skipping...
)

:: Success
echo.
echo ===============================================
echo Restore completed successfully!
echo ===============================================
echo.
echo Next steps:
echo 1. Restart Unity Editor to load restored Bridge code
echo 2. Restart any MCP clients to use restored Server code
echo.
pause
exit /b 0

:invalid_choice
echo Invalid choice. Please enter a number between 1 and %counter%.
pause
exit /b 1