"""Asset CLI commands."""

import sys
import json
import click
from typing import Optional, Any

from cli.utils.config import get_config
from cli.utils.output import format_output, print_error, print_success
from cli.utils.connection import run_command, UnityConnectionError


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

    try:
        result = run_command("manage_asset", params, config)
        click.echo(format_output(result, config.format))
    except UnityConnectionError as e:
        print_error(str(e))
        sys.exit(1)


@asset.command("info")
@click.argument("path")
@click.option(
    "--preview",
    is_flag=True,
    help="Generate preview thumbnail (may be large)."
)
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

    try:
        result = run_command("manage_asset", params, config)
        click.echo(format_output(result, config.format))
    except UnityConnectionError as e:
        print_error(str(e))
        sys.exit(1)


@asset.command("create")
@click.argument("path")
@click.argument("asset_type")
@click.option(
    "--properties", "-p",
    default=None,
    help='Initial properties as JSON.'
)
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
        try:
            params["properties"] = json.loads(properties)
        except json.JSONDecodeError as e:
            print_error(f"Invalid JSON for properties: {e}")
            sys.exit(1)

    try:
        result = run_command("manage_asset", params, config)
        click.echo(format_output(result, config.format))
        if result.get("success"):
            print_success(f"Created {asset_type}: {path}")
    except UnityConnectionError as e:
        print_error(str(e))
        sys.exit(1)


@asset.command("delete")
@click.argument("path")
@click.option(
    "--force", "-f",
    is_flag=True,
    help="Skip confirmation prompt."
)
def delete(path: str, force: bool):
    """Delete an asset.

    \b
    Examples:
        unity-mcp asset delete "Assets/OldMaterial.mat"
        unity-mcp asset delete "Assets/Unused" --force
    """
    config = get_config()

    if not force:
        click.confirm(f"Delete asset '{path}'?", abort=True)

    try:
        result = run_command(
            "manage_asset", {"action": "delete", "path": path}, config)
        click.echo(format_output(result, config.format))
        if result.get("success"):
            print_success(f"Deleted: {path}")
    except UnityConnectionError as e:
        print_error(str(e))
        sys.exit(1)


@asset.command("duplicate")
@click.argument("source")
@click.argument("destination")
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

    try:
        result = run_command("manage_asset", params, config)
        click.echo(format_output(result, config.format))
        if result.get("success"):
            print_success(f"Duplicated to: {destination}")
    except UnityConnectionError as e:
        print_error(str(e))
        sys.exit(1)


@asset.command("move")
@click.argument("source")
@click.argument("destination")
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

    try:
        result = run_command("manage_asset", params, config)
        click.echo(format_output(result, config.format))
        if result.get("success"):
            print_success(f"Moved to: {destination}")
    except UnityConnectionError as e:
        print_error(str(e))
        sys.exit(1)


@asset.command("rename")
@click.argument("path")
@click.argument("new_name")
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

    try:
        result = run_command("manage_asset", params, config)
        click.echo(format_output(result, config.format))
        if result.get("success"):
            print_success(f"Renamed to: {new_name}")
    except UnityConnectionError as e:
        print_error(str(e))
        sys.exit(1)


@asset.command("import")
@click.argument("path")
def import_asset(path: str):
    """Import/reimport an asset.

    \b
    Examples:
        unity-mcp asset import "Assets/Textures/NewTexture.png"
    """
    config = get_config()

    try:
        result = run_command(
            "manage_asset", {"action": "import", "path": path}, config)
        click.echo(format_output(result, config.format))
        if result.get("success"):
            print_success(f"Imported: {path}")
    except UnityConnectionError as e:
        print_error(str(e))
        sys.exit(1)


@asset.command("mkdir")
@click.argument("path")
def mkdir(path: str):
    """Create a folder.

    \b
    Examples:
        unity-mcp asset mkdir "Assets/NewFolder"
        unity-mcp asset mkdir "Assets/Levels/Chapter1"
    """
    config = get_config()

    try:
        result = run_command(
            "manage_asset", {"action": "create_folder", "path": path}, config)
        click.echo(format_output(result, config.format))
        if result.get("success"):
            print_success(f"Created folder: {path}")
    except UnityConnectionError as e:
        print_error(str(e))
        sys.exit(1)
