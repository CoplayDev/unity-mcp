"""Asset Store package management CLI commands."""

import click
from typing import Optional, Any

from cli.utils.config import get_config
from cli.utils.output import format_output, print_success
from cli.utils.connection import run_command, handle_unity_errors


@click.group("asset-store")
def asset_store():
    """Asset Store packages - list purchases, download, import."""
    pass


@asset_store.command("auth")
@handle_unity_errors
def check_auth():
    """Check Unity account login status.

    \b
    Examples:
        unity-mcp asset-store auth
    """
    config = get_config()
    result = run_command("manage_asset_store", {"action": "check_auth"}, config)
    click.echo(format_output(result, config.format))


@asset_store.command("list")
@click.option("--page", type=int, default=None, help="Page number (1-based).")
@click.option("--page-size", type=int, default=None, help="Results per page.")
@handle_unity_errors
def list_purchases(page: Optional[int], page_size: Optional[int]):
    """List purchased Asset Store packages.

    \b
    Examples:
        unity-mcp asset-store list
        unity-mcp asset-store list --page 1 --page-size 20
    """
    config = get_config()
    params: dict[str, Any] = {"action": "list_purchases"}
    if page is not None:
        params["page"] = page
    if page_size is not None:
        params["page_size"] = page_size
    result = run_command("manage_asset_store", params, config)
    click.echo(format_output(result, config.format))


@asset_store.command("download")
@click.argument("product_id", type=int)
@handle_unity_errors
def download(product_id: int):
    """Download an Asset Store package.

    \b
    Examples:
        unity-mcp asset-store download 12345
    """
    config = get_config()
    result = run_command("manage_asset_store", {"action": "download", "product_id": product_id}, config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success("Download complete.")


@asset_store.command("import")
@click.argument("product_id", type=int)
@handle_unity_errors
def import_package(product_id: int):
    """Import an already-downloaded Asset Store package.

    \b
    Examples:
        unity-mcp asset-store import 12345
    """
    config = get_config()
    result = run_command("manage_asset_store", {"action": "import", "product_id": product_id}, config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success("Package imported.")
