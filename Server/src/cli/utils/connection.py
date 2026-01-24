"""Connection utilities for CLI to communicate with Unity via MCP server."""

import asyncio
import json
import sys
from typing import Any, Dict, Optional

import httpx

from cli.utils.config import get_config, CLIConfig


class UnityConnectionError(Exception):
    """Raised when connection to Unity fails."""
    pass


def warn_if_remote_host(config: CLIConfig) -> None:
    """Warn user if connecting to a non-localhost server.

    This is a security measure to alert users that connecting to remote
    servers exposes Unity control to potential network attacks.

    Args:
        config: CLI configuration with host setting
    """
    import click

    local_hosts = ("127.0.0.1", "localhost", "::1", "0.0.0.0")
    if config.host.lower() not in local_hosts:
        click.echo(
            "⚠️  Security Warning: Connecting to non-localhost server.\n"
            "   The MCP CLI has no authentication. Anyone on the network could\n"
            "   intercept commands or send unauthorized commands to Unity.\n"
            "   Only proceed if you trust this network.\n",
            err=True
        )


async def send_command(
    command_type: str,
    params: Dict[str, Any],
    config: Optional[CLIConfig] = None,
    timeout: Optional[int] = None,
) -> Dict[str, Any]:
    """Send a command to Unity via the MCP HTTP server.

    Args:
        command_type: The command type (e.g., 'manage_gameobject', 'manage_scene')
        params: Command parameters
        config: Optional CLI configuration
        timeout: Optional timeout override

    Returns:
        Response dict from Unity

    Raises:
        UnityConnectionError: If connection fails
    """
    cfg = config or get_config()
    url = f"http://{cfg.host}:{cfg.port}/api/command"

    payload = {
        "type": command_type,
        "params": params,
    }

    if cfg.unity_instance:
        payload["unity_instance"] = cfg.unity_instance

    try:
        async with httpx.AsyncClient() as client:
            response = await client.post(
                url,
                json=payload,
                timeout=timeout or cfg.timeout,
            )
            response.raise_for_status()
            return response.json()
    except httpx.ConnectError as e:
        raise UnityConnectionError(
            f"Cannot connect to Unity MCP server at {cfg.host}:{cfg.port}. "
            f"Make sure the server is running and Unity is connected.\n"
            f"Error: {e}"
        )
    except httpx.TimeoutException:
        raise UnityConnectionError(
            f"Connection to Unity timed out after {timeout or cfg.timeout}s. "
            f"Unity may be busy or unresponsive."
        )
    except httpx.HTTPStatusError as e:
        raise UnityConnectionError(
            f"HTTP error from server: {e.response.status_code} - {e.response.text}"
        )
    except Exception as e:
        raise UnityConnectionError(f"Unexpected error: {e}")


def run_command(
    command_type: str,
    params: Dict[str, Any],
    config: Optional[CLIConfig] = None,
    timeout: Optional[int] = None,
) -> Dict[str, Any]:
    """Synchronous wrapper for send_command.

    Args:
        command_type: The command type
        params: Command parameters
        config: Optional CLI configuration
        timeout: Optional timeout override

    Returns:
        Response dict from Unity
    """
    return asyncio.run(send_command(command_type, params, config, timeout))


async def check_connection(config: Optional[CLIConfig] = None) -> bool:
    """Check if we can connect to the Unity MCP server.

    Args:
        config: Optional CLI configuration

    Returns:
        True if connection successful, False otherwise
    """
    cfg = config or get_config()
    url = f"http://{cfg.host}:{cfg.port}/health"

    try:
        async with httpx.AsyncClient() as client:
            response = await client.get(url, timeout=5)
            return response.status_code == 200
    except Exception:
        return False


def run_check_connection(config: Optional[CLIConfig] = None) -> bool:
    """Synchronous wrapper for check_connection."""
    return asyncio.run(check_connection(config))


async def list_unity_instances(config: Optional[CLIConfig] = None) -> Dict[str, Any]:
    """List available Unity instances.

    Args:
        config: Optional CLI configuration

    Returns:
        Dict with list of Unity instances
    """
    cfg = config or get_config()

    # Try the new /api/instances endpoint first, fall back to /plugin/sessions
    urls_to_try = [
        f"http://{cfg.host}:{cfg.port}/api/instances",
        f"http://{cfg.host}:{cfg.port}/plugin/sessions",
    ]

    async with httpx.AsyncClient() as client:
        for url in urls_to_try:
            try:
                response = await client.get(url, timeout=10)
                if response.status_code == 200:
                    data = response.json()
                    # Normalize response format
                    if "instances" in data:
                        return data
                    elif "sessions" in data:
                        # Convert sessions format to instances format
                        instances = []
                        for session_id, details in data["sessions"].items():
                            instances.append({
                                "session_id": session_id,
                                "project": details.get("project", "Unknown"),
                                "hash": details.get("hash", ""),
                                "unity_version": details.get("unity_version", "Unknown"),
                                "connected_at": details.get("connected_at", ""),
                            })
                        return {"success": True, "instances": instances}
            except Exception:
                continue

    raise UnityConnectionError(
        "Failed to list Unity instances: No working endpoint found")


def run_list_instances(config: Optional[CLIConfig] = None) -> Dict[str, Any]:
    """Synchronous wrapper for list_unity_instances."""
    return asyncio.run(list_unity_instances(config))
