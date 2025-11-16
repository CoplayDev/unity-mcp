"""
Configuration settings for the MCP for Unity Server.
This file contains all configurable parameters for the server.
"""

from dataclasses import dataclass


@dataclass
class ServerConfig:
    """Main configuration class for the MCP server."""

    # Network settings
    unity_host: str = "localhost"
    unity_port: int = 6400
    mcp_port: int = 6500

    # Connection settings
    connection_timeout: float = 30.0
    buffer_size: int = 16 * 1024 * 1024  # 16MB buffer

    # STDIO framing behaviour
    require_framing: bool = True
    handshake_timeout: float = 1.0
    framed_receive_timeout: float = 2.0
    max_heartbeat_frames: int = 16
    heartbeat_timeout: float = 2.0

    # Logging settings
    log_level: str = "INFO"
    log_format: str = "%(asctime)s - %(name)s - %(levelname)s - %(message)s"

    # Server settings
    max_retries: int = 5
    retry_delay: float = 0.25
    # Backoff hint returned to clients when Unity is reloading (milliseconds)
    reload_retry_ms: int = 250
    # Number of polite retries when Unity reports reloading
    # 40 × 250ms ≈ 10s default window
    reload_max_retries: int = 40

    # Port discovery cache
    port_registry_ttl: float = 5.0

    # Telemetry settings
    telemetry_enabled: bool = True
    # Align with telemetry.py default Cloud Run endpoint
    telemetry_endpoint: str = "https://api-prod.coplay.dev/telemetry/events"

    def configure_logging(self) -> None:
        level = getattr(logging, self.log_level, logging.INFO)
        logging.basicConfig(level=level, format=self.log_format)


# Create a global config instance
config = ServerConfig()
