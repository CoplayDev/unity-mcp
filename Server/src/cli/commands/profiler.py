import click
from cli.utils.connection import handle_unity_errors, run_command, get_config
from cli.utils.output import format_output


@click.group("profiler")
def profiler():
    """Unity Profiler session control, counter reads, memory snapshots, and Frame Debugger."""
    pass


# --- Session ---

@profiler.command("start")
@click.option("--log-file", default=None, help="Path to .raw file for recording.")
@click.option("--callstacks", is_flag=True, default=False, help="Enable allocation callstacks.")
@handle_unity_errors
def start(log_file, callstacks):
    """Start the Unity Profiler, optionally record to a .raw file."""
    config = get_config()
    params = {"action": "profiler_start"}
    if log_file:
        params["log_file"] = log_file
    if callstacks:
        params["enable_callstacks"] = True
    result = run_command("manage_profiler", params, config)
    click.echo(format_output(result, config.format))


@profiler.command("stop")
@handle_unity_errors
def stop():
    """Stop the Unity Profiler and any active recording."""
    config = get_config()
    result = run_command("manage_profiler", {"action": "profiler_stop"}, config)
    click.echo(format_output(result, config.format))


@profiler.command("status")
@handle_unity_errors
def status():
    """Get Profiler enabled state, active areas, and recording status."""
    config = get_config()
    result = run_command("manage_profiler", {"action": "profiler_status"}, config)
    click.echo(format_output(result, config.format))


@profiler.command("set-areas")
@click.option("--area", multiple=True, help="Area=bool pairs (e.g. CPU=true Audio=false).")
@handle_unity_errors
def set_areas(area):
    """Toggle specific ProfilerAreas on or off."""
    config = get_config()
    areas = {}
    for a in area:
        name, _, val = a.partition("=")
        areas[name.strip()] = val.strip().lower() in ("true", "1", "yes")
    result = run_command("manage_profiler", {"action": "profiler_set_areas", "areas": areas}, config)
    click.echo(format_output(result, config.format))


# --- Counters ---

@profiler.command("frame-timing")
@handle_unity_errors
def frame_timing():
    """Get frame timing via FrameTimingManager (12 fields, synchronous)."""
    config = get_config()
    result = run_command("manage_profiler", {"action": "get_frame_timing"}, config)
    click.echo(format_output(result, config.format))


@profiler.command("get-counters")
@click.option("--category", required=True, help="Profiler category (e.g. Render, Scripts, Memory).")
@click.option("--counter", multiple=True, help="Specific counter names. Omit to read all in category.")
@handle_unity_errors
def get_counters(category, counter):
    """Read profiler counters by category (async, 1-frame wait)."""
    config = get_config()
    params = {"action": "get_counters", "category": category}
    if counter:
        params["counters"] = list(counter)
    result = run_command("manage_profiler", params, config)
    click.echo(format_output(result, config.format))


@profiler.command("object-memory")
@click.option("--path", required=True, help="Scene hierarchy or asset path.")
@handle_unity_errors
def object_memory(path):
    """Get native memory size of a specific Unity object."""
    config = get_config()
    result = run_command("manage_profiler", {"action": "get_object_memory", "object_path": path}, config)
    click.echo(format_output(result, config.format))


# --- Memory Snapshot ---

@profiler.command("memory-snapshot")
@click.option("--path", default=None, help="Output .snap file path (default: auto-generated).")
@handle_unity_errors
def memory_snapshot(path):
    """Take a memory snapshot (requires com.unity.memoryprofiler)."""
    config = get_config()
    params = {"action": "memory_take_snapshot"}
    if path:
        params["snapshot_path"] = path
    result = run_command("manage_profiler", params, config)
    click.echo(format_output(result, config.format))


@profiler.command("memory-list")
@click.option("--search-path", default=None, help="Directory to search for snapshots.")
@handle_unity_errors
def memory_list(search_path):
    """List available memory snapshot files."""
    config = get_config()
    params = {"action": "memory_list_snapshots"}
    if search_path:
        params["search_path"] = search_path
    result = run_command("manage_profiler", params, config)
    click.echo(format_output(result, config.format))


@profiler.command("memory-compare")
@click.option("--a", "snapshot_a", required=True, help="First snapshot path.")
@click.option("--b", "snapshot_b", required=True, help="Second snapshot path.")
@handle_unity_errors
def memory_compare(snapshot_a, snapshot_b):
    """Compare two memory snapshots."""
    config = get_config()
    result = run_command("manage_profiler", {
        "action": "memory_compare_snapshots",
        "snapshot_a": snapshot_a, "snapshot_b": snapshot_b,
    }, config)
    click.echo(format_output(result, config.format))


# --- Frame Debugger ---

@profiler.command("frame-debugger-enable")
@handle_unity_errors
def frame_debugger_enable():
    """Enable the Frame Debugger and report event count."""
    config = get_config()
    result = run_command("manage_profiler", {"action": "frame_debugger_enable"}, config)
    click.echo(format_output(result, config.format))


@profiler.command("frame-debugger-disable")
@handle_unity_errors
def frame_debugger_disable():
    """Disable the Frame Debugger."""
    config = get_config()
    result = run_command("manage_profiler", {"action": "frame_debugger_disable"}, config)
    click.echo(format_output(result, config.format))


@profiler.command("frame-debugger-events")
@click.option("--page-size", default=50, help="Events per page (default 50).")
@click.option("--cursor", default=None, type=int, help="Cursor offset.")
@handle_unity_errors
def frame_debugger_events(page_size, cursor):
    """Get Frame Debugger draw call events (paged)."""
    config = get_config()
    params = {"action": "frame_debugger_get_events", "page_size": page_size}
    if cursor is not None:
        params["cursor"] = cursor
    result = run_command("manage_profiler", params, config)
    click.echo(format_output(result, config.format))
