"""Asset CLI commands."""

import sys
import json
import click
from typing import Optional, Any

from cli.utils.config import get_config
from cli.utils.output import format_output, print_error, print_success
from cli.utils.connection import run_command, handle_unity_errors
from cli.utils.parsers import parse_json_dict_or_exit
from cli.utils.confirmation import confirm_destructive_action


@click.group()
def asset():
    """Asset operations - search, import, create, delete assets."""
    pass


@asset.command("search")
@click.argument("pattern", default="*")
@click.option(
    "--path", "-p",
    default="Assets",
    help="Folder path to search in."
)
@click.option(
    "--type", "-t",
    "filter_type",
    default=None,
    help="Filter by asset type (e.g., Material, Prefab, MonoScript)."
)
@click.option(
    "--limit", "-l",
    default=25,
    type=int,
    help="Maximum results per page."
)
@click.option(
    "--page",
    default=1,
    type=int,
    help="Page number (1-based)."
)
@handle_unity_errors
def search(pattern: str, path: str, filter_type: Optional[str], limit: int, page: int):
    """Search for assets.

    \b
    Examples:
        unity-mcp asset search "*.prefab"
        unity-mcp asset search "Player*" --path "Assets/Characters"
        unity-mcp asset search "*" --type Material
        unity-mcp asset search "t:MonoScript" --path "Assets/Scripts"
    """
    config = get_config()

    params: dict[str, Any] = {
        "action": "search",
        "path": path,
        "searchPattern": pattern,
        "pageSize": limit,
        "pageNumber": page,
    }

    if filter_type:
        params["filterType"] = filter_type

    result = run_command("manage_asset", params, config)
    click.echo(format_output(result, config.format))


@asset.command("info")
@click.argument("path")
@click.option(
    "--preview",
    is_flag=True,
    help="Generate preview thumbnail (may be large)."
)
@handle_unity_errors
def info(path: str, preview: bool):
    """Get detailed information about an asset.

    \b
    Examples:
        unity-mcp asset info "Assets/Materials/Red.mat"
        unity-mcp asset info "Assets/Prefabs/Player.prefab" --preview
    """
    config = get_config()

    params: dict[str, Any] = {
        "action": "get_info",
        "path": path,
        "generatePreview": preview,
    }

    result = run_command("manage_asset", params, config)
    click.echo(format_output(result, config.format))


@asset.command("create")
@click.argument("path")
@click.argument("asset_type")
@click.option(
    "--properties", "-p",
    default=None,
    help='Initial properties as JSON.'
)
@handle_unity_errors
def create(path: str, asset_type: str, properties: Optional[str]):
    """Create a new asset.

    \b
    Examples:
        unity-mcp asset create "Assets/Materials/Blue.mat" Material
        unity-mcp asset create "Assets/NewFolder" Folder
        unity-mcp asset create "Assets/Materials/Custom.mat" Material --properties '{"color": [0,0,1,1]}'
    """
    config = get_config()

    params: dict[str, Any] = {
        "action": "create",
        "path": path,
        "assetType": asset_type,
    }

    if properties:
        params["properties"] = parse_json_dict_or_exit(properties, "properties")

    result = run_command("manage_asset", params, config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success(f"Created {asset_type}: {path}")


@asset.command("delete")
@click.argument("path")
@click.option(
    "--force", "-f",
    is_flag=True,
    help="Skip confirmation prompt."
)
@handle_unity_errors
def delete(path: str, force: bool):
    """Delete an asset.

    \b
    Examples:
        unity-mcp asset delete "Assets/OldMaterial.mat"
        unity-mcp asset delete "Assets/Unused" --force
    """
    config = get_config()

    confirm_destructive_action("Delete", "asset", path, force)

    result = run_command(
        "manage_asset", {"action": "delete", "path": path}, config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success(f"Deleted: {path}")


@asset.command("duplicate")
@click.argument("source")
@click.argument("destination")
@handle_unity_errors
def duplicate(source: str, destination: str):
    """Duplicate an asset.

    \b
    Examples:
        unity-mcp asset duplicate "Assets/Materials/Red.mat" "Assets/Materials/RedCopy.mat"
    """
    config = get_config()

    params: dict[str, Any] = {
        "action": "duplicate",
        "path": source,
        "destination": destination,
    }

    result = run_command("manage_asset", params, config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success(f"Duplicated to: {destination}")


@asset.command("move")
@click.argument("source")
@click.argument("destination")
@handle_unity_errors
def move(source: str, destination: str):
    """Move an asset to a new location.

    \b
    Examples:
        unity-mcp asset move "Assets/Old/Material.mat" "Assets/New/Material.mat"
    """
    config = get_config()

    params: dict[str, Any] = {
        "action": "move",
        "path": source,
        "destination": destination,
    }

    result = run_command("manage_asset", params, config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success(f"Moved to: {destination}")


@asset.command("rename")
@click.argument("path")
@click.argument("new_name")
@handle_unity_errors
def rename(path: str, new_name: str):
    """Rename an asset.

    \b
    Examples:
        unity-mcp asset rename "Assets/Materials/Old.mat" "New.mat"
    """
    config = get_config()

    # Construct destination path
    import os
    dir_path = os.path.dirname(path)
    destination = os.path.join(dir_path, new_name).replace("\\", "/")

    params: dict[str, Any] = {
        "action": "rename",
        "path": path,
        "destination": destination,
    }

    result = run_command("manage_asset", params, config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success(f"Renamed to: {new_name}")


@asset.command("import")
@click.argument("path")
@handle_unity_errors
def import_asset(path: str):
    """Import/reimport an asset.

    \b
    Examples:
        unity-mcp asset import "Assets/Textures/NewTexture.png"
    """
    config = get_config()

    result = run_command(
        "manage_asset", {"action": "import", "path": path}, config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success(f"Imported: {path}")


@asset.command("mkdir")
@click.argument("path")
@handle_unity_errors
def mkdir(path: str):
    """Create a folder.

    \b
    Examples:
        unity-mcp asset mkdir "Assets/NewFolder"
        unity-mcp asset mkdir "Assets/Levels/Chapter1"
    """
    config = get_config()

    result = run_command(
        "manage_asset", {"action": "create_folder", "path": path}, config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success(f"Created folder: {path}")
