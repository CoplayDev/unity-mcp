"""CLI Configuration utilities."""

import os
from dataclasses import dataclass
from typing import Optional


@dataclass
class CLIConfig:
    """Configuration for CLI connection to Unity."""

    host: str = "127.0.0.1"
    port: int = 8080
    timeout: int = 30
    format: str = "text"  # text, json, table
    unity_instance: Optional[str] = None

    @classmethod
    def from_env(cls) -> "CLIConfig":
        port_raw = os.environ.get("UNITY_MCP_HTTP_PORT", "8080")
        try:
            port = int(port_raw)
        except (ValueError, TypeError):
            raise ValueError(
                f"Invalid UNITY_MCP_HTTP_PORT value: {port_raw!r}")

        timeout_raw = os.environ.get("UNITY_MCP_TIMEOUT", "30")
        try:
            timeout = int(timeout_raw)
        except (ValueError, TypeError):
            raise ValueError(
                f"Invalid UNITY_MCP_TIMEOUT value: {timeout_raw!r}")

        return cls(
            host=os.environ.get("UNITY_MCP_HOST", "127.0.0.1"),
            port=port,
            timeout=timeout,
            format=os.environ.get("UNITY_MCP_FORMAT", "text"),
            unity_instance=os.environ.get("UNITY_MCP_INSTANCE"),
        )


# Global config instance
_config: Optional[CLIConfig] = None


def get_config() -> CLIConfig:
    """Get the current CLI configuration."""
    global _config
    if _config is None:
        _config = CLIConfig.from_env()
    return _config


def set_config(config: CLIConfig) -> None:
    """Set the CLI configuration."""
    global _config
    _config = config
