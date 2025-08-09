"""
Configuration settings for the Unity MCP Server.
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
    connection_timeout: float = 60.0  # default steady-state timeout; retries use shorter timeouts
    buffer_size: int = 16 * 1024 * 1024  # 16MB buffer
    
    # Logging settings
    log_level: str = "INFO"
    log_format: str = "%(asctime)s - %(name)s - %(levelname)s - %(message)s"
    
    # Server settings
    max_retries: int = 10
    retry_delay: float = 0.25

# Create a global config instance
config = ServerConfig() 