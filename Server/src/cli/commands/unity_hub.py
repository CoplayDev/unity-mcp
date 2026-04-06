"""Unity Hub CLI commands — runs on host, does not require Unity Editor."""

import asyncio
import json

import click

from cli.utils.output import print_info, print_success
from services.unity_hub import (
    detect_hub_path,
    parse_available_releases,
    parse_installed_editors,
    run_hub_command,
)


@click.group("hub")
def unity_hub():
    """Unity Hub operations - editors, releases, install path (host-side, no Unity needed)."""
    pass


def _run_async(coro):
    """Run an async function synchronously."""
    try:
        loop = asyncio.get_running_loop()
    except RuntimeError:
        loop = None
    if loop and loop.is_running():
        import concurrent.futures
        with concurrent.futures.ThreadPoolExecutor() as pool:
            return pool.submit(asyncio.run, coro).result()
    return asyncio.run(coro)


def _print_result(result: dict) -> None:
    click.echo(json.dumps(result, indent=2))


@unity_hub.command("info")
def info() -> None:
    """Show detected Unity Hub and host information."""
    import platform as _platform
    hub_path = detect_hub_path()
    _print_result({
        "hub_detected": hub_path is not None,
        "hub_path": hub_path,
        "os": _platform.system(),
        "architecture": _platform.machine(),
    })


@unity_hub.command("editors")
def editors() -> None:
    """List locally installed Unity Editor versions."""
    result = _run_async(run_hub_command(["editors", "--installed"]))
    if not result["success"]:
        _print_result(result)
        return
    parsed = parse_installed_editors(result["raw_output"])
    for editor in parsed:
        click.echo(f"  {editor['version']}  ->  {editor['path']}")
    if not parsed:
        click.echo("  No editors installed.")


@unity_hub.command("releases")
@click.option("--limit", type=int, default=None, help="Maximum number of releases to return.")
def releases(limit: int | None) -> None:
    """List Unity Editor releases available through Unity Hub."""
    result = _run_async(run_hub_command(["editors", "--releases"]))
    if not result["success"]:
        _print_result(result)
        return
    parsed = parse_available_releases(result["raw_output"], limit)
    for release in parsed:
        channel = f" ({release['channel']})" if "channel" in release else ""
        click.echo(f"  {release['version']}{channel}")


@unity_hub.command("install-path")
@click.option("--set", "new_path", type=str, default=None, help="Set the Unity Editor install path.")
def install_path(new_path: str | None) -> None:
    """Get or set the Unity Editor install path."""
    if new_path is None:
        result = _run_async(run_hub_command(["install-path", "--get"]))
        if result["success"]:
            click.echo(f"  Install path: {result['raw_output']}")
        else:
            _print_result(result)
        return

    click.confirm(f"Change Unity Editor install path to '{new_path}'?", abort=True)
    result = _run_async(run_hub_command(["install-path", "--set", new_path]))
    if result["success"]:
        print_success(f"Install path changed to: {new_path}")
    else:
        _print_result(result)


@unity_hub.command("install")
@click.argument("version")
def install(version: str) -> None:
    """Download and install a Unity Editor version via Unity Hub."""
    click.confirm(f"Install Unity Editor {version}?", abort=True)
    result = _run_async(run_hub_command(["install", "--version", version], timeout=600))
    if result["success"]:
        print_success(f"Unity Editor {version} installation started.")
        print_info("Unity Hub may continue the install in the background.")
    else:
        _print_result(result)


@unity_hub.command("install-modules")
@click.argument("version")
@click.option("--modules", "-m", multiple=True, required=True, help="Module to install (can repeat: -m android -m ios).")
def install_modules(version: str, modules: tuple[str, ...]) -> None:
    """Install platform modules for an existing Unity Editor version."""
    module_list = list(modules)
    click.confirm(f"Install modules [{', '.join(module_list)}] for Unity {version}?", abort=True)
    args = ["install-modules", "--version", version]
    for mod in module_list:
        args.extend(["--module", mod])
    result = _run_async(run_hub_command(args, timeout=600))
    if result["success"]:
        print_success(f"Module installation started for Unity {version}.")
        print_info("Unity Hub may continue the module install in the background.")
    else:
        _print_result(result)
