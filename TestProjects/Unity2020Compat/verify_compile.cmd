@echo off
rem ============================================================
rem  Unity 2020.3 compile verification for MCP for Unity
rem
rem  Usage:
rem    verify_compile.cmd                (uses UNITY_EDITOR env var or auto-detect)
rem    verify_compile.cmd <path\to\Unity.exe>
rem
rem  Exit codes: 0 = verification passed, 1 = verification failed
rem              (compile errors, Unity startup failure, missing log, unclean exit),
rem              2 = Unity executable not found
rem ============================================================
setlocal

set "PROJECT_DIR=%~dp0"
set "LOG_FILE=%PROJECT_DIR%compile_check.log"
set "UNITY_EXE="

if not "%~1"=="" (
    set "UNITY_EXE=%~1"
) else if defined UNITY_EDITOR (
    set "UNITY_EXE=%UNITY_EDITOR%"
) else (
    for %%E in (
        "C:\SoftWare\Unity\2020.3.24f1\Editor\Unity.exe"
        "C:\Program Files\Unity\Hub\Editor\2020.3.*\Editor\Unity.exe"
        "%ProgramFiles%\Unity\Hub\Editor\2020.3.*\Editor\Unity.exe"
    ) do (
        if not defined UNITY_EXE if exist "%%~E" set "UNITY_EXE=%%~E"
    )
)

if not defined UNITY_EXE (
    echo [verify] Unity 2020.3 executable not found.
    echo [verify] Pass the path:  verify_compile.cmd C:\path\to\Unity.exe
    exit /b 2
)
if not exist "%UNITY_EXE%" (
    echo [verify] Unity executable not found: %UNITY_EXE%
    exit /b 2
)

echo %UNITY_EXE% | findstr /c:"2020.3" >nul
if errorlevel 1 (
    echo [verify] WARNING: %UNITY_EXE% is not a Unity 2020.3 editor.
    echo [verify] This project validates the 2020.3 compatibility floor; a newer
    echo [verify] editor only confirms forward-compatibility, not 2020.3 support.
)

echo [verify] Unity: %UNITY_EXE%
echo [verify] Project: %PROJECT_DIR%
del /q "%LOG_FILE%" 2>nul

"%UNITY_EXE%" -batchmode -nographics -quit -projectPath "%PROJECT_DIR%" -logFile "%LOG_FILE%"
set "UNITY_STATUS=%ERRORLEVEL%"

if not "%UNITY_STATUS%"=="0" (
    echo [verify] FAILED: Unity exited with status %UNITY_STATUS%.
    exit /b 1
)

if not exist "%LOG_FILE%" (
    echo [verify] No log file produced (Unity may have failed to start).
    exit /b 1
)

findstr /c:"error CS" "%LOG_FILE%" >nul
if not errorlevel 1 (
    echo [verify] FAILED: compiler errors found in %LOG_FILE%
    exit /b 1
)

findstr /c:"Exiting batchmode successfully now!" "%LOG_FILE%" >nul
if errorlevel 1 (
    echo [verify] FAILED: Unity did not exit cleanly (status %UNITY_STATUS%).
    exit /b 1
)

echo [verify] PASS: 0 compiler errors, clean batchmode exit.
echo [verify] NOTE: for the full supported-version matrix, also run
echo [verify]   tools/check-unity-versions.sh (see repo CI).
exit /b 0
