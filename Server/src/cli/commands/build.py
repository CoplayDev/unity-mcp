"""Build management CLI commands."""

import click
from typing import Optional

from cli.utils.config import get_config
from cli.utils.output import format_output, print_info
from cli.utils.connection import run_command, handle_unity_errors


@click.group()
def build():
    """Build management - player builds, platforms, settings, batch."""
    pass


@build.command("run")
@click.option("--target", "-t", help="Build target: windows64, osx, linux64, android, ios, webgl")
@click.option("--output", "-o", "output_path", help="Output path")
@click.option("--development", "-d", is_flag=True, help="Development build")
@click.option("--backend", "scripting_backend", type=click.Choice(["mono", "il2cpp"]), help="Scripting backend")
@click.option("--subtarget", type=click.Choice(["player", "server"]), help="Build subtarget")
@click.option("--profile", help="Build Profile asset path (Unity 6+)")
@click.option("--clean", is_flag=True, help="Clean build cache")
@click.option("--auto-run", is_flag=True, help="Auto-run after build")
@handle_unity_errors
def run_build(target, output_path, development, scripting_backend, subtarget, profile, clean, auto_run):
    """Trigger a player build.

    \b
    Examples:
        unity-mcp build run --target windows64 --development
        unity-mcp build run --target android --backend il2cpp
        unity-mcp build run --profile "Assets/Settings/Build Profiles/iOS.asset"
    """
    config = get_config()
    params = {"action": "build"}
    if target:
        params["target"] = target
    if output_path:
        params["output_path"] = output_path
    if development:
        params["development"] = True
    if scripting_backend:
        params["scripting_backend"] = scripting_backend
    if subtarget:
        params["subtarget"] = subtarget
    if profile:
        params["profile"] = profile

    options = []
    if clean:
        options.append("clean_build")
    if auto_run:
        options.append("auto_run")
    if options:
        params["options"] = options

    result = run_command("manage_build", params, config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        job_id = (result.get("data") or {}).get("job_id")
        if job_id:
            print_info(f"Build started. Poll with: unity-mcp build status {job_id}")


@build.command("status")
@click.argument("job_id", required=False)
@handle_unity_errors
def status(job_id: Optional[str]):
    """Check build status or get last build report.

    \b
    Examples:
        unity-mcp build status
        unity-mcp build status build-abc123
    """
    config = get_config()
    params = {"action": "status"}
    if job_id:
        params["job_id"] = job_id
    result = run_command("manage_build", params, config)
    click.echo(format_output(result, config.format))


@build.command("platform")
@click.argument("target", required=False)
@click.option("--subtarget", type=click.Choice(["player", "server"]), help="Build subtarget")
@handle_unity_errors
def platform(target: Optional[str], subtarget: Optional[str]):
    """Read or switch the active build platform.

    \b
    Examples:
        unity-mcp build platform
        unity-mcp build platform android
        unity-mcp build platform windows64 --subtarget server
    """
    config = get_config()
    params = {"action": "platform"}
    if target:
        params["target"] = target
    if subtarget:
        params["subtarget"] = subtarget
    result = run_command("manage_build", params, config)
    click.echo(format_output(result, config.format))


@build.command("settings")
@click.argument("property_name")
@click.option("--value", "-v", help="Value to set. Omit to read.")
@click.option("--target", "-t", help="Build target for platform-specific settings")
@handle_unity_errors
def settings(property_name: str, value: Optional[str], target: Optional[str]):
    """Read or write player settings.

    \b
    Properties: product_name, company_name, version, bundle_id,
                scripting_backend, defines, architecture

    \b
    Examples:
        unity-mcp build settings product_name
        unity-mcp build settings product_name --value "My Game"
        unity-mcp build settings scripting_backend --value il2cpp --target android
    """
    config = get_config()
    params = {"action": "settings", "property": property_name}
    if value:
        params["value"] = value
    if target:
        params["target"] = target
    result = run_command("manage_build", params, config)
    click.echo(format_output(result, config.format))


@build.command("scenes")
@click.option("--set", "scene_paths", help="Comma-separated scene paths to set")
@handle_unity_errors
def scenes(scene_paths: Optional[str]):
    """Read or update the build scene list.

    \b
    Examples:
        unity-mcp build scenes
        unity-mcp build scenes --set "Assets/Scenes/Main.unity,Assets/Scenes/Level1.unity"
    """
    config = get_config()
    params = {"action": "scenes"}
    if scene_paths:
        scene_list = [
            {"path": p.strip(), "enabled": True}
            for p in scene_paths.split(",")
        ]
        params["scenes"] = scene_list
    result = run_command("manage_build", params, config)
    click.echo(format_output(result, config.format))


@build.command("profiles")
@click.argument("profile", required=False)
@click.option("--activate", is_flag=True, help="Activate the specified profile")
@handle_unity_errors
def profiles_cmd(profile: Optional[str], activate: bool):
    """List, inspect, or activate build profiles (Unity 6+).

    \b
    Examples:
        unity-mcp build profiles
        unity-mcp build profiles "Assets/Settings/Build Profiles/iOS.asset"
        unity-mcp build profiles "Assets/Settings/Build Profiles/iOS.asset" --activate
    """
    config = get_config()
    params = {"action": "profiles"}
    if profile:
        params["profile"] = profile
    if activate:
        params["activate"] = True
    result = run_command("manage_build", params, config)
    click.echo(format_output(result, config.format))


@build.command("batch")
@click.option("--targets", "-t", help="Comma-separated targets: windows64,linux64,webgl")
@click.option("--profiles", "-p", "profile_paths", help="Comma-separated profile paths (Unity 6+)")
@click.option("--output-dir", "-o", help="Base output directory")
@click.option("--development", "-d", is_flag=True, help="Development build for all")
@handle_unity_errors
def batch(targets, profile_paths, output_dir, development):
    """Run batch builds across multiple platforms or profiles.

    \b
    Examples:
        unity-mcp build batch --targets windows64,linux64,webgl
        unity-mcp build batch --profiles "Assets/Profiles/A.asset,Assets/Profiles/B.asset"
        unity-mcp build batch --targets windows64,android --development
    """
    config = get_config()
    params = {"action": "batch"}
    if targets:
        params["targets"] = [t.strip() for t in targets.split(",")]
    if profile_paths:
        params["profiles"] = [p.strip() for p in profile_paths.split(",")]
    if output_dir:
        params["output_dir"] = output_dir
    if development:
        params["development"] = True
    result = run_command("manage_build", params, config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        job_id = (result.get("data") or {}).get("job_id")
        if job_id:
            print_info(f"Batch started. Poll with: unity-mcp build status {job_id}")


@build.command("cancel")
@click.argument("job_id")
@handle_unity_errors
def cancel(job_id: str):
    """Cancel a build or batch job (best-effort).

    \b
    Examples:
        unity-mcp build cancel batch-xyz789
    """
    config = get_config()
    result = run_command("manage_build", {"action": "cancel", "job_id": job_id}, config)
    click.echo(format_output(result, config.format))
