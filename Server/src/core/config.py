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

    # Transport settings
    transport_mode: str = "stdio"

    # HTTP transport behaviour
    http_remote_hosted: bool = False

    # API key authentication (required when http_remote_hosted=True)
    api_key_validation_url: str | None = None  # POST endpoint to validate keys
    api_key_login_url: str | None = None       # URL for users to get/manage keys
    # Cache TTL in seconds (5 min default)
    api_key_cache_ttl: float = 300.0
    # Optional service token for authenticating to the validation endpoint
    api_key_service_token_header: str | None = None  # e.g. "X-Service-Token"
    api_key_service_token: str | None = None         # The token value

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


# Create a global config instance
config = ServerConfig()
