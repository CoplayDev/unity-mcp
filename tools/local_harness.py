#!/usr/bin/env python3
"""Local headless test harness for MCP for Unity.

A single, stdlib-only, cross-platform Python orchestrator that, from one command,
boots a headless Hub-licensed Unity Editor, reuses the resident route-B stdio
bridge, runs up to three test legs (bridge smoke + Unity EditMode + Unity
PlayMode over the Unity Test Framework wire protocol), aggregates a JUnit report,
and tears down. The same entry point is callable from CI so
``.github/workflows/e2e-bridge.yml`` can collapse its boot/wait/discover/run-smoke
shell into one invocation.

Design: ``docs/superpowers/specs/2026-06-07-local-headless-test-harness-design.md``.

The pure (filesystem/Unity-free) helpers sit at the top of this module and are
importable + unit-testable without Unity. The reused server modules
(``transport.legacy.unity_connection`` / ``transport.legacy.port_discovery``)
are imported INSIDE the live functions (after prepending ``Server/src`` to
``sys.path``, mirroring ``Server/tests/e2e/bridge_smoke.py``), so importing this
module never requires ``Server/src`` on ``sys.path``.

Exit-code contract:
    0  All blocking legs passed. PlayMode non-blocking failures still 0
       (surfaced in the report).
    1  A blocking leg failed = real regression (smoke returncode 1; OR EditMode
       status=="failed"; OR PlayMode failure under --strict-playmode).
    2  Bridge unreachable / setup failure (smoke returncode 2; OR editor
       PID/container died during wait with no license/compile signal; OR the
       overall watchdog timed out).
    3  Project does not compile (read_console compile probe OR compile_fatal
       log-grep).
    4  No Unity license / Hub seat (license_fatal log-grep after warm-up grace).
    5  Editor binary/version not found (discovery layer, before any boot; lists
       every searched path).

Run locally (against TestProjects/UnityMCPTests, default isolated tmp status dir)::

    python tools/local_harness.py --legs smoke,editmode,playmode \
        --project-path TestProjects/UnityMCPTests

    # Attach to an already-resident bridge instead of booting one:
    python tools/local_harness.py --reuse --legs smoke \
        --project-path TestProjects/UnityMCPTests

    # Point at an arbitrary consumer project with an explicit editor binary:
    python tools/local_harness.py --editor /path/to/Unity \
        --project-path ~/TestbedMCP --legs smoke,editmode

    # CI parity (DockerLauncher; license threaded as an opaque editor arg):
    python tools/local_harness.py --ci --no-warmup \
        --legs smoke,editmode,playmode \
        --project-path TestProjects/UnityMCPTests --status-dir .unity-mcp \
        --editor-arg -manualLicenseFile --editor-arg /root/.../Unity_lic.ulf
"""

from __future__ import annotations

import argparse
import glob
import json
import os
import re
import shutil
import signal
import socket
import subprocess
import sys
import tempfile
import threading
import time
import xml.etree.ElementTree as ET
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Callable

# ---------------------------------------------------------------------------
# Repo geometry. This file lives at <repo>/tools/local_harness.py, so the repo
# root is parents[1] and Server/src is <repo>/Server/src. We compute these once
# but only mutate sys.path lazily inside the live functions (see _ensure_src_on_path).
# ---------------------------------------------------------------------------
_THIS = Path(__file__).resolve()
REPO_ROOT = _THIS.parents[1]
SERVER_SRC = REPO_ROOT / "Server" / "src"

DEFAULT_PROJECT_PATH = "TestProjects/UnityMCPTests"
BOOT_METHOD = "MCPForUnity.Editor.McpCiBoot.StartStdioForCi"

# Socket-release delay before any resident re-launch, matching StdioBridgeHost's
# own toWait.Wait(2000) for the #688/#692/#1173 Windows TcpListener leak fix.
SOCKET_RELEASE_MS = 2000

# License-fatal grace: tolerate transient Licensing chatter for this long after
# boot (matches claude-nl-suite.yml fatal_after = boot_start + 120 s).
LICENSE_GRACE_S = 120

# PlayMode init-timeout (ms). C# default is 15000; 120000 is only a docstring
# recommendation, so the harness passes it explicitly.
DEFAULT_PLAYMODE_INIT_TIMEOUT_MS = 120000


# ===========================================================================
# Dataclasses / exceptions (Unity-free)
# ===========================================================================
@dataclass
class EditorNotFound(Exception):
    """Raised when no Unity editor binary can be resolved.

    Carries the full list of absolute paths probed so main() can print a
    remediation hint and exit 5.
    """

    searched: list[str] = field(default_factory=list)

    def __str__(self) -> str:  # pragma: no cover - trivial
        return "No Unity editor binary found. Searched:\n  " + "\n  ".join(self.searched)


@dataclass
class EditorSpec:
    """A resolved editor binary plus the Unity version it satisfies."""

    binary: str
    version: str


@dataclass
class ReadyInfo:
    """Result of bridge discovery: the port, the instance id, and the status file."""

    port: int
    instance_id: str
    status_file: str


@dataclass
class JUnitCase:
    name: str
    time_s: float = 0.0
    failure: str | None = None
    skipped: bool = False


@dataclass
class JUnitSuite:
    name: str
    cases: list[JUnitCase] = field(default_factory=list)


@dataclass
class LegOutcome:
    """The result of running one leg.

    status is one of: "pass", "fail", "skip", "error".
    blocking marks whether a failure here can raise the top-level exit code.
    exit_code carries a specific severity (1/2/3/4) a failing leg maps to.
    """

    name: str
    status: str
    blocking: bool
    detail: str = ""
    exit_code: int = 0
    junit_suite: JUnitSuite | None = None


# ===========================================================================
# Pure helpers: version resolution
# ===========================================================================
def read_project_version(project_path: str | Path) -> str | None:
    """Parse <P>/ProjectSettings/ProjectVersion.txt for the m_EditorVersion line.

    Returns the trimmed token after the colon, or None if the file/line is
    absent or malformed (BOM, garbled). Tolerant: never raises.
    """
    try:
        pv = Path(project_path) / "ProjectSettings" / "ProjectVersion.txt"
        text = pv.read_text(encoding="utf-8-sig", errors="replace")
    except OSError:
        return None
    for raw in text.splitlines():
        line = raw.lstrip("﻿").strip()
        if line.startswith("m_EditorVersion:"):
            token = line.split(":", 1)[1].strip()
            return token or None
    return None


def read_default_version(versions_json: str | Path | None = None) -> str | None:
    """Read defaultVersion from tools/unity-versions.json. Tolerant: returns None
    rather than raising on a missing/garbled file."""
    path = Path(versions_json) if versions_json else (REPO_ROOT / "tools" / "unity-versions.json")
    try:
        data = json.loads(Path(path).read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return None
    val = data.get("defaultVersion")
    return val if isinstance(val, str) and val else None


def resolve_version(project_path: str | Path, versions_json: str | Path | None = None) -> str | None:
    """Version precedence: ProjectVersion.txt, else unity-versions.json defaultVersion."""
    return read_project_version(project_path) or read_default_version(versions_json)


def parse_version(version: str) -> tuple[int, int, int, str]:
    """Parse '6000.0.75f1' -> (6000, 0, 75, 'f1').

    Tolerant of partial versions; missing numeric parts default to 0 and a
    missing suffix to "". Used for comparison and nearest-patch selection.
    """
    m = re.match(r"^\s*(\d+)(?:\.(\d+))?(?:\.(\d+))?\s*([A-Za-z]\w*)?", version or "")
    if not m:
        return (0, 0, 0, "")
    major = int(m.group(1))
    minor = int(m.group(2)) if m.group(2) else 0
    patch = int(m.group(3)) if m.group(3) else 0
    suffix = m.group(4) or ""
    return (major, minor, patch, suffix)


# ===========================================================================
# Pure helpers: editor binary discovery
# ===========================================================================
def editor_relpath(platform: str | None = None) -> str:
    """Per-OS path of the Unity binary relative to a Hub/<version> directory."""
    plat = platform or sys.platform
    if plat == "darwin":
        return "Unity.app/Contents/MacOS/Unity"
    if plat.startswith("win"):
        return "Editor/Unity.exe"
    return "Editor/Unity"  # linux + everything else


def hub_roots(platform: str | None = None, environ: dict[str, str] | None = None) -> list[str]:
    """Per-OS Hub Editor install roots (the directory that holds <version>/ dirs).

    Mirrors check-unity-versions.{sh,ps1}. On Windows, both ProgramFiles roots
    are iterated.
    """
    plat = platform or sys.platform
    env = environ if environ is not None else os.environ
    home = env.get("HOME") or env.get("USERPROFILE") or str(Path.home())
    if plat == "darwin":
        return ["/Applications/Unity/Hub/Editor"]
    if plat.startswith("win"):
        roots: list[str] = []
        for var in ("ProgramFiles", "ProgramFiles(x86)"):
            base = env.get(var)
            if base:
                roots.append(str(Path(base) / "Unity" / "Hub" / "Editor"))
        if not roots:
            roots.append(r"C:\Program Files\Unity\Hub\Editor")
        return roots
    # linux + everything else
    return [str(Path(home) / "Unity" / "Hub" / "Editor")]


def read_secondary_install_path(platform: str | None = None,
                                environ: dict[str, str] | None = None,
                                read_text: Callable[[str], str] | None = None) -> str | None:
    """Read Hub's secondaryInstallPath JSON-encoded string.

    macOS: ~/Library/Application Support/UnityHub/secondaryInstallPath.json
    Windows: %APPDATA%/UnityHub/secondaryInstallPath.json
    Linux: ~/.config/UnityHub/secondaryInstallPath.json

    Tolerates a missing file, "" or a non-string payload (returns None).
    """
    plat = platform or sys.platform
    env = environ if environ is not None else os.environ
    home = env.get("HOME") or env.get("USERPROFILE") or str(Path.home())
    if plat == "darwin":
        cfg = Path(home) / "Library" / "Application Support" / "UnityHub" / "secondaryInstallPath.json"
    elif plat.startswith("win"):
        appdata = env.get("APPDATA") or str(Path(home) / "AppData" / "Roaming")
        cfg = Path(appdata) / "UnityHub" / "secondaryInstallPath.json"
    else:
        xdg = env.get("XDG_CONFIG_HOME") or str(Path(home) / ".config")
        cfg = Path(xdg) / "UnityHub" / "secondaryInstallPath.json"

    reader = read_text or (lambda p: Path(p).read_text(encoding="utf-8"))
    try:
        raw = reader(str(cfg))
    except OSError:
        return None
    try:
        val = json.loads(raw)
    except ValueError:
        # The file may store a bare path string without JSON quoting.
        val = raw.strip()
    if isinstance(val, str) and val.strip():
        return val.strip()
    return None


def candidate_editor_paths(version: str,
                           explicit_editor: str | None = None,
                           platform: str | None = None,
                           environ: dict[str, str] | None = None,
                           read_text: Callable[[str], str] | None = None) -> list[str]:
    """Ordered candidate editor binary paths for a resolved version.

    Precedence:
      1. --editor (explicit_editor)
      2. $UNITY_EDITOR
      3. Per-OS Hub layout for <version>
      4. Hub secondaryInstallPath root, searched for <version>
    Nearest-patch fallback (precedence 5) is NOT enumerated here — it is handled
    by discover_editor(), which needs to inspect the filesystem.
    """
    plat = platform or sys.platform
    env = environ if environ is not None else os.environ
    relpath = editor_relpath(plat)
    out: list[str] = []

    if explicit_editor:
        out.append(explicit_editor)

    env_editor = env.get("UNITY_EDITOR")
    if env_editor:
        out.append(env_editor)

    for root in hub_roots(plat, env):
        out.append(str(Path(root) / version / relpath))

    sec = read_secondary_install_path(plat, env, read_text)
    if sec:
        out.append(str(Path(sec) / version / relpath))

    return out


def _default_exists(path: str) -> bool:
    return os.path.exists(path)


def _default_is_exec(path: str) -> bool:
    # On Windows the +x bit is meaningless; existence is enough.
    if sys.platform.startswith("win"):
        return os.path.isfile(path)
    return os.path.isfile(path) and os.access(path, os.X_OK)


def discover_editor(version: str,
                    explicit_editor: str | None = None,
                    platform: str | None = None,
                    environ: dict[str, str] | None = None,
                    exists: Callable[[str], bool] | None = None,
                    is_exec: Callable[[str], bool] | None = None,
                    list_dir: Callable[[str], list[str]] | None = None,
                    read_text: Callable[[str], str] | None = None) -> EditorSpec:
    """Resolve a Unity editor binary for the requested version.

    First existing + executable candidate wins (precedence 1-4 from
    candidate_editor_paths). If none match, fall back to the nearest patch
    *restricted to the same major.minor*: enumerate installed <version> dirs
    under every Hub/secondary root, keep only those whose (major, minor) equals
    the target's, pick the max by (patch, suffix). Never cross major.minor.

    Filesystem access is injectable (exists / is_exec / list_dir / read_text)
    so the whole function is hermetically unit-testable.

    Raises EditorNotFound(searched=[...]) carrying every absolute path probed.
    """
    plat = platform or sys.platform
    env = environ if environ is not None else os.environ
    _exists = exists or _default_exists
    _is_exec = is_exec or _default_is_exec
    _listdir = list_dir or (lambda d: os.listdir(d) if os.path.isdir(d) else [])
    relpath = editor_relpath(plat)
    searched: list[str] = []

    # Precedence 1-4: direct candidates.
    for cand in candidate_editor_paths(version, explicit_editor, plat, env, read_text):
        searched.append(cand)
        if _exists(cand) and _is_exec(cand):
            return EditorSpec(binary=cand, version=version)

    # Precedence 5: nearest-patch fallback, same major.minor only.
    target = parse_version(version)
    roots = list(hub_roots(plat, env))
    sec = read_secondary_install_path(plat, env, read_text)
    if sec:
        roots.append(sec)

    best: tuple[tuple[int, str], str, str] | None = None  # ((patch, suffix), binary, dir_version)
    for root in roots:
        try:
            entries = _listdir(root)
        except OSError:
            continue
        for name in entries:
            pv = parse_version(name)
            if (pv[0], pv[1]) != (target[0], target[1]):
                continue
            binary = str(Path(root) / name / relpath)
            searched.append(binary)
            if not (_exists(binary) and _is_exec(binary)):
                continue
            key = (pv[2], pv[3])
            if best is None or key > best[0]:
                best = (key, binary, name)

    if best is not None:
        return EditorSpec(binary=best[1], version=best[2])

    raise EditorNotFound(searched=searched)


# ===========================================================================
# Pure helpers: status-file discovery
# ===========================================================================
def newest_status_file(status_dir: str | Path,
                       glob_fn: Callable[[str], list[str]] | None = None,
                       mtime_fn: Callable[[str], float] | None = None) -> str | None:
    """Return the path to the newest unity-mcp-status-*.json under status_dir, or None."""
    pattern = str(Path(status_dir) / "unity-mcp-status-*.json")
    g = glob_fn or glob.glob
    files = list(g(pattern))
    if not files:
        return None
    m = mtime_fn or (lambda p: os.path.getmtime(p))
    try:
        return max(files, key=m)
    except OSError:
        # If stat races a deletion, fall back to lexical newest.
        return sorted(files)[-1]


def instance_id_from_status(status_file: str | Path, data: dict[str, Any] | None = None) -> str:
    """Derive the <name>@<hash> instance id from a status file path + payload.

    hash = the filename segment between 'unity-mcp-status-' and '.json'
    (= sha1(Application.dataPath)[:8]). Glob the newest file rather than
    recomputing the hash so the harness is robust to /Users vs /private/var
    symlink canonicalization. name = project root folder name, derived from the
    status payload's project_path (strip trailing /Assets) when available, else
    project_name, else "Unknown" — mirroring the C# project_name derivation.
    """
    fname = os.path.basename(str(status_file))
    h = fname
    if h.startswith("unity-mcp-status-"):
        h = h[len("unity-mcp-status-"):]
    if h.endswith(".json"):
        h = h[: -len(".json")]

    name = "Unknown"
    if data:
        project_path = data.get("project_path")
        if isinstance(project_path, str) and project_path:
            p = project_path.rstrip("/\\")
            if p.lower().endswith("assets"):
                p = p[:-6].rstrip("/\\")
            base = os.path.basename(p)
            if base:
                name = base
        if name == "Unknown":
            pn = data.get("project_name")
            if isinstance(pn, str) and pn:
                name = pn
    return f"{name}@{h}"


def port_from_status(data: dict[str, Any] | None) -> int | None:
    """Extract unity_port from a status payload, or None."""
    if not isinstance(data, dict):
        return None
    port = data.get("unity_port")
    return port if isinstance(port, int) else None


# ===========================================================================
# Pure helpers: log classification + redaction
# ===========================================================================
_LICENSE_RE = re.compile(
    r"No valid Unity"
    r"|License is not active"
    r"|cannot load ULF"
    r"|Signature element not found"
    r"|Token not found"
    r"|0 entitlement"
    r"|Entitlement.*(failed|denied)"
    r"|License (activation|return|renewal).*(failed|expired|denied)",
    re.IGNORECASE,
)
_COMPILE_RE = re.compile(
    r"error CS\d|Scripts have compiler errors|Compilation failed",
    re.IGNORECASE,
)
_READY_RE = re.compile(
    r"(Bridge|MCP(For)?Unity|AutoConnect).*(listening|ready|started|port|bound)",
    re.IGNORECASE,
)
_REDACT_RE = re.compile(r"(?i)((email|serial|license|password|token)\S*)")
_CS_ERROR_RE = re.compile(r"error CS\d")


def redact(text: str) -> str:
    """Redact secret-ish tokens from log echoes.

    Mirrors the CI sed idiom
    `sed -E 's/((email|serial|license|password|token)[^[:space:]]*)/[REDACTED]/Ig'`.
    """
    return _REDACT_RE.sub("[REDACTED]", text or "")


def classify_log(text: str, license_grace_elapsed: bool = True) -> str:
    """Classify an editor log tail.

    Returns one of: "license_fatal", "compile_fatal", "ready_ok", "none".
    Order: license > compile > ready > none. The license gate is suppressed
    until license_grace_elapsed is True (the caller passes whether
    boot_start + LICENSE_GRACE_S has passed) to tolerate transient
    Licensing::... chatter during warm-up.
    """
    if not text:
        return "none"
    if license_grace_elapsed and _LICENSE_RE.search(text):
        return "license_fatal"
    if _COMPILE_RE.search(text):
        return "compile_fatal"
    if _READY_RE.search(text):
        return "ready_ok"
    return "none"


# ===========================================================================
# Pure helpers: bridge_smoke-shaped response handling (mirrors bridge_smoke.py)
# ===========================================================================
def _ok(resp: Any) -> bool:
    """True when Unity reports success, tolerant of both response shapes."""
    if not isinstance(resp, dict):
        return False
    return resp.get("success") is True or resp.get("status") == "success"


def _result(resp: Any) -> dict:
    """Extract the payload dict from either response shape."""
    if isinstance(resp, dict):
        inner = resp.get("result")
        if isinstance(inner, dict):
            return inner
        if isinstance(inner, str):
            return {"message": inner}
        return resp
    return {}


def _message(resp: Any) -> str:
    """Best-effort human-readable message from a response."""
    if not isinstance(resp, dict):
        return repr(resp)
    return (
        str(resp.get("message"))
        if resp.get("message")
        else str(resp.get("error"))
        if resp.get("error")
        else str(_result(resp).get("message"))
        if _result(resp).get("message")
        else ""
    )


def _dig(resp: Any, key: str) -> Any:
    """Return the first value found for ``key`` anywhere in a nested response.

    Tolerates the transport wrapping the C# payload under result/data so contract
    checks depend on the field, not the exact envelope shape.
    """
    stack: list[Any] = [resp]
    while stack:
        cur = stack.pop()
        if isinstance(cur, dict):
            if key in cur:
                return cur[key]
            stack.extend(cur.values())
        elif isinstance(cur, list):
            stack.extend(cur)
    return None


# ===========================================================================
# Pure helpers: JUnit merge + exit aggregation
# ===========================================================================
def merge_junit(suites: list[JUnitSuite]) -> ET.ElementTree:
    """Merge JUnitSuites into a single <testsuites> ElementTree."""
    root = ET.Element("testsuites")
    total_tests = total_failures = total_skipped = 0
    total_time = 0.0
    for suite in suites:
        if suite is None:
            continue
        s_failures = sum(1 for c in suite.cases if c.failure is not None)
        s_skipped = sum(1 for c in suite.cases if c.skipped)
        s_time = sum(c.time_s for c in suite.cases)
        total_tests += len(suite.cases)
        total_failures += s_failures
        total_skipped += s_skipped
        total_time += s_time
        suite_el = ET.SubElement(
            root,
            "testsuite",
            {
                "name": suite.name,
                "tests": str(len(suite.cases)),
                "failures": str(s_failures),
                "skipped": str(s_skipped),
                "time": f"{s_time:.3f}",
            },
        )
        for case in suite.cases:
            case_el = ET.SubElement(
                suite_el,
                "testcase",
                {"name": case.name, "classname": suite.name, "time": f"{case.time_s:.3f}"},
            )
            if case.skipped:
                ET.SubElement(case_el, "skipped")
            if case.failure is not None:
                fail_el = ET.SubElement(case_el, "failure", {"message": case.failure[:200]})
                fail_el.text = case.failure
    root.set("tests", str(total_tests))
    root.set("failures", str(total_failures))
    root.set("skipped", str(total_skipped))
    root.set("time", f"{total_time:.3f}")
    return ET.ElementTree(root)


def aggregate_exit(outcomes: list[LegOutcome]) -> int:
    """Top-level exit code = max-severity by precedence 5 > 4 > 3 > 2 > 1 > 0.

    Setup/infra codes dominate test-result codes so CI can tell environment
    problems from regressions. A non-blocking leg (PlayMode without
    --strict-playmode) never raises the code above 0 on its own.
    """
    code = 0
    precedence = {5: 5, 4: 4, 3: 3, 2: 2, 1: 1, 0: 0}
    best_rank = -1
    for o in outcomes:
        if o.status not in ("fail", "error"):
            continue
        if not o.blocking:
            continue
        c = o.exit_code if o.exit_code else 1
        rank = precedence.get(c, 0)
        if rank > best_rank:
            best_rank = rank
            code = c
    return code


# ===========================================================================
# Argument parser (pure, importable, Unity-free)
# ===========================================================================
def build_arg_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(
        prog="local_harness.py",
        description="Local headless test harness: boot Unity, run smoke/EditMode/PlayMode legs, aggregate, teardown.",
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    p.add_argument(
        "--legs",
        default="smoke,editmode,playmode",
        help="Comma list of legs to run (subset of smoke,editmode,playmode).",
    )
    p.add_argument(
        "--project-path",
        default=DEFAULT_PROJECT_PATH,
        help="Unity project to boot (repo-relative or absolute). Default: TestProjects/UnityMCPTests.",
    )
    p.add_argument("--editor", default=None, help="Explicit Unity binary (discovery precedence 1).")
    p.add_argument("--unity-version", default=None, help="Override the resolved Unity version.")
    p.add_argument("--ci", action="store_true", help="Use DockerLauncher + repo .unity-mcp status dir; implies --no-warmup semantics.")
    p.add_argument("--status-dir", default=None, help="Status-file directory. Default: fresh tmp dir (local) / <workspace>/.unity-mcp (--ci).")
    p.add_argument("--reuse", action="store_true", help="Attach to an already-resident bridge via ~/.unity-mcp; owns_editor=False.")
    p.add_argument("--keep-alive", action="store_true", help="Leave the editor running after legs (no teardown of an owned editor).")
    p.add_argument("--no-warmup", action="store_true", help="Skip the warm-up phase.")
    p.add_argument("--strict-playmode", action="store_true", help="Promote a PlayMode failure to a blocking failure (exit 1).")
    p.add_argument("--native-editmode", action="store_true", help="Optional CI-parity native -runTests EditMode leg, serialized after resident teardown.")
    p.add_argument(
        "--junit",
        default="reports/junit-e2e-bridge.xml",
        help="Smoke JUnit path. EditMode/PlayMode write reports/junit-editmode.xml / reports/junit-playmode.xml alongside.",
    )
    p.add_argument("--reports", default=None, help="Reports directory. Default: dirname(--junit) or reports/.")
    p.add_argument("--boot-timeout", type=int, default=900, help="Warm-up + resident-boot budget (s).")
    p.add_argument("--bridge-wait", type=int, default=600, help="Bridge-ready budget (s).")
    p.add_argument(
        "--playmode-init-timeout",
        type=int,
        default=DEFAULT_PLAYMODE_INIT_TIMEOUT_MS,
        help="initTimeout (ms) passed verbatim to PlayMode run_tests.",
    )
    p.add_argument("--overall-timeout", type=int, default=2400, help="Wall-clock watchdog (s); on expiry kill owned PID, exit 2.")
    p.add_argument("--max-retries", type=int, default=8, help="Reload retries per wire command.")
    p.add_argument("--retry-ms", type=int, default=250, help="Reload retry delay (ms).")
    p.add_argument("--editor-arg", action="append", default=[], dest="editor_args", help="Opaque extra editor arg appended to the resident argv (repeatable).")
    return p


# Task-prose aliases for the canonical pure helpers (design spec §13 uses
# discover_editor / resolve_version / classify_log / aggregate_exit; the harness
# task wording uses these descriptive names). Both are importable + Unity-free.
resolve_editor_binary = discover_editor
resolve_unity_version = resolve_version
classify_editor_log = classify_log
aggregate_exit_code = aggregate_exit


ALLOWED_LEGS = ("smoke", "editmode", "playmode")


def parse_legs(legs: str) -> list[str]:
    """Normalize the --legs CSV into an ordered, de-duplicated list.

    Lenient by design -- unknown values are dropped (see test_drops_unknown_legs).
    The CLI entry point (main) validates raw input against ALLOWED_LEGS and
    rejects unknown values before calling this; keep this a pure normalizer.
    """
    seen: set[str] = set()
    out: list[str] = []
    for part in (legs or "").split(","):
        leg = part.strip().lower()
        if leg and leg in ALLOWED_LEGS and leg not in seen:
            seen.add(leg)
            out.append(leg)
    return out


# ===========================================================================
# Launcher seam (local vs docker). Inlined here so the harness is a single,
# self-contained, runnable file. Arg-builders are pure string assemblers.
# ===========================================================================
@dataclass
class Handle:
    """A live editor handle. Exactly one of (proc, container) is meaningful."""

    proc: Any = None  # subprocess.Popen for LocalLauncher
    container: str | None = None  # container name for DockerLauncher
    log_path: str | None = None
    pid: int | None = None


class LocalLauncher:
    """Boots a native Hub editor via detached Popen; PID-based liveness."""

    def __init__(self, args: argparse.Namespace):
        self.args = args

    def resolve_editor(self, project_path: Path) -> EditorSpec:
        version = self.args.unity_version or resolve_version(project_path)
        if not version:
            raise EditorNotFound(searched=[])
        return discover_editor(version, explicit_editor=self.args.editor)

    @staticmethod
    def warmup_argv(editor: str, project_path: Path, log_path: Path) -> list[str]:
        return [
            editor, "-batchmode", "-nographics", "-quit",
            "-projectPath", str(project_path),
            "-logFile", str(log_path),
        ]

    @staticmethod
    def resident_argv(editor: str, project_path: Path, log_path: Path,
                      extra_editor_args: list[str]) -> list[str]:
        return [
            editor, "-batchmode", "-nographics",
            "-projectPath", str(project_path),
            "-logFile", str(log_path),
            *list(extra_editor_args or []),
            "-executeMethod", BOOT_METHOD,
        ]

    @staticmethod
    def resident_env(base_env: dict[str, str], status_dir: Path) -> dict[str, str]:
        env = dict(base_env)
        env["UNITY_MCP_ALLOW_BATCH"] = "1"
        env["UNITY_MCP_STATUS_DIR"] = str(status_dir)
        return env

    def warmup(self, editor: str, project_path: Path, log_path: Path, timeout_s: int) -> int:
        argv = self.warmup_argv(editor, project_path, log_path)
        proc = subprocess.Popen(argv)
        try:
            return proc.wait(timeout=timeout_s)
        except subprocess.TimeoutExpired:
            proc.kill()
            proc.wait()
            return -1

    def launch(self, editor: str, project_path: Path, status_dir: Path, log_path: Path,
               extra_editor_args: list[str]) -> Handle:
        argv = self.resident_argv(editor, project_path, log_path, extra_editor_args)
        env = self.resident_env(os.environ, status_dir)
        kwargs: dict[str, Any] = {"env": env}
        if hasattr(os, "setsid"):
            kwargs["start_new_session"] = True
        elif sys.platform.startswith("win"):
            kwargs["creationflags"] = getattr(subprocess, "CREATE_NEW_PROCESS_GROUP", 0)
        proc = subprocess.Popen(argv, **kwargs)
        return Handle(proc=proc, log_path=str(log_path), pid=proc.pid)

    def is_alive(self, handle: Handle) -> bool:
        if handle.proc is not None:
            return handle.proc.poll() is None
        if handle.pid is None:
            return False
        try:
            os.kill(handle.pid, 0)
            return True
        except OSError:
            return False

    def tail_log(self, handle: Handle, n: int) -> str:
        if not handle.log_path:
            return ""
        try:
            with open(handle.log_path, "r", encoding="utf-8", errors="replace") as f:
                return "".join(f.readlines()[-n:])
        except OSError:
            return ""

    def fixup_permissions(self, status_dir: Path) -> None:
        return  # local: no-op

    def teardown(self, handle: Handle, grace_s: float = 10.0) -> None:
        proc = handle.proc
        pid = handle.pid
        try:
            if proc is not None:
                proc.terminate()
                try:
                    proc.wait(timeout=grace_s)
                    return
                except subprocess.TimeoutExpired:
                    proc.kill()
                    proc.wait()
                    return
            if pid is not None:
                os.kill(pid, signal.SIGTERM)
                deadline = time.time() + grace_s
                while time.time() < deadline:
                    try:
                        os.kill(pid, 0)
                    except OSError:
                        return
                    time.sleep(0.2)
                try:
                    os.kill(pid, signal.SIGKILL)
                except OSError:
                    pass
        except OSError:
            pass


class DockerLauncher:
    """Reproduces e2e-bridge.yml's docker run exactly; docker-based liveness."""

    CONTAINER = "unity-mcp"

    def __init__(self, args: argparse.Namespace):
        self.args = args

    def resolve_editor(self, project_path: Path) -> EditorSpec:
        # CI short-circuits to the fixed image entrypoint; never touches Hub.
        return EditorSpec(binary="/opt/unity/Editor/Unity", version=self.args.unity_version or "docker")

    @staticmethod
    def docker_run_argv(image: str, workspace: Path, project_path: Path, status_dir: Path,
                        log_path: str, extra_editor_args: list[str],
                        container: str = "unity-mcp",
                        runner_temp: str | None = None) -> list[str]:
        # When RUNNER_TEMP is present (GitHub Actions), mount the same license /
        # config / cache volumes the warm-up + activation steps populated so the
        # resident bridge container sees the staged ULF/EBL seat. Locally these
        # mounts are simply omitted.
        license_mounts: list[str] = []
        if runner_temp:
            rt = str(runner_temp)
            license_mounts = [
                "-v", f"{rt}/unity-config:/root/.config/unity3d",
                "-v", f"{rt}/unity-local:/root/.local/share/unity3d",
                "-v", f"{rt}/unity-cache:/root/.cache/unity3d",
            ]
        return [
            "docker", "run", "-d", "--name", container, "--network", "host",
            "-e", "HOME=/root",
            "-e", "UNITY_MCP_ALLOW_BATCH=1",
            "-e", f"UNITY_MCP_STATUS_DIR={status_dir}",
            "-e", "UNITY_MCP_BIND_HOST=127.0.0.1",
            "-v", f"{workspace}:{workspace}", "-w", str(workspace),
            *license_mounts,
            image,
            "/opt/unity/Editor/Unity", "-batchmode", "-nographics",
            "-logFile", log_path,
            "-projectPath", str(project_path),
            *list(extra_editor_args or []),
            "-executeMethod", BOOT_METHOD,
        ]

    def warmup(self, editor: str, project_path: Path, log_path: Path, timeout_s: int) -> int:
        return 0  # no-op in CI (YAML already warmed up)

    def launch(self, editor: str, project_path: Path, status_dir: Path, log_path: Path,
               extra_editor_args: list[str]) -> Handle:
        image = os.environ.get("UNITY_IMAGE", "")
        workspace = Path(os.environ.get("GITHUB_WORKSPACE", str(REPO_ROOT)))
        runner_temp = os.environ.get("RUNNER_TEMP")
        container_log = "/root/.config/unity3d/Editor.log"
        subprocess.run(["docker", "rm", "-f", self.CONTAINER],
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=False)
        argv = self.docker_run_argv(image, workspace, project_path, status_dir,
                                     container_log, extra_editor_args, self.CONTAINER,
                                     runner_temp)
        subprocess.run(argv, check=True)
        return Handle(container=self.CONTAINER, log_path=container_log)

    def is_alive(self, handle: Handle) -> bool:
        try:
            out = subprocess.run(
                ["docker", "inspect", "-f", "{{.State.Status}}", self.CONTAINER],
                capture_output=True, text=True, check=False,
            )
            return out.stdout.strip() == "running"
        except OSError:
            return False

    def tail_log(self, handle: Handle, n: int) -> str:
        try:
            out = subprocess.run(
                ["docker", "logs", "--tail", str(n), self.CONTAINER],
                capture_output=True, text=True, check=False,
            )
            return (out.stdout or "") + (out.stderr or "")
        except OSError:
            return ""

    def fixup_permissions(self, status_dir: Path) -> None:
        subprocess.run(["docker", "exec", self.CONTAINER, "chmod", "-R", "a+rwx", str(status_dir)],
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=False)

    def teardown(self, handle: Handle, grace_s: float = 10.0) -> None:
        subprocess.run(["docker", "rm", "-f", self.CONTAINER],
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=False)


def make_launcher(args: argparse.Namespace):
    return DockerLauncher(args) if args.ci else LocalLauncher(args)


# ===========================================================================
# Live functions (Unity-touching; guarded under main()).
# ===========================================================================
def _ensure_src_on_path() -> None:
    """Prepend <repo>/Server/src to sys.path (mirrors bridge_smoke.py)."""
    src = str(SERVER_SRC)
    if SERVER_SRC.is_dir() and src not in sys.path:
        sys.path.insert(0, src)


def _read_status(status_file: str) -> dict[str, Any] | None:
    try:
        with open(status_file, "r", encoding="utf-8") as f:
            return json.load(f)
    except (OSError, ValueError):
        return None


def _tcp_probe(port: int, timeout: float = 0.5) -> bool:
    try:
        with socket.create_connection(("127.0.0.1", int(port)), timeout):
            return True
    except OSError:
        return False


def wait_for_ready(launcher, handle: Handle, status_dir: Path, bridge_wait_s: int,
                   boot_start: float, deadline: float) -> ReadyInfo:
    """Poll status-file discovery + TCP probe up to bridge_wait_s.

    Liveness (launcher.is_alive) is the authoritative death signal: a slow-but-
    healthy cold boot may show zero discoverable instances for a while, which is
    tolerated. On editor death, classify the log tail -> raise SystemExit with
    the mapped exit code (4 license / 3 compile / else 2). On overall watchdog
    expiry -> SystemExit(2).
    """
    wait_deadline = time.time() + bridge_wait_s
    while True:
        now = time.time()
        if now > deadline:
            _redacted_tail(launcher, handle)
            raise SystemExit(2)
        if now > wait_deadline:
            print("::error:: bridge not ready before --bridge-wait deadline")
            _redacted_tail(launcher, handle)
            raise SystemExit(2)

        if not launcher.is_alive(handle):
            grace_elapsed = (now - boot_start) >= LICENSE_GRACE_S
            tail = launcher.tail_log(handle, 200)
            kind = classify_log(tail, license_grace_elapsed=grace_elapsed)
            print("::error:: editor process died during bridge wait")
            print(redact(tail))
            if kind == "license_fatal":
                raise SystemExit(4)
            if kind == "compile_fatal":
                raise SystemExit(3)
            raise SystemExit(2)

        status_file = newest_status_file(status_dir)
        if status_file:
            data = _read_status(status_file)
            port = port_from_status(data)
            if port and _tcp_probe(port):
                instance_id = instance_id_from_status(status_file, data)
                launcher.fixup_permissions(status_dir)
                return ReadyInfo(port=port, instance_id=instance_id, status_file=status_file)

        time.sleep(2)


def _redacted_tail(launcher, handle: Handle, n: int = 200) -> None:
    tail = launcher.tail_log(handle, n)
    if tail:
        print(redact(tail))


def _console_entries(resp: Any) -> list[Any]:
    """Pull read_console log entries out of either envelope shape.

    Non-paging read_console returns the entries as a BARE LIST under "data"
    (ReadConsole.HandleCommand -> SuccessResponse(message, entries)); paging
    returns them under data.items. Either may sit under a top-level "result".
    """
    body = _dig(resp, "data")
    if isinstance(body, list):
        return body
    if isinstance(body, dict) and isinstance(body.get("items"), list):
        return body["items"]
    dug = _dig(resp, "items")
    if isinstance(dug, list):
        return dug
    raw = resp.get("result") if isinstance(resp, dict) else None
    if isinstance(raw, list):
        return raw
    return []


def compile_probe(instance_id: str, max_retries: int, retry_ms: int, send=None) -> bool:
    """Run a read-only read_console probe before any UTF leg.

    Returns True if the project compiles (no `error CS\\d`), False if a compile
    error is detected. Driving raw run_tests bypasses the Python preflight()
    that would otherwise hang on a non-compiling project, so this probe guards
    the UTF legs explicitly. ``send`` is an injection seam for tests; in
    production it defaults to the real bridge wire.
    """
    if send is None:
        _ensure_src_on_path()
        from transport.legacy.unity_connection import send_command_with_retry as send

    try:
        resp = send(
            "read_console",
            {"action": "get", "types": ["error"], "count": "200", "include_stacktrace": False},
            instance_id=instance_id,
            max_retries=max_retries,
            retry_ms=retry_ms,
            retry_on_reload=True,
        )
    except Exception:
        # A timed-out / errored probe is inconclusive; do not block the UTF legs.
        return True
    if not _ok(resp):
        # An errored probe is inconclusive; do not block on it.
        return True

    entries = _console_entries(resp)
    for e in entries:
        if isinstance(e, dict):
            msg = str(e.get("message") or "")
        elif isinstance(e, str):
            msg = e
        else:
            msg = ""
        if _CS_ERROR_RE.search(msg):
            return False
    return True


def run_smoke_leg(instance_id: str, junit_path: Path, max_retries: int, retry_ms: int,
                  deadline: float | None = None, python_exe: str | None = None) -> LegOutcome:
    """Run bridge_smoke.py as a subprocess; honor its 0/1/2 exit contract.

    Bounded by the overall deadline so a wedged smoke run cannot outlive the
    --overall-timeout budget (the watchdog is the hard backstop; passing a
    timeout here reaps the child cleanly first). retry_ms is threaded through so
    all three legs share the configured reload-retry delay.
    """
    smoke = REPO_ROOT / "Server" / "tests" / "e2e" / "bridge_smoke.py"
    py = python_exe or sys.executable
    argv = [py, str(smoke), "--instance", instance_id, "--junit", str(junit_path),
            "--max-retries", str(max_retries), "--retry-ms", str(retry_ms)]
    timeout = max(1.0, deadline - time.time()) if deadline is not None else None
    try:
        rc = subprocess.run(argv, check=False, timeout=timeout).returncode
    except subprocess.TimeoutExpired:
        return LegOutcome("smoke", "error", blocking=True,
                          detail="bridge smoke timed out (overall budget)", exit_code=2)
    if rc == 0:
        return LegOutcome("smoke", "pass", blocking=True, detail="bridge smoke passed", exit_code=0)
    if rc == 1:
        return LegOutcome("smoke", "fail", blocking=True, detail="bridge smoke assertion regression", exit_code=1)
    # rc == 2 (or anything else) -> bridge unreachable / setup failure.
    return LegOutcome("smoke", "error", blocking=True, detail="no bridge reachable", exit_code=2)


def _start_utf(send, mode: str, instance_id: str, init_timeout_ms: int | None,
               max_retries: int, retry_ms: int) -> tuple[str | None, dict[str, Any] | Any]:
    """Issue run_tests; return (job_id, raw_start_response). Gates on result.success."""
    params: dict[str, Any] = {"mode": mode, "includeFailedTests": True}
    if init_timeout_ms is not None:
        params["initTimeout"] = init_timeout_ms
    # tests_running back-off: a "tests already running" reply is an ErrorResponse
    # (success:false), so it must be detected BEFORE the _ok() gate, not after.
    for _ in range(5):
        try:
            start = send("run_tests", params, instance_id=instance_id,
                         max_retries=max_retries, retry_ms=retry_ms, retry_on_reload=True)
        except Exception:
            # Transport hiccup starting the job (editor briefly busy / reloading);
            # back off and retry rather than crashing.
            time.sleep(min(float(retry_ms) / 1000.0 * 2, 2.0))
            continue
        err = None
        if isinstance(start, dict):
            err = start.get("error") or start.get("code") or _dig(start, "error")
        if err == "tests_running":
            back = _dig(start, "retry_after_ms") or 1000
            time.sleep(min(float(back) / 1000.0, 5.0))
            continue
        if not _ok(start):
            return None, start
        job_id = _dig(start, "job_id")
        if isinstance(job_id, (str, int)):
            return str(job_id), start
        return None, start
    return None, {"error": "tests_running (exhausted)"}


def _poll_utf(send, job_id: str, instance_id: str, deadline: float,
              max_retries: int, retry_ms: int) -> dict[str, Any] | Any:
    """Poll get_test_job until terminal {succeeded, failed} or deadline.

    A returned MCPResponse / reason=="reloading" / hint=="retry" is treated as
    non-terminal (keep polling). Returns the last response (terminal or a wedge
    marker dict when the deadline expires).
    """
    while time.time() < deadline:
        try:
            poll = send("get_test_job", {"job_id": job_id, "includeFailedTests": True},
                        instance_id=instance_id, max_retries=max_retries, retry_ms=retry_ms,
                        retry_on_reload=True)
        except Exception:
            # Unity blocks its main thread while running tests, so get_test_job can
            # time out mid-run. That is NOT terminal -- keep polling until the editor
            # frees up or the deadline expires (rather than crashing the harness).
            time.sleep(2)
            continue
        if not isinstance(poll, dict):
            time.sleep(2)
            continue
        reason = _dig(poll, "reason")
        hint = _dig(poll, "hint")
        if reason == "reloading" or hint == "retry":
            time.sleep(2)
            continue
        status = _dig(poll, "status")
        if status in ("succeeded", "failed"):
            return poll
        time.sleep(2)
    return {"_wedge": True}


def _outcome_from_terminal(name: str, mode: str, terminal: dict[str, Any] | Any,
                           blocking: bool) -> LegOutcome:
    """Map a terminal get_test_job response into a LegOutcome + JUnit suite."""
    status = _dig(terminal, "status")
    suite = JUnitSuite(name=name)

    if isinstance(terminal, dict) and terminal.get("_wedge"):
        suite.cases.append(JUnitCase(name=f"{mode}.wedge", failure="no terminal status within budget"))
        return LegOutcome(name, "fail", blocking=blocking, detail="wedge (no terminal status)",
                          exit_code=1, junit_suite=suite)

    if status == "succeeded":
        result = _dig(terminal, "result") or {}
        summary = (result.get("summary") if isinstance(result, dict) else None) or {}
        total = int(summary.get("total", 0) or 0)
        passed = int(summary.get("passed", 0) or 0)
        failed = int(summary.get("failed", 0) or 0)
        skipped = int(summary.get("skipped", 0) or 0)
        duration = float(summary.get("durationSeconds", 0.0) or 0.0)
        rows = result.get("results") if isinstance(result, dict) else None
        if isinstance(rows, list) and rows:
            for r in rows:
                if not isinstance(r, dict):
                    continue
                rname = str(r.get("fullName") or r.get("name") or f"{mode}.test")
                rtime = float(r.get("durationSeconds", 0.0) or 0.0)
                state = str(r.get("state") or "")
                if state.lower() in ("failed", "error"):
                    fmsg = str(r.get("message") or "") + "\n" + str(r.get("stackTrace") or "")
                    suite.cases.append(JUnitCase(name=rname, time_s=rtime, failure=fmsg.strip()))
                elif state.lower() in ("skipped", "ignored", "inconclusive"):
                    suite.cases.append(JUnitCase(name=rname, time_s=rtime, skipped=True))
                else:
                    suite.cases.append(JUnitCase(name=rname, time_s=rtime))
        else:
            # No per-test rows: synthesize from the summary.
            for i in range(passed):
                suite.cases.append(JUnitCase(name=f"{mode}.passed.{i}"))
            for i in range(failed):
                suite.cases.append(JUnitCase(name=f"{mode}.failed.{i}", failure="failed (no detail)"))
            for i in range(skipped):
                suite.cases.append(JUnitCase(name=f"{mode}.skipped.{i}", skipped=True))
            if not suite.cases and total == 0:
                suite.cases.append(JUnitCase(name=f"{mode}.empty", time_s=duration))
        if failed > 0:
            return LegOutcome(name, "fail", blocking=blocking,
                              detail=f"{failed}/{total} {mode} tests failed", exit_code=1,
                              junit_suite=suite)
        return LegOutcome(name, "pass", blocking=blocking,
                          detail=f"{passed}/{total} {mode} tests passed", exit_code=0,
                          junit_suite=suite)

    # status == "failed": data.result is null; surface error + capped failures.
    error = _dig(terminal, "error") or "test job failed"
    failures = _dig(terminal, "failures_so_far") or []
    detail = str(error)
    fail_text = detail
    if isinstance(failures, list) and failures:
        for fr in failures:
            if isinstance(fr, dict):
                fail_text += "\n  - " + str(fr.get("full_name") or "") + ": " + str(fr.get("message") or "")
    suite.cases.append(JUnitCase(name=f"{mode}.job", failure=fail_text))
    return LegOutcome(name, "fail", blocking=blocking, detail=detail, exit_code=1, junit_suite=suite)


def run_utf_leg(mode: str, instance_id: str, blocking: bool, deadline: float,
                max_retries: int, retry_ms: int, init_timeout_ms: int | None = None) -> LegOutcome:
    """Drive one EditMode/PlayMode leg over the raw run_tests/get_test_job wire."""
    _ensure_src_on_path()
    from transport.legacy.unity_connection import send_command_with_retry as send

    name = "editmode" if mode == "EditMode" else "playmode"
    job_id, start = _start_utf(send, mode, instance_id, init_timeout_ms, max_retries, retry_ms)
    if job_id is None:
        suite = JUnitSuite(name=name, cases=[JUnitCase(name=f"{name}.start", failure=_message(start) or "run_tests start failed")])
        return LegOutcome(name, "fail", blocking=blocking, detail="run_tests start failed", exit_code=1, junit_suite=suite)
    terminal = _poll_utf(send, job_id, instance_id, deadline, max_retries, retry_ms)
    return _outcome_from_terminal(name, mode, terminal, blocking)


def _ensure_clean_editmode(send, instance_id: str, max_retries: int, retry_ms: int,
                           deadline: float) -> None:
    """Best-effort: wait until no PlayMode/test job is running (S0)."""
    end = min(time.time() + 30, deadline)
    while time.time() < end:
        try:
            resp = send("get_test_job", {"job_id": ""}, instance_id=instance_id,
                        max_retries=max_retries, retry_ms=retry_ms, retry_on_reload=True)
        except Exception:
            # Editor busy/unresponsive; best-effort wait, treat as still settling.
            time.sleep(2)
            continue
        status = _dig(resp, "status")
        if status not in ("running",):
            return
        time.sleep(2)


def run_playmode_with_retry(instance_id: str, deadline: float, max_retries: int, retry_ms: int,
                            init_timeout_ms: int, strict: bool,
                            relaunch: Callable[[], str] | None = None) -> LegOutcome:
    """PlayMode state machine: start, poll, classify-can-rerun, retry ONCE.

    Non-blocking by default; --strict-playmode promotes failure to blocking.
    On a "can rerun" failure (error contains 'failed to initialize') or a wedge,
    re-establish clean EditMode and repeat once. A wedge may relaunch the editor
    (honoring the socket-release delay) before the single retry.
    """
    _ensure_src_on_path()
    from transport.legacy.unity_connection import send_command_with_retry as send

    blocking = bool(strict)

    def attempt(inst: str) -> LegOutcome:
        _ensure_clean_editmode(send, inst, max_retries, retry_ms, deadline)
        job_id, start = _start_utf(send, "PlayMode", inst, init_timeout_ms, max_retries, retry_ms)
        if job_id is None:
            suite = JUnitSuite(name="playmode", cases=[JUnitCase(name="playmode.start", failure=_message(start) or "run_tests start failed")])
            return LegOutcome("playmode", "fail", blocking=blocking, detail="run_tests start failed", exit_code=1, junit_suite=suite)
        terminal = _poll_utf(send, job_id, inst, deadline, max_retries, retry_ms)
        return _outcome_from_terminal("playmode", "PlayMode", terminal, blocking)

    first = attempt(instance_id)
    if first.status == "pass":
        return first

    error_text = (first.detail or "").lower()
    can_rerun = ("failed to initialize" in error_text) or ("wedge" in error_text)
    if not can_rerun:
        return first

    # A wedge may need the editor relaunched (respecting the socket-release delay).
    inst = instance_id
    if "wedge" in error_text and relaunch is not None:
        time.sleep(SOCKET_RELEASE_MS / 1000.0)
        inst = relaunch()

    second = attempt(inst)
    second.detail = f"retry-once: {second.detail}"
    return second


def write_reports(junit_path: Path, reports_dir: Path, outcomes: list[LegOutcome]) -> None:
    """Write merged + per-leg JUnit files: smoke -> --junit, others alongside."""
    reports_dir.mkdir(parents=True, exist_ok=True)
    per_leg_file = {
        "smoke": junit_path,
        "editmode": reports_dir / "junit-editmode.xml",
        "playmode": reports_dir / "junit-playmode.xml",
    }
    for o in outcomes:
        if o.junit_suite is None:
            continue
        target = per_leg_file.get(o.name, reports_dir / f"junit-{o.name}.xml")
        merge_junit([o.junit_suite]).write(str(target), encoding="utf-8", xml_declaration=True)
    # Also write a combined report.
    suites = [o.junit_suite for o in outcomes if o.junit_suite is not None]
    if suites:
        merge_junit(suites).write(str(reports_dir / "junit-all.xml"), encoding="utf-8", xml_declaration=True)


def _print_summary(outcomes: list[LegOutcome], exit_code: int) -> None:
    print("== local harness summary ==")
    for o in outcomes:
        tag = {"pass": "PASS", "fail": "FAIL", "skip": "SKIP", "error": "ERROR"}.get(o.status, o.status.upper())
        block = "blocking" if o.blocking else "non-blocking"
        print(f"  [{tag}] {o.name} ({block}) -- {o.detail}")
    print(f"== exit {exit_code} ==")


def main(argv: list[str] | None = None) -> int:
    args = build_arg_parser().parse_args(argv)

    requested_legs = [p.strip().lower() for p in (args.legs or "").split(",") if p.strip()]
    invalid_legs = [leg for leg in requested_legs if leg not in ALLOWED_LEGS]
    if invalid_legs:
        print(f"::error:: --legs included invalid value(s): {', '.join(invalid_legs)} "
              f"(allowed: {', '.join(ALLOWED_LEGS)})")
        return 2
    legs = parse_legs(args.legs)
    if not legs:
        print(f"::error:: --legs did not include any valid values (allowed: {', '.join(ALLOWED_LEGS)})")
        return 2
    if args.ci:
        args.no_warmup = True
        if not os.environ.get("UNITY_IMAGE"):
            print("::error:: --ci requires the UNITY_IMAGE environment variable "
                  "(the unityci/editor image to run the headless Editor in)")
            return 2

    # Resolve project path (repo-relative or absolute).
    project_path = Path(args.project_path)
    if not project_path.is_absolute():
        project_path = (REPO_ROOT / project_path).resolve()

    # Reports / JUnit geometry.
    junit_path = Path(args.junit)
    if not junit_path.is_absolute():
        junit_path = (REPO_ROOT / junit_path).resolve()
    reports_dir = Path(args.reports).resolve() if args.reports else junit_path.parent

    # Status-dir + isolation. Default local: fresh tmp dir; --ci / --status-dir override.
    owns_status_dir = False
    if args.status_dir:
        sd = Path(args.status_dir)
        status_dir = sd if sd.is_absolute() else (REPO_ROOT / sd).resolve()
        status_dir.mkdir(parents=True, exist_ok=True)
    elif args.ci:
        status_dir = (REPO_ROOT / ".unity-mcp").resolve()
        status_dir.mkdir(parents=True, exist_ok=True)
    elif args.reuse:
        status_dir = Path.home() / ".unity-mcp"
    else:
        status_dir = Path(tempfile.mkdtemp(prefix="unity-mcp-harness-"))
        owns_status_dir = True

    launcher = make_launcher(args)
    boot_start = time.time()
    deadline = boot_start + args.overall_timeout

    handle: Handle | None = None
    owns_editor = not (args.reuse or args.keep_alive)
    outcomes: list[LegOutcome] = []

    def do_teardown() -> None:
        # Only kill the editor we started; clean only our own status files.
        if handle is not None and owns_editor and not args.keep_alive:
            try:
                launcher.teardown(handle)
            except Exception:
                pass
        if owns_status_dir:
            try:
                for p in glob.glob(str(status_dir / "unity-mcp-status-*.json")):
                    try:
                        os.remove(p)
                    except OSError:
                        pass
                shutil.rmtree(status_dir, ignore_errors=True)
            except OSError:
                pass

    def _signal_handler(signum, frame):  # pragma: no cover - signal path
        do_teardown()
        raise SystemExit(2)

    for sig in (signal.SIGINT, signal.SIGTERM):
        try:
            signal.signal(sig, _signal_handler)
        except (ValueError, OSError):
            pass

    # Hard wall-clock watchdog: the polled deadlines inside the wait/poll loops do
    # not cover blocking subprocess/socket calls (smoke subprocess, compile probe),
    # so a background daemon enforces --overall-timeout across ALL phases.
    _watchdog_stop = threading.Event()

    def _watchdog() -> None:  # pragma: no cover - timing/daemon path
        remaining = deadline - time.time()
        if remaining > 0:
            _watchdog_stop.wait(remaining)
        if _watchdog_stop.is_set():
            return
        print(f"::error:: overall watchdog timed out after {args.overall_timeout}s -- killing editor", flush=True)
        try:
            do_teardown()
        finally:
            os._exit(2)

    threading.Thread(target=_watchdog, name="harness-watchdog", daemon=True).start()

    try:
        # --- Reuse path: attach to a resident bridge via ~/.unity-mcp ---
        if args.reuse:
            # Read the user's default registry without our own STATUS_DIR override.
            prev = os.environ.pop("UNITY_MCP_STATUS_DIR", None)
            try:
                status_file = _find_reuse_status(status_dir, project_path)
            finally:
                if prev is not None:
                    os.environ["UNITY_MCP_STATUS_DIR"] = prev
            if not status_file:
                print(f"::error:: --reuse: no resident bridge found for {project_path} under {status_dir}")
                return 2
            data = _read_status(status_file)
            port = port_from_status(data)
            if not port or not _tcp_probe(port):
                print(f"::error:: --reuse: resident bridge for {project_path} is not reachable")
                return 2
            os.environ["UNITY_MCP_STATUS_DIR"] = str(status_dir)
            instance_id = instance_id_from_status(status_file, data)
            ready = ReadyInfo(port=port, instance_id=instance_id, status_file=status_file)
        else:
            # --- Boot path ---
            try:
                spec = launcher.resolve_editor(project_path)
            except EditorNotFound as e:
                searched = e.searched if e.searched else ["<no candidate paths>"]
                print("::error:: no matching Unity editor found")
                for s in searched:
                    print(f"  searched: {s}")
                want = args.unity_version or resolve_version(project_path) or "<unknown>"
                maj_min = ".".join(str(x) for x in parse_version(want)[:2])
                print(f"  install a matching {maj_min}.x editor or pass --editor (wanted {want})")
                return 5

            os.environ["UNITY_MCP_STATUS_DIR"] = str(status_dir)

            # Phase 1 -- warm-up (skip with --no-warmup / --ci).
            if not args.no_warmup:
                warmup_log = status_dir / "warmup.log"
                rc = launcher.warmup(spec.binary, project_path, warmup_log, args.boot_timeout)
                if rc not in (0,):
                    tail = launcher.tail_log(Handle(log_path=str(warmup_log)), 200)
                    kind = classify_log(tail, license_grace_elapsed=(time.time() - boot_start) >= LICENSE_GRACE_S)
                    if kind == "license_fatal":
                        print(redact(tail))
                        return 4
                    if kind == "compile_fatal":
                        print(redact(tail))
                        return 3
                    # Non-zero warm-up without a clear signal: continue to resident boot.

            # Phase 2 -- resident (NO -quit).
            editor_log = status_dir / "editor.log"
            handle = launcher.launch(spec.binary, project_path, status_dir, editor_log, args.editor_args)
            ready = wait_for_ready(launcher, handle, status_dir, args.bridge_wait, boot_start, deadline)
            instance_id = ready.instance_id

        # Pin the instance so smoke + UTF target our own editor.
        os.environ["UNITY_MCP_DEFAULT_INSTANCE"] = instance_id
        print(f"== bridge ready: instance={instance_id} port={ready.port} ==")

        # --- Compile probe before any UTF leg (exit 3 on compile failure) ---
        wants_utf = ("editmode" in legs) or ("playmode" in legs)
        compile_ok = True
        if wants_utf:
            compile_ok = compile_probe(instance_id, args.max_retries, args.retry_ms)
            if not compile_ok:
                print("::error:: project does not compile -- skipping UTF legs")

        # --- Smoke leg ---
        if "smoke" in legs:
            outcomes.append(run_smoke_leg(instance_id, junit_path, args.max_retries,
                                          args.retry_ms, deadline=deadline))

        # --- EditMode leg ---
        if "editmode" in legs:
            if not compile_ok:
                outcomes.append(LegOutcome("editmode", "fail", blocking=True,
                                           detail="project does not compile", exit_code=3))
            else:
                outcomes.append(run_utf_leg("EditMode", instance_id, blocking=True,
                                            deadline=deadline, max_retries=args.max_retries,
                                            retry_ms=args.retry_ms))

        # --- PlayMode leg (default-ON, NON-BLOCKING unless --strict-playmode) ---
        if "playmode" in legs:
            if not compile_ok:
                outcomes.append(LegOutcome("playmode", "fail", blocking=bool(args.strict_playmode),
                                           detail="project does not compile", exit_code=3))
            else:
                def _relaunch() -> str:
                    nonlocal handle, ready, instance_id
                    if handle is not None and owns_editor:
                        launcher.teardown(handle)
                    time.sleep(SOCKET_RELEASE_MS / 1000.0)
                    editor_log = status_dir / "editor.log"
                    handle = launcher.launch(spec.binary, project_path, status_dir, editor_log, args.editor_args)
                    ready = wait_for_ready(launcher, handle, status_dir, args.bridge_wait, time.time(), deadline)
                    instance_id = ready.instance_id
                    os.environ["UNITY_MCP_DEFAULT_INSTANCE"] = instance_id
                    return instance_id

                relaunch = _relaunch if (owns_editor and not args.reuse) else None
                outcomes.append(run_playmode_with_retry(
                    instance_id, deadline, args.max_retries, args.retry_ms,
                    args.playmode_init_timeout, bool(args.strict_playmode), relaunch=relaunch))

        # Aggregate + write reports.
        write_reports(junit_path, reports_dir, outcomes)
        exit_code = aggregate_exit(outcomes)
        # If a compile failure surfaced, it dominates (precedence keeps 3).
        _print_summary(outcomes, exit_code)
        return exit_code

    except SystemExit as e:
        return int(e.code) if isinstance(e.code, int) else 2
    finally:
        _watchdog_stop.set()
        do_teardown()


def _norm_project_root(p: str) -> str:
    """Normalize a project path for comparison: strip a trailing Assets, case, sep."""
    p = p.rstrip("/\\")
    if p.lower().endswith("assets"):
        p = p[:-6].rstrip("/\\")
    return os.path.normcase(os.path.normpath(p))


def _find_reuse_status(status_dir: Path, project_path: Path) -> str | None:
    """Find a status file under status_dir whose project root matches project_path.

    Matches on the full normalized project root (not just the leaf folder name) so
    two projects sharing a folder name never attach to the wrong resident editor.
    Refuses (returns None) rather than attach to a non-matching instance.
    """
    want = _norm_project_root(str(project_path))
    files = sorted(
        glob.glob(str(status_dir / "unity-mcp-status-*.json")),
        key=lambda p: os.path.getmtime(p) if os.path.exists(p) else 0,
        reverse=True,
    )
    for f in files:
        data = _read_status(f)
        if not data:
            continue
        pp = data.get("project_path") or ""
        if isinstance(pp, str) and pp and _norm_project_root(pp) == want:
            return f
    return None  # refuse rather than attach to a non-matching instance


if __name__ == "__main__":
    raise SystemExit(main())
