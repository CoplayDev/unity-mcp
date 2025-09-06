#!/usr/bin/env python3
import asyncio
import argparse
import json
import os
import struct
import time
from pathlib import Path
import random
import sys


TIMEOUT = float(os.environ.get("MCP_STRESS_TIMEOUT", "2.0"))
DEBUG = os.environ.get("MCP_STRESS_DEBUG", "").lower() in ("1", "true", "yes")


def dlog(*args):
    if DEBUG:
        print(*args, file=sys.stderr)


def find_status_files() -> list[Path]:
    home = Path.home()
    status_dir = Path(os.environ.get("UNITY_MCP_STATUS_DIR", home / ".unity-mcp"))
    if not status_dir.exists():
        return []
    return sorted(status_dir.glob("unity-mcp-status-*.json"), key=lambda p: p.stat().st_mtime, reverse=True)


def discover_port(project_path: str | None) -> int:
    # Default bridge port if nothing found
    default_port = 6400
    files = find_status_files()
    for f in files:
        try:
            data = json.loads(f.read_text())
            port = int(data.get("unity_port", 0) or 0)
            proj = data.get("project_path") or ""
            if project_path:
                # Match status for the given project if possible
                if proj and project_path in proj:
                    if 0 < port < 65536:
                        return port
            else:
                if 0 < port < 65536:
                    return port
        except Exception:
            pass
    return default_port


async def read_exact(reader: asyncio.StreamReader, n: int) -> bytes:
    buf = b""
    while len(buf) < n:
        chunk = await reader.read(n - len(buf))
        if not chunk:
            raise ConnectionError("Connection closed while reading")
        buf += chunk
    return buf


async def read_frame(reader: asyncio.StreamReader) -> bytes:
    header = await read_exact(reader, 8)
    (length,) = struct.unpack(">Q", header)
    if length <= 0 or length > (64 * 1024 * 1024):
        raise ValueError(f"Invalid frame length: {length}")
    return await read_exact(reader, length)


async def write_frame(writer: asyncio.StreamWriter, payload: bytes) -> None:
    header = struct.pack(">Q", len(payload))
    writer.write(header)
    writer.write(payload)
    await asyncio.wait_for(writer.drain(), timeout=TIMEOUT)


async def do_handshake(reader: asyncio.StreamReader) -> None:
    # Server sends a single line handshake: "WELCOME UNITY-MCP 1 FRAMING=1\n"
    line = await reader.readline()
    if not line or b"WELCOME UNITY-MCP" not in line:
        raise ConnectionError(f"Unexpected handshake from server: {line!r}")


def make_ping_frame() -> bytes:
    return b"ping"


def make_execute_menu_item(menu_path: str) -> bytes:
    # Retained for manual debugging; not used in normal stress runs
    payload = {"type": "execute_menu_item", "params": {"action": "execute", "menu_path": menu_path}}
    return json.dumps(payload).encode("utf-8")


async def client_loop(idx: int, host: str, port: int, stop_time: float, stats: dict):
    reconnect_delay = 0.2
    while time.time() < stop_time:
        writer = None
        try:
            # slight stagger to prevent burst synchronization across clients
            await asyncio.sleep(0.003 * (idx % 11))
            reader, writer = await asyncio.wait_for(asyncio.open_connection(host, port), timeout=TIMEOUT)
            await asyncio.wait_for(do_handshake(reader), timeout=TIMEOUT)
            # Send a quick ping first
            await write_frame(writer, make_ping_frame())
            _ = await asyncio.wait_for(read_frame(reader), timeout=TIMEOUT)  # ignore content

            # Main activity loop (keep-alive + light load). Edit spam handled by reload_churn_task.
            while time.time() < stop_time:
                # Ping-only; edits are sent via reload_churn_task to avoid console spam
                await write_frame(writer, make_ping_frame())
                _ = await asyncio.wait_for(read_frame(reader), timeout=TIMEOUT)
                stats["pings"] += 1
                await asyncio.sleep(0.02 + random.uniform(-0.003, 0.003))

        except (ConnectionError, OSError, asyncio.IncompleteReadError, asyncio.TimeoutError):
            stats["disconnects"] += 1
            dlog(f"[client {idx}] disconnect/backoff {reconnect_delay}s")
            await asyncio.sleep(reconnect_delay)
            reconnect_delay = min(reconnect_delay * 1.5, 2.0)
            continue
        except Exception:
            stats["errors"] += 1
            dlog(f"[client {idx}] unexpected error")
            await asyncio.sleep(0.2)
            continue
        finally:
            if writer is not None:
                try:
                    writer.close()
                    await writer.wait_closed()
                except Exception:
                    pass


async def reload_churn_task(project_path: str, stop_time: float, unity_file: str | None, host: str, port: int, stats: dict, storm_count: int = 1):
    # Use script edit tool to touch a C# file, which triggers compilation reliably
    path = Path(unity_file) if unity_file else None
    seq = 0
    proj_root = Path(project_path).resolve() if project_path else None
    # Build candidate list for storm mode
    candidates: list[Path] = []
    if proj_root:
        try:
            for p in (proj_root / "Assets").rglob("*.cs"):
                candidates.append(p.resolve())
        except Exception:
            candidates = []
    if path and path.exists():
        rp = path.resolve()
        if rp not in candidates:
            candidates.append(rp)
    while time.time() < stop_time:
        try:
            if path and path.exists():
                # Determine files to touch this cycle
                targets: list[Path]
                if storm_count and storm_count > 1 and candidates:
                    k = min(max(1, storm_count), len(candidates))
                    targets = random.sample(candidates, k)
                else:
                    targets = [path]

                for tpath in targets:
                    # Build a tiny ApplyTextEdits request that toggles a trailing comment
                    relative = None
                    try:
                        # Derive Unity-relative path under Assets/ (cross-platform)
                        resolved = tpath.resolve()
                        parts = list(resolved.parts)
                        if "Assets" in parts:
                            i = parts.index("Assets")
                            relative = Path(*parts[i:]).as_posix()
                        elif proj_root and str(resolved).startswith(str(proj_root)):
                            rel = resolved.relative_to(proj_root)
                            parts2 = list(rel.parts)
                            if "Assets" in parts2:
                                i2 = parts2.index("Assets")
                                relative = Path(*parts2[i2:]).as_posix()
                    except Exception:
                        relative = None

                    if relative:
                        # Derive name and directory for ManageScript and compute precondition SHA + EOF position
                        name_base = Path(relative).stem
                        dir_path = str(Path(relative).parent).replace('\\', '/')

                        # 1) Read current contents via manage_script.read to compute SHA and true EOF location
                        contents = None
                        read_success = False
                        for attempt in range(3):
                            writer = None
                            try:
                                reader, writer = await asyncio.wait_for(asyncio.open_connection(host, port), timeout=TIMEOUT)
                                await asyncio.wait_for(do_handshake(reader), timeout=TIMEOUT)
                                read_payload = {
                                    "type": "manage_script",
                                    "params": {
                                        "action": "read",
                                        "name": name_base,
                                        "path": dir_path
                                    }
                                }
                                await write_frame(writer, json.dumps(read_payload).encode("utf-8"))
                                resp = await asyncio.wait_for(read_frame(reader), timeout=TIMEOUT)

                                read_obj = json.loads(resp.decode("utf-8", errors="ignore"))
                                result = read_obj.get("result", read_obj) if isinstance(read_obj, dict) else {}
                                if result.get("success"):
                                    data_obj = result.get("data", {})
                                    contents = data_obj.get("contents") or ""
                                    read_success = True
                                    break
                            except Exception:
                                # retry with backoff
                                await asyncio.sleep(0.2 * (2 ** attempt) + random.uniform(0.0, 0.1))
                            finally:
                                if 'writer' in locals() and writer is not None:
                                    try:
                                        writer.close()
                                        await writer.wait_closed()
                                    except Exception:
                                        pass

                        if not read_success or contents is None:
                            stats["apply_errors"] = stats.get("apply_errors", 0) + 1
                            await asyncio.sleep(0.5)
                            continue

                        # Compute SHA and EOF insertion point
                        import hashlib
                        sha = hashlib.sha256(contents.encode("utf-8")).hexdigest()
                        lines = contents.splitlines(keepends=True)
                        # Insert at true EOF (safe against header guards)
                        end_line = len(lines) + 1  # 1-based exclusive end
                        end_col = 1

                        # Build a unique marker append; ensure it begins with a newline if needed
                        marker = f"// MCP_STRESS seq={seq} time={int(time.time())}"
                        seq += 1
                        insert_text = ("\n" if not contents.endswith("\n") else "") + marker + "\n"

                        # 2) Apply text edits with immediate refresh and precondition
                        apply_payload = {
                            "type": "manage_script",
                            "params": {
                                "action": "apply_text_edits",
                                "name": name_base,
                                "path": dir_path,
                                "edits": [
                                    {
                                        "startLine": end_line,
                                        "startCol": end_col,
                                        "endLine": end_line,
                                        "endCol": end_col,
                                        "newText": insert_text
                                    }
                                ],
                                "precondition_sha256": sha,
                                "options": {"refresh": "immediate", "validate": "standard"}
                            }
                        }

                        apply_success = False
                        for attempt in range(3):
                            writer = None
                            try:
                                reader, writer = await asyncio.wait_for(asyncio.open_connection(host, port), timeout=TIMEOUT)
                                await asyncio.wait_for(do_handshake(reader), timeout=TIMEOUT)
                                await write_frame(writer, json.dumps(apply_payload).encode("utf-8"))
                                resp = await asyncio.wait_for(read_frame(reader), timeout=TIMEOUT)
                                try:
                                    data = json.loads(resp.decode("utf-8", errors="ignore"))
                                    result = data.get("result", data) if isinstance(data, dict) else {}
                                    ok = bool(result.get("success", False))
                                    if ok:
                                        stats["applies"] = stats.get("applies", 0) + 1
                                        apply_success = True
                                        break
                                except Exception:
                                    # fall through to retry
                                    pass
                            except Exception:
                                # retry with backoff
                                await asyncio.sleep(0.2 * (2 ** attempt) + random.uniform(0.0, 0.1))
                            finally:
                                if 'writer' in locals() and writer is not None:
                                    try:
                                        writer.close()
                                        await writer.wait_closed()
                                    except Exception:
                                        pass
                        if not apply_success:
                            stats["apply_errors"] = stats.get("apply_errors", 0) + 1

        except Exception:
            pass
        await asyncio.sleep(1.0)


async def main():
    ap = argparse.ArgumentParser(description="Stress test the Unity MCP bridge with concurrent clients and reload churn")
    ap.add_argument("--host", default="127.0.0.1")
    ap.add_argument("--project", default=str(Path(__file__).resolve().parents[1] / "TestProjects" / "UnityMCPTests"))
    ap.add_argument("--unity-file", default=str(Path(__file__).resolve().parents[1] / "TestProjects" / "UnityMCPTests" / "Assets" / "Scripts" / "LongUnityScriptClaudeTest.cs"))
    ap.add_argument("--clients", type=int, default=10)
    ap.add_argument("--duration", type=int, default=60)
    ap.add_argument("--storm-count", type=int, default=1, help="Number of scripts to touch each cycle")
    args = ap.parse_args()

    port = discover_port(args.project)
    stop_time = time.time() + max(10, args.duration)

    stats = {"pings": 0, "menus": 0, "mods": 0, "disconnects": 0, "errors": 0}
    tasks = []

    # Spawn clients
    for i in range(max(1, args.clients)):
        tasks.append(asyncio.create_task(client_loop(i, args.host, port, stop_time, stats)))

    # Spawn reload churn task
    tasks.append(asyncio.create_task(reload_churn_task(args.project, stop_time, args.unity_file, args.host, port, stats, storm_count=args.storm_count)))

    await asyncio.gather(*tasks, return_exceptions=True)
    print(json.dumps({"port": port, "stats": stats}, indent=2))


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        pass


