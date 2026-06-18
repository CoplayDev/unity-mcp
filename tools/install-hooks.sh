#!/usr/bin/env bash
# Installs the repo's git hooks into .git/hooks/.
#
# Usage:
#   tools/install-hooks.sh           # install missing hooks (preserves existing ones)
#   tools/install-hooks.sh --force   # overwrite any existing hooks
#   tools/install-hooks.sh --uninstall  # remove hooks that match the ones we installed

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SRC_DIR="${REPO_ROOT}/tools/hooks"
DST_DIR="${REPO_ROOT}/.git/hooks"

if [[ ! -d "$DST_DIR" ]]; then
  echo "error: $DST_DIR not found — is this a git checkout?" >&2
  exit 2
fi
if [[ ! -d "$SRC_DIR" ]]; then
  echo "error: $SRC_DIR missing" >&2
  exit 2
fi

FORCE=0
UNINSTALL=0
for arg in "$@"; do
  case "$arg" in
    --force) FORCE=1 ;;
    --uninstall) UNINSTALL=1 ;;
    -h|--help)
      sed -n '2,7p' "$0" | sed 's/^# \{0,1\}//'
      exit 0 ;;
    *) echo "Unknown argument: $arg" >&2; exit 2 ;;
  esac
done

installed=0
skipped=0
removed=0

for src in "$SRC_DIR"/*; do
  [[ -e "$src" ]] || continue
  name="$(basename "$src")"
  dst="$DST_DIR/$name"

  if [[ $UNINSTALL -eq 1 ]]; then
    if [[ -e "$dst" ]] && cmp -s "$src" "$dst"; then
      rm -f "$dst"
      echo "  removed $dst"
      removed=$((removed + 1))
    else
      echo "  skipped $dst (not our hook, or absent)"
    fi
    continue
  fi

  if [[ -e "$dst" && $FORCE -ne 1 ]]; then
    if cmp -s "$src" "$dst"; then
      echo "  ok       $dst (already up to date)"
      skipped=$((skipped + 1))
    else
      echo "  skipped  $dst (exists and differs — use --force to overwrite)"
      skipped=$((skipped + 1))
    fi
    continue
  fi

  cp "$src" "$dst"
  chmod +x "$dst"
  echo "  installed $dst"
  installed=$((installed + 1))
done

if [[ $UNINSTALL -eq 1 ]]; then
  echo
  echo "Summary: $removed hook(s) removed"
else
  echo
  echo "Summary: $installed installed, $skipped skipped"
  if [[ $installed -gt 0 ]]; then
    echo
    echo "Hooks are active. To bypass for a single push: git push --no-verify"
  fi
fi
