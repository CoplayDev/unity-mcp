#!/usr/bin/env python3
import asyncio
import argparse
import json
import os
import random
import socket
import struct
import sys
import time
from pathlib import Path


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
    await writer.drain()


async def do_handshake(reader: asyncio.StreamReader) -> None:
    # Server sends a single line handshake: "WELCOME UNITY-MCP 1 FRAMING=1\n"
    line = await reader.readline()
    if not line or b"WELCOME UNITY-MCP" not in line:
        raise ConnectionError(f"Unexpected handshake from server: {line!r}")


def make_ping_frame() -> bytes:
    return b"ping"


def make_execute_menu_item(menu_path: str) -> bytes:
    payload = {
        "type": "execute_menu_item",
        "params": {"action": "execute", "menu_path": menu_path},
    }
    return json.dumps(payload).encode("utf-8")


def make_manage_gameobject_modify_dummy(target_name: str) -> bytes:
    payload = {
        "type": "manage_gameobject",
        "params": {
            "action": "modify",
            "target": target_name,
            "search_method": "by_name",
            # Intentionally small and sometimes invalid to exercise error paths safely
            "componentProperties": {
                "Transform": {"localScale": {"x": 1.0, "y": 1.0, "z": 1.0}},
                "Rigidbody": {"velocity": "invalid_type"},
            },
        },
    }
    return json.dumps(payload).encode("utf-8")


async def client_loop(idx: int, host: str, port: int, stop_time: float, stats: dict):
    reconnect_delay = 0.2
    while time.time() < stop_time:
        try:
            reader, writer = await asyncio.open_connection(host, port)
            await do_handshake(reader)
            # Send a quick ping first
            await write_frame(writer, make_ping_frame())
            _ = await read_frame(reader)  # ignore content

            # Main activity loop
            while time.time() < stop_time:
                r = random.random()
                if r < 0.70:
                    # Ping
                    await write_frame(writer, make_ping_frame())
                    _ = await read_frame(reader)
                    stats["pings"] += 1
                elif r < 0.90:
                    # Lightweight menu execute: Assets/Refresh
                    await write_frame(writer, make_execute_menu_item("Assets/Refresh"))
                    _ = await read_frame(reader)
                    stats["menus"] += 1
                else:
                    # Small manage_gameobject request (may legitimately error if target not found)
                    await write_frame(writer, make_manage_gameobject_modify_dummy("__MCP_Stress_Object__"))
                    _ = await read_frame(reader)
                    stats["mods"] += 1

                await asyncio.sleep(0.01)

        except (ConnectionError, OSError, asyncio.IncompleteReadError):
            stats["disconnects"] += 1
            await asyncio.sleep(reconnect_delay)
            reconnect_delay = min(reconnect_delay * 1.5, 2.0)
            continue
        except Exception:
            stats["errors"] += 1
            await asyncio.sleep(0.2)
            continue
        finally:
            try:
                writer.close()  # type: ignore
                await writer.wait_closed()  # type: ignore
            except Exception:
                pass


async def reload_churn_task(project_path: str, stop_time: float, unity_file: str | None, host: str, port: int):
    # Toggle a comment in a large .cs file to force a recompilation; then request Assets/Refresh
    path = Path(unity_file) if unity_file else None
    toggle = True
    while time.time() < stop_time:
        try:
            if path and path.exists():
                s = path.read_text(encoding="utf-8", errors="ignore")
                marker_on = "// MCP_STRESS_ON"
                marker_off = "// MCP_STRESS_OFF"
                if toggle:
                    if marker_on not in s:
                        path.write_text(s + ("\n" if not s.endswith("\n") else "") + marker_on + "\n", encoding="utf-8")
                else:
                    if marker_off not in s:
                        path.write_text(s + ("\n" if not s.endswith("\n") else "") + marker_off + "\n", encoding="utf-8")
                toggle = not toggle

            # Ask Unity to refresh assets (safe, Editor main thread)
            try:
                reader, writer = await asyncio.open_connection(host, port)
                await do_handshake(reader)
                await write_frame(writer, make_execute_menu_item("Assets/Refresh"))
                _ = await read_frame(reader)
                writer.close()
                await writer.wait_closed()
            except Exception:
                pass

        except Exception:
            pass
        await asyncio.sleep(10.0)


async def main():
    ap = argparse.ArgumentParser(description="Stress test the Unity MCP bridge with concurrent clients and reload churn")
    ap.add_argument("--host", default="127.0.0.1")
    ap.add_argument("--project", default=str(Path(__file__).resolve().parents[1] / "TestProjects" / "UnityMCPTests"))
    ap.add_argument("--unity-file", default=str(Path(__file__).resolve().parents[1] / "TestProjects" / "UnityMCPTests" / "Assets" / "Scripts" / "LongUnityScriptClaudeTest.cs"))
    ap.add_argument("--clients", type=int, default=10)
    ap.add_argument("--duration", type=int, default=60)
    args = ap.parse_args()

    port = discover_port(args.project)
    stop_time = time.time() + max(10, args.duration)

    stats = {"pings": 0, "menus": 0, "mods": 0, "disconnects": 0, "errors": 0}
    tasks = []

    # Spawn clients
    for i in range(max(1, args.clients)):
        tasks.append(asyncio.create_task(client_loop(i, args.host, port, stop_time, stats)))

    # Spawn reload churn task
    tasks.append(asyncio.create_task(reload_churn_task(args.project, stop_time, args.unity_file, args.host, port)))

    await asyncio.gather(*tasks, return_exceptions=True)
    print(json.dumps({"port": port, "stats": stats}, indent=2))


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        pass


