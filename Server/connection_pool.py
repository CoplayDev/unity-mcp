"""
Connection pool for managing multiple Unity Editor instances.
"""
import logging
import os
import threading
import time

from models import UnityInstanceInfo
from port_discovery import PortDiscovery

logger = logging.getLogger(__name__)


class UnityConnectionPool:
    """Manages connections to multiple Unity Editor instances"""

    def __init__(self):
        # Import here to avoid circular dependency
        from unity_connection import UnityConnection
        self._UnityConnection = UnityConnection

        self._connections: dict[str, "UnityConnection"] = {}
        self._known_instances: dict[str, UnityInstanceInfo] = {}
        self._last_full_scan: float = 0
        self._scan_interval: float = 5.0  # Cache for 5 seconds
        self._pool_lock = threading.Lock()
        self._default_instance_id: str | None = None

        # Check for default instance from environment
        env_default = os.environ.get("UNITY_MCP_DEFAULT_INSTANCE", "").strip()
        if env_default:
            self._default_instance_id = env_default
            logger.info(f"Default Unity instance set from environment: {env_default}")

    def discover_all_instances(self, force_refresh: bool = False) -> list[UnityInstanceInfo]:
        """
        Discover all running Unity Editor instances.

        Args:
            force_refresh: If True, bypass cache and scan immediately

        Returns:
            List of UnityInstanceInfo objects
        """
        now = time.time()

        # Return cached results if valid
        if not force_refresh and (now - self._last_full_scan) < self._scan_interval:
            logger.debug(f"Returning cached Unity instances (age: {now - self._last_full_scan:.1f}s)")
            return list(self._known_instances.values())

        # Scan for instances
        logger.debug("Scanning for Unity instances...")
        instances = PortDiscovery.discover_all_unity_instances()

        # Update cache
        with self._pool_lock:
            self._known_instances = {inst.id: inst for inst in instances}
            self._last_full_scan = now

        logger.info(f"Found {len(instances)} Unity instances: {[inst.id for inst in instances]}")
        return instances

    def _resolve_instance_id(self, instance_identifier: str | None, instances: list[UnityInstanceInfo]) -> UnityInstanceInfo:
        """
        Resolve an instance identifier to a specific Unity instance.

        Args:
            instance_identifier: User-provided identifier (name, hash, name@hash, path, port, or None)
            instances: List of available instances

        Returns:
            Resolved UnityInstanceInfo

        Raises:
            ConnectionError: If instance cannot be resolved
        """
        if not instances:
            raise ConnectionError(
                "No Unity Editor instances found. Please ensure Unity is running with MCP for Unity bridge."
            )

        # Use default instance if no identifier provided
        if instance_identifier is None:
            if self._default_instance_id:
                instance_identifier = self._default_instance_id
                logger.debug(f"Using default instance: {instance_identifier}")
            else:
                # Use the most recently active instance
                # Instances with no heartbeat (None) should be sorted last (use 0.0 as sentinel)
                sorted_instances = sorted(
                    instances,
                    key=lambda inst: inst.last_heartbeat.timestamp() if inst.last_heartbeat else 0.0,
                    reverse=True,
                )
                logger.info(f"No instance specified, using most recent: {sorted_instances[0].id}")
                return sorted_instances[0]

        identifier = instance_identifier.strip()

        # Try exact ID match first
        for inst in instances:
            if inst.id == identifier:
                return inst

        # Try project name match
        name_matches = [inst for inst in instances if inst.name == identifier]
        if len(name_matches) == 1:
            return name_matches[0]
        elif len(name_matches) > 1:
            # Multiple projects with same name - return helpful error
            suggestions = [
                {
                    "id": inst.id,
                    "path": inst.path,
                    "port": inst.port,
                    "suggest": f"Use unity_instance='{inst.id}'"
                }
                for inst in name_matches
            ]
            raise ConnectionError(
                f"Project name '{identifier}' matches {len(name_matches)} instances. "
                f"Please use the full format (e.g., '{name_matches[0].id}'). "
                f"Available instances: {suggestions}"
            )

        # Try hash match
        hash_matches = [inst for inst in instances if inst.hash == identifier or inst.hash.startswith(identifier)]
        if len(hash_matches) == 1:
            return hash_matches[0]
        elif len(hash_matches) > 1:
            raise ConnectionError(
                f"Hash '{identifier}' matches multiple instances: {[inst.id for inst in hash_matches]}"
            )

        # Try composite format: Name@Hash or Name@Port
        if "@" in identifier:
            name_part, hint_part = identifier.split("@", 1)
            composite_matches = [
                inst for inst in instances
                if inst.name == name_part and (
                    inst.hash.startswith(hint_part) or str(inst.port) == hint_part
                )
            ]
            if len(composite_matches) == 1:
                return composite_matches[0]

        # Try port match (as string)
        try:
            port_num = int(identifier)
            port_matches = [inst for inst in instances if inst.port == port_num]
            if len(port_matches) == 1:
                return port_matches[0]
        except ValueError:
            pass

        # Try path match
        path_matches = [inst for inst in instances if inst.path == identifier]
        if len(path_matches) == 1:
            return path_matches[0]

        # Nothing matched
        available_ids = [inst.id for inst in instances]
        raise ConnectionError(
            f"Unity instance '{identifier}' not found. "
            f"Available instances: {available_ids}. "
            f"Use the unity_instances resource to see all instances."
        )

    def get_connection(self, instance_identifier: str | None = None):
        """
        Get or create a connection to a Unity instance.

        Args:
            instance_identifier: Optional identifier (name, hash, name@hash, etc.)
                                If None, uses default or most recent instance

        Returns:
            UnityConnection to the specified instance

        Raises:
            ConnectionError: If instance cannot be found or connected
        """
        # Refresh instance list if cache expired
        instances = self.discover_all_instances()

        # Resolve identifier to specific instance
        target = self._resolve_instance_id(instance_identifier, instances)

        # Return existing connection or create new one
        with self._pool_lock:
            if target.id not in self._connections:
                logger.info(f"Creating new connection to Unity instance: {target.id} (port {target.port})")
                conn = self._UnityConnection(port=target.port, instance_id=target.id)
                if not conn.connect():
                    raise ConnectionError(
                        f"Failed to connect to Unity instance '{target.id}' on port {target.port}. "
                        f"Ensure the Unity Editor is running."
                    )
                self._connections[target.id] = conn
            else:
                # Update existing connection with instance_id and port if changed
                conn = self._connections[target.id]
                conn.instance_id = target.id
                if conn.port != target.port:
                    logger.info(f"Updating cached port for {target.id}: {conn.port} -> {target.port}")
                    conn.port = target.port
                logger.debug(f"Reusing existing connection to: {target.id}")

            return self._connections[target.id]

    def disconnect_all(self):
        """Disconnect all active connections"""
        with self._pool_lock:
            for instance_id, conn in self._connections.items():
                try:
                    logger.info(f"Disconnecting from Unity instance: {instance_id}")
                    conn.disconnect()
                except Exception:
                    logger.exception(f"Error disconnecting from {instance_id}")
            self._connections.clear()


# Global Unity connection pool
_unity_connection_pool: UnityConnectionPool | None = None
_pool_init_lock = threading.Lock()


def get_unity_connection_pool() -> UnityConnectionPool:
    """Get or create the global Unity connection pool."""
    global _unity_connection_pool
    if _unity_connection_pool is None:
        with _pool_init_lock:
            if _unity_connection_pool is None:
                _unity_connection_pool = UnityConnectionPool()
    return _unity_connection_pool
