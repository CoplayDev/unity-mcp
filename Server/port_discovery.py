"""
Port discovery utility for MCP for Unity Server.

What changed and why:
- Unity now writes a per-project port file named like
  `~/.unity-mcp/unity-mcp-port-<hash>.json` to avoid projects overwriting
  each other's saved port. The legacy file `unity-mcp-port.json` may still
  exist.
- This module now scans for both patterns, prefers the most recently
  modified file, and verifies that the port is actually a MCP for Unity listener
  (quick socket connect + ping) before choosing it.
"""

import glob
import json
import logging
import os
import struct
from datetime import datetime
from pathlib import Path
import socket
from typing import Optional, List

from models import UnityInstanceInfo

logger = logging.getLogger("mcp-for-unity-server")


class PortDiscovery:
    """Handles port discovery from Unity Bridge registry"""
    REGISTRY_FILE = "unity-mcp-port.json"  # legacy single-project file
    DEFAULT_PORT = 6400
    CONNECT_TIMEOUT = 0.3  # seconds, keep this snappy during discovery

    @staticmethod
    def get_registry_path() -> Path:
        """Get the path to the port registry file"""
        return Path.home() / ".unity-mcp" / PortDiscovery.REGISTRY_FILE

    @staticmethod
    def get_registry_dir() -> Path:
        return Path.home() / ".unity-mcp"

    @staticmethod
    def list_candidate_files() -> List[Path]:
        """Return candidate registry files, newest first.
        Includes hashed per-project files and the legacy file (if present).
        """
        base = PortDiscovery.get_registry_dir()
        hashed = sorted(
            (Path(p) for p in glob.glob(str(base / "unity-mcp-port-*.json"))),
            key=lambda p: p.stat().st_mtime,
            reverse=True,
        )
        legacy = PortDiscovery.get_registry_path()
        if legacy.exists():
            # Put legacy at the end so hashed, per-project files win
            hashed.append(legacy)
        return hashed

    @staticmethod
    def _try_probe_unity_mcp(port: int) -> bool:
        """Quickly check if a MCP for Unity listener is on this port.
        Uses Unity's framed protocol: receives handshake, sends framed ping, expects framed pong.
        """
        try:
            with socket.create_connection(("127.0.0.1", port), PortDiscovery.CONNECT_TIMEOUT) as s:
                s.settimeout(PortDiscovery.CONNECT_TIMEOUT)
                try:
                    # 1. Receive handshake from Unity
                    handshake = s.recv(512)
                    if not handshake or b"FRAMING=1" not in handshake:
                        # Try legacy mode as fallback
                        s.sendall(b"ping")
                        data = s.recv(512)
                        return data and b'"message":"pong"' in data

                    # 2. Send framed ping command
                    # Frame format: 8-byte length header (big-endian uint64) + payload
                    payload = b"ping"
                    header = struct.pack('>Q', len(payload))
                    s.sendall(header + payload)

                    # 3. Receive framed response
                    response_header = s.recv(8)
                    if len(response_header) != 8:
                        return False

                    response_length = struct.unpack('>Q', response_header)[0]
                    if response_length > 10000:  # Sanity check
                        return False

                    response = s.recv(response_length)
                    return b'"message":"pong"' in response
                except Exception as e:
                    logger.debug(f"Port probe failed for {port}: {e}")
                    return False
        except Exception as e:
            logger.debug(f"Connection failed for port {port}: {e}")
            return False

    @staticmethod
    def _read_latest_status() -> Optional[dict]:
        try:
            base = PortDiscovery.get_registry_dir()
            status_files = sorted(
                (Path(p)
                 for p in glob.glob(str(base / "unity-mcp-status-*.json"))),
                key=lambda p: p.stat().st_mtime,
                reverse=True,
            )
            if not status_files:
                return None
            with status_files[0].open('r') as f:
                return json.load(f)
        except Exception:
            return None

    @staticmethod
    def discover_unity_port() -> int:
        """
        Discover Unity port by scanning per-project and legacy registry files.
        Prefer the newest file whose port responds; fall back to first parsed
        value; finally default to 6400.

        Returns:
            Port number to connect to
        """
        # Prefer the latest heartbeat status if it points to a responsive port
        status = PortDiscovery._read_latest_status()
        if status:
            port = status.get('unity_port')
            if isinstance(port, int) and PortDiscovery._try_probe_unity_mcp(port):
                logger.info(f"Using Unity port from status: {port}")
                return port

        candidates = PortDiscovery.list_candidate_files()

        first_seen_port: Optional[int] = None

        for path in candidates:
            try:
                with open(path, 'r') as f:
                    cfg = json.load(f)
                unity_port = cfg.get('unity_port')
                if isinstance(unity_port, int):
                    if first_seen_port is None:
                        first_seen_port = unity_port
                    if PortDiscovery._try_probe_unity_mcp(unity_port):
                        logger.info(
                            f"Using Unity port from {path.name}: {unity_port}")
                        return unity_port
            except Exception as e:
                logger.warning(f"Could not read port registry {path}: {e}")

        if first_seen_port is not None:
            logger.info(
                f"No responsive port found; using first seen value {first_seen_port}")
            return first_seen_port

        # Fallback to default port
        logger.info(
            f"No port registry found; using default port {PortDiscovery.DEFAULT_PORT}")
        return PortDiscovery.DEFAULT_PORT

    @staticmethod
    def get_port_config() -> Optional[dict]:
        """
        Get the most relevant port configuration from registry.
        Returns the most recent hashed file's config if present,
        otherwise the legacy file's config. Returns None if nothing exists.

        Returns:
            Port configuration dict or None if not found
        """
        candidates = PortDiscovery.list_candidate_files()
        if not candidates:
            return None
        for path in candidates:
            try:
                with open(path, 'r') as f:
                    return json.load(f)
            except Exception as e:
                logger.warning(
                    f"Could not read port configuration {path}: {e}")
        return None

    @staticmethod
    def _extract_project_name(project_path: str) -> str:
        """Extract project name from Assets path.

        Examples:
            /Users/sakura/Projects/MyGame/Assets -> MyGame
            C:\\Projects\\TestProject\\Assets -> TestProject
        """
        if not project_path:
            return "Unknown"

        try:
            # Remove trailing /Assets or \Assets
            path = project_path.rstrip('/\\')
            if path.endswith('Assets'):
                path = path[:-6].rstrip('/\\')

            # Get the last directory name
            name = os.path.basename(path)
            return name if name else "Unknown"
        except Exception:
            return "Unknown"

    @staticmethod
    def discover_all_unity_instances() -> List[UnityInstanceInfo]:
        """
        Discover all running Unity Editor instances by scanning status files.

        Returns:
            List of UnityInstanceInfo objects for all discovered instances
        """
        instances = []
        base = PortDiscovery.get_registry_dir()

        # Scan all status files
        status_pattern = str(base / "unity-mcp-status-*.json")
        status_files = glob.glob(status_pattern)

        for status_file_path in status_files:
            try:
                with open(status_file_path, 'r') as f:
                    data = json.load(f)

                # Extract hash from filename: unity-mcp-status-{hash}.json
                filename = os.path.basename(status_file_path)
                hash_value = filename.replace('unity-mcp-status-', '').replace('.json', '')

                # Extract information
                project_path = data.get('project_path', '')
                project_name = PortDiscovery._extract_project_name(project_path)
                port = data.get('unity_port')
                is_reloading = data.get('reloading', False)

                # Parse last_heartbeat
                last_heartbeat = None
                heartbeat_str = data.get('last_heartbeat')
                if heartbeat_str:
                    try:
                        last_heartbeat = datetime.fromisoformat(heartbeat_str.replace('Z', '+00:00'))
                    except Exception:
                        pass

                # Verify port is actually responding
                is_alive = PortDiscovery._try_probe_unity_mcp(port) if isinstance(port, int) else False

                if not is_alive:
                    logger.debug(f"Instance {project_name}@{hash_value} has heartbeat but port {port} not responding")
                    continue

                # Create instance info
                instance = UnityInstanceInfo(
                    id=f"{project_name}@{hash_value}",
                    name=project_name,
                    path=project_path,
                    hash=hash_value,
                    port=port,
                    status="reloading" if is_reloading else "running",
                    last_heartbeat=last_heartbeat,
                    unity_version=data.get('unity_version')  # May not be available in current version
                )

                instances.append(instance)
                logger.debug(f"Discovered Unity instance: {instance.id} on port {instance.port}")

            except Exception as e:
                logger.debug(f"Failed to parse status file {status_file_path}: {e}")
                continue

        logger.info(f"Discovered {len(instances)} Unity instances")
        return instances
