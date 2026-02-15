#!/usr/bin/env bash
# start-scene-builder.sh
# macOS / Linux friendly wrapper to create a virtualenv and run the Scene Builder (streamlit)

set -euo pipefail

# Determine repository root (script directory)
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$SCRIPT_DIR"
SERVER_DIR="$REPO_ROOT/Server"
VENV_DIR="$SERVER_DIR/.venv"
VENV_PY="$VENV_DIR/bin/python3"
APP_PATH="$SERVER_DIR/src/scene_generator/app.py"

print() { printf "%s\n" "$*"; }

if [ ! -d "$SERVER_DIR" ]; then
  echo "Server directory not found: $SERVER_DIR" >&2
  exit 1
fi


# Create venv if missing, with fallbacks to avoid broken venvs on some macOS setups
create_venv() {
  local py_exec="$1"
  local venv_dir="$2"

  print "Attempting to create venv using: $py_exec -m venv --upgrade-deps $venv_dir"
  if "$py_exec" -m venv --upgrade-deps "$venv_dir" >/dev/null 2>&1; then
    return 0
  fi

  print "Falling back to creating venv with --copies"
  if "$py_exec" -m venv --copies "$venv_dir" >/dev/null 2>&1; then
    return 0
  fi

  print "Falling back to simple venv creation"
  if "$py_exec" -m venv "$venv_dir" >/dev/null 2>&1; then
    return 0
  fi

  # As a last resort, try virtualenv (install locally if necessary)
  if "$py_exec" -m pip install --user virtualenv >/dev/null 2>&1; then
    if "$py_exec" -m virtualenv --copies "$venv_dir" >/dev/null 2>&1; then
      return 0
    fi
  fi

  return 1
}

if [ ! -x "$VENV_PY" ]; then
  # pick python executable: prefer python3 then python
  PY_EXEC=""
  if command -v python3 >/dev/null 2>&1; then
    PY_EXEC="$(command -v python3)"
  elif command -v python >/dev/null 2>&1; then
    PY_EXEC="$(command -v python)"
  else
    echo "No python3/python binary found in PATH" >&2
    exit 1
  fi

  print "Creating virtual environment at $VENV_DIR ... (using $PY_EXEC)"
  rm -rf "$VENV_DIR" || true
  if ! create_venv "$PY_EXEC" "$VENV_DIR"; then
    echo "Failed to create a working virtual environment at $VENV_DIR" >&2
    exit 1
  fi
fi

print "Ensuring pip is available in venv ..."
"$VENV_PY" -m ensurepip --upgrade >/dev/null 2>&1 || true

# Verify venv works: import encodings
if ! "$VENV_PY" -c "import encodings; import sys; print('venv-ok', sys.executable)" >/dev/null 2>&1; then
  echo "Virtualenv appears broken (missing encodings). Removing and retrying with virtualenv fallback..." >&2
  rm -rf "$VENV_DIR" || true

  # try again using system python executable
  if command -v python3 >/dev/null 2>&1; then
    PY_EXEC="$(command -v python3)"
  else
    PY_EXEC="$(command -v python)"
  fi

  if ! create_venv "$PY_EXEC" "$VENV_DIR"; then
    echo "Retry venv creation failed. Please create a virtualenv manually or use a different Python installation." >&2
    exit 1
  fi

  # final verify
  if ! "$VENV_PY" -c "import encodings" >/dev/null 2>&1; then
    echo "Virtualenv still broken after retries. Aborting." >&2
    exit 1
  fi
fi

print "Upgrading pip ..."
"$VENV_PY" -m pip install --upgrade pip

print "Installing runtime dependencies (streamlit openai anthropic) ..."
"$VENV_PY" -m pip install streamlit openai anthropic

if [ ! -f "$APP_PATH" ]; then
  echo "App entrypoint not found: $APP_PATH" >&2
  exit 1
fi

print "Starting Scene Builder (streamlit) ..."
exec "$VENV_PY" -m streamlit run "$APP_PATH"
