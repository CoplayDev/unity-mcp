#!/usr/bin/env bash

# Build and upload the Python package to PyPI (manual).
#
# Requirements:
# - Python 3 available on PATH.
# - `uv` installed (used to build the sdist and wheel).
# - `twine` installed (used to upload to PyPI / TestPyPI).
# - Credentials provided via environment variables:
#   - Preferred: PYPI_TOKEN (a PyPI API token)
#   - Or: TWINE_USERNAME and TWINE_PASSWORD
#
# Usage:
#   export PYPI_TOKEN="pypi-..."
#   ./tools/pypi_publish.sh
#
# TestPyPI:
#   ./tools/pypi_publish.sh --test
#
# Notes:
# - PyPI does not allow overwriting an existing version; bump the version in Server/pyproject.toml first.
# - This script clears Server/dist/*.whl and Server/dist/*.tar.gz before building.
# - Only artifacts matching the current version in Server/pyproject.toml are uploaded.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"

REPOSITORY="pypi"

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  echo "Usage: $(basename "$0") [--test]" >&2
  echo "Environment:" >&2
  echo "  PYPI_TOKEN (preferred) or TWINE_USERNAME/TWINE_PASSWORD" >&2
  exit 2
fi

if [[ "${1:-}" == "--test" ]]; then
  REPOSITORY="testpypi"
fi

if [[ "${PYPI_TOKEN:-}" != "" && "${TWINE_PASSWORD:-}" == "" ]]; then
  export TWINE_USERNAME="__token__"
  export TWINE_PASSWORD="$PYPI_TOKEN"
fi

if [[ "${TWINE_USERNAME:-}" == "" || "${TWINE_PASSWORD:-}" == "" ]]; then
  echo "Error: missing credentials. Set PYPI_TOKEN or TWINE_USERNAME and TWINE_PASSWORD." >&2
  exit 2
fi

if ! command -v uv >/dev/null 2>&1; then
  echo "Error: uv is not installed. Install it and retry." >&2
  exit 2
fi

python3 -m twine --version >/dev/null 2>&1 || {
  echo "Error: twine is not installed. Install it (e.g. python3 -m pip install --upgrade twine) and retry." >&2
  exit 2
}

(
  cd "$ROOT_DIR/Server"
  mkdir -p dist
  rm -f dist/*.whl dist/*.tar.gz
  uv build
)

DIST_DIR="$ROOT_DIR/Server/dist"
if [[ ! -d "$DIST_DIR" ]]; then
  echo "Error: dist dir not found: $DIST_DIR" >&2
  exit 2
fi

shopt -s nullglob
VERSION="$(python3 -c 'import tomllib, pathlib; p = pathlib.Path("'"$ROOT_DIR"'/Server/pyproject.toml"); print(tomllib.loads(p.read_text(encoding="utf-8"))["project"]["version"])')"
FILES=("$DIST_DIR"/mcpforunityserver-"$VERSION"*)
shopt -u nullglob

if (( ${#FILES[@]} == 0 )); then
  echo "Error: no files found in $DIST_DIR" >&2
  exit 2
fi

python3 -m twine upload --repository "$REPOSITORY" "${FILES[@]}"
