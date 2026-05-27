#requires -Version 5
<#
.SYNOPSIS
  Local parity check for the CI Unity-version matrix (Windows companion to check-unity-versions.sh).

.DESCRIPTION
  Reads tools\unity-versions.json (the shared source of truth used by .github\workflows\unity-tests.yml)
  and runs a compile-only batchmode pass on each Unity version installed via Unity Hub.

  -Docker switches to running inside GameCI containers (unityci/editor:ubuntu-<id>-base-<tag>) instead
  of looking for local Unity Hub installs. Requires Docker Desktop running and $env:UNITY_LICENSE set
  to the contents of a Unity_lic.ulf file.

  Exits non-zero if any *checked* version fails. Versions skipped (not installed locally / no image)
  do not cause failure on their own.

.PARAMETER Full
  Run the full EditMode test suite per version instead of compile-only. Matches CI behavior; slower.

.PARAMETER Only
  Filter to versions whose id starts with this prefix (e.g. -Only 6000.0).

.PARAMETER Docker
  Run each version inside a GameCI Docker container instead of a local Unity Hub install.

.PARAMETER DockerImageTag
  Override the GameCI image tag suffix (default: 'base-3'). Pin to e.g. 'base-3.2.2' for reproducibility.

.PARAMETER PrePush
  Hint mode used by the pre-push hook; changes the failure message to mention --no-verify.

.EXAMPLE
  pwsh .\tools\check-unity-versions.ps1
  pwsh .\tools\check-unity-versions.ps1 -Full
  pwsh .\tools\check-unity-versions.ps1 -Only 6000.0
  pwsh .\tools\check-unity-versions.ps1 -Docker
#>

[CmdletBinding()]
param(
  [switch]$Full,
  [string]$Only = "",
  [switch]$Docker,
  [string]$DockerImageTag = "base-3",
  [switch]$PrePush
)

$ErrorActionPreference = "Stop"

$RepoRoot     = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$VersionsJson = Join-Path $RepoRoot "tools\unity-versions.json"
$ProjectPath  = Join-Path $RepoRoot "TestProjects\UnityMCPTests"
$LogDir       = Join-Path $RepoRoot "tools\.unity-check-logs"

if (-not (Test-Path $VersionsJson)) { throw "Missing: $VersionsJson" }
if (-not (Test-Path $ProjectPath))  { throw "Missing project: $ProjectPath" }
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null

# ---- mode setup --------------------------------------------------------------

if ($Docker) {
  if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    Write-Error "-Docker requires Docker Desktop (https://docs.docker.com/get-docker/)"
    exit 2
  }
  & docker info *> $null
  if ($LASTEXITCODE -ne 0) {
    Write-Error "Docker daemon not reachable. Start Docker Desktop and retry."
    exit 2
  }
  if (-not $env:UNITY_LICENSE) {
    Write-Host "error: -Docker requires a Unity license. Set `$env:UNITY_LICENSE to the contents of a Unity_lic.ulf file." -ForegroundColor Red
    Write-Host ""
    Write-Host "One-time setup (free Personal license):"
    Write-Host "  1. Generate a request file inside a GameCI container:"
    Write-Host "       docker run --rm -v `"`${PWD}:/work`" unityci/editor:ubuntu-2021.3.45f2-base-3 ``"
    Write-Host "         /opt/unity/Editor/Unity -batchmode -nographics -quit -createManualActivationFile ``"
    Write-Host "         -logFile /dev/stdout"
    Write-Host "     This writes Unity_v<version>.alf to your current directory."
    Write-Host "  2. Upload that .alf at https://license.unity3d.com/manual -> Personal -> save the .ulf it returns."
    Write-Host "  3. Persist the license in your PowerShell profile:"
    Write-Host "       `$env:UNITY_LICENSE = Get-Content C:\path\to\Unity_v<version>.ulf -Raw"
    Write-Host "  4. Re-run this script."
    Write-Host ""
    Write-Host "(The same UNITY_LICENSE secret is what the GitHub Actions workflow uses; one .ulf works across all"
    Write-Host "matrix versions in practice -- Unity Personal activations are tied to the machine, not the editor version.)"
    exit 2
  }
} else {
  # Unity Hub installs editors under one of these roots on Windows.
  $HubRoots = @()
  foreach ($base in @($env:ProgramFiles, ${env:ProgramFiles(x86)})) {
    if ($base) {
      $candidate = Join-Path $base "Unity\Hub\Editor"
      if (Test-Path $candidate) { $HubRoots += $candidate }
    }
  }

  if ($HubRoots.Count -eq 0) {
    Write-Warning "Unity Hub editor root not found under Program Files. Install at least one editor or use -Docker."
  }
}

function Get-UnityBin([string]$Version) {
  foreach ($root in $HubRoots) {
    $candidate = Join-Path $root "$Version\Editor\Unity.exe"
    if (Test-Path $candidate) { return $candidate }
  }
  return $null
}

$Manifest = Get-Content $VersionsJson -Raw | ConvertFrom-Json
$Versions = $Manifest.versions | ForEach-Object { $_.id }

if ($Only) {
  $Versions = $Versions | Where-Object { $_.StartsWith($Only) }
  if ($Versions.Count -eq 0) {
    Write-Error "No versions matched -Only '$Only'"
    exit 2
  }
}

$modeLabel = if ($Full) { "full EditMode test run" } else { "compile-only" }
$runnerLabel = if ($Docker) { "GameCI Docker ($DockerImageTag)" } else { "local Unity Hub" }
Write-Host "Unity-version check ($modeLabel, $runnerLabel) -- $($Versions.Count) version(s) requested"
Write-Host "  Project: $ProjectPath"
Write-Host "  Logs:    $LogDir"
Write-Host ""

function Invoke-LocalUnity([string]$Version, [string]$LogFile) {
  $unityBin = Get-UnityBin $Version
  if (-not $unityBin) {
    Write-Host "  [SKIP] $Version -- not installed under any Unity Hub root" -ForegroundColor Yellow
    return 2  # skip sentinel
  }

  Write-Host -NoNewline "  [ .. ] $Version -- running...`r"

  # -quit on both paths so Unity batchmode always exits; without it -runTests can hang on test framework shutdown.
  if ($Full) {
    $unityArgs = @("-batchmode", "-quit", "-nographics", "-projectPath", $ProjectPath, "-runTests", "-testPlatform", "editmode", "-logFile", $LogFile)
  } else {
    $unityArgs = @("-batchmode", "-quit", "-nographics", "-projectPath", $ProjectPath, "-logFile", $LogFile)
  }

  $proc = Start-Process -FilePath $unityBin -ArgumentList $unityArgs -NoNewWindow -PassThru -Wait
  if ($proc.ExitCode -eq 0) { return 0 } else { return 1 }
}

function Invoke-DockerUnity([string]$Version, [string]$LogFile) {
  $image = "unityci/editor:ubuntu-$Version-$DockerImageTag"

  Write-Host -NoNewline "  [ .. ] $Version -- pulling $image ...`r"
  & docker pull $image *>> $LogFile
  if ($LASTEXITCODE -ne 0) {
    Write-Host "  [FAIL] $Version -- image pull failed ($image); see $LogFile" -ForegroundColor Red
    Write-Host "    Pull errors:"
    Get-Content $LogFile -Tail 5 | ForEach-Object { Write-Host "      $_" }
    return 1
  }

  Write-Host -NoNewline "  [ .. ] $Version -- running in container...`r"

  # -quit on both paths (see Invoke-LocalUnity note).
  $unityExtra = if ($Full) { "-quit -runTests -testPlatform editmode" } else { "-quit" }

  # Convert Windows path to Docker-friendly format for -v.
  $projectMount = $ProjectPath.Replace('\', '/')

  $script = @"
set -e
mkdir -p /root/.local/share/unity3d/Unity
printf '%s' "`$UNITY_LICENSE" > /root/.local/share/unity3d/Unity/Unity_lic.ulf
/opt/unity/Editor/Unity -batchmode -nographics -projectPath /project $unityExtra -logFile /dev/stdout
"@

  & docker run --rm `
    --platform linux/amd64 `
    -e UNITY_LICENSE `
    -v "${projectMount}:/project" `
    --entrypoint /bin/bash `
    $image `
    -c $script *>> $LogFile

  if ($LASTEXITCODE -eq 0) { return 0 } else { return 1 }
}

$pass = 0; $fail = 0; $skip = 0

foreach ($version in $Versions) {
  $logFile = Join-Path $LogDir "$version.log"
  "" | Set-Content -Path $logFile  # truncate stale

  if ($Docker) {
    $rc = Invoke-DockerUnity $version $logFile
  } else {
    $rc = Invoke-LocalUnity $version $logFile
  }

  switch ($rc) {
    0 { Write-Host "  [PASS] $version                    " -ForegroundColor Green; $pass++ }
    2 { $skip++ }
    default {
      Write-Host "  [FAIL] $version -- see $logFile" -ForegroundColor Red
      $compileErrors = Select-String -Path $logFile -Pattern "error CS\d+" -ErrorAction SilentlyContinue
      if ($compileErrors) {
        Write-Host "    Compile errors:"
        $compileErrors | Select-Object -First 10 | ForEach-Object { Write-Host "      $($_.Line)" }
      } else {
        Write-Host "    Last 20 lines of log:"
        Get-Content $logFile -Tail 20 | ForEach-Object { Write-Host "      $_" }
      }
      $fail++
    }
  }
}

Write-Host ""
Write-Host "Summary: $pass passed, $fail failed, $skip skipped (of $($Versions.Count) configured)"

if ($fail -gt 0) {
  if ($PrePush) {
    Write-Host ""
    Write-Host "Pre-push check failed. To push anyway (skipping this hook): git push --no-verify"
  }
  exit 1
}

if ($pass -eq 0 -and $skip -gt 0) {
  Write-Host ""
  if ($Docker) {
    Write-Host "Note: no versions ran. Check image pull errors above."
  } else {
    Write-Host "Note: no versions from tools/unity-versions.json are installed on this machine."
    Write-Host "Either install via Unity Hub or use -Docker (see Get-Help for license setup)."
  }
}

exit 0
