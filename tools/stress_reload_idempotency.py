#!/usr/bin/env python3
"""Exactly-once stress test: mutating MCP tool calls racing Unity domain reloads.

Acts as a real MCP client (fastmcp Client) against the MCP for Unity server, over stdio
(spawns ``Server/src/main.py --transport stdio``) or HTTP (``--url``). Each cycle creates one
uniquely named GameObject through ``execute_code`` while a domain reload happens around it:

* even cycles - the snippet itself calls ``EditorUtility.RequestScriptReload()``, so the
  reload starts right after the command ran and usually before its reply is written;
* odd cycles  - a script in ``Assets/McpStressProbe`` is rewritten and a compile is
  requested, then the create command is sent immediately into the compile/reload window.

With ``--inject-reply-loss`` (HTTP) the even-cycle snippet also runs the plugin's own
before-reload teardown (HttpBridgeReloadHandler) right after creating its object, so the
socket is gone before the reply can be written - the lost-reply window, every time instead
of only when the race happens to go that way.

Like an agent would, the client repeats a call whose reply says retry/disconnected/reloading.
At the end every name must exist exactly once: 0 means a lost command, 2+ a duplicate.

    python tools/stress_reload_idempotency.py --transport http --url http://127.0.0.1:8080/mcp \\
        --project-path /path/to/UnityProject --cycles 25
    python tools/stress_reload_idempotency.py --transport stdio --status-dir ~/.unity-mcp \\
        --project-path /path/to/UnityProject

Exit code 0 = no lost and no duplicated commands; 1 = violation; 2 = setup failure.
Requires the execute_code tool (group scripting_ext, activated by the script).
"""
from __future__ import annotations

import argparse
import asyncio
import json
import os
import secrets
import sys
import time
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
TRANSIENT_MARKERS = ("retry", "reload", "disconnect", "timed out", "timeout", "could not connect",
                     "not ready", "no unity session", "connection", "compiling")


def _payload(result) -> dict:
    """Tool results come back as one JSON text block; fall back to structured content."""
    for block in getattr(result, "content", None) or []:
        text = getattr(block, "text", None)
        if text:
            try:
                return json.loads(text)
            except json.JSONDecodeError:
                return {"success": False, "message": text}
    structured = getattr(result, "structured_content", None)
    return structured if isinstance(structured, dict) else {"success": False, "message": "empty result"}


def _is_transient(resp: dict) -> bool:
    if resp.get("success"):
        return False
    text = json.dumps(resp).lower()
    return resp.get("hint") == "retry" or any(m in text for m in TRANSIENT_MARKERS)


class Stress:
    def __init__(self, client, args):
        self.client = client
        self.args = args
        self.run = secrets.token_hex(3)
        self.prefix = f"McpStress_{self.run}_"
        self.probe_dir = Path(args.project_path) / "Assets" / "McpStressProbe"

    async def call(self, tool: str, arguments: dict) -> dict:
        try:
            result = await self.client.call_tool(tool, arguments, raise_on_error=False,
                                                 timeout=self.args.call_timeout)
        except Exception as exc:  # transport-level failure of the MCP call itself
            return {"success": False, "message": f"client error: {exc}", "hint": "retry"}
        return _payload(result)

    async def call_until_settled(self, tool: str, arguments: dict) -> tuple[dict, int]:
        """Repeat an identical call while the reply is transient, as an agent would."""
        resp: dict = {}
        for attempt in range(1, self.args.max_attempts + 1):
            resp = await self.call(tool, arguments)
            if not _is_transient(resp):
                return resp, attempt
            await asyncio.sleep(self.args.retry_delay)
        return resp, self.args.max_attempts

    async def wait_ready(self, timeout_s: float = 90.0) -> bool:
        deadline = time.monotonic() + timeout_s
        while time.monotonic() < deadline:
            try:
                contents = await self.client.read_resource("mcpforunity://editor/state")
                state = json.loads(contents[0].text)
                data = state.get("data") or state
                if (data.get("advice") or {}).get("ready_for_tools"):
                    return True
            except Exception:
                pass
            await asyncio.sleep(0.5)
        return False

    INJECT_REPLY_LOSS = (
        'System.Type handler = null;\n'
        'foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())\n'
        '{ handler = asm.GetType("MCPForUnity.Editor.Services.HttpBridgeReloadHandler"); if (handler != null) break; }\n'
        'handler.GetMethod("OnBeforeAssemblyReload", System.Reflection.BindingFlags.NonPublic | '
        'System.Reflection.BindingFlags.Static).Invoke(null, null);\n')

    def create_code(self, i: int, self_reload: bool) -> str:
        reload = ""
        if self_reload:
            reload = (self.INJECT_REPLY_LOSS if self.args.inject_reply_loss else "") + \
                "UnityEditor.EditorUtility.RequestScriptReload();"
        return (f'var go = new UnityEngine.GameObject("{self.prefix}{i}");\n'
                f'UnityEditor.Undo.RegisterCreatedObjectUndo(go, "stress");\n'
                f'{reload}\nreturn go.name;')

    def count_code(self) -> str:
        return ("var counts = new System.Collections.Generic.Dictionary<string, int>();\n"
                "foreach (var g in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())\n"
                f'{{ if (!g.name.StartsWith("{self.prefix}")) continue; int n; '
                "counts.TryGetValue(g.name, out n); counts[g.name] = n + 1; }\n"
                "return counts;")

    def touch_probe_script(self, i: int) -> None:
        self.probe_dir.mkdir(parents=True, exist_ok=True)
        (self.probe_dir / "McpStressTick.cs").write_text(
            f"// stress run {self.run} cycle {i}\npublic static class McpStressTick {{ "
            f"public const int Cycle = {i}; }}\n")

    async def cycle(self, i: int) -> dict:
        self_reload = i % 2 == 0
        if not self_reload:
            self.touch_probe_script(i)
            await self.call("refresh_unity", {"mode": "force", "scope": "scripts",
                                              "compile": "request", "wait_for_ready": False})
        resp, attempts = await self.call_until_settled(
            "execute_code", {"action": "execute", "code": self.create_code(i, self_reload)})
        ready = await self.wait_ready()
        return {"cycle": i, "kind": "self_reload" if self_reload else "recompile_race",
                "success": bool(resp.get("success")), "attempts": attempts, "ready_after": ready,
                "message": (resp.get("message") or resp.get("error") or "")[:160]}

    async def counts(self) -> dict[str, int]:
        resp, _ = await self.call_until_settled("execute_code", {"action": "execute", "code": self.count_code()})
        data = resp.get("data") or {}
        result = data.get("result", data)
        if isinstance(result, str):
            result = json.loads(result)
        return {k: int(v) for k, v in (result or {}).items()}

    async def cleanup(self) -> None:
        code = ("int removed = 0;\n"
                "foreach (var g in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())\n"
                f'{{ if (g.name.StartsWith("{self.prefix}")) {{ UnityEngine.Object.DestroyImmediate(g); removed++; }} }}\n'
                "return removed;")
        await self.call_until_settled("execute_code", {"action": "execute", "code": code})
        for path in (self.probe_dir / "McpStressTick.cs", self.probe_dir / "McpStressTick.cs.meta"):
            path.unlink(missing_ok=True)
        meta = self.probe_dir.with_name(self.probe_dir.name + ".meta")
        if self.probe_dir.exists() and not any(self.probe_dir.iterdir()):
            self.probe_dir.rmdir()
            meta.unlink(missing_ok=True)
        await self.call("refresh_unity", {"mode": "force", "scope": "all", "compile": "request",
                                          "wait_for_ready": True})


def _client(args):
    from fastmcp import Client
    from fastmcp.client.transports import StdioTransport

    if args.transport == "http":
        return Client(args.url)
    env = dict(os.environ)
    if args.status_dir:
        env["UNITY_MCP_STATUS_DIR"] = str(Path(args.status_dir).expanduser())
    server_dir = str(REPO_ROOT / "Server")
    return Client(StdioTransport(command="uv", args=["run", "--directory", server_dir, "python",
                                                     "src/main.py", "--transport", "stdio"],
                                 env=env, cwd=server_dir))


async def main_async(args) -> int:
    async with _client(args) as client:
        stress = Stress(client, args)
        activated = await stress.call("manage_tools", {"action": "activate", "group": "scripting_ext"})
        if not activated.get("success", True):
            print("could not activate scripting_ext:", activated)
            return 2
        if not await stress.wait_ready():
            print("Unity never reported ready_for_tools")
            return 2

        print(f"run {stress.run}: {args.cycles} cycles over {args.transport}")
        cycles = []
        for i in range(args.cycles):
            row = await stress.cycle(i)
            cycles.append(row)
            print(json.dumps(row))

        counts = await stress.counts()
        expected = {f"{stress.prefix}{i}" for i in range(args.cycles)}
        lost = sorted(n for n in expected if counts.get(n, 0) == 0)
        duplicated = {n: c for n, c in counts.items() if c > 1}
        summary = {
            "transport": args.transport,
            "cycles": args.cycles,
            "reloads_requested": args.cycles,
            "calls_needing_retry": sum(1 for c in cycles if c["attempts"] > 1),
            "failed_calls": [c["cycle"] for c in cycles if not c["success"]],
            "lost": lost,
            "duplicated": duplicated,
        }
        print("SUMMARY", json.dumps(summary))
        if not args.keep_objects:
            await stress.cleanup()
        return 0 if not lost and not duplicated else 1


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--transport", choices=["stdio", "http"], default="http")
    p.add_argument("--url", default="http://127.0.0.1:8080/mcp")
    p.add_argument("--status-dir", default=None, help="UNITY_MCP_STATUS_DIR for the spawned stdio server.")
    p.add_argument("--project-path", required=True, help="Unity project root (for the recompile probe).")
    p.add_argument("--cycles", type=int, default=25)
    p.add_argument("--max-attempts", type=int, default=8)
    p.add_argument("--retry-delay", type=float, default=1.5)
    p.add_argument("--call-timeout", type=float, default=120.0)
    p.add_argument("--keep-objects", action="store_true", help="Leave the created objects in the scene.")
    p.add_argument("--inject-reply-loss", action="store_true",
                   help="HTTP only: drop the plugin socket after each self-reload command ran, before its reply.")
    args = p.parse_args()
    if args.inject_reply_loss and args.transport != "http":
        p.error("--inject-reply-loss is only supported with --transport http")
    return asyncio.run(main_async(args))


if __name__ == "__main__":
    sys.exit(main())
