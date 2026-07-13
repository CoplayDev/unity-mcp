"""AI asset generation CLI commands (3D model gen/import, 2D image gen, audio gen).

Thin pass-through to Unity over HTTP: these commands carry NO API keys and NO
file bytes. The C# side reads provider keys from the OS secure store, performs
the provider call, and imports the result.
"""

import click
from typing import Optional, Any

from cli.utils.config import get_config
from cli.utils.output import format_output, print_info
from cli.utils.connection import run_command, handle_unity_errors


@click.group(name="asset-gen")
def asset_gen():
    """AI asset generation - generate 3D models, import marketplace models, generate images."""
    pass


def _emit(result, config, verb):
    """Echo the command result, then (on success with a job_id) print the status-poll hint."""
    click.echo(format_output(result, config.format))
    if result.get("success"):
        job_id = (result.get("data") or {}).get("job_id")
        if job_id:
            print_info(f"{verb} started. Poll with: unity-mcp asset-gen status --job-id {job_id}")


@asset_gen.command("generate-model")
@click.option("--provider", default=None, help="Provider id (tripo, meshy).")
@click.option("--mode", default=None, help="Generation mode: text or image.")
@click.option("--prompt", default=None, help="Text prompt for text->3D.")
@click.option("--image-path", default=None, help="Source image path for image->3D.")
@click.option("--image-url", default=None, help="Source image URL for image->3D.")
@click.option("--format", "fmt", default=None, help="Output format: glb, fbx, obj, usdz.")
@click.option("--target-size", default=None, type=float, help="Normalize largest dimension (meters).")
@click.option("--texture/--no-texture", "texture", default=None, help="Generate textures.")
@click.option("--tier", default=None, help="Provider quality/cost tier.")
@click.option("--model", default=None, help="Provider model id/version (omit for the GUI-selected default).")
@click.option("--name", default=None, help="Base name for the imported asset.")
@click.option("--output-folder", default=None, help="Destination folder under Assets/.")
@handle_unity_errors
def generate_model(
    provider: Optional[str],
    mode: Optional[str],
    prompt: Optional[str],
    image_path: Optional[str],
    image_url: Optional[str],
    fmt: Optional[str],
    target_size: Optional[float],
    texture: Optional[bool],
    tier: Optional[str],
    model: Optional[str],
    name: Optional[str],
    output_folder: Optional[str],
):
    """Generate a 3D model with an AI provider.

    \b
    Examples:
        unity-mcp asset-gen generate-model --provider tripo --mode text --prompt "a red chair"
        unity-mcp asset-gen generate-model --provider meshy --mode image --image-path Assets/ref.png
    """
    config = get_config()

    params: dict[str, Any] = {"action": "generate"}
    optional = {
        "provider": provider,
        "mode": mode,
        "prompt": prompt,
        "imagePath": image_path,
        "imageUrl": image_url,
        "format": fmt,
        "targetSize": target_size,
        "texture": texture,
        "tier": tier,
        "model": model,
        "name": name,
        "outputFolder": output_folder,
    }
    params.update({k: v for k, v in optional.items() if v is not None})

    result = run_command("generate_model", params, config)
    _emit(result, config, "Generation")


@asset_gen.command("import-model")
@click.option("--uid", required=True, help="Sketchfab model uid to import.")
@click.option("--target-size", default=None, type=float, help="Normalize largest dimension (meters).")
@click.option("--name", default=None, help="Base name for the imported asset.")
@click.option("--output-folder", default=None, help="Destination folder under Assets/.")
@handle_unity_errors
def import_model(
    uid: str,
    target_size: Optional[float],
    name: Optional[str],
    output_folder: Optional[str],
):
    """Import a 3D model from the Sketchfab marketplace by uid.

    \b
    Examples:
        unity-mcp asset-gen import-model --uid abc123
        unity-mcp asset-gen import-model --uid abc123 --name MyProp --output-folder Assets/Props
    """
    config = get_config()

    params: dict[str, Any] = {"action": "import", "uid": uid}
    optional = {
        "targetSize": target_size,
        "name": name,
        "outputFolder": output_folder,
    }
    params.update({k: v for k, v in optional.items() if v is not None})

    result = run_command("import_model", params, config)
    _emit(result, config, "Import")


@asset_gen.command("import-model-file")
@click.option("--source-path", "source_path", required=True,
              help="Path to a local model file (.fbx/.obj/.glb/.gltf/.zip).")
@click.option("--name", default=None, help="Base name for the imported asset.")
@click.option("--output-folder", default=None, help="Destination folder under Assets/.")
@click.option("--target-size", default=None, type=float, help="Normalize largest dimension (meters).")
@click.option("--animation-type", "animation_type", default=None,
              type=click.Choice(["none", "generic", "humanoid", "legacy"]),
              help="FBX/OBJ rig mode: generic/humanoid surface animation clips; "
                   "legacy selects Unity's legacy Animation system (glTF ignores this).")
@handle_unity_errors
def import_model_file(source_path, name, output_folder, target_size, animation_type):
    """Import a local 3D model file (e.g. a Blender export) into the Unity project."""
    config = get_config()
    params = {
        "sourcePath": source_path,
        "name": name,
        "outputFolder": output_folder,
        "targetSize": target_size,
        "animationType": animation_type,
    }
    params = {k: v for k, v in params.items() if v is not None}
    result = run_command("import_model_file", params, config)
    click.echo(format_output(result, config.format))


@asset_gen.command("generate-image")
@click.option("--provider", default=None, help="Provider id (fal, openrouter).")
@click.option("--mode", default=None, help="Generation mode: text or image.")
@click.option("--prompt", default=None, help="Text prompt for text->image.")
@click.option("--image-path", default=None, help="Source image path for image->image.")
@click.option("--image-url", default=None, help="Source image URL for image->image.")
@click.option("--model", default=None, help="Provider model id/slug.")
@click.option("--transparent/--no-transparent", "transparent", default=None, help="Request transparency.")
@click.option("--width", default=None, type=int, help="Output width in pixels.")
@click.option("--height", default=None, type=int, help="Output height in pixels.")
@click.option("--name", default=None, help="Base name for the imported asset.")
@click.option("--output-folder", default=None, help="Destination folder under Assets/.")
@handle_unity_errors
def generate_image(
    provider: Optional[str],
    mode: Optional[str],
    prompt: Optional[str],
    image_path: Optional[str],
    image_url: Optional[str],
    model: Optional[str],
    transparent: Optional[bool],
    width: Optional[int],
    height: Optional[int],
    name: Optional[str],
    output_folder: Optional[str],
):
    """Generate a 2D image with an AI provider.

    \b
    Examples:
        unity-mcp asset-gen generate-image --provider fal --prompt "a stone texture"
        unity-mcp asset-gen generate-image --provider openrouter --prompt "logo" --transparent
    """
    config = get_config()

    params: dict[str, Any] = {"action": "generate"}
    optional = {
        "provider": provider,
        "mode": mode,
        "prompt": prompt,
        "imagePath": image_path,
        "imageUrl": image_url,
        "model": model,
        "transparent": transparent,
        "width": width,
        "height": height,
        "name": name,
        "outputFolder": output_folder,
    }
    params.update({k: v for k, v in optional.items() if v is not None})

    result = run_command("generate_image", params, config)
    _emit(result, config, "Generation")


@asset_gen.command("generate-audio")
@click.option("--provider", default=None, help="Provider id (fal).")
@click.option("--prompt", default=None, help="Text prompt describing the sound or music.")
@click.option("--model", default=None, help="fal model id (omit for the GUI-selected default).")
@click.option("--duration", default=None, type=float, help="Requested length in seconds (soft-clamped per model).")
@click.option("--name", default=None, help="Base name for the imported asset.")
@click.option("--output-folder", default=None, help="Destination folder under Assets/.")
@handle_unity_errors
def generate_audio(
    provider: Optional[str],
    prompt: Optional[str],
    model: Optional[str],
    duration: Optional[float],
    name: Optional[str],
    output_folder: Optional[str],
):
    """Generate audio (SFX / music) with a fal.ai model.

    \b
    Examples:
        unity-mcp asset-gen generate-audio --provider fal --prompt "8-bit coin pickup"
        unity-mcp asset-gen generate-audio --prompt "calm ambient loop" --duration 30
    """
    config = get_config()

    params: dict[str, Any] = {"action": "generate"}
    optional = {
        "provider": provider,
        "prompt": prompt,
        "model": model,
        "duration": duration,
        "name": name,
        "outputFolder": output_folder,
    }
    params.update({k: v for k, v in optional.items() if v is not None})

    result = run_command("generate_audio", params, config)
    _emit(result, config, "Generation")


@asset_gen.command("status")
@click.option("--job-id", "job_id", required=True, help="Job id returned by a generate/import command.")
@handle_unity_errors
def status(job_id: str):
    """Check the status of an asset generation/import job.

    \b
    Examples:
        unity-mcp asset-gen status --job-id abc123
    """
    config = get_config()
    result = run_command("generate_model", {"action": "status", "jobId": job_id}, config)
    click.echo(format_output(result, config.format))
