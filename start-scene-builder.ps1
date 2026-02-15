$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$serverDir = Join-Path $repoRoot "Server"
$venvDir = Join-Path $serverDir ".venv"
$venvPython = Join-Path $venvDir "Scripts\python.exe"
$appPath = Join-Path $serverDir "src\scene_generator\app.py"

if (-not (Test-Path $serverDir)) {
    throw "Server directory not found: $serverDir"
}

if (-not (Test-Path $venvPython)) {
    Write-Host "Creating virtual environment at $venvDir ..."
    python3 -m venv $venvDir
}

Write-Host "Ensuring pip is available in venv ..."
& $venvPython -m ensurepip --upgrade | Out-Null

Write-Host "Upgrading pip ..."
& $venvPython -m pip install --upgrade pip

Write-Host "Installing runtime dependencies (streamlit/openai/anthropic) ..."
& $venvPython -m pip install streamlit openai anthropic

if (-not (Test-Path $appPath)) {
    throw "App entrypoint not found: $appPath"
}

Write-Host "Starting Scene Builder ..."
& $venvPython -m streamlit run $appPath
