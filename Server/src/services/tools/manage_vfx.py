from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry

# All possible actions grouped by component type
PARTICLE_ACTIONS = [
    "particle_get_info", "particle_set_main", "particle_set_emission", "particle_set_shape",
    "particle_set_color_over_lifetime", "particle_set_size_over_lifetime",
    "particle_set_velocity_over_lifetime", "particle_set_noise", "particle_set_renderer",
    "particle_enable_module", "particle_play", "particle_stop", "particle_pause",
    "particle_restart", "particle_clear", "particle_add_burst", "particle_clear_bursts"
]

VFX_ACTIONS = [
    # Asset management
    "vfx_create_asset", "vfx_assign_asset", "vfx_list_templates", "vfx_list_assets",
    # Runtime control
    "vfx_get_info", "vfx_set_float", "vfx_set_int", "vfx_set_bool",
    "vfx_set_vector2", "vfx_set_vector3", "vfx_set_vector4", "vfx_set_color",
    "vfx_set_gradient", "vfx_set_texture", "vfx_set_mesh", "vfx_set_curve",
    "vfx_send_event", "vfx_play", "vfx_stop", "vfx_pause", "vfx_reinit",
    "vfx_set_playback_speed", "vfx_set_seed"
]

LINE_ACTIONS = [
    "line_get_info", "line_set_positions", "line_add_position", "line_set_position",
    "line_set_width", "line_set_color", "line_set_material", "line_set_properties",
    "line_clear", "line_create_line", "line_create_circle", "line_create_arc", "line_create_bezier"
]

TRAIL_ACTIONS = [
    "trail_get_info", "trail_set_time", "trail_set_width", "trail_set_color",
    "trail_set_material", "trail_set_properties", "trail_clear", "trail_emit"
]

ALL_ACTIONS = ["ping"] + PARTICLE_ACTIONS + \
    VFX_ACTIONS + LINE_ACTIONS + TRAIL_ACTIONS


@mcp_for_unity_tool(
    description="""Unified VFX management for Unity visual effects components.

Each action prefix requires a specific component on the target GameObject:
- `particle_*` actions require **ParticleSystem** component
- `vfx_*` actions require **VisualEffect** component (+ com.unity.visualeffectgraph package)
- `line_*` actions require **LineRenderer** component
- `trail_*` actions require **TrailRenderer** component

**If the component doesn't exist, the action will FAIL
Before using this tool, either:
1. Use `manage_gameobject` with `action="get_components"` to check if component exists
2. Use `manage_gameobject` with `action="add_component", component_name="ParticleSystem"` (or LineRenderer/TrailRenderer/VisualEffect) to add the component first
3. Assign material to the component beforehand to avoid empty effects

**TARGETING:**
Use `target` parameter to specify the GameObject:
- By name: `target="Fire"` (finds first GameObject named "Fire")
- By path: `target="Effects/Fire"` with `search_method="by_path"`
- By instance ID: `target="12345"` with `search_method="by_id"` (most reliable)
- By tag: `target="Player"` with `search_method="by_tag"`

**Component Types & Action Prefixes:**
- `particle_*` - ParticleSystem (legacy particle effects)
- `vfx_*` - Visual Effect Graph (modern GPU particles, requires com.unity.visualeffectgraph)
- `line_*` - LineRenderer (lines, curves, shapes)
- `trail_*` - TrailRenderer (motion trails)

**ParticleSystem Actions (particle_*):**
- particle_get_info: Get particle system info
- particle_set_main: Set main module (duration, looping, startLifetime, startSpeed, startSize, startColor, gravityModifier, maxParticles)
- particle_set_emission: Set emission (rateOverTime, rateOverDistance)
- particle_set_shape: Set shape (shapeType, radius, angle, arc, position, rotation, scale)
- particle_set_color_over_lifetime, particle_set_size_over_lifetime, particle_set_velocity_over_lifetime
- particle_set_noise: Set noise (strength, frequency, scrollSpeed)
- particle_set_renderer: Set renderer (renderMode, material)
- particle_enable_module: Enable/disable modules
- particle_play/stop/pause/restart/clear: Playback control
- particle_add_burst, particle_clear_bursts: Burst management

**VFX Graph Actions (vfx_*):**
- **Asset Management:**
  - vfx_create_asset: Create a new VFX Graph asset file (requires: assetName, optional: folderPath, template, overwrite)
  - vfx_assign_asset: Assign a VFX asset to a VisualEffect component (requires: target, assetPath)
  - vfx_list_templates: List available VFX templates in project and packages
  - vfx_list_assets: List all VFX assets in project (optional: folder, search)
- **Runtime Control:**
  - vfx_get_info: Get VFX info
  - vfx_set_float/int/bool: Set exposed parameters
  - vfx_set_vector2/vector3/vector4: Set vector parameters
  - vfx_set_color, vfx_set_gradient: Set color/gradient parameters
  - vfx_set_texture, vfx_set_mesh: Set asset parameters
  - vfx_set_curve: Set animation curve
  - vfx_send_event: Send events with attributes (position, velocity, color, size, lifetime)
  - vfx_play/stop/pause/reinit: Playback control
  - vfx_set_playback_speed, vfx_set_seed

**LineRenderer Actions (line_*):**
- line_get_info: Get line info
- line_set_positions: Set all positions
- line_add_position, line_set_position: Modify positions
- line_set_width: Set width (uniform, start/end, curve)
- line_set_color: Set color (uniform, gradient)
- line_set_material, line_set_properties
- line_clear: Clear positions
- line_create_line: Create simple line
- line_create_circle: Create circle
- line_create_arc: Create arc
- line_create_bezier: Create Bezier curve

**TrailRenderer Actions (trail_*):**
- trail_get_info: Get trail info
- trail_set_time: Set trail duration
- trail_set_width, trail_set_color, trail_set_material, trail_set_properties
- trail_clear: Clear trail
- trail_emit: Emit point (Unity 2021.1+)""",
    annotations=ToolAnnotations(
        title="Manage VFX",
        destructiveHint=True,
    ),
)
async def manage_vfx(
    ctx: Context,
    action: Annotated[str, "Action to perform. Use prefix: particle_, vfx_, line_, or trail_"],

    # Target specification (common) - REQUIRED for most actions
    # Using str | None to accept any string format
    target: Annotated[str | None,
                      "Target GameObject with the VFX component. Use name (e.g. 'Fire'), path ('Effects/Fire'), instance ID, or tag. The GameObject MUST have the required component (ParticleSystem/VisualEffect/LineRenderer/TrailRenderer) for the action prefix."] = None,
    search_method: Annotated[
        Literal["by_id", "by_name", "by_path", "by_tag", "by_layer"] | None,
        "How to find target: by_name (default), by_path (hierarchy path), by_id (instance ID - most reliable), by_tag, by_layer"
    ] = None,

    # === PARTICLE SYSTEM PARAMETERS ===
    # Main module - All use Any to accept string coercion from MCP clients
    duration: Annotated[Any,
                        "[Particle] Duration in seconds (number or string)"] = None,
    looping: Annotated[Any,
                       "[Particle] Whether to loop (bool or string 'true'/'false')"] = None,
    prewarm: Annotated[Any,
                       "[Particle] Prewarm the system (bool or string)"] = None,
    start_delay: Annotated[Any,
                           "[Particle] Start delay (number or MinMaxCurve dict)"] = None,
    start_lifetime: Annotated[Any,
                              "[Particle] Particle lifetime (number or MinMaxCurve dict)"] = None,
    start_speed: Annotated[Any,
                           "[Particle] Initial speed (number or MinMaxCurve dict)"] = None,
    start_size: Annotated[Any,
                          "[Particle] Initial size (number or MinMaxCurve dict)"] = None,
    start_rotation: Annotated[Any,
                              "[Particle] Initial rotation (number or MinMaxCurve dict)"] = None,
    start_color: Annotated[Any,
                           "[Particle/VFX] Start color [r,g,b,a] (array, dict, or JSON string)"] = None,
    gravity_modifier: Annotated[Any,
                                "[Particle] Gravity multiplier (number or MinMaxCurve dict)"] = None,
    simulation_space: Annotated[Literal["Local", "World",
                                        "Custom"] | None, "[Particle] Simulation space"] = None,
    scaling_mode: Annotated[Literal["Hierarchy", "Local",
                                    "Shape"] | None, "[Particle] Scaling mode"] = None,
    play_on_awake: Annotated[Any,
                             "[Particle] Play on awake (bool or string)"] = None,
    max_particles: Annotated[Any,
                             "[Particle] Maximum particles (integer or string)"] = None,

    # Emission
    rate_over_time: Annotated[Any,
                              "[Particle] Emission rate over time (number or MinMaxCurve dict)"] = None,
    rate_over_distance: Annotated[Any,
                                  "[Particle] Emission rate over distance (number or MinMaxCurve dict)"] = None,

    # Shape
    shape_type: Annotated[Literal["Sphere", "Hemisphere", "Cone", "Box",
                                  "Circle", "Edge", "Donut"] | None, "[Particle] Shape type"] = None,
    radius: Annotated[Any,
                      "[Particle/Line] Shape radius (number or string)"] = None,
    radius_thickness: Annotated[Any,
                                "[Particle] Radius thickness 0-1 (number or string)"] = None,
    angle: Annotated[Any, "[Particle] Cone angle (number or string)"] = None,
    arc: Annotated[Any, "[Particle] Arc angle (number or string)"] = None,

    # Noise
    strength: Annotated[Any,
                        "[Particle] Noise strength (number or MinMaxCurve dict)"] = None,
    frequency: Annotated[Any,
                         "[Particle] Noise frequency (number or string)"] = None,
    scroll_speed: Annotated[Any,
                            "[Particle] Noise scroll speed (number or MinMaxCurve dict)"] = None,
    damping: Annotated[Any,
                       "[Particle] Noise damping (bool or string)"] = None,
    octave_count: Annotated[Any,
                            "[Particle] Noise octaves 1-4 (integer or string)"] = None,
    quality: Annotated[Literal["Low", "Medium", "High"]
                       | None, "[Particle] Noise quality"] = None,

    # Module control
    module: Annotated[str | None,
                      "[Particle] Module name to enable/disable"] = None,
    enabled: Annotated[Any,
                       "[Particle] Enable/disable module (bool or string)"] = None,

    # Burst
    time: Annotated[Any,
                    "[Particle/Trail] Burst time or trail duration (number or string)"] = None,
    count: Annotated[Any, "[Particle] Burst count (integer or string)"] = None,
    min_count: Annotated[Any,
                         "[Particle] Min burst count (integer or string)"] = None,
    max_count: Annotated[Any,
                         "[Particle] Max burst count (integer or string)"] = None,
    cycles: Annotated[Any,
                      "[Particle] Burst cycles (integer or string)"] = None,
    interval: Annotated[Any,
                        "[Particle] Burst interval (number or string)"] = None,
    probability: Annotated[Any,
                           "[Particle] Burst probability 0-1 (number or string)"] = None,

    # Playback
    with_children: Annotated[Any,
                             "[Particle] Apply to children (bool or string)"] = None,

    # === VFX GRAPH PARAMETERS ===
    # Asset management
    asset_name: Annotated[str | None,
                          "[VFX] Name for new VFX asset (without .vfx extension)"] = None,
    folder_path: Annotated[str | None,
                           "[VFX] Folder path for new asset (default: Assets/VFX)"] = None,
    template: Annotated[str | None,
                        "[VFX] Template name for new asset (use vfx_list_templates to see available)"] = None,
    asset_path: Annotated[str | None,
                          "[VFX] Path to VFX asset to assign (e.g. Assets/VFX/MyEffect.vfx)"] = None,
    overwrite: Annotated[Any,
                         "[VFX] Overwrite existing asset (bool or string)"] = None,
    folder: Annotated[str | None,
                      "[VFX] Folder to search for assets (for vfx_list_assets)"] = None,
    search: Annotated[str | None,
                      "[VFX] Search pattern for assets (for vfx_list_assets)"] = None,

    # Runtime parameters
    parameter: Annotated[str | None, "[VFX] Exposed parameter name"] = None,
    value: Annotated[Any,
                     "[VFX] Parameter value (number, bool, array, or string)"] = None,
    texture_path: Annotated[str | None, "[VFX] Texture asset path"] = None,
    mesh_path: Annotated[str | None, "[VFX] Mesh asset path"] = None,
    gradient: Annotated[Any,
                        "[VFX/Line/Trail] Gradient {colorKeys, alphaKeys} or {startColor, endColor} (dict or JSON string)"] = None,
    curve: Annotated[Any,
                     "[VFX] Animation curve keys or {startValue, endValue} (array, dict, or JSON string)"] = None,
    event_name: Annotated[str | None, "[VFX] Event name to send"] = None,
    velocity: Annotated[Any,
                        "[VFX] Event velocity [x,y,z] (array or JSON string)"] = None,
    size: Annotated[Any, "[VFX] Event size (number or string)"] = None,
    lifetime: Annotated[Any, "[VFX] Event lifetime (number or string)"] = None,
    play_rate: Annotated[Any,
                         "[VFX] Playback speed multiplier (number or string)"] = None,
    seed: Annotated[Any, "[VFX] Random seed (integer or string)"] = None,
    reset_seed_on_play: Annotated[Any,
                                  "[VFX] Reset seed on play (bool or string)"] = None,

    # === LINE/TRAIL RENDERER PARAMETERS ===
    positions: Annotated[Any,
                         "[Line] Positions [[x,y,z], ...] (array or JSON string)"] = None,
    position: Annotated[Any,
                        "[Line/Trail] Single position [x,y,z] (array or JSON string)"] = None,
    index: Annotated[Any, "[Line] Position index (integer or string)"] = None,

    # Width
    width: Annotated[Any,
                     "[Line/Trail] Uniform width (number or string)"] = None,
    start_width: Annotated[Any,
                           "[Line/Trail] Start width (number or string)"] = None,
    end_width: Annotated[Any,
                         "[Line/Trail] End width (number or string)"] = None,
    width_curve: Annotated[Any,
                           "[Line/Trail] Width curve (number or dict)"] = None,
    width_multiplier: Annotated[Any,
                                "[Line/Trail] Width multiplier (number or string)"] = None,

    # Color
    color: Annotated[Any,
                     "[Line/Trail/VFX] Color [r,g,b,a] (array or JSON string)"] = None,
    start_color_line: Annotated[Any,
                                "[Line/Trail] Start color (array or JSON string)"] = None,
    end_color: Annotated[Any,
                         "[Line/Trail] End color (array or JSON string)"] = None,

    # Material & properties
    material_path: Annotated[str | None,
                             "[Particle/Line/Trail] Material asset path"] = None,
    trail_material_path: Annotated[str | None,
                                   "[Particle] Trail material asset path"] = None,
    loop: Annotated[Any,
                    "[Line] Connect end to start (bool or string)"] = None,
    use_world_space: Annotated[Any,
                               "[Line] Use world space (bool or string)"] = None,
    num_corner_vertices: Annotated[Any,
                                   "[Line/Trail] Corner vertices (integer or string)"] = None,
    num_cap_vertices: Annotated[Any,
                                "[Line/Trail] Cap vertices (integer or string)"] = None,
    alignment: Annotated[Literal["View", "Local", "TransformZ"]
                         | None, "[Line/Trail] Alignment"] = None,
    texture_mode: Annotated[Literal["Stretch", "Tile", "DistributePerSegment",
                                    "RepeatPerSegment"] | None, "[Line/Trail] Texture mode"] = None,
    generate_lighting_data: Annotated[Any,
                                      "[Line/Trail] Generate lighting data for GI (bool or string)"] = None,
    sorting_order: Annotated[Any,
                             "[Line/Trail/Particle] Sorting order (integer or string)"] = None,
    sorting_layer_name: Annotated[str | None,
                                  "[Renderer] Sorting layer name"] = None,
    sorting_layer_id: Annotated[Any,
                                "[Renderer] Sorting layer ID (integer or string)"] = None,
    render_mode: Annotated[str | None,
                           "[Particle] Render mode (Billboard, Stretch, HorizontalBillboard, VerticalBillboard, Mesh, None)"] = None,
    sort_mode: Annotated[str | None,
                         "[Particle] Sort mode (None, Distance, OldestInFront, YoungestInFront, Depth)"] = None,

    # === RENDERER COMMON PROPERTIES (Shadows, Lighting, Probes) ===
    shadow_casting_mode: Annotated[Literal["Off", "On", "TwoSided",
                                           "ShadowsOnly"] | None, "[Renderer] Shadow casting mode"] = None,
    receive_shadows: Annotated[Any,
                               "[Renderer] Receive shadows (bool or string)"] = None,
    shadow_bias: Annotated[Any,
                           "[Renderer] Shadow bias (number or string)"] = None,
    light_probe_usage: Annotated[Literal["Off", "BlendProbes", "UseProxyVolume",
                                         "CustomProvided"] | None, "[Renderer] Light probe usage mode"] = None,
    reflection_probe_usage: Annotated[Literal["Off", "BlendProbes", "BlendProbesAndSkybox",
                                              "Simple"] | None, "[Renderer] Reflection probe usage mode"] = None,
    motion_vector_generation_mode: Annotated[Literal["Camera", "Object",
                                                     "ForceNoMotion"] | None, "[Renderer] Motion vector generation mode"] = None,
    rendering_layer_mask: Annotated[Any,
                                    "[Renderer] Rendering layer mask for SRP (integer or string)"] = None,

    # === PARTICLE RENDERER SPECIFIC ===
    min_particle_size: Annotated[Any,
                                 "[Particle] Min particle size relative to viewport (number or string)"] = None,
    max_particle_size: Annotated[Any,
                                 "[Particle] Max particle size relative to viewport (number or string)"] = None,
    length_scale: Annotated[Any,
                            "[Particle] Length scale for stretched billboard (number or string)"] = None,
    velocity_scale: Annotated[Any,
                              "[Particle] Velocity scale for stretched billboard (number or string)"] = None,
    camera_velocity_scale: Annotated[Any,
                                     "[Particle] Camera velocity scale for stretched billboard (number or string)"] = None,
    normal_direction: Annotated[Any,
                                "[Particle] Normal direction 0-1 (number or string)"] = None,
    pivot: Annotated[Any,
                     "[Particle] Pivot offset [x,y,z] (array or JSON string)"] = None,
    flip: Annotated[Any,
                    "[Particle] Flip [x,y,z] (array or JSON string)"] = None,
    allow_roll: Annotated[Any,
                          "[Particle] Allow roll for mesh particles (bool or string)"] = None,

    # Shape creation (line_create_*)
    start: Annotated[Any,
                     "[Line] Start point [x,y,z] (array or JSON string)"] = None,
    end: Annotated[Any,
                   "[Line] End point [x,y,z] (array or JSON string)"] = None,
    center: Annotated[Any,
                      "[Line] Circle/arc center [x,y,z] (array or JSON string)"] = None,
    segments: Annotated[Any,
                        "[Line] Number of segments (integer or string)"] = None,
    normal: Annotated[Any,
                      "[Line] Normal direction [x,y,z] (array or JSON string)"] = None,
    start_angle: Annotated[Any,
                           "[Line] Arc start angle degrees (number or string)"] = None,
    end_angle: Annotated[Any,
                         "[Line] Arc end angle degrees (number or string)"] = None,
    control_point1: Annotated[Any,
                              "[Line] Bezier control point 1 (array or JSON string)"] = None,
    control_point2: Annotated[Any,
                              "[Line] Bezier control point 2 (cubic) (array or JSON string)"] = None,

    # Trail specific
    min_vertex_distance: Annotated[Any,
                                   "[Trail] Min vertex distance (number or string)"] = None,
    autodestruct: Annotated[Any,
                            "[Trail] Destroy when finished (bool or string)"] = None,
    emitting: Annotated[Any, "[Trail] Is emitting (bool or string)"] = None,

    # Common vector params for shape/velocity
    x: Annotated[Any,
                 "[Particle] Velocity X (number or MinMaxCurve dict)"] = None,
    y: Annotated[Any,
                 "[Particle] Velocity Y (number or MinMaxCurve dict)"] = None,
    z: Annotated[Any,
                 "[Particle] Velocity Z (number or MinMaxCurve dict)"] = None,
    speed_modifier: Annotated[Any,
                              "[Particle] Speed modifier (number or MinMaxCurve dict)"] = None,
    space: Annotated[Literal["Local", "World"] |
                     None, "[Particle] Velocity space"] = None,
    separate_axes: Annotated[Any,
                             "[Particle] Separate XYZ axes (bool or string)"] = None,
    size_over_lifetime: Annotated[Any,
                                  "[Particle] Size over lifetime (number or MinMaxCurve dict)"] = None,
    size_x: Annotated[Any,
                      "[Particle] Size X (number or MinMaxCurve dict)"] = None,
    size_y: Annotated[Any,
                      "[Particle] Size Y (number or MinMaxCurve dict)"] = None,
    size_z: Annotated[Any,
                      "[Particle] Size Z (number or MinMaxCurve dict)"] = None,

) -> dict[str, Any]:
    """Unified VFX management tool."""

    # Normalize action to lowercase to match Unity-side behavior
    action_normalized = action.lower()

    # Validate action against known actions using normalized value
    if action_normalized not in ALL_ACTIONS:
        # Provide helpful error with closest matches by prefix
        prefix = action_normalized.split(
            "_")[0] + "_" if "_" in action_normalized else ""
        available_by_prefix = {
            "particle_": PARTICLE_ACTIONS,
            "vfx_": VFX_ACTIONS,
            "line_": LINE_ACTIONS,
            "trail_": TRAIL_ACTIONS,
        }
        suggestions = available_by_prefix.get(prefix, [])
        if suggestions:
            return {
                "success": False,
                "message": f"Unknown action '{action}'. Available {prefix}* actions: {', '.join(suggestions)}",
            }
        else:
            return {
                "success": False,
                "message": (
                    f"Unknown action '{action}'. Use prefixes: "
                    "particle_*, vfx_*, line_*, trail_*. Run with action='ping' to test connection."
                ),
            }

    unity_instance = get_unity_instance_from_context(ctx)

    # Build parameters dict with normalized action to stay consistent with Unity
    params_dict: dict[str, Any] = {"action": action_normalized}

    # Target
    if target is not None:
        params_dict["target"] = target
    if search_method is not None:
        params_dict["searchMethod"] = search_method

    # === PARTICLE SYSTEM ===
    # Pass through all values - C# side handles parsing (ParseColor, ParseVector3, ParseMinMaxCurve, ToObject<T>)
    if duration is not None:
        params_dict["duration"] = duration
    if looping is not None:
        params_dict["looping"] = looping
    if prewarm is not None:
        params_dict["prewarm"] = prewarm
    if start_delay is not None:
        params_dict["startDelay"] = start_delay
    if start_lifetime is not None:
        params_dict["startLifetime"] = start_lifetime
    if start_speed is not None:
        params_dict["startSpeed"] = start_speed
    if start_size is not None:
        params_dict["startSize"] = start_size
    if start_rotation is not None:
        params_dict["startRotation"] = start_rotation
    if start_color is not None:
        params_dict["startColor"] = start_color
    if gravity_modifier is not None:
        params_dict["gravityModifier"] = gravity_modifier
    if simulation_space is not None:
        params_dict["simulationSpace"] = simulation_space
    if scaling_mode is not None:
        params_dict["scalingMode"] = scaling_mode
    if play_on_awake is not None:
        params_dict["playOnAwake"] = play_on_awake
    if max_particles is not None:
        params_dict["maxParticles"] = max_particles

    # Emission
    if rate_over_time is not None:
        params_dict["rateOverTime"] = rate_over_time
    if rate_over_distance is not None:
        params_dict["rateOverDistance"] = rate_over_distance

    # Shape
    if shape_type is not None:
        params_dict["shapeType"] = shape_type
    if radius is not None:
        params_dict["radius"] = radius
    if radius_thickness is not None:
        params_dict["radiusThickness"] = radius_thickness
    if angle is not None:
        params_dict["angle"] = angle
    if arc is not None:
        params_dict["arc"] = arc

    # Noise
    if strength is not None:
        params_dict["strength"] = strength
    if frequency is not None:
        params_dict["frequency"] = frequency
    if scroll_speed is not None:
        params_dict["scrollSpeed"] = scroll_speed
    if damping is not None:
        params_dict["damping"] = damping
    if octave_count is not None:
        params_dict["octaveCount"] = octave_count
    if quality is not None:
        params_dict["quality"] = quality

    # Module
    if module is not None:
        params_dict["module"] = module
    if enabled is not None:
        params_dict["enabled"] = enabled

    # Burst
    if time is not None:
        params_dict["time"] = time
    if count is not None:
        params_dict["count"] = count
    if min_count is not None:
        params_dict["minCount"] = min_count
    if max_count is not None:
        params_dict["maxCount"] = max_count
    if cycles is not None:
        params_dict["cycles"] = cycles
    if interval is not None:
        params_dict["interval"] = interval
    if probability is not None:
        params_dict["probability"] = probability

    # Playback
    if with_children is not None:
        params_dict["withChildren"] = with_children

    # === VFX GRAPH ===
    # Asset management parameters
    if asset_name is not None:
        params_dict["assetName"] = asset_name
    if folder_path is not None:
        params_dict["folderPath"] = folder_path
    if template is not None:
        params_dict["template"] = template
    if asset_path is not None:
        params_dict["assetPath"] = asset_path
    if overwrite is not None:
        params_dict["overwrite"] = overwrite
    if folder is not None:
        params_dict["folder"] = folder
    if search is not None:
        params_dict["search"] = search

    # Runtime parameters
    if parameter is not None:
        params_dict["parameter"] = parameter
    if value is not None:
        params_dict["value"] = value
    if texture_path is not None:
        params_dict["texturePath"] = texture_path
    if mesh_path is not None:
        params_dict["meshPath"] = mesh_path
    if gradient is not None:
        params_dict["gradient"] = gradient
    if curve is not None:
        params_dict["curve"] = curve
    if event_name is not None:
        params_dict["eventName"] = event_name
    if velocity is not None:
        params_dict["velocity"] = velocity
    if size is not None:
        params_dict["size"] = size
    if lifetime is not None:
        params_dict["lifetime"] = lifetime
    if play_rate is not None:
        params_dict["playRate"] = play_rate
    if seed is not None:
        params_dict["seed"] = seed
    if reset_seed_on_play is not None:
        params_dict["resetSeedOnPlay"] = reset_seed_on_play

    # === LINE/TRAIL RENDERER ===
    if positions is not None:
        params_dict["positions"] = positions
    if position is not None:
        params_dict["position"] = position
    if index is not None:
        params_dict["index"] = index

    # Width
    if width is not None:
        params_dict["width"] = width
    if start_width is not None:
        params_dict["startWidth"] = start_width
    if end_width is not None:
        params_dict["endWidth"] = end_width
    if width_curve is not None:
        params_dict["widthCurve"] = width_curve
    if width_multiplier is not None:
        params_dict["widthMultiplier"] = width_multiplier

    # Color
    if color is not None:
        params_dict["color"] = color
    if start_color_line is not None:
        params_dict["startColor"] = start_color_line
    if end_color is not None:
        params_dict["endColor"] = end_color

    # Material & properties
    if material_path is not None:
        params_dict["materialPath"] = material_path
    if trail_material_path is not None:
        params_dict["trailMaterialPath"] = trail_material_path
    if loop is not None:
        params_dict["loop"] = loop
    if use_world_space is not None:
        params_dict["useWorldSpace"] = use_world_space
    if num_corner_vertices is not None:
        params_dict["numCornerVertices"] = num_corner_vertices
    if num_cap_vertices is not None:
        params_dict["numCapVertices"] = num_cap_vertices
    if alignment is not None:
        params_dict["alignment"] = alignment
    if texture_mode is not None:
        params_dict["textureMode"] = texture_mode
    if generate_lighting_data is not None:
        params_dict["generateLightingData"] = generate_lighting_data
    if sorting_order is not None:
        params_dict["sortingOrder"] = sorting_order
    if sorting_layer_name is not None:
        params_dict["sortingLayerName"] = sorting_layer_name
    if sorting_layer_id is not None:
        params_dict["sortingLayerID"] = sorting_layer_id
    if render_mode is not None:
        params_dict["renderMode"] = render_mode
    if sort_mode is not None:
        params_dict["sortMode"] = sort_mode

    # Renderer common properties (shadows, lighting, probes)
    if shadow_casting_mode is not None:
        params_dict["shadowCastingMode"] = shadow_casting_mode
    if receive_shadows is not None:
        params_dict["receiveShadows"] = receive_shadows
    if shadow_bias is not None:
        params_dict["shadowBias"] = shadow_bias
    if light_probe_usage is not None:
        params_dict["lightProbeUsage"] = light_probe_usage
    if reflection_probe_usage is not None:
        params_dict["reflectionProbeUsage"] = reflection_probe_usage
    if motion_vector_generation_mode is not None:
        params_dict["motionVectorGenerationMode"] = motion_vector_generation_mode
    if rendering_layer_mask is not None:
        params_dict["renderingLayerMask"] = rendering_layer_mask

    # Particle renderer specific
    if min_particle_size is not None:
        params_dict["minParticleSize"] = min_particle_size
    if max_particle_size is not None:
        params_dict["maxParticleSize"] = max_particle_size
    if length_scale is not None:
        params_dict["lengthScale"] = length_scale
    if velocity_scale is not None:
        params_dict["velocityScale"] = velocity_scale
    if camera_velocity_scale is not None:
        params_dict["cameraVelocityScale"] = camera_velocity_scale
    if normal_direction is not None:
        params_dict["normalDirection"] = normal_direction
    if pivot is not None:
        params_dict["pivot"] = pivot
    if flip is not None:
        params_dict["flip"] = flip
    if allow_roll is not None:
        params_dict["allowRoll"] = allow_roll

    # Shape creation
    if start is not None:
        params_dict["start"] = start
    if end is not None:
        params_dict["end"] = end
    if center is not None:
        params_dict["center"] = center
    if segments is not None:
        params_dict["segments"] = segments
    if normal is not None:
        params_dict["normal"] = normal
    if start_angle is not None:
        params_dict["startAngle"] = start_angle
    if end_angle is not None:
        params_dict["endAngle"] = end_angle
    if control_point1 is not None:
        params_dict["controlPoint1"] = control_point1
    if control_point2 is not None:
        params_dict["controlPoint2"] = control_point2

    # Trail specific
    if min_vertex_distance is not None:
        params_dict["minVertexDistance"] = min_vertex_distance
    if autodestruct is not None:
        params_dict["autodestruct"] = autodestruct
    if emitting is not None:
        params_dict["emitting"] = emitting

    # Velocity/size axes
    if x is not None:
        params_dict["x"] = x
    if y is not None:
        params_dict["y"] = y
    if z is not None:
        params_dict["z"] = z
    if speed_modifier is not None:
        params_dict["speedModifier"] = speed_modifier
    if space is not None:
        params_dict["space"] = space
    if separate_axes is not None:
        params_dict["separateAxes"] = separate_axes
    if size_over_lifetime is not None:
        params_dict["size"] = size_over_lifetime
    if size_x is not None:
        params_dict["sizeX"] = size_x
    if size_y is not None:
        params_dict["sizeY"] = size_y
    if size_z is not None:
        params_dict["sizeZ"] = size_z

    # Remove None values
    params_dict = {k: v for k, v in params_dict.items() if v is not None}

    # Send to Unity
    result = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "manage_vfx",
        params_dict,
    )

    return result if isinstance(result, dict) else {"success": False, "message": str(result)}
