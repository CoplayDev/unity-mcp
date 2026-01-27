#!/usr/bin/env python3
"""
Stress test for EditorStateCache.GetSnapshot() to reproduce GC allocation spikes.

This script rapidly polls the editor state to simulate an MCP client that frequently
checks Unity's readiness state. Run this while profiling in Unity to see if it
causes the GC spikes reported in GitHub issue #577.

Usage:
    python tools/stress_editor_state.py --duration 30 --interval 0.05

While this runs, open Unity Profiler and look for:
- EditorStateCache.OnUpdate
- EditorStateCache.GetSnapshot  
- GC.Alloc spikes
"""
import asyncio
import argparse
import json
import os
import struct
import time
from pathlib import Path
import sys


TIMEOUT = 5.0


def find_status_files() -> list[Path]:
    home = Path.home()
    status_dir = Path(os.environ.get("UNITY_MCP_STATUS_DIR", home / ".unity-mcp"))
    if not status_dir.exists():
        return []
    return sorted(status_dir.glob("unity-mcp-status-*.json"), key=lambda p: p.stat().st_mtime, reverse=True)


def discover_port(project_path: str | None) -> int:
    default_port = 6400
    files = find_status_files()
    for f in files:
        try:
            data = json.loads(f.read_text())
            port = int(data.get("unity_port", 0) or 0)
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
    line = await reader.readline()
    if not line or b"WELCOME UNITY-MCP" not in line:
        raise ConnectionError(f"Unexpected handshake from server: {line!r}")


def make_get_editor_state_frame() -> bytes:
    payload = {"type": "get_editor_state", "params": {}}
    return json.dumps(payload).encode("utf-8")


async def stress_loop(host: str, port: int, duration: float, interval: float, verbose: bool):
    stop_time = time.time() + duration
    stats = {"requests": 0, "errors": 0, "reconnects": 0}
    
    print(f"Starting editor state stress test...")
    print(f"  Target: {host}:{port}")
    print(f"  Duration: {duration}s")
    print(f"  Interval: {interval}s ({1/interval:.1f} requests/sec)")
    print(f"  Press Ctrl+C to stop early")
    print()
    
    writer = None
    reader = None
    
    try:
        while time.time() < stop_time:
            try:
                # Connect if needed
                if writer is None:
                    reader, writer = await asyncio.wait_for(
                        asyncio.open_connection(host, port), timeout=TIMEOUT
                    )
                    await asyncio.wait_for(do_handshake(reader), timeout=TIMEOUT)
                    if verbose:
                        print(f"[{time.time():.2f}] Connected")
                
                # Send get_editor_state request
                await write_frame(writer, make_get_editor_state_frame())
                response = await asyncio.wait_for(read_frame(reader), timeout=TIMEOUT)
                stats["requests"] += 1
                
                if verbose and stats["requests"] % 20 == 0:
                    try:
                        data = json.loads(response.decode("utf-8", errors="ignore"))
                        seq = data.get("data", {}).get("sequence", "?")
                        print(f"[{time.time():.2f}] Request #{stats['requests']}, sequence={seq}")
                    except Exception:
                        print(f"[{time.time():.2f}] Request #{stats['requests']}")
                
                await asyncio.sleep(interval)
                
            except (ConnectionError, OSError, asyncio.TimeoutError) as e:
                stats["errors"] += 1
                stats["reconnects"] += 1
                if verbose:
                    print(f"[{time.time():.2f}] Connection error: {e}, reconnecting...")
                if writer:
                    try:
                        writer.close()
                        await writer.wait_closed()
                    except Exception:
                        pass
                writer = None
                reader = None
                await asyncio.sleep(0.5)
                
    except KeyboardInterrupt:
        print("\nStopped by user")
    finally:
        if writer:
            try:
                writer.close()
                await writer.wait_closed()
            except Exception:
                pass
    
    elapsed = duration - max(0, stop_time - time.time())
    print()
    print("=" * 50)
    print("Results:")
    print(f"  Total requests: {stats['requests']}")
    print(f"  Errors: {stats['errors']}")
    print(f"  Reconnects: {stats['reconnects']}")
    print(f"  Elapsed: {elapsed:.1f}s")
    print(f"  Rate: {stats['requests']/elapsed:.1f} requests/sec")
    print("=" * 50)


async def main():
    parser = argparse.ArgumentParser(
        description="Stress test EditorStateCache.GetSnapshot() to reproduce GC spikes"
    )
    parser.add_argument("--host", default="127.0.0.1", help="Unity bridge host")
    parser.add_argument("--port", type=int, default=0, help="Unity bridge port (0=auto-discover)")
    parser.add_argument("--duration", type=float, default=30.0, help="Test duration in seconds")
    parser.add_argument("--interval", type=float, default=0.05, help="Interval between requests (0.05 = 20/sec)")
    parser.add_argument("-v", "--verbose", action="store_true", help="Verbose output")
    args = parser.parse_args()
    
    port = args.port if args.port > 0 else discover_port(None)
    
    await stress_loop(args.host, port, args.duration, args.interval, args.verbose)


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        pass
