"""Texture CLI commands."""

import sys
import click
from typing import Optional, Any

from cli.utils.config import get_config
from cli.utils.output import format_output, print_error, print_success
from cli.utils.connection import run_command, handle_unity_errors
from cli.utils.parsers import parse_json_or_exit as try_parse_json


_TEXTURE_TYPES = {
    "default": "Default",
    "normal_map": "NormalMap",
    "editor_gui": "GUI",
    "sprite": "Sprite",
    "cursor": "Cursor",
    "cookie": "Cookie",
    "lightmap": "Lightmap",
    "directional_lightmap": "DirectionalLightmap",
    "shadow_mask": "Shadowmask",
    "single_channel": "SingleChannel",
}

_TEXTURE_SHAPES = {"2d": "Texture2D", "cube": "TextureCube"}

_ALPHA_SOURCES = {
    "none": "None",
    "from_input": "FromInput",
    "from_gray_scale": "FromGrayScale",
}

_WRAP_MODES = {
    "repeat": "Repeat",
    "clamp": "Clamp",
    "mirror": "Mirror",
    "mirror_once": "MirrorOnce",
}

_FILTER_MODES = {"point": "Point",
                 "bilinear": "Bilinear", "trilinear": "Trilinear"}

_COMPRESSIONS = {
    "none": "Uncompressed",
    "low_quality": "CompressedLQ",
    "normal_quality": "Compressed",
    "high_quality": "CompressedHQ",
}

_SPRITE_MODES = {"single": "Single",
                 "multiple": "Multiple", "polygon": "Polygon"}

_SPRITE_MESH_TYPES = {"full_rect": "FullRect", "tight": "Tight"}

_MIPMAP_FILTERS = {"box": "BoxFilter", "kaiser": "KaiserFilter"}

_MAX_TEXTURE_DIMENSION = 1024
_MAX_TEXTURE_PIXELS = 1024 * 1024


def _validate_texture_dimensions(width: int, height: int) -> list[str]:
    if width <= 0 or height <= 0:
        raise ValueError("width and height must be positive")
    warnings: list[str] = []
    if width > _MAX_TEXTURE_DIMENSION or height > _MAX_TEXTURE_DIMENSION:
        warnings.append(
            f"width and height should be <= {_MAX_TEXTURE_DIMENSION} (got {width}x{height})")
    total_pixels = width * height
    if total_pixels > _MAX_TEXTURE_PIXELS:
        warnings.append(
            f"width*height should be <= {_MAX_TEXTURE_PIXELS} (got {width}x{height})")
    return warnings


def _is_normalized_color(values: list[Any]) -> bool:
    if not values:
        return False

    try:
        numeric_values = [float(v) for v in values]
    except (TypeError, ValueError):
        return False

    all_small = all(0 <= v <= 1.0 for v in numeric_values)
    if not all_small:
        return False

    has_fractional = any(0 < v < 1 for v in numeric_values)
    all_binary = all(v in (0, 1, 0.0, 1.0) for v in numeric_values)

    return has_fractional or all_binary


def _parse_hex_color(value: str) -> list[int]:
    h = value.lstrip("#")
    if len(h) == 6:
        return [int(h[i:i + 2], 16) for i in (0, 2, 4)] + [255]
    if len(h) == 8:
        return [int(h[i:i + 2], 16) for i in (0, 2, 4, 6)]
    raise ValueError(f"Invalid hex color: {value}")


def _normalize_color(value: Any, context: str) -> list[int]:
    if value is None:
        raise ValueError(f"{context} is required")

    if isinstance(value, str):
        if value.startswith("#"):
            return _parse_hex_color(value)
        value = try_parse_json(value, context)

    # Handle dict with r/g/b keys (e.g., {"r": 1, "g": 0, "b": 0} or {"r": 1, "g": 0, "b": 0, "a": 1})
    if isinstance(value, dict):
        if all(k in value for k in ("r", "g", "b")):
            try:
                color = [value["r"], value["g"], value["b"]]
                if "a" in value:
                    color.append(value["a"])
                else:
                    color.append(1.0 if _is_normalized_color(color) else 255)
                if _is_normalized_color(color):
                    return [int(round(float(c) * 255)) for c in color]
                return [int(c) for c in color]
            except (TypeError, ValueError):
                raise ValueError(f"{context} dict values must be numeric, got {value}")
        raise ValueError(f"{context} dict must have 'r', 'g', 'b' keys, got {list(value.keys())}")

    if isinstance(value, (list, tuple)):
        if len(value) == 3:
            value = list(value) + [1.0 if _is_normalized_color(value) else 255]
        if len(value) == 4:
            try:
                if _is_normalized_color(value):
                    return [int(round(float(c) * 255)) for c in value]
                return [int(c) for c in value]
            except (TypeError, ValueError):
                raise ValueError(
                    f"{context} values must be numeric, got {value}")
        raise ValueError(
            f"{context} must have 3 or 4 components, got {len(value)}")

    raise ValueError(f"{context} must be a list or hex string")


def _normalize_palette(value: Any, context: str) -> list[list[int]]:
    if value is None:
        return []
    if isinstance(value, str):
        value = try_parse_json(value, context)
    if not isinstance(value, list):
        raise ValueError(f"{context} must be a list of colors")
    return [_normalize_color(color, f"{context} item") for color in value]


def _normalize_pixels(value: Any, width: int, height: int, context: str) -> list[list[int]] | str:
    if value is None:
        raise ValueError(f"{context} is required")
    if isinstance(value, str):
        if value.startswith("base64:"):
            return value
        trimmed = value.strip()
        if trimmed.startswith("[") and trimmed.endswith("]"):
            value = try_parse_json(trimmed, context)
        else:
            return f"base64:{value}"
    if isinstance(value, list):
        expected_count = width * height
        if len(value) != expected_count:
            raise ValueError(
                f"{context} must have {expected_count} entries, got {len(value)}")
        return [_normalize_color(pixel, f"{context} pixel") for pixel in value]
    raise ValueError(f"{context} must be a list or base64 string")


def _normalize_set_pixels(value: Any) -> dict[str, Any]:
    if value is None:
        raise ValueError("set-pixels is required")
    if isinstance(value, str):
        value = try_parse_json(value, "set-pixels")
    if not isinstance(value, dict):
        raise ValueError("set-pixels must be a JSON object")

    result: dict[str, Any] = dict(value)

    if "pixels" in value:
        width = value.get("width")
        height = value.get("height")
        if width is None or height is None:
            raise ValueError(
                "set-pixels requires width and height when pixels are provided")
        width = int(width)
        height = int(height)
        if width <= 0 or height <= 0:
            raise ValueError("set-pixels width and height must be positive")
        result["width"] = width
        result["height"] = height
        result["pixels"] = _normalize_pixels(
            value["pixels"], width, height, "set-pixels pixels")

    if "color" in value:
        result["color"] = _normalize_color(value["color"], "set-pixels color")

    if "pixels" not in value and "color" not in value:
        raise ValueError("set-pixels requires 'color' or 'pixels'")

    if "x" in value:
        result["x"] = int(value["x"])
    if "y" in value:
        result["y"] = int(value["y"])

    if "width" in value and "pixels" not in value:
        result["width"] = int(value["width"])
    if "height" in value and "pixels" not in value:
        result["height"] = int(value["height"])

    return result


def _map_enum(value: Any, mapping: dict[str, str]) -> Any:
    if isinstance(value, str):
        key = value.lower()
        return mapping.get(key, value)
    return value


_TRUE_STRINGS = {"true", "1", "yes", "on"}
_FALSE_STRINGS = {"false", "0", "no", "off"}


def _coerce_bool(value: Any, name: str) -> bool:
    if isinstance(value, bool):
        return value
    if isinstance(value, (int, float)) and value in (0, 1, 0.0, 1.0):
        return bool(value)
    if isinstance(value, str):
        lowered = value.strip().lower()
        if lowered in _TRUE_STRINGS:
            return True
        if lowered in _FALSE_STRINGS:
            return False
    raise ValueError(f"{name} must be a boolean")


def _normalize_import_settings(value: Any) -> dict[str, Any]:
    if value is None:
        return {}
    if isinstance(value, str):
        value = try_parse_json(value, "import_settings")
    if not isinstance(value, dict):
        raise ValueError("import_settings must be a JSON object")

    result: dict[str, Any] = {}

    if "texture_type" in value:
        result["textureType"] = _map_enum(
            value["texture_type"], _TEXTURE_TYPES)
    if "texture_shape" in value:
        result["textureShape"] = _map_enum(
            value["texture_shape"], _TEXTURE_SHAPES)

    for snake, camel in [
        ("srgb", "sRGBTexture"),
        ("alpha_is_transparency", "alphaIsTransparency"),
        ("readable", "isReadable"),
        ("generate_mipmaps", "mipmapEnabled"),
        ("compression_crunched", "crunchedCompression"),
    ]:
        if snake in value:
            result[camel] = _coerce_bool(value[snake], snake)

    if "alpha_source" in value:
        result["alphaSource"] = _map_enum(
            value["alpha_source"], _ALPHA_SOURCES)

    for snake, camel in [("wrap_mode", "wrapMode"), ("wrap_mode_u", "wrapModeU"), ("wrap_mode_v", "wrapModeV")]:
        if snake in value:
            result[camel] = _map_enum(value[snake], _WRAP_MODES)

    if "filter_mode" in value:
        result["filterMode"] = _map_enum(value["filter_mode"], _FILTER_MODES)
    if "mipmap_filter" in value:
        result["mipmapFilter"] = _map_enum(
            value["mipmap_filter"], _MIPMAP_FILTERS)
    if "compression" in value:
        result["textureCompression"] = _map_enum(
            value["compression"], _COMPRESSIONS)

    if "aniso_level" in value:
        result["anisoLevel"] = int(value["aniso_level"])
    if "max_texture_size" in value:
        result["maxTextureSize"] = int(value["max_texture_size"])
    if "compression_quality" in value:
        result["compressionQuality"] = int(value["compression_quality"])

    if "sprite_mode" in value:
        result["spriteImportMode"] = _map_enum(
            value["sprite_mode"], _SPRITE_MODES)
    if "sprite_pixels_per_unit" in value:
        result["spritePixelsPerUnit"] = float(value["sprite_pixels_per_unit"])
    if "sprite_pivot" in value:
        result["spritePivot"] = value["sprite_pivot"]
    if "sprite_mesh_type" in value:
        result["spriteMeshType"] = _map_enum(
            value["sprite_mesh_type"], _SPRITE_MESH_TYPES)
    if "sprite_extrude" in value:
        result["spriteExtrude"] = int(value["sprite_extrude"])

    for key, val in value.items():
        if key in result:
            continue
        if key in (
            "textureType", "textureShape", "sRGBTexture", "alphaSource",
            "alphaIsTransparency", "isReadable", "mipmapEnabled", "wrapMode",
            "wrapModeU", "wrapModeV", "filterMode", "mipmapFilter", "anisoLevel",
            "maxTextureSize", "textureCompression", "crunchedCompression",
            "compressionQuality", "spriteImportMode", "spritePixelsPerUnit",
            "spritePivot", "spriteMeshType", "spriteExtrude",
        ):
            result[key] = val

    return result


@click.group()
def texture():
    """Texture operations - create, modify, generate sprites."""
    pass


@texture.command("create")
@click.argument("path")
@click.option("--width", default=64, help="Texture width (default: 64)")
@click.option("--height", default=64, help="Texture height (default: 64)")
@click.option("--image-path", help="Source image path (PNG/JPG) to import.")
@click.option("--color", help="Fill color (e.g., '#FF0000' or '[1,0,0,1]')")
@click.option("--pattern", type=click.Choice([
    "checkerboard", "stripes", "stripes_h", "stripes_v", "stripes_diag",
    "dots", "grid", "brick"
]), help="Pattern type")
@click.option("--palette", help="Color palette for pattern (JSON array of colors)")
@click.option("--import-settings", help="TextureImporter settings (JSON)")
@handle_unity_errors
def create(path: str, width: int, height: int, image_path: Optional[str], color: Optional[str],
           pattern: Optional[str], palette: Optional[str], import_settings: Optional[str]):
    """Create a new procedural texture.

    \b
    Examples:
        unity-mcp texture create Assets/Red.png --color '[255,0,0]'
        unity-mcp texture create Assets/Check.png --pattern checkerboard
        unity-mcp texture create Assets/UI.png --import-settings '{"texture_type": "sprite"}'
    """
    config = get_config()
    if image_path:
        if color or pattern or palette:
            print_error(
                "image-path cannot be combined with color, pattern, or palette.")
            sys.exit(1)
    else:
        try:
            warnings = _validate_texture_dimensions(width, height)
        except ValueError as e:
            print_error(str(e))
            sys.exit(1)
        for warning in warnings:
            click.echo(f"⚠️ Warning: {warning}")

    params: dict[str, Any] = {
        "action": "create",
        "path": path,
        "width": width,
        "height": height,
    }

    if color:
        try:
            params["fillColor"] = _normalize_color(color, "color")
        except ValueError as e:
            print_error(str(e))
            sys.exit(1)
    elif not pattern and not image_path:
        # Default to white if no color or pattern specified
        params["fillColor"] = [255, 255, 255, 255]

    if pattern:
        params["pattern"] = pattern

    if palette:
        try:
            params["palette"] = _normalize_palette(palette, "palette")
        except ValueError as e:
            print_error(str(e))
            sys.exit(1)

    if import_settings:
        try:
            params["importSettings"] = _normalize_import_settings(
                import_settings)
        except ValueError as e:
            print_error(str(e))
            sys.exit(1)

    if image_path:
        params["imagePath"] = image_path

    result = run_command("manage_texture", params, config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success(f"Created texture: {path}")


@texture.command("sprite")
@click.argument("path")
@click.option("--width", default=64, help="Texture width (default: 64)")
@click.option("--height", default=64, help="Texture height (default: 64)")
@click.option("--image-path", help="Source image path (PNG/JPG) to import.")
@click.option("--color", help="Fill color (e.g., '#FF0000' or '[1,0,0,1]')")
@click.option("--pattern", type=click.Choice([
    "checkerboard", "stripes", "dots", "grid"
]), help="Pattern type (defaults to checkerboard if no color specified)")
@click.option("--ppu", default=100.0, help="Pixels Per Unit")
@click.option("--pivot", help="Pivot as [x,y] (default: [0.5, 0.5])")
@handle_unity_errors
def sprite(path: str, width: int, height: int, image_path: Optional[str], color: Optional[str], pattern: Optional[str], ppu: float, pivot: Optional[str]):
    """Quickly create a sprite texture.

    \b
    Examples:
        unity-mcp texture sprite Assets/Sprites/Player.png
        unity-mcp texture sprite Assets/Sprites/Coin.png --pattern dots
        unity-mcp texture sprite Assets/Sprites/Solid.png --color '[0,255,0]'
    """
    config = get_config()
    if image_path:
        if color or pattern:
            print_error("image-path cannot be combined with color or pattern.")
            sys.exit(1)
    else:
        try:
            warnings = _validate_texture_dimensions(width, height)
        except ValueError as e:
            print_error(str(e))
            sys.exit(1)
        for warning in warnings:
            click.echo(f"⚠️ Warning: {warning}")

    sprite_settings: dict[str, Any] = {"pixelsPerUnit": ppu}
    if pivot:
        sprite_settings["pivot"] = try_parse_json(pivot, "pivot")
    else:
        sprite_settings["pivot"] = [0.5, 0.5]

    params: dict[str, Any] = {
        "action": "create_sprite",
        "path": path,
        "width": width,
        "height": height,
        "spriteSettings": sprite_settings
    }

    if color:
        try:
            params["fillColor"] = _normalize_color(color, "color")
        except ValueError as e:
            print_error(str(e))
            sys.exit(1)

    # Only default pattern if no color is specified
    if pattern:
        params["pattern"] = pattern
    elif not color and not image_path:
        params["pattern"] = "checkerboard"

    if image_path:
        params["imagePath"] = image_path

    result = run_command("manage_texture", params, config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success(f"Created sprite: {path}")


@texture.command("modify")
@click.argument("path")
@click.option("--set-pixels", required=True, help="Modification args as JSON")
@handle_unity_errors
def modify(path: str, set_pixels: str):
    """Modify an existing texture.

    \b
    Examples:
        unity-mcp texture modify Assets/Tex.png --set-pixels '{"x":0,"y":0,"width":10,"height":10,"color":[255,0,0]}'
        unity-mcp texture modify Assets/Tex.png --set-pixels '{"x":0,"y":0,"width":2,"height":2,"pixels":[[255,0,0,255],[0,255,0,255],[0,0,255,255],[255,255,0,255]]}'
    """
    config = get_config()

    params: dict[str, Any] = {
        "action": "modify",
        "path": path,
    }

    try:
        params["setPixels"] = _normalize_set_pixels(set_pixels)
    except ValueError as e:
        print_error(str(e))
        sys.exit(1)

    result = run_command("manage_texture", params, config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success(f"Modified texture: {path}")


@texture.command("delete")
@click.argument("path")
@click.option(
    "--force", "-f",
    is_flag=True,
    help="Skip confirmation prompt."
)
@handle_unity_errors
def delete(path: str, force: bool):
    """Delete a texture.

    \\b
    Examples:
        unity-mcp texture delete "Assets/Textures/Old.png"
        unity-mcp texture delete "Assets/Textures/Old.png" --force
    """
    from cli.utils.confirmation import confirm_destructive_action
    config = get_config()

    confirm_destructive_action("Delete", "texture", path, force)

    result = run_command("manage_texture", {
                         "action": "delete", "path": path}, config)
    click.echo(format_output(result, config.format))
    if result.get("success"):
        print_success(f"Deleted texture: {path}")
