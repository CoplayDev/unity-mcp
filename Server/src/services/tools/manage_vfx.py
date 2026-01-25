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
    description="""Manage Unity VFX components: ParticleSystem, VisualEffect, LineRenderer, TrailRenderer.

Action prefixes: particle_*, vfx_*, line_*, trail_*. Target GameObject must have corresponding component.
Common actions: *_get_info, *_play, *_stop, *_set_color, *_set_material. See C# ManageVFX.cs for full action list.""",
    annotations=ToolAnnotations(
        title="Manage VFX",
        destructiveHint=True,
    ),
)
async def manage_vfx(
    ctx: Context,
    action: Annotated[str, "Action: particle_*, vfx_*, line_*, or trail_*"],

    # Target specification (common) - REQUIRED for most actions
    # Using str | None to accept any string format
    target: Annotated[str | None,
                      "Target GameObject with the VFX component. Use name (e.g. 'Fire'), path ('Effects/Fire'), instance ID, or tag. The GameObject MUST have the required component (ParticleSystem/VisualEffect/LineRenderer/TrailRenderer) for the action prefix."] = None,
    search_method: Annotated[
        Literal["by_id", "by_name", "by_path", "by_tag", "by_layer"] | None,
        "How to find target: by_name (default), by_path (hierarchy path), by_id (instance ID - most reliable), by_tag, by_layer"
    ] = None,

    # Particle - Main
    duration: Annotated[Any, "Duration (s)"] = None,
    looping: Annotated[Any, "Loop"] = None,
    prewarm: Annotated[Any, "Prewarm"] = None,
    start_delay: Annotated[Any, "Start delay"] = None,
    start_lifetime: Annotated[Any, "Lifetime"] = None,
    start_speed: Annotated[Any, "Speed"] = None,
    start_size: Annotated[Any, "Size"] = None,
    start_rotation: Annotated[Any, "Rotation"] = None,
    start_color: Annotated[Any, "Color [r,g,b,a]"] = None,
    gravity_modifier: Annotated[Any, "Gravity"] = None,
    simulation_space: Annotated[Literal["Local", "World", "Custom"] | None, "Space"] = None,
    scaling_mode: Annotated[Literal["Hierarchy", "Local", "Shape"] | None, "Scale mode"] = None,
    play_on_awake: Annotated[Any, "Auto-play"] = None,
    max_particles: Annotated[Any, "Max particles"] = None,

    # Emission
    rate_over_time: Annotated[Any,
                              "[Particle] Emission rate over time (number or MinMaxCurve dict)"] = None,
    rate_over_distance: Annotated[Any,
                                  "[Particle] Emission rate over distance (number or MinMaxCurve dict)"] = None,

    # Shape
    shape_type: Annotated[Literal["Sphere", "Hemisphere", "Cone", "Box", "Circle", "Edge", "Donut"] | None,
                          "Shape"] = None,
    radius: Annotated[Any, "Radius"] = None,
    radius_thickness: Annotated[Any, "Thickness"] = None,
    angle: Annotated[Any, "Angle"] = None,
    arc: Annotated[Any, "Arc"] = None,

    # Noise
    strength: Annotated[Any,
                        "Noise strength (number or MinMaxCurve dict)"] = None,
    frequency: Annotated[Any,
                         "Noise frequency (number or string)"] = None,
    scroll_speed: Annotated[Any,
                            "Noise scroll speed (number or MinMaxCurve dict)"] = None,
    damping: Annotated[Any,
                       "Noise damping (bool or string)"] = None,
    octave_count: Annotated[Any,
                            "Noise octaves 1-4 (integer or string)"] = None,
    quality: Annotated[Literal["Low", "Medium", "High"]
                       | None, "Noise quality"] = None,

    # Module
    module: Annotated[str | None, "Module name"] = None,
    enabled: Annotated[Any, "Enable"] = None,

    # Burst
    time: Annotated[Any, "Burst Time or trail duration"] = None,
    count: Annotated[Any, "Burst Count"] = None,
    min_count: Annotated[Any, "Min Burst Count"] = None,
    max_count: Annotated[Any, "Max Burst Count"] = None,
    cycles: Annotated[Any, "Burst Cycles"] = None,
    interval: Annotated[Any, "Burst Interval"] = None,
    probability: Annotated[Any, "Burst Probability 0-1"] = None,
    with_children: Annotated[Any, "With children"] = None,

    # VFX
    asset_name: Annotated[str | None, "VFX asset name"] = None,
    folder_path: Annotated[str | None, "Folder path"] = None,
    template: Annotated[str | None, "Template"] = None,
    asset_path: Annotated[str | None, "Asset path to assign"] = None,
    overwrite: Annotated[Any, "Overwrite existing assets"] = None,
    folder: Annotated[str | None, "Folder to search for assets"] = None,
    search: Annotated[str | None, "Search patterns for assets"] = None,
    parameter: Annotated[str | None, "Parameter"] = None,
    value: Annotated[Any, "Value"] = None,
    texture_path: Annotated[str | None, "Texture path"] = None,
    mesh_path: Annotated[str | None, "Mesh path"] = None,
    gradient: Annotated[Any, "Gradient {colorKeys, alphaKeys} or {startColor, endColor} (dict or JSON strings)"] = None,
    curve: Annotated[Any, "Curve keys (array, dict, or JSON string)"] = None,
    event_name: Annotated[str | None, "Event"] = None,
    velocity: Annotated[Any, "Event Velocity [x,y,z]"] = None,
    size: Annotated[Any, "Size"] = None,
    lifetime: Annotated[Any, "Lifetime"] = None,
    play_rate: Annotated[Any, "Playback speed multiplier"] = None,
    seed: Annotated[Any, "Random Seed"] = None,
    reset_seed_on_play: Annotated[Any, "Reset seed on play"] = None,

    # Line/Trail
    positions: Annotated[Any, "Positions"] = None,
    position: Annotated[Any, "Position"] = None,
    index: Annotated[Any, "Index"] = None,
    width: Annotated[Any, "Width"] = None,
    start_width: Annotated[Any, "Start width"] = None,
    end_width: Annotated[Any, "End width"] = None,
    width_curve: Annotated[Any, "Width curve"] = None,
    width_multiplier: Annotated[Any, "Width mult"] = None,
    color: Annotated[Any, "Color"] = None,
    start_color_line: Annotated[Any, "Start color"] = None,
    end_color: Annotated[Any, "End color"] = None,
    material_path: Annotated[str | None, "Material path"] = None,
    trail_material_path: Annotated[str | None, "Trail mat path"] = None,
    loop: Annotated[Any, "Loop"] = None,
    use_world_space: Annotated[Any, "World space"] = None,
    num_corner_vertices: Annotated[Any, "Corner verts"] = None,
    num_cap_vertices: Annotated[Any, "Cap verts"] = None,
    alignment: Annotated[Literal["View", "Local", "TransformZ"] | None, "Alignment"] = None,
    texture_mode: Annotated[Literal["Stretch", "Tile", "DistributePerSegment", "RepeatPerSegment"] | None,
                            "Texture mode"] = None,
    generate_lighting_data: Annotated[Any, "Gen lighting"] = None,
    sorting_order: Annotated[Any, "Sort order"] = None,
    sorting_layer_name: Annotated[str | None, "Sort layer"] = None,
    sorting_layer_id: Annotated[Any, "Sort layer ID"] = None,
    render_mode: Annotated[str | None, "Render mode"] = None,
    sort_mode: Annotated[str | None, "Sort mode"] = None,

    # Renderer
    shadow_casting_mode: Annotated[Literal["Off", "On", "TwoSided", "ShadowsOnly"] | None, "Shadows"] = None,
    receive_shadows: Annotated[Any, "Receive shadows (bool or string)" ] = None,
    shadow_bias: Annotated[Any, "Shadow bias (number or string)" ] = None,
    light_probe_usage: Annotated[Literal["Off", "BlendProbes", "UseProxyVolume", "CustomProvided"] | None,
                                 "Light probes"] = None,
    reflection_probe_usage: Annotated[Literal["Off", "BlendProbes", "BlendProbesAndSkybox", "Simple"] | None,
                                      "Reflection probes"] = None,
    motion_vector_generation_mode: Annotated[Literal["Camera", "Object", "ForceNoMotion"] | None,
                                             "Motion vectors"] = None,
    rendering_layer_mask: Annotated[Any, "Render mask"] = None,
    min_particle_size: Annotated[Any, "Min particle size relative to viewport"] = None,
    max_particle_size: Annotated[Any, "Max particle size relative to viewport"] = None,
    length_scale: Annotated[Any, "Length scale for stretched billboard"] = None,
    velocity_scale: Annotated[Any, "Camera velocity scale"] = None,
    camera_velocity_scale: Annotated[Any, "Cam vel scale"] = None,
    normal_direction: Annotated[Any, "Normal direction 0-1 (number)" ] = None,
    pivot: Annotated[Any, "Pivot offset [x,y,z] (array or JSON string)"] = None,
    flip: Annotated[Any, "Flip [x,y,z] (array or JSON string)"] = None,
    allow_roll: Annotated[Any, "Allow roll for mesh particles (bool or string)"] = None,

    # Shape creation
    start: Annotated[Any, "Start point"] = None,
    end: Annotated[Any, "End point"] = None,
    center: Annotated[Any, "Circle/arc center"] = None,
    segments: Annotated[Any, "Segment count"] = None,
    normal: Annotated[Any, "Normal vector"] = None,
    start_angle: Annotated[Any, "Start angle"] = None,
    end_angle: Annotated[Any, "End angle"] = None,
    control_point1: Annotated[Any, "Bezier Control pt 1"] = None,
    control_point2: Annotated[Any, "Bezier Control pt 2"] = None,
    min_vertex_distance: Annotated[Any, "Min vertex distance"] = None,
    autodestruct: Annotated[Any, "Auto-destroy"] = None,
    emitting: Annotated[Any, "Emitting"] = None,

    # Velocity
    x: Annotated[Any, "Velocity X"] = None,
    y: Annotated[Any, "Velocity Y"] = None,
    z: Annotated[Any, "Velocity Z"] = None,
    speed_modifier: Annotated[Any, "Speed mod"] = None,
    space: Annotated[Literal["Local", "World"] | None, "Velocity Space"] = None,
    separate_axes: Annotated[Any, "Separate XYZ axes"] = None,
    size_over_lifetime: Annotated[Any, "Size over lifetime"] = None,
    size_x: Annotated[Any, "Size X"] = None,
    size_y: Annotated[Any, "Size Y"] = None,
    size_z: Annotated[Any, "Size Z"] = None,

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
