"""Prepare and activate CI licensing with GameCI's tested Linux activation paths.

Only stages repository-provided secrets. XML checks select a candidate; Unity
itself decides whether it is licensed. Never print raw activation output, which
can contain credentials, license serials and account identifiers.
"""

from __future__ import annotations

import argparse
import base64
import os
from pathlib import Path
import subprocess
import urllib.request
import uuid
import xml.etree.ElementTree as ET

# The CLI version used by our successful Unity CI activation (v0.1.69).
# Pin the implementation, including its capability-based older-editor fallback.
GAME_CI_COMMIT = "75c5dcf81523f31b3cca9d7cd16228a2b53de9a8"
GAME_CI_SCRIPTS = (
    "activate.sh", "return_license.sh", "licensing_method.sh", "resolve_unity_path.sh",
)


def decode_ulf(value: str) -> bytes:
    """Accept raw/base64 XML, including XML signatures with namespace/attributes."""
    raw = value.strip().encode("utf-8")
    candidates = [raw]
    try:
        candidates.append(base64.b64decode(b"".join(raw.split()), validate=True))
    except ValueError:
        pass
    for candidate in candidates:
        try:
            root = ET.fromstring(candidate)
        except ET.ParseError:
            continue
        if any(node.tag.rsplit("}", 1)[-1] == "Signature" for node in root.iter()):
            return candidate
    raise ValueError("UNITY_LICENSE is not signed XML (raw or base64 encoded).")


def prepare(directory: Path, environ: dict[str, str]) -> None:
    directory.mkdir(parents=True, exist_ok=True)
    credentials = bool(environ.get("UNITY_EMAIL") and environ.get("UNITY_PASSWORD"))
    license_file = directory / "input.ulf"
    license_file.unlink(missing_ok=True)
    if environ.get("UNITY_LICENSE"):
        try:
            candidate = decode_ulf(environ["UNITY_LICENSE"])
        except ValueError:
            if not credentials:
                raise
            print("::warning::UNITY_LICENSE is not signed XML; trying the configured Unity account.")
        else:
            license_file.write_bytes(candidate)
            license_file.chmod(0o600)
    if not license_file.exists() and not credentials:
        raise ValueError("Unity activation requires a license file or UNITY_EMAIL and UNITY_PASSWORD.")

    steps = directory / "steps"
    steps.mkdir(exist_ok=True)
    for name in GAME_CI_SCRIPTS:
        url = (f"https://raw.githubusercontent.com/game-ci/cli/{GAME_CI_COMMIT}/"
               f"dist/platforms/ubuntu/steps/{name}")
        with urllib.request.urlopen(url, timeout=30) as response:
            (steps / name).write_bytes(response.read())
    # A new identity for this job, shared by activation, both Editors and return.
    # Reuse it when preparation is retried within the same job.
    identity = directory.parent / "unity-machine-id"
    if not identity.exists():
        identity.write_text(uuid.uuid4().hex + "\n", encoding="ascii")


CONTAINER_SCRIPT = r'''
set +eux
export STEPS_DIR="${STEPS_DIR:-/steps}" ACTIVATE_LICENSE_PATH="${ACTIVATE_LICENSE_PATH:-/activation}"
# Match GameCI's headless editor launcher while retaining its licensing logic.
unity-editor() { /opt/unity/Editor/Unity -batchmode -nographics "$@"; }
export -f unity-editor
if [ "$1" = activate ]; then
  source "$STEPS_DIR/activate.sh" || exit $?
  # The file strategy can acquire a Personal seat through account fallback.
  printf '%s' "${GAME_CI_ACTIVATED_VIA:-}" > "$ACTIVATE_LICENSE_PATH/activated-via"
else
  export GAME_CI_ACTIVATED_VIA="$(cat "$ACTIVATE_LICENSE_PATH/activated-via" 2>/dev/null)"
  source "$STEPS_DIR/return_license.sh" || exit $?
  exit "${RETURN_EXIT_CODE:-0}"
fi
'''


def docker_args(directory: Path, image: str, operation: str) -> list[str]:
    if operation not in {"activate", "return"}:
        raise ValueError("Unsupported licensing operation")
    runner_temp = directory.parent
    args = ["docker", "run", "--rm", "--network", "host", "-e", "HOME=/root"]
    # Environment names only: values are never put on the host command line.
    for name in ("UNITY_EMAIL", "UNITY_PASSWORD", "UNITY_SERIAL"):
        args.extend(["-e", name])
    args.extend(["-e", "UNITY_LICENSE=", "-e", "UNITY_LICENSING_SERVER="])
    args.extend(["-e", "UNITY_LICENSE_FILE=" + (
        "/activation/input.ulf" if (directory / "input.ulf").exists() else "")])
    for source, target in (
        (directory, "/activation"),
        (directory / "steps", "/steps:ro"),
        (runner_temp / "unity-machine-id", "/etc/machine-id:ro"),
        (runner_temp / "unity-config", "/root/.config/unity3d"),
        (runner_temp / "unity-local", "/root/.local/share/unity3d"),
        (runner_temp / "unity-cache", "/root/.cache/unity3d"),
    ):
        args.extend(["-v", f"{source}:{target}"])
    return [*args, image, "bash", "-c", CONTAINER_SCRIPT, "ci-unity-license", operation]


def run_license(directory: Path, image: str, operation: str) -> int:
    if operation == "return" and not (directory / "activation-attempted").exists():
        return 0
    if operation == "activate":
        (directory / "activation-attempted").touch()
    result = subprocess.run(docker_args(directory, image, operation), capture_output=True,
                            text=True, encoding="utf-8", errors="replace", check=False)
    if result.returncode:
        output = (result.stdout + result.stderr).lower()
        reason = "Unity or Docker rejected the licensing operation"
        for signature, description in (
            ("machine bindings don't match", "license file belongs to a different machine"),
            ("no seat available", "no Unity license seat is available for this account"),
            ("two-factor", "account requires two-factor authentication"),
            ("invalid credentials", "Unity rejected the account credentials"),
        ):
            if signature in output:
                reason = description
                break
        print(f"::error::Unity license {operation} failed: {reason} (exit {result.returncode}). "
              "Raw licensing output is withheld because it can contain credentials. "
              "Check the configured Unity license/account and available seats.")
        return 1
    print(f"Unity license {operation} completed.")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("operation", choices=("prepare", "activate", "return"))
    args = parser.parse_args()
    directory = Path(os.environ["RUNNER_TEMP"]) / "unity-activation"
    try:
        if args.operation == "prepare":
            prepare(directory, dict(os.environ))
            return 0
        return run_license(directory, os.environ["UNITY_IMAGE"], args.operation)
    except (OSError, ValueError) as exc:
        # Only our constant validation messages are safe to expose; network and
        # filesystem exceptions can include paths or provider response content.
        message = str(exc) if isinstance(exc, ValueError) else "Unable to prepare or run Unity licensing."
        print(f"::error::{message}")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
