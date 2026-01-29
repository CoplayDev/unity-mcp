"""
Defines the manage_texture tool for procedural texture generation in Unity.
"""
import base64
import json
from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from services.tools.utils import parse_json_payload, coerce_bool, coerce_int, normalize_color
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry
from services.tools.preflight import preflight


def _normalize_dimension(value: Any, name: str, default: int = 64) -> tuple[int | None, str | None]:
    if value is None:
        return default, None
    coerced = coerce_int(value)
    if coerced is None:
        return None, f"{name} must be an integer"
    if coerced <= 0:
        return None, f"{name} must be positive"
    return coerced, None


def _normalize_positive_int(value: Any, name: str) -> tuple[int | None, str | None]:
    if value is None:
        return None, None
    coerced = coerce_int(value)
    if coerced is None or coerced <= 0:
        return None, f"{name} must be a positive integer"
    return coerced, None


def _normalize_color_int(value: Any) -> tuple[list[int] | None, str | None]:
    """Thin wrapper for normalize_color with int output for texture operations."""
    return normalize_color(value, output_range="int")


def _normalize_palette(value: Any) -> tuple[list[list[int]] | None, str | None]:
    """
    Normalize color palette to list of [r, g, b, a] colors (0-255).
    Returns (parsed_palette, error_message).
    """
    if value is None:
        return None, None

    # Try parsing as string first
    if isinstance(value, str):
        if value in ("[object Object]", "undefined", "null", ""):
            return None, f"palette received invalid value: '{value}'"
        value = parse_json_payload(value)

    if not isinstance(value, list):
        return None, f"palette must be a list of colors, got {type(value).__name__}"

    normalized = []
    for i, color in enumerate(value):
        parsed, error = _normalize_color_int(color)
        if error:
            return None, f"palette[{i}]: {error}"
        normalized.append(parsed)

    return normalized, None


def _normalize_pixels(value: Any, width: int, height: int) -> tuple[list[list[int]] | str | None, str | None]:
    """
    Normalize pixel data to list of [r, g, b, a] colors or base64 string.
    Returns (pixels, error_message).
    """
    if value is None:
        return None, None

    # Base64 string
    if isinstance(value, str):
        if value.startswith("base64:"):
            return value, None  # Pass through for Unity to decode
        # Try parsing as JSON array
        parsed = parse_json_payload(value)
        if isinstance(parsed, list):
            value = parsed
        else:
            # Assume it's raw base64
            return f"base64:{value}", None

    if isinstance(value, list):
        expected_count = width * height
        if len(value) != expected_count:
            return None, f"pixels array must have {expected_count} entries for {width}x{height} texture, got {len(value)}"

        normalized = []
        for i, pixel in enumerate(value):
            parsed, error = _normalize_color_int(pixel)
            if error:
                return None, f"pixels[{i}]: {error}"
            normalized.append(parsed)
        return normalized, None

    return None, f"pixels must be a list or base64 string, got {type(value).__name__}"


def _normalize_sprite_settings(value: Any) -> tuple[dict | None, str | None]:
    """
    Normalize sprite settings.
    Returns (settings, error_message).
    """
    if value is None:
        return None, None

    if isinstance(value, str):
        value = parse_json_payload(value)

    if isinstance(value, dict):
        result = {}
        if "pivot" in value:
            pivot = value["pivot"]
            if isinstance(pivot, (list, tuple)) and len(pivot) == 2:
                result["pivot"] = [float(pivot[0]), float(pivot[1])]
            else:
                return None, f"sprite pivot must be [x, y], got {pivot}"
        if "pixels_per_unit" in value:
            result["pixelsPerUnit"] = float(value["pixels_per_unit"])
        elif "pixelsPerUnit" in value:
            result["pixelsPerUnit"] = float(value["pixelsPerUnit"])
        return result, None

    if isinstance(value, bool) and value:
        # Just enable sprite mode with defaults
        return {"pivot": [0.5, 0.5], "pixelsPerUnit": 100}, None

    return None, f"as_sprite must be a dict or boolean, got {type(value).__name__}"


# Valid values for import settings enums
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

_FILTER_MODES = {"point": "Point", "bilinear": "Bilinear", "trilinear": "Trilinear"}

_COMPRESSIONS = {
    "none": "Uncompressed",
    "low_quality": "CompressedLQ",
    "normal_quality": "Compressed",
    "high_quality": "CompressedHQ",
}

_SPRITE_MODES = {"single": "Single", "multiple": "Multiple", "polygon": "Polygon"}

_SPRITE_MESH_TYPES = {"full_rect": "FullRect", "tight": "Tight"}

_MIPMAP_FILTERS = {"box": "BoxFilter", "kaiser": "KaiserFilter"}


def _normalize_bool_setting(value: Any, name: str) -> tuple[bool | None, str | None]:
    """
    Normalize boolean settings.
    Returns (bool_value, error_message).
    """
    if value is None:
        return None, None

    if isinstance(value, bool):
        return value, None

    if isinstance(value, (int, float)):
        if value in (0, 1, 0.0, 1.0):
            return bool(value), None
        return None, f"{name} must be a boolean"

    if isinstance(value, str):
        coerced = coerce_bool(value, default=None)
        if coerced is None:
            return None, f"{name} must be a boolean"
        return coerced, None

    return None, f"{name} must be a boolean"


def _normalize_import_settings(value: Any) -> tuple[dict | None, str | None]:
    """
    Normalize TextureImporter settings.
    Converts snake_case keys to camelCase and validates enum values.
    Returns (settings, error_message).
    """
    if value is None:
        return None, None

    if isinstance(value, str):
        value = parse_json_payload(value)

    if not isinstance(value, dict):
        return None, f"import_settings must be a dict, got {type(value).__name__}"

    result = {}

    # Texture type
    if "texture_type" in value:
        tt = value["texture_type"].lower() if isinstance(value["texture_type"], str) else value["texture_type"]
        if tt not in _TEXTURE_TYPES:
            return None, f"Invalid texture_type '{tt}'. Valid: {list(_TEXTURE_TYPES.keys())}"
        result["textureType"] = _TEXTURE_TYPES[tt]

    # Texture shape
    if "texture_shape" in value:
        ts = value["texture_shape"].lower() if isinstance(value["texture_shape"], str) else value["texture_shape"]
        if ts not in _TEXTURE_SHAPES:
            return None, f"Invalid texture_shape '{ts}'. Valid: {list(_TEXTURE_SHAPES.keys())}"
        result["textureShape"] = _TEXTURE_SHAPES[ts]

    # Boolean settings
    for snake, camel in [
        ("srgb", "sRGBTexture"),
        ("alpha_is_transparency", "alphaIsTransparency"),
        ("readable", "isReadable"),
        ("generate_mipmaps", "mipmapEnabled"),
        ("compression_crunched", "crunchedCompression"),
    ]:
        if snake in value:
            bool_value, bool_error = _normalize_bool_setting(value[snake], snake)
            if bool_error:
                return None, bool_error
            if bool_value is not None:
                result[camel] = bool_value

    # Alpha source
    if "alpha_source" in value:
        alpha = value["alpha_source"].lower() if isinstance(value["alpha_source"], str) else value["alpha_source"]
        if alpha not in _ALPHA_SOURCES:
            return None, f"Invalid alpha_source '{alpha}'. Valid: {list(_ALPHA_SOURCES.keys())}"
        result["alphaSource"] = _ALPHA_SOURCES[alpha]

    # Wrap modes
    for snake, camel in [("wrap_mode", "wrapMode"), ("wrap_mode_u", "wrapModeU"), ("wrap_mode_v", "wrapModeV")]:
        if snake in value:
            wm = value[snake].lower() if isinstance(value[snake], str) else value[snake]
            if wm not in _WRAP_MODES:
                return None, f"Invalid {snake} '{wm}'. Valid: {list(_WRAP_MODES.keys())}"
            result[camel] = _WRAP_MODES[wm]

    # Filter mode
    if "filter_mode" in value:
        fm = value["filter_mode"].lower() if isinstance(value["filter_mode"], str) else value["filter_mode"]
        if fm not in _FILTER_MODES:
            return None, f"Invalid filter_mode '{fm}'. Valid: {list(_FILTER_MODES.keys())}"
        result["filterMode"] = _FILTER_MODES[fm]

    # Mipmap filter
    if "mipmap_filter" in value:
        mf = value["mipmap_filter"].lower() if isinstance(value["mipmap_filter"], str) else value["mipmap_filter"]
        if mf not in _MIPMAP_FILTERS:
            return None, f"Invalid mipmap_filter '{mf}'. Valid: {list(_MIPMAP_FILTERS.keys())}"
        result["mipmapFilter"] = _MIPMAP_FILTERS[mf]

    # Compression
    if "compression" in value:
        comp = value["compression"].lower() if isinstance(value["compression"], str) else value["compression"]
        if comp not in _COMPRESSIONS:
            return None, f"Invalid compression '{comp}'. Valid: {list(_COMPRESSIONS.keys())}"
        result["textureCompression"] = _COMPRESSIONS[comp]

    # Integer settings
    if "aniso_level" in value:
        raw = value["aniso_level"]
        level = coerce_int(raw)
        if level is None:
            if raw is not None:
                return None, f"aniso_level must be an integer, got {raw}"
        else:
            if not 0 <= level <= 16:
                return None, f"aniso_level must be 0-16, got {level}"
            result["anisoLevel"] = level

    if "max_texture_size" in value:
        raw = value["max_texture_size"]
        size = coerce_int(raw)
        if size is None:
            if raw is not None:
                return None, f"max_texture_size must be an integer, got {raw}"
        else:
            valid_sizes = [32, 64, 128, 256, 512, 1024, 2048, 4096, 8192, 16384]
            if size not in valid_sizes:
                return None, f"max_texture_size must be one of {valid_sizes}, got {size}"
            result["maxTextureSize"] = size

    if "compression_quality" in value:
        raw = value["compression_quality"]
        quality = coerce_int(raw)
        if quality is None:
            if raw is not None:
                return None, f"compression_quality must be an integer, got {raw}"
        else:
            if not 0 <= quality <= 100:
                return None, f"compression_quality must be 0-100, got {quality}"
            result["compressionQuality"] = quality

    # Sprite-specific settings
    if "sprite_mode" in value:
        sm = value["sprite_mode"].lower() if isinstance(value["sprite_mode"], str) else value["sprite_mode"]
        if sm not in _SPRITE_MODES:
            return None, f"Invalid sprite_mode '{sm}'. Valid: {list(_SPRITE_MODES.keys())}"
        result["spriteImportMode"] = _SPRITE_MODES[sm]

    if "sprite_pixels_per_unit" in value:
        raw = value["sprite_pixels_per_unit"]
        try:
            result["spritePixelsPerUnit"] = float(raw)
        except (TypeError, ValueError):
            return None, f"sprite_pixels_per_unit must be a number, got {raw}"

    if "sprite_pivot" in value:
        pivot = value["sprite_pivot"]
        if isinstance(pivot, (list, tuple)) and len(pivot) == 2:
            result["spritePivot"] = [float(pivot[0]), float(pivot[1])]
        else:
            return None, f"sprite_pivot must be [x, y], got {pivot}"

    if "sprite_mesh_type" in value:
        mt = value["sprite_mesh_type"].lower() if isinstance(value["sprite_mesh_type"], str) else value["sprite_mesh_type"]
        if mt not in _SPRITE_MESH_TYPES:
            return None, f"Invalid sprite_mesh_type '{mt}'. Valid: {list(_SPRITE_MESH_TYPES.keys())}"
        result["spriteMeshType"] = _SPRITE_MESH_TYPES[mt]

    if "sprite_extrude" in value:
        raw = value["sprite_extrude"]
        extrude = coerce_int(raw)
        if extrude is None:
            if raw is not None:
                return None, f"sprite_extrude must be an integer, got {raw}"
        else:
            if not 0 <= extrude <= 32:
                return None, f"sprite_extrude must be 0-32, got {extrude}"
            result["spriteExtrude"] = extrude

    return result, None


@mcp_for_unity_tool(
    description=(
        "Procedural texture generation for Unity. Creates textures with solid fills, "
        "patterns (checkerboard, stripes, dots, grid, brick), gradients, and noise. "
        "Actions: create, modify, delete, create_sprite, apply_pattern, apply_gradient, apply_noise"
    ),
    annotations=ToolAnnotations(
        title="Manage Texture",
        destructiveHint=True,
    ),
)
async def manage_texture(
    ctx: Context,
    action: Annotated[Literal[
        "create",
        "modify",
        "delete",
        "create_sprite",
        "apply_pattern",
        "apply_gradient",
        "apply_noise"
    ], "Action to perform."],

    # Required for most actions
    path: Annotated[str,
                    "Output texture path (e.g., 'Assets/Textures/MyTexture.png')"] | None = None,

    # Dimensions (defaults to 64x64)
    width: Annotated[int, "Texture width in pixels (default: 64)"] | None = None,
    height: Annotated[int, "Texture height in pixels (default: 64)"] | None = None,

    # Solid fill (accepts both 0-255 integers and 0.0-1.0 normalized floats)
    fill_color: Annotated[list[int | float] | dict[str, int | float] | str,
                          "Fill color as [r, g, b] or [r, g, b, a] array, {r, g, b, a} object, or hex string. Accepts both 0-255 range (e.g., [255, 0, 0]) or 0.0-1.0 normalized range (e.g., [1.0, 0, 0])"] | None = None,

    # Pattern-based generation
    pattern: Annotated[Literal[
        "checkerboard", "stripes", "stripes_h", "stripes_v", "stripes_diag",
        "dots", "grid", "brick"
    ], "Pattern type for apply_pattern action"] | None = None,

    palette: Annotated[list[list[int | float]],
                       "Color palette as [[r,g,b,a], ...]. Accepts both 0-255 range or 0.0-1.0 normalized range"] | None = None,

    pattern_size: Annotated[int,
                            "Pattern cell size in pixels (default: 8)"] | None = None,

    # Direct pixel data
    pixels: Annotated[list[list[int]] | str,
                      "Pixel data as JSON array of [r,g,b,a] values or base64 string"] | None = None,

    image_path: Annotated[str,
                          "Source image file path for create/create_sprite (PNG/JPG)."] | None = None,

    # Gradient settings
    gradient_type: Annotated[Literal["linear", "radial"],
                             "Gradient type (default: linear)"] | None = None,
    gradient_angle: Annotated[float,
                              "Gradient angle in degrees for linear gradient (default: 0)"] | None = None,

    # Noise settings
    noise_scale: Annotated[float,
                           "Noise scale/frequency (default: 0.1)"] | None = None,
    octaves: Annotated[int,
                       "Number of noise octaves for detail (default: 1)"] | None = None,

    # Modify action
    set_pixels: Annotated[dict,
                          "Region to modify: {x, y, width, height, color or pixels}"] | None = None,

    # Sprite creation (legacy, prefer import_settings)
    as_sprite: Annotated[dict | bool,
                         "Configure as sprite: {pivot: [x,y], pixels_per_unit: 100} or true for defaults"] | None = None,

    # TextureImporter settings
    import_settings: Annotated[dict,
        "TextureImporter settings dict. Keys: texture_type (default/normal_map/sprite/etc), "
        "texture_shape (2d/cube), srgb (bool), alpha_source (none/from_input/from_gray_scale), "
        "alpha_is_transparency (bool), readable (bool), generate_mipmaps (bool), "
        "wrap_mode/wrap_mode_u/wrap_mode_v (repeat/clamp/mirror/mirror_once), "
        "filter_mode (point/bilinear/trilinear), aniso_level (0-16), max_texture_size (32-16384), "
        "compression (none/low_quality/normal_quality/high_quality), compression_quality (0-100), "
        "sprite_mode (single/multiple/polygon), sprite_pixels_per_unit, sprite_pivot, "
        "sprite_mesh_type (full_rect/tight), sprite_extrude (0-32)"] | None = None,

) -> dict[str, Any]:
    unity_instance = get_unity_instance_from_context(ctx)

    # Preflight check
    gate = await preflight(ctx, wait_for_no_compile=True, refresh_if_dirty=True)
    if gate is not None:
        return gate.model_dump()

    # --- Normalize parameters ---
    fill_color, fill_error = _normalize_color_int(fill_color)
    if fill_error:
        return {"success": False, "message": fill_error}

    action_lower = action.lower()

    if image_path is not None and action_lower not in ("create", "create_sprite"):
        return {"success": False, "message": "image_path is only supported for create/create_sprite."}

    if image_path is not None and (fill_color is not None or pattern is not None or pixels is not None):
        return {"success": False, "message": "image_path cannot be combined with fill_color, pattern, or pixels."}

    # Default to white for create action if nothing else specified
    if action == "create" and fill_color is None and pattern is None and pixels is None and image_path is None:
        fill_color = [255, 255, 255, 255]

    palette, palette_error = _normalize_palette(palette)
    if palette_error:
        return {"success": False, "message": palette_error}

    if image_path is None:
        # Normalize dimensions
        width, width_error = _normalize_dimension(width, "width")
        if width_error:
            return {"success": False, "message": width_error}
        height, height_error = _normalize_dimension(height, "height")
        if height_error:
            return {"success": False, "message": height_error}
        pattern_size, pattern_error = _normalize_positive_int(pattern_size, "pattern_size")
        if pattern_error:
            return {"success": False, "message": pattern_error}

        octaves, octaves_error = _normalize_positive_int(octaves, "octaves")
        if octaves_error:
            return {"success": False, "message": octaves_error}
    else:
        width = None
        height = None

    # Normalize pixels if provided
    pixels_normalized = None
    if pixels is not None:
        pixels_normalized, pixels_error = _normalize_pixels(pixels, width, height)
        if pixels_error:
            return {"success": False, "message": pixels_error}

    # Normalize sprite settings
    sprite_settings, sprite_error = _normalize_sprite_settings(as_sprite)
    if sprite_error:
        return {"success": False, "message": sprite_error}

    # Normalize import settings
    import_settings_normalized, import_error = _normalize_import_settings(import_settings)
    if import_error:
        return {"success": False, "message": import_error}

    # Normalize set_pixels for modify action
    set_pixels_normalized = None
    if set_pixels is not None:
        if isinstance(set_pixels, str):
            parsed = parse_json_payload(set_pixels)
            if not isinstance(parsed, dict):
                return {"success": False, "message": "set_pixels must be a JSON object"}
            set_pixels = parsed
        if not isinstance(set_pixels, dict):
            return {"success": False, "message": "set_pixels must be a JSON object"}

        set_pixels_normalized = set_pixels.copy()
        if "color" in set_pixels_normalized:
            color, error = _normalize_color_int(set_pixels_normalized["color"])
            if error:
                return {"success": False, "message": f"set_pixels.color: {error}"}
            set_pixels_normalized["color"] = color
        if "pixels" in set_pixels_normalized:
            region_width = coerce_int(set_pixels_normalized.get("width"))
            region_height = coerce_int(set_pixels_normalized.get("height"))
            if region_width is None or region_height is None or region_width <= 0 or region_height <= 0:
                return {"success": False, "message": "set_pixels width and height must be positive integers"}
            pixels_normalized, pixels_error = _normalize_pixels(
                set_pixels_normalized["pixels"], region_width, region_height
            )
            if pixels_error:
                return {"success": False, "message": f"set_pixels.pixels: {pixels_error}"}
            set_pixels_normalized["pixels"] = pixels_normalized

    # --- Build params for Unity ---
    params_dict = {
        "action": action.lower(),
        "path": path,
        "width": width,
        "height": height,
        "fillColor": fill_color,
        "pattern": pattern,
        "palette": palette,
        "patternSize": pattern_size,
        "pixels": pixels_normalized,
        "imagePath": image_path,
        "gradientType": gradient_type,
        "gradientAngle": gradient_angle,
        "noiseScale": noise_scale,
        "octaves": octaves,
        "setPixels": set_pixels_normalized,
        "spriteSettings": sprite_settings,
        "importSettings": import_settings_normalized,
    }

    # Remove None values
    params_dict = {k: v for k, v in params_dict.items() if v is not None}

    # Send to Unity
    result = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "manage_texture",
        params_dict,
    )

    if isinstance(result, dict):
        result["_debug_params"] = params_dict

    return result if isinstance(result, dict) else {"success": False, "message": str(result)}
