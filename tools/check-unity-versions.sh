#!/usr/bin/env bash
# Local parity check for the CI Unity-version matrix.
#
# Usage:
#   tools/check-unity-versions.sh                 # compile-only, all installed versions from tools/unity-versions.json
#   tools/check-unity-versions.sh --full          # full EditMode test run (matches CI behavior)
#   tools/check-unity-versions.sh --only 6000.0   # check only versions whose id starts with the given prefix
#   tools/check-unity-versions.sh --docker        # run inside GameCI containers (no local Unity Hub install needed)
#   tools/check-unity-versions.sh --pre-push      # hint mode used by the pre-push hook (changes failure message)
#
# Modes:
#   - Default (local): looks for Unity editors under Unity Hub. Versions not installed are skipped.
#   - --docker: runs each version inside unityci/editor:ubuntu-<id>-base-<tag>. Requires UNITY_LICENSE env
#     (contents of a .ulf file). On macOS arm64, expect ~5-10× slowdown from amd64 emulation.
#
# Exits non-zero if any *checked* version fails. Versions skipped (not installed locally / image not pulled
# in offline mode) do not cause failure on their own.
#
# Linked to CI: both this script and .github/workflows/unity-tests.yml read tools/unity-versions.json.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSIONS_JSON="${REPO_ROOT}/tools/unity-versions.json"
PROJECT_PATH="${REPO_ROOT}/TestProjects/UnityMCPTests"
LOG_DIR="${REPO_ROOT}/tools/.unity-check-logs"

# Default GameCI image tag suffix. GameCI publishes both sliding (base-3) and pinned (base-3.1.0) tags;
# we default to the major-major (base-3) sliding tag and let users pin via --docker-image-tag.
DOCKER_IMAGE_TAG="base-3"

FULL=0
ONLY=""
PRE_PUSH=0
USE_DOCKER=0

require_value() {
  # Validate that a flag taking a value got one (not another flag, not nothing).
  local flag="$1" value="${2:-}"
  if [[ -z "$value" || "$value" == --* ]]; then
    echo "error: $flag requires a value" >&2
    exit 2
  fi
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --full|--with-tests) FULL=1 ;;
    --only) require_value "$1" "${2:-}"; ONLY="$2"; shift ;;
    --docker) USE_DOCKER=1 ;;
    --docker-image-tag) require_value "$1" "${2:-}"; DOCKER_IMAGE_TAG="$2"; shift ;;
    --pre-push) PRE_PUSH=1 ;;
    -h|--help)
      sed -n '2,18p' "$0" | sed 's/^# \{0,1\}//'
      exit 0 ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
  shift
done

if ! command -v jq >/dev/null 2>&1; then
  echo "error: 'jq' is required (brew install jq / apt-get install jq)" >&2
  exit 2
fi
if [[ ! -f "$VERSIONS_JSON" ]]; then
  echo "error: $VERSIONS_JSON missing" >&2
  exit 2
fi
if [[ ! -d "$PROJECT_PATH" ]]; then
  echo "error: project path not found: $PROJECT_PATH" >&2
  exit 2
fi

mkdir -p "$LOG_DIR"

# ---- mode setup ---------------------------------------------------------------------

if [[ $USE_DOCKER -eq 1 ]]; then
  if ! command -v docker >/dev/null 2>&1; then
    echo "error: --docker requires Docker (https://docs.docker.com/get-docker/)" >&2
    exit 2
  fi
  if ! docker info >/dev/null 2>&1; then
    echo "error: Docker daemon not reachable. Start Docker Desktop / dockerd and retry." >&2
    exit 2
  fi
  if [[ -z "${UNITY_LICENSE:-}" ]]; then
    cat >&2 <<'EOF'
error: --docker requires a Unity license. Set UNITY_LICENSE to the contents of a Unity_lic.ulf file.

One-time setup (free Personal license):
  1. Generate a request file inside a GameCI container:
       docker run --rm -v "$PWD":/work unityci/editor:ubuntu-2021.3.45f2-base-3 \
         /opt/unity/Editor/Unity -batchmode -nographics -quit -createManualActivationFile \
         -logFile /dev/stdout
     This writes Unity_v<version>.alf to your current directory.
  2. Upload that .alf at https://license.unity3d.com/manual → choose Personal → save the .ulf it returns.
  3. Export the .ulf contents in your shell (add to ~/.zshrc or similar to persist):
       export UNITY_LICENSE="$(cat /path/to/Unity_v<version>.ulf)"
  4. Re-run this script.

(The same UNITY_LICENSE secret is what the GitHub Actions workflow uses; one .ulf works across all matrix
versions in practice — Unity Personal activations are tied to the machine, not the editor version.)
EOF
    exit 2
  fi

  if [[ "$(uname -m)" == "arm64" || "$(uname -m)" == "aarch64" ]]; then
    if [[ "$(uname -s)" == "Darwin" ]]; then
      echo "note: arm64 Mac — GameCI images run via amd64 emulation; expect ~5-10× slowdown."
    fi
  fi
else
  case "$(uname -s)" in
    Darwin) HUB_ROOT="/Applications/Unity/Hub/Editor" ; UNITY_RELPATH="Unity.app/Contents/MacOS/Unity" ;;
    Linux)  HUB_ROOT="${HOME}/Unity/Hub/Editor"       ; UNITY_RELPATH="Editor/Unity" ;;
    *) echo "error: unsupported OS '$(uname -s)' — use check-unity-versions.ps1 on Windows, or --docker on any platform" >&2; exit 2 ;;
  esac
fi

# Pretty output even without colors set up.
if [[ -t 1 ]]; then
  C_OK=$'\033[32m'; C_FAIL=$'\033[31m'; C_SKIP=$'\033[33m'; C_DIM=$'\033[2m'; C_RST=$'\033[0m'
else
  C_OK=""; C_FAIL=""; C_SKIP=""; C_DIM=""; C_RST=""
fi

VERSIONS=()
while IFS= read -r line; do
  VERSIONS+=("$line")
done < <(jq -r '.versions[].id' "$VERSIONS_JSON")

if [[ -n "$ONLY" ]]; then
  filtered=()
  for v in "${VERSIONS[@]}"; do
    [[ "$v" == "$ONLY"* ]] && filtered+=("$v")
  done
  if [[ ${#filtered[@]} -eq 0 ]]; then
    echo "No versions matched --only '$ONLY'" >&2
    exit 2
  fi
  VERSIONS=("${filtered[@]}")
fi

mode_label="compile-only"
[[ $FULL -eq 1 ]] && mode_label="full EditMode test run"
runner_label="local Unity Hub"
[[ $USE_DOCKER -eq 1 ]] && runner_label="GameCI Docker (${DOCKER_IMAGE_TAG})"
echo "Unity-version check (${mode_label}, ${runner_label}) — ${#VERSIONS[@]} version(s) requested"
echo "  Project: $PROJECT_PATH"
echo "  Logs:    $LOG_DIR"
echo

fail_count=0
pass_count=0
skip_count=0

# ---- per-version runners ------------------------------------------------------------

run_local() {
  local version="$1" log_file="$2"
  local unity_bin="${HUB_ROOT}/${version}/${UNITY_RELPATH}"

  if [[ ! -x "$unity_bin" ]]; then
    echo "  ${C_SKIP}[SKIP]${C_RST} ${version} — not installed (expected at ${C_DIM}${unity_bin}${C_RST})"
    return 2  # skip
  fi

  printf "  [ .. ] %s — running...\r" "$version"

  # -quit on both paths so Unity batchmode always exits — without it -runTests can hang waiting
  # on test framework shutdown on some Unity versions.
  local args
  if [[ $FULL -eq 1 ]]; then
    args=(-batchmode -quit -nographics -projectPath "$PROJECT_PATH" -runTests -testPlatform editmode -logFile "$log_file")
  else
    args=(-batchmode -quit -nographics -projectPath "$PROJECT_PATH" -logFile "$log_file")
  fi

  if "$unity_bin" "${args[@]}" >/dev/null 2>&1; then
    return 0
  else
    return 1
  fi
}

run_docker() {
  local version="$1" log_file="$2"
  local image="unityci/editor:ubuntu-${version}-${DOCKER_IMAGE_TAG}"

  printf "  [ .. ] %s — pulling %s ...\r" "$version" "$image"
  if ! docker pull "$image" >>"$log_file" 2>&1; then
    echo "  ${C_FAIL}[FAIL]${C_RST} ${version} — image pull failed (${C_DIM}${image}${C_RST}); see ${log_file}"
    echo "    Pull errors:"
    tail -5 "$log_file" | sed 's/^/      /'
    return 1
  fi

  printf "  [ .. ] %s — running in container...\r" "$version"

  # GameCI images run as root and expect the .ulf at /root/.local/share/unity3d/Unity/Unity_lic.ulf.
  # Write the license from env on container start, then run Unity with -logFile /dev/stdout so
  # everything (mkdir output, Unity compile log, errors) streams through docker's stdout into our
  # host log_file via a single `>> "$log_file"` redirection. No bind-mount race.
  # -quit on both paths (see run_local note).
  local unity_extra
  if [[ $FULL -eq 1 ]]; then
    unity_extra="-quit -runTests -testPlatform editmode"
  else
    unity_extra="-quit"
  fi

  if docker run --rm \
       --platform linux/amd64 \
       -e UNITY_LICENSE \
       -v "${PROJECT_PATH}:/project" \
       --entrypoint /bin/bash \
       "$image" \
       -c 'set -e
           mkdir -p /root/.local/share/unity3d/Unity
           printf "%s" "$UNITY_LICENSE" > /root/.local/share/unity3d/Unity/Unity_lic.ulf
           /opt/unity/Editor/Unity -batchmode -nographics -projectPath /project '"$unity_extra"' -logFile /dev/stdout' \
       >>"$log_file" 2>&1; then
    return 0
  else
    return 1
  fi
}

# ---- main loop ----------------------------------------------------------------------

for version in "${VERSIONS[@]}"; do
  log_file="${LOG_DIR}/${version}.log"
  : >"$log_file"  # truncate stale log

  if [[ $USE_DOCKER -eq 1 ]]; then
    run_docker "$version" "$log_file" && rc=0 || rc=$?
  else
    run_local "$version" "$log_file" && rc=0 || rc=$?
  fi

  case "$rc" in
    0)
      echo "  ${C_OK}[PASS]${C_RST} ${version}                    "
      pass_count=$((pass_count + 1))
      ;;
    2)
      skip_count=$((skip_count + 1))
      ;;
    *)
      echo "  ${C_FAIL}[FAIL]${C_RST} ${version} — see ${C_DIM}${log_file}${C_RST}"
      if grep -q "error CS" "$log_file" 2>/dev/null; then
        echo "    Compile errors:"
        grep -E "error CS[0-9]+" "$log_file" | head -10 | sed 's/^/      /'
      else
        echo "    Last 20 lines of log:"
        tail -20 "$log_file" | sed 's/^/      /'
      fi
      fail_count=$((fail_count + 1))
      ;;
  esac
done

echo
echo "Summary: ${pass_count} passed, ${fail_count} failed, ${skip_count} skipped (of ${#VERSIONS[@]} configured)"

if [[ $fail_count -gt 0 ]]; then
  if [[ $PRE_PUSH -eq 1 ]]; then
    echo
    echo "Pre-push check failed. To push anyway (skipping this hook): git push --no-verify"
  fi
  exit 1
fi

if [[ $pass_count -eq 0 && $skip_count -gt 0 ]]; then
  echo
  if [[ $USE_DOCKER -eq 1 ]]; then
    echo "Note: no versions ran. Check image pull errors above."
  else
    echo "Note: no versions from tools/unity-versions.json are installed on this machine."
    echo "Either install via Unity Hub or use --docker (see --help for license setup)."
  fi
fi

exit 0
