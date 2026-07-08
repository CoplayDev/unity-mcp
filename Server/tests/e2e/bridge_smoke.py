#!/usr/bin/env python3
"""Deterministic, no-LLM end-to-end smoke test for the Python<->Unity bridge.

This drives a LIVE Unity Editor (booted in CI via
``McpForUnity.Editor.McpCiBoot.StartStdioForCi``) over the same wire path the
real MCP server uses -- ``send_command_with_retry`` from
``transport.legacy.unity_connection`` -- and asserts exact response fields. It
replaces the LLM agent in ``claude-nl-suite.yml`` with a fixed script so the
Python->C# request/response contract is gated on every PR without an API key.

Run locally against a running Unity Editor with the MCP bridge active::

    cd Server
    uv run python tests/e2e/bridge_smoke.py

Exit codes:
    0  all steps passed
    1  a step failed an assertion (real bridge regression)
    2  no Unity bridge reachable (setup problem, not a contract failure)
"""
from __future__ import annotations

import argparse
import os
import sys
import time
import uuid
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Callable

# Make the server's ``src`` importable whether run from repo root or Server/.
_SRC = Path(__file__).resolve().parents[2] / "src"
if _SRC.is_dir() and str(_SRC) not in sys.path:
    sys.path.insert(0, str(_SRC))

from transport.legacy.unity_connection import send_command_with_retry  # noqa: E402


class BridgeUnavailable(Exception):
    """Raised when no Unity instance can be reached at all."""


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
    if not isinstance(resp, dict):
        return repr(resp)
    return str(resp.get("message") or resp.get("error") or _result(resp).get("message") or "")


def _dig(resp: Any, key: str) -> Any:
    """Return the first value found for ``key`` anywhere in a nested response.

    Tolerates the transport wrapping the C# payload under ``result``/``data`` so
    the contract checks depend on the field, not the exact envelope shape.
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


@dataclass
class Step:
    name: str
    command: str
    params: dict
    # check(resp) -> None; raise AssertionError on failure.
    check: Callable[[Any], None]
    retry_on_reload: bool = True


@dataclass
class StepResult:
    name: str
    passed: bool
    detail: str
    elapsed_s: float


# A unique tag so repeated runs (and parallel CI shards) never collide.
_RUN = uuid.uuid4().hex[:8]
GO_EMPTY = f"MCP_E2E_Empty_{_RUN}"
GO_CUBE = f"MCP_E2E_Cube_{_RUN}"


def _assert(cond: bool, msg: str) -> None:
    if not cond:
        raise AssertionError(msg)


def build_steps() -> list[Step]:
    """The ordered contract every bridge build must satisfy."""

    # Shared across steps so the find/cleanup checks can assert against the exact
    # object the create step made. find_gameobjects returns instance IDs only
    # (never names), so a name-substring check would never match.
    created: dict[str, int] = {}

    def check_console(resp: Any) -> None:
        _assert(_ok(resp), f"read_console did not succeed: {_message(resp)}")

    def check_create_empty(resp: Any) -> None:
        _assert(_ok(resp), f"create empty GameObject failed: {_message(resp)}")
        gid = _dig(resp, "instanceID")
        _assert(isinstance(gid, int), f"create did not return an int instanceID: {_message(resp)}")
        created["empty"] = gid

    def check_found(resp: Any) -> None:
        _assert(_ok(resp), f"find_gameobjects failed: {_message(resp)}")
        ids = _dig(resp, "instanceIDs") or []
        _assert(
            created.get("empty") in ids,
            f"created object '{GO_EMPTY}' (id={created.get('empty')}) not in find results {ids}",
        )

    def check_create_cube(resp: Any) -> None:
        _assert(_ok(resp), f"create primitive Cube failed: {_message(resp)}")

    def check_delete(resp: Any) -> None:
        _assert(_ok(resp), f"delete failed: {_message(resp)}")

    def check_gone(resp: Any) -> None:
        _assert(_ok(resp), f"post-delete find_gameobjects failed: {_message(resp)}")
        ids = _dig(resp, "instanceIDs") or []
        _assert(
            created.get("empty") not in ids,
            f"'{GO_EMPTY}' (id={created.get('empty')}) still present after delete -- cleanup did not take effect",
        )

    return [
        Step("read_console_baseline", "read_console",
             {"action": "get", "count": "5", "include_stacktrace": False}, check_console),
        Step("create_empty_gameobject", "manage_gameobject",
             {"action": "create", "name": GO_EMPTY}, check_create_empty),
        Step("find_created_gameobject", "find_gameobjects",
             {"searchMethod": "by_name", "searchTerm": GO_EMPTY}, check_found),
        Step("create_primitive_with_component", "manage_gameobject",
             {"action": "create", "name": GO_CUBE, "primitiveType": "Cube",
              "componentsToAdd": ["Rigidbody"]}, check_create_cube),
        Step("delete_cube", "manage_gameobject",
             {"action": "delete", "target": GO_CUBE, "searchMethod": "by_name"}, check_delete),
        Step("delete_empty", "manage_gameobject",
             {"action": "delete", "target": GO_EMPTY, "searchMethod": "by_name"}, check_delete),
        Step("verify_cleanup", "find_gameobjects",
             {"searchMethod": "by_name", "searchTerm": GO_EMPTY}, check_gone),
    ]


def run(instance_id: str | None, max_retries: int, retry_ms: int) -> list[StepResult]:
    results: list[StepResult] = []
    for step in build_steps():
        t0 = time.time()
        try:
            resp = send_command_with_retry(
                step.command, step.params,
                instance_id=instance_id, max_retries=max_retries,
                retry_ms=retry_ms, retry_on_reload=step.retry_on_reload,
            )
        except Exception as exc:  # connection refused, timeout, etc.
            elapsed = time.time() - t0
            # A failure on the very first call usually means no bridge at all.
            if not results:
                raise BridgeUnavailable(str(exc)) from exc
            results.append(StepResult(step.name, False, f"transport error: {exc}", elapsed))
            break
        elapsed = time.time() - t0
        try:
            step.check(resp)
            results.append(StepResult(step.name, True, "ok", elapsed))
        except AssertionError as err:
            results.append(StepResult(step.name, False, str(err), elapsed))
            # Keep going so cleanup deletes still run, but record the failure.
    return results


def write_junit(path: Path, results: list[StepResult]) -> None:
    import xml.sax.saxutils as sx
    failures = sum(0 if r.passed else 1 for r in results)
    total_time = sum(r.elapsed_s for r in results)
    cases = []
    for r in results:
        body = "" if r.passed else f"<failure message={sx.quoteattr(r.detail)}>{sx.escape(r.detail)}</failure>"
        cases.append(
            f'  <testcase classname="UnityMCP.E2E.Bridge" name={sx.quoteattr(r.name)} '
            f'time="{r.elapsed_s:.3f}">{body}</testcase>'
        )
    xml = (
        '<?xml version="1.0" encoding="UTF-8"?>\n'
        f'<testsuites><testsuite name="UnityMCP.E2E.Bridge" tests="{len(results)}" '
        f'failures="{failures}" errors="0" skipped="0" time="{total_time:.3f}">\n'
        + "\n".join(cases)
        + "\n</testsuite></testsuites>\n"
    )
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(xml, encoding="utf-8")


def main() -> int:
    ap = argparse.ArgumentParser(description="Deterministic Unity bridge E2E smoke test")
    ap.add_argument("--instance", default=os.environ.get("UNITY_MCP_DEFAULT_INSTANCE") or None,
                    help="Unity instance id (name, hash, name@hash). Default: env or auto-discover.")
    ap.add_argument("--max-retries", type=int, default=8, help="Reload retries per command")
    ap.add_argument("--retry-ms", type=int, default=250, help="Delay between reload retries (ms)")
    ap.add_argument("--junit", default=os.environ.get("E2E_JUNIT_OUT"),
                    help="Optional path to write a JUnit XML report")
    args = ap.parse_args()

    instance = args.instance.strip() if isinstance(args.instance, str) else None
    print(f"== Unity bridge E2E smoke (run={_RUN}, instance={instance or 'auto'}) ==", flush=True)

    try:
        results = run(instance, args.max_retries, args.retry_ms)
    except BridgeUnavailable as exc:
        print(f"::error::No Unity bridge reachable: {exc}", flush=True)
        print("Is a Unity Editor running with the MCP bridge active? "
              "(set UNITY_MCP_STATUS_DIR / UNITY_MCP_DEFAULT_INSTANCE for CI)", flush=True)
        return 2

    if args.junit:
        write_junit(Path(args.junit), results)

    failed = [r for r in results if not r.passed]
    for r in results:
        status = "PASS" if r.passed else "FAIL"
        print(f"  [{status}] {r.name} ({r.elapsed_s:.2f}s){'' if r.passed else ' -- ' + r.detail}", flush=True)
    print(f"== {len(results) - len(failed)}/{len(results)} passed ==", flush=True)
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
