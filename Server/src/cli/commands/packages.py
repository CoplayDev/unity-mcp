"""Package management CLI commands."""

import click
from typing import Optional, Any

from cli.utils.config import get_config
from cli.utils.output import format_output, print_error, print_success, print_info
from cli.utils.connection import run_command, handle_unity_errors


@click.group()
def packages():
    """Package management - install, remove, search, registries."""
    pass


@packages.command("add")
@click.argument("package")
@handle_unity_errors
def add_package(package: str):
    """Install a package.

    \b
    Examples:
        unity-mcp packages add com.unity.inputsystem
        unity-mcp packages add com.unity.inputsystem@1.8.0
        unity-mcp packages add https://github.com/user/repo.git
    """
    config = get_config()
    result = run_command("manage_packages", {"action": "add_package", "package": package}, config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        job_id = (result.get("data") or {}).get("job_id")
        if job_id:
            print_info(f"Installation started. Poll with: unity-mcp packages status {job_id}")
        else:
            print_success(f"Package added: {package}")


@packages.command("remove")
@click.argument("package")
@click.option("--force", "-f", is_flag=True, help="Force removal even if other packages depend on it.")
@handle_unity_errors
def remove_package(package: str, force: bool):
    """Remove a package.

    \b
    Examples:
        unity-mcp packages remove com.unity.inputsystem
        unity-mcp packages remove com.unity.inputsystem --force
    """
    config = get_config()
    params: dict[str, Any] = {"action": "remove_package", "package": package}
    if force:
        params["force"] = True
    result = run_command("manage_packages", params, config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        job_id = (result.get("data") or {}).get("job_id")
        if job_id:
            print_info(f"Removal started. Poll with: unity-mcp packages status {job_id}")
        else:
            print_success(f"Package removed: {package}")


@packages.command("list")
@handle_unity_errors
def list_packages():
    """List installed packages.

    \b
    Examples:
        unity-mcp packages list
    """
    config = get_config()
    result = run_command("manage_packages", {"action": "list_packages"}, config)
    click.echo(format_output(result, config.format))


@packages.command("search")
@click.argument("query")
@handle_unity_errors
def search_packages(query: str):
    """Search Unity package registry.

    \b
    Examples:
        unity-mcp packages search input
        unity-mcp packages search xr
    """
    config = get_config()
    result = run_command("manage_packages", {"action": "search_packages", "query": query}, config)
    click.echo(format_output(result, config.format))


@packages.command("info")
@click.argument("package")
@handle_unity_errors
def get_info(package: str):
    """Get detailed package info.

    \b
    Examples:
        unity-mcp packages info com.unity.inputsystem
    """
    config = get_config()
    result = run_command("manage_packages", {"action": "get_package_info", "package": package}, config)
    click.echo(format_output(result, config.format))


@packages.command("status")
@click.argument("job_id", required=False)
@handle_unity_errors
def status(job_id: Optional[str]):
    """Check package operation status.

    \b
    Examples:
        unity-mcp packages status
        unity-mcp packages status abc123
    """
    config = get_config()
    params: dict[str, Any] = {"action": "status"}
    if job_id:
        params["job_id"] = job_id
    result = run_command("manage_packages", params, config)
    click.echo(format_output(result, config.format))


@packages.command("embed")
@click.argument("package")
@handle_unity_errors
def embed_package(package: str):
    """Embed a package for local editing.

    \b
    Examples:
        unity-mcp packages embed com.unity.timeline
    """
    config = get_config()
    result = run_command("manage_packages", {"action": "embed_package", "package": package}, config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        job_id = (result.get("data") or {}).get("job_id")
        if job_id:
            print_info(f"Embedding started. Poll with: unity-mcp packages status {job_id}")
        else:
            print_success(f"Package embedded: {package}")


@packages.command("resolve")
@handle_unity_errors
def resolve():
    """Force re-resolution of all packages.

    \b
    Examples:
        unity-mcp packages resolve
    """
    config = get_config()
    result = run_command("manage_packages", {"action": "resolve_packages"}, config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success("Packages resolved")


@packages.command("add-registry")
@click.argument("registry_name")
@click.option("--url", required=True, help="Registry URL.")
@click.option("--scope", "-s", multiple=True, required=True, help="Package scope (can specify multiple).")
@handle_unity_errors
def add_registry(registry_name: str, url: str, scope: tuple):
    """Add a scoped registry.

    \b
    Examples:
        unity-mcp packages add-registry OpenUPM --url https://package.openupm.com --scope com.cysharp --scope com.neuecc
    """
    config = get_config()
    result = run_command("manage_packages", {
        "action": "add_registry",
        "name": registry_name,
        "url": url,
        "scopes": list(scope),
    }, config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success(f"Registry added: {registry_name}")


@packages.command("remove-registry")
@click.argument("registry_name")
@handle_unity_errors
def remove_registry(registry_name: str):
    """Remove a scoped registry.

    \b
    Examples:
        unity-mcp packages remove-registry OpenUPM
    """
    config = get_config()
    result = run_command("manage_packages", {
        "action": "remove_registry",
        "name": registry_name,
    }, config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success(f"Registry removed: {registry_name}")


@packages.command("ping")
@handle_unity_errors
def ping():
    """Check package manager status.

    \b
    Examples:
        unity-mcp packages ping
    """
    config = get_config()
    result = run_command("manage_packages", {"action": "ping"}, config)
    click.echo(format_output(result, config.format))
